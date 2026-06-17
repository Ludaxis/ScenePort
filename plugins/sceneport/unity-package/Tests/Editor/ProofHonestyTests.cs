using NUnit.Framework;

namespace ScenePort.McpBridge.Editor.Tests
{
    /// <summary>
    /// Guards the v1.0 "no false greens" contract: the scenario harness reports itself as an
    /// unimplemented preview, and perf budgets only claim to be evaluated/passed for metrics
    /// the bridge actually measures.
    /// </summary>
    internal sealed class ProofHonestyTests
    {
        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Version = "test", BoundPort = 0 };
        }

        private static T Read<T>(object response, string property)
        {
            var prop = response.GetType().GetProperty(property);
            Assert.IsNotNull(prop, "Missing property: " + property);
            return (T)prop.GetValue(response);
        }

        private static bool Has(object response, string property)
        {
            return response.GetType().GetProperty(property) != null;
        }

        [Test]
        public void ScenarioStatusIsHonestPreview()
        {
            var response = ProofHandlers.ScenarioStatus(new ScenePortRequest("", "{}"), ctx);
            Assert.AreEqual("ok", Read<string>(response, "status"));
            Assert.AreEqual("preview", Read<string>(response, "state"));
            Assert.IsFalse(Read<bool>(response, "implemented"));
            StringAssert.Contains("preview", Read<string>(response, "note"));
        }

        [Test]
        public void ScenarioRunIsHonestPreview()
        {
            var response = ProofHandlers.ScenarioRun(new ScenePortRequest("", "{}"), ctx);
            Assert.AreEqual("ok", Read<string>(response, "status"));
            Assert.AreEqual("preview", Read<string>(response, "state"));
            Assert.IsFalse(Read<bool>(response, "implemented"));
            Assert.IsFalse(string.IsNullOrEmpty(Read<string>(response, "scenarioRunId")));
            StringAssert.Contains("preview", Read<string>(response, "note"));
        }

        [Test]
        public void ScenarioRunNeverReportsFinished()
        {
            var response = ProofHandlers.ScenarioRun(new ScenePortRequest("", "{}"), ctx);
            Assert.AreNotEqual("finished", Read<string>(response, "state"));
        }

        [Test]
        public void PerfBudgetEvaluatesMeasuredMetric()
        {
            // totalAllocatedMemory is a real measured counter, so it is evaluated.
            var response = ProofHandlers.PerfCheckBudget(
                new ScenePortRequest("", "{\"metric\":\"totalAllocatedMemory\",\"max\":2147483647}"),
                ctx);

            Assert.AreEqual("ok", Read<string>(response, "status"));
            Assert.IsTrue(Read<bool>(response, "evaluated"));
            Assert.IsTrue(Read<bool>(response, "passed"));
        }

        [Test]
        public void PerfBudgetDoesNotEvaluateUnknownMetricAndNeverPasses()
        {
            var response = ProofHandlers.PerfCheckBudget(
                new ScenePortRequest("", "{\"metric\":\"fps\",\"max\":1}"),
                ctx);

            Assert.AreEqual("ok", Read<string>(response, "status"));
            Assert.IsFalse(Read<bool>(response, "evaluated"));
            Assert.IsFalse(Read<bool>(response, "passed"), "An unmeasured metric must never report passed.");
            Assert.IsTrue(Has(response, "note"));
        }
    }
}
