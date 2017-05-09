﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Logging;

namespace Microsoft.VisualStudio.ProjectSystem.LanguageServices.Handlers
{
    /// <summary>
    ///     Handles changes to the  &lt;Analyzer/&gt; item during design-time builds.
    /// </summary>
    [Export(typeof(ILanguageServiceCommandLineHandler))]
    [AppliesTo(ProjectCapability.CSharpOrVisualBasicOrFSharpLanguageService)]
    internal class AnalyzerItemHandler : ILanguageServiceCommandLineHandler
    {
        [ImportingConstructor]
        public AnalyzerItemHandler(UnconfiguredProject project)
        {
        }

        public void Handle(BuildOptions added, BuildOptions removed, IWorkspaceProjectContext context, bool isActiveContext, ProjectLoggerContext loggerContext)
        {
            Requires.NotNull(added, nameof(added));
            Requires.NotNull(removed, nameof(removed));

            foreach (CommandLineAnalyzerReference analyzer in removed.AnalyzerReferences)
            {
                loggerContext.WriteLine("Removing analyzer {0}", analyzer.FilePath);

                context.RemoveAnalyzerReference(analyzer.FilePath);
            }

            foreach (CommandLineAnalyzerReference analyzer in added.AnalyzerReferences)
            {
                loggerContext.WriteLine("Adding analyzer {0}", analyzer.FilePath);

                context.AddAnalyzerReference(analyzer.FilePath);
            }
        }
    }
}
