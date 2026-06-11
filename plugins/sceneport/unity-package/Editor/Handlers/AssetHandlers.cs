using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal static class AssetHandlers
    {
        internal static object AssetSearch(ScenePortRequest req, ScenePortContext ctx)
        {
            var search = req.GetString("query", req.GetString("q", null));
            if (string.IsNullOrEmpty(search))
            {
                return new ErrorResponse("query is required.");
            }

            var limit = Mathf.Clamp(req.GetInt("limit", 100), 1, 500);
            var folders = (ScenePortRequest.SplitCsv(req.GetString("folders", null)) ?? Array.Empty<string>())
                .Where(f => f.StartsWith("Assets", StringComparison.Ordinal))
                .ToArray();

            var guids = folders.Length > 0 ? AssetDatabase.FindAssets(search, folders) : AssetDatabase.FindAssets(search);
            var count = Mathf.Min(limit, guids.Length);

            var response = new AssetSearchResponse
            {
                Query = search,
                Count = guids.Length,
                Truncated = guids.Length > count,
            };

            for (var i = 0; i < count; i++)
            {
                var guid = guids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                var asset = new AssetDto
                {
                    Guid = guid,
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Type = type == null ? "Unknown" : type.FullName,
                };

                var labels = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path));
                asset.Labels.AddRange(labels);
                response.Assets.Add(asset);
            }

            return response;
        }

        internal static object Packages(ScenePortRequest req, ScenePortContext ctx)
        {
            var projectPath = ScenePortPaths.ProjectPath();
            var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
            var lockPath = Path.Combine(projectPath, "Packages", "packages-lock.json");
            var manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";

            var response = new PackagesResponse
            {
                ManifestPath = manifestPath,
                PackagesLockPath = lockPath,
                PackagesLockExists = File.Exists(lockPath),
            };

            foreach (var dependency in ParseDependencies(manifest))
            {
                response.Dependencies.Add(dependency);
            }

            return response;
        }

        private static System.Collections.Generic.List<DependencyDto> ParseDependencies(string manifest)
        {
            var result = new System.Collections.Generic.List<DependencyDto>();
            try
            {
                var root = JObject.Parse(manifest);
                if (root["dependencies"] is JObject dependencies)
                {
                    foreach (var pair in dependencies)
                    {
                        result.Add(new DependencyDto(pair.Key, pair.Value?.ToString() ?? string.Empty));
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Tolerate a missing or malformed manifest, matching the original regex behavior.
            }

            return result;
        }
    }
}
