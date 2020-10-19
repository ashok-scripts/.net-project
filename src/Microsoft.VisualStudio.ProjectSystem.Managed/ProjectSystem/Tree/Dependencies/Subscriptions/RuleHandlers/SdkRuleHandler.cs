﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Immutable;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.CrossTarget;
using Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.Models;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies;

namespace Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies.Subscriptions.RuleHandlers
{
    [Export(DependencyRulesSubscriber.DependencyRulesSubscriberContract,
            typeof(IDependenciesRuleHandler))]
    [Export(typeof(IProjectDependenciesSubTreeProvider))]
    [AppliesTo(ProjectCapability.DependenciesTree)]
    internal class SdkRuleHandler : DependenciesRuleHandlerBase
    {
        public const string ProviderTypeString = "Sdk";

        private static readonly DependencyGroupModel s_groupModel = new(
            ProviderTypeString,
            Resources.SdkNodeName,
            new DependencyIconSet(
                icon: KnownMonikers.SDK,
                expandedIcon: KnownMonikers.SDK,
                unresolvedIcon: KnownMonikers.SDKWarning,
                unresolvedExpandedIcon: KnownMonikers.SDKWarning),
            DependencyTreeFlags.SdkDependencyGroup);

        public SdkRuleHandler()
            : base(SdkReference.SchemaName, ResolvedSdkReference.SchemaName)
        {
        }

        public override string ProviderType => ProviderTypeString;

        public override ImageMoniker ImplicitIcon => KnownMonikers.SDKPrivate;

        public override IDependencyModel CreateRootDependencyNode() => s_groupModel;

        protected override IDependencyModel CreateDependencyModel(
            string path,
            string originalItemSpec,
            bool resolved,
            bool isImplicit,
            IImmutableDictionary<string, string> properties)
        {
            // Note that an implicit SDK is always created as unresolved. It will be resolved
            // later when SdkAndPackagesDependenciesSnapshotFilter observes their corresponding
            // package.

            return new SdkDependencyModel(
                path,
                originalItemSpec,
                resolved && !isImplicit,
                isImplicit,
                properties);
        }
    }
}
