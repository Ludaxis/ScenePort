using NUnit.Framework;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    // A component with public fields of every serialized type the bridge can write,
    // so set-serialized-property can be exercised against real Unity serialization.
    internal sealed class ScenePortTestProbe : MonoBehaviour
    {
        public enum Choice { Alpha, Beta, Gamma }

        public float floatField;
        public int intField;
        public bool boolField;
        public string stringField;
        public Color colorField = Color.black;
        public Choice enumField;
    }

    internal sealed class ScenePortTestAsset : ScriptableObject
    {
        public string label;
    }

    internal sealed class SceneHandlerTests
    {
        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 0 };
        }

        private static ScenePortRequest Body(string json) => new ScenePortRequest("", json);

        private static GameObject Probe(out ScenePortTestProbe probe)
        {
            var go = new GameObject("Probe");
            probe = go.AddComponent<ScenePortTestProbe>();
            return go;
        }

        [Test]
        public void CreateGameObjectAndUndo()
        {
            var result = SceneEditHandlers.CreateGameObject(Body("{\"name\":\"Created\"}"), ctx);
            Assert.IsInstanceOf<GameObjectRefResponse>(result);
            Assert.IsNotNull(GameObject.Find("Created"));

            Undo.PerformUndo();
            Assert.IsNull(GameObject.Find("Created"));
        }

        [Test]
        public void CreateGameObjectRequiresName()
        {
            var result = SceneEditHandlers.CreateGameObject(Body("{}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void CreateGameObjectUnderParent()
        {
            new GameObject("Parent");
            SceneEditHandlers.CreateGameObject(Body("{\"name\":\"Child\",\"parentPath\":\"Parent\"}"), ctx);
            var child = GameObject.Find("Child");
            Assert.IsNotNull(child);
            Assert.AreEqual("Parent", child.transform.parent.name);
        }

        [Test]
        public void SetTransformPositionOnlyLeavesRotationAndScale()
        {
            var go = new GameObject("T");
            go.transform.localEulerAngles = new Vector3(10, 20, 30);
            go.transform.localScale = new Vector3(2, 2, 2);

            var rotationBefore = go.transform.localEulerAngles;
            SceneEditHandlers.SetTransform(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"position\":{\"x\":5,\"y\":6,\"z\":7}}"), ctx);

            Assert.AreEqual(new Vector3(5, 6, 7), go.transform.localPosition);
            // Euler angles round-trip through quaternion, so compare with tolerance.
            Assert.Less(Vector3.Distance(rotationBefore, go.transform.localEulerAngles), 0.001f);
            Assert.AreEqual(new Vector3(2, 2, 2), go.transform.localScale);
        }

        [Test]
        public void SetTransformAppliesExponentValues()
        {
            var go = new GameObject("T");
            SceneEditHandlers.SetTransform(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"position\":{\"x\":1e-7,\"y\":2.5E3,\"z\":-1e-10}}"), ctx);
            Assert.AreEqual(1e-7f, go.transform.localPosition.x);
            Assert.AreEqual(2500f, go.transform.localPosition.y);
            Assert.AreEqual(-1e-10f, go.transform.localPosition.z);
        }

        [Test]
        public void SetTransformRequiresAValue()
        {
            var go = new GameObject("T");
            var result = SceneEditHandlers.SetTransform(Body("{\"instanceId\":" + go.GetInstanceID() + "}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void AddComponentByShortName()
        {
            var go = new GameObject("C");
            var result = SceneEditHandlers.AddComponent(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"typeName\":\"BoxCollider\"}"), ctx);
            Assert.IsInstanceOf<AddComponentResponse>(result);
            Assert.IsNotNull(go.GetComponent<BoxCollider>());
        }

        [Test]
        public void AddComponentUnknownTypeReturnsError()
        {
            var go = new GameObject("C");
            var result = SceneEditHandlers.AddComponent(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"typeName\":\"NotARealType\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void SetSerializedPropertyAcrossTypes()
        {
            var go = Probe(out var probe);
            var id = go.GetInstanceID();

            SetProp(id, "floatField", "{\"valueKind\":\"number\",\"numberValue\":3.5}");
            SetProp(id, "intField", "{\"valueKind\":\"number\",\"numberValue\":42}");
            SetProp(id, "boolField", "{\"valueKind\":\"bool\",\"boolValue\":true}");
            SetProp(id, "stringField", "{\"valueKind\":\"string\",\"stringValue\":\"hello\"}");
            SetProp(id, "colorField", "{\"valueKind\":\"color\",\"colorValue\":{\"r\":1,\"g\":0,\"b\":0,\"a\":1}}");
            SetProp(id, "enumField", "{\"valueKind\":\"string\",\"stringValue\":\"Beta\"}");

            Assert.AreEqual(3.5f, probe.floatField);
            Assert.AreEqual(42, probe.intField);
            Assert.IsTrue(probe.boolField);
            Assert.AreEqual("hello", probe.stringField);
            Assert.AreEqual(new Color(1, 0, 0, 1), probe.colorField);
            Assert.AreEqual(ScenePortTestProbe.Choice.Beta, probe.enumField);
        }

        [Test]
        public void SetSerializedPropertyBlocksScriptReference()
        {
            var go = Probe(out _);
            var result = SceneEditHandlers.SetSerializedProperty(
                Body("{\"instanceId\":" + go.GetInstanceID() + ",\"componentType\":\"ScenePortTestProbe\",\"propertyPath\":\"m_Script\",\"valueKind\":\"objectReference\",\"objectReferenceAssetPath\":\"Assets/Nope.cs\"}"),
                ctx);

            Assert.IsInstanceOf<ErrorResponse>(result);
            StringAssert.Contains("not writable", ((ErrorResponse)result).Error);
        }

        [Test]
        public void SetSerializedPropertyRejectsNonSceneObjectTargets()
        {
            var asset = ScriptableObject.CreateInstance<ScenePortTestAsset>();
            try
            {
                var result = SceneEditHandlers.SetSerializedProperty(
                    Body("{\"instanceId\":" + asset.GetInstanceID() + ",\"propertyPath\":\"label\",\"valueKind\":\"string\",\"stringValue\":\"changed\"}"),
                    ctx);

                Assert.IsInstanceOf<ErrorResponse>(result);
                Assert.IsNull(asset.label);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private void SetProp(int instanceId, string path, string valueJson)
        {
            var body = "{\"instanceId\":" + instanceId + ",\"componentType\":\"ScenePortTestProbe\",\"propertyPath\":\"" + path + "\"," + valueJson.Substring(1);
            var result = SceneEditHandlers.SetSerializedProperty(Body(body), ctx);
            Assert.IsInstanceOf<SetSerializedPropertyResponse>(result, "set " + path + " failed: " + ScenePortJson.Serialize(result));
        }

        [Test]
        public void HierarchyPaginationAndTruncation()
        {
            for (var i = 0; i < 5; i++)
            {
                new GameObject("Root" + i);
            }

            var limited = (HierarchyResponse)SceneQueryHandlers.Hierarchy(new ScenePortRequest("limit=2&maxDepth=8", ""), ctx);
            Assert.AreEqual(2, limited.Objects.Count);
            Assert.IsTrue(limited.Truncated);
        }

        [Test]
        public void HierarchyExactCountIsNotTruncated()
        {
            for (var i = 0; i < 3; i++)
            {
                new GameObject("Root" + i);
            }

            var exact = (HierarchyResponse)SceneQueryHandlers.Hierarchy(new ScenePortRequest("limit=3&maxDepth=8", ""), ctx);
            Assert.AreEqual(3, exact.Objects.Count);
            Assert.IsFalse(exact.Truncated, "Exact-count hierarchy must not report truncated (regression).");
        }

        [Test]
        public void HierarchyMaxDepthDoesNotMarkTruncated()
        {
            var root = new GameObject("Root");
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform);
            var grandchild = new GameObject("Grandchild");
            grandchild.transform.SetParent(child.transform);

            var shallow = (HierarchyResponse)SceneQueryHandlers.Hierarchy(new ScenePortRequest("limit=100&maxDepth=1", ""), ctx);
            // root (depth 0) + child (depth 1); grandchild cut by depth, not by limit.
            Assert.AreEqual(2, shallow.Objects.Count);
            Assert.IsFalse(shallow.Truncated);
        }

        [Test]
        public void SelectionReportsMultiple()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            Selection.objects = new Object[] { a, b };

            var selection = (SelectionResponse)SceneQueryHandlers.Selection(new ScenePortRequest("", ""), ctx);
            Assert.AreEqual(2, selection.Objects.Count);
        }

        [Test]
        public void GameObjectResolvesByPathAndId()
        {
            var go = new GameObject("Findable");
            var byId = (GameObjectDetailResponse)SceneQueryHandlers.GameObject(new ScenePortRequest("instanceId=" + go.GetInstanceID(), ""), ctx);
            Assert.AreEqual("Findable", byId.Object.Name);

            var byPath = (GameObjectDetailResponse)SceneQueryHandlers.GameObject(new ScenePortRequest("path=Findable", ""), ctx);
            Assert.AreEqual(go.GetInstanceID(), byPath.Object.InstanceId);
        }

        [Test]
        public void SceneQueryFiltersByComponentType()
        {
            Probe(out _);
            new GameObject("Plain");

            var query = (SceneQueryResponse)PerceptionHandlers.SceneQuery(Body("{\"componentType\":\"ScenePortTestProbe\",\"includeComponents\":true}"), ctx);
            Assert.AreEqual(1, query.Items.Count);
            Assert.AreEqual("Probe", query.Items[0].Name);
            Assert.IsNotNull(query.Items[0].Components);
        }

        [Test]
        public void SerializedReadReturnsTypedProperties()
        {
            var go = Probe(out _);
            var result = (SerializedReadResponse)PerceptionHandlers.SerializedRead(
                Body("{\"instanceId\":" + go.GetInstanceID() + ",\"componentType\":\"ScenePortTestProbe\",\"propertyLimit\":20}"),
                ctx);

            Assert.IsTrue(result.Properties.Exists(item => item.Path == "floatField" && item.ValueKind == "number"));
        }

        [Test]
        public void ConsoleEventsUsesCursor()
        {
            ctx.Console.Add("one", "", "Log");
            ctx.Console.Add("two", "", "Warning");
            var first = (ConsoleEventsResponse)PerceptionHandlers.ConsoleEvents(new ScenePortRequest("cursor=0&limit=1", ""), ctx);
            var second = (ConsoleEventsResponse)PerceptionHandlers.ConsoleEvents(new ScenePortRequest("cursor=" + first.NextCursor + "&limit=10", ""), ctx);

            Assert.AreEqual(1, first.Entries.Count);
            Assert.AreEqual("two", second.Entries[0].Message);
        }

        [Test]
        public void AuthoringRejectsPathTraversal()
        {
            var result = AuthoringHandlers.CreateScript(Body("{\"className\":\"Bad\",\"folder\":\"Assets/../ProjectSettings\",\"dryRun\":true}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void CreateScriptDryRunDoesNotWriteFile()
        {
            var path = Path.Combine(ScenePortPaths.ProjectPath(), "Assets", "ScenePortDryRun.cs");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var result = AuthoringHandlers.CreateScript(Body("{\"className\":\"ScenePortDryRun\",\"folder\":\"Assets\",\"dryRun\":true}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void AuthoringBatchDryRunDoesNotCallLegacyMutation()
        {
            var result = AuthoringHandlers.Batch(
                Body("{\"dryRun\":true,\"operations\":[{\"op\":\"createGameObject\",\"args\":{\"name\":\"ShouldNotExist\"}}]}"),
                ctx);

            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsNull(GameObject.Find("ShouldNotExist"));
        }

        [Test]
        public void MenuItemExecutionRejectsNonAllowlistedPath()
        {
            var result = AuthoringHandlers.ExecuteMenuItem(Body("{\"path\":\"Assets/Delete\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void GetPathReportsNestedHierarchy()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            var c = new GameObject("C");
            b.transform.SetParent(a.transform);
            c.transform.SetParent(b.transform);

            Assert.AreEqual("A/B/C", ScenePortObjects.GetPath(c.transform));
        }
    }
}
