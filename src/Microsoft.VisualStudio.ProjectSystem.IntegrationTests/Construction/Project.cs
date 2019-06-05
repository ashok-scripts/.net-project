﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.Test.Apex.VisualStudio.Solution;

namespace Microsoft.VisualStudio.ProjectSystem.VS
{
    /// <summary>
    /// Defines a <c>.csproj</c> file to be created when using <see cref="ProjectLayoutTestBase"/>.
    /// </summary>
    public sealed class Project : IEnumerable   
    {
        private static readonly Guid _sdkProjectTypeGuid = Guid.Parse("9A19103F-16F7-4668-BE54-9A1E7A4F7556");

        private List<Project>? _referencedProjects;
        private List<PackageReference>? _packageReferences;

        public XElement XElement { get; } = new XElement("Project");

        public string Sdk { get; }
        public string TargetFrameworks { get; }

        public string ProjectName { get; } = "Project_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        public string ProjectFileName => $"{ProjectName}.csproj";
        public string RelativeProjectFilePath => $"{ProjectName}\\{ProjectName}.csproj";

        public Guid ProjectGuid { get; } = Guid.NewGuid();
        public object ProjectTypeGuid => _sdkProjectTypeGuid;

        public ProjectTestExtension? Extension { get; set; }

        public Project(string targetFrameworks, string sdk = "Microsoft.NET.Sdk")
        {
            TargetFrameworks = targetFrameworks;
            Sdk = sdk;
        }

        public void Save(string rootPath)
        {
            XElement.Add(new XAttribute("Sdk", Sdk));

            XElement.Add(new XElement(
                "PropertyGroup",
                new XElement("TargetFrameworks", TargetFrameworks)));

            if (_referencedProjects != null)
            {
                XElement.Add(new XElement(
                    "ItemGroup",
                    _referencedProjects.Select(p => new XElement(
                        "ProjectReference",
                        new XAttribute("Include", $"..\\{p.RelativeProjectFilePath}")))));
            }

            if (_packageReferences != null)
            {
                XElement.Add(new XElement(
                    "ItemGroup",
                    _packageReferences.Select(p => new XElement(
                        "PackageReference",
                        new XAttribute("Include", p.PackageId),
                        new XAttribute("Version", p.Version)))));
            }

            Directory.CreateDirectory(Path.Combine(rootPath, ProjectName));

            XElement.Save(Path.Combine(rootPath, RelativeProjectFilePath));
        }

        /// <summary>
        /// Adds a P2P (project-to-project) reference from this project to <paramref name="referree"/>.
        /// </summary>
        /// <param name="referree">The project to reference.</param>
        public void Add(Project referree)
        {
            if (_referencedProjects == null)
                _referencedProjects = new List<Project>();
            _referencedProjects.Add(referree);
        }

        public void Add(PackageReference packageReference)
        {
            if (_packageReferences == null)
                _packageReferences = new List<PackageReference>();
            _packageReferences.Add(packageReference);
        }

        /// <summary>
        /// We only implement <see cref="IEnumerable"/> to support collection initialiser syntax.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => throw new NotSupportedException();
    }

    public readonly struct PackageReference
    {
        public string PackageId { get; }
        public string Version { get; }

        public PackageReference(string packageId, string version)
        {
            PackageId = packageId;
            Version = version;
        }
    }
}
