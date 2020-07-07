﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Oryx.BuildScriptGenerator.Common;

namespace Microsoft.Oryx.BuildScriptGenerator.Hugo
{
    public class HugoPlatformInstaller : PlatformInstallerBase
    {
        public HugoPlatformInstaller(
            IOptions<BuildScriptGeneratorOptions> commonOptions,
            ILoggerFactory loggerFactory)
            : base(commonOptions, loggerFactory)
        {
        }

        public virtual string GetInstallerScriptSnippet(string version)
        {
            var tarFile = HugoConstants.TarFileNameFormat.Replace("#VERSION#", version);
            var downloadUrl = HugoConstants.InstallationUrlFormat
                .Replace("#VERSION#", version)
                .Replace("#TAR_FILE#", tarFile);
            var platformName = HugoConstants.PlatformName;
            var versionDirInTemp = $"{_commonOptions.DynamicInstallRootDir}/{platformName}/{version}";

            var snippet = new StringBuilder();
            snippet
                .AppendLine()
                .AppendLine("PLATFORM_SETUP_START=$SECONDS")
                .AppendLine("echo")
                .AppendLine(
                $"echo Downloading and extracting {platformName} version '{version}' to {versionDirInTemp}...")
                .AppendLine($"rm -rf {versionDirInTemp}")
                .AppendLine($"mkdir -p {versionDirInTemp}")
                .AppendLine($"cd {versionDirInTemp}")
                .AppendLine("PLATFORM_BINARY_DOWNLOAD_START=$SECONDS")
                .AppendLine($"curl -fsSLO --compressed \"{downloadUrl}\" >/dev/null 2>&1")
                .AppendLine("PLATFORM_BINARY_DOWNLOAD_ELAPSED_TIME=$(($SECONDS - $PLATFORM_BINARY_DOWNLOAD_START))")
                .AppendLine("echo \"Downloaded in $PLATFORM_BINARY_DOWNLOAD_ELAPSED_TIME sec(s).\"")
                .AppendLine("echo Extracting contents...")
                .AppendLine($"tar -xzf {tarFile} -C .")
                .AppendLine($"rm -f {tarFile}")
                .AppendLine("PLATFORM_SETUP_ELAPSED_TIME=$(($SECONDS - $PLATFORM_SETUP_START))")
                .AppendLine("echo \"Done in $PLATFORM_SETUP_ELAPSED_TIME sec(s).\"")
                .AppendLine("echo")

                // Write out a sentinel file to indicate downlaod and extraction was successful
                .AppendLine($"echo > {versionDirInTemp}/{SdkStorageConstants.SdkDownloadSentinelFileName}");
            return snippet.ToString();
        }

        public virtual bool IsVersionAlreadyInstalled(string version)
        {
            return IsVersionInstalled(
                version,
                builtInDir: HugoConstants.InstalledHugoVersionsDir,
                dynamicInstallDir: $"{_commonOptions.DynamicInstallRootDir}/hugo");
        }
    }
}
