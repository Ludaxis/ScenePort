using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

namespace ScenePort.McpBridge.Editor
{
    internal sealed class PlaytestSessionDto
    {
        [JsonProperty("sessionId")] public string SessionId;
        [JsonProperty("label")] public string Label;
        [JsonProperty("status")] public string Status;
        [JsonProperty("startedUtc")] public string StartedUtc;
        [JsonProperty("endedUtc")] public string EndedUtc;
        [JsonProperty("activeScene")] public string ActiveScene;
        [JsonProperty("isPlaying")] public bool IsPlaying;
        [JsonProperty("isPlayingOrWillChangePlaymode")] public bool IsPlayingOrWillChangePlaymode;
        [JsonProperty("elapsedSeconds")] public double ElapsedSeconds;
        [JsonProperty("interactionCount")] public int InteractionCount;
        [JsonProperty("captureCount")] public int CaptureCount;
        [JsonProperty("errorCount")] public int ErrorCount;
        [JsonProperty("warningCount")] public int WarningCount;
        [JsonProperty("lastAction")] public string LastAction;
    }

    internal sealed class PlaytestInteractionDto
    {
        [JsonProperty("utc")] public string Utc;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("detail")] public string Detail;
    }

    internal sealed class PlaytestCaptureDto
    {
        [JsonProperty("utc")] public string Utc;
        [JsonProperty("path")] public string Path;
        [JsonProperty("superSize")] public int SuperSize;
        [JsonProperty("note")] public string Note;

        // Inline image content (v0.9). Null unless the request asked for an inline capture.
        [JsonProperty("imageBase64", NullValueHandling = NullValueHandling.Ignore)] public string ImageBase64;
        [JsonProperty("width", NullValueHandling = NullValueHandling.Ignore)] public int? Width;
        [JsonProperty("height", NullValueHandling = NullValueHandling.Ignore)] public int? Height;
    }

    internal sealed class PlaytestReportDto
    {
        [JsonProperty("session")] public PlaytestSessionDto Session;
        [JsonProperty("summary")] public string Summary;
        [JsonProperty("observations")] public List<string> Observations = new List<string>();
        [JsonProperty("recommendations")] public List<string> Recommendations = new List<string>();
        [JsonProperty("interactions")] public List<PlaytestInteractionDto> Interactions = new List<PlaytestInteractionDto>();
        [JsonProperty("captures")] public List<PlaytestCaptureDto> Captures = new List<PlaytestCaptureDto>();
        [JsonProperty("logs")] public List<LogEntryDto> Logs = new List<LogEntryDto>();
    }

    internal sealed class PlaytestSessionResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("session")] public PlaytestSessionDto Session;
    }

    internal sealed class PlaytestInteractionResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("interaction")] public PlaytestInteractionDto Interaction;
        [JsonProperty("session")] public PlaytestSessionDto Session;
    }

    internal sealed class PlaytestCaptureResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("capture")] public PlaytestCaptureDto Capture;
        [JsonProperty("session")] public PlaytestSessionDto Session;
    }

    internal sealed class PlaytestReportResponse
    {
        [JsonProperty("status")] public string Status = "ok";
        [JsonProperty("report")] public PlaytestReportDto Report;
    }

    internal static class PlaytestHandlers
    {
        private const int MaxReportLogs = 200;
        private static PlaytestSessionState current;

        internal static object Start(ScenePortRequest req, ScenePortContext ctx)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorResponse("Unity is compiling or updating assets. Playtest start is blocked.");
            }

            var label = req.ExtractString("label", req.GetString("label", "ScenePort Playtest"));
            var enterPlayMode = req.ExtractBool("enterPlayMode", req.GetBool("enterPlayMode", true));
            var captureInitialFrame = req.ExtractBool("captureInitialFrame", req.GetBool("captureInitialFrame", false));
            var superSize = Mathf.Clamp(req.ExtractInt("superSize", req.GetInt("superSize", 1)), 1, 4);
            var scene = SceneManager.GetActiveScene();

            current = new PlaytestSessionState
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Label = label,
                Status = "running",
                StartedUtc = DateTime.UtcNow,
                ActiveScene = scene.name,
            };
            Record("start", "Started playtest '" + label + "' in scene '" + scene.name + "'.");

            if (enterPlayMode && !EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
                Record("play-mode", "Requested enter play mode.");
            }

            if (captureInitialFrame)
            {
                CaptureFrameInternal(superSize, null);
            }

            return new PlaytestSessionResponse { Session = BuildSession(ctx) };
        }

        internal static object Stop(ScenePortRequest req, ScenePortContext ctx)
        {
            EnsureSession();
            var exitPlayMode = req.ExtractBool("exitPlayMode", req.GetBool("exitPlayMode", true));
            var captureFinalFrame = req.ExtractBool("captureFinalFrame", req.GetBool("captureFinalFrame", false));
            var superSize = Mathf.Clamp(req.ExtractInt("superSize", req.GetInt("superSize", 1)), 1, 4);

            if (captureFinalFrame)
            {
                CaptureFrameInternal(superSize, null);
            }

            if (exitPlayMode && EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                Record("play-mode", "Requested exit play mode.");
            }

            current.Status = "stopped";
            current.EndedUtc = DateTime.UtcNow;
            Record("stop", "Stopped playtest.");
            return new PlaytestReportResponse { Report = BuildReport(ctx) };
        }

        internal static object Status(ScenePortRequest req, ScenePortContext ctx)
        {
            return new PlaytestSessionResponse { Session = BuildSession(ctx) };
        }

        internal static object Report(ScenePortRequest req, ScenePortContext ctx)
        {
            return new PlaytestReportResponse { Report = BuildReport(ctx) };
        }

        internal static object CaptureFrame(ScenePortRequest req, ScenePortContext ctx)
        {
            EnsureSession();
            var superSize = Mathf.Clamp(req.ExtractInt("superSize", req.GetInt("superSize", 1)), 1, 4);
            var fileName = req.ExtractString("fileName", req.GetString("fileName", null));
            var inline = req.ExtractBool("inline", req.GetBool("inline", true));
            var maxEdge = Mathf.Clamp(req.ExtractInt("maxEdge", req.GetInt("maxEdge", 1024)), 64, 4096);
            var capture = CaptureFrameInternal(superSize, fileName, inline, maxEdge);
            return new PlaytestCaptureResponse { Capture = capture, Session = BuildSession(ctx) };
        }

        internal static object SendKey(ScenePortRequest req, ScenePortContext ctx)
        {
            EnsureSession();
            var key = req.ExtractString("key", req.GetString("key", null));
            var eventType = req.ExtractString("eventType", req.GetString("eventType", "press")).ToLowerInvariant();
            var modifiers = ParseModifiers(req.ExtractString("modifiers", req.GetString("modifiers", null)));

            if (!TryParseKeyCode(key, out var keyCode))
            {
                return new ErrorResponse("Unknown KeyCode: " + key);
            }

            var window = FocusGameView();
            if (window == null)
            {
                return new ErrorResponse("Unity Game view is not available.");
            }

            if (eventType == "down" || eventType == "press")
            {
                window.SendEvent(BuildKeyEvent(EventType.KeyDown, keyCode, key, modifiers));
            }

            if (eventType == "up" || eventType == "press")
            {
                window.SendEvent(BuildKeyEvent(EventType.KeyUp, keyCode, key, modifiers));
            }

            var interaction = Record("key", "Sent " + eventType + " for key " + keyCode + ".");
            return new PlaytestInteractionResponse { Interaction = interaction, Session = BuildSession(ctx) };
        }

        internal static object SendClick(ScenePortRequest req, ScenePortContext ctx)
        {
            EnsureSession();
            var x = req.ExtractFloat("x", req.GetInt("x", 0));
            var y = req.ExtractFloat("y", req.GetInt("y", 0));
            var button = Mathf.Clamp(req.ExtractInt("button", req.GetInt("button", 0)), 0, 2);
            var eventType = req.ExtractString("eventType", req.GetString("eventType", "press")).ToLowerInvariant();
            var coordinateSpace = req.ExtractString("coordinateSpace", req.GetString("coordinateSpace", "normalized")).ToLowerInvariant();
            var modifiers = ParseModifiers(req.ExtractString("modifiers", req.GetString("modifiers", null)));

            var window = FocusGameView();
            if (window == null)
            {
                return new ErrorResponse("Unity Game view is not available.");
            }

            var mousePosition = coordinateSpace == "pixels"
                ? new Vector2(x, y)
                : new Vector2(Mathf.Clamp01(x) * window.position.width, Mathf.Clamp01(y) * window.position.height);

            if (eventType == "down" || eventType == "press")
            {
                window.SendEvent(BuildMouseEvent(EventType.MouseDown, mousePosition, button, modifiers));
            }

            if (eventType == "up" || eventType == "press")
            {
                window.SendEvent(BuildMouseEvent(EventType.MouseUp, mousePosition, button, modifiers));
            }

            var interaction = Record("click", "Sent " + eventType + " at " + mousePosition + " button " + button + ".");
            return new PlaytestInteractionResponse { Interaction = interaction, Session = BuildSession(ctx) };
        }

        internal static void ResetForTests()
        {
            current = null;
        }

        private static void EnsureSession()
        {
            if (current != null)
            {
                return;
            }

            var scene = SceneManager.GetActiveScene();
            current = new PlaytestSessionState
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Label = "ScenePort Playtest",
                Status = "running",
                StartedUtc = DateTime.UtcNow,
                ActiveScene = scene.name,
            };
            Record("start", "Implicitly started playtest session.");
        }

        private static PlaytestCaptureDto CaptureFrameInternal(int superSize, string fileName, bool inline = false, int maxEdge = 1024)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "playtest-" + current.SessionId + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            }

            var capture = EditorStateHandlers.CaptureGameViewFile(fileName, superSize, inline, maxEdge);
            var dto = new PlaytestCaptureDto
            {
                Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Path = capture.Path,
                SuperSize = capture.SuperSize,
                Note = capture.Note,
                ImageBase64 = capture.ImageBase64,
                Width = capture.Width,
                Height = capture.Height,
            };
            current.Captures.Add(dto);
            Record("capture", "Captured Game view to " + dto.Path + ".");
            return dto;
        }

        private static PlaytestInteractionDto Record(string kind, string detail)
        {
            var interaction = new PlaytestInteractionDto
            {
                Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Kind = kind,
                Detail = detail,
            };

            if (current != null)
            {
                current.Interactions.Add(interaction);
                current.LastAction = detail;
            }

            return interaction;
        }

        private static PlaytestSessionDto BuildSession(ScenePortContext ctx)
        {
            if (current == null)
            {
                return new PlaytestSessionDto
                {
                    SessionId = string.Empty,
                    Label = string.Empty,
                    Status = "idle",
                    StartedUtc = string.Empty,
                    EndedUtc = string.Empty,
                    ActiveScene = SceneManager.GetActiveScene().name,
                    IsPlaying = EditorApplication.isPlaying,
                    IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                    LastAction = string.Empty,
                };
            }

            var logs = LogsSince(ctx);
            var errorCount = CountLogs(logs, "Error", "Exception", "Assert");
            var warningCount = CountLogs(logs, "Warning");
            var end = current.EndedUtc ?? DateTime.UtcNow;

            return new PlaytestSessionDto
            {
                SessionId = current.SessionId,
                Label = current.Label,
                Status = current.Status,
                StartedUtc = current.StartedUtc.ToString("o", CultureInfo.InvariantCulture),
                EndedUtc = current.EndedUtc.HasValue ? current.EndedUtc.Value.ToString("o", CultureInfo.InvariantCulture) : string.Empty,
                ActiveScene = current.ActiveScene,
                IsPlaying = EditorApplication.isPlaying,
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                ElapsedSeconds = Math.Max(0, (end - current.StartedUtc).TotalSeconds),
                InteractionCount = current.Interactions.Count,
                CaptureCount = current.Captures.Count,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                LastAction = current.LastAction ?? string.Empty,
            };
        }

        private static PlaytestReportDto BuildReport(ScenePortContext ctx)
        {
            var session = BuildSession(ctx);
            var report = new PlaytestReportDto
            {
                Session = session,
                Summary = SummaryFor(session),
                Interactions = current == null ? new List<PlaytestInteractionDto>() : new List<PlaytestInteractionDto>(current.Interactions),
                Captures = current == null ? new List<PlaytestCaptureDto>() : new List<PlaytestCaptureDto>(current.Captures),
                Logs = current == null ? new List<LogEntryDto>() : LogsSince(ctx),
            };

            if (session.Status == "idle")
            {
                report.Observations.Add("No playtest session has been started.");
                report.Recommendations.Add("Start a playtest before collecting a report.");
                return report;
            }

            report.Observations.Add("Scene: " + session.ActiveScene + ".");
            report.Observations.Add("Play mode: " + (session.IsPlaying ? "playing" : "not playing") + ".");
            report.Observations.Add("Captured frames: " + session.CaptureCount + ".");
            report.Observations.Add("Interactions sent: " + session.InteractionCount + ".");
            report.Observations.Add("Warnings/errors during session: " + session.WarningCount + "/" + session.ErrorCount + ".");

            if (session.ErrorCount > 0)
            {
                report.Recommendations.Add("Inspect console errors before continuing the playtest loop.");
            }

            if (session.CaptureCount == 0)
            {
                report.Recommendations.Add("Capture at least one Game view frame for visual evidence.");
            }

            if (session.ErrorCount == 0 && session.CaptureCount > 0)
            {
                report.Recommendations.Add("No console errors were observed; compare captured frames against expected gameplay.");
            }

            return report;
        }

        private static string SummaryFor(PlaytestSessionDto session)
        {
            if (session.Status == "idle")
            {
                return "No playtest session has been started.";
            }

            return "Playtest '" + session.Label + "' is " + session.Status + " after " +
                session.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s with " +
                session.CaptureCount + " captures, " + session.InteractionCount + " interactions, and " +
                session.ErrorCount + " errors.";
        }

        private static List<LogEntryDto> LogsSince(ScenePortContext ctx)
        {
            var result = new List<LogEntryDto>();
            if (ctx?.Console == null || current == null)
            {
                return result;
            }

            var logs = ctx.Console.Snapshot(MaxReportLogs, "all");
            for (var i = 0; i < logs.Count; i++)
            {
                if (!DateTime.TryParse(logs[i].Utc, null, DateTimeStyles.RoundtripKind, out var utc) || utc >= current.StartedUtc)
                {
                    result.Add(logs[i]);
                }
            }

            return result;
        }

        private static int CountLogs(List<LogEntryDto> logs, params string[] types)
        {
            var count = 0;
            for (var i = 0; i < logs.Count; i++)
            {
                for (var t = 0; t < types.Length; t++)
                {
                    if (string.Equals(logs[i].Type, types[t], StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static EditorWindow FocusGameView()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                return null;
            }

            var window = EditorWindow.GetWindow(gameViewType);
            window.Focus();
            return window;
        }

        private static Event BuildKeyEvent(EventType type, KeyCode keyCode, string key, EventModifiers modifiers)
        {
            return new Event
            {
                type = type,
                keyCode = keyCode,
                character = CharacterFor(key),
                modifiers = modifiers,
            };
        }

        private static Event BuildMouseEvent(EventType type, Vector2 position, int button, EventModifiers modifiers)
        {
            return new Event
            {
                type = type,
                mousePosition = position,
                button = button,
                clickCount = 1,
                modifiers = modifiers,
            };
        }

        private static char CharacterFor(string key)
        {
            return !string.IsNullOrEmpty(key) && key.Length == 1 ? key[0] : '\0';
        }

        private static bool TryParseKeyCode(string key, out KeyCode keyCode)
        {
            keyCode = KeyCode.None;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            var normalized = key.Trim();
            if (normalized == " ")
            {
                normalized = "Space";
            }
            else if (normalized.Length == 1 && char.IsLetter(normalized[0]))
            {
                normalized = char.ToUpperInvariant(normalized[0]).ToString();
            }
            else if (normalized.Length == 1 && char.IsDigit(normalized[0]))
            {
                normalized = "Alpha" + normalized;
            }

            return Enum.TryParse(normalized, true, out keyCode);
        }

        private static EventModifiers ParseModifiers(string csv)
        {
            var modifiers = EventModifiers.None;
            var parts = ScenePortRequest.SplitCsv(csv);
            if (parts == null)
            {
                return modifiers;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (Enum.TryParse(parts[i], true, out EventModifiers parsed))
                {
                    modifiers |= parsed;
                }
            }

            return modifiers;
        }

        private sealed class PlaytestSessionState
        {
            internal string SessionId;
            internal string Label;
            internal string Status;
            internal DateTime StartedUtc;
            internal DateTime? EndedUtc;
            internal string ActiveScene;
            internal string LastAction;
            internal readonly List<PlaytestInteractionDto> Interactions = new List<PlaytestInteractionDto>();
            internal readonly List<PlaytestCaptureDto> Captures = new List<PlaytestCaptureDto>();
        }
    }
}
