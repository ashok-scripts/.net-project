﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.VisualStudio.ProjectSystem.VS.LanguageServices
{
    public class CSharpCodeDomProviderTests
    {
        [Fact]
        public void Constructor_DoesNotThrow()
        {
            new CSharpCodeDomProvider();
        }

        [Fact]
        public void UnconfiguredProject_CanGetSet()
        {
            var project = UnconfiguredProjectFactory.Create();
            var provider = CreateInstance();

            provider.Project = project;

            Assert.Same(project, provider.Project);
        }

        private static CSharpCodeDomProvider CreateInstance()
        {
            return new CSharpCodeDomProvider();
        }
    }
}
