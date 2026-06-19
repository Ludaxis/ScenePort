using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Project/Player/Quality/Time/Physics settings, gated behind the dedicated "settings"
    /// capability group (denied by team-safe/playtest/read-only profiles). Writes are
    /// allowlist-only: a key not in <see cref="Keys"/> is rejected, which by construction
    /// hard-denies destructive keys (bundle id, signing, scripting backend). Each write echoes
    /// the previous value so the agent can revert by setting it back; settings APIs are largely
    /// static (no Unity Undo), so this handler is deliberately excluded from authoring batches.
    /// </summary>
    internal static class SettingsHandlers
    {
        private sealed class SettingKey
        {
            internal string Type;
            internal Func<object> Get;
            internal Func<ScenePortRequest, object> Set; // returns the applied value
        }

        private static readonly Dictionary<string, SettingKey> Keys = BuildKeys();

        private static Dictionary<string, SettingKey> BuildKeys()
        {
            return new Dictionary<string, SettingKey>(StringComparer.Ordinal)
            {
                ["player.companyName"] = new SettingKey
                {
                    Type = "string",
                    Get = () => PlayerSettings.companyName,
                    Set = req => PlayerSettings.companyName = req.ExtractString("value", PlayerSettings.companyName),
                },
                ["player.productName"] = new SettingKey
                {
                    Type = "string",
                    Get = () => PlayerSettings.productName,
                    Set = req => PlayerSettings.productName = req.ExtractString("value", PlayerSettings.productName),
                },
                ["quality.level"] = new SettingKey
                {
                    Type = "int",
                    Get = () => QualitySettings.GetQualityLevel(),
                    Set = req =>
                    {
                        var level = Mathf.Clamp(Mathf.RoundToInt(req.ExtractFloat("value", QualitySettings.GetQualityLevel())), 0, QualitySettings.names.Length - 1);
                        QualitySettings.SetQualityLevel(level, true);
                        return level;
                    },
                },
                ["quality.vSyncCount"] = new SettingKey
                {
                    Type = "int",
                    Get = () => QualitySettings.vSyncCount,
                    Set = req => QualitySettings.vSyncCount = Mathf.Clamp(Mathf.RoundToInt(req.ExtractFloat("value", QualitySettings.vSyncCount)), 0, 4),
                },
                ["time.fixedDeltaTime"] = new SettingKey
                {
                    Type = "float",
                    Get = () => Time.fixedDeltaTime,
                    Set = req => Time.fixedDeltaTime = Mathf.Clamp(req.ExtractFloat("value", Time.fixedDeltaTime), 0.0001f, 10f),
                },
                ["time.maximumDeltaTime"] = new SettingKey
                {
                    Type = "float",
                    Get = () => Time.maximumDeltaTime,
                    Set = req => Time.maximumDeltaTime = Mathf.Clamp(req.ExtractFloat("value", Time.maximumDeltaTime), 0.0001f, 10f),
                },
                ["application.targetFrameRate"] = new SettingKey
                {
                    Type = "int",
                    Get = () => Application.targetFrameRate,
                    Set = req => Application.targetFrameRate = Mathf.Clamp(Mathf.RoundToInt(req.ExtractFloat("value", Application.targetFrameRate)), -1, 1000),
                },
                ["physics.gravity"] = new SettingKey
                {
                    Type = "vector3",
                    Get = () => ToVectorDto(Physics.gravity),
                    Set = req =>
                    {
                        var g = req.GetVector3("value", Physics.gravity);
                        Physics.gravity = g;
                        return ToVectorDto(g);
                    },
                },
            };
        }

        internal static object GetSettings(ScenePortRequest req, ScenePortContext ctx)
        {
            var entries = new List<object>(Keys.Count);
            foreach (var pair in Keys)
            {
                entries.Add(new { key = pair.Key, type = pair.Value.Type, value = pair.Value.Get() });
            }
            return new SettingsSnapshotResponse { Settings = entries };
        }

        internal static object SetSetting(ScenePortRequest req, ScenePortContext ctx)
        {
            var key = req.ExtractString("key", req.GetString("key", null));
            if (string.IsNullOrEmpty(key))
            {
                return new ErrorResponse("request.invalid", "key is required.", "request", false);
            }
            if (!Keys.TryGetValue(key, out var setting))
            {
                return new ErrorResponse(
                    "capability.denied",
                    "Setting key is not in the ScenePort allowlist: " + key,
                    "auth",
                    false,
                    null,
                    "Allowed keys: " + string.Join(", ", new List<string>(Keys.Keys).ToArray()));
            }

            var dryRun = req.ExtractBool("dryRun", false);
            var previous = setting.Get();
            var response = new AuthoringResponse { DryRun = dryRun, Operation = "setSetting" };
            response.Changes.Add(new AuthoringChangeDto { Kind = "setting", Action = "modify", Target = key, UndoSupported = false, RollbackSupported = false });
            response.Warnings.Add("Settings changes are not Unity-Undo reversible; revert by setting '" + key + "' back to its previous value.");
            if (dryRun)
            {
                response.Result = new { key, previous, applied = (object)null };
                return response;
            }

            var applied = setting.Set(req);
            response.Result = new { key, previous, applied };
            return response;
        }

        private static object ToVectorDto(Vector3 v)
        {
            return new { x = v.x, y = v.y, z = v.z };
        }
    }
}
