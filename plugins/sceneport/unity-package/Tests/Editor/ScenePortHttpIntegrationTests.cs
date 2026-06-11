using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace ScenePort.McpBridge.Editor.Tests
{
    // End-to-end through the real auto-started bridge: HttpListener → main-thread pump →
    // router → JSON. Must be [UnityTest] coroutines — a synchronous blocking wait would
    // deadlock the pump (it runs on EditorApplication.update between yields).
    internal sealed class ScenePortHttpIntegrationTests
    {
        private static readonly HttpClient Client = new HttpClient();

        private static string BaseUrl => "http://127.0.0.1:" + ScenePortBridge.BoundPort;

        private static IEnumerator Await(Task task, int timeoutMs = 10000)
        {
            // Wall-clock deadline, not a frame count: in batchmode frames advance far faster
            // than a TCP round-trip, so a frame budget would expire before the socket responds.
            // Each yield still lets EditorApplication.update drive the main-thread pump.
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
        public IEnumerator HealthRoundTripsThroughHttp()
        {
            Assert.IsTrue(ScenePortBridge.IsRunning, "Bridge should auto-start.");

            var task = Client.GetStringAsync(BaseUrl + "/health");
            yield return Await(task);

            var json = JObject.Parse(task.Result);
            Assert.AreEqual("ok", json["status"].Value<string>());
            Assert.AreEqual("sceneport", json["bridge"].Value<string>());
            Assert.AreEqual(ScenePortBridge.BoundPort, json["port"].Value<int>());
        }

        [UnityTest]
        public IEnumerator CreateGameObjectRoundTripsThroughHttp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var content = new StringContent("{\"name\":\"HttpProbe\"}", Encoding.UTF8, "application/json");
            var task = Client.PostAsync(BaseUrl + "/create-game-object", content);
            yield return Await(task);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, task.Result.StatusCode);
            Assert.IsNotNull(GameObject.Find("HttpProbe"));
        }

        [UnityTest]
        public IEnumerator UnknownEndpointReturnsErrorEnvelopeWith200()
        {
            var task = Client.GetAsync(BaseUrl + "/no-such-endpoint");
            yield return Await(task);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, task.Result.StatusCode);
            var bodyTask = task.Result.Content.ReadAsStringAsync();
            yield return Await(bodyTask);
            var json = JObject.Parse(bodyTask.Result);
            Assert.AreEqual("error", json["status"].Value<string>());
        }
    }
}
