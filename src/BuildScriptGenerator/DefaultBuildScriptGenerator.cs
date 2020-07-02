﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator.Common;
using Microsoft.Oryx.BuildScriptGenerator.Exceptions;
using Microsoft.Oryx.BuildScriptGenerator.Resources;
using Microsoft.Oryx.Common.Extensions;
using Microsoft.Oryx.Detector;

namespace Microsoft.Oryx.BuildScriptGenerator
{
    /// <summary>
    /// Finds and resolves scripts generators based on user input and invokes one of them to generate a script.
    /// </summary>
    internal class DefaultBuildScriptGenerator : IBuildScriptGenerator
    {
        private readonly BuildScriptGeneratorOptions _cliOptions;
        private readonly ICompatiblePlatformDetector _compatiblePlatformDetector;
        private readonly DefaultPlatformDetector _platformDetector;
        private readonly PlatformsInstallationScriptProvider _environmentSetupScriptProvider;
        private readonly IEnumerable<IChecker> _checkers;
        private readonly ILogger<DefaultBuildScriptGenerator> _logger;
        private readonly IStandardOutputWriter _writer;

        public DefaultBuildScriptGenerator(
            DefaultPlatformDetector platformDetector,
            PlatformsInstallationScriptProvider environmentSetupScriptProvider,
            IOptions<BuildScriptGeneratorOptions> cliOptions,
            ICompatiblePlatformDetector compatiblePlatformDetector,
            IEnumerable<IChecker> checkers,
            ILogger<DefaultBuildScriptGenerator> logger,
            IStandardOutputWriter writer)
        {
            _platformDetector = platformDetector;
            _environmentSetupScriptProvider = environmentSetupScriptProvider;
            _cliOptions = cliOptions.Value;
            _compatiblePlatformDetector = compatiblePlatformDetector;
            _logger = logger;
            _checkers = checkers;
            _writer = writer;
            _logger.LogDebug("Available checkers: {checkerCount}", _checkers?.Count() ?? 0);
        }

        public void GenerateBashScript(
            BuildScriptGeneratorContext context,
            out string script,
            List<ICheckerMessage> checkerMessageSink = null)
        {
            script = null;

            IList<BuildScriptSnippet> buildScriptSnippets;
            var directoriesToExcludeFromCopyToIntermediateDir = new List<string>();
            var directoriesToExcludeFromCopyToBuildOutputDir = new List<string>();

            // Try detecting ALL platforms since in some scenarios this is required.
            // For example, in case of a multi-platform app like ASP.NET Core + NodeJs, we might need to dynamically
            // install both these platforms' sdks before actually using any of their commands. So even though a user
            // of Oryx might explicitly supply the platform of the app as .NET Core, we still need to make sure the
            // build environment is setup with detected platforms' sdks.
            var detectionResults = _platformDetector.DetectPlatforms(context);
            var installationScript = _environmentSetupScriptProvider.GetBashScriptSnippet(
                context,
                detectionResults);

            // Get list of detected platforms and versions to be set on benv
            var toolsToVersion = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var detectionResult in detectionResults)
            {
                toolsToVersion[detectionResult.Platform] = detectionResult.PlatformVersion;
            }

            using (var timedEvent = _logger.LogTimedEvent("GetBuildSnippets"))
            {
                buildScriptSnippets = GetBuildSnippets(
                    context,
                    detectionResults,
                    runDetection: false,
                    directoriesToExcludeFromCopyToIntermediateDir,
                    directoriesToExcludeFromCopyToBuildOutputDir);
                timedEvent.SetProperties(toolsToVersion);
            }

            if (_checkers != null && checkerMessageSink != null && _cliOptions.EnableCheckers)
            {
                try
                {
                    _logger.LogDebug("Running checkers");
                    RunCheckers(context, toolsToVersion, checkerMessageSink);
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "Exception caught while running checkers");
                }
            }
            else
            {
                _logger.LogInformation("Not running checkers - condition evaluates to " +
                                       "({checkersNotNull} && {sinkNotNull} && {enableCheckers})",
                                       _checkers != null, checkerMessageSink != null, _cliOptions.EnableCheckers);
            }

            if (buildScriptSnippets != null)
            {
                foreach (var snippet in buildScriptSnippets)
                {
                    if (snippet.IsFullScript)
                    {
                        script = snippet.BashBuildScriptSnippet;
                        return;
                    }
                }
            }

            if (buildScriptSnippets.Any())
            {
                // By default exclude these irrespective of platform
                directoriesToExcludeFromCopyToIntermediateDir.Add(".git");
                directoriesToExcludeFromCopyToBuildOutputDir.Add(".git");

                script = BuildScriptFromSnippets(
                    context,
                    installationScript,
                    buildScriptSnippets,
                    new ReadOnlyDictionary<string, string>(toolsToVersion),
                    directoriesToExcludeFromCopyToIntermediateDir,
                    directoriesToExcludeFromCopyToBuildOutputDir,
                    detectionResults);
            }
            else
            {
                // TODO: Should an UnsupportedPlatformException be thrown here?
                // Seeing as the issue was that platforms were IDENTIFIED, but no build snippets were emitted from them
                throw new UnsupportedPlatformException(Labels.UnableToDetectPlatformMessage);
            }
        }

        private void RunCheckers(
            BuildScriptGeneratorContext ctx,
            IDictionary<string, string> tools,
            [NotNull] List<ICheckerMessage> checkerMessageSink)
        {
            var checkers = _checkers.WhereApplicable(tools).ToArray();

            _logger.LogInformation("Running {checkerCount} applicable checkers for {toolCount} tools: {toolNames}",
                checkers.Length, tools.Keys.Count, string.Join(',', tools.Keys));

            using (var timedEvent = _logger.LogTimedEvent("RunCheckers"))
            {
                var repoMessages = checkers.SelectMany(checker => checker.CheckSourceRepo(ctx.SourceRepo));
                checkerMessageSink.AddRange(repoMessages);

                var toolMessages = checkers.SelectMany(checker => checker.CheckToolVersions(tools));
                checkerMessageSink.AddRange(toolMessages);

                timedEvent.AddProperty("repoMsgCount", repoMessages.Count().ToString());
                timedEvent.AddProperty("toolMsgCount", toolMessages.Count().ToString());

                timedEvent.AddProperty("checkersApplied",
                    string.Join(',', checkers.Select(checker => checker.GetType().Name)));
            }
        }

        private IList<BuildScriptSnippet> GetBuildSnippets(
            BuildScriptGeneratorContext context,
            IEnumerable<PlatformDetectorResult> detectionResults,
            bool runDetection,
            [CanBeNull] List<string> directoriesToExcludeFromCopyToIntermediateDir,
            [CanBeNull] List<string> directoriesToExcludeFromCopyToBuildOutputDir)
        {
            var snippets = new List<BuildScriptSnippet>();

            IDictionary<IProgrammingPlatform, PlatformDetectorResult> platformsToUse;
            if (runDetection)
            {
                platformsToUse = _compatiblePlatformDetector.GetCompatiblePlatforms(context);
            }
            else
            {
                platformsToUse = _compatiblePlatformDetector.GetCompatiblePlatforms(context, detectionResults);
            }

            foreach (var platformAndDetectorResult in platformsToUse)
            {
                var (platform, detectorResult) = platformAndDetectorResult;

                if (directoriesToExcludeFromCopyToIntermediateDir != null)
                {
                    var excludedDirs = platform.GetDirectoriesToExcludeFromCopyToIntermediateDir(context);
                    if (excludedDirs.Any())
                    {
                        directoriesToExcludeFromCopyToIntermediateDir.AddRange(excludedDirs);
                    }
                }

                if (directoriesToExcludeFromCopyToBuildOutputDir != null)
                {
                    var excludedDirs = platform.GetDirectoriesToExcludeFromCopyToBuildOutputDir(context);
                    if (excludedDirs.Any())
                    {
                        directoriesToExcludeFromCopyToBuildOutputDir.AddRange(excludedDirs);
                    }
                }

                string cleanOrNot = platform.IsCleanRepo(context.SourceRepo) ? "clean" : "not clean";
                _logger.LogDebug($"Repo is {cleanOrNot} for {platform.Name}");

                var snippet = platform.GenerateBashBuildScriptSnippet(context, detectorResult);
                if (snippet != null)
                {
                    _logger.LogDebug(
                        "Platform {platformName} with version {platformVersion} was used.",
                        platform.Name,
                        detectorResult);
                    snippets.Add(snippet);
                }
                else
                {
                    _logger.LogWarning("{platformType}.GenerateBashBuildScriptSnippet() returned null",
                        platform.GetType());
                }
            }

            return snippets;
        }

        private void LogScriptIfGiven(string type, string scriptPath)
        {
            if (!string.IsNullOrWhiteSpace(scriptPath))
            {
                _logger.LogInformation("Using {type} script", type);
            }
        }

        /// <summary>
        /// Builds the full build script from the list of snippets for each platform.
        /// </summary>
        /// <returns>Finalized build script as a string.</returns>
        private string BuildScriptFromSnippets(
            BuildScriptGeneratorContext context,
            string installationScript,
            IList<BuildScriptSnippet> buildScriptSnippets,
            IDictionary<string, string> toolsToVersion,
            List<string> directoriesToExcludeFromCopyToIntermediateDir,
            List<string> directoriesToExcludeFromCopyToBuildOutputDir,
            IEnumerable<PlatformDetectorResult> detectionResults)
        {
            string script;
            string benvArgs = StringExtensions.JoinKeyValuePairs(toolsToVersion);

            Dictionary<string, string> buildProperties = buildScriptSnippets
                .Where(s => s.BuildProperties != null)
                .SelectMany(s => s.BuildProperties)
                .ToDictionary(p => p.Key, p => p.Value);
            buildProperties[ManifestFilePropertyKeys.OperationId] = context.OperationId;

            var allPlatformNames = detectionResults
                .Where( s => s.Platform != null)
                .Select(s => s.Platform)
                .ToList();

            foreach (var eachPlatformName in allPlatformNames)
            {
                _logger.LogInformation($"Build Property Key:{ManifestFilePropertyKeys.PlatformName} value: {eachPlatformName} is written into manifest");
                if (buildProperties.ContainsKey(ManifestFilePropertyKeys.PlatformName))
                {
                    buildProperties[ManifestFilePropertyKeys.PlatformName]
                    = string.Join(
                        buildProperties[ManifestFilePropertyKeys.PlatformName],
                        ",",
                        eachPlatformName);
                }
                else
                {
                    buildProperties[ManifestFilePropertyKeys.PlatformName] = eachPlatformName;
                }
            }

            (var preBuildCommand, var postBuildCommand) = PreAndPostBuildCommandHelper.GetPreAndPostBuildCommands(
                context.SourceRepo,
                _cliOptions);

            var outputIsSubDirOfSourceDir = false;
            if (!string.IsNullOrEmpty(_cliOptions.DestinationDir))
            {
                outputIsSubDirOfSourceDir = DirectoryHelper.IsSubDirectory(
                    _cliOptions.DestinationDir,
                    _cliOptions.SourceDir);
            }

            // Copy the source content to destination only if all the platforms involved in generating the build script
            // say yes.
            var copySourceDirectoryContentToDestinationDirectory = buildScriptSnippets.All(
                snippet => snippet.CopySourceDirectoryContentToDestinationDirectory);

            if (!string.IsNullOrEmpty(_cliOptions.AppType)
                && !string.IsNullOrWhiteSpace(_cliOptions.AppType))
            {
                _logger.LogInformation($"Build Property Key {Constants.AppType} with value {_cliOptions.AppType} is written into manifest");
                buildProperties[Constants.AppType] = _cliOptions.AppType;
            }

            var buildScriptProps = new BaseBashBuildScriptProperties()
            {
                OsPackagesToInstall = _cliOptions.RequiredOsPackages ?? new string[0],
                BuildScriptSnippets = buildScriptSnippets.Select(s => s.BashBuildScriptSnippet),
                BenvArgs = benvArgs,
                PreBuildCommand = preBuildCommand,
                PostBuildCommand = postBuildCommand,
                DirectoriesToExcludeFromCopyToIntermediateDir = directoriesToExcludeFromCopyToIntermediateDir,
                DirectoriesToExcludeFromCopyToBuildOutputDir = directoriesToExcludeFromCopyToBuildOutputDir,
                ManifestFileName = FilePaths.BuildManifestFileName,
                ManifestDir = context.ManifestDir,
                BuildProperties = buildProperties,
                BenvPath = FilePaths.Benv,
                PlatformInstallationScript = installationScript,
                OutputDirectoryIsNested = outputIsSubDirOfSourceDir,
                CopySourceDirectoryContentToDestinationDirectory = copySourceDirectoryContentToDestinationDirectory,
            };

            LogScriptIfGiven("pre-build", buildScriptProps.PreBuildCommand);
            LogScriptIfGiven("post-build", buildScriptProps.PostBuildCommand);

            script = TemplateHelper.Render(
                TemplateHelper.TemplateResource.BaseBashScript,
                buildScriptProps,
                _logger);
            return script;
        }
    }
}