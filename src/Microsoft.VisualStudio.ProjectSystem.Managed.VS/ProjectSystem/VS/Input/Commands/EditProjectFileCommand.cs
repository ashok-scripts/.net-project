﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Packaging;
using Microsoft.VisualStudio.ProjectSystem.Input;
using Microsoft.VisualStudio.ProjectSystem.VS.Editor;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Input.Commands
{
    [ProjectCommand(ManagedProjectSystemPackage.ManagedProjectSystemCommandSet, ManagedProjectSystemPackage.EditProjectFileCmdId)]
    [AppliesTo(ProjectCapability.OpenProjectFile)]
    internal class EditProjectFileCommand : AbstractSingleNodeProjectCommand
    {
        private readonly UnconfiguredProject _unconfiguredProject;
        private readonly EditorStateModel _editorState;

        [ImportingConstructor]
        public EditProjectFileCommand(UnconfiguredProject unconfiguredProject, EditorStateModel editorState)
        {
            _unconfiguredProject = unconfiguredProject;
            _editorState = editorState;
        }

        protected override Task<CommandStatusResult> GetCommandStatusAsync(IProjectTree node, bool focused, string commandText, CommandStatus progressiveStatus) =>
            ShouldHandle(node) ?
                GetCommandStatusResult.Handled(GetCommandText(node), CommandStatus.Enabled) :
                GetCommandStatusResult.Unhandled;

        protected override async Task<bool> TryHandleCommandAsync(IProjectTree node, bool focused, Int64 commandExecuteOptions, IntPtr variantArgIn, IntPtr variantArgOut)
        {
            if (!ShouldHandle(node)) return false;
            await _editorState.OpenEditorAsync().ConfigureAwait(false);
            return true;
        }

        protected string GetCommandText(IProjectTree node)
        {
            return string.Format(VSResources.EditProjectFileCommand, Path.GetFileName(_unconfiguredProject.FullPath));
        }

        private bool ShouldHandle(IProjectTree node) => node.IsRoot();
    }
}
