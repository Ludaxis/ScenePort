using NUnit.Framework;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortConsoleBufferTests
    {
        [Test]
        public void EvictsOldestBeyondCapacity()
        {
            var buffer = new ScenePortConsoleBuffer(3);
            buffer.Add("a", "", "Log");
            buffer.Add("b", "", "Log");
            buffer.Add("c", "", "Log");
            buffer.Add("d", "", "Log");

            Assert.AreEqual(3, buffer.Count);
            var snapshot = buffer.Snapshot(10, "all");
            // Newest first; "a" evicted.
            Assert.AreEqual("d", snapshot[0].Message);
            Assert.AreEqual("c", snapshot[1].Message);
            Assert.AreEqual("b", snapshot[2].Message);
        }

        [Test]
        public void TypeFilterIsCaseInsensitive()
        {
            var buffer = new ScenePortConsoleBuffer();
            buffer.Add("info", "", "Log");
            buffer.Add("bad", "", "Error");

            var errors = buffer.Snapshot(10, "error");
            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual("bad", errors[0].Message);
        }

        [Test]
        public void LimitClampsToBuffer()
        {
            var buffer = new ScenePortConsoleBuffer();
            for (var i = 0; i < 10; i++)
            {
                buffer.Add("m" + i, "", "Log");
            }

            Assert.AreEqual(5, buffer.Snapshot(5, "all").Count);
        }

        [Test]
        public void ErrorSnapshotFiltersErrorExceptionAssert()
        {
            var buffer = new ScenePortConsoleBuffer();
            buffer.Add("log", "", "Log");
            buffer.Add("warn", "", "Warning");
            buffer.Add("err", "", "Error");
            buffer.Add("exc", "", "Exception");
            buffer.Add("assert", "", "Assert");

            var errors = buffer.ErrorSnapshot(50);
            Assert.AreEqual(3, errors.Count);
        }

        [Test]
        public void NullFieldsBecomeEmptyStrings()
        {
            var buffer = new ScenePortConsoleBuffer();
            buffer.Add(null, null, null);
            var entry = buffer.Snapshot(1, "all")[0];
            Assert.AreEqual(string.Empty, entry.Message);
            Assert.AreEqual(string.Empty, entry.StackTrace);
            Assert.AreEqual(string.Empty, entry.Type);
        }
    }
}
