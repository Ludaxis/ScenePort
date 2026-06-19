using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor.Tests
{
    // Holds a live ObjectReference field so wiring by instance id can be exercised.
    internal sealed class ScenePortReferenceProbe : MonoBehaviour
    {
        public GameObject target;
    }

    internal sealed class SceneGraphHandlerTests
    {
        private ScenePortContext ctx;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ctx = new ScenePortContext { Console = new ScenePortConsoleBuffer(), Audit = new ScenePortAuditLog(), Version = "test", BoundPort = 0 };
        }

        private static ScenePortRequest Body(string json) => new ScenePortRequest("", json);

        [Test]
        public void ReparentMovesChildUnderNewParentAndUndoRestores()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            Assert.IsNull(child.transform.parent);

            var result = SceneGraphHandlers.Reparent(
                Body("{\"instanceId\":" + child.GetInstanceID() + ",\"parentInstanceId\":" + parent.GetInstanceID() + "}"),
                ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.AreEqual(parent.transform, child.transform.parent);

            Undo.PerformUndo();
            Assert.IsNull(child.transform.parent);
        }

        [Test]
        public void ReparentWithNoParentUnparentsToRoot()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            SceneGraphHandlers.Reparent(Body("{\"instanceId\":" + child.GetInstanceID() + "}"), ctx);
            Assert.IsNull(child.transform.parent);
        }

        [Test]
        public void ReparentDryRunDoesNotMutate()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");

            var result = SceneGraphHandlers.Reparent(
                Body("{\"instanceId\":" + child.GetInstanceID() + ",\"parentInstanceId\":" + parent.GetInstanceID() + ",\"dryRun\":true}"),
                ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsTrue(((AuthoringResponse)result).DryRun);
            Assert.IsNull(child.transform.parent);
        }

        [Test]
        public void ReparentMissingTargetReturnsError()
        {
            var result = SceneGraphHandlers.Reparent(Body("{\"instanceId\":0,\"path\":\"DoesNotExist\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
        }

        [Test]
        public void RenameChangesNameAndUndoRestores()
        {
            var go = new GameObject("Original");
            var result = SceneGraphHandlers.Rename(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"newName\":\"Renamed\"}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.AreEqual("Renamed", go.name);

            Undo.PerformUndo();
            Assert.AreEqual("Original", go.name);
        }

        [Test]
        public void RenameRequiresNonEmptyName()
        {
            var go = new GameObject("Original");
            var result = SceneGraphHandlers.Rename(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"newName\":\"\"}"), ctx);
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.AreEqual("Original", go.name);
        }

        [Test]
        public void DuplicateCreatesSiblingWithSameNameUnderSameParent()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            var result = SceneGraphHandlers.Duplicate(Body("{\"instanceId\":" + child.GetInstanceID() + "}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.AreEqual(2, parent.transform.childCount);
            Assert.AreEqual("Child", parent.transform.GetChild(1).name);
        }

        [Test]
        public void DeleteRemovesGameObjectAndUndoRestores()
        {
            var go = new GameObject("Doomed");
            var id = go.GetInstanceID();

            var result = SceneGraphHandlers.Delete(Body("{\"instanceId\":" + id + "}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsNull(GameObject.Find("Doomed"));

            Undo.PerformUndo();
            Assert.IsNotNull(GameObject.Find("Doomed"));
        }

        [Test]
        public void DeleteDryRunDoesNotDestroy()
        {
            var go = new GameObject("Survivor");
            var result = SceneGraphHandlers.Delete(Body("{\"instanceId\":" + go.GetInstanceID() + ",\"dryRun\":true}"), ctx);
            Assert.IsInstanceOf<AuthoringResponse>(result);
            Assert.IsNotNull(GameObject.Find("Survivor"));
        }

        [Test]
        public void ReorderSiblingMovesToRequestedIndex()
        {
            var parent = new GameObject("Parent");
            var a = new GameObject("A");
            var b = new GameObject("B");
            var c = new GameObject("C");
            a.transform.SetParent(parent.transform);
            b.transform.SetParent(parent.transform);
            c.transform.SetParent(parent.transform);

            SceneGraphHandlers.ReorderSibling(Body("{\"instanceId\":" + c.GetInstanceID() + ",\"siblingIndex\":0}"), ctx);
            Assert.AreEqual(0, c.transform.GetSiblingIndex());
        }

        [Test]
        public void SetSerializedPropertyWiresObjectReferenceByInstanceId()
        {
            var holder = new GameObject("Holder");
            var probe = holder.AddComponent<ScenePortReferenceProbe>();
            var referenced = new GameObject("Referenced");

            var result = SceneEditHandlers.SetSerializedProperty(
                Body("{\"instanceId\":" + holder.GetInstanceID() + ",\"componentType\":\"ScenePortReferenceProbe\",\"propertyPath\":\"target\",\"valueKind\":\"objectReference\",\"objectReferenceInstanceId\":" + referenced.GetInstanceID() + "}"),
                ctx);

            Assert.IsInstanceOf<SetSerializedPropertyResponse>(result);
            Assert.AreEqual(referenced, probe.target);
        }

        [Test]
        public void SetSerializedPropertyClearsObjectReferenceWhenZero()
        {
            var holder = new GameObject("Holder");
            var probe = holder.AddComponent<ScenePortReferenceProbe>();
            probe.target = new GameObject("Pre");

            var result = SceneEditHandlers.SetSerializedProperty(
                Body("{\"instanceId\":" + holder.GetInstanceID() + ",\"componentType\":\"ScenePortReferenceProbe\",\"propertyPath\":\"target\",\"valueKind\":\"objectReference\",\"objectReferenceInstanceId\":0}"),
                ctx);

            Assert.IsInstanceOf<SetSerializedPropertyResponse>(result);
            Assert.IsNull(probe.target);
        }
    }
}
