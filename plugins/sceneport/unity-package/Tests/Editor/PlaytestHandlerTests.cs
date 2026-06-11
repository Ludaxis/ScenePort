using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class PlaytestHandlerTests
    {
        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PlaytestHandlers.ResetForTests();
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Version = "test", BoundPort = 12345 };
        }

        [TearDown]
        public void TearDown()
        {
            PlaytestHandlers.ResetForTests();
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }

        [Test]
        public void StatusIsIdleBeforeStart()
        {
            var response = (PlaytestSessionResponse)PlaytestHandlers.Status(new ScenePortRequest("", null), ctx);
            Assert.AreEqual("idle", response.Session.Status);
            Assert.AreEqual(string.Empty, response.Session.SessionId);
        }

        [Test]
        public void StartWithoutPlayModeCreatesRunningSession()
        {
            var response = (PlaytestSessionResponse)PlaytestHandlers.Start(
                new ScenePortRequest("", "{\"label\":\"Smoke\",\"enterPlayMode\":false}"),
                ctx);

            Assert.AreEqual("running", response.Session.Status);
            Assert.AreEqual("Smoke", response.Session.Label);
            Assert.IsFalse(response.Session.IsPlaying);
            Assert.AreEqual(1, response.Session.InteractionCount);
        }

        [Test]
        public void ReportIncludesSessionLogsAndRecommendations()
        {
            PlaytestHandlers.Start(new ScenePortRequest("", "{\"label\":\"Report\",\"enterPlayMode\":false}"), ctx);
            ctx.Console.Add("boom", "stack", "Error");

            var response = (PlaytestReportResponse)PlaytestHandlers.Report(new ScenePortRequest("", null), ctx);

            Assert.AreEqual("running", response.Report.Session.Status);
            Assert.AreEqual(1, response.Report.Session.ErrorCount);
            Assert.AreEqual(1, response.Report.Logs.Count);
            Assert.IsTrue(response.Report.Recommendations.Exists(r => r.Contains("console errors")));
        }

        [Test]
        public void StopReturnsStoppedReport()
        {
            PlaytestHandlers.Start(new ScenePortRequest("", "{\"enterPlayMode\":false}"), ctx);

            var response = (PlaytestReportResponse)PlaytestHandlers.Stop(
                new ScenePortRequest("", "{\"exitPlayMode\":false}"),
                ctx);

            Assert.AreEqual("stopped", response.Report.Session.Status);
            Assert.IsNotEmpty(response.Report.Session.EndedUtc);
            Assert.IsTrue(response.Report.Summary.Contains("stopped"));
        }
    }
}
