using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace ScenePort.McpBridge.Editor.Tests
{
    internal sealed class ShaderGraphPreviewHandlerTests
    {
        private const string TempFolder = "Assets/ScenePortShaderGraphTests";

        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 0 };
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        private static ScenePortRequest Body(string json) => new ScenePortRequest("", json);

        [Test]
        public void CreateShaderGraphDryRunDoesNotWriteFile()
        {
            var path = TempFolder + "/DryRun.shadergraph";
            var result = ShaderGraphPreviewHandlers.CreateShaderGraph(Body("{\"path\":\"" + path + "\",\"dryRun\":true}"), ctx);

            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsTrue(((AuthoringResponse)result).DryRun);
            Assert.IsFalse(File.Exists(Path.Combine(ScenePortPaths.ProjectPath(), path)), "Dry run must not write the .shadergraph file.");
        }

        [Test]
        public void CreateShaderGraphRejectsBadExtension()
        {
            var result = ShaderGraphPreviewHandlers.CreateShaderGraph(Body("{\"path\":\"" + TempFolder + "/Wrong.asset\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void CreateShaderGraphInvalidContentRollsBackAndReportsUnsupported()
        {
            // Obviously invalid .shadergraph JSON. Whether or not the ShaderGraph package is
            // installed, LoadAssetAtPath returns null, so the handler must roll the write back
            // (delete the asset) and surface capability.unsupported.
            var path = TempFolder + "/Invalid.shadergraph";
            var result = ShaderGraphPreviewHandlers.CreateShaderGraph(
                Body("{\"path\":\"" + path + "\",\"content\":\"this is not shadergraph json\"}"),
                ctx);

            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.AreEqual("capability.unsupported", ((ErrorResponse)result).Code);
            Assert.IsTrue(string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)), "Failed import must be rolled back (asset deleted).");
        }
    }
}
</content>
