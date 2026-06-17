using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortSetupTests
    {
        private const string ProjectPath = "/Users/dev/MyUnityGame";

        [Test]
        public void ClaudeAddCommandHasExpectedPrefix()
        {
            var command = ScenePortSetup.ClaudeAddCommand(ProjectPath);
            StringAssert.Contains("claude mcp add-json sceneport", command);
        }

        [Test]
        public void ClaudeAddCommandEmbedsParsableJson()
        {
            var command = ScenePortSetup.ClaudeAddCommand(ProjectPath);
            var start = command.IndexOf('{');
            var end = command.LastIndexOf('}');
            Assert.Greater(end, start, "Command should contain a JSON object: " + command);

            var json = JObject.Parse(command.Substring(start, end - start + 1));
            Assert.AreEqual("npx", json["command"].Value<string>());
            Assert.AreEqual("sceneport-mcp", json["args"][1].Value<string>());
            Assert.AreEqual(ProjectPath, json["env"]["SCENEPORT_PROJECT_PATH"].Value<string>());
        }

        [Test]
        public void NpxServerConfigJsonParsesAndContainsCoreFields()
        {
            var json = JObject.Parse(ScenePortSetup.NpxServerConfigJson(ProjectPath));
            Assert.AreEqual("npx", json["command"].Value<string>());
            Assert.AreEqual("-y", json["args"][0].Value<string>());
            Assert.AreEqual("sceneport-mcp", json["args"][1].Value<string>());
            Assert.AreEqual(ProjectPath, json["env"]["SCENEPORT_PROJECT_PATH"].Value<string>());
        }

        [Test]
        public void ClaudeConfigJsonHasMcpServersMap()
        {
            var json = JObject.Parse(ScenePortSetup.ClaudeConfigJson(ProjectPath));
            var server = json["mcpServers"]["sceneport"];
            Assert.IsNotNull(server, "Expected mcpServers.sceneport entry");
            Assert.AreEqual("npx", server["command"].Value<string>());
            Assert.AreEqual("sceneport-mcp", server["args"][1].Value<string>());
            Assert.AreEqual(ProjectPath, server["env"]["SCENEPORT_PROJECT_PATH"].Value<string>());
        }

        [Test]
        public void CodexConfigTomlHasServerTableAndPath()
        {
            var toml = ScenePortSetup.CodexConfigToml(ProjectPath);
            StringAssert.Contains("[mcp_servers.sceneport]", toml);
            StringAssert.Contains("command = \"npx\"", toml);
            StringAssert.Contains("sceneport-mcp", toml);
            StringAssert.Contains("[mcp_servers.sceneport.env]", toml);
            StringAssert.Contains("SCENEPORT_PROJECT_PATH = \"" + ProjectPath + "\"", toml);
        }

        [Test]
        public void CodexConfigTomlEscapesBackslashesAndQuotes()
        {
            var toml = ScenePortSetup.CodexConfigToml("C:\\Users\\dev\\My \"Game\"");
            StringAssert.Contains("C:\\\\Users\\\\dev\\\\My \\\"Game\\\"", toml);
        }

        [Test]
        public void UrlForPortFormatsLoopback()
        {
            Assert.AreEqual("http://127.0.0.1:38987", ScenePortSetup.UrlForPort(38987));
            Assert.AreEqual(string.Empty, ScenePortSetup.UrlForPort(0));
        }

        [Test]
        public void DoctorCommandUsesNpxWithJsonFlag()
        {
            var command = ScenePortSetup.DoctorCommand();
            Assert.AreEqual("npx", command.FileName);
            CollectionAssert.Contains(command.Args, "sceneport-mcp");
            CollectionAssert.Contains(command.Args, "doctor");
            CollectionAssert.Contains(command.Args, "--json");
        }

        [Test]
        public void BuildStatusFromNullInfoIsNotRunning()
        {
            var model = ScenePortSetup.BuildStatus(null, ProjectPath, true);
            Assert.IsFalse(model.Running);
            Assert.AreEqual(0, model.Port);
            Assert.AreEqual(ProjectPath, model.ProjectPath);
            Assert.IsTrue(model.AuthRequired);
            Assert.AreEqual("none", model.Source);
        }

        [Test]
        public void BuildStatusFromInfoPopulatesModel()
        {
            var info = new ScenePortDiscoveryFile.BridgeInfo
            {
                port = 38990,
                url = "http://127.0.0.1:38990",
                tokenFingerprint = "abcd1234",
                projectPath = ProjectPath,
                projectName = "MyUnityGame",
                unityVersion = "6000.0.0f1",
                policyProfile = "full-safe-local",
            };

            var model = ScenePortSetup.BuildStatus(info, null, false);
            Assert.IsTrue(model.Running);
            Assert.AreEqual(38990, model.Port);
            Assert.AreEqual("http://127.0.0.1:38990", model.Url);
            Assert.AreEqual("abcd1234", model.TokenFingerprint);
            Assert.AreEqual("MyUnityGame", model.ProjectName);
            Assert.AreEqual("6000.0.0f1", model.UnityVersion);
            Assert.AreEqual("full-safe-local", model.PolicyProfile);
            Assert.AreEqual("discovery", model.Source);
        }

        [Test]
        public void BuildStatusDerivesUrlWhenMissing()
        {
            var info = new ScenePortDiscoveryFile.BridgeInfo { port = 38991, url = null };
            var model = ScenePortSetup.BuildStatus(info, ProjectPath, false);
            Assert.AreEqual("http://127.0.0.1:38991", model.Url);
        }
    }
}
