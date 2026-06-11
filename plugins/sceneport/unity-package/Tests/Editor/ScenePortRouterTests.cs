using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortRouterTests
    {
        private static ScenePortRouter NewRouter()
        {
            var ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 12345 };
            return new ScenePortRouter(ctx);
        }

        [Test]
        public void AllDocumentedEndpointsAreRouted()
        {
            var router = NewRouter();
            var endpoints = new[]
            {
                "/health", "/capabilities", "/scene", "/scene-hierarchy", "/selection", "/console",
                "/game-object", "/components", "/create-game-object", "/set-transform",
                "/add-component", "/set-serialized-property", "/asset-search",
                "/compilation-status", "/run-tests", "/tests-last", "/capture-game-view",
                "/play-mode", "/packages", "/playtest/start", "/playtest/stop",
                "/playtest/status", "/playtest/report", "/playtest/capture-frame",
                "/playtest/send-key", "/playtest/send-click", "/audit-log",
            };

            foreach (var endpoint in endpoints)
            {
                Assert.IsTrue(router.HasRoute(endpoint), "Missing route: " + endpoint);
            }
        }

        [Test]
        public void EmptyPathNormalizesToHealth()
        {
            Assert.AreEqual("/health", ScenePortRouter.Normalize(""));
            Assert.AreEqual("/health", ScenePortRouter.Normalize("/"));
            Assert.AreEqual("/health", ScenePortRouter.Normalize(null));
        }

        [Test]
        public void TrailingSlashTrimmed()
        {
            Assert.AreEqual("/scene", ScenePortRouter.Normalize("/scene/"));
        }

        [Test]
        public void UnknownEndpointReturnsErrorEnvelope()
        {
            var router = NewRouter();
            var json = JObject.Parse(router.Dispatch("/nope", "", null));
            Assert.AreEqual("error", json["status"].Value<string>());
            StringAssert.Contains("Unknown endpoint", json["error"].Value<string>());
        }

        [Test]
        public void HealthDispatchReportsContextValues()
        {
            var router = NewRouter();
            var json = JObject.Parse(router.Dispatch("/health", "", null));
            Assert.AreEqual("ok", json["status"].Value<string>());
            Assert.AreEqual("sceneport", json["bridge"].Value<string>());
            Assert.AreEqual(12345, json["port"].Value<int>());
            Assert.AreEqual("test", json["version"].Value<string>());
            Assert.AreEqual(ScenePortProtocol.Version, json["protocolVersion"].Value<int>());
            Assert.AreEqual(ScenePortProtocol.CapabilitiesHash, json["capabilitiesHash"].Value<string>());
        }

        [Test]
        public void CapabilitiesDispatchReportsProtocol()
        {
            var router = NewRouter();
            var json = JObject.Parse(router.Dispatch("/capabilities", "", null));
            Assert.AreEqual("ok", json["status"].Value<string>());
            Assert.AreEqual("sceneport", json["bridge"].Value<string>());
            Assert.AreEqual(ScenePortProtocol.Version, json["protocolVersion"].Value<int>());
            Assert.AreEqual(ScenePortProtocol.CapabilitiesHash, json["capabilitiesHash"].Value<string>());
        }
    }
}
