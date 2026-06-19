using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Animation authoring handlers. Like every ScenePort mutation these follow the authoring
    /// contract: extract -> validate -> dry-run barrier -> mutate -> AuthoringResponse, reusing
    /// the shared path/conflict/folder helpers on <see cref="AuthoringHandlers"/>. Asset creation
    /// is rollback-tracked (the batch engine deletes created assets on failure); animator
    /// assignment to a scene object is Undo-wrapped. UnityEditor.Animations ships inside the core
    /// UnityEditor assembly, so no extra asmdef reference is required.
    /// </summary>
    internal static class AnimationHandlers
    {
        // Defensive caps so a malformed or hostile request cannot allocate unbounded data.
        private const int MaxCurves = 256;
        private const int MaxKeysPerCurve = 4096;
        private const int MaxParameters = 64;
        private const int MaxConditions = 32;

        internal static object CreateAnimationClip(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = AuthoringHandlers.ValidateAssetPath(path, ".anim");
            if (error != null)
            {
                return error;
            }

            var curvesToken = req.Body["curves"] as JArray;
            if (curvesToken != null && curvesToken.Count > MaxCurves)
            {
                return new ErrorResponse("request.invalid", "curves exceeds the maximum of " + MaxCurves + ".", "request", false);
            }

            path = AuthoringHandlers.ResolveConflict(path, req);
            error = AuthoringHandlers.EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }

            var clipName = req.ExtractString("name", System.IO.Path.GetFileNameWithoutExtension(path));
            var curveCount = curvesToken?.Count ?? 0;

            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createAnimationClip" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path, name = clipName, curveCount };
                return response;
            }

            var clip = new AnimationClip { name = clipName };
            if (curvesToken != null)
            {
                for (var i = 0; i < curvesToken.Count; i++)
                {
                    if (!(curvesToken[i] is JObject curveObj))
                    {
                        continue;
                    }
                    var relPath = Value(curveObj, "path", string.Empty);
                    var typeName = Value(curveObj, "type", "UnityEngine.Transform");
                    var property = Value(curveObj, "property", null);
                    if (string.IsNullOrEmpty(property))
                    {
                        continue;
                    }
                    var componentType = ResolveComponentType(typeName);
                    if (componentType == null)
                    {
                        return new ErrorResponse("request.invalid", "Unknown component type for curve: " + typeName, "request", false);
                    }

                    var keysToken = curveObj["keys"] as JArray;
                    if (keysToken == null || keysToken.Count == 0)
                    {
                        continue;
                    }
                    if (keysToken.Count > MaxKeysPerCurve)
                    {
                        return new ErrorResponse("request.invalid", "keys per curve exceeds the maximum of " + MaxKeysPerCurve + ".", "request", false);
                    }

                    var keyframes = new Keyframe[keysToken.Count];
                    for (var k = 0; k < keysToken.Count; k++)
                    {
                        var key = keysToken[k] as JObject;
                        keyframes[k] = new Keyframe(Float(key, "time", 0f), Float(key, "value", 0f));
                    }
                    var curve = new AnimationCurve(keyframes);
                    var binding = EditorCurveBinding.FloatCurve(relPath, componentType, property);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            AuthoringHandlers.EnsureAssetFolder(System.IO.Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            response.Result = new { path, name = clip.name, curveCount };
            return response;
        }

        internal static object CreateAnimatorController(ScenePortRequest req, ScenePortContext ctx)
        {
            var path = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("path", req.GetString("path", null)));
            var dryRun = req.ExtractBool("dryRun", false);
            var error = AuthoringHandlers.ValidateAssetPath(path, ".controller");
            if (error != null)
            {
                return error;
            }

            var parametersToken = req.Body["parameters"] as JArray;
            if (parametersToken != null && parametersToken.Count > MaxParameters)
            {
                return new ErrorResponse("request.invalid", "parameters exceeds the maximum of " + MaxParameters + ".", "request", false);
            }

            path = AuthoringHandlers.ResolveConflict(path, req);
            error = AuthoringHandlers.EnsureDoesNotExist(path);
            if (error != null)
            {
                return error;
            }

            // Validate parameter types up-front so a dry run reports the same errors as a real run.
            if (parametersToken != null)
            {
                for (var i = 0; i < parametersToken.Count; i++)
                {
                    if (!(parametersToken[i] is JObject paramObj))
                    {
                        continue;
                    }
                    var typeName = Value(paramObj, "type", "Float");
                    if (!TryParseParameterType(typeName, out _))
                    {
                        return new ErrorResponse("request.invalid", "Unknown animator parameter type: " + typeName + ". Use float, int, bool, or trigger.", "request", false);
                    }
                }
            }

            var parameterCount = parametersToken?.Count ?? 0;
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "createAnimatorController" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "create", path, false, true));
            if (dryRun)
            {
                response.Result = new { path, parameterCount };
                return response;
            }

            AuthoringHandlers.EnsureAssetFolder(System.IO.Path.GetDirectoryName(path));
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if (controller == null)
            {
                return new ErrorResponse("operation.failed", "Unity did not create an animator controller at: " + path, "bridge", false);
            }

            if (parametersToken != null)
            {
                for (var i = 0; i < parametersToken.Count; i++)
                {
                    if (!(parametersToken[i] is JObject paramObj))
                    {
                        continue;
                    }
                    var name = Value(paramObj, "name", null);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    TryParseParameterType(Value(paramObj, "type", "Float"), out var paramType);
                    controller.AddParameter(name, paramType);
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            response.Result = new { path, parameterCount };
            return response;
        }

        internal static object AddAnimatorState(ScenePortRequest req, ScenePortContext ctx)
        {
            var controllerPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("controllerPath", req.GetString("controllerPath", null)));
            var error = AuthoringHandlers.ValidateAssetPath(controllerPath, ".controller");
            if (error != null)
            {
                return error;
            }
            var stateName = req.ExtractString("stateName", req.GetString("stateName", null));
            if (string.IsNullOrEmpty(stateName))
            {
                return new ErrorResponse("request.invalid", "stateName is required.", "request", false);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                return new ErrorResponse("request.invalid", "Animator controller not found: " + controllerPath, "request", false);
            }
            if (controller.layers == null || controller.layers.Length == 0)
            {
                return new ErrorResponse("request.invalid", "Animator controller has no layers: " + controllerPath, "request", false);
            }

            AnimationClip motion = null;
            var motionPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("motionPath", null));
            if (!string.IsNullOrEmpty(motionPath))
            {
                var motionError = AuthoringHandlers.ValidateAssetPath(motionPath, ".anim");
                if (motionError != null)
                {
                    return motionError;
                }
                motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(motionPath);
                if (motion == null)
                {
                    return new ErrorResponse("request.invalid", "Animation clip not found: " + motionPath, "request", false);
                }
            }

            var isDefault = req.ExtractBool("isDefault", false);
            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "addAnimatorState" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "modify", controllerPath, false, false));
            if (dryRun)
            {
                response.Result = new { controllerPath, stateName, motionPath, isDefault };
                return response;
            }

            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.AddState(stateName);
            if (motion != null)
            {
                state.motion = motion;
            }
            if (isDefault)
            {
                stateMachine.defaultState = state;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            response.Result = new { controllerPath, stateName, motionPath, isDefault };
            return response;
        }

        internal static object AddAnimatorTransition(ScenePortRequest req, ScenePortContext ctx)
        {
            var controllerPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("controllerPath", req.GetString("controllerPath", null)));
            var error = AuthoringHandlers.ValidateAssetPath(controllerPath, ".controller");
            if (error != null)
            {
                return error;
            }
            var fromName = req.ExtractString("fromState", req.GetString("fromState", null));
            var toName = req.ExtractString("toState", req.GetString("toState", null));
            if (string.IsNullOrEmpty(fromName) || string.IsNullOrEmpty(toName))
            {
                return new ErrorResponse("request.invalid", "fromState and toState are required.", "request", false);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                return new ErrorResponse("request.invalid", "Animator controller not found: " + controllerPath, "request", false);
            }
            if (controller.layers == null || controller.layers.Length == 0)
            {
                return new ErrorResponse("request.invalid", "Animator controller has no layers: " + controllerPath, "request", false);
            }

            var stateMachine = controller.layers[0].stateMachine;
            var fromState = FindState(stateMachine, fromName);
            var toState = FindState(stateMachine, toName);
            if (fromState == null)
            {
                return new ErrorResponse("request.invalid", "fromState not found in controller: " + fromName, "request", false);
            }
            if (toState == null)
            {
                return new ErrorResponse("request.invalid", "toState not found in controller: " + toName, "request", false);
            }

            var conditionsToken = req.Body["conditions"] as JArray;
            if (conditionsToken != null && conditionsToken.Count > MaxConditions)
            {
                return new ErrorResponse("request.invalid", "conditions exceeds the maximum of " + MaxConditions + ".", "request", false);
            }
            // Validate condition modes up-front so a dry run reports the same errors as a real run.
            if (conditionsToken != null)
            {
                for (var i = 0; i < conditionsToken.Count; i++)
                {
                    if (!(conditionsToken[i] is JObject condObj))
                    {
                        continue;
                    }
                    if (!TryParseConditionMode(Value(condObj, "mode", "Greater"), out _))
                    {
                        return new ErrorResponse("request.invalid", "Unknown transition condition mode: " + Value(condObj, "mode", null) + ". Use if, ifNot, greater, less, equals, or notEqual.", "request", false);
                    }
                }
            }

            var conditionCount = conditionsToken?.Count ?? 0;
            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "addAnimatorTransition" };
            response.Changes.Add(AuthoringHandlers.Change("asset", "modify", controllerPath, false, false));
            if (dryRun)
            {
                response.Result = new { controllerPath, fromState = fromName, toState = toName, conditionCount };
                return response;
            }

            var transition = fromState.AddTransition(toState);
            if (conditionsToken != null)
            {
                for (var i = 0; i < conditionsToken.Count; i++)
                {
                    if (!(conditionsToken[i] is JObject condObj))
                    {
                        continue;
                    }
                    var parameter = Value(condObj, "parameter", null);
                    if (string.IsNullOrEmpty(parameter))
                    {
                        continue;
                    }
                    TryParseConditionMode(Value(condObj, "mode", "Greater"), out var mode);
                    transition.AddCondition(mode, Float(condObj, "threshold", 0f), parameter);
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            response.Result = new { controllerPath, fromState = fromName, toState = toName, conditionCount };
            return response;
        }

        internal static object AssignAnimator(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var goPath = req.ExtractString("path", req.GetString("path", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, goPath);
            if (go == null)
            {
                return new ErrorResponse("request.invalid", "GameObject not found. Provide instanceId or hierarchy path.", "request", false);
            }

            var controllerPath = AuthoringHandlers.NormalizeAssetPath(req.ExtractString("controllerPath", req.GetString("controllerPath", null)));
            var pathError = AuthoringHandlers.ValidateAssetPath(controllerPath, ".controller");
            if (pathError != null)
            {
                return pathError;
            }
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (controller == null)
            {
                return new ErrorResponse("request.invalid", "Animator controller not found: " + controllerPath, "request", false);
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "assignAnimator" };
            response.Changes.Add(AuthoringHandlers.Change("scene", "modify", null, true, true));
            if (dryRun)
            {
                response.Result = new { gameObject = ScenePortObjects.BuildRef(go), controllerPath };
                return response;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Assign Animator");

            var animator = go.GetComponent<Animator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<Animator>(go);
            }
            Undo.RecordObject(animator, "ScenePort Assign Animator");
            animator.runtimeAnimatorController = controller;

            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);
            response.Result = new { gameObject = ScenePortObjects.BuildRef(go), controllerPath };
            return response;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string name)
        {
            var states = stateMachine.states;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].state != null && string.Equals(states[i].state.name, name, StringComparison.Ordinal))
                {
                    return states[i].state;
                }
            }
            return null;
        }

        // Resolve a component type for a curve binding. Try the fully-qualified UnityEngine name
        // first, then a bare type name under the UnityEngine assembly, mirroring how Unity expects
        // curve bindings to name their target component type.
        private static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return typeof(Transform);
            }
            var direct = Type.GetType(typeName, false);
            if (direct != null)
            {
                return direct;
            }
            var qualified = typeName.Contains(".") ? typeName : "UnityEngine." + typeName;
            return Type.GetType(qualified + ", UnityEngine", false)
                ?? Type.GetType(qualified + ", UnityEngine.CoreModule", false);
        }

        private static bool TryParseParameterType(string value, out AnimatorControllerParameterType type)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "float":
                    type = AnimatorControllerParameterType.Float;
                    return true;
                case "int":
                    type = AnimatorControllerParameterType.Int;
                    return true;
                case "bool":
                    type = AnimatorControllerParameterType.Bool;
                    return true;
                case "trigger":
                    type = AnimatorControllerParameterType.Trigger;
                    return true;
                default:
                    type = AnimatorControllerParameterType.Float;
                    return false;
            }
        }

        private static bool TryParseConditionMode(string value, out AnimatorConditionMode mode)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "if":
                case "true":
                    mode = AnimatorConditionMode.If;
                    return true;
                case "ifnot":
                case "false":
                    mode = AnimatorConditionMode.IfNot;
                    return true;
                case "greater":
                case "gt":
                    mode = AnimatorConditionMode.Greater;
                    return true;
                case "less":
                case "lt":
                    mode = AnimatorConditionMode.Less;
                    return true;
                case "equals":
                case "eq":
                    mode = AnimatorConditionMode.Equals;
                    return true;
                case "notequal":
                case "neq":
                    mode = AnimatorConditionMode.NotEqual;
                    return true;
                default:
                    mode = AnimatorConditionMode.Greater;
                    return false;
            }
        }

        private static string Value(JObject obj, string key, string fallback)
        {
            var token = obj?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static float Float(JObject obj, string key, float fallback)
        {
            var token = obj?[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<float>();
        }
    }
}
