using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal static class AuthoringHandlers
    {
        private static readonly string[] MenuAllowlist = { "Assets/Refresh", "Window/General/Console" };

        internal static object Validate(ScenePortRequest req, ScenePortContext ctx)
        {
            var op = req.ExtractString("op", req.GetString("op", "authoring"));
            var response = new AuthoringResponse { DryRun = true, Operation = op };
            var path = req.ExtractString("path", req.GetString("path", null));
            if (!string.IsNullOrEmpty(path))
            {
                var error = ValidateAssetPath(path, null);
                if (error != null)
                {
                    return error;
                }
                response.Changes.Add(Change("asset", "validate", path, false, false));
            }
            return response;
        }

        internal static object Batch(ScenePortRequest req, ScenePortContext ctx)
        {
            var dryRun = req.ExtractBool("dryRun", false);
            var transactional = req.ExtractBool("transactional", true);
            var operations = req.Body["operations"] as JArray;
            if (operations == null || operations.Count == 0)
            {
                return new ErrorResponse("request.invalid", "operations is required.", "request", false);
            }
            if (operations.Count > 25)
            {
                return new ErrorResponse("request.invalid", "Batch operations are limited to 25.", "request", false);
            }

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "authoringBatch" };
            var createdAssets = new List<string>();
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Authoring Batch");

            try
            {
                for (var i = 0; i < operations.Count; i++)
                {
                    var op = operations[i] as JObject;
                    if (op == null)
                    {
                        return new ErrorResponse("request.invalid", "Each batch operation must be an object.", "request", false);
                    }
                    var kind = Value(op, "op", string.Empty);
                    var args = op["args"] as JObject ?? new JObject();
                    args["dryRun"] = dryRun;
                    var result = ExecuteBatchOperation(kind, new ScenePortRequest(req.Query, args), ctx);
                    if (result is ErrorResponse)
                    {
                        if (transactional && !dryRun)
                        {
                            Undo.RevertAllDownToGroup(undoGroup);
                            DeleteCreatedAssets(createdAssets);
                        }
                        return result;
                    }
                    var authoring = result as AuthoringResponse;
                    if (authoring != null)
                    {
                        response.Changes.AddRange(authoring.Changes);
                        for (var j = 0; j < authoring.Changes.Count; j++)
                        {
                            if (authoring.Changes[j].RollbackSupported && !string.IsNullOrEmpty(authoring.Changes[j].Path))
                            {
                                createdAssets.Add(authoring.Changes[j].Path);
                            }
                        }
                    }
                }

                if (!dryRun)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                }
                response.Result = new { operationCount = operations.Count, transactional, undoGroup };
                return response;
            }
            catch (Exception ex)
            {
                if (transactional && !dryRun)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    DeleteCreatedAssets(createdAssets);
                }
                return new ErrorResponse("operation.failed", ex.Message, "bridge", true);
            }
        }

        internal static object CreateScript(ScenePortRequest req, ScenePortContext ctx)
        {
            var className = req.ExtractString("className", req.GetString("className", null));
            var ns = req.ExtractString("namespace", req.GetString("namespace", null));
            var folder = req.ExtractString("folder", req.GetString("folder", "Assets"));
            var kind = req.ExtractString("kind", req.GetString("kind", "MonoBehaviour"));
            var dryRun = req.ExtractBool("dryRun", false);
            if (!IsIdentifier(className))
            {
                return new ErrorResponse("request.invalid", "className must be a valid C# identifier.", "request", false);
            }
            if (!string.IsNullOrEmpty(ns) && !IsNamespace(ns))
            {
                return new ErrorResponse("request.invalid", "namespace must contain valid C# identifiers.", "request", false);
            }

            var fileName = req.ExtractString("fileName", className + ".cs");
            if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".cs";
            }
            var path = NormalizeAssetPath(folder.TrimEnd('/') + "/" + fileName);
            var error = ValidateAssetPath(path, ".cs");
            if (error != null)
            {
                return error;
            }
            path = ResolveConflict(path, req);
            error = EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createScript" };
            response.Changes.Add(Change("script", "create", path, false, true));
            if (dryRun)
            {
                return response;
            }

            EnsureAssetFolder(Path.GetDirectoryName(path));
            File.WriteAllText(Path.Combine(ScenePortPaths.ProjectPath(), path), ScriptTemplate(className, ns, kind));
            AssetDatabase.ImportAsset(path);
            response.Result = new { path, className, kind };
            return response;
        }

        internal static object CreateMaterial(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = ValidateAssetPath(path, ".mat");
            if (error != null)
            {
                return error;
            }
            path = ResolveConflict(path, req);
            error = EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }
            var shaderName = req.ExtractString("shaderName", "Standard");
            var shader = Shader.Find(shaderName) ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return new ErrorResponse("request.invalid", "Shader not found: " + shaderName, "request", false);
            }

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createMaterial" };
            response.Changes.Add(Change("asset", "create", path, false, true));
            if (dryRun)
            {
                return response;
            }

            EnsureAssetFolder(Path.GetDirectoryName(path));
            var material = new Material(shader);
            if (req.Body["color"] is JObject color)
            {
                var c = new Color(Float(color, "r", 1), Float(color, "g", 1), Float(color, "b", 1), Float(color, "a", 1));
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", c);
                }
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", c);
                }
            }
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            response.Result = new { path, shader = shader.name };
            return response;
        }

        internal static object CreatePrefab(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = ValidateAssetPath(path, ".prefab");
            if (error != null)
            {
                return error;
            }
            var source = req.Body["source"] as JObject;
            var instanceId = source == null ? req.ExtractInt("instanceId", 0) : Int(source, "instanceId", 0);
            var sourcePath = source == null ? req.ExtractString("sourcePath", null) : Value(source, "path", null);
            var go = ScenePortObjects.ResolveGameObject(instanceId, sourcePath);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "Prefab source GameObject not found.", "request", false);
            }
            path = ResolveConflict(path, req);
            error = EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createPrefab" };
            response.Changes.Add(Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path, source = ScenePortObjects.BuildRef(go) };
                return response;
            }

            EnsureAssetFolder(Path.GetDirectoryName(path));
            var connect = req.ExtractBool("connectToSource", true);
            var prefab = connect ? PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction) : PrefabUtility.SaveAsPrefabAsset(go, path);
            response.Result = new { path, prefab = prefab == null ? null : prefab.name };
            return response;
        }

        internal static object MenuItemAllowlist(ScenePortRequest req, ScenePortContext ctx)
        {
            return new MenuItemAllowlistResponse { Items = MenuAllowlist };
        }

        internal static object ExecuteMenuItem(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = req.ExtractString("path", req.GetString("path", null));
            var dryRun = req.ExtractBool("dryRun", false);
            if (Array.IndexOf(MenuAllowlist, path) < 0)
            {
                return new ErrorResponse("capability.denied", "Menu item is not allowlisted: " + path, "auth", false);
            }
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "executeMenuItem" };
            response.Changes.Add(Change("menu", "execute", null, false, false, path));
            if (!dryRun)
            {
                if (!EditorApplication.ExecuteMenuItem(path))
                {
                    return new ErrorResponse("operation.failed", "Unity did not execute menu item: " + path, "bridge", false);
                }
            }
            return response;
        }

        private static object ExecuteBatchOperation(string op, ScenePortRequest req, ScenePortContext ctx)
        {
            if (req.ExtractBool("dryRun", false) && IsSceneMutation(op))
            {
                var dryRunResponse = new AuthoringResponse { DryRun = true, Operation = op };
                dryRunResponse.Changes.Add(Change("scene", "modify", null, true, true));
                return dryRunResponse;
            }

            switch (op)
            {
                case "createScript": return CreateScript(req, ctx);
                case "createMaterial": return CreateMaterial(req, ctx);
                case "createPrefab": return CreatePrefab(req, ctx);
                case "executeMenuItem": return ExecuteMenuItem(req, ctx);
                case "createGameObject": return WrapSceneMutation("createGameObject", SceneEditHandlers.CreateGameObject(req, ctx));
                case "setTransform": return WrapSceneMutation("setTransform", SceneEditHandlers.SetTransform(req, ctx));
                case "addComponent": return WrapSceneMutation("addComponent", SceneEditHandlers.AddComponent(req, ctx));
                case "setSerializedProperty": return WrapSceneMutation("setSerializedProperty", SceneEditHandlers.SetSerializedProperty(req, ctx));
                default: return new ErrorResponse("request.invalid", "Unknown batch operation: " + op, "request", false);
            }
        }

        private static bool IsSceneMutation(string op)
        {
            return op == "createGameObject" || op == "setTransform" || op == "addComponent" || op == "setSerializedProperty";
        }

        private static object WrapSceneMutation(string operation, object result)
        {
            if (result is ErrorResponse)
            {
                return result;
            }
            var response = new AuthoringResponse { Operation = operation };
            response.Changes.Add(Change("scene", "modify", null, true, true));
            response.Result = result;
            return response;
        }

        private static ErrorResponse ValidateAssetPath(string path, string extension)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResponse("request.invalid", "Asset path is required.", "request", false);
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && path != "Assets")
            {
                return new ErrorResponse("request.invalid", "Asset path must be under Assets/.", "request", false);
            }
            if (path.IndexOf("..", StringComparison.Ordinal) >= 0 || path.IndexOf('\\') >= 0 || Path.IsPathRooted(path))
            {
                return new ErrorResponse("request.invalid", "Asset path must not be absolute or contain traversal.", "request", false);
            }
            if (path.StartsWith("Assets/../", StringComparison.Ordinal) || path.IndexOf("/Library/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ErrorResponse("request.invalid", "Asset path targets a blocked directory.", "request", false);
            }
            if (!string.IsNullOrEmpty(extension) && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return new ErrorResponse("request.invalid", "Asset path must end with " + extension + ".", "request", false);
            }
            return null;
        }

        private static ErrorResponse EnsureDoesNotExist(string path)
        {
            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))
                ? new ErrorResponse("request.invalid", "Asset already exists: " + path, "request", false, null, "Use onConflict: generateUniquePath to avoid overwriting.")
                : null;
        }

        private static string ResolveConflict(string path, ScenePortRequest req)
        {
            var onConflict = req.ExtractString("onConflict", "error");
            return onConflict == "generateUniquePath" ? AssetDatabase.GenerateUniqueAssetPath(path) : path;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/').Trim();
        }

        private static AuthoringChangeDto Change(string kind, string action, string path, bool undo, bool rollback, string target = null)
        {
            return new AuthoringChangeDto { Kind = kind, Action = action, Path = path, Target = target, UndoSupported = undo, RollbackSupported = rollback };
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder == "Assets" || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void DeleteCreatedAssets(List<string> paths)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                if (!string.IsNullOrEmpty(paths[i]))
                {
                    AssetDatabase.DeleteAsset(paths[i]);
                }
            }
        }

        private static string ScriptTemplate(string className, string ns, string kind)
        {
            var baseClass = kind == "ScriptableObject" ? "ScriptableObject" : kind == "PlainClass" ? string.Empty : "MonoBehaviour";
            var inherit = string.IsNullOrEmpty(baseClass) ? string.Empty : " : " + baseClass;
            var body = "using UnityEngine;\n\npublic class " + className + inherit + "\n{\n}\n";
            if (!string.IsNullOrEmpty(ns))
            {
                body = "using UnityEngine;\n\nnamespace " + ns + "\n{\n    public class " + className + inherit + "\n    {\n    }\n}\n";
            }
            return body;
        }

        private static bool IsIdentifier(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$");
        }

        private static bool IsNamespace(string value)
        {
            var parts = value.Split('.');
            for (var i = 0; i < parts.Length; i++)
            {
                if (!IsIdentifier(parts[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Value(JObject obj, string key, string fallback)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static int Int(JObject obj, string key, int fallback)
        {
            var token = obj[key];
            return token == null ? fallback : token.Value<int>();
        }

        private static float Float(JObject obj, string key, float fallback)
        {
            var token = obj[key];
            return token == null ? fallback : token.Value<float>();
        }
    }
}
