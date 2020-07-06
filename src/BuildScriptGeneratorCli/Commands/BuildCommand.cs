﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator;
using Microsoft.Oryx.BuildScriptGenerator.Common;
using Microsoft.Oryx.BuildScriptGeneratorCli.Options;

namespace Microsoft.Oryx.BuildScriptGeneratorCli
{
    [Command(Name, Description = "Build an app.")]
    internal class BuildCommand : BuildCommandBase
    {
        public const string Name = "build";

        // Beginning and ending markers for build script output spans that should be time measured
        private readonly TextSpan[] _measurableStdOutSpans =
        {
            new TextSpan(
                "RunPreBuildScript",
                Oryx.BuildScriptGenerator.Constants.PreBuildCommandPrologue,
                Oryx.BuildScriptGenerator.Constants.PreBuildCommandEpilogue),
            new TextSpan(
                "RunPostBuildScript",
                Oryx.BuildScriptGenerator.Constants.PostBuildCommandPrologue,
                Oryx.BuildScriptGenerator.Constants.PostBuildCommandEpilogue),
        };

        private bool _languageVersionWasSet;
        private bool _languageWasSet;

        [Option(
            "-i|--intermediate-dir <dir>",
            CommandOptionType.SingleValue,
            Description = "The path to a temporary directory to be used by this tool.")]
        public string IntermediateDir { get; set; }

        [Option(
            OptionTemplates.Language,
            CommandOptionType.SingleValue,
            Description = "The name of the programming platform used in the provided source directory.",
            ShowInHelpText = false)]
        public string LanguageName
        {
            get => PlatformName;
            set
            {
                PlatformName = value;
                _languageWasSet = true;
            }
        }

        [Option(
            OptionTemplates.LanguageVersion,
            CommandOptionType.SingleValue,
            Description = "The version of the programming platform used in the provided source directory.",
            ShowInHelpText = false)]
        public string LanguageVersion
        {
            get => PlatformVersion;
            set
            {
                PlatformVersion = value;
                _languageVersionWasSet = true;
            }
        }

        [Option(
            OptionTemplates.AppType,
            CommandOptionType.SingleValue,
            Description = "Type of application that the source directory has, for example: 'functions' or 'static-sites' etc.",
            ShowInHelpText = true)]
        public string AppType { get; set; }

        [Option(
            "-o|--output <dir>",
            CommandOptionType.SingleValue,
            Description = "The destination directory.")]
        public string DestinationDir { get; set; }

        [Option(
            OptionTemplates.ManifestDir,
            CommandOptionType.SingleValue,
            Description = "The path to a directory into which the build manifest file should be written.")]
        public string ManifestDir { get; set; }

        public static string BuildOperationName(IEnvironment env)
        {
            string result = LoggingConstants.DefaultOperationName;

            LoggingConstants.EnvTypeOperationNamePrefix.TryGetValue(env.Type, out string prefix);
            LoggingConstants.OperationNameSourceEnvVars.TryGetValue(env.Type, out string opNameSrcVarName);
            if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(opNameSrcVarName))
            {
                return result;
            }

            string opName = env.GetEnvironmentVariable(opNameSrcVarName);
            if (!string.IsNullOrWhiteSpace(opName))
            {
                result = $"{prefix}:{opName}";
            }

            return result;
        }

        internal static IDictionary<string, string> ProcessProperties(string[] properties)
        {
            return BuildScriptGeneratorOptionsHelper.ProcessProperties(properties);
        }

        internal override int Execute(IServiceProvider serviceProvider, IConsole console)
        {
            var environment = serviceProvider.GetRequiredService<IEnvironment>();
            var logger = serviceProvider.GetRequiredService<ILogger<BuildCommand>>();
            var buildOperationId = logger.StartOperation(BuildOperationName(environment));

            var sourceRepo = serviceProvider.GetRequiredService<ISourceRepoProvider>().GetSourceRepo();
            var sourceRepoCommitId = GetSourceRepoCommitId(environment, sourceRepo, logger);

            var oryxVersion = Program.GetVersion();
            var oryxCommitId = Program.GetMetadataValue(Program.GitCommit);
            var oryxReleaseTagName = Program.GetMetadataValue(Program.ReleaseTagName);
            var buildEventProps = new Dictionary<string, string>()
            {
                { "oryxVersion", oryxVersion },
                { "oryxCommitId", oryxCommitId },
                { "oryxReleaseTagName", oryxReleaseTagName },
                {
                    "oryxCommandLine",
                    string.Join(' ', environment.GetCommandLineArgs())
                },
                { "sourceRepoCommitId", sourceRepoCommitId },
            };

            logger.LogEvent("BuildRequested", buildEventProps);

            var options = serviceProvider.GetRequiredService<IOptions<BuildScriptGeneratorOptions>>().Value;

            var beginningOutputLog = GetBeginningCommandOutputLog();
            console.WriteLine(beginningOutputLog);
            var buildInfo = new DefinitionListFormatter();
            buildInfo.AddDefinition("Build Operation ID", buildOperationId);
            if (!string.IsNullOrWhiteSpace(sourceRepoCommitId))
            {
                buildInfo.AddDefinition("Repository Commit", sourceRepoCommitId);
            }

            console.WriteLine(buildInfo.ToString());

            // Generate build script
            string scriptContent;
            Exception exception;
            using (var stopwatch = logger.LogTimedEvent("GenerateBuildScript"))
            {
                var checkerMessages = new List<ICheckerMessage>();
                var scriptGenerator = new BuildScriptGenerator(
                    serviceProvider, console, checkerMessages, buildOperationId);

                var generated = scriptGenerator.TryGenerateScript(out scriptContent, out exception);
                stopwatch.AddProperty("generateSucceeded", generated.ToString());

                if (checkerMessages.Count > 0)
                {
                    var messageFormatter = new DefinitionListFormatter();
                    checkerMessages.ForEach(msg => messageFormatter.AddDefinition(msg.Level.ToString(), msg.Content));
                    console.WriteLine(messageFormatter.ToString());
                }
                else
                {
                    logger.LogDebug("No checker messages emitted");
                }

                if (!generated)
                {
                    if (exception != null)
                    {
                        return ProcessExitCodeHelper.GetExitCodeForException(exception);
                    }

                    return ProcessConstants.ExitFailure;
                }
            }

            // Get the path where the generated script should be written into.
            var tempDirectoryProvider = serviceProvider.GetRequiredService<ITempDirectoryProvider>();
            var buildScriptPath = Path.Combine(tempDirectoryProvider.GetTempDirectory(), "build.sh");

            // Write build script to selected path
            File.WriteAllText(buildScriptPath, scriptContent);
            logger.LogTrace("Build script written to file");
            if (DebugMode)
            {
                console.WriteLine($"Build script content:\n{scriptContent}");
            }

            // Merge the earlier build event properties
            buildEventProps = new Dictionary<string, string>(buildEventProps)
            {
                { "scriptPath", buildScriptPath },
                { "envVars", string.Join(",", GetEnvVarNames(environment)) },
            };

            var buildScriptOutput = new StringBuilder();
            var stdOutEventLoggers = new ITextStreamProcessor[]
            {
                new TextSpanEventLogger(logger, _measurableStdOutSpans),
                new PipDownloadEventLogger(logger),
            };

            DataReceivedEventHandler stdOutBaseHandler = (sender, args) =>
            {
                string line = args.Data;
                if (line == null)
                {
                    return;
                }

                console.WriteLine(line);
                buildScriptOutput.AppendLine(line);

                foreach (var processor in stdOutEventLoggers)
                {
                    // Catch any exception and log them instead of failing this build since whatever these processors
                    // do are not really relevant to the actual build of the app.
                    try
                    {
                        processor.ProcessLine(line);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(
                            ex,
                            $"An error occurred when trying to process the line '{line}' from standard " +
                            $"out using the  '{processor.GetType()}' processor.");
                    }
                }
            };

            DataReceivedEventHandler stdErrBaseHandler = (sender, args) =>
            {
                string line = args.Data;
                if (line == null)
                {
                    return;
                }

                // Not using IConsole.WriteErrorLine intentionally, to keep the child's error stream intact
                console.Error.WriteLine(line);
                buildScriptOutput.AppendLine(line);
            };

            // Run the generated script
            int exitCode;
            using (var timedEvent = logger.LogTimedEvent("RunBuildScript", buildEventProps))
            {
                console.WriteLine();
                exitCode = serviceProvider.GetRequiredService<IScriptExecutor>().ExecuteScript(
                    buildScriptPath,
                    new[]
                    {
                        sourceRepo.RootPath,
                        options.DestinationDir ?? string.Empty,
                        options.IntermediateDir ?? string.Empty,
                    },
                    workingDirectory: sourceRepo.RootPath,
                    stdOutBaseHandler,
                    stdErrBaseHandler);

                timedEvent.AddProperty("exitCode", exitCode.ToString());
            }

            if (exitCode != ProcessConstants.ExitSuccess)
            {
                logger.LogLongMessage(
                    LogLevel.Error,
                    header: "Error running build script",
                    buildScriptOutput.ToString(),
                    new Dictionary<string, object>
                    {
                        ["buildExitCode"] = exitCode,
                        ["oryxVersion"] = oryxVersion,
                        ["oryxReleaseTagName"] = oryxReleaseTagName,
                    });
                return exitCode;
            }

            return ProcessConstants.ExitSuccess;
        }

        internal override bool IsValidInput(IServiceProvider serviceProvider, IConsole console)
        {
            var options = serviceProvider.GetRequiredService<IOptions<BuildScriptGeneratorOptions>>().Value;
            var logger = serviceProvider.GetRequiredService<ILogger<BuildCommand>>();

            if (_languageWasSet)
            {
                logger.LogWarning("Deprecated option '--language' used");
                console.WriteLine("Warning: the deprecated option '--language' was used.");
            }

            if (_languageVersionWasSet)
            {
                logger.LogWarning("Deprecated option '--language-version' used");
                console.WriteLine("Warning: the deprecated option '--language-version' was used.");
            }

            // Invalid to specify platform version without platform name
            if (string.IsNullOrEmpty(options.PlatformName) && !string.IsNullOrEmpty(options.PlatformVersion))
            {
                logger.LogError("Cannot use lang version without lang name");
                console.WriteErrorLine("Cannot use platform version without specifying platform name also.");
                return false;
            }

            // Invalid to specify apptype anything other than "static-sites" or "functions" empty or null is expected
            if (!string.IsNullOrEmpty(options.AppType)
                && !(string.Equals(Constants.FunctionApplications, options.AppType.ToLower())
                || string.Equals(Constants.StaticSiteApplications, options.AppType.ToLower())))
            {
                logger.LogError($"Invalid value '{options.AppType}' for --apptype, only permitted values are 'static-sites' or 'functions'");
                console.WriteErrorLine($"Invalid input '{options.AppType}' for --apptype, only permitted values are 'static-sites' or 'functions'");
                return false;
            }

            if (!string.IsNullOrEmpty(options.IntermediateDir))
            {
                if (DirectoryHelper.AreSameDirectories(options.IntermediateDir, options.SourceDir))
                {
                    logger.LogError(
                        "Intermediate directory cannot be same as the source directory.");
                    console.WriteErrorLine(
                        $"Intermediate directory '{options.IntermediateDir}' cannot be " +
                        $"same as the source directory '{options.SourceDir}'.");
                    return false;
                }

                // Intermediate directory cannot be a sub-directory of the source directory
                if (DirectoryHelper.IsSubDirectory(options.IntermediateDir, options.SourceDir))
                {
                    logger.LogError(
                        "Intermediate directory cannot be a child of the source directory.");
                    console.WriteErrorLine(
                        $"Intermediate directory '{options.IntermediateDir}' cannot be a " +
                        $"sub-directory of source directory '{options.SourceDir}'.");
                    return false;
                }
            }

            return true;
        }

        internal override IServiceProvider TryGetServiceProvider(IConsole console)
        {
            // Gather all the values supplied by the user in command line
            SourceDir = string.IsNullOrEmpty(SourceDir) ?
                Directory.GetCurrentDirectory() : Path.GetFullPath(SourceDir);
            ManifestDir = string.IsNullOrEmpty(ManifestDir) ? null : Path.GetFullPath(ManifestDir);
            IntermediateDir = string.IsNullOrEmpty(IntermediateDir) ? null : Path.GetFullPath(IntermediateDir);
            DestinationDir = string.IsNullOrEmpty(DestinationDir) ? null : Path.GetFullPath(DestinationDir);
            var buildProperties = ProcessProperties(Properties);

            // NOTE: Order of the following is important. So a command line provided value has higher precedence
            // than the value provided in a configuration file of the repo.
            var config = new ConfigurationBuilder()
                .AddIniFile(Path.Combine(SourceDir, Constants.BuildEnvironmentFileName), optional: true)
                .AddEnvironmentVariables()
                .Add(GetCommandLineConfigSource(buildProperties))
                .Build();

            // Override the GetServiceProvider() call in CommandBase to pass the IConsole instance to
            // ServiceProviderBuilder and allow for writing to the console if needed during this command.
            var serviceProviderBuilder = new ServiceProviderBuilder(LogFilePath, console)
                .ConfigureServices(services =>
                {
                    // Configure Options related services
                    // We first add IConfiguration to DI so that option services like
                    // `DotNetCoreScriptGeneratorOptionsSetup` services can get it through DI and read from the config
                    // and set the options.
                    services
                        .AddSingleton<IConfiguration>(config)
                        .AddOptionsServices()
                        .Configure<BuildScriptGeneratorOptions>(options =>
                        {
                            // These values are not retrieve through the 'config' api since we do not expect
                            // them to be provided by an end user.
                            options.SourceDir = SourceDir;
                            options.IntermediateDir = IntermediateDir;
                            options.DestinationDir = DestinationDir;
                            options.ManifestDir = ManifestDir;
                            options.Properties = buildProperties;
                            options.ScriptOnly = false;
                        });
                });

            return serviceProviderBuilder.Build();
        }

        private CustomConfigurationSource GetCommandLineConfigSource(
            IDictionary<string, string> buildProperties)
        {
            var commandLineConfigSource = new CustomConfigurationSource();
            commandLineConfigSource.Set(SettingsKeys.PlatformName, PlatformName);
            commandLineConfigSource.Set(SettingsKeys.PlatformVersion, PlatformVersion);

            // Set the platform key and version in the format that they are represented in other sources
            // (like environment variables and build.env file).git 
            // This is so that this enables Configuration api to apply the hierarchical config.
            // Example: "--platform python --platform-version 3.6" will win over "PYTHON_VERSION=3.7"
            // in environment variable
            SetPlatformVersion(PlatformName, PlatformVersion);

            commandLineConfigSource.Set(SettingsKeys.CreatePackage, ShouldPackage.ToString());
            commandLineConfigSource.Set(SettingsKeys.RequiredOsPackages, OsRequirements);
            commandLineConfigSource.Set(SettingsKeys.AppType, AppType);

            if (buildProperties != null)
            {
                SetPropertyValueInConfigurationSource(SettingsKeys.Project);
                SetPropertyValueInConfigurationSource(SettingsKeys.PruneDevDependencies);
                SetPropertyValueInConfigurationSource(SettingsKeys.NpmRegistryUrl);
                SetPropertyValueInConfigurationSource(SettingsKeys.PythonVirtualEnvironmentName);
            }

            return commandLineConfigSource;

            void SetPropertyValueInConfigurationSource(string key)
            {
                // Set the platform version only if it is present otherwise we would be overwriting any
                // value that is set by earlier configuration sources.
                if (buildProperties.TryGetValue(key, out var value))
                {
                    commandLineConfigSource.Set(key, value);
                }
            }

            void SetPlatformVersion(string platformName, string platformVersion)
            {
                if (string.IsNullOrEmpty(platformName))
                {
                    return;
                }

                commandLineConfigSource.Set(SettingsKeys.PlatformName, platformName);

                // Set the platform version only if it is present otherwise we would be overwriting any
                // value that is set by earlier configuration sources.
                if (!string.IsNullOrEmpty(platformVersion))
                {
                    platformName = platformName == "nodejs" ? "node" : platformName;
                    var platformVersionKey = $"{platformName}_version".ToUpper();
                    commandLineConfigSource.Set(platformVersionKey, platformVersion);
                }
            }
        }

        private string GetSourceRepoCommitId(IEnvironment env, ISourceRepo repo, ILogger<BuildCommand> logger)
        {
            string commitId = env.GetEnvironmentVariable(ExtVarNames.ScmCommitIdEnvVarName);

            if (string.IsNullOrEmpty(commitId))
            {
                using (var timedEvent = logger.LogTimedEvent("GetGitCommitId"))
                {
                    commitId = repo.GetGitCommitId();
                    timedEvent.AddProperty(nameof(commitId), commitId);
                }
            }

            return commitId;
        }

        private string[] GetEnvVarNames([CanBeNull] IEnvironment env)
        {
            var envVarKeyCollection = env?.GetEnvironmentVariables()?.Keys;
            if (envVarKeyCollection == null)
            {
                return new string[] { };
            }

            string[] envVarNames = new string[envVarKeyCollection.Count];
            envVarKeyCollection.CopyTo(envVarNames, 0);
            return envVarNames;
        }
    }
}