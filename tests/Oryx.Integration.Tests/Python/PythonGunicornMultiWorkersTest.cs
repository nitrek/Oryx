// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------
using System.Threading.Tasks;
using Microsoft.Oryx.BuildScriptGenerator.Python;
using Microsoft.Oryx.Common;
using Microsoft.Oryx.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Oryx.Integration.Tests
{
    [Trait("category", "python")]
    public class PythonGunicornMultiWorkersTest : PythonEndToEndTestsBase
    {
        public PythonGunicornMultiWorkersTest(ITestOutputHelper output, TestTempDirTestFixture testTempDirTestFixture)
            : base(output, testTempDirTestFixture)
        {
        }

        [Fact]
        public async Task CanBuildAndRunPythonApp_UsingGunicornMultipleWorkers()
        {
            // Arrange
            var appName = "django-app";
            var volume = CreateAppVolume(appName);
            var appDir = volume.ContainerDir;
            var buildScript = new ShellScriptBuilder()
                .SetEnvironmentVariable(ExtVarNames.PythonEnableGunicornMultiWorkersEnvVarName, true.ToString())
                .AddCommand($"oryx build {appDir} --platform {PythonConstants.PlatformName} --platform-version 3.7")
                .SetEnvironmentVariable(ExtVarNames.PythonEnableGunicornMultiWorkersEnvVarName, false.ToString())
                .ToString();

            var runScript = new ShellScriptBuilder()
                .AddCommand($"cd {appDir}")
                .SetEnvironmentVariable(ExtVarNames.PythonEnableGunicornMultiWorkersEnvVarName, true.ToString())
                .AddCommand($"oryx create-script -appPath {appDir} -bindPort {ContainerPort}")
                .AddCommand(DefaultStartupFilePath)
                .SetEnvironmentVariable(ExtVarNames.PythonEnableGunicornMultiWorkersEnvVarName, false.ToString())
                .ToString();

            await EndToEndTestHelper.BuildRunAndAssertAppAsync(
                appName,
                _output,
                volume,
                "/bin/bash",
                new[]
                {
                    "-c",
                    buildScript
                },
                _imageHelper.GetRuntimeImage("python", "3.7"),
                ContainerPort,
                "/bin/bash",
                new[]
                {
                    "-c",
                    runScript
                },
                async (hostPort) =>
                {
                    var data = await GetResponseDataAsync($"http://localhost:{hostPort}/uservoice/");
                    Assert.Contains("Hello, World!", data);
                });
        }
    }
}