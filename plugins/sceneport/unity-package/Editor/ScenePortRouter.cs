using System;
using System.Collections.Generic;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Mutable per-bridge context handed to every handler. Fields the bridge updates at
    /// runtime (bound port, auth requirement) are read fresh on each request.
    /// </summary>
    internal sealed class ScenePortContext
    {
        internal ScenePortConsoleBuffer Console;
        internal int BoundPort;
        internal string Version = "unknown";
        internal bool TokenRequired;
        internal string Token;
    }

    /// <summary>
    /// Maps an endpoint path to a handler and serializes the result at a single choke point.
    /// Logical errors are returned as HTTP 200 with a {status:"error"} envelope (handlers
    /// return ErrorResponse). Handler exceptions are NOT caught here — they propagate to the
    /// HTTP server, which returns HTTP 500, preserving the original transport behavior.
    /// </summary>
    internal sealed class ScenePortRouter
    {
        private readonly Dictionary<string, Func<ScenePortRequest, ScenePortContext, object>> routes;
        private readonly ScenePortContext context;

        internal ScenePortRouter(ScenePortContext context)
        {
            this.context = context;
            routes = new Dictionary<string, Func<ScenePortRequest, ScenePortContext, object>>(StringComparer.Ordinal)
            {
                ["/health"] = EditorStateHandlers.Health,
                ["/scene"] = SceneQueryHandlers.Scene,
                ["/scene-hierarchy"] = SceneQueryHandlers.Hierarchy,
                ["/selection"] = SceneQueryHandlers.Selection,
                ["/console"] = EditorStateHandlers.Console,
                ["/game-object"] = SceneQueryHandlers.GameObject,
                ["/components"] = SceneQueryHandlers.Components,
                ["/create-game-object"] = SceneEditHandlers.CreateGameObject,
                ["/set-transform"] = SceneEditHandlers.SetTransform,
                ["/add-component"] = SceneEditHandlers.AddComponent,
                ["/set-serialized-property"] = SceneEditHandlers.SetSerializedProperty,
                ["/asset-search"] = AssetHandlers.AssetSearch,
                ["/compilation-status"] = EditorStateHandlers.CompilationStatus,
                ["/run-tests"] = TestRunHandlers.RunTests,
                ["/tests-last"] = TestRunHandlers.TestsLast,
                ["/capture-game-view"] = EditorStateHandlers.CaptureGameView,
                ["/play-mode"] = EditorStateHandlers.PlayMode,
                ["/packages"] = AssetHandlers.Packages,
                ["/playtest/start"] = PlaytestHandlers.Start,
                ["/playtest/stop"] = PlaytestHandlers.Stop,
                ["/playtest/status"] = PlaytestHandlers.Status,
                ["/playtest/report"] = PlaytestHandlers.Report,
                ["/playtest/capture-frame"] = PlaytestHandlers.CaptureFrame,
                ["/playtest/send-key"] = PlaytestHandlers.SendKey,
                ["/playtest/send-click"] = PlaytestHandlers.SendClick,
            };
        }

        internal bool HasRoute(string path)
        {
            return routes.ContainsKey(Normalize(path));
        }

        /// <summary>
        /// Dispatch a request to its handler and return serialized JSON. Must be called on the
        /// Unity main thread (handlers touch editor state). Throws if a handler throws.
        /// </summary>
        internal string Dispatch(string path, string queryString, string body)
        {
            var normalized = Normalize(path);
            if (!routes.TryGetValue(normalized, out var handler))
            {
                return ScenePortJson.Serialize(new ErrorResponse("Unknown endpoint: " + normalized));
            }

            var request = new ScenePortRequest(queryString, body);
            var result = handler(request, context);
            return ScenePortJson.Serialize(result);
        }

        internal static string Normalize(string path)
        {
            var trimmed = string.IsNullOrEmpty(path) ? string.Empty : path.TrimEnd('/');
            return string.IsNullOrEmpty(trimmed) ? "/health" : trimmed;
        }
    }
}
