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
                "/diagnostics", "/auth/rotate", "/console-events", "/game-object", "/components",
                "/scene-query", "/component-query", "/serialized-read", "/scene-view", "/capture-scene-view",
                "/runtime-status", "/runtime-query", "/runtime-object", "/profiler-snapshot", "/asset-graph",
                "/create-game-object", "/set-transform", "/add-component", "/set-serialized-property",
                "/reparent", "/rename", "/delete-game-object", "/duplicate-game-object", "/reorder-sibling",
                "/instantiate-prefab", "/prefab-apply", "/prefab-revert",
                "/authoring/validate", "/authoring/batch", "/create-script", "/create-material", "/create-prefab",
                "/animation/create-clip", "/animation/create-controller", "/animation/add-state",
                "/animation/add-transition", "/animation/assign-animator",
                "/shadergraph/create",
                "/menu-item-allowlist", "/execute-menu-item", "/asset-search",
                "/compilation-status", "/run-tests", "/tests-last", "/tests/run", "/tests/status",
                "/tests/wait", "/tests/artifacts", "/assertions/catalog", "/assertions/evaluate",
                "/golden-frame/capture", "/golden-frame/compare", "/golden-frame/approve",
                "/scenario/run", "/scenario/status", "/scenario/wait", "/scenario/report",
                "/metrics", "/perf/probe", "/perf/check-budget", "/capture-game-view",
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

        [Test]
        public void GetCannotInvokeMutatingEndpoint()
        {
            var router = NewRouter();
            var result = router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"Nope\"}", "GET");
            var json = JObject.Parse(result.Body);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("request.method_not_allowed", json["code"].Value<string>());
        }

        [Test]
        public void ReadOnlyPolicyDeniesMutationAndAuditsAttempt()
        {
            var ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 12345, PolicyProfile = "read-only" };
            var router = new ScenePortRouter(ctx);
            var before = ctx.Audit.Snapshot(200).Count;
            var result = router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"Denied\"}", "POST");
            Assert.AreEqual(403, result.StatusCode);
            Assert.AreEqual(before + 1, ctx.Audit.Snapshot(200).Count);
        }

        [Test]
        public void ReadOnlyPostIsNotAudited()
        {
            var ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 12345 };
            var router = new ScenePortRouter(ctx);
            var before = ctx.Audit.Snapshot(200).Count;
            var result = router.DispatchWithStatus("/scene-query", "", "{\"limit\":1}", "POST");
            Assert.AreEqual(200, result.StatusCode);
            Assert.AreEqual(before, ctx.Audit.Snapshot(200).Count);
        }
    }
}
