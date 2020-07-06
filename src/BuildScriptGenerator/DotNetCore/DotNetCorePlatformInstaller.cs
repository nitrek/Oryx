// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator.Common;

namespace Microsoft.Oryx.BuildScriptGenerator.DotNetCore
{
    public class DotNetCorePlatformInstaller : PlatformInstallerBase
    {
        private readonly IDotNetCoreVersionProvider _versionProvider;
        private readonly string _dynamicDotNetCoreRuntimeVersionsInstallDir;
        private readonly string _dynamicDotNetCoreSdkVersionsInstallDir;

        public DotNetCorePlatformInstaller(
            IOptions<BuildScriptGeneratorOptions> cliOptions,
            IDotNetCoreVersionProvider versionProvider,
            ILoggerFactory loggerFactory)
            : base(cliOptions, loggerFactory)
        {
            _versionProvider = versionProvider;
            _dynamicDotNetCoreRuntimeVersionsInstallDir =
                $"{_commonOptions.DynamicInstallRootDir}/{DotNetCoreConstants.PlatformName}/runtimes";
            _dynamicDotNetCoreSdkVersionsInstallDir =
                $"{_commonOptions.DynamicInstallRootDir}/{DotNetCoreConstants.PlatformName}/sdks";
        }

        public virtual string GetInstallerScriptSnippet(string runtimeVersion, string globalJsonSdkVersion)
        {
            string sdkVersion;
            if (string.IsNullOrEmpty(globalJsonSdkVersion))
            {
                var versionMap = _versionProvider.GetSupportedVersions();
                sdkVersion = versionMap[runtimeVersion];
                _logger.LogDebug(
                    "Generating installation script for sdk version {sdkVersion} based on " +
                    "runtime version {runtimeVersion}",
                    sdkVersion,
                    runtimeVersion);
            }
            else
            {
                sdkVersion = globalJsonSdkVersion;
                _logger.LogDebug(
                    "Generating installation script for sdk version {sdkVersion} based on global.json file.",
                    sdkVersion);
            }

            var dirToInstall = $"{_dynamicDotNetCoreSdkVersionsInstallDir}/{sdkVersion}";
            var sentinelFileDir = $"{_dynamicDotNetCoreRuntimeVersionsInstallDir}/{runtimeVersion}";

            var sdkInstallerScript = GetInstallerScriptSnippet(
                DotNetCoreConstants.PlatformName,
                sdkVersion,
                dirToInstall);

            // Create the following structure so that 'benv' tool can understand it as it already does.
            var scriptBuilder = new StringBuilder();
            scriptBuilder
            .AppendLine(sdkInstallerScript)
            .AppendLine($"mkdir -p {_dynamicDotNetCoreRuntimeVersionsInstallDir}/{runtimeVersion}")
            .AppendLine(
                $"echo '{sdkVersion}' > {_dynamicDotNetCoreRuntimeVersionsInstallDir}/{runtimeVersion}/sdkVersion.txt")

            // Write out a sentinel file to indicate downlaod and extraction was successful
            .AppendLine($"echo > {sentinelFileDir}/{SdkStorageConstants.SdkDownloadSentinelFileName}");
            return scriptBuilder.ToString();
        }

        public virtual bool IsVersionAlreadyInstalled(string runtimeVersion, string globalJsonSdkVersion)
        {
            if (string.IsNullOrEmpty(globalJsonSdkVersion))
            {
                return IsVersionInstalled(
                    runtimeVersion,
                    builtInDir: DotNetCoreConstants.DefaultDotNetCoreRuntimeVersionsInstallDir,
                    _dynamicDotNetCoreRuntimeVersionsInstallDir);
            }
            else
            {
                return IsVersionInstalled(
                    globalJsonSdkVersion,
                    builtInDir: DotNetCoreConstants.DefaultDotNetCoreSdkVersionsInstallDir,
                    _dynamicDotNetCoreSdkVersionsInstallDir);
            }
        }
    }
}
