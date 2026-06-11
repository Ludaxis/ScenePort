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
        internal ScenePortAuditLog Audit;
        internal int BoundPort;
        internal string Version = "unknown";
        internal int ProtocolVersion = ScenePortProtocol.Version;
        internal string CapabilitiesHash = ScenePortProtocol.CapabilitiesHash;
        internal string OwnerLeaseId;
        internal string StartedUtc;
        internal string EditorRole;
        internal string ProcessName;
        internal bool TokenRequired;
        internal string Token;
    }

    internal sealed class ScenePortDispatchResult
    {
        internal int StatusCode;
        internal string Body;

        internal ScenePortDispatchResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }
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
                ["/capabilities"] = EditorStateHandlers.Capabilities,
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
                ["/audit-log"] = EditorStateHandlers.AuditLog,
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
        internal string Dispatch(string path, string queryString, string body, string method = "GET")
        {
            return DispatchWithStatus(path, queryString, body, method).Body;
        }

        internal ScenePortDispatchResult DispatchWithStatus(string path, string queryString, string body, string method = "GET")
        {
            var normalized = Normalize(path);
            if (!routes.TryGetValue(normalized, out var handler))
            {
                var notFound = new ErrorResponse(
                    "request.unknown_endpoint",
                    "Unknown endpoint: " + normalized,
                    "request",
                    false);
                return new ScenePortDispatchResult(404, ScenePortJson.Serialize(notFound));
            }

            var request = new ScenePortRequest(queryString, body);
            var result = handler(request, context);
            if (ShouldAudit(method, normalized))
            {
                context.Audit?.Record(method, normalized, request, result);
            }
            return new ScenePortDispatchResult(StatusCodeFor(result), ScenePortJson.Serialize(result));
        }

        private static int StatusCodeFor(object result)
        {
            var error = result as ErrorResponse;
            if (error == null)
            {
                return 200;
            }

            switch (error.Code)
            {
                case "request.invalid":
                    return 400;
                case "bridge.unauthorized":
                    return 401;
                case "request.unknown_endpoint":
                case "capability.unsupported":
                    return 404;
                case "editor.busy.compiling":
                case "editor.busy.updating":
                case "editor.playmode.transition":
                case "editor.main_thread.timeout":
                    return 503;
                default:
                    return 200;
            }
        }

        private static bool ShouldAudit(string method, string path)
        {
            return string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && path != "/audit-log";
        }

        internal static string Normalize(string path)
        {
            var trimmed = string.IsNullOrEmpty(path) ? string.Empty : path.TrimEnd('/');
            return string.IsNullOrEmpty(trimmed) ? "/health" : trimmed;
        }
    }

    internal static class ScenePortProtocol
    {
        internal const int Version = 1;
        internal const string CapabilitiesHash = "sceneport-m0-v1";

        internal static readonly string[] EndpointGroups =
        {
            "status",
            "scene-query",
            "console",
            "safe-write",
            "assets",
            "tests",
            "capture",
            "play-mode",
            "playtest",
            "audit",
        };
    }
}
