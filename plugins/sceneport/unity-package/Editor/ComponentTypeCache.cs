using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Resolves a Component type from a short name, full name, or assembly-qualified name.
    /// Backed by UnityEditor.TypeCache (fast) and a memoized dictionary, replacing the
    /// original per-call full AppDomain scan. The cache is a static in an editor assembly,
    /// so a domain reload — the only event that can change the type set — clears it.
    /// </summary>
    internal static class ComponentTypeCache
    {
        private static Dictionary<string, Type> lookup;
        private static readonly Dictionary<string, Type> Resolved = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        // Test hook: number of resolutions served from the memo vs. computed.
        internal static int Hits { get; private set; }
        internal static int Misses { get; private set; }

        internal static Type Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            lock (Gate)
            {
                if (Resolved.TryGetValue(typeName, out var memoized))
                {
                    Hits++;
                    return memoized;
                }

                Misses++;
                var type = Resolve(typeName);
                Resolved[typeName] = type; // cache negatives too (cleared on domain reload)
                return type;
            }
        }

        internal static void ResetForTests()
        {
            lock (Gate)
            {
                lookup = null;
                Resolved.Clear();
                Hits = 0;
                Misses = 0;
            }
        }

        private static Type Resolve(string typeName)
        {
            // Exact assembly-qualified or full name first (deterministic).
            var direct = Type.GetType(typeName, false, true);
            if (IsConcreteComponent(direct))
            {
                return direct;
            }

            EnsureLookup();

            // FullName exact match beats short-name match, so resolution is order-independent
            // (the original depended on AppDomain assembly iteration order).
            if (lookup.TryGetValue("full:" + typeName.ToLowerInvariant(), out var byFull))
            {
                return byFull;
            }

            if (lookup.TryGetValue("name:" + typeName.ToLowerInvariant(), out var byName))
            {
                return byName;
            }

            return null;
        }

        private static void EnsureLookup()
        {
            if (lookup != null)
            {
                return;
            }

            lookup = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (type.IsAbstract)
                {
                    continue;
                }

                if (type.FullName != null)
                {
                    var fullKey = "full:" + type.FullName.ToLowerInvariant();
                    if (!lookup.ContainsKey(fullKey))
                    {
                        lookup[fullKey] = type;
                    }
                }

                var nameKey = "name:" + type.Name.ToLowerInvariant();
                if (!lookup.ContainsKey(nameKey))
                {
                    lookup[nameKey] = type; // first-wins on short-name collisions
                }
            }
        }

        private static bool IsConcreteComponent(Type type)
        {
            return type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract;
        }
    }
}
