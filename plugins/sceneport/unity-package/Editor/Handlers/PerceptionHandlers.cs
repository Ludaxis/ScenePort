using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    internal static class PerceptionHandlers
    {
        internal static object SceneQuery(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = Mathf.Clamp(req.ExtractInt("limit", req.GetInt("limit", 200)), 1, 1000);
            var cursor = Mathf.Max(0, req.ExtractInt("cursor", req.GetInt("cursor", 0)));
            var maxDepth = Mathf.Clamp(req.ExtractInt("maxDepth", req.GetInt("maxDepth", 16)), 0, 64);
            var propertyLimit = Mathf.Clamp(req.ExtractInt("propertyLimit", req.GetInt("propertyLimit", 0)), 0, 100);
            var includeComponents = req.ExtractBool("includeComponents", req.GetBool("includeComponents", false));
            var includeTransform = req.ExtractBool("includeTransform", req.GetBool("includeTransform", true));
            var nameContains = req.ExtractString("nameContains", req.GetString("nameContains", null));
            var tag = req.ExtractString("tag", req.GetString("tag", null));
            var componentType = req.ExtractString("componentType", req.GetString("componentType", null));

            var all = new List<SceneQueryItemDto>();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                AppendObject(roots[i], 0, maxDepth, propertyLimit, includeComponents, includeTransform, all, nameContains, tag, componentType);
            }

            var response = new SceneQueryResponse();
            for (var i = cursor; i < all.Count && response.Items.Count < limit; i++)
            {
                response.Items.Add(all[i]);
            }

            var next = cursor + response.Items.Count;
            response.Page = new PageDto
            {
                Limit = limit,
                Truncated = next < all.Count,
                NextCursor = next < all.Count ? next.ToString(CultureInfo.InvariantCulture) : null,
            };
            return response;
        }

        internal static object ComponentQuery(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = Mathf.Clamp(req.ExtractInt("limit", req.GetInt("limit", 200)), 1, 1000);
            var cursor = Mathf.Max(0, req.ExtractInt("cursor", req.GetInt("cursor", 0)));
            var propertyLimit = Mathf.Clamp(req.ExtractInt("propertyLimit", req.GetInt("propertyLimit", 20)), 0, 100);
            var typeName = req.ExtractString("typeName", req.GetString("typeName", null));
            var all = new List<ComponentQueryItemDto>();

            foreach (var go in AllGameObjects())
            {
                var components = go.GetComponents<Component>();
                for (var i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (!MatchesComponent(component, typeName))
                    {
                        continue;
                    }

                    all.Add(new ComponentQueryItemDto
                    {
                        Object = ScenePortObjects.BuildRef(go),
                        Component = ScenePortObjects.BuildComponent(component, propertyLimit, i),
                    });
                }
            }

            var response = new ComponentQueryResponse();
            for (var i = cursor; i < all.Count && response.Items.Count < limit; i++)
            {
                response.Items.Add(all[i]);
            }

            var next = cursor + response.Items.Count;
            response.Page = new PageDto
            {
                Limit = limit,
                Truncated = next < all.Count,
                NextCursor = next < all.Count ? next.ToString(CultureInfo.InvariantCulture) : null,
            };
            return response;
        }

        internal static object SerializedRead(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var componentType = req.ExtractString("componentType", req.GetString("componentType", null));
            var componentIndex = req.ExtractInt("componentIndex", req.GetInt("componentIndex", -1));
            var propertyLimit = Mathf.Clamp(req.ExtractInt("propertyLimit", req.GetInt("propertyLimit", 100)), 1, 500);
            var cursor = Mathf.Max(0, req.ExtractInt("cursor", req.GetInt("cursor", 0)));

            var target = ResolveSerializedTarget(instanceId, componentType, componentIndex);
            if (target == null)
            {
                return new ErrorResponse("request.invalid", "Serialized target not found.", "request", false);
            }

            var all = new List<TypedPropertyDto>();
            var serializedObject = new SerializedObject(target);
            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                all.Add(BuildTypedProperty(property));
            }

            var response = new SerializedReadResponse { Target = ScenePortObjects.BuildObjectRef(target) };
            for (var i = cursor; i < all.Count && response.Properties.Count < propertyLimit; i++)
            {
                response.Properties.Add(all[i]);
            }

            var next = cursor + response.Properties.Count;
            response.Page = new PageDto
            {
                Limit = propertyLimit,
                Truncated = next < all.Count,
                NextCursor = next < all.Count ? next.ToString(CultureInfo.InvariantCulture) : null,
            };
            return response;
        }

        internal static object SceneView(ScenePortRequest req, ScenePortContext ctx)
        {
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                return new SceneViewResponse { Available = false, Reason = "No active Scene view is available." };
            }

            return new SceneViewResponse
            {
                Available = true,
                CameraPosition = new Vector3Dto(sceneView.camera.transform.position),
                CameraRotation = new Vector3Dto(sceneView.camera.transform.eulerAngles),
                Orthographic = sceneView.orthographic,
                Size = sceneView.size,
            };
        }

        internal static object CaptureSceneView(ScenePortRequest req, ScenePortContext ctx)
        {
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                return new ErrorResponse("editor.scene_view_unavailable", "No active Scene view is available.", "editor", true, 1000);
            }

            var width = Mathf.Clamp(req.ExtractInt("width", req.GetInt("width", 1024)), 64, 4096);
            var height = Mathf.Clamp(req.ExtractInt("height", req.GetInt("height", 768)), 64, 4096);
            var inline = req.ExtractBool("inline", req.GetBool("inline", true));
            var maxEdge = Mathf.Clamp(req.ExtractInt("maxEdge", req.GetInt("maxEdge", 1024)), 64, 4096);
            var fileName = req.ExtractString("fileName", req.GetString("fileName", null));
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "scene-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            }
            fileName = ScenePortPaths.SanitizeFileName(fileName);
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var directory = Path.Combine(ScenePortPaths.ProjectPath(), "Temp", "ScenePort");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            var previousTarget = sceneView.camera.targetTexture;
            var renderTexture = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var response = new CaptureGameViewResponse { Path = path, SuperSize = 1, Note = "Captured active Scene view camera." };
            try
            {
                sceneView.camera.targetTexture = renderTexture;
                sceneView.camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());

                if (inline)
                {
                    var encoded = ScenePortImage.EncodeBase64(texture, maxEdge);
                    if (!string.IsNullOrEmpty(encoded.Base64))
                    {
                        response.ImageBase64 = encoded.Base64;
                        response.Width = encoded.Width;
                        response.Height = encoded.Height;
                    }
                }
            }
            finally
            {
                sceneView.camera.targetTexture = previousTarget;
                RenderTexture.active = null;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            return response;
        }

        internal static object RuntimeStatus(ScenePortRequest req, ScenePortContext ctx)
        {
            return new RuntimeStatusResponse
            {
                IsPlaying = EditorApplication.isPlaying,
                IsPaused = EditorApplication.isPaused,
                TimeScale = Time.timeScale,
                FrameCount = Time.frameCount,
                TimeSinceStartup = EditorApplication.timeSinceStartup,
            };
        }

        internal static object RuntimeQuery(ScenePortRequest req, ScenePortContext ctx)
        {
            return SceneQuery(req, ctx);
        }

        internal static object RuntimeObject(ScenePortRequest req, ScenePortContext ctx)
        {
            return SceneQueryHandlers.GameObject(req, ctx);
        }

        internal static object ConsoleEvents(ScenePortRequest req, ScenePortContext ctx)
        {
            var cursor = Math.Max(0, req.GetInt("cursor", req.ExtractInt("cursor", 0)));
            var limit = Mathf.Clamp(req.GetInt("limit", req.ExtractInt("limit", 100)), 1, 500);
            var type = req.GetString("type", req.ExtractString("type", "all"));
            return ctx.Console.EventsAfter(cursor, limit, type);
        }

        internal static object ProfilerSnapshot(ScenePortRequest req, ScenePortContext ctx)
        {
            return new ProfilerSnapshotResponse
            {
                TotalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(),
                TotalReservedMemory = Profiler.GetTotalReservedMemoryLong(),
                MonoUsedSize = Profiler.GetMonoUsedSizeLong(),
                MonoHeapSize = Profiler.GetMonoHeapSizeLong(),
                FrameCount = Time.frameCount,
            };
        }

        internal static object AssetGraph(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = req.ExtractString("path", req.GetString("path", null));
            var guid = req.ExtractString("guid", req.GetString("guid", null));
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(guid))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
            }
            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResponse("request.invalid", "path or guid is required.", "request", false);
            }

            var limit = Mathf.Clamp(req.ExtractInt("limit", req.GetInt("limit", 100)), 1, 500);
            var includeReferencers = req.ExtractBool("includeReferencers", req.GetBool("includeReferencers", false));
            var response = new AssetGraphResponse { Asset = BuildAsset(path) };
            var dependencies = AssetDatabase.GetDependencies(path, true);
            for (var i = 0; i < dependencies.Length && response.Dependencies.Count < limit; i++)
            {
                if (!string.Equals(dependencies[i], path, StringComparison.Ordinal))
                {
                    response.Dependencies.Add(BuildAsset(dependencies[i]));
                }
            }
            response.Truncated = dependencies.Length > response.Dependencies.Count + 1;

            if (includeReferencers)
            {
                var allGuids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
                for (var i = 0; i < allGuids.Length && response.Referencers.Count < limit; i++)
                {
                    var candidate = AssetDatabase.GUIDToAssetPath(allGuids[i]);
                    if (string.IsNullOrEmpty(candidate) || string.Equals(candidate, path, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var deps = AssetDatabase.GetDependencies(candidate, true);
                    if (Array.IndexOf(deps, path) >= 0)
                    {
                        response.Referencers.Add(BuildAsset(candidate));
                    }
                }
            }

            return response;
        }

        private static void AppendObject(
            GameObject go,
            int depth,
            int maxDepth,
            int propertyLimit,
            bool includeComponents,
            bool includeTransform,
            List<SceneQueryItemDto> items,
            string nameContains,
            string tag,
            string componentType)
        {
            if (MatchesObject(go, nameContains, tag, componentType))
            {
                items.Add(new SceneQueryItemDto
                {
                    Name = go.name,
                    Path = ScenePortObjects.GetPath(go.transform),
                    InstanceId = go.GetInstanceID(),
                    Active = go.activeSelf,
                    ActiveInHierarchy = go.activeInHierarchy,
                    Tag = go.tag,
                    Layer = go.layer,
                    Scene = go.scene.name,
                    Depth = depth,
                    ChildCount = go.transform.childCount,
                    Components = includeComponents ? ScenePortObjects.BuildComponents(go, propertyLimit) : null,
                    Transform = includeTransform ? ScenePortObjects.BuildTransform(go.transform) : null,
                });
            }

            if (depth >= maxDepth)
            {
                return;
            }
            var transform = go.transform;
            for (var i = 0; i < transform.childCount; i++)
            {
                AppendObject(transform.GetChild(i).gameObject, depth + 1, maxDepth, propertyLimit, includeComponents, includeTransform, items, nameContains, tag, componentType);
            }
        }

        private static bool MatchesObject(GameObject go, string nameContains, string tag, string componentType)
        {
            if (!string.IsNullOrEmpty(nameContains) && go.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(tag) && !string.Equals(go.tag, tag, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(componentType))
            {
                var components = go.GetComponents<Component>();
                for (var i = 0; i < components.Length; i++)
                {
                    if (MatchesComponent(components[i], componentType))
                    {
                        return true;
                    }
                }
                return false;
            }

            return true;
        }

        private static bool MatchesComponent(Component component, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return true;
            }
            if (component == null)
            {
                return string.Equals(typeName, "MissingScript", StringComparison.OrdinalIgnoreCase);
            }
            var type = component.GetType();
            return string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.AssemblyQualifiedName, typeName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<GameObject> AllGameObjects()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                foreach (var go in Walk(roots[i]))
                {
                    yield return go;
                }
            }
        }

        private static IEnumerable<GameObject> Walk(GameObject root)
        {
            yield return root;
            var transform = root.transform;
            for (var i = 0; i < transform.childCount; i++)
            {
                foreach (var child in Walk(transform.GetChild(i).gameObject))
                {
                    yield return child;
                }
            }
        }

        private static UnityEngine.Object ResolveSerializedTarget(int instanceId, string componentType, int componentIndex)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is Component)
            {
                return obj;
            }
            if (obj is GameObject go)
            {
                if (componentIndex >= 0)
                {
                    var components = go.GetComponents<Component>();
                    return componentIndex < components.Length ? components[componentIndex] : null;
                }
                if (!string.IsNullOrEmpty(componentType))
                {
                    var components = go.GetComponents<Component>();
                    for (var i = 0; i < components.Length; i++)
                    {
                        if (MatchesComponent(components[i], componentType))
                        {
                            return components[i];
                        }
                    }
                    return null;
                }
                return go;
            }
            return null;
        }

        private static TypedPropertyDto BuildTypedProperty(SerializedProperty property)
        {
            return new TypedPropertyDto
            {
                Path = property.propertyPath,
                DisplayName = property.displayName,
                PropertyType = property.propertyType.ToString(),
                Editable = property.editable,
                Depth = property.depth,
                HasChildren = property.hasChildren,
                ValueKind = ValueKind(property),
                Value = TypedValue(property),
                DisplayValue = ScenePortObjects.SerializedPropertyValue(property),
            };
        }

        private static string ValueKind(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    return "integer";
                case SerializedPropertyType.Boolean:
                    return "boolean";
                case SerializedPropertyType.Float:
                    return "number";
                case SerializedPropertyType.String:
                    return "string";
                case SerializedPropertyType.Color:
                    return "color";
                case SerializedPropertyType.ObjectReference:
                    return "objectReference";
                case SerializedPropertyType.Vector2:
                    return "vector2";
                case SerializedPropertyType.Vector3:
                    return "vector3";
                case SerializedPropertyType.Vector4:
                    return "vector4";
                default:
                    return property.hasVisibleChildren ? "object" : "string";
            }
        }

        private static object TypedValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return new { r = property.colorValue.r, g = property.colorValue.g, b = property.colorValue.b, a = property.colorValue.a };
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null
                        ? null
                        : new { name = property.objectReferenceValue.name, instanceId = property.objectReferenceValue.GetInstanceID(), path = AssetDatabase.GetAssetPath(property.objectReferenceValue) };
                case SerializedPropertyType.Vector2:
                    return new { x = property.vector2Value.x, y = property.vector2Value.y };
                case SerializedPropertyType.Vector3:
                    return new { x = property.vector3Value.x, y = property.vector3Value.y, z = property.vector3Value.z };
                case SerializedPropertyType.Vector4:
                    return new { x = property.vector4Value.x, y = property.vector4Value.y, z = property.vector4Value.z, w = property.vector4Value.w };
                default:
                    return property.hasVisibleChildren ? null : ScenePortObjects.SerializedPropertyValue(property);
            }
        }

        private static AssetDto BuildAsset(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return new AssetDto
            {
                Guid = AssetDatabase.AssetPathToGUID(path),
                Path = path,
                Name = asset == null ? Path.GetFileNameWithoutExtension(path) : asset.name,
                Type = asset == null ? string.Empty : asset.GetType().Name,
                Labels = asset == null ? new List<string>() : new List<string>(AssetDatabase.GetLabels(asset)),
            };
        }
    }
}
