using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal static class SceneEditHandlers
    {
        internal static object CreateGameObject(ScenePortRequest req, ScenePortContext ctx)
        {
            var name = req.ExtractString("name", req.GetString("name", null));
            var parentPath = req.ExtractString("parentPath", req.GetString("parentPath", null));
            if (string.IsNullOrEmpty(name))
            {
                return new ErrorResponse("name is required.");
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Create GameObject");

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = ScenePortObjects.FindByPath(parentPath);
                if (parent == null)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    return new ErrorResponse("GameObject parent not found: " + parentPath);
                }
                else
                {
                    Undo.SetTransformParent(go.transform, parent.transform, "Parent " + name);
                }
            }

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            return new GameObjectRefResponse { Object = ScenePortObjects.BuildRef(go) };
        }

        internal static object SetTransform(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var go = ScenePortObjects.ResolveGameObject(instanceId, null);
            if (go == null)
            {
                return new ErrorResponse("GameObject not found for instanceId " + instanceId);
            }

            if (!req.HasObject("position") && !req.HasObject("rotation") && !req.HasObject("scale"))
            {
                return new ErrorResponse("At least one transform field is required: position, rotation, or scale.");
            }

            Undo.RecordObject(go.transform, "ScenePort Set Transform");

            if (req.HasObject("position"))
            {
                go.transform.localPosition = req.GetVector3("position", go.transform.localPosition);
            }

            if (req.HasObject("rotation"))
            {
                go.transform.localEulerAngles = req.GetVector3("rotation", go.transform.localEulerAngles);
            }

            if (req.HasObject("scale"))
            {
                go.transform.localScale = req.GetVector3("scale", go.transform.localScale);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return new GameObjectRefResponse { Object = ScenePortObjects.BuildRef(go) };
        }

        internal static object AddComponent(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var path = req.ExtractString("path", req.GetString("path", null));
            var typeName = req.ExtractString("typeName", req.GetString("typeName", null));
            var go = ScenePortObjects.ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return new ErrorResponse("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var type = ComponentTypeCache.Find(typeName);
            if (type == null)
            {
                return new ErrorResponse("Component type not found: " + typeName);
            }

            if (go.GetComponent(type) != null && type.GetCustomAttributes(typeof(DisallowMultipleComponent), true).Length > 0)
            {
                return new ErrorResponse("GameObject already has a " + type.FullName + " component.");
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Add Component");
            var component = Undo.AddComponent(go, type);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            return new AddComponentResponse
            {
                Object = ScenePortObjects.BuildRef(go),
                Component = ScenePortObjects.BuildComponent(component, 40, Array.IndexOf(go.GetComponents<Component>(), component)),
            };
        }

        internal static object SetSerializedProperty(ScenePortRequest req, ScenePortContext ctx)
        {
            var instanceId = req.ExtractInt("instanceId", req.GetInt("instanceId", 0));
            var componentType = req.ExtractString("componentType", req.GetString("componentType", null));
            var componentIndex = req.ExtractInt("componentIndex", req.GetInt("componentIndex", -1));
            var propertyPath = req.ExtractString("propertyPath", req.GetString("propertyPath", null));
            if (string.IsNullOrEmpty(propertyPath))
            {
                return new ErrorResponse("propertyPath is required.");
            }

            var target = ResolveSerializedTarget(instanceId, componentType, componentIndex);
            if (target == null)
            {
                return new ErrorResponse("Serialized target not found for instanceId " + instanceId);
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return new ErrorResponse("SerializedProperty not found: " + propertyPath);
            }

            if (IsBlockedPropertyPath(propertyPath))
            {
                return new ErrorResponse("SerializedProperty path is not writable through ScenePort: " + propertyPath);
            }

            if (!property.editable)
            {
                return new ErrorResponse("SerializedProperty is not editable: " + propertyPath);
            }

            Undo.RecordObject(target, "ScenePort Set Serialized Property");
            var changed = ApplySerializedValue(property, req);
            if (!changed)
            {
                return new ErrorResponse("Unsupported SerializedProperty type for " + propertyPath + ": " + property.propertyType);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            var go = target is Component component ? component.gameObject : target as GameObject;
            if (go != null)
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            return new SetSerializedPropertyResponse
            {
                Target = ScenePortObjects.BuildObjectRef(target),
                PropertyPath = propertyPath,
                PropertyType = property.propertyType.ToString(),
            };
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
                    foreach (var component in go.GetComponents<Component>())
                    {
                        if (MatchesComponentType(component, componentType))
                        {
                            return component;
                        }
                    }

                    return null;
                }

                return go;
            }

            return null;
        }

        private static bool IsBlockedPropertyPath(string propertyPath)
        {
            return string.Equals(propertyPath, "m_Script", StringComparison.Ordinal)
                || propertyPath.StartsWith("m_Script.", StringComparison.Ordinal)
                || string.Equals(propertyPath, "m_CorrespondingSourceObject", StringComparison.Ordinal)
                || string.Equals(propertyPath, "m_PrefabInstance", StringComparison.Ordinal)
                || string.Equals(propertyPath, "m_PrefabAsset", StringComparison.Ordinal)
                || string.Equals(propertyPath, "m_GameObject", StringComparison.Ordinal);
        }

        private static bool MatchesComponentType(Component component, string typeName)
        {
            if (component == null || string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            var type = component.GetType();
            return string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.AssemblyQualifiedName, typeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ApplySerializedValue(SerializedProperty property, ScenePortRequest req)
        {
            var valueKind = req.ExtractString("valueKind", string.Empty);
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Mathf.RoundToInt(req.ExtractFloat("numberValue", property.intValue));
                    return true;
                case SerializedPropertyType.Boolean:
                    property.boolValue = req.ExtractBool("boolValue", property.boolValue);
                    return true;
                case SerializedPropertyType.Float:
                    property.floatValue = req.ExtractFloat("numberValue", property.floatValue);
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = req.ExtractString("stringValue", property.stringValue);
                    return true;
                case SerializedPropertyType.Color:
                    property.colorValue = req.GetColor("colorValue", property.colorValue);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    var assetPath = req.ExtractString("objectReferenceAssetPath", req.ExtractString("stringValue", null));
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    }
                    else
                    {
                        // Wire a live scene/component reference by instance id. Zero clears it.
                        var refInstanceId = req.ExtractInt("objectReferenceInstanceId", 0);
                        property.objectReferenceValue = refInstanceId == 0 ? null : EditorUtility.InstanceIDToObject(refInstanceId);
                    }
                    return true;
                case SerializedPropertyType.LayerMask:
                    property.intValue = Mathf.RoundToInt(req.ExtractFloat("numberValue", property.intValue));
                    return true;
                case SerializedPropertyType.Enum:
                    var enumValue = req.ExtractString("stringValue", req.ExtractString("enumValue", null));
                    if (!string.IsNullOrEmpty(enumValue))
                    {
                        var index = Array.IndexOf(property.enumNames, enumValue);
                        if (index < 0)
                        {
                            index = Array.IndexOf(property.enumDisplayNames, enumValue);
                        }
                        if (index >= 0)
                        {
                            property.enumValueIndex = index;
                            return true;
                        }
                    }
                    property.enumValueIndex = Mathf.RoundToInt(req.ExtractFloat("numberValue", property.enumValueIndex));
                    return true;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = req.GetVector2(valueKind == "vector2" ? "vector2Value" : "value", property.vector2Value);
                    return true;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = req.GetVector3(valueKind == "vector3" ? "vector3Value" : "value", property.vector3Value);
                    return true;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = req.GetVector4(valueKind == "vector4" ? "vector4Value" : "value", property.vector4Value);
                    return true;
                case SerializedPropertyType.Rect:
                    var rect = property.rectValue;
                    var rectVector = req.GetVector4(valueKind == "vector4" ? "vector4Value" : "value", new Vector4(rect.x, rect.y, rect.width, rect.height));
                    property.rectValue = new Rect(rectVector.x, rectVector.y, rectVector.z, rectVector.w);
                    return true;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = new Bounds(
                        req.GetVector3("center", property.boundsValue.center),
                        req.GetVector3("size", property.boundsValue.size));
                    return true;
                case SerializedPropertyType.Quaternion:
                    var q = property.quaternionValue;
                    var rotation = req.GetVector4(valueKind == "vector4" ? "vector4Value" : "value", new Vector4(q.x, q.y, q.z, q.w));
                    property.quaternionValue = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
                    return true;
                default:
                    return false;
            }
        }
    }
}
