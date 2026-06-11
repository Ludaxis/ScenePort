using System;
using System.IO;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// The discovery file at &lt;project&gt;/Library/ScenePort/bridge.json carries the bound
    /// port, the per-project auth token, and project identity so the MCP server can connect
    /// with zero configuration. Library survives editor restarts, so a long-lived MCP
    /// session keeps a stable token even when the port changes.
    /// </summary>
    internal static class ScenePortDiscoveryFile
    {
        [Serializable]
        internal sealed class BridgeInfo
        {
            public int schemaVersion = 1;
            public string bridge = "sceneport";
            public string bridgeVersion;
            public string url;
            public int port;
            public string token;
            public string projectPath;
            public string projectId;
            public string projectName;
            public string unityVersion;
            public int processId;
            public string startedUtc;
        }

        internal static string PathFor(string projectPath)
        {
            return Path.Combine(projectPath, "Library", "ScenePort", "bridge.json");
        }

        internal static string TryReadToken(string projectPath)
        {
            try
            {
                var path = PathFor(projectPath);
                if (!File.Exists(path))
                {
                    return null;
                }

                var info = JsonUtility.FromJson<BridgeInfo>(File.ReadAllText(path));
                return info != null && ScenePortAuth.IsValidTokenFormat(info.token) ? info.token : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static void Write(string projectPath, BridgeInfo info)
        {
            try
            {
                var path = PathFor(projectPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(info, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ScenePort could not write discovery file: " + ex.Message);
            }
        }

        internal static void Delete(string projectPath)
        {
            try
            {
                var path = PathFor(projectPath);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup.
            }
        }
    }
}
