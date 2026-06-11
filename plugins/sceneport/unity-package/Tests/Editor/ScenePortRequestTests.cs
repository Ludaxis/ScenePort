using NUnit.Framework;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortRequestTests
    {
        [Test]
        public void BodyWinsOverQuery()
        {
            var req = new ScenePortRequest("name=fromQuery", "{\"name\":\"fromBody\"}");
            Assert.AreEqual("fromBody", req.ExtractString("name", req.GetString("name", "fallback")));
        }

        [Test]
        public void QueryUsedWhenBodyMissing()
        {
            var req = new ScenePortRequest("name=fromQuery", "");
            Assert.AreEqual("fromQuery", req.ExtractString("name", req.GetString("name", "fallback")));
        }

        [Test]
        public void FallbackWhenBothMissing()
        {
            var req = new ScenePortRequest("", "");
            Assert.AreEqual("fallback", req.ExtractString("name", req.GetString("name", "fallback")));
        }

        [Test]
        public void ExponentNotationParsesExactly()
        {
            // The original regex `-?\d+(\.\d+)?` silently dropped these to the fallback.
            var req = new ScenePortRequest("", "{\"position\":{\"x\":1e-7,\"y\":2.5E3,\"z\":-1e-10}}");
            var v = req.GetVector3("position", Vector3.zero);
            Assert.AreEqual(1e-7f, v.x);
            Assert.AreEqual(2500f, v.y);
            Assert.AreEqual(-1e-10f, v.z);
        }

        [Test]
        public void NestedBracesAndKeyInStringDoNotConfuseParsing()
        {
            // A key name appearing inside a string value must not shadow the real key.
            var req = new ScenePortRequest("", "{\"name\":\"value with \\\"name\\\": fake inside\",\"position\":{\"x\":1,\"y\":2,\"z\":3}}");
            Assert.AreEqual("value with \"name\": fake inside", req.ExtractString("name", "fallback"));
            Assert.AreEqual(new Vector3(1, 2, 3), req.GetVector3("position", Vector3.zero));
        }

        [Test]
        public void PresenceDetection()
        {
            var req = new ScenePortRequest("", "{\"position\":{\"x\":1,\"y\":2,\"z\":3}}");
            Assert.IsTrue(req.HasObject("position"));
            Assert.IsFalse(req.HasObject("rotation"));
        }

        [Test]
        public void IntAndBoolExtraction()
        {
            var req = new ScenePortRequest("", "{\"instanceId\":42,\"runSynchronously\":true}");
            Assert.AreEqual(42, req.ExtractInt("instanceId", 0));
            Assert.IsTrue(req.ExtractBool("runSynchronously", false));
            Assert.AreEqual(7, req.ExtractInt("missing", 7));
        }

        [Test]
        public void ColorExtractionWithDefaultedAlpha()
        {
            var req = new ScenePortRequest("", "{\"colorValue\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1}}");
            Assert.AreEqual(new Color(1, 0, 0, 1), req.GetColor("colorValue", Color.black));
        }

        [Test]
        public void QueryParsingHandlesEncoding()
        {
            var req = new ScenePortRequest("query=t%3APrefab%20Player&limit=50", "");
            Assert.AreEqual("t:Prefab Player", req.GetString("query", null));
            Assert.AreEqual(50, req.GetInt("limit", 1));
        }

        [Test]
        public void SplitCsvBehavior()
        {
            Assert.IsNull(ScenePortRequest.SplitCsv(null));
            Assert.IsNull(ScenePortRequest.SplitCsv(""));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ScenePortRequest.SplitCsv("a, b ,c,"));
        }

        [Test]
        public void MalformedBodyThrowsBadRequest()
        {
            Assert.Throws<ScenePortBadRequestException>(() => new ScenePortRequest("", "{broken"));
        }
    }
}
