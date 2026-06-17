using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortIdempotencyTests
    {
        [SetUp]
        public void SetUp()
        {
            ScenePortIdempotencyCache.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ScenePortIdempotencyCache.Clear();
        }

        // --- Cache unit behavior ---

        [Test]
        public void StoreThenGetReturnsCachedResult()
        {
            var result = new ScenePortDispatchResult(200, "{\"status\":\"ok\"}");
            ScenePortIdempotencyCache.Store("id-1", result);

            Assert.IsTrue(ScenePortIdempotencyCache.TryGet("id-1", out var got));
            Assert.AreSame(result, got);
        }

        [Test]
        public void UnknownIdMisses()
        {
            ScenePortIdempotencyCache.Store("id-1", new ScenePortDispatchResult(200, "{}"));
            Assert.IsFalse(ScenePortIdempotencyCache.TryGet("id-2", out _));
        }

        [Test]
        public void EmptyIdIsNeverStoredOrFound()
        {
            ScenePortIdempotencyCache.Store("", new ScenePortDispatchResult(200, "{}"));
            ScenePortIdempotencyCache.Store(null, new ScenePortDispatchResult(200, "{}"));
            Assert.AreEqual(0, ScenePortIdempotencyCache.Count);
            Assert.IsFalse(ScenePortIdempotencyCache.TryGet("", out _));
            Assert.IsFalse(ScenePortIdempotencyCache.TryGet(null, out _));
        }

        [Test]
        public void FirstWriteWinsForRepeatedId()
        {
            var first = new ScenePortDispatchResult(200, "{\"v\":1}");
            var second = new ScenePortDispatchResult(200, "{\"v\":2}");
            ScenePortIdempotencyCache.Store("id-1", first);
            ScenePortIdempotencyCache.Store("id-1", second);

            Assert.IsTrue(ScenePortIdempotencyCache.TryGet("id-1", out var got));
            Assert.AreSame(first, got);
            Assert.AreEqual(1, ScenePortIdempotencyCache.Count);
        }

        [Test]
        public void EvictsOldestBeyondCapacity()
        {
            // Capacity is 64; insert 65 and confirm the oldest was evicted.
            for (var i = 0; i < 65; i++)
            {
                ScenePortIdempotencyCache.Store("id-" + i, new ScenePortDispatchResult(200, "{}"));
            }

            Assert.AreEqual(64, ScenePortIdempotencyCache.Count);
            Assert.IsFalse(ScenePortIdempotencyCache.TryGet("id-0", out _), "Oldest entry should be evicted.");
            Assert.IsTrue(ScenePortIdempotencyCache.TryGet("id-64", out _), "Newest entry should remain.");
        }

        // --- Router integration ---

        private static ScenePortRouter NewRouter()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 12345 };
            return new ScenePortRouter(ctx);
        }

        private static int InstanceId(string body)
        {
            return JObject.Parse(body)["object"]["instanceId"].Value<int>();
        }

        [Test]
        public void RepeatedClientRequestIdReturnsCachedResponse()
        {
            var router = NewRouter();
            var body = "{\"name\":\"Dedup\",\"clientRequestId\":\"req-A\"}";

            var first = router.DispatchWithStatus("/create-game-object", "", body, "POST");
            var second = router.DispatchWithStatus("/create-game-object", "", body, "POST");

            // Byte-identical body proves the handler did not re-run; a fresh create would
            // produce a distinct instanceId.
            Assert.AreEqual(first.Body, second.Body);
            Assert.AreEqual(first.StatusCode, second.StatusCode);
            Assert.AreEqual(InstanceId(first.Body), InstanceId(second.Body));
        }

        [Test]
        public void DifferentClientRequestIdRunsHandlerAgain()
        {
            var router = NewRouter();

            var first = router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"Twin\",\"clientRequestId\":\"req-1\"}", "POST");
            var second = router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"Twin\",\"clientRequestId\":\"req-2\"}", "POST");

            // Distinct ids => handler runs twice => two distinct GameObjects (instanceIds differ).
            Assert.AreNotEqual(InstanceId(first.Body), InstanceId(second.Body), "Distinct client-request-ids must each run the handler.");
        }

        [Test]
        public void MissingClientRequestIdIsNotDeduped()
        {
            var router = NewRouter();

            router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"NoId\"}", "POST");
            router.DispatchWithStatus("/create-game-object", "", "{\"name\":\"NoId\"}", "POST");

            Assert.AreEqual(0, ScenePortIdempotencyCache.Count, "Requests without a client-request-id must not populate the cache.");
        }
    }
}
