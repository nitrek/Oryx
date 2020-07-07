﻿// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Oryx.BuildScriptGeneratorCli
{
    internal static class OptionTemplates
    {
        public const string Platform = "--platform <name>";
        public const string PlatformVersion = "--platform-version <version>";

        [Obsolete("Previous name for --platform")]
        public const string Language = "-l|--language <name>";
        [Obsolete("Previous name for --platform-version")]
        public const string LanguageVersion = "--language-version <version>";

        public const string Property = "-p|--property <key-value>";
        public const string ManifestDir = "--manifest-dir <directory-path>";
        public const string AppType = "--apptype <application-type>";
        public const string DynamicInstallRootDir = "--dynamic-install-root-dir <dir-path>";
    }
}
