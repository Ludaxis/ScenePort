using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Structured compiler diagnostics that survive domain reloads. Script edits trigger a
    /// domain reload, so the in-memory accumulator is mirrored into <see cref="SessionState"/>
    /// (which survives reloads but not editor restarts) and rehydrated on load. A monotonic
    /// reload epoch lets clients detect when a fresh compile cycle has completed.
    ///
    /// The accumulator is cleared when a compilation starts and appended to as each assembly
    /// finishes, so after a clean build the list reflects the most recent compile cycle.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScenePortCompilation
    {
        private const string MessagesKey = "ScenePort.CompilerMessages";
        private const string ReloadEpochKey = "ScenePort.ReloadEpoch";

        private static readonly object Gate = new object();
        private static List<CompilerMessageRecord> messages = new List<CompilerMessageRecord>();

        static ScenePortCompilation()
        {
            messages = LoadMessages();
            BumpReloadEpoch();

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        /// <summary>Structured compiler messages from the most recent compile cycle.</summary>
        internal static List<CompilerMessageRecord> CompilerMessages
        {
            get
            {
                lock (Gate)
                {
                    return new List<CompilerMessageRecord>(messages);
                }
            }
        }

        /// <summary>Monotonic counter incremented once per domain reload. Survives reloads, not restarts.</summary>
        internal static int ReloadEpoch => SessionState.GetInt(ReloadEpochKey, 0);

        internal static int ErrorCount
        {
            get
            {
                lock (Gate)
                {
                    return CountOfType("error");
                }
            }
        }

        internal static int WarningCount
        {
            get
            {
                lock (Gate)
                {
                    return CountOfType("warning");
                }
            }
        }

        private static void OnCompilationStarted(object context)
        {
            lock (Gate)
            {
                messages = new List<CompilerMessageRecord>();
                Persist();
            }
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] compilerMessages)
        {
            if (compilerMessages == null || compilerMessages.Length == 0)
            {
                return;
            }

            var assembly = System.IO.Path.GetFileNameWithoutExtension(assemblyPath);
            lock (Gate)
            {
                for (var i = 0; i < compilerMessages.Length; i++)
                {
                    var m = compilerMessages[i];
                    messages.Add(new CompilerMessageRecord
                    {
                        File = m.file ?? string.Empty,
                        Line = m.line,
                        Column = m.column,
                        Type = TypeString(m.type),
                        Message = m.message ?? string.Empty,
                        Assembly = assembly ?? string.Empty,
                    });
                }

                Persist();
            }
        }

        /// <summary>Increment helper for the reload epoch. Returns the new epoch. Testable.</summary>
        internal static int BumpReloadEpoch()
        {
            var next = SessionState.GetInt(ReloadEpochKey, 0) + 1;
            SessionState.SetInt(ReloadEpochKey, next);
            return next;
        }

        private static int CountOfType(string type)
        {
            var count = 0;
            for (var i = 0; i < messages.Count; i++)
            {
                if (messages[i].Type == type)
                {
                    count++;
                }
            }
            return count;
        }

        private static void Persist()
        {
            SessionState.SetString(MessagesKey, JsonConvert.SerializeObject(messages, ScenePortJson.Settings));
        }

        private static List<CompilerMessageRecord> LoadMessages()
        {
            var json = SessionState.GetString(MessagesKey, null);
            if (string.IsNullOrEmpty(json))
            {
                return new List<CompilerMessageRecord>();
            }

            try
            {
                return JsonConvert.DeserializeObject<List<CompilerMessageRecord>>(json, ScenePortJson.Settings)
                       ?? new List<CompilerMessageRecord>();
            }
            catch (JsonException)
            {
                return new List<CompilerMessageRecord>();
            }
        }

        internal static string TypeString(CompilerMessageType type)
        {
            switch (type)
            {
                case CompilerMessageType.Error:
                    return "error";
                case CompilerMessageType.Warning:
                    return "warning";
                default:
                    return "info";
            }
        }
    }

    /// <summary>
    /// Serializable, domain-reload-safe record of a single compiler diagnostic. Uses
    /// [JsonProperty] so the SessionState mirror and the wire DTO share the same field shape.
    /// </summary>
    internal sealed class CompilerMessageRecord
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("line")] public int Line;
        [JsonProperty("column")] public int Column;
        [JsonProperty("type")] public string Type;
        [JsonProperty("message")] public string Message;
        [JsonProperty("assembly")] public string Assembly;
    }
}
