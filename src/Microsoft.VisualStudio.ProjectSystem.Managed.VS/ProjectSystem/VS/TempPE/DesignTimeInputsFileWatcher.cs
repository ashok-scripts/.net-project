// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks.Dataflow;

using Microsoft.VisualStudio.ProjectSystem.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.ProjectSystem.VS.TempPE
{
    /// <summary>
    /// Produces output whenever a design time input changes
    /// </summary>
    [Export(typeof(IDesignTimeInputsFileWatcher))]
    [AppliesTo(ProjectCapability.CSharpOrVisualBasicLanguageService)]
    internal class DesignTimeInputsFileWatcher : ProjectValueDataSourceBase<string>, IVsFreeThreadedFileChangeEvents2, IDesignTimeInputsFileWatcher
    {
        private readonly IProjectThreadingService _threadingService;
        private readonly IDesignTimeInputsDataSource _designTimeInputsDataSource;
        private readonly IVsService<IVsAsyncFileChangeEx> _fileChangeService;
        private ImmutableDictionary<string, uint> _fileWatcherCookies = ImmutableDictionary<string, uint>.Empty.WithComparers(StringComparers.Paths);

        private int _version;
        private readonly DisposableBag _disposables = new DisposableBag();

        /// <summary>
        /// The block that receives updates from the active tree provider.
        /// </summary>
        private IBroadcastBlock<IProjectVersionedValue<string>>? _broadcastBlock;

        /// <summary>
        /// The public facade for the broadcast block.
        /// </summary>
        private IReceivableSourceBlock<IProjectVersionedValue<string>>? _publicBlock;

        [ImportingConstructor]
        public DesignTimeInputsFileWatcher(ConfiguredProject project,
                                           IDesignTimeInputsDataSource designTimeInputsDataSource,
                                           IVsService<SVsFileChangeEx, IVsAsyncFileChangeEx> fileChangeService)
             : base(project.Services, synchronousDisposal: true, registerDataSource: false)
        {
            _threadingService = project.Services.ThreadingPolicy;
            _designTimeInputsDataSource = designTimeInputsDataSource;
            _fileChangeService = fileChangeService;
        }

        public override NamedIdentity DataSourceKey { get; } = new NamedIdentity(nameof(DesignTimeInputsFileWatcher));

        public override IComparable DataSourceVersion => _version;

        public override IReceivableSourceBlock<IProjectVersionedValue<string>> SourceBlock
        {
            get
            {
                EnsureInitialized();

                return _publicBlock!;
            }
        }

        protected override void Initialize()
        {
            base.Initialize();

            IDisposable dataSourceLink = _designTimeInputsDataSource.SourceBlock.LinkToAsyncAction(ProcessDesignTimeInputs);

            IDisposable join = JoinUpstreamDataSources(_designTimeInputsDataSource);

            _disposables.AddDisposable(dataSourceLink);
            _disposables.AddDisposable(join);

            _broadcastBlock = DataflowBlockSlim.CreateBroadcastBlock<IProjectVersionedValue<string>>(nameFormat: nameof(DesignTimeInputsFileWatcher) + "Broadcast {1}");
            _publicBlock = _broadcastBlock.SafePublicize();
        }

        private async Task ProcessDesignTimeInputs(IProjectVersionedValue<DesignTimeInputs> input)
        {
            // We don't want to watch any new files while we're disposing
            if (IsDisposing)
            {
                return;
            }

            DesignTimeInputs designTimeInputs = input.Value;

            IVsAsyncFileChangeEx vsAsyncFileChangeEx = await _fileChangeService.GetValueAsync();

            // we don't care about the difference between types of inputs, so we just construct one hashset for fast comparisons later
            var allFiles = new HashSet<string>(StringComparers.Paths);
            allFiles.AddRange(designTimeInputs.Inputs);
            allFiles.AddRange(designTimeInputs.SharedInputs);

            // Remove any files we're watching that we don't care about any more
            foreach (string file in _fileWatcherCookies.Keys.ToArray())
            {
                if (!allFiles.Contains(file))
                {
                    ImmutableInterlocked.TryRemove(ref _fileWatcherCookies, file, out uint cookie);
                    await vsAsyncFileChangeEx.UnadviseFileChangeAsync(cookie);
                }
            }

            // Now watch and output files that are new
            foreach (string file in allFiles)
            {
                if (!_fileWatcherCookies.ContainsKey(file))
                {
                    // We don't care about delete and add here, as they come through data flow, plus they are really bouncy - every file change is a Time, Del and Add event)
                    uint cookie = await vsAsyncFileChangeEx.AdviseFileChangeAsync(file, _VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size, sink: this);

                    ImmutableInterlocked.TryAdd(ref _fileWatcherCookies, file, cookie);

                    // Advise of an addition now
                    PostToOutput(file);
                }
            }
        }

        private void PostToOutput(string file)
        {
            _version++;
            ImmutableDictionary<NamedIdentity, IComparable> dataSources = ImmutableDictionary<NamedIdentity, IComparable>.Empty.Add(DataSourceKey, DataSourceVersion);

            _broadcastBlock.Post(new ProjectVersionedValue<string>(file, dataSources));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _broadcastBlock?.Complete();
                _disposables.Dispose();

                _threadingService.ExecuteSynchronously(async () =>
                {
                    IVsAsyncFileChangeEx vsAsyncFileChangeEx = await _fileChangeService.GetValueAsync();

                    foreach (uint cookie in _fileWatcherCookies.Values)
                    {
                        await vsAsyncFileChangeEx.UnadviseFileChangeAsync(cookie);
                    }
                });
            }

            base.Dispose(disposing);
        }

        public int FilesChanged(uint cChanges, string[] rgpszFile, uint[] rggrfChange)
        {
            for (int i = 0; i < cChanges; i++)
            {
                PostToOutput(rgpszFile[i]);
            }

            return HResult.OK;
        }

        public int DirectoryChanged(string pszDirectory)
        {
            return HResult.NotImplemented;
        }

        public int DirectoryChangedEx(string pszDirectory, string pszFile)
        {
            return HResult.NotImplemented;
        }

        public int DirectoryChangedEx2(string pszDirectory, uint cChanges, string[] rgpszFile, uint[] rggrfChange)
        {
            return HResult.NotImplemented;
        }
    }
}
