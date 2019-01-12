﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// --------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Oryx.BuildScriptGenerator.DotnetCore
{
    internal class DotnetCoreLanguageDetector : ILanguageDetector
    {
        private readonly IDotnetCoreVersionProvider _versionProvider;
        private readonly DotnetCoreScriptGeneratorOptions _scriptGeneratorOptions;
        private readonly ILogger<DotnetCoreLanguageDetector> _logger;

        public DotnetCoreLanguageDetector(
            IDotnetCoreVersionProvider versionProvider,
            IOptions<DotnetCoreScriptGeneratorOptions> options,
            ILogger<DotnetCoreLanguageDetector> logger)
        {
            _versionProvider = versionProvider;
            _scriptGeneratorOptions = options.Value;
            _logger = logger;
        }

        public LanguageDetectorResult Detect(ISourceRepo sourceRepo)
        {
            var projectFile = sourceRepo
                .EnumerateFiles($"*.{DotnetCoreConstants.ProjectFileExtensionName}", searchSubDirectories: false)
                .FirstOrDefault();

            if (projectFile == null)
            {
                _logger.LogDebug(
                    $"Could not find file with extension '{DotnetCoreConstants.ProjectFileExtensionName}' in repo");
                return null;
            }

            var projectFileDoc = XDocument.Load(new StringReader(sourceRepo.ReadFile(projectFile)));
            var targetFrameworkElement = projectFileDoc.XPathSelectElement("/Project/PropertyGroup/TargetFramework");
            var targetFramework = targetFrameworkElement?.Value;
            if (string.IsNullOrEmpty(targetFramework))
            {
                _logger.LogDebug(
                    $"Could not find 'TargetFramework' element in the project file '{projectFile}'.");
                return null;
            }

            // If a repo explicitly specifies an sdk version, then just use it as it is.
            string languageVersion = null;
            if (sourceRepo.FileExists(DotnetCoreConstants.GlobalJsonFileName))
            {
                var globalJson = GetGlobalJsonObject(sourceRepo);
                var sdkVersion = globalJson?.sdk?.version?.Value as string;
                if (sdkVersion != null)
                {
                    languageVersion = sdkVersion;
                }
            }

            if (string.IsNullOrEmpty(languageVersion))
            {
                languageVersion = DetermineSdkVersion(targetFramework);
            }

            if (languageVersion == null)
            {
                _logger.LogDebug(
                    $"Could not find a {DotnetCoreConstants.LanguageName} version corresponding to 'TargetFramework'" +
                    $" '{targetFramework}'.");
                return null;
            }

            return new LanguageDetectorResult
            {
                Language = DotnetCoreConstants.LanguageName,
                LanguageVersion = languageVersion
            };
        }

        internal string DetermineSdkVersion(string targetFramework)
        {
            switch (targetFramework)
            {
                case DotnetCoreConstants.NetCoreApp10:
                case DotnetCoreConstants.NetCoreApp11:
                    return "1.1";

                case DotnetCoreConstants.NetCoreApp20:
                case DotnetCoreConstants.NetCoreApp21:
                    return "2.1";

                case DotnetCoreConstants.NetCoreApp22:
                    return "2.2";
            }

            return null;
        }

        private dynamic GetGlobalJsonObject(ISourceRepo sourceRepo)
        {
            dynamic globalJson = null;
            try
            {
                var jsonContent = sourceRepo.ReadFile(DotnetCoreConstants.GlobalJsonFileName);
                globalJson = JsonConvert.DeserializeObject(jsonContent);
            }
            catch (Exception ex)
            {
                // We just ignore errors, so we leave malformed package.json
                // files for node.js to handle, not us. This prevents us from
                // erroring out when node itself might be able to tolerate some errors
                // in the package.json file.
                _logger.LogError(
                    ex,
                    $"An error occurred while trying to deserialize {DotnetCoreConstants.GlobalJsonFileName}");
            }

            return globalJson;
        }
    }
}