﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Oryx.Detector.Node;
using Xunit;

namespace Microsoft.Oryx.Detector.Tests.Node
{
    public class NodeDetectorTest
    {
        private const string PackageJsonWithNoVersions = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo \""Error: no test specified\"" && exit 1"",
            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC""
        }";

        private const string PackageJsonWithNodeVersion = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo \""Error: no test specified\"" && exit 1"",
            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC"",
          ""engines"" : { ""node"" : ""6.11.0"" }
        }";

        private const string PackageJsonTemplateWithNodeVersion = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo \""Error: no test specified\"" && exit 1"",
            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC"",
          ""engines"" : { ""node"" : ""#VERSION_RANGE#"" }
        }";

        private const string PackageJsonWithOnlyNpmVersion = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo \""Error: no test specified\"" && exit 1"",
            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC"",
          ""engines"" : { ""npm"" : ""5.4.2"" }
        }";

        private const string PakageJsonWithUnsupportedNodeVersion = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo \""Error: no test specified\"" && exit 1"",

            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC"",
          ""engines"" : { ""node"" : ""20.20.20"" }
        }";

        private const string MalformedPackageJson = @"{
          ""name"": ""mynodeapp"",
          ""version"": ""1.0.0"",
          ""description"": ""test app"",
          ""main"": ""server.js"",
          ""scripts"": {
            ""test"": ""echo ,
            ""start"": ""node server.js""
          },
          ""author"": ""Dev"",
          ""license"": ""ISC""
        }";

        private const string SimpleServerJs = @"
            var http = require(""http"")
            http.createServer(function(req, res) {
                res.writeHead(200, { ""Content-Type"": ""text/plain""});
                res.write(""Test!"");
                res.end();
            }).listen(8888);";

        [Fact]
        public void Detect_ReturnsNull_IfSourceDirectory_DoesNotHaveAnyFiles()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            // No files in source directory
            var repo = new MemorySourceRepo();
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForSourceRepoOnlyWithServerJs()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(SimpleServerJs, "server.js");
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsNull_ForSourceRepoWithServerJs_NotInRootDirectory()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(SimpleServerJs, "subDir1", "server.js");
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForSourceRepoOnlyWithAppJs()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile("app.js content", "app.js");
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsNull_ForSourceRepoWithAppJs_NotInRootDirectory()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(SimpleServerJs, "subDir1", "app.js");
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForPackageJsonWithNoVersion()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(PackageJsonWithNoVersions, NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsNull_ForSourceRepoWithPackageJson_NotInRootDirectory()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(PackageJsonWithNodeVersion, "subDir1", NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForPackageJsonWithOnlyNpmVersion()
        {
            // Node detector only looks for node version and not the NPM version. The individual script
            // generator looks for npm version.

            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(PackageJsonWithOnlyNpmVersion, NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsVersionFromPackageJson_IfEnvironmentVariableValueIsNotPresent()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            var packageJson = PackageJsonTemplateWithNodeVersion.Replace("#VERSION_RANGE#", "6.11.0");
            repo.AddFile(packageJson, NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Equal("6.11.0", result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForMalformedPackageJson()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(MalformedPackageJson, NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Fact]
        public void Detect_ReturnsNullVersion_ForPackageJsonWithNoExplicitVersionsSpecified()
        {
            // Arrange
            var detector = CreateNodePlatformDetector();
            var repo = new MemorySourceRepo();
            repo.AddFile(PackageJsonWithNoVersions, NodeConstants.PackageJsonFileName);
            var context = CreateContext(repo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("nodejs", result.Platform);
            Assert.Null(result.PlatformVersion);
        }

        [Theory]
        [InlineData("default.htm")]
        [InlineData("default.html")]
        [InlineData("default.asp")]
        [InlineData("index.htm")]
        [InlineData("index.html")]
        [InlineData("iisstart.htm")]
        [InlineData("default.aspx")]
        [InlineData("index.php")]
        public void Detect_ReturnsNull_IfIISStartupFileIsPresent(string iisStartupFileName)
        {
            // Arrange
            var sourceRepo = new MemorySourceRepo();
            sourceRepo.AddFile("", iisStartupFileName);
            var detector = CreateNodePlatformDetector();
            var context = CreateContext(sourceRepo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("default.htm")]
        [InlineData("default.html")]
        [InlineData("default.asp")]
        [InlineData("index.htm")]
        [InlineData("index.html")]
        [InlineData("iisstart.htm")]
        [InlineData("default.aspx")]
        [InlineData("index.php")]
        public void Detect_ReturnsNull_IfServerJs_AndIISStartupFileIsPresent(string iisStartupFileName)
        {
            // Arrange
            var sourceRepo = new MemorySourceRepo();
            sourceRepo.AddFile("", "server.js");
            sourceRepo.AddFile("", iisStartupFileName);
            var detector = CreateNodePlatformDetector();
            var context = CreateContext(sourceRepo);

            // Act
            var result = detector.Detect(context);

            // Assert
            Assert.Null(result);
        }

        private DetectorContext CreateContext(ISourceRepo sourceRepo)
        {
            return new DetectorContext
            {
                SourceRepo = sourceRepo,
            };
        }

        private NodeDetector CreateNodePlatformDetector()
        {
            return new NodeDetector(
                NullLogger<NodeDetector>.Instance);
        }

    }
}
