﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using NuGet.SolutionRestoreManager;
using Microsoft.VisualStudio.ProjectSystem.PackageRestore;

namespace Microsoft.VisualStudio.ProjectSystem.VS.PackageRestore;

/// <summary>
///     Wraps a collection of <see cref="ReferenceProperty"/> instances to implement the <see cref="IVsReferenceProperties"/>
///     interface for NuGet.
/// </summary>
internal class ReferencePropertiesWrapper : ImmutablePropertyCollection<IVsReferenceProperty, ReferenceProperty>, IVsReferenceProperties
{
    public ReferencePropertiesWrapper(ImmutableList<ReferenceProperty> properties)
        : base(properties, item => item.Name, item => new ReferencePropertyWrapper(item))
    {
    }
}