﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using NuGet.SolutionRestoreManager;
using Microsoft.VisualStudio.ProjectSystem.PackageRestore;

namespace Microsoft.VisualStudio.ProjectSystem.VS.PackageRestore;

/// <summary>
///     Wraps a <see cref="ProjectRestoreInfo"/> instance to implement the <see cref="IVsProjectRestoreInfo2"/>
///     interface for NuGet.
/// </summary>
internal class ProjectRestoreInfoWrapper : IVsProjectRestoreInfo2
{
    private readonly ProjectRestoreInfo _info;
    
    private TargetFrameworksWrapper? _targetFrameworks;
    private ReferenceItemsWrapper? _toolReferences;

    public ProjectRestoreInfoWrapper(ProjectRestoreInfo info)
    {
        _info = info;
    }

    public string BaseIntermediatePath => _info.MSBuildProjectExtensionsPath;

    public IVsTargetFrameworks2 TargetFrameworks => _targetFrameworks ??= new TargetFrameworksWrapper(_info.TargetFrameworks);

    public IVsReferenceItems ToolReferences => _toolReferences ??= new ReferenceItemsWrapper(_info.ToolReferences);

    public string OriginalTargetFrameworks => _info.OriginalTargetFrameworks;
}