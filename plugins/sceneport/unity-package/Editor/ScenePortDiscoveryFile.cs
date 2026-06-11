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
            public int schemaVersion = 2;
            public string bridge = "sceneport";
            public string bridgeVersion;
            public int protocolVersion;
            public string capabilitiesHash;
            public string url;
            public int port;
            public string token;
            public string projectPath;
            public string projectId;
            public string projectName;
            public string unityVersion;
            public int processId;
            public string processName;
            public string startedUtc;
            public string heartbeatUtc;
            public string expiresUtc;
            public string ownerLeaseId;
            public string editorRole;
        }

        internal static string PathFor(string projectPath)
        {
            return Path.Combine(projectPath, "Library", "ScenePort", "bridge.json");
        }

        internal static string TryReadToken(string projectPath)
        {
            try
            {
                BridgeInfo info;
                if (!TryRead(projectPath, out info))
                {
                    return null;
                }
                return info != null && ScenePortAuth.IsValidTokenFormat(info.token) ? info.token : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool TryRead(string projectPath, out BridgeInfo info)
        {
            info = null;
            try
            {
                var path = PathFor(projectPath);
                if (!File.Exists(path))
                {
                    return false;
                }

                info = JsonUtility.FromJson<BridgeInfo>(File.ReadAllText(path));
                return info != null;
            }
            catch (Exception)
            {
                info = null;
                return false;
            }
        }

        internal static void Write(string projectPath, BridgeInfo info)
        {
            try
            {
                var path = PathFor(projectPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, JsonUtility.ToJson(info, true));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch
                    {
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ScenePort could not write discovery file: " + ex.Message);
            }
        }

        internal static void DeleteIfOwner(string projectPath, string ownerLeaseId)
        {
            try
            {
                BridgeInfo info;
                if (!TryRead(projectPath, out info) || info == null || info.ownerLeaseId == ownerLeaseId)
                {
                    Delete(projectPath);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup.
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
