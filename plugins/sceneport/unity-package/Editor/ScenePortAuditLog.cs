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
        private bool loaded;

        internal string Path => System.IO.Path.Combine(ScenePortPaths.ScenePortLibraryPath(), "audit.json");

        internal List<AuditLogEntryDto> Snapshot(int limit)
        {
            limit = Mathf.Clamp(limit, 1, MaxEntries);
            lock (gate)
            {
                LoadIfNeeded();
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
                RequestId = req.ExtractString("clientRequestId", null),
                DryRun = req.ExtractBool("dryRun", false),
                Transactional = req.ExtractBool("transactional", false),
                Operation = req.ExtractString("op", Operation(endpoint)),
                OperationCount = OperationCount(req),
                Paths = Paths(req),
            };

            lock (gate)
            {
                LoadIfNeeded();
                entries.Add(entry);
                while (entries.Count > MaxEntries)
                {
                    entries.RemoveAt(0);
                }
            }

            Persist();
        }

        private static string Operation(string endpoint)
        {
            switch (endpoint)
            {
                case "/create-game-object": return "createGameObject";
                case "/set-transform": return "setTransform";
                case "/add-component": return "addComponent";
                case "/set-serialized-property": return "setSerializedProperty";
                case "/create-script": return "createScript";
                case "/create-material": return "createMaterial";
                case "/create-prefab": return "createPrefab";
                case "/authoring/batch": return "authoringBatch";
                case "/authoring/validate": return "authoringValidate";
                case "/execute-menu-item": return "executeMenuItem";
                default: return endpoint.TrimStart('/');
            }
        }

        private static int OperationCount(ScenePortRequest req)
        {
            var operations = req.Body["operations"] as Newtonsoft.Json.Linq.JArray;
            return operations == null ? 0 : operations.Count;
        }

        private static List<string> Paths(ScenePortRequest req)
        {
            var result = new List<string>();
            var path = req.ExtractString("path", req.ExtractString("folder", null));
            if (!string.IsNullOrEmpty(path))
            {
                result.Add(path);
            }
            var operations = req.Body["operations"] as Newtonsoft.Json.Linq.JArray;
            if (operations != null)
            {
                for (var i = 0; i < operations.Count; i++)
                {
                    var op = operations[i] as Newtonsoft.Json.Linq.JObject;
                    var args = op?["args"] as Newtonsoft.Json.Linq.JObject;
                    var token = args?["path"] ?? args?["folder"];
                    if (token != null && token.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        result.Add(token.ToString());
                    }
                }
            }
            return result;
        }

        private void LoadIfNeeded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;

            try
            {
                if (!File.Exists(Path))
                {
                    return;
                }
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<AuditLogResponse>(File.ReadAllText(Path), ScenePortJson.Settings);
                if (parsed?.Entries == null)
                {
                    return;
                }
                entries.Clear();
                var start = Math.Max(0, parsed.Entries.Count - MaxEntries);
                for (var i = start; i < parsed.Entries.Count; i++)
                {
                    entries.Add(parsed.Entries[i]);
                }
            }
            catch
            {
                entries.Clear();
            }
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
