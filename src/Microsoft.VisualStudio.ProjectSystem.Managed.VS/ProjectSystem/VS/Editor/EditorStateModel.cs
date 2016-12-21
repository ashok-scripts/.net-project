﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.VS.UI;
using Microsoft.VisualStudio.ProjectSystem.VS.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Editor
{
    /// <summary>
    /// This class maintains the state machine that controls the in-memory project file editor. It manages
    /// the subscriptions to project events and the current state of the buffer.
    /// </summary>
    [Export]
    internal class EditorStateModel
    {
        /// <summary>
        /// The different states that the editor can be in.
        /// </summary>
        private enum EditorState
        {
            /// <summary>
            /// There is no open editor. No listeners are subscribed to project events.
            /// Allowed Transitions: <see cref="Initializing"/> when the user opens the editor.
            /// </summary>
            NoEditor,
            /// <summary>
            /// The editor is being initialized.
            /// Allowed Transitioins: <see cref="NoUnsavedChanges"/> once initialization is completed.
            /// </summary>
            Initializing,
            /// <summary>
            /// There is an open editor, and it has no unsaved changes.
            /// Allowed Transitions:
            /// * <see cref="UnsavedChanges"/> if the user makes an edit.
            /// * <see cref="BufferUpdateScheduledFromClean"/> if the user makes a change from the UI, ushc as adding a NuGet package.
            /// * <see cref="EditorClosing"/> if the user closes the buffer.
            /// </summary>
            NoUnsavedChanges,
            /// <summary>
            /// There is an open editor, and it has one or more unsaved changes.
            /// Allowed Transitions:
            /// * <see cref="WritingProjectFile"/> if the user presses save.
            /// * <see cref="BufferUpdateScheduledFromDirty"/> if the user makes a change from the UI, such as adding a NuGet package.
            /// * <see cref="NoUnsavedChanges"/> if the user undoes their edits.
            /// </summary>
            UnsavedChanges,
            /// <summary>
            /// The changes from the open editor are being written to disk.
            /// Allowed Transitions: <see cref="NoUnsavedChanges"/> once the updated file is written out.
            /// </summary>
            WritingProjectFile,
            /// <summary>
            /// A buffe update has been scheduled. Before the update began, the state was <see cref="NoUnsavedChanges"/>.
            /// Allowed Transitions: <see cref="BufferChangedFromClean"/> when the editor update is run.
            /// </summary>
            BufferUpdateScheduledFromClean,
            /// <summary>
            /// A buffer update has been scheduled. Before the update began, the state was <see cref="UnsavedChanges"/>.
            /// Allowed Transitions: <see cref="BufferChangingFromDirty"/> when the editor update is run.
            /// </summary>
            BufferUpdateScheduledFromDirty,
            /// <summary>
            /// The project is being changed programmatically. The open editor was originally in a clean state.
            /// Allowed Transitions: <see cref="NoUnsavedChanges"/> when the editor is updated.
            /// </summary>
            BufferChangedFromClean,
            /// <summary>
            /// The project is being changed programmatically. The open editor was originally in a dirty state.
            /// Allowed Transitions:
            /// * <see cref="UnsavedChanges"/> if the user cancels the update.
            /// * <see cref="BufferChangedFromClean"/> if the user discards changes.
            /// </summary>
            BufferChangingFromDirty,
            /// <summary>
            /// The editor is being closed and states being cleaned up. It should not be possible to reach this from dirty states, as
            /// the editor should be saved before then.
            /// Allowed Transitions: <see cref="NoEditor"/> when editor shutdown is complete.
            /// </summary>
            EditorClosing
        }

        private readonly object _lock = new object();
        private readonly IProjectThreadingService _threadingService;
        private readonly UnconfiguredProject _unconfiguredProject;
        private readonly IServiceProvider _serviceProvider;
        private readonly IVsShellUtilitiesHelper _shellHelper;
        private readonly ExportFactory<IProjectFileModelWatcher> _projectFileWatcherFactory;
        private readonly ExportFactory<ITextBufferStateListener> _textBufferListenerFactory;
        private readonly ExportFactory<IFrameOpenCloseListener> _frameEventsListenerFactory;
        private readonly ITextBufferManager _textBufferManager;
        private readonly IDialogServices _dialogServices;

        private EditorState _currentState = EditorState.NoEditor;
        private IVsWindowFrame _windowFrame;
        private IProjectFileModelWatcher _projectFileModelWatcher;
        private IFrameOpenCloseListener _frameEventsListener;
        private ITextBufferStateListener _textBufferStateListener;

        [ImportingConstructor]
        public EditorStateModel(IProjectThreadingService threadingService,
            UnconfiguredProject unconfiguredProject,
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            IVsShellUtilitiesHelper shellHelper,
            ExportFactory<IProjectFileModelWatcher> projectFileModelWatcherFactory,
            ExportFactory<ITextBufferStateListener> textBufferListenerFactory,
            ExportFactory<IFrameOpenCloseListener> frameEventsListenerFactory,
            ITextBufferManager textBufferManager,
            IDialogServices dialogServices)
        {
            _threadingService = threadingService;
            _unconfiguredProject = unconfiguredProject;
            _serviceProvider = serviceProvider;
            _shellHelper = shellHelper;
            _projectFileWatcherFactory = projectFileModelWatcherFactory;
            _textBufferListenerFactory = textBufferListenerFactory;
            _textBufferManager = textBufferManager;
            _frameEventsListenerFactory = frameEventsListenerFactory;
            _dialogServices = dialogServices;
        }

        #region Initialization/Destruction

        /// <summary>
        /// Called by anything attempting to open a project file editor window, usually by a command. This will show the window frame, creating it if
        /// has not already been created.
        /// </summary>
        public async Task OpenEditorAsync()
        {
            await _threadingService.SwitchToUIThread();
            lock (_lock)
            {
                // If the editor is already open, just show it and return
                if (_currentState != EditorState.NoEditor)
                {
                    // If we're initializing, _windowFrame might be null. In that case, when the initialization code
                    // is done, it'll take care of showing the frame.
                    _windowFrame?.Show();
                    return;
                }
                _currentState = EditorState.Initializing;
            }

            // First, open the frame. This performs a long set of callbacks that will set _windowPane when the actual pane is created.
            var loadedProjectEditorGuid = Guid.Parse(LoadedProjectFileEditorFactory.EditorFactoryGuid);
            _shellHelper.OpenDocumentWithSpecificEditor(_serviceProvider, _unconfiguredProject.FullPath, loadedProjectEditorGuid, Guid.Empty);

            // Finally, show the frame and move to EditorClean in lockstep
            lock (_lock)
            {
                Assumes.True(_currentState == EditorState.NoUnsavedChanges);
                Assumes.NotNull(_windowFrame);
                _windowFrame.Show();
            }
        }

        /// <summary>
        /// Called from the editor wrapper. This sets up the listener that controls the dirty state and saving commands, and the listener
        /// that watches for frame creation and destruction events.
        /// </summary>
        public async Task InitializeWindowPaneAsync(WindowPane hostPane)
        {
            // If we were invoked from the Edit Project File command, we'll already have been set to Initializing. Otherwise, we might
            // be invoked from someone opening the file in the editor (via open with, or the project file being reopened as the project
            // is reopened). This method can only be called from either the NoEditor or Initializing states.
            lock (_lock)
            {
                Assumes.True(_currentState == EditorState.NoEditor || _currentState == EditorState.Initializing);
                _currentState = EditorState.Initializing;
            }

            _textBufferStateListener = _textBufferListenerFactory.CreateExport().Value;
            await _textBufferStateListener.InitializeAsync(hostPane).ConfigureAwait(false);

            _frameEventsListener = _frameEventsListenerFactory.CreateExport().Value;
            await _frameEventsListener.InitializeEventsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Called from the frame events listener. This will only be called when the IVsWindowFrame hosting the editor has been created, during
        /// initialization. At this point, we're done initialization.
        /// </summary>
        public void InitializeWindowFrame(IVsWindowFrame frame)
        {
            lock (_lock)
            {
                Assumes.True(_currentState == EditorState.Initializing);
            }

            _windowFrame = frame;

            // Set up the project file watcher, so changes to the project file are detected and the buffer is updated.
            _projectFileModelWatcher = _projectFileWatcherFactory.CreateExport().Value;
            _projectFileModelWatcher.Initialize();

            lock (_lock)
            {
                _currentState = EditorState.NoUnsavedChanges;
            }
        }

        /// <summary>
        /// Notifies the editor that the window may be closing. It is possible for the window close process to be stopped by this method, or for
        /// another method to cancel the close action. If another method cancels the close, there is no notification that the close was cancelled.
        /// Therefore, we don't adjust the state.
        /// </summary>
        /// <returns>Whether or not to cancel the window close. True if the window close should continue, false if the window close should be cancelled.</returns>
        public async Task<bool> NotifyWindowMaybeClosingAsync()
        {
            lock (_lock)
            {
                // If there are no unsaved changes, we just return immediately.
                if (_currentState != EditorState.UnsavedChanges)
                {
                    return true;
                }
            }

            await _threadingService.SwitchToUIThread();

            var buttons = new string[]
            {
                VSResources.Cancel,
                VSResources.No,
                VSResources.Yes,
            };
            var msgText = string.Format(VSResources.ProjectFileBufferClosing, _unconfiguredProject.FullPath);

            await _threadingService.SwitchToUIThread();
            switch (_dialogServices.ShowMultiChoiceMsgBox(VSResources.ProjectFileClosingTitle, msgText, buttons))
            {
                case MultiChoiceMsgBoxResult.Cancel:
                    // The user cancelled, which is the same as button 1.
                    goto case MultiChoiceMsgBoxResult.Button1;
                case MultiChoiceMsgBoxResult.Button1:
                    // Cancel the window close result.
                    return false;
                case MultiChoiceMsgBoxResult.Button2:
                    // The user doesn't want to save their changes, to just return true
                    return true;
                case MultiChoiceMsgBoxResult.Button3:
                    // Save the file, then return true. We don't need to return to the UI thread here.
                    await SaveProjectFileAsync().ConfigureAwait(false);
                    lock (_lock)
                    {
                        _currentState = EditorState.NoUnsavedChanges;
                    }
                    return true;
                default:
                    Assumes.NotReachable();
                    return false;
            }
        }

        /// <summary>
        /// Starts the process of closing the project file editor, if one is open currently.
        /// </summary>
        public async Task CloseWindowAsync()
        {
            lock (_lock)
            {
                if (_currentState == EditorState.NoEditor) return;

                // Checking for potential dirty state and asking if the user wants to save their changes will have already occurred at this point.
                // Just go to EditorClosing.
                _currentState = EditorState.EditorClosing;
            }

            _projectFileModelWatcher?.Dispose();
            _textBufferStateListener?.Dispose();
            if (_frameEventsListener != null)
            {
                await _frameEventsListener.DisposeAsync().ConfigureAwait(false);
            }

            _projectFileModelWatcher = null;
            _frameEventsListener = null;
            _textBufferStateListener = null;

            lock (_lock)
            {
                _currentState = EditorState.NoEditor;
            }
            return;
        }

        #endregion

        #region Update Project File

        /// <summary>
        /// Schedules the project file to be updated. This is called by the ProjectFileModelWatcher, which is within a Project write lock.
        /// Because of the time-sensitive nature of that lock, instead of updating the file immediately we schedule an update as soon
        /// as the JTF can run our code.
        /// </summary>
        public void ScheduleProjectFileUpdate()
        {
            lock (_lock)
            {
                // If the current state is writing project file, we don't want to update now, as the project will not have been fully
                // reloaded yet. We'll get called back again after the ProjectReloadManager is finished reloading the project
                if (_currentState == EditorState.WritingProjectFile) return;
                _currentState = _currentState == EditorState.UnsavedChanges ?
                    EditorState.BufferUpdateScheduledFromDirty : EditorState.BufferUpdateScheduledFromClean;
                _threadingService.JoinableTaskFactory.RunAsync(UpdateProjectFileAsync);
            }
        }

        /// <summary>
        /// Updates the content of the project file to be the latest msbuild text.
        /// </summary>
        /// <returns></returns>
        private async Task UpdateProjectFileAsync()
        {
            // We do this on the UI thread to ensure that between setting the state to a variant of ProjectFileChanging and setting the window
            // frame to readonly, the user can't do any input (and potentially cause a conflict).
            await _threadingService.SwitchToUIThread();
            lock (_lock)
            {
                // Only update the project file if we have scheduled an update. Any other state is either already updating the buffer,
                // or in the processes of closing the buffer.
                if (_currentState != EditorState.BufferUpdateScheduledFromClean
                    && _currentState != EditorState.BufferUpdateScheduledFromDirty)
                {
                    return;
                }

                _currentState = _currentState == EditorState.BufferUpdateScheduledFromDirty ?
                    EditorState.BufferChangingFromDirty : EditorState.BufferChangedFromClean;
            }

            // Set set the buffer to be unmodifiable to prevent any changes while we update the project
            await _textBufferManager.SetReadOnlyAsync(true).ConfigureAwait(true);

            // TODO: Handle dirty state

            // Set the buffer to default state and set the current state to editor clean
            _textBufferManager.ResetBuffer();
            await _textBufferStateListener.ForceBufferStateCleanAsync().ConfigureAwait(true);
            lock (_lock)
            {
                _currentState = EditorState.NoUnsavedChanges;
            }
            await _textBufferManager.SetReadOnlyAsync(false).ConfigureAwait(true);
        }

        #endregion

        /// <summary>
        /// Causes the dirty changes to the editor to be saved to the disk.
        /// </summary>
        public async Task SaveProjectFileAsync()
        {
            lock (_lock)
            {
                // In order to save, the editor must be in the dirty state.
                if (_currentState != EditorState.UnsavedChanges) return;
                _currentState = EditorState.WritingProjectFile;
            }

            // While saving the file, we disallow edits to the project file for sync purposes.
            await _textBufferManager.SetReadOnlyAsync(true).ConfigureAwait(false);
            await _textBufferStateListener.SaveAsync().ConfigureAwait(false);

            lock (_lock)
            {
                _currentState = EditorState.NoUnsavedChanges;
            }
            await _textBufferManager.SetReadOnlyAsync(false).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the current editor state to either dirty or clean.
        /// </summary>
        public void SetEditorDirty(bool isDirty)
        {
            lock (_lock)
            {
                // If the current state isn't clean or dirty, some other change is happening, and we should not touch the current state
                if (_currentState != EditorState.NoUnsavedChanges && _currentState != EditorState.UnsavedChanges) return;

                _currentState = isDirty ? EditorState.UnsavedChanges : EditorState.NoUnsavedChanges;
            }
        }
    }
}
