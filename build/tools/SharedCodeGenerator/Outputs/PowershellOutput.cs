// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Oryx.SharedCodeGenerator.Outputs
{
    [OutputType("powershell")]
    internal class PowershellOutput : IOutputFile
    {
        private ConstantCollection _collection;
        private string _directory;
        private string _fileNamePrefix;

        public void Initialize(ConstantCollection constantCollection, Dictionary<string, string> typeInfo)
        {
            _collection = constantCollection;
            _directory = typeInfo["directory"];
            _fileNamePrefix = typeInfo["file-name-prefix"];
        }

        public string GetPath()
        {
            var name = _collection.Name.Camelize();
            name = char.ToLowerInvariant(name[0]) + name.Substring(1);
            return Path.Combine(_directory, _fileNamePrefix + name + ".ps1");
        }

        public string GetContent()
        {
            StringBuilder body = new StringBuilder();
            var autoGeneratedMessage = Program.BuildAutogenDisclaimer(_collection.SourcePath);
            body.AppendLine($"# {autoGeneratedMessage}");
            body.AppendLine();
            foreach (var constant in _collection.Constants)
            {
                var name = constant.Key.Replace(ConstantCollection.NameSeparator[0], '_').ToUpper();
                var value = constant.Value.WrapValueInQuotes();
                body.AppendLine($"${name}={value}");
            }

            return body.ToString();
        }
    }
}
