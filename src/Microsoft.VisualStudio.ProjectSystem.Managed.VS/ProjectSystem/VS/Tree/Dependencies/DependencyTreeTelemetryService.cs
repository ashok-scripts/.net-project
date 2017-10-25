﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.VisualStudio.ProjectSystem.LanguageServices;
using Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies.CrossTarget;
using Microsoft.VisualStudio.Telemetry;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Dependencies
{
    /// <summary>
    /// Model for creating telemetry events when dependency tree is updated.
    /// It maintains some light state for each Target Framework to keep track
    /// of the status of Rule processing so we can heuristically determine
    /// when the dependency tree is "ready"
    /// </summary>
    [Export(typeof(IDependencyTreeTelemetryService))]
    [AppliesTo(ProjectCapability.DependenciesTree)]
    internal class DependencyTreeTelemetryService : IDependencyTreeTelemetryService
    {
        private const string TelemetryEventName = "TreeUpdated";
        private const string UnresolvedLabel = "Unresolved";
        private const string ResolvedLabel = "Resolved";
        private const string ProjectProperty = "Project";

        /// <summary>
        /// Maintain state for each target framework
        /// </summary>
        internal class TelemetryState
        {
            public ConcurrentDictionary<string, bool> ObservedRuleChanges { get; } = new ConcurrentDictionary<string, bool>(StringComparers.RuleNames);
            public ConcurrentDictionary<string, bool> ObservedHandlerDesignTime { get; } = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            public bool StopTelemetry { get; set; }

            public bool IsReadyToResolve() => !ObservedRuleChanges.IsEmpty && ObservedRuleChanges.All(entry => entry.Value)
                && !ObservedHandlerDesignTime.IsEmpty && ObservedHandlerDesignTime.All(entry => entry.Value);

            public void UpdateObservedRuleChanges(string rule, bool hasChanges = true)
            {
                ObservedRuleChanges.AddOrUpdate(
                    rule,
                    hasChanges,
                    (key, anyChanges) => anyChanges || hasChanges);
            }

            public void UpdateObservedHandlerDesignTime(string handlerName, bool isDesignTime)
            {
                ObservedHandlerDesignTime.AddOrUpdate(
                    handlerName,
                    isDesignTime,
                    (key, anyDesignTime) => anyDesignTime || isDesignTime);
            }
        }

        private readonly UnconfiguredProject _project;
        private readonly ITelemetryService _telemetryService;
        private readonly ConcurrentDictionary<ITargetFramework, TelemetryState> _telemetryStates = 
            new ConcurrentDictionary<ITargetFramework, TelemetryState>();
        private readonly object _stateUpdateLock = new object();
        private string _projectId;
        private bool _stopTelemetry = false;
        private bool _isReadyToResolve = false;

        [ImportingConstructor]
        public DependencyTreeTelemetryService(
            UnconfiguredProject project,
            ITelemetryService telemetryService)
        {
            _project = project;
            _telemetryService = telemetryService;
        }

        /// <summary>
        /// Fire a telemetry event when the dependency tree update is complete, 
        /// conditioned on three heuristics:
        ///  1. Telemetry is fired with 'Unresolved' label if any target frameworks are
        ///     yet to process Rules from a Design Time build. This is tracked by 
        ///     TelemetryState.ObservedDesignTime
        ///  2. Telemetry is fired with 'Unresolved' label if any relevant Rules from design
        ///     time builds are yet to report any changes. Relevant Rules are determined
        ///     based on the 'ICrossTargetRuleHandler's that have processed. This is tracked
        ///     by TelemetryState.ObservedRuleChanges
        ///  3. Telemetry fires with 'Resolved' label when 1. and 2. are no longer true
        ///  4. Telemetry stops firing once all target frameworks observe an Evaluation following
        ///     the first design time build, or once it has fired once with 'Resolved' label. 
        ///     This prevents telemetry events caused by project changes and tree updates after initial load has completed.
        /// </summary>
        public void ObserveTreeUpdateCompleted()
        {
            lock (_stateUpdateLock)
            {
                if (_stopTelemetry) return;

                _stopTelemetry = _isReadyToResolve || IsStopTelemetryInAllTargetFrameworks();
            }

            if (_projectId == null)
            {
                InitializeProjectId();
            }

            _telemetryService.PostProperty($"{TelemetryEventName}/{(_isReadyToResolve ? ResolvedLabel : UnresolvedLabel)}",
                ProjectProperty, _projectId);
        }

        public void ObserveUnresolvedRules(ITargetFramework targetFramework, IEnumerable<string> rules)
        {
            lock (_stateUpdateLock)
            {
                if (_stopTelemetry) return;

                var telemetryState = _telemetryStates.GetOrAdd(targetFramework, (key) => new TelemetryState());

                foreach (var rule in rules)
                {
                    // observe all unresolved rules by default ignoring whether they have actual changes
                    telemetryState.UpdateObservedRuleChanges(rule);
                }
            }
        }

        public void ObserveHandlerRulesChanges(
            ITargetFramework targetFramework,
            string handlerName,
            IEnumerable<string> handlerRules, 
            RuleHandlerType handlerType, 
            IImmutableDictionary<string, IProjectChangeDescription> projectChanges)
        {
            lock (_stateUpdateLock)
            {
                if (_stopTelemetry) return;

                if (_telemetryStates.TryGetValue(targetFramework, out var telemetryState))
                {
                    foreach (var rule in handlerRules)
                    {
                        var hasChanges = projectChanges.TryGetValue(rule, out var description)
                            && description.Difference.AnyChanges;
                        telemetryState.UpdateObservedRuleChanges(rule, hasChanges);
                    }

                    telemetryState.UpdateObservedHandlerDesignTime(handlerName, handlerType == RuleHandlerType.DesignTimeBuild);
                }
            }
        }

        public void ObserveCompleteHandlers(ITargetFramework targetFramework, RuleHandlerType handlerType)
        {
            lock (_stateUpdateLock)
            {
                if (_stopTelemetry) return;

                if (_telemetryStates.TryGetValue(targetFramework, out var telemetryState))
                {
                    telemetryState.StopTelemetry |= telemetryState.ObservedHandlerDesignTime.Any()
                        && telemetryState.ObservedHandlerDesignTime.All(entry => entry.Value)
                        && handlerType == RuleHandlerType.Evaluation;
                }

                _isReadyToResolve = !_telemetryStates.IsEmpty && _telemetryStates.Values.All(s => s.IsReadyToResolve());

                // if isReadyToResolve, wait until atleast one Resolved telemetry event has fired before stopping telemetry
                _stopTelemetry = !_isReadyToResolve && IsStopTelemetryInAllTargetFrameworks();
            }
        }

        private void InitializeProjectId()
        {
            var projectGuidService = _project.Services.ExportProvider.GetExportedValueOrDefault<IProjectGuidService>();
            if (projectGuidService != null)
            {
                SetProjectId(projectGuidService.ProjectGuid.ToString());
            }
            else
            {
                SetProjectId(_telemetryService.HashValue(_project.FullPath));
            }
        }

        private bool IsStopTelemetryInAllTargetFrameworks() => 
            !_telemetryStates.IsEmpty && _telemetryStates.Values.All(t => t.StopTelemetry);

        // helper to support testing
        internal void SetProjectId(string projectId)
        {
            _projectId = projectId;
        }
    }
}
