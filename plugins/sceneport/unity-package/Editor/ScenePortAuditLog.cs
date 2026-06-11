using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    internal sealed class ScenePortAuditLog
    {
        private const int MaxEntries = 200;
        private readonly List<AuditLogEntryDto> entries = new List<AuditLogEntryDto>();
        private readonly object gate = new object();

        internal string Path => System.IO.Path.Combine(ScenePortPaths.ScenePortLibraryPath(), "audit.json");

        internal List<AuditLogEntryDto> Snapshot(int limit)
        {
            limit = Mathf.Clamp(limit, 1, MaxEntries);
            lock (gate)
            {
                var start = Math.Max(0, entries.Count - limit);
                return entries.GetRange(start, entries.Count - start);
            }
        }

        internal void Record(string method, string endpoint, ScenePortRequest req, object result)
        {
            var error = result as ErrorResponse;
            var entry = new AuditLogEntryDto
            {
                Utc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                Method = string.IsNullOrEmpty(method) ? "POST" : method.ToUpperInvariant(),
                Endpoint = endpoint,
                Status = error == null ? "ok" : "error",
                Summary = Summary(endpoint, req),
                Target = Target(req),
                Error = error?.Error,
            };

            lock (gate)
            {
                entries.Add(entry);
                while (entries.Count > MaxEntries)
                {
                    entries.RemoveAt(0);
                }
            }

            Persist();
        }

        private void Persist()
        {
            try
            {
                Directory.CreateDirectory(ScenePortPaths.ScenePortLibraryPath());
                File.WriteAllText(Path, ScenePortJson.Serialize(new AuditLogResponse { Path = Path, Entries = Snapshot(MaxEntries) }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ScenePort audit log write failed: " + ex.Message);
            }
        }

        private static string Summary(string endpoint, ScenePortRequest req)
        {
            switch (endpoint)
            {
                case "/create-game-object":
                    return "Create GameObject '" + req.ExtractString("name", "(missing name)") + "'.";
                case "/set-transform":
                    return "Set transform.";
                case "/add-component":
                    return "Add Component '" + req.ExtractString("typeName", "(missing type)") + "'.";
                case "/set-serialized-property":
                    return "Set SerializedProperty '" + req.ExtractString("propertyPath", "(missing path)") + "'.";
                case "/run-tests":
                    return "Run " + req.ExtractString("mode", req.GetString("mode", "tests")) + " tests.";
                case "/capture-game-view":
                    return "Capture Game view.";
                case "/play-mode":
                    return "Play mode action '" + req.ExtractString("action", req.GetString("action", "status")) + "'.";
                case "/playtest/start":
                    return "Start playtest '" + req.ExtractString("label", "ScenePort Playtest") + "'.";
                case "/playtest/stop":
                    return "Stop playtest.";
                case "/playtest/capture-frame":
                    return "Capture playtest frame.";
                case "/playtest/send-key":
                    return "Send key '" + req.ExtractString("key", "(missing key)") + "'.";
                case "/playtest/send-click":
                    return "Send click.";
                default:
                    return endpoint;
            }
        }

        private static string Target(ScenePortRequest req)
        {
            var instanceId = req.ExtractInt("instanceId", 0);
            if (instanceId != 0)
            {
                return "instanceId:" + instanceId;
            }

            var path = req.ExtractString("path", req.ExtractString("parentPath", null));
            if (!string.IsNullOrEmpty(path))
            {
                return "path:" + path;
            }

            return null;
        }
    }
}
