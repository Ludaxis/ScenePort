using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal sealed class TestRunSummaryDto
    {
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("runId")] public string RunId;
        [JsonProperty("status")] public string Status;
        [JsonProperty("resultState")] public string ResultState;
        [JsonProperty("startedUtc")] public string StartedUtc;
        [JsonProperty("finishedUtc")] public string FinishedUtc;
        [JsonProperty("duration")] public double Duration;
        [JsonProperty("totalCount")] public int TotalCount;
        [JsonProperty("passCount")] public int PassCount;
        [JsonProperty("failCount")] public int FailCount;
        [JsonProperty("skipCount")] public int SkipCount;
        [JsonProperty("inconclusiveCount")] public int InconclusiveCount;
        [JsonProperty("assertCount")] public int AssertCount;
        [JsonProperty("rootName")] public string RootName;
        [JsonProperty("lastStartedTest")] public string LastStartedTest;
        [JsonProperty("message")] public string Message;
        [JsonProperty("stackTrace")] public string StackTrace;
        [JsonProperty("filter")] public string Filter;
        [JsonProperty("failedTests")] public List<FailedTestDto> FailedTests = new List<FailedTestDto>();

        internal static TestRunSummaryDto Empty(string mode)
        {
            return new TestRunSummaryDto
            {
                Mode = mode,
                RunId = string.Empty,
                Status = "not-run",
                ResultState = string.Empty,
                StartedUtc = string.Empty,
                FinishedUtc = string.Empty,
                RootName = string.Empty,
                LastStartedTest = string.Empty,
                Message = string.Empty,
                StackTrace = string.Empty,
                Filter = string.Empty,
            };
        }

        internal TestRunSummaryDto Clone()
        {
            return new TestRunSummaryDto
            {
                Mode = Mode,
                RunId = RunId,
                Status = Status,
                ResultState = ResultState,
                StartedUtc = StartedUtc,
                FinishedUtc = FinishedUtc,
                Duration = Duration,
                TotalCount = TotalCount,
                PassCount = PassCount,
                FailCount = FailCount,
                SkipCount = SkipCount,
                InconclusiveCount = InconclusiveCount,
                AssertCount = AssertCount,
                RootName = RootName,
                LastStartedTest = LastStartedTest,
                Message = Message,
                StackTrace = StackTrace,
                Filter = Filter,
                FailedTests = new List<FailedTestDto>(FailedTests),
            };
        }
    }

    internal static class TestRunHandlers
    {
        private const int MaxFailedTests = 50;
        private static readonly object TestLock = new object();
        private static TestRunSummaryDto lastEditModeTestRun = TestRunSummaryDto.Empty("editmode");
        private static TestRunSummaryDto lastPlayModeTestRun = TestRunSummaryDto.Empty("playmode");

        internal static object RunTests(ScenePortRequest req, ScenePortContext ctx)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorResponse("Unity is compiling or updating assets. Wait before starting tests.");
            }

            var modeName = NormalizeMode(req.ExtractString("mode", req.GetString("mode", "editmode")));
            var mode = modeName == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter
            {
                testMode = mode,
                testNames = ScenePortRequest.SplitCsv(req.ExtractString("testNames", req.GetString("testNames", null))),
                groupNames = ScenePortRequest.SplitCsv(req.ExtractString("groupNames", req.GetString("groupNames", null))),
                categoryNames = ScenePortRequest.SplitCsv(req.ExtractString("categoryNames", req.GetString("categoryNames", null))),
                assemblyNames = ScenePortRequest.SplitCsv(req.ExtractString("assemblyNames", req.GetString("assemblyNames", null))),
            };

            var settings = new ExecutionSettings(filter)
            {
                runSynchronously = mode == TestMode.EditMode && req.ExtractBool("runSynchronously", req.GetBool("runSynchronously", false)),
            };

            var scheduled = new TestRunSummaryDto
            {
                Mode = modeName,
                RunId = "pending",
                Status = "scheduled",
                ResultState = string.Empty,
                StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                FinishedUtc = string.Empty,
                RootName = string.Empty,
                LastStartedTest = string.Empty,
                Message = string.Empty,
                StackTrace = string.Empty,
                Filter = filter.ToString(),
            };
            SetLastTestRun(scheduled);

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var runId = api.Execute(settings);

            TestRunSummaryDto snapshot;
            lock (TestLock)
            {
                var current = GetLastTestRunLocked(modeName);
                current.RunId = runId;
                if (current.Status != "finished")
                {
                    current.Status = "running";
                }
                snapshot = current.Clone();
            }

            return new TestRunResponse { Run = snapshot };
        }

        internal static object TestsLast(ScenePortRequest req, ScenePortContext ctx)
        {
            var modeName = NormalizeMode(req.GetString("mode", req.ExtractString("mode", "editmode")));
            lock (TestLock)
            {
                return new TestRunResponse { Run = GetLastTestRunLocked(modeName).Clone() };
            }
        }

        // --- Test runner callbacks (registered once by the bridge) ---

        internal static void RegisterCallbacks()
        {
            TestRunnerApi.RegisterTestCallback(new Callbacks());
        }

        private static void RunStarted(ITestAdaptor testsToRun)
        {
            var mode = ModeFromTestMode(testsToRun.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                summary.Status = "running";
                summary.TotalCount = testsToRun.TestCaseCount;
                summary.RootName = testsToRun.FullName;
                summary.StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
        }

        private static void TestStarted(ITestAdaptor test)
        {
            var mode = ModeFromTestMode(test.TestMode);
            lock (TestLock)
            {
                GetLastTestRunLocked(mode).LastStartedTest = test.FullName;
            }
        }

        private static void TestFinished(ITestResultAdaptor result)
        {
            var mode = ModeFromTestMode(result.Test.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                if (result.ResultState.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) && summary.FailedTests.Count < MaxFailedTests)
                {
                    summary.FailedTests.Add(new FailedTestDto
                    {
                        Name = result.FullName,
                        Message = result.Message,
                        StackTrace = result.StackTrace,
                    });
                }
            }
        }

        private static void RunFinished(ITestResultAdaptor result)
        {
            var mode = ModeFromTestMode(result.Test.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                summary.Status = "finished";
                summary.ResultState = result.ResultState;
                summary.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                summary.Duration = result.Duration;
                summary.PassCount = result.PassCount;
                summary.FailCount = result.FailCount;
                summary.SkipCount = result.SkipCount;
                summary.InconclusiveCount = result.InconclusiveCount;
                summary.AssertCount = result.AssertCount;
                summary.Message = result.Message;
                summary.StackTrace = result.StackTrace;
            }
        }

        private static TestRunSummaryDto GetLastTestRunLocked(string mode)
        {
            return mode == "playmode" ? lastPlayModeTestRun : lastEditModeTestRun;
        }

        private static void SetLastTestRun(TestRunSummaryDto summary)
        {
            lock (TestLock)
            {
                if (summary.Mode == "playmode")
                {
                    lastPlayModeTestRun = summary;
                }
                else
                {
                    lastEditModeTestRun = summary;
                }
            }
        }

        private static string ModeFromTestMode(TestMode mode)
        {
            return (mode & TestMode.PlayMode) == TestMode.PlayMode && (mode & TestMode.EditMode) != TestMode.EditMode ? "playmode" : "editmode";
        }

        private static string NormalizeMode(string mode)
        {
            return string.Equals(mode, "playmode", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "play", StringComparison.OrdinalIgnoreCase)
                ? "playmode"
                : "editmode";
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) => TestRunHandlers.RunStarted(testsToRun);
            public void RunFinished(ITestResultAdaptor result) => TestRunHandlers.RunFinished(result);
            public void TestStarted(ITestAdaptor test) => TestRunHandlers.TestStarted(test);
            public void TestFinished(ITestResultAdaptor result) => TestRunHandlers.TestFinished(result);
        }
    }
}
