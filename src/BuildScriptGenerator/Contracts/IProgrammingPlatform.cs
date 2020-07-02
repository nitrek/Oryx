﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.Oryx.Detector;

namespace Microsoft.Oryx.BuildScriptGenerator
{
    /// <summary>
    /// Represents a programming platform.
    /// </summary>
    public interface IProgrammingPlatform
    {
        /// <summary>
        /// Gets the name of the platform which the script generator will create builds for.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the list of versions that the script generator supports.
        /// </summary>
        IEnumerable<string> SupportedVersions { get; }

        /// <summary>
        /// Detects the programming platform name and version required by the application in source directory.
        /// </summary>
        /// <param name="context">The <see cref="RepositoryContext"/>.</param>
        /// <returns>An instance of <see cref="PlatformDetectorResult"/> if detection was
        /// successful, <c>null</c> otherwise.</returns>
        PlatformDetectorResult Detect(RepositoryContext context);

        /// <summary>
        /// Generates a build Bash script based on the application in source directory.
        /// </summary>
        /// <param name="scriptGeneratorContext">The <see cref="BuildScriptGeneratorContext"/>.</param>
        /// <returns><see cref="BuildScriptSnippet "/> with the build snippet if successful,
        /// <c>null</c> otherwise.</returns>
        BuildScriptSnippet GenerateBashBuildScriptSnippet(
            BuildScriptGeneratorContext scriptGeneratorContext,
            PlatformDetectorResult detectorResult);

        /// <summary>
        /// Generate a bash script that can install the required runtime bits for the application's platforms.
        /// </summary>
        /// <param name="options">Options to generate the installation script with.</param>
        /// <returns>The bash installation script.</returns>
        string GenerateBashRunTimeInstallationScript(RunTimeInstallationScriptGeneratorOptions options);

        /// <summary>
        /// Checks if the programming platform should be included in a build script.
        /// </summary>
        /// <param name="ctx">The repository context.</param>
        /// <returns>true if the platform should be included, false otherwise.</returns>
        bool IsEnabled(RepositoryContext ctx);

        /// <summary>
        /// Checks if the source repository seems to have artifacts from a previous build.
        /// </summary>
        /// <param name="repo">Source repo to check.</param>
        /// <returns>true if the repo doesn't seem to have artifacts from a previous build, false otherwise.</returns>
        bool IsCleanRepo(ISourceRepo repo);

        /// <summary>
        /// Gets list of directories which need to be excluded from being copied to the output directory.
        /// </summary>
        /// <param name="scriptGeneratorContext">The context of BuildScriptGenerator.</param>
        /// <returns>List of directories.</returns>
        IEnumerable<string> GetDirectoriesToExcludeFromCopyToBuildOutputDir(
            BuildScriptGeneratorContext scriptGeneratorContext);

        /// <summary>
        /// Gets list of directories which need to be excluded from being copied to an intermediate directory, if used.
        /// </summary>
        /// <param name="scriptGeneratorContext">The context of BuildScriptGenerator.</param>
        /// <returns>List of directories.</returns>
        IEnumerable<string> GetDirectoriesToExcludeFromCopyToIntermediateDir(
            BuildScriptGeneratorContext scriptGeneratorContext);

        /// <summary>
        /// Checks if the programming platform wants to participate in a multi-platform build.
        /// </summary>
        /// <param name="ctx">The repository context.</param>
        /// <returns>true, if the platform wants to participate, false otherwise.</returns>
        bool IsEnabledForMultiPlatformBuild(RepositoryContext ctx);

        string GetInstallerScriptSnippet(BuildScriptGeneratorContext context, PlatformDetectorResult detectorResult);

        string ResolveVersion(string versionToResolve);
    }
}