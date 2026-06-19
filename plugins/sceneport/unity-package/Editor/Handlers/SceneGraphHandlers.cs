using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Scene-graph and prefab handlers: reparent / rename / delete / duplicate / reorder of
    /// scene GameObjects, plus prefab instantiation and override apply/revert. Every mutation
    /// is Undo-wrapped with the shared group idiom (mirroring SceneEditHandlers.CreateGameObject),
    /// honors a dry-run barrier, and marks the owning scene dirty so the editor persists it.
    /// </summary>
    internal static class SceneGraphHandlers
    {
        internal static object Reparent(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            GameObject parent = null;
            var parentInstanceId = req.ExtractInt("parentInstanceId", req.GetInt("parentInstanceId", 0));
            var parentPath = req.ExtractString("parentPath", req.GetString("parentPath", null));
            if (parentInstanceId != 0 || !string.IsNullOrEmpty(parentPath))
            {
                parent = ScenePortObjects.ResolveGameObject(parentInstanceId, parentPath);
                if (parent == null)
                {
                    return new ErrorResponse("request.invalid", "New parent not found. Provide parentInstanceId or parentPath, or omit both to unparent.", "request", false);
                }
                if (parent == go)
                {
                    return new ErrorResponse("request.invalid", "A GameObject cannot be parented to itself.", "request", false);
                }
            }

            var worldPositionStays = req.ExtractBool("worldPositionStays", true);
            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "reparent" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go), parent = parent == null ? null : ScenePortObjects.BuildRef(parent) };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Reparent");
            Undo.SetTransformParent(go.transform, parent != null ? parent.transform : null, worldPositionStays, "ScenePort Reparent");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { gameObject = ScenePortObjects.BuildRef(go), parent = parent == null ? null : ScenePortObjects.BuildRef(parent) };
            return response;
        }

        internal static object Rename(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var newName = req.ExtractString("newName", req.GetString("newName", null));
            if (string.IsNullOrEmpty(newName))
            {
                return new ErrorResponse("request.invalid", "newName is required and must be non-empty.", "request", false);
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "rename" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go), newName };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Rename");
            Undo.RecordObject(go, "ScenePort Rename");
            go.name = newName;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { gameObject = ScenePortObjects.BuildRef(go), newName };
            return response;
        }

        internal static object Delete(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var deletedRef = ScenePortObjects.BuildRef(go);
            var scene = go.scene;
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "deleteGameObject" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "delete", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = deletedRef };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Delete GameObject");
            Undo.DestroyObjectImmediate(go);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { gameObject = deletedRef };
            return response;
        }

        internal static object Duplicate(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "duplicateGameObject" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "create", null, true, true));
            if (dryRun)
            {
                response.Result = new { source = ScenePortObjects.BuildRef(go) };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Duplicate GameObject");
            var clone = UnityEngine.Object.Instantiate(go);
            clone.name = go.name;
            clone.transform.SetParent(go.transform.parent, true);
            Undo.RegisterCreatedObjectUndo(clone, "ScenePort Duplicate GameObject");
            EditorSceneManager.MarkSceneDirty(clone.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { source = ScenePortObjects.BuildRef(go), clone = ScenePortObjects.BuildRef(clone) };
            return response;
        }

        internal static object ReorderSibling(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var siblingIndex = req.ExtractInt("siblingIndex", req.GetInt("siblingIndex", -1));
            if (siblingIndex < 0)
            {
                return new ErrorResponse("request.invalid", "siblingIndex is required and must be zero or greater.", "request", false);
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "reorderSibling" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go), siblingIndex };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Reorder Sibling");
            Undo.RecordObject(go.transform, "ScenePort Reorder Sibling");
            go.transform.SetSiblingIndex(siblingIndex);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { gameObject = ScenePortObjects.BuildRef(go), siblingIndex = go.transform.GetSiblingIndex() };
            return response;
        }

        internal static object InstantiatePrefab(ScenePortRequest req, ScenePortContext ctx)
        {
            var prefabPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("prefabPath", req.GetString("prefabPath", null)));
            var error = AuthoringHandlers.ValidateAssetPath(prefabPath, ".prefab");
            if (error != null)
            {
                return error;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return new ErrorResponse("request.invalid", "Prefab asset not found: " + prefabPath, "request", false);
            }

            GameObject parent = null;
            var parentInstanceId = req.ExtractInt("parentInstanceId", req.GetInt("parentInstanceId", 0));
            var parentPath = req.ExtractString("parentPath", req.GetString("parentPath", null));
            if (parentInstanceId != 0 || !string.IsNullOrEmpty(parentPath))
            {
                parent = ScenePortObjects.ResolveGameObject(parentInstanceId, parentPath);
                if (parent == null)
                {
                    return new ErrorResponse("request.invalid", "Parent not found. Provide parentInstanceId or parentPath, or omit both for a root instance.", "request", false);
                }
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "instantiatePrefab" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "create", null, true, true));
            if (dryRun)
            {
                response.Result = new { prefabPath, parent = parent == null ? null : ScenePortObjects.BuildRef(parent) };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Instantiate Prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null)
            {
                inst.transform.SetParent(parent.transform, false);
            }
            if (req.HasObject("position"))
            {
                inst.transform.localPosition = req.GetVector3("position", inst.transform.localPosition);
            }
            Undo.RegisterCreatedObjectUndo(inst, "ScenePort Instantiate Prefab");
            EditorSceneManager.MarkSceneDirty(inst.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { prefabPath, instance = ScenePortObjects.BuildRef(inst) };
            return response;
        }

        internal static object ApplyPrefabOverrides(ScenePortRequest req, ScenePortContext ctx)
        {
            return ApplyOrRevert(req, apply: true);
        }

        internal static object RevertPrefabOverrides(ScenePortRequest req, ScenePortContext ctx)
        {
            return ApplyOrRevert(req, apply: false);
        }

        private static object ApplyOrRevert(ScenePortRequest req, bool apply)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
            {
                return new ErrorResponse("request.invalid", "GameObject is not part of a prefab instance.", "request", false);
            }

            var operation = apply ? "prefabApply" : "prefabRevert";
            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = operation };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go) };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(apply ? "ScenePort Apply Prefab Overrides" : "ScenePort Revert Prefab Overrides");
            if (apply)
            {
                PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            }
            else
            {
                PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
            }
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            response.Result = new { gameObject = ScenePortObjects.BuildRef(go) };
            return response;
        }
    }
}
