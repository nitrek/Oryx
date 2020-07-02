﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator;
using Microsoft.Oryx.BuildScriptGenerator.Common;
using Microsoft.Oryx.BuildScriptGeneratorCli.Options;
using Microsoft.Oryx.Detector;

namespace Microsoft.Oryx.BuildScriptGeneratorCli
{
    [Command(Name, Description = "Sets up environment by detecting and installing platforms.")]
    internal class PrepareEnvironmentCommand : CommandBase
    {
        public const string Name = "prep";
        private const string SourceDirectoryTemplate = "-s|--src";
        private const string SkipDetectionTemplate = "--skip-detection";
        private const string PlatformsAndVersionsTemplate = "--platforms-and-versions";
        private const string PlatformsAndVersionsFileTemplate = "--platforms-and-versions-file";

        [Option(
            SourceDirectoryTemplate,
            CommandOptionType.SingleValue,
            Description = "The source directory.")]
        [DirectoryExists]
        public string SourceDir { get; set; }

        [Option(
            SkipDetectionTemplate,
            CommandOptionType.NoValue,
            Description = "Skip detection of platforms and install the requested platforms.")]
        public bool SkipDetection { get; set; }

        [Option(
            PlatformsAndVersionsTemplate,
            CommandOptionType.SingleValue,
            Description =
            "Comma separated values of platforms and versions to be installed. " +
            "Example: dotnet=3.1.200,php=7.4.5,node=2.3")]
        public string PlatformsAndVersions { get; set; }

        [Option(
            PlatformsAndVersionsFileTemplate,
            CommandOptionType.SingleValue,
            Description =
            "A .env file which contains list of platforms and the versions that need to be installed. " +
            "Example: \ndotnet=3.1.200\nphp=7.4.5\nnode=2.3")]
        public string PlatformsAndVersionsFile { get; set; }

        // To enable unit testing
        internal static bool TryValidateSuppliedPlatformsAndVersions(
            IEnumerable<IProgrammingPlatform> availablePlatforms,
            string suppliedPlatformsAndVersions,
            string suppliedPlatformsAndVersionsFile,
            IConsole console,
            out List<PlatformDetectorResult> results)
        {
            results = new List<PlatformDetectorResult>();

            if (string.IsNullOrEmpty(suppliedPlatformsAndVersions)
                && string.IsNullOrEmpty(suppliedPlatformsAndVersionsFile))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(suppliedPlatformsAndVersionsFile)
                && !File.Exists(suppliedPlatformsAndVersionsFile))
            {
                console.WriteErrorLine($"Supplied file '{suppliedPlatformsAndVersionsFile}' does not exist.");
                return false;
            }

            IEnumerable<string> platformsAndVersions;
            if (string.IsNullOrEmpty(suppliedPlatformsAndVersions))
            {
                var lines = File.ReadAllLines(suppliedPlatformsAndVersionsFile);
                platformsAndVersions = lines
                    .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith('#'));
            }
            else
            {
                // Example: python,dotnet=3.1.300, node=12.3, Python=3.7.3
                platformsAndVersions = suppliedPlatformsAndVersions
                    .Trim()
                    .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(nv => nv.Trim());
            }

            var platformNames = availablePlatforms.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var platformNameAndVersion in platformsAndVersions)
            {
                var parts = platformNameAndVersion.Split("=", StringSplitOptions.RemoveEmptyEntries);

                // It is OK to have a platform name without version in which case a default version of the platform
                // is installed.
                string platformName = null;
                string version = null;
                platformName = parts[0].Trim();
                if (parts.Length == 2)
                {
                    version = parts[1].Trim();
                }

                if (!platformNames.ContainsKey(platformName))
                {
                    console.WriteErrorLine(
                        $"Platform name '{platformName}' is not valid. Make sure platform name matches one of the " +
                        $"following names: {string.Join(", ", platformNames.Keys)}");
                    return false;
                }

                var platform = platformNames[platformName];
                var resolvedVersion = platform.ResolveVersion(version);

                var platformDetectorResult = new PlatformDetectorResult
                {
                    Platform = platform.Name,
                    PlatformVersion = resolvedVersion,
                };

                results.Add(platformDetectorResult);
            }

            return true;
        }

        internal override int Execute(IServiceProvider serviceProvider, IConsole console)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PrepareEnvironmentCommand>>();
            var options = serviceProvider.GetRequiredService<IOptions<BuildScriptGeneratorOptions>>().Value;

            var beginningOutputLog = GetBeginningCommandOutputLog();
            console.WriteLine(beginningOutputLog);

            int exitCode;
            using (var timedEvent = logger.LogTimedEvent("EnvSetupCommand"))
            {
                var context = BuildScriptGenerator.CreateContext(serviceProvider, operationId: null);

                IEnumerable<PlatformDetectorResult> detectedPlatforms = null;
                if (SkipDetection)
                {
                    console.WriteLine(
                        $"Skipping platform detection since '{SkipDetectionTemplate}' switch was used...");

                    var platforms = serviceProvider.GetRequiredService<IEnumerable<IProgrammingPlatform>>();
                    if (TryValidateSuppliedPlatformsAndVersions(
                        platforms,
                        PlatformsAndVersions,
                        PlatformsAndVersionsFile,
                        console,
                        out var results))
                    {
                        detectedPlatforms = results;
                    }
                    else
                    {
                        console.WriteErrorLine(
                            $"Invalid value for switch '{PlatformsAndVersionsTemplate}'.");
                        return ProcessConstants.ExitFailure;
                    }
                }
                else
                {
                    var detector = serviceProvider.GetRequiredService<Detector.DefaultPlatformDetector>();
                    detectedPlatforms = detector.GetAllDetectedPlatforms(new DetectorContext
                    {
                        SourceRepo = new Detector.LocalSourceRepo(context.SourceRepo.RootPath),
                    });

                    if (!detectedPlatforms.Any())
                    {
                        return ProcessConstants.ExitFailure;
                    }
                }

                var environmentScriptProvider = serviceProvider.GetRequiredService<PlatformsInstallationScriptProvider>();
                var snippet = environmentScriptProvider.GetBashScriptSnippet(context, detectedPlatforms);

                var scriptBuilder = new StringBuilder()
                    .AppendLine($"#!{FilePaths.Bash}")
                    .AppendLine("set -e")
                    .AppendLine();

                if (!string.IsNullOrEmpty(snippet))
                {
                    scriptBuilder
                        .AppendLine("echo")
                        .AppendLine("echo Setting up environment...")
                        .AppendLine("echo")
                        .AppendLine(snippet)
                        .AppendLine("echo")
                        .AppendLine("echo Done setting up environment.")
                        .AppendLine("echo");
                }

                // Create temporary file to store script
                // Get the path where the generated script should be written into.
                var tempDirectoryProvider = serviceProvider.GetRequiredService<ITempDirectoryProvider>();
                var tempScriptPath = Path.Combine(tempDirectoryProvider.GetTempDirectory(), "setupEnvironment.sh");
                var script = scriptBuilder.ToString();
                File.WriteAllText(tempScriptPath, script);
                timedEvent.AddProperty(nameof(tempScriptPath), tempScriptPath);

                if (DebugMode)
                {
                    console.WriteLine($"Temporary script @ {tempScriptPath}:");
                    console.WriteLine("---");
                    console.WriteLine(scriptBuilder);
                    console.WriteLine("---");
                }

                var environment = serviceProvider.GetRequiredService<IEnvironment>();
                var shellPath = environment.GetEnvironmentVariable("BASH") ?? FilePaths.Bash;

                exitCode = ProcessHelper.RunProcess(
                    shellPath,
                    new[] { tempScriptPath },
                    options.SourceDir,
                    (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            console.WriteLine(args.Data);
                        }
                    },
                    (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            console.Error.WriteLine(args.Data);
                        }
                    },
                    waitTimeForExit: null);
                timedEvent.AddProperty("exitCode", exitCode.ToString());
            }

            return exitCode;
        }

        internal override void ConfigureBuildScriptGeneratorOptions(BuildScriptGeneratorOptions options)
        {
            BuildScriptGeneratorOptionsHelper.ConfigureBuildScriptGeneratorOptions(options, sourceDir: SourceDir);
        }

        internal override IServiceProvider TryGetServiceProvider(IConsole console)
        {
            if (!IsValidInput(console))
            {
                return null;
            }

            // NOTE: Order of the following is important. So a command line provided value has higher precedence
            // than the value provided in a configuration file of the repo.
            var configBuilder = new ConfigurationBuilder();

            if (string.IsNullOrEmpty(PlatformsAndVersionsFile))
            {
                // Gather all the values supplied by the user in command line
                SourceDir = string.IsNullOrEmpty(SourceDir) ?
                    Directory.GetCurrentDirectory() : Path.GetFullPath(SourceDir);
                configBuilder.AddIniFile(Path.Combine(SourceDir, Constants.BuildEnvironmentFileName), optional: true);
            }
            else
            {
                string versionsFilePath;
                if (PlatformsAndVersionsFile.StartsWith("/"))
                {
                    versionsFilePath = Path.GetFullPath(PlatformsAndVersionsFile);
                }
                else
                {
                    versionsFilePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        Path.GetFullPath(PlatformsAndVersionsFile));
                }

                if (!File.Exists(versionsFilePath))
                {
                    throw new FileNotFoundException(
                        $"Could not find the file provided to the '{PlatformsAndVersionsFileTemplate}' switch.",
                        versionsFilePath);
                }

                configBuilder.AddIniFile(versionsFilePath, optional: false);
            }

            configBuilder
                .AddEnvironmentVariables()
                .Add(GetCommandLineConfigSource());

            var config = configBuilder.Build();

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
                        });
                });

            return serviceProviderBuilder.Build();
        }

        private bool IsValidInput(IConsole console)
        {
            if (!SkipDetection && string.IsNullOrEmpty(SourceDir))
            {
                console.WriteErrorLine("Source directory is required.");
                return false;
            }

            if (SkipDetection
                && string.IsNullOrEmpty(PlatformsAndVersions)
                && string.IsNullOrEmpty(PlatformsAndVersionsFile))
            {
                console.WriteErrorLine("Platform names and versions are required.");
                return false;
            }

            return true;
        }

        private CustomConfigurationSource GetCommandLineConfigSource()
        {
            var commandLineConfigSource = new CustomConfigurationSource();
            return commandLineConfigSource;
        }
    }
}
