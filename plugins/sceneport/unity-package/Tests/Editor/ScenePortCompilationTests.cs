using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortCompilationTests
    {
        [Test]
        public void CompilerMessageRecordRoundTripsThroughJson()
        {
            var original = new List<CompilerMessageRecord>
            {
                new CompilerMessageRecord
                {
                    File = "Assets/Scripts/Foo.cs",
                    Line = 42,
                    Column = 7,
                    Type = "error",
                    Message = "CS1002: ; expected",
                    Assembly = "Assembly-CSharp",
                },
                new CompilerMessageRecord
                {
                    File = "Assets/Scripts/Bar.cs",
                    Line = 3,
                    Column = 1,
                    Type = "warning",
                    Message = "CS0168: variable declared but never used",
                    Assembly = "Assembly-CSharp-Editor",
                },
            };

            var json = JsonConvert.SerializeObject(original, ScenePortJson.Settings);
            var restored = JsonConvert.DeserializeObject<List<CompilerMessageRecord>>(json, ScenePortJson.Settings);

            Assert.AreEqual(2, restored.Count);

            Assert.AreEqual("Assets/Scripts/Foo.cs", restored[0].File);
            Assert.AreEqual(42, restored[0].Line);
            Assert.AreEqual(7, restored[0].Column);
            Assert.AreEqual("error", restored[0].Type);
            Assert.AreEqual("CS1002: ; expected", restored[0].Message);
            Assert.AreEqual("Assembly-CSharp", restored[0].Assembly);

            Assert.AreEqual("warning", restored[1].Type);
            Assert.AreEqual("Assembly-CSharp-Editor", restored[1].Assembly);
        }

        [Test]
        public void CompilerMessageDtoRoundTripsThroughJson()
        {
            var original = new List<CompilerMessageDto>
            {
                new CompilerMessageDto
                {
                    File = "Assets/Scripts/Foo.cs",
                    Line = 12,
                    Column = 4,
                    Type = "info",
                    Message = "note",
                    Assembly = "Assembly-CSharp",
                },
            };

            var json = JsonConvert.SerializeObject(original, ScenePortJson.Settings);
            // Wire field names must match the contract exactly.
            StringAssert.Contains("\"file\"", json);
            StringAssert.Contains("\"line\"", json);
            StringAssert.Contains("\"column\"", json);
            StringAssert.Contains("\"type\"", json);
            StringAssert.Contains("\"message\"", json);
            StringAssert.Contains("\"assembly\"", json);

            var restored = JsonConvert.DeserializeObject<List<CompilerMessageDto>>(json, ScenePortJson.Settings);
            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual("Assets/Scripts/Foo.cs", restored[0].File);
            Assert.AreEqual(12, restored[0].Line);
            Assert.AreEqual(4, restored[0].Column);
            Assert.AreEqual("info", restored[0].Type);
            Assert.AreEqual("note", restored[0].Message);
            Assert.AreEqual("Assembly-CSharp", restored[0].Assembly);
        }

        [Test]
        public void BumpReloadEpochIsMonotonic()
        {
            var first = ScenePortCompilation.BumpReloadEpoch();
            var second = ScenePortCompilation.BumpReloadEpoch();
            var third = ScenePortCompilation.BumpReloadEpoch();

            Assert.AreEqual(first + 1, second);
            Assert.AreEqual(second + 1, third);
            Assert.AreEqual(third, ScenePortCompilation.ReloadEpoch);
        }

        [Test]
        public void CompilerMessageTypeMapsToWireStrings()
        {
            Assert.AreEqual("error", ScenePortCompilation.TypeString(UnityEditor.Compilation.CompilerMessageType.Error));
            Assert.AreEqual("warning", ScenePortCompilation.TypeString(UnityEditor.Compilation.CompilerMessageType.Warning));
            Assert.AreEqual("info", ScenePortCompilation.TypeString(UnityEditor.Compilation.CompilerMessageType.Info));
        }
    }
}
