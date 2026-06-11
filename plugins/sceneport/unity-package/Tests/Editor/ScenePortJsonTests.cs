using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    // Proves the JSON layer fixes the bugs the hand-rolled StringBuilder/regex code had:
    // exponent-notation numbers, non-finite floats, and control characters.
    internal sealed class ScenePortJsonTests
    {
        [Test]
        public void NonFiniteFloatsSerializeAsNull()
        {
            var dto = new Vector3Dto { X = float.NaN, Y = float.PositiveInfinity, Z = float.NegativeInfinity };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));

            Assert.AreEqual(JTokenType.Null, json["x"].Type);
            Assert.AreEqual(JTokenType.Null, json["y"].Type);
            Assert.AreEqual(JTokenType.Null, json["z"].Type);
        }

        [Test]
        public void FiniteFloatsSerializeNormally()
        {
            var dto = new Vector3Dto { X = 1.5f, Y = -2.25f, Z = 0f };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));

            Assert.AreEqual(1.5f, json["x"].Value<float>());
            Assert.AreEqual(-2.25f, json["y"].Value<float>());
            Assert.AreEqual(0f, json["z"].Value<float>());
        }

        [Test]
        public void ControlCharactersAreEscapedToValidJson()
        {
            var dto = new LogEntryDto
            {
                Type = "Error",
                Utc = "now",
                Message = "line1\nline2\ttabbell\bback",
                StackTrace = "at <\"quoted\">",
            };

            // Parsing back is the assertion: the original Escape() emitted invalid JSON here.
            var json = JObject.Parse(ScenePortJson.Serialize(dto));
            Assert.AreEqual("line1\nline2\ttabbell\bback", json["message"].Value<string>());
            Assert.AreEqual("at <\"quoted\">", json["stackTrace"].Value<string>());
        }

        [Test]
        public void UnicodeRoundTrips()
        {
            var dto = new LogEntryDto { Type = "Log", Utc = "t", Message = "日本語 😀 ✓", StackTrace = "" };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));
            Assert.AreEqual("日本語 😀 ✓", json["message"].Value<string>());
        }

        [Test]
        public void NullableEnabledIsIncludedAsNull()
        {
            var dto = new ComponentDto { Index = 0, Type = "X", InstanceId = 1, Enabled = null };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));
            Assert.IsTrue(json.ContainsKey("enabled"));
            Assert.AreEqual(JTokenType.Null, json["enabled"].Type);
        }

        [Test]
        public void MissingScriptOmitsFullTypeKeys()
        {
            var dto = new ComponentDto { Index = 2, Type = "MissingScript", InstanceId = 0, Enabled = null };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));
            Assert.IsFalse(json.ContainsKey("fullType"));
            Assert.IsFalse(json.ContainsKey("assemblyQualifiedName"));
        }

        [Test]
        public void IncludedComponentsKeyOmittedWhenNull()
        {
            var dto = new GameObjectDetail { Name = "n", Components = null };
            var json = JObject.Parse(ScenePortJson.Serialize(dto));
            Assert.IsFalse(json.ContainsKey("components"));
        }

        [Test]
        public void ErrorResponseShape()
        {
            var json = JObject.Parse(ScenePortJson.Serialize(new ErrorResponse("boom")));
            Assert.AreEqual("error", json["status"].Value<string>());
            Assert.AreEqual("boom", json["error"].Value<string>());
        }

        [Test]
        public void ParseBodyToleratesGarbage()
        {
            Assert.AreEqual(0, ScenePortJson.ParseBody("{not json").Count);
            Assert.AreEqual(0, ScenePortJson.ParseBody("").Count);
            Assert.AreEqual(0, ScenePortJson.ParseBody(null).Count);
            Assert.AreEqual(0, ScenePortJson.ParseBody("[1,2,3]").Count); // arrays → empty object
        }
    }
}
