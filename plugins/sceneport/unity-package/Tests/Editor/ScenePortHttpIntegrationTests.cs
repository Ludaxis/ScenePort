using System.Collections;
using System.Net.Http;
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
        public IEnumerator MissingTokenIsRejected()
        {
            var task = Client.GetAsync(BaseUrl + "/scene");
            yield return Await(task);
            Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, task.Result.StatusCode);
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
    }
}
