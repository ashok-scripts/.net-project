﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Construction;
using Xunit;

namespace Microsoft.VisualStudio.Build
{
    public class BuildUtilitiesTests
    {
        [Theory]
        [InlineData("'$(Configuration)' == ''",                                                     new[] { "Configuration" },                                new[] { "" })]
        [InlineData("'$(BuildingInsideVisualStudio)' == 'true'",                                    new[] { "BuildingInsideVisualStudio" },                   new[] { "true" })]
        [InlineData("'$(OS)' == 'Windows_NT'",                                                      new[] { "OS" },                                           new[] { "Windows_NT" })]
        [InlineData("'$(Configuration)' == 'Debug'",                                                new[] { "Configuration" },                                new[] { "Debug" })]
        [InlineData("'$(Configuration)' ==  'Debug'",                                               new[] { "Configuration" },                                new[] { "Debug" })]
        [InlineData(" '$(Configuration)' == 'Debug' ",                                              new[] { "Configuration" },                                new[] { "Debug" })]
        [InlineData("'$(Configuration)|$(Configuration)' == 'Debug|AnyCPU'",                        new[] { "Configuration" }, new[] { "AnyCPU" })]
        [InlineData("'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'",                             new[] { "Configuration", "Platform" },                    new[] { "Debug", "AnyCPU" })]
        [InlineData(" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU'",                            new[] { "Configuration", "Platform" },                    new[] { "Debug", "AnyCPU" })]
        [InlineData(" '$(Configuration)|$(Platform)'  ==  'Debug|AnyCPU' ",                         new[] { "Configuration", "Platform" },                    new[] { "Debug", "AnyCPU" })]
        [InlineData("'$(Configuration)|$(Platform)|$(TargetFramework)' == 'Debug|AnyCPU|net46'",    new[] { "Configuration", "Platform", "TargetFramework" }, new[] { "Debug", "AnyCPU", "net46" })]
        [InlineData(" '$(Configuration)|$(Platform)|$(TargetFramework)' == 'Debug|AnyCPU|net46' ",  new[] { "Configuration", "Platform", "TargetFramework" }, new[] { "Debug", "AnyCPU", "net46" })]
        [InlineData("'$(Configuration)|$(Platform)|$(TargetFramework)' == 'Debug|AnyCPU|'",         new[] { "Configuration", "Platform", "TargetFramework" }, new[] { "Debug", "AnyCPU", "" })]
        public void TryCalculateConditionalProperties_ValidConditions(string condition, string[] expectedNames, string[] expectedValues)
        {
            bool result = BuildUtilities.TryCalculateConditionalProperties(condition, out IReadOnlyDictionary<string, string>? values);

            Assert.True(result);

            Assert.NotNull(values);
            Assert.Equal(expectedNames, values!.Keys);
            Assert.Equal(expectedValues, values!.Values);
        }

        [Theory]
        [InlineData("")]
        [InlineData("=")]
        [InlineData(" = ")]
        [InlineData("'$(Configuration'")]
        [InlineData("'Configuration' == 'Debug'")]
        [InlineData("'Configuration'")]
        [InlineData("true")]
        [InlineData("$(Configuration)")]
        [InlineData("$(Configuration) == ''")]
        [InlineData("'' ==")]
        [InlineData("'' == ''")]
        [InlineData("'$()' == ")]
        [InlineData("'$()' == ''")]
        [InlineData("'$(Configuration)' == ")]
        [InlineData("'$(Configuration)|$(Platform)' == 'Debug'")]
        [InlineData("'$(Configuration)|' == 'Debug|'")]
        [InlineData("'$(Configuration)|$(Platform)|$(TargetFramework)' == 'Debug|AnyCPU'")]
        [InlineData("'$(Configuration)|$(Platform)' == 'Debug|AnyCPU|net46'")]
        public void TryCalculateConditionalProperties_InvalidConditions(string condition)
        {
            bool result = BuildUtilities.TryCalculateConditionalProperties(condition, out IReadOnlyDictionary<string, string>? values);

            Assert.False(result);
            Assert.Null(values);
        }

        [Fact]
        public void GetProperty_MissingProperty()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>MyPropertyValue</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            var property = BuildUtilities.GetProperty(project, "NonExistentProperty");
            Assert.Null(property);
        }

        [Fact]
        public void GetProperty_ExistentProperty()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>MyPropertyValue</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
        }

        [Fact]
        public void GetPropertyValues_SingleValue()
        {
            var values = BuildUtilities.GetPropertyValues("MyPropertyValue");
            Assert.Collection(values, firstValue => Assert.Equal("MyPropertyValue", firstValue));
        }

        [Fact]
        public void GetPropertyValues_MultipleValues()
        {
            var values = BuildUtilities.GetPropertyValues("1;2");
            Assert.Collection(values,
                firstValue => Assert.Equal("1", firstValue),
                secondValue => Assert.Equal("2", secondValue));
        }

        [Fact]
        public void GetPropertyValues_EmptyValues()
        {
            var values = BuildUtilities.GetPropertyValues("1;   ;;;2");
            Assert.Collection(values,
                firstValue => Assert.Equal("1", firstValue),
                secondValue => Assert.Equal("2", secondValue));
        }

        [Fact]
        public void GetPropertyValues_WhiteSpace()
        {
            var values = BuildUtilities.GetPropertyValues("   1;   ; ; ; 2 ");
            Assert.Collection(values,
                firstValue => Assert.Equal("1", firstValue),
                secondValue => Assert.Equal("2", secondValue));
        }

        [Fact]
        public void GetPropertyValues_Duplicates()
        {
            var values = BuildUtilities.GetPropertyValues("1;2;1;1;2;2;2;1");
            Assert.Collection(values,
                firstValue => Assert.Equal("1", firstValue),
                secondValue => Assert.Equal("2", secondValue));
        }

        [Fact]
        public void GetOrAddProperty_NoGroups()
        {
            var project = ProjectRootElementFactory.Create();
            BuildUtilities.GetOrAddProperty(project, "MyProperty");
            Assert.Single(project.Properties);
            Assert.Collection(project.PropertyGroups,
                group => Assert.Collection(group.Properties,
                    firstProperty => Assert.Equal(string.Empty, firstProperty.Value)));
        }

        [Fact]
        public void GetOrAddProperty_FirstGroup()
        {
            string projectXml =
@"<Project>
  <PropertyGroup/>
  <PropertyGroup/>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.GetOrAddProperty(project, "MyProperty");
            Assert.Single(project.Properties);
            AssertEx.CollectionLength(project.PropertyGroups, 2);

            var group = project.PropertyGroups.First();
            Assert.Single(group.Properties);

            var property = group.Properties.First();
            Assert.Equal(string.Empty, property.Value);
        }

        [Fact]
        public void GetOrAddProperty_ExistingProperty()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
    <MyProperty>1</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.GetOrAddProperty(project, "MyProperty");
            Assert.Single(project.Properties);
            Assert.Single(project.PropertyGroups);

            var group = project.PropertyGroups.First();
            Assert.Single(group.Properties);

            var property = group.Properties.First();
            Assert.Equal("1", property.Value);
        }

        [Fact]
        public void AppendPropertyValue_DefaultDelimiter()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>1;2</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.AppendPropertyValue(project, "1;2", "MyProperty", "3");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1;2;3", property!.Value);
        }

        [Fact]
        public void AppendPropertyValue_EmptyProperty()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty/>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.AppendPropertyValue(project, "", "MyProperty", "1");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1", property!.Value);
        }

        [Fact]
        public void AppendPropertyValue_InheritedValue()
        {
            var project = ProjectRootElementFactory.Create();
            BuildUtilities.AppendPropertyValue(project, "1;2", "MyProperty", "3");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1;2;3", property!.Value);
        }

        [Fact]
        public void AppendPropertyValue_MissingProperty()
        {
            var project = ProjectRootElementFactory.Create();
            BuildUtilities.AppendPropertyValue(project, "", "MyProperty", "1");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1", property!.Value);
        }

        [Fact]
        public void RemovePropertyValue_DefaultDelimiter()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>1;2</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.RemovePropertyValue(project, "1;2", "MyProperty", "2");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1", property!.Value);
        }

        [Fact]
        public void RemovePropertyValue_EmptyAfterRemove()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>1</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.RemovePropertyValue(project, "1", "MyProperty", "1");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal(string.Empty, property!.Value);
        }

        [Fact]
        public void RemovePropertyValue_InheritedValue()
        {
            var project = ProjectRootElementFactory.Create();
            BuildUtilities.RemovePropertyValue(project, "1;2", "MyProperty", "1");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("2", property!.Value);
        }

        [Fact]
        public void RemovePropertyValue_MissingProperty()
        {
            var project = ProjectRootElementFactory.Create();
            Assert.Throws<ArgumentException>("valueToRemove", () => BuildUtilities.RemovePropertyValue(project, "", "MyProperty", "1"));
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal(string.Empty, property!.Value);
        }

        [Fact]
        public void RenamePropertyValue_DefaultDelimiter()
        {
            string projectXml =
@"<Project>
  <PropertyGroup>
     <MyProperty>1;2</MyProperty>
  </PropertyGroup>
</Project>";

            var project = ProjectRootElementFactory.Create(projectXml);
            BuildUtilities.RenamePropertyValue(project, "1;2", "MyProperty", "2", "5");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("1;5", property!.Value);
        }

        [Fact]
        public void RenamePropertyValue_InheritedValue()
        {
            var project = ProjectRootElementFactory.Create();
            BuildUtilities.RenamePropertyValue(project, "1;2", "MyProperty", "1", "3");
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal("3;2", property!.Value);
        }

        [Fact]
        public void RenamePropertyValue_MissingProperty()
        {
            var project = ProjectRootElementFactory.Create();
            Assert.Throws<ArgumentException>("oldValue", () => BuildUtilities.RenamePropertyValue(project, "", "MyProperty", "1", "2"));
            var property = BuildUtilities.GetProperty(project, "MyProperty");
            Assert.NotNull(property);
            Assert.Equal(string.Empty, property!.Value);
        }
    }
}
