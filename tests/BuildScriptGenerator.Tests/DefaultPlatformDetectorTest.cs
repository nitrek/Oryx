﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Oryx.Detector;
using Moq;
using Xunit;

namespace Microsoft.Oryx.BuildScriptGenerator.Tests
{
    public class DefaultPlatformDetectorTest
    {
        [Fact]
        public void RunsDetectionOnAllEnabledPlatforms()
        {
            // Arrange
            var platform1 = new Mock<IProgrammingPlatform>();
            platform1
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(new PlatformDetectorResult { Platform = "platform1", PlatformVersion = "1.0.0" });
            platform1
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(true);
            var platform2 = new Mock<IProgrammingPlatform>();
            platform2
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(new PlatformDetectorResult { Platform = "platform2", PlatformVersion = "1.0.0" });
            platform2
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(true);
            var detector = CreatePlatformDetector(new[] { platform1.Object, platform2.Object });
            var context = CreateScriptGeneratorContext();

            // Act
            var actualResults = detector.DetectPlatforms(context);

            // Assert
            Assert.NotNull(actualResults);
            Assert.Equal(2, actualResults.Count());
            Assert.Equal("platform1", actualResults.ElementAt(0).Platform);
            Assert.Equal("1.0.0", actualResults.ElementAt(0).PlatformVersion);
            Assert.Equal("platform2", actualResults.ElementAt(1).Platform);
            Assert.Equal("1.0.0", actualResults.ElementAt(1).PlatformVersion);
        }

        [Fact]
        public void RunsDetectionOnEnabledPlatformsOnly()
        {
            // Arrange
            var platform1 = new Mock<IProgrammingPlatform>();
            platform1
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(new PlatformDetectorResult { Platform = "platform1", PlatformVersion = "1.0.0" });
            platform1
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(true);
            var platform2 = new Mock<IProgrammingPlatform>();
            platform2
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(new PlatformDetectorResult { Platform = "platform2", PlatformVersion = "1.0.0" });
            platform2
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(false);
            var detector = CreatePlatformDetector(new[] { platform1.Object, platform2.Object });
            var context = CreateScriptGeneratorContext();

            // Act
            var actualResults = detector.DetectPlatforms(context);

            // Assert
            Assert.NotNull(actualResults);
            Assert.Single(actualResults);
            Assert.Equal("platform1", actualResults.ElementAt(0).Platform);
            Assert.Equal("1.0.0", actualResults.ElementAt(0).PlatformVersion);
        }

        [Fact]
        public void DoesNoFailIfPlatformDetectorReturnsNull()
        {
            // Arrange
            var platform1 = new Mock<IProgrammingPlatform>();
            platform1
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(value: null);
            platform1
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(true);
            var platform2 = new Mock<IProgrammingPlatform>();
            platform2
                .Setup(p => p.Detect(It.IsAny<RepositoryContext>()))
                .Returns(new PlatformDetectorResult { Platform = "platform2", PlatformVersion = "1.0.0" });
            platform2
                .Setup(p => p.IsEnabled(It.IsAny<RepositoryContext>()))
                .Returns(true);
            var detector = CreatePlatformDetector(new[] { platform1.Object, platform2.Object });
            var context = CreateScriptGeneratorContext();

            // Act
            var actualResults = detector.DetectPlatforms(context);

            // Assert
            Assert.NotNull(actualResults);
            Assert.Single(actualResults);
            Assert.Equal("platform2", actualResults.ElementAt(0).Platform);
            Assert.Equal("1.0.0", actualResults.ElementAt(0).PlatformVersion);
        }

        private DefaultPlatformDetector CreatePlatformDetector(
            IEnumerable<IProgrammingPlatform> platforms)
        {
            return new DefaultPlatformDetector(
                platforms,
                new DefaultStandardOutputWriter());
        }

        private static BuildScriptGeneratorContext CreateScriptGeneratorContext()
        {
            return new BuildScriptGeneratorContext
            {
                SourceRepo = new MemorySourceRepo(),
            };
        }
    }
}