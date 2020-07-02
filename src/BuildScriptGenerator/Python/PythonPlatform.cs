// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator.Exceptions;
using Microsoft.Oryx.Common.Extensions;
using Microsoft.Oryx.Detector;
using Microsoft.Oryx.Detector.Python;

namespace Microsoft.Oryx.BuildScriptGenerator.Python
{
    /// <summary>
    /// Python Platform.
    /// </summary>
    [BuildProperty(
        VirtualEnvironmentNamePropertyKey,
        "Name of the virtual environment to be created. Defaults to 'pythonenv<Python version>'.")]
    [BuildProperty(
        CompressVirtualEnvPropertyKey,
        "Indicates how and if virtual environment folder should be compressed into a single file in the output " +
        "folder. Options are '" + ZipOption + "', and '" + TarGzOption + "'. Default is to not compress. " +
        "If this option is used, when running the app the virtual environment folder must be extracted from " +
        "this file.")]
    [BuildProperty(
        TargetPackageDirectoryPropertyKey,
        "If provided, packages will be downloaded to the given directory instead of to a virtual environment.")]
    internal class PythonPlatform : IProgrammingPlatform
    {
        /// <summary>
        /// The name of virtual environment.
        /// </summary>
        internal const string VirtualEnvironmentNamePropertyKey = "virtualenv_name";

        /// <summary>
        /// The target package directory.
        /// </summary>
        internal const string TargetPackageDirectoryPropertyKey = "packagedir";

        /// <summary>
        /// The compress virtual environment.
        /// </summary>
        internal const string CompressVirtualEnvPropertyKey = "compress_virtualenv";

        /// <summary>
        /// The zip option.
        /// </summary>
        internal const string ZipOption = "zip";

        /// <summary>
        /// The tar-gz option.
        /// </summary>
        internal const string TarGzOption = "tar-gz";
        private readonly BuildScriptGeneratorOptions _commonOptions;
        private readonly PythonScriptGeneratorOptions _pythonScriptGeneratorOptions;
        private readonly IPythonVersionProvider _versionProvider;
        private readonly ILogger<PythonPlatform> _logger;
        private readonly IPythonPlatformDetector _detector;
        private readonly PythonPlatformInstaller _platformInstaller;

        /// <summary>
        /// Initializes a new instance of the <see cref="PythonPlatform"/> class.
        /// </summary>
        /// <param name="commonOptions">The <see cref="BuildScriptGeneratorOptions"/>.</param>
        /// <param name="pythonScriptGeneratorOptions">The <see cref="PythonScriptGeneratorOptions"/>.</param>
        /// <param name="versionProvider">The Python version provider.</param>
        /// <param name="logger">The logger of Python platform.</param>
        /// <param name="detector">The detector of Python platform.</param>
        /// <param name="platformInstaller">The <see cref="PythonPlatformInstaller"/>.</param>
        public PythonPlatform(
            IOptions<BuildScriptGeneratorOptions> commonOptions,
            IOptions<PythonScriptGeneratorOptions> pythonScriptGeneratorOptions,
            IPythonVersionProvider versionProvider,
            ILogger<PythonPlatform> logger,
            IPythonPlatformDetector detector,
            PythonPlatformInstaller platformInstaller)
        {
            _commonOptions = commonOptions.Value;
            _pythonScriptGeneratorOptions = pythonScriptGeneratorOptions.Value;
            _versionProvider = versionProvider;
            _logger = logger;
            _detector = detector;
            _platformInstaller = platformInstaller;
        }

        /// <inheritdoc/>
        public string Name => PythonConstants.PlatformName;

        public IEnumerable<string> SupportedVersions
        {
            get
            {
                var versionInfo = _versionProvider.GetVersionInfo();
                return versionInfo.SupportedVersions;
            }
        }

        /// <inheritdoc/>
        public PlatformDetectorResult Detect(RepositoryContext context)
        {
            var detectionResult = _detector.Detect(new DetectorContext
            {
                SourceRepo = new Detector.LocalSourceRepo(context.SourceRepo.RootPath),
            });

            if (detectionResult == null)
            {
                return null;
            }

            var version = ResolveVersion(detectionResult.PlatformVersion);
            detectionResult.PlatformVersion = version;
            return detectionResult;
        }

        /// <inheritdoc/>
        public BuildScriptSnippet GenerateBashBuildScriptSnippet(
            BuildScriptGeneratorContext context,
            PlatformDetectorResult detectorResult)
        {
            var manifestFileProperties = new Dictionary<string, string>();

            // Write the platform name and version to the manifest file
            manifestFileProperties[ManifestFilePropertyKeys.PythonVersion] = detectorResult.PlatformVersion;

            var packageDir = GetPackageDirectory(context);
            var virtualEnvName = GetVirtualEnvironmentName(context);

            if (!string.IsNullOrWhiteSpace(packageDir) && !string.IsNullOrWhiteSpace(virtualEnvName))
            {
                throw new InvalidUsageException($"Options '{TargetPackageDirectoryPropertyKey}' and " +
                    $"'{VirtualEnvironmentNamePropertyKey}' are mutually exclusive. Please provide " +
                    $"only the target package directory or virtual environment name.");
            }

            if (string.IsNullOrWhiteSpace(packageDir))
            {
                // If the package directory was not provided, we default to virtual envs
                if (string.IsNullOrWhiteSpace(virtualEnvName))
                {
                    virtualEnvName = GetDefaultVirtualEnvName(detectorResult);
                }

                manifestFileProperties[PythonManifestFilePropertyKeys.VirtualEnvName] = virtualEnvName;
            }
            else
            {
                manifestFileProperties[PythonManifestFilePropertyKeys.PackageDir] = packageDir;
            }

            var virtualEnvModule = string.Empty;
            var virtualEnvCopyParam = string.Empty;

            var pythonVersion = detectorResult.PlatformVersion;
            _logger.LogDebug("Selected Python version: {pyVer}", pythonVersion);

            if (!string.IsNullOrEmpty(pythonVersion) && !string.IsNullOrWhiteSpace(virtualEnvName))
            {
                (virtualEnvModule, virtualEnvCopyParam) = GetVirtualEnvModules(pythonVersion);

                _logger.LogDebug(
                    "Using virtual environment {venv}, module {venvModule}",
                    virtualEnvName,
                    virtualEnvModule);
            }

            GetVirtualEnvPackOptions(
                context,
                virtualEnvName,
                out var compressVirtualEnvCommand,
                out var compressedVirtualEnvFileName);

            if (!string.IsNullOrWhiteSpace(compressedVirtualEnvFileName))
            {
                manifestFileProperties[PythonManifestFilePropertyKeys.CompressedVirtualEnvFile]
                    = compressedVirtualEnvFileName;
            }

            TryLogDependencies(pythonVersion, context.SourceRepo);

            var scriptProps = new PythonBashBuildSnippetProperties(
                virtualEnvironmentName: virtualEnvName,
                virtualEnvironmentModule: virtualEnvModule,
                virtualEnvironmentParameters: virtualEnvCopyParam,
                packagesDirectory: packageDir,
                enableCollectStatic: _pythonScriptGeneratorOptions.EnableCollectStatic,
                compressVirtualEnvCommand: compressVirtualEnvCommand,
                compressedVirtualEnvFileName: compressedVirtualEnvFileName);
            string script = TemplateHelper.Render(
                TemplateHelper.TemplateResource.PythonSnippet,
                scriptProps,
                _logger);

            return new BuildScriptSnippet()
            {
                BashBuildScriptSnippet = script,
                BuildProperties = manifestFileProperties,
            };
        }

        /// <inheritdoc/>
        public bool IsCleanRepo(ISourceRepo repo)
        {
            // TODO: support venvs
            return !repo.DirExists(PythonConstants.DefaultTargetPackageDirectory);
        }

        /// <inheritdoc/>
        public string GenerateBashRunTimeInstallationScript(RunTimeInstallationScriptGeneratorOptions options)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public bool IsEnabled(RepositoryContext ctx)
        {
            return _commonOptions.EnablePythonBuild;
        }

        /// <inheritdoc/>
        public bool IsEnabledForMultiPlatformBuild(RepositoryContext ctx)
        {
            return true;
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetDirectoriesToExcludeFromCopyToBuildOutputDir(
            BuildScriptGeneratorContext context)
        {
            var dirs = new List<string>();
            var virtualEnvName = GetVirtualEnvironmentName(context);
            if (GetVirtualEnvPackOptions(
                context,
                virtualEnvName,
                out _,
                out string compressedFileName))
            {
                dirs.Add(virtualEnvName);
            }
            else if (!string.IsNullOrWhiteSpace(compressedFileName))
            {
                dirs.Add(compressedFileName);
            }

            return dirs;
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetDirectoriesToExcludeFromCopyToIntermediateDir(
            BuildScriptGeneratorContext context)
        {
            var excludeDirs = new List<string>();

            excludeDirs.Add(PythonConstants.DefaultTargetPackageDirectory);

            var virtualEnvName = GetVirtualEnvironmentName(context);
            if (!string.IsNullOrEmpty(virtualEnvName))
            {
                excludeDirs.Add(virtualEnvName);
                excludeDirs.Add(string.Format(PythonConstants.ZipVirtualEnvFileNameFormat, virtualEnvName));
                excludeDirs.Add(string.Format(PythonConstants.TarGzVirtualEnvFileNameFormat, virtualEnvName));
            }

            return excludeDirs;
        }

        public string GetInstallerScriptSnippet(
            BuildScriptGeneratorContext context,
            PlatformDetectorResult detectorResult)
        {
            string installationScriptSnippet = null;
            if (_commonOptions.EnableDynamicInstall)
            {
                _logger.LogDebug("Dynamic install is enabled.");

                if (_platformInstaller.IsVersionAlreadyInstalled(detectorResult.PlatformVersion))
                {
                    _logger.LogDebug(
                       "Python version {version} is already installed. So skipping installing it again.",
                       detectorResult.PlatformVersion);
                }
                else
                {
                    _logger.LogDebug(
                        "Python version {version} is not installed. " +
                        "So generating an installation script snippet for it.",
                        detectorResult.PlatformVersion);

                    installationScriptSnippet = _platformInstaller.GetInstallerScriptSnippet(
                        detectorResult.PlatformVersion);
                }
            }
            else
            {
                _logger.LogDebug("Dynamic install not enabled.");
            }

            return installationScriptSnippet;
        }

        public string ResolveVersion(string versionToResolve)
        {
            var resolvedVersion = GetVersionUsingHierarchicalRules(versionToResolve);
            resolvedVersion = GetMaxSatisfyingVersionAndVerify(resolvedVersion);
            return resolvedVersion;
        }

        private static string GetPackageDirectory(BuildScriptGeneratorContext context)
        {
            string packageDir = null;
            if (context.Properties != null)
            {
                context.Properties.TryGetValue(TargetPackageDirectoryPropertyKey, out packageDir);
            }

            return packageDir;
        }

        private string GetDefaultVirtualEnvName(PlatformDetectorResult detectorResult)
        {
            string pythonVersion = detectorResult.PlatformVersion;
            if (!string.IsNullOrWhiteSpace(pythonVersion))
            {
                var versionSplit = pythonVersion.Split('.');
                if (versionSplit.Length > 1)
                {
                    pythonVersion = $"{versionSplit[0]}.{versionSplit[1]}";
                }
            }

            return $"pythonenv{pythonVersion}";
        }

        private bool GetVirtualEnvPackOptions(
            BuildScriptGeneratorContext context,
            string virtualEnvName,
            out string compressVirtualEnvCommand,
            out string compressedVirtualEnvFileName)
        {
            var isVirtualEnvPackaged = false;
            compressVirtualEnvCommand = null;
            compressedVirtualEnvFileName = null;
            if (context.Properties != null &&
                context.Properties.TryGetValue(CompressVirtualEnvPropertyKey, out string compressVirtualEnvOption))
            {
                // default to tar.gz if the property was provided with no value.
                if (string.IsNullOrEmpty(compressVirtualEnvOption) ||
                    compressVirtualEnvOption.EqualsIgnoreCase(TarGzOption))
                {
                    compressedVirtualEnvFileName = string.Format(
                        PythonConstants.TarGzVirtualEnvFileNameFormat,
                        virtualEnvName);
                    compressVirtualEnvCommand = $"tar -zcf";
                    isVirtualEnvPackaged = true;
                }
                else if (compressVirtualEnvOption.EqualsIgnoreCase(ZipOption))
                {
                    compressedVirtualEnvFileName = string.Format(
                        PythonConstants.ZipVirtualEnvFileNameFormat,
                        virtualEnvName);
                    compressVirtualEnvCommand = $"zip -y -q -r";
                    isVirtualEnvPackaged = true;
                }
            }

            return isVirtualEnvPackaged;
        }

        private (string virtualEnvModule, string virtualEnvCopyParam) GetVirtualEnvModules(string pythonVersion)
        {
            string virtualEnvModule;
            string virtualEnvCopyParam = string.Empty;
            switch (pythonVersion.Split('.')[0])
            {
                case "2":
                    virtualEnvModule = "virtualenv";
                    break;

                case "3":
                    virtualEnvModule = "venv";
                    virtualEnvCopyParam = "--copies";
                    break;

                default:
                    string errorMessage = "Python version '" + pythonVersion + "' is not supported";
                    _logger.LogError(errorMessage);
                    throw new NotSupportedException(errorMessage);
            }

            return (virtualEnvModule, virtualEnvCopyParam);
        }

        private void TryLogDependencies(string pythonVersion, ISourceRepo repo)
        {
            if (!repo.FileExists(PythonConstants.RequirementsFileName))
            {
                return;
            }

            try
            {
                var deps = repo.ReadAllLines(PythonConstants.RequirementsFileName)
                    .Where(line => !line.TrimStart().StartsWith("#"));
                _logger.LogDependencies(PythonConstants.PlatformName, pythonVersion, deps);
            }
            catch (Exception exc)
            {
                _logger.LogWarning(exc, "Exception caught while logging dependencies");
            }
        }

        private string GetVirtualEnvironmentName(BuildScriptGeneratorContext context)
        {
            if (context.Properties == null ||
                !context.Properties.TryGetValue(VirtualEnvironmentNamePropertyKey, out var virtualEnvName))
            {
                virtualEnvName = string.Empty;
            }

            return virtualEnvName;
        }

        private string GetMaxSatisfyingVersionAndVerify(string version)
        {
            var supportedVersions = SupportedVersions;

            // Since our semantic versioning library does not work with Python preview version format, here
            // we do some trivial way of finding the latest version which matches a given runtime version.
            // Preview version of sdks have alphabet letter in the version name. Such as '3.8.0b3', '3.9.0b1',etc.
            var nonPreviewRuntimeVersions = supportedVersions.Where(v => !v.Any(c => char.IsLetter(c)));
            var maxSatisfyingVersion = SemanticVersionResolver.GetMaxSatisfyingVersion(
                version,
                nonPreviewRuntimeVersions);

            // Check if a preview version is available
            if (string.IsNullOrEmpty(maxSatisfyingVersion))
            {
                // Preview versions: '3.8.0b3', '3.9.0b1', etc
                var previewRuntimeVersions = supportedVersions
                    .Where(v => v.Any(c => char.IsLetter(c)))
                    .Where(v => v.StartsWith(version))
                    .OrderByDescending(v => v);
                if (previewRuntimeVersions.Any())
                {
                    maxSatisfyingVersion = previewRuntimeVersions.First();
                }
            }

            if (string.IsNullOrEmpty(maxSatisfyingVersion))
            {
                var exc = new UnsupportedVersionException(
                    PythonConstants.PlatformName,
                    version,
                    supportedVersions);
                _logger.LogError(
                    exc,
                    $"Exception caught, the version '{version}' is not supported for the Python platform.");
                throw exc;
            }

            return maxSatisfyingVersion;
        }

        private string GetVersionUsingHierarchicalRules(string detectedVersion)
        {
            // Explicitly specified version by user wins over detected version
            if (!string.IsNullOrEmpty(_pythonScriptGeneratorOptions.PythonVersion))
            {
                return _pythonScriptGeneratorOptions.PythonVersion;
            }

            // If a version was detected, then use it.
            if (detectedVersion != null)
            {
                return detectedVersion;
            }

            // Fallback to default version
            var versionInfo = _versionProvider.GetVersionInfo();
            return versionInfo.DefaultVersion;
        }
    }
}