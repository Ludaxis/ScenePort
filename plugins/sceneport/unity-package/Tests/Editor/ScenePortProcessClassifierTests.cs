using NUnit.Framework;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ScenePortProcessClassifierTests
    {
        [Test]
        public void AssetImportWorkerProcessIsRejected()
        {
            Assert.IsFalse(ScenePortEditorProcessRole.ShouldHostBridge("Unity AssetImportWorker", new string[0], false));
            Assert.AreEqual("asset-import-worker", ScenePortEditorProcessRole.RoleFor("Unity AssetImportWorker", new string[0], false));
        }

        [Test]
        public void AssetImportWorkerArgIsRejected()
        {
            Assert.IsFalse(ScenePortEditorProcessRole.ShouldHostBridge("Unity", new[] { "-assetImportWorker" }, true));
        }

        [Test]
        public void NormalEditorIsAllowed()
        {
            Assert.IsTrue(ScenePortEditorProcessRole.ShouldHostBridge("Unity", new string[0], false));
            Assert.AreEqual("editor", ScenePortEditorProcessRole.RoleFor("Unity", new string[0], false));
        }

        [Test]
        public void RunTestsBatchmodeIsAllowed()
        {
            Assert.IsTrue(ScenePortEditorProcessRole.ShouldHostBridge("Unity", new[] { "-batchmode", "-runTests" }, true));
            Assert.AreEqual("batchmode-tests", ScenePortEditorProcessRole.RoleFor("Unity", new[] { "-runTests" }, true));
        }
    }
}
