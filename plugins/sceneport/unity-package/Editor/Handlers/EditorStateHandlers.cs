using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    internal static class EditorStateHandlers
    {
        internal static object Health(ScenePortRequest req, ScenePortContext ctx)
        {
            var scene = SceneManager.GetActiveScene();
            return new HealthResponse
            {
                Version = ctx.Version,
                UnityVersion = Application.unityVersion,
                ProjectPath = ScenePortPaths.ProjectPath(),
                ActiveScene = scene.name,
                IsPlaying = EditorApplication.isPlaying,
                IsCompiling = EditorApplication.isCompiling,
                IsUpdating = EditorApplication.isUpdating,
                Port = ctx.BoundPort,
                ProjectId = PlayerSettings.productGUID.ToString("N"),
                ProjectName = Application.productName,
                TokenRequired = ctx.TokenRequired,
            };
        }

        internal static object Console(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = req.GetInt("limit", 100);
            var type = req.GetString("type", "all");
            return new ConsoleResponse { Logs = ctx.Console.Snapshot(limit, type) };
        }

        internal static object CompilationStatus(ScenePortRequest req, ScenePortContext ctx)
        {
            return new CompilationStatusResponse
            {
                IsCompiling = EditorApplication.isCompiling,
                IsUpdating = EditorApplication.isUpdating,
                IsPlaying = EditorApplication.isPlaying,
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                TimeSinceStartup = EditorApplication.timeSinceStartup,
                RecentErrors = ctx.Console.ErrorSnapshot(50),
            };
        }

        internal static object PlayMode(ScenePortRequest req, ScenePortContext ctx)
        {
            var action = req.ExtractString("action", req.GetString("action", "status")).ToLowerInvariant();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorResponse("Unity is compiling or updating assets. Play mode changes are blocked.");
            }

            if (action == "enter" && !EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = true;
            }
            else if (action == "exit" && EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
            else if (action == "toggle")
            {
                EditorApplication.isPlaying = !EditorApplication.isPlaying;
            }

            return new PlayModeResponse
            {
                Action = action,
                IsPlaying = EditorApplication.isPlaying,
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
            };
        }

        internal static object CaptureGameView(ScenePortRequest req, ScenePortContext ctx)
        {
            var superSize = Mathf.Clamp(req.ExtractInt("superSize", req.GetInt("superSize", 1)), 1, 4);
            var fileName = req.ExtractString("fileName", req.GetString("fileName", null));
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "game-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            }

            fileName = ScenePortPaths.SanitizeFileName(fileName);
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var directory = Path.Combine(ScenePortPaths.ProjectPath(), "Temp", "ScenePort");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            ScreenCapture.CaptureScreenshot(path, superSize);

            return new CaptureGameViewResponse
            {
                Path = path,
                SuperSize = superSize,
                Note = "Unity writes screenshots asynchronously; the file may appear after a short delay.",
            };
        }
    }
}
