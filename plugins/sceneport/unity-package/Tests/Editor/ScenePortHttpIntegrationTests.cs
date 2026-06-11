using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace ScenePort.McpBridge.Editor.Tests
{
    // End-to-end through the real auto-started bridge: HttpListener → gate → main-thread
    // pump → router → JSON. Must be [UnityTest] coroutines — a synchronous blocking wait
    // would deadlock the pump (it runs on EditorApplication.update between yields).
    internal sealed class ScenePortHttpIntegrationTests
    {
        private static readonly HttpClient Client = new HttpClient();

        private static string BaseUrl => "http://127.0.0.1:" + ScenePortBridge.BoundPort;

        private static HttpRequestMessage Authed(HttpMethod method, string path, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path) { Content = content };
            request.Headers.Add(ScenePortAuth.TokenHeader, ScenePortBridge.CurrentToken);
            return request;
        }

        private static IEnumerator Await(Task task, int timeoutMs = 10000)
        {
            // Wall-clock deadline, not a frame count: in batchmode frames advance far faster
            // than a TCP round-trip, so a frame budget would expire before the socket responds.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                yield return null;
            }

            Assert.IsTrue(task.IsCompleted, "HTTP request timed out.");
            if (task.IsFaulted)
            {
                throw task.Exception;
            }
        }

        [UnityTest]
        public IEnumerator HealthRoundTripsWithoutToken()
        {
            Assert.IsTrue(ScenePortBridge.IsRunning, "Bridge should auto-start.");

            // /health is intentionally exempt from auth (reachability handshake + curl debugging).
            var task = Client.GetStringAsync(BaseUrl + "/health");
            yield return Await(task);

            var json = JObject.Parse(task.Result);
            Assert.AreEqual("ok", json["status"].Value<string>());
            Assert.AreEqual("sceneport", json["bridge"].Value<string>());
            Assert.AreEqual(ScenePortBridge.BoundPort, json["port"].Value<int>());
            Assert.AreEqual(ScenePortProtocol.Version, json["protocolVersion"].Value<int>());
            Assert.AreEqual(ScenePortProtocol.CapabilitiesHash, json["capabilitiesHash"].Value<string>());
            Assert.IsFalse(string.IsNullOrEmpty(json["ownerLeaseId"].Value<string>()));
            Assert.IsFalse(string.IsNullOrEmpty(json["heartbeatUtc"].Value<string>()));
        }

        [UnityTest]
        public IEnumerator CapabilitiesRoundTripsWithToken()
        {
            var task = Client.SendAsync(Authed(HttpMethod.Get, "/capabilities"));
            yield return Await(task);

            var read = task.Result.Content.ReadAsStringAsync();
            yield return Await(read);
            var json = JObject.Parse(read.Result);
            Assert.AreEqual("ok", json["status"].Value<string>());
            Assert.AreEqual("sceneport", json["bridge"].Value<string>());
            Assert.AreEqual(ScenePortProtocol.Version, json["protocolVersion"].Value<int>());
        }

        [UnityTest]
        public IEnumerator AuthedCreateGameObjectRoundTrips()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var content = new StringContent("{\"name\":\"HttpProbe\"}", Encoding.UTF8, "application/json");
            var task = Client.SendAsync(Authed(HttpMethod.Post, "/create-game-object", content));
            yield return Await(task);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, task.Result.StatusCode);
            Assert.IsNotNull(GameObject.Find("HttpProbe"));
        }

        [UnityTest]
        public IEnumerator MalformedJsonPostIsRejectedAndDoesNotMutate()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var content = new StringContent("{broken", Encoding.UTF8, "application/json");
            var task = Client.SendAsync(Authed(HttpMethod.Post, "/create-game-object", content));
            yield return Await(task);

            Assert.AreEqual((System.Net.HttpStatusCode)400, task.Result.StatusCode);
            var read = task.Result.Content.ReadAsStringAsync();
            yield return Await(read);
            var body = JObject.Parse(read.Result);
            Assert.AreEqual("request.invalid", body["code"].Value<string>());
            Assert.IsNull(GameObject.Find("ScenePort GameObject"));
        }

        [UnityTest]
        public IEnumerator AuditLogRecordsAuthedMutation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var create = Client.SendAsync(Authed(HttpMethod.Post, "/create-game-object",
                new StringContent("{\"name\":\"AuditProbe\"}", Encoding.UTF8, "application/json")));
            yield return Await(create);
            Assert.AreEqual(System.Net.HttpStatusCode.OK, create.Result.StatusCode);

            var audit = Client.SendAsync(Authed(HttpMethod.Get, "/audit-log?limit=5"));
            yield return Await(audit);

            var read = audit.Result.Content.ReadAsStringAsync();
            yield return Await(read);
            var body = JObject.Parse(read.Result);
            Assert.AreEqual("ok", body["status"].Value<string>());
            Assert.IsTrue(body["entries"].HasValues, "Expected at least one audit entry.");
            var last = body["entries"].Last;
            Assert.AreEqual("/create-game-object", last["endpoint"].Value<string>());
            StringAssert.Contains("AuditProbe", last["summary"].Value<string>());
        }

        [UnityTest]
        public IEnumerator MissingTokenIsRejected()
        {
            var task = Client.GetAsync(BaseUrl + "/scene");
            yield return Await(task);
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, task.Result.StatusCode);
            var read = task.Result.Content.ReadAsStringAsync();
            yield return Await(read);
            var body = JObject.Parse(read.Result);
            Assert.AreEqual("bridge.unauthorized", body["code"].Value<string>());
        }

        [UnityTest]
        public IEnumerator HealthEndpointSurvivesThousandRequestStress()
        {
            for (var i = 0; i < 1000; i++)
            {
                var task = Client.GetStringAsync(BaseUrl + "/health");
                yield return Await(task, 30000);
                var json = JObject.Parse(task.Result);
                Assert.AreEqual("ok", json["status"].Value<string>());
            }
        }

        // The core CSRF defense: a browser-style POST (Origin header present) must be rejected
        // before any editor mutation, even with a valid token attached.
        [UnityTest]
        public IEnumerator CrossOriginPostIsRejectedAndDoesNotMutate()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(ScenePortBridge.AuthRequired, "Auth must be on for this test to be meaningful.");

            var request = Authed(HttpMethod.Post, "/create-game-object",
                new StringContent("{\"name\":\"CsrfProbe\"}", Encoding.UTF8, "application/json"));
            request.Headers.Add("Origin", "http://evil.example");

            var task = Client.SendAsync(request);
            yield return Await(task);

            Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, task.Result.StatusCode);
            Assert.IsNull(GameObject.Find("CsrfProbe"), "Cross-origin request must not mutate the scene.");
        }

        [UnityTest]
        public IEnumerator ChunkedBodyOverLimitIsRejectedAndDoesNotMutate()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var body = "{\"name\":\"OversizeProbe\",\"padding\":\"" + new string('x', 1024 * 1024 + 1) + "\"}";
            var task = Client.SendAsync(Authed(HttpMethod.Post, "/create-game-object", new StreamingJsonContent(body)));
            yield return Await(task);

            Assert.AreEqual((System.Net.HttpStatusCode)413, task.Result.StatusCode);
            Assert.IsNull(GameObject.Find("OversizeProbe"), "Oversized chunked request must not mutate the scene.");
        }

        private sealed class StreamingJsonContent : HttpContent
        {
            private readonly byte[] bytes;

            internal StreamingJsonContent(string body)
            {
                bytes = Encoding.UTF8.GetBytes(body);
                Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return stream.WriteAsync(bytes, 0, bytes.Length);
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }
    }
}
