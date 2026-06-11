using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    internal static class SceneQueryHandlers
    {
        internal static object Scene(ScenePortRequest req, ScenePortContext ctx)
        {
            var scene = SceneManager.GetActiveScene();
            return new SceneResponse
            {
                Name = scene.name,
                Path = scene.path,
                BuildIndex = scene.buildIndex,
                RootCount = scene.rootCount,
                IsDirty = scene.isDirty,
                IsLoaded = scene.isLoaded,
                IsValid = scene.IsValid(),
            };
        }

        internal static object Hierarchy(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = Mathf.Clamp(req.GetInt("limit", 200), 1, 1000);
            var maxDepth = Mathf.Clamp(req.GetInt("maxDepth", 8), 0, 32);

            var scene = SceneManager.GetActiveScene();
            var response = new HierarchyResponse { Scene = scene.name };
            var truncated = false;

            void Append(GameObject go, int depth)
            {
                if (response.Objects.Count >= limit)
                {
                    truncated = true;
                    return;
                }

                response.Objects.Add(BuildNode(go, depth));

                if (depth >= maxDepth)
                {
                    return;
                }

                var transform = go.transform;
                for (var i = 0; i < transform.childCount; i++)
                {
                    Append(transform.GetChild(i).gameObject, depth + 1);
                }
            }

            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                Append(roots[i], 0);
            }

            response.Truncated = truncated;
            return response;
        }

        private static HierarchyNode BuildNode(GameObject go, int depth)
        {
            var node = new HierarchyNode
            {
                Name = go.name,
                Path = ScenePortObjects.GetPath(go.transform),
                InstanceId = go.GetInstanceID(),
                Active = go.activeSelf,
                ActiveInHierarchy = go.activeInHierarchy,
                Depth = depth,
                ChildCount = go.transform.childCount,
            };

            var components = go.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                node.Components.Add(ScenePortObjects.ComponentTypeName(components[i]));
            }

            return node;
        }

        internal static object Selection(ScenePortRequest req, ScenePortContext ctx)
        {
            var response = new SelectionResponse();
            var selection = UnityEditor.Selection.gameObjects;
            for (var i = 0; i < selection.Length; i++)
            {
                response.Objects.Add(ScenePortObjects.BuildRef(selection[i]));
            }

            return response;
        }

        internal static object GameObject(ScenePortRequest req, ScenePortContext ctx)
        {
            var go = ScenePortObjects.ResolveGameObject(req.GetInt("instanceId", 0), req.GetString("path", null));
            if (go == null)
            {
                return new ErrorResponse("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var includeComponents = req.GetBool("includeComponents", true);
            var propertyLimit = req.GetInt("propertyLimit", 40);
            return new GameObjectDetailResponse
            {
                Object = ScenePortObjects.BuildDetail(go, includeComponents, propertyLimit),
            };
        }

        internal static object Components(ScenePortRequest req, ScenePortContext ctx)
        {
            var go = ScenePortObjects.ResolveGameObject(req.GetInt("instanceId", 0), req.GetString("path", null));
            if (go == null)
            {
                return new ErrorResponse("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var propertyLimit = req.GetInt("propertyLimit", 80);
            return new ComponentsResponse
            {
                Object = ScenePortObjects.BuildRef(go),
                Components = ScenePortObjects.BuildComponents(go, propertyLimit),
            };
        }
    }
}
