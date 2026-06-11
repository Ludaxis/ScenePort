using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Shared GameObject/Component → DTO mapping and resolution helpers used by the
    /// scene query and scene edit handlers.
    /// </summary>
    internal static class ScenePortObjects
    {
        internal static GameObjectRef BuildRef(GameObject go)
        {
            return new GameObjectRef
            {
                Name = go.name,
                Path = GetPath(go.transform),
                InstanceId = go.GetInstanceID(),
                Active = go.activeSelf,
                ActiveInHierarchy = go.activeInHierarchy,
                ChildCount = go.transform.childCount,
            };
        }

        internal static GameObjectDetail BuildDetail(GameObject go, bool includeComponents, int propertyLimit)
        {
            var detail = new GameObjectDetail
            {
                Name = go.name,
                Path = GetPath(go.transform),
                InstanceId = go.GetInstanceID(),
                Active = go.activeSelf,
                ActiveInHierarchy = go.activeInHierarchy,
                ChildCount = go.transform.childCount,
                Tag = go.tag,
                Layer = go.layer,
                Scene = go.scene.name,
                Transform = BuildTransform(go.transform),
            };

            if (includeComponents)
            {
                detail.Components = BuildComponents(go, propertyLimit);
            }

            return detail;
        }

        internal static TransformDto BuildTransform(Transform transform)
        {
            return new TransformDto
            {
                LocalPosition = new Vector3Dto(transform.localPosition),
                LocalEulerAngles = new Vector3Dto(transform.localEulerAngles),
                LocalScale = new Vector3Dto(transform.localScale),
                WorldPosition = new Vector3Dto(transform.position),
            };
        }

        internal static List<ComponentDto> BuildComponents(GameObject go, int propertyLimit)
        {
            var components = go.GetComponents<Component>();
            var result = new List<ComponentDto>(components.Length);
            for (var i = 0; i < components.Length; i++)
            {
                result.Add(BuildComponent(components[i], propertyLimit, i));
            }

            return result;
        }

        internal static ComponentDto BuildComponent(Component component, int propertyLimit, int index)
        {
            if (component == null)
            {
                return new ComponentDto
                {
                    Index = index,
                    Type = "MissingScript",
                    InstanceId = 0,
                    Enabled = null,
                    Properties = new List<PropertyDto>(),
                };
            }

            var dto = new ComponentDto
            {
                Index = index,
                Type = component.GetType().Name,
                FullType = component.GetType().FullName,
                AssemblyQualifiedName = component.GetType().AssemblyQualifiedName,
                InstanceId = component.GetInstanceID(),
                Properties = BuildProperties(component, propertyLimit),
            };

            if (component is Behaviour behaviour)
            {
                dto.Enabled = behaviour.enabled;
            }
            else if (component is Renderer renderer)
            {
                dto.Enabled = renderer.enabled;
            }
            else
            {
                dto.Enabled = null;
            }

            return dto;
        }

        internal static List<PropertyDto> BuildProperties(Object target, int propertyLimit)
        {
            propertyLimit = Mathf.Clamp(propertyLimit, 0, 300);
            var result = new List<PropertyDto>();
            if (propertyLimit == 0 || target == null)
            {
                return result;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren) && result.Count < propertyLimit)
            {
                enterChildren = false;
                result.Add(new PropertyDto
                {
                    Path = property.propertyPath,
                    DisplayName = property.displayName,
                    Type = property.propertyType.ToString(),
                    Editable = property.editable,
                    Value = SerializedPropertyValue(property),
                });
            }

            return result;
        }

        internal static string SerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null ? "null" : property.objectReferenceValue.name;
                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString();
                default:
                    return property.hasVisibleChildren ? "<object>" : string.Empty;
            }
        }

        internal static ObjectRef BuildObjectRef(Object obj)
        {
            return new ObjectRef
            {
                Name = obj == null ? string.Empty : obj.name,
                InstanceId = obj == null ? 0 : obj.GetInstanceID(),
                Type = obj == null ? "null" : obj.GetType().FullName,
            };
        }

        internal static GameObject ResolveGameObject(int instanceId, string path)
        {
            if (instanceId != 0)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject go)
                {
                    return go;
                }

                if (obj is Component component)
                {
                    return component.gameObject;
                }
            }

            return FindByPath(path);
        }

        internal static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var found = FindInChildren(roots[i].transform, path);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static Transform FindInChildren(Transform current, string path)
        {
            if (GetPath(current) == path)
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                var found = FindInChildren(current.GetChild(i), path);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        internal static string GetPath(Transform transform)
        {
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        internal static string ComponentTypeName(Component component)
        {
            return component == null ? "MissingScript" : component.GetType().Name;
        }
    }
}
