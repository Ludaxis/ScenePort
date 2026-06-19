using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Mesh authoring handlers. Like every ScenePort mutation these follow the authoring
    /// contract: validate -> dry-run barrier -> mutate -> AuthoringResponse, reusing the shared
    /// path/conflict/folder helpers on <see cref="AuthoringHandlers"/>. Asset creation is
    /// rollback-tracked (the batch engine deletes created assets on failure); scene assignment
    /// is Undo-wrapped.
    /// </summary>
    internal static class MeshHandlers
    {
        // Defensive caps so a malformed or hostile request cannot allocate unbounded geometry.
        private const int MaxVertices = 200000;
        private const int MaxIndices = 600000;

        internal static object CreatePrimitiveMesh(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = AuthoringHandlers.ValidateAssetPath(path, ".asset");
            if (error != null)
            {
                return error;
            }
            var shapeName = req.ExtractString("shape", "box");
            if (!TryParsePrimitive(shapeName, out var primitive))
            {
                return new ErrorResponse("request.invalid", "Unknown primitive shape: " + shapeName + ". Use box, sphere, cylinder, capsule, plane, or quad.", "request", false);
            }
            path = AuthoringHandlers.ResolveConflict(path, req);
            error = AuthoringHandlers.EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createPrimitiveMesh" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path, shape = shapeName };
                return response;
            }

            var size = req.GetVector3("size", Vector3.one);
            var mesh = BuildPrimitiveMesh(primitive, size);
            AuthoringHandlers.EnsureAssetFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            response.Result = new { path, shape = shapeName, vertexCount = mesh.vertexCount, triangleCount = mesh.triangles.Length / 3 };
            return response;
        }

        internal static object CreateProceduralMesh(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = AuthoringHandlers.ValidateAssetPath(path, ".asset");
            if (error != null)
            {
                return error;
            }

            if (!(req.Body["vertices"] is JArray verticesToken) || verticesToken.Count == 0)
            {
                return new ErrorResponse("request.invalid", "vertices is required and must be a non-empty array.", "request", false);
            }
            if (!(req.Body["triangles"] is JArray trianglesToken) || trianglesToken.Count == 0)
            {
                return new ErrorResponse("request.invalid", "triangles is required and must be a non-empty array.", "request", false);
            }
            if (verticesToken.Count > MaxVertices)
            {
                return new ErrorResponse("request.invalid", "vertices exceeds the maximum of " + MaxVertices + ".", "request", false);
            }
            if (trianglesToken.Count > MaxIndices)
            {
                return new ErrorResponse("request.invalid", "triangles exceeds the maximum of " + MaxIndices + ".", "request", false);
            }
            if (trianglesToken.Count % 3 != 0)
            {
                return new ErrorResponse("request.invalid", "triangles length must be a multiple of 3.", "request", false);
            }

            var vertices = ParseVector3Array(verticesToken);
            var triangles = ParseIntArray(trianglesToken);
            for (var i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] >= vertices.Length)
                {
                    return new ErrorResponse("request.invalid", "triangle index " + triangles[i] + " is out of range for " + vertices.Length + " vertices.", "request", false);
                }
            }

            Vector3[] normals = null;
            if (req.Body["normals"] is JArray normalsToken)
            {
                if (normalsToken.Count != vertices.Length)
                {
                    return new ErrorResponse("request.invalid", "normals length must equal vertices length.", "request", false);
                }
                normals = ParseVector3Array(normalsToken);
            }

            Vector2[] uv = null;
            if (req.Body["uv"] is JArray uvToken)
            {
                if (uvToken.Count != vertices.Length)
                {
                    return new ErrorResponse("request.invalid", "uv length must equal vertices length.", "request", false);
                }
                uv = ParseVector2Array(uvToken);
            }

            path = AuthoringHandlers.ResolveConflict(path, req);
            error = AuthoringHandlers.EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createProceduralMesh" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path, vertexCount = vertices.Length, triangleCount = triangles.Length / 3 };
                return response;
            }

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            if (vertices.Length > 65535)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            if (uv != null)
            {
                mesh.SetUVs(0, uv);
            }
            if (normals != null)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();

            AuthoringHandlers.EnsureAssetFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();
            response.Result = new { path, vertexCount = mesh.vertexCount, triangleCount = mesh.triangles.Length / 3 };
            return response;
        }

        internal static object AssignMesh(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var goPath = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, goPath);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var meshPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("meshPath", null));
            if (string.IsNullOrEmpty(meshPath))
            {
                return new ErrorResponse("request.invalid", "meshPath is required.", "request", false);
            }
            var pathError = AuthoringHandlers.ValidateAssetPath(meshPath, ".asset");
            if (pathError != null)
            {
                return pathError;
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                return new ErrorResponse("request.invalid", "Mesh asset not found: " + meshPath, "request", false);
            }

            var materialPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("materialPath", null));
            Material material = null;
            if (!string.IsNullOrEmpty(materialPath))
            {
                var matError = AuthoringHandlers.ValidateAssetPath(materialPath, ".mat");
                if (matError != null)
                {
                    return matError;
                }
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    return new ErrorResponse("request.invalid", "Material asset not found: " + materialPath, "request", false);
                }
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "assignMesh" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go), meshPath };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Assign Mesh");

            var filter = go.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = Undo.AddComponent<MeshFilter>(go);
            }
            Undo.RecordObject(filter, "ScenePort Assign Mesh");
            filter.sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = Undo.AddComponent<MeshRenderer>(go);
            }
            if (material != null)
            {
                Undo.RecordObject(renderer, "ScenePort Assign Material");
                renderer.sharedMaterial = material;
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);
            response.Result = new { gameObject = ScenePortObjects.BuildRef(go), meshPath, materialPath };
            return response;
        }

        private static bool TryParsePrimitive(string shape, out PrimitiveType primitive)
        {
            switch ((shape ?? string.Empty).ToLowerInvariant())
            {
                case "box":
                case "cube":
                    primitive = PrimitiveType.Cube;
                    return true;
                case "sphere":
                    primitive = PrimitiveType.Sphere;
                    return true;
                case "cylinder":
                    primitive = PrimitiveType.Cylinder;
                    return true;
                case "capsule":
                    primitive = PrimitiveType.Capsule;
                    return true;
                case "plane":
                    primitive = PrimitiveType.Plane;
                    return true;
                case "quad":
                    primitive = PrimitiveType.Quad;
                    return true;
                default:
                    primitive = PrimitiveType.Cube;
                    return false;
            }
        }

        // Derive a primitive's geometry from a throwaway GameObject, copy its mesh into one we
        // own (the built-in shared mesh is not a savable asset), then destroy the temp object.
        private static Mesh BuildPrimitiveMesh(PrimitiveType primitive, Vector3 size)
        {
            GameObject temp = null;
            try
            {
                temp = GameObject.CreatePrimitive(primitive);
                var source = temp.GetComponent<MeshFilter>().sharedMesh;
                var mesh = UnityEngine.Object.Instantiate(source);
                mesh.name = primitive.ToString();
                if (size != Vector3.one)
                {
                    var verts = mesh.vertices;
                    for (var i = 0; i < verts.Length; i++)
                    {
                        verts[i] = Vector3.Scale(verts[i], size);
                    }
                    mesh.vertices = verts;
                    mesh.RecalculateBounds();
                }
                return mesh;
            }
            finally
            {
                if (temp != null)
                {
                    UnityEngine.Object.DestroyImmediate(temp);
                }
            }
        }

        private static Vector3[] ParseVector3Array(JArray array)
        {
            var result = new Vector3[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JObject obj)
                {
                    result[i] = new Vector3(F(obj, "x"), F(obj, "y"), F(obj, "z"));
                }
                else if (array[i] is JArray tuple && tuple.Count >= 3)
                {
                    result[i] = new Vector3(tuple[0].Value<float>(), tuple[1].Value<float>(), tuple[2].Value<float>());
                }
            }
            return result;
        }

        private static Vector2[] ParseVector2Array(JArray array)
        {
            var result = new Vector2[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JObject obj)
                {
                    result[i] = new Vector2(F(obj, "x"), F(obj, "y"));
                }
                else if (array[i] is JArray tuple && tuple.Count >= 2)
                {
                    result[i] = new Vector2(tuple[0].Value<float>(), tuple[1].Value<float>());
                }
            }
            return result;
        }

        private static int[] ParseIntArray(JArray array)
        {
            var result = new int[array.Count];
            for (var i = 0; i < array.Count; i++)
            {
                result[i] = array[i].Value<int>();
            }
            return result;
        }

        private static float F(JObject obj, string key)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? 0f : token.Value<float>();
        }
    }
}
