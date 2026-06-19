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
        internal string PolicyProfile = "full-safe-local";
        internal string TokenStorage = "library";
        internal string TokenFingerprint;
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
        private readonly Dictionary<string, ScenePortRoute> routes;
        private readonly ScenePortContext context;

        internal ScenePortRouter(ScenePortContext context)
        {
            this.context = context;
            routes = new Dictionary<string, ScenePortRoute>(StringComparer.Ordinal)
            {
                ["/health"] = Route(EditorStateHandlers.Health, "status", false),
                ["/capabilities"] = Route(EditorStateHandlers.Capabilities, "status", false),
                ["/diagnostics"] = Route(EditorStateHandlers.Diagnostics, "diagnostics", false),
                ["/auth/rotate"] = Route(EditorStateHandlers.AuthRotate, "diagnostics", true),
                ["/scene"] = Route(SceneQueryHandlers.Scene, "scene-query", false),
                ["/scene-hierarchy"] = Route(SceneQueryHandlers.Hierarchy, "scene-query", false),
                ["/selection"] = Route(SceneQueryHandlers.Selection, "scene-query", false),
                ["/console"] = Route(EditorStateHandlers.Console, "console", false),
                ["/console-events"] = Route(PerceptionHandlers.ConsoleEvents, "console-stream", false),
                ["/game-object"] = Route(SceneQueryHandlers.GameObject, "scene-query", false),
                ["/components"] = Route(SceneQueryHandlers.Components, "scene-query", false),
                ["/scene-query"] = Route(PerceptionHandlers.SceneQuery, "perception", false, "POST"),
                ["/component-query"] = Route(PerceptionHandlers.ComponentQuery, "perception", false, "POST"),
                ["/serialized-read"] = Route(PerceptionHandlers.SerializedRead, "typed-serialization", false, "POST"),
                ["/scene-view"] = Route(PerceptionHandlers.SceneView, "scene-view", false),
                ["/capture-scene-view"] = Route(PerceptionHandlers.CaptureSceneView, "scene-view", true),
                ["/runtime-status"] = Route(PerceptionHandlers.RuntimeStatus, "runtime", false),
                ["/runtime-query"] = Route(PerceptionHandlers.RuntimeQuery, "runtime", false, "POST"),
                ["/runtime-object"] = Route(PerceptionHandlers.RuntimeObject, "runtime", false),
                ["/profiler-snapshot"] = Route(PerceptionHandlers.ProfilerSnapshot, "profiler", false),
                ["/asset-graph"] = Route(PerceptionHandlers.AssetGraph, "asset-graph", false, "POST"),
                ["/create-game-object"] = Route(SceneEditHandlers.CreateGameObject, "safe-write", true),
                ["/set-transform"] = Route(SceneEditHandlers.SetTransform, "safe-write", true),
                ["/add-component"] = Route(SceneEditHandlers.AddComponent, "safe-write", true),
                ["/set-serialized-property"] = Route(SceneEditHandlers.SetSerializedProperty, "safe-write", true),
                ["/authoring/validate"] = Route(AuthoringHandlers.Validate, "authoring", true),
                ["/authoring/batch"] = Route(AuthoringHandlers.Batch, "authoring", true),
                ["/create-script"] = Route(AuthoringHandlers.CreateScript, "authoring", true),
                ["/create-material"] = Route(AuthoringHandlers.CreateMaterial, "authoring", true),
                ["/create-prefab"] = Route(AuthoringHandlers.CreatePrefab, "authoring", true),
                ["/create-folder"] = Route(AuthoringHandlers.CreateFolder, "authoring", true),
                ["/create-text-asset"] = Route(AuthoringHandlers.CreateTextAsset, "authoring", true),
                ["/create-shader"] = Route(AuthoringHandlers.CreateShader, "authoring", true),
                ["/mesh/create-primitive"] = Route(MeshHandlers.CreatePrimitiveMesh, "mesh", true),
                ["/mesh/create-procedural"] = Route(MeshHandlers.CreateProceduralMesh, "mesh", true),
                ["/mesh/assign"] = Route(MeshHandlers.AssignMesh, "mesh", true),
                ["/settings/get"] = Route(SettingsHandlers.GetSettings, "settings", false),
                ["/settings/set"] = Route(SettingsHandlers.SetSetting, "settings", true),
                ["/menu-item-allowlist"] = Route(AuthoringHandlers.MenuItemAllowlist, "authoring", false),
                ["/execute-menu-item"] = Route(AuthoringHandlers.ExecuteMenuItem, "authoring", true),
                ["/asset-search"] = Route(AssetHandlers.AssetSearch, "assets", false),
                ["/compilation-status"] = Route(EditorStateHandlers.CompilationStatus, "status", false),
                ["/run-tests"] = Route(TestRunHandlers.RunTests, "tests", true),
                ["/tests-last"] = Route(TestRunHandlers.TestsLast, "tests", false),
                ["/tests/run"] = Route(ProofHandlers.TestsRun, "proof", true),
                ["/tests/status"] = Route(ProofHandlers.TestsStatus, "proof", false),
                ["/tests/wait"] = Route(ProofHandlers.TestsWait, "proof", false),
                ["/tests/artifacts"] = Route(ProofHandlers.TestsArtifacts, "proof", false),
                ["/assertions/catalog"] = Route(ProofHandlers.AssertionsCatalog, "proof", false),
                ["/assertions/evaluate"] = Route(ProofHandlers.AssertionsEvaluate, "proof", false, "POST"),
                ["/golden-frame/capture"] = Route(ProofHandlers.GoldenCapture, "proof", true),
                ["/golden-frame/compare"] = Route(ProofHandlers.GoldenCompare, "proof", false, "POST"),
                ["/golden-frame/approve"] = Route(ProofHandlers.GoldenApprove, "proof", true),
                ["/scenario/run"] = Route(ProofHandlers.ScenarioRun, "proof", true),
                ["/scenario/status"] = Route(ProofHandlers.ScenarioStatus, "proof", false),
                ["/scenario/wait"] = Route(ProofHandlers.ScenarioWait, "proof", false),
                ["/scenario/report"] = Route(ProofHandlers.ScenarioReport, "proof", false),
                ["/metrics"] = Route(ProofHandlers.Metrics, "proof", false),
                ["/perf/probe"] = Route(ProofHandlers.PerfProbe, "proof", false, "POST"),
                ["/perf/check-budget"] = Route(ProofHandlers.PerfCheckBudget, "proof", false, "POST"),
                ["/capture-game-view"] = Route(EditorStateHandlers.CaptureGameView, "capture", true),
                ["/play-mode"] = Route(EditorStateHandlers.PlayMode, "play-mode", true),
                ["/packages"] = Route(AssetHandlers.Packages, "assets", false),
                ["/playtest/start"] = Route(PlaytestHandlers.Start, "playtest", true),
                ["/playtest/stop"] = Route(PlaytestHandlers.Stop, "playtest", true),
                ["/playtest/status"] = Route(PlaytestHandlers.Status, "playtest", false),
                ["/playtest/report"] = Route(PlaytestHandlers.Report, "playtest", false),
                ["/playtest/capture-frame"] = Route(PlaytestHandlers.CaptureFrame, "playtest", true),
                ["/playtest/send-key"] = Route(PlaytestHandlers.SendKey, "playtest", true),
                ["/playtest/send-click"] = Route(PlaytestHandlers.SendClick, "playtest", true),
                ["/audit-log"] = Route(EditorStateHandlers.AuditLog, "audit", false),
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
            if (!routes.TryGetValue(normalized, out var route))
            {
                var notFound = new ErrorResponse(
                    "request.unknown_endpoint",
                    "Unknown endpoint: " + normalized,
                    "request",
                    false);
                return new ScenePortDispatchResult(404, ScenePortJson.Serialize(notFound));
            }

            var request = new ScenePortRequest(queryString, body);
            if (!route.AllowsMethod(method))
            {
                var methodError = new ErrorResponse(
                    "request.method_not_allowed",
                    "Endpoint " + normalized + " does not allow " + method + ".",
                    "request",
                    false,
                    null,
                    "Use one of the endpoint's documented HTTP methods.",
                    new Dictionary<string, object> { { "endpoint", normalized }, { "allowedMethods", route.Methods } });
                return new ScenePortDispatchResult(StatusCodeFor(methodError), ScenePortJson.Serialize(methodError));
            }

            if (!ScenePortPolicy.Allows(context.PolicyProfile, route.Group, route.Mutating))
            {
                var denied = new ErrorResponse(
                    "capability.denied",
                    "ScenePort policy '" + context.PolicyProfile + "' denies endpoint group '" + route.Group + "'.",
                    "auth",
                    false,
                    null,
                    "Change the ScenePort policy profile in Unity only if this team should allow that operation.",
                    new Dictionary<string, object> { { "endpoint", normalized }, { "endpointGroup", route.Group }, { "policyProfile", context.PolicyProfile } });
                if (route.Mutating)
                {
                    context.Audit?.Record(method, normalized, request, denied);
                }
                return new ScenePortDispatchResult(StatusCodeFor(denied), ScenePortJson.Serialize(denied));
            }

            // POST idempotency: a mutating request that repeats a non-empty client-request-id
            // returns the prior response verbatim instead of re-running the state-changing
            // handler. Reads (GET) and read-only POSTs are never deduped. Dispatch runs on the
            // Unity main thread (the bridge marshals every request), so cache access here is
            // already serialized; the cache adds its own lock for defense in depth.
            var mutatingPost = route.Mutating && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
            var clientRequestId = mutatingPost ? request.ExtractString("clientRequestId", null) : null;
            if (mutatingPost && !string.IsNullOrEmpty(clientRequestId)
                && ScenePortIdempotencyCache.TryGet(clientRequestId, out var cached))
            {
                return cached;
            }

            var result = route.Handler(request, context);
            if (ShouldAudit(method, route))
            {
                context.Audit?.Record(method, normalized, request, result);
            }

            var dispatch = new ScenePortDispatchResult(StatusCodeFor(result), ScenePortJson.Serialize(result));
            if (mutatingPost && !string.IsNullOrEmpty(clientRequestId))
            {
                ScenePortIdempotencyCache.Store(clientRequestId, dispatch);
            }

            return dispatch;
        }

        private static ScenePortRoute Route(Func<ScenePortRequest, ScenePortContext, object> handler, string group, bool mutating, string methods = null)
        {
            return new ScenePortRoute(handler, group, mutating, methods ?? (mutating ? "POST" : "GET"));
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
                case "request.method_not_allowed":
                    return 400;
                case "bridge.unauthorized":
                    return 401;
                case "request.unknown_endpoint":
                case "capability.unsupported":
                    return 404;
                case "capability.denied":
                    return 403;
                case "editor.busy.compiling":
                case "editor.busy.updating":
                case "editor.playmode.transition":
                case "editor.main_thread.timeout":
                    return 503;
                default:
                    return 200;
            }
        }

        private static bool ShouldAudit(string method, ScenePortRoute route)
        {
            return route.Mutating && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Normalize(string path)
        {
            var trimmed = string.IsNullOrEmpty(path) ? string.Empty : path.TrimEnd('/');
            return string.IsNullOrEmpty(trimmed) ? "/health" : trimmed;
        }
    }

    /// <summary>
    /// Bounded LRU dedup cache keyed by client-request-id, holding the prior dispatch result.
    /// Used only for mutating POST requests so a retried write (same id) returns the original
    /// response instead of mutating twice. Capacity-bounded with oldest-first eviction so a
    /// long-lived editor session cannot grow it without limit. Thread-safe.
    /// </summary>
    internal static class ScenePortIdempotencyCache
    {
        private const int Capacity = 64;

        private static readonly object Gate = new object();

        // Insertion-ordered: a Dictionary for O(1) lookup plus a Queue tracking key age for
        // O(1) eviction of the oldest entry once Capacity is exceeded.
        private static readonly Dictionary<string, ScenePortDispatchResult> Entries =
            new Dictionary<string, ScenePortDispatchResult>(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new Queue<string>();

        internal static bool TryGet(string id, out ScenePortDispatchResult result)
        {
            if (string.IsNullOrEmpty(id))
            {
                result = null;
                return false;
            }

            lock (Gate)
            {
                return Entries.TryGetValue(id, out result);
            }
        }

        internal static void Store(string id, ScenePortDispatchResult result)
        {
            if (string.IsNullOrEmpty(id) || result == null)
            {
                return;
            }

            lock (Gate)
            {
                if (Entries.ContainsKey(id))
                {
                    // First write wins; keep the original response for a repeated id.
                    return;
                }

                Entries[id] = result;
                Order.Enqueue(id);
                while (Order.Count > Capacity)
                {
                    var oldest = Order.Dequeue();
                    Entries.Remove(oldest);
                }
            }
        }

        internal static int Count
        {
            get
            {
                lock (Gate)
                {
                    return Entries.Count;
                }
            }
        }

        internal static void Clear()
        {
            lock (Gate)
            {
                Entries.Clear();
                Order.Clear();
            }
        }
    }

    internal sealed class ScenePortRoute
    {
        internal readonly Func<ScenePortRequest, ScenePortContext, object> Handler;
        internal readonly string Group;
        internal readonly bool Mutating;
        internal readonly string Methods;

        internal ScenePortRoute(Func<ScenePortRequest, ScenePortContext, object> handler, string group, bool mutating, string methods)
        {
            Handler = handler;
            Group = group;
            Mutating = mutating;
            Methods = methods ?? "GET";
        }

        internal bool AllowsMethod(string method)
        {
            return Methods.IndexOf(method ?? "GET", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class ScenePortPolicy
    {
        internal static readonly string[] AllGroups =
        {
            "status",
            "diagnostics",
            "scene-query",
            "perception",
            "typed-serialization",
            "scene-view",
            "runtime",
            "console",
            "console-stream",
            "profiler",
            "assets",
            "asset-graph",
            "tests",
            "proof",
            "capture",
            "play-mode",
            "playtest",
            "safe-write",
            "authoring",
            "mesh",
            "settings",
            "shadergraph-preview",
            "audit",
        };

        internal static bool Allows(string profile, string group, bool mutating)
        {
            profile = string.IsNullOrEmpty(profile) ? "full-safe-local" : profile;

            // shadergraph-preview rides internal, version-fragile Unity APIs. It is off unless a
            // profile opts in explicitly; full-safe-local (single-developer local trust) does.
            if (group == "shadergraph-preview")
            {
                return profile == "full-safe-local";
            }

            if (profile == "full-safe-local")
            {
                return true;
            }

            if (profile == "read-only")
            {
                return !mutating;
            }

            if (profile == "team-safe")
            {
                return group != "authoring" && group != "safe-write" && group != "play-mode" && group != "playtest"
                    && group != "mesh" && group != "settings";
            }

            if (profile == "playtest")
            {
                return group != "authoring" && group != "safe-write" && group != "mesh" && group != "settings";
            }

            return !mutating;
        }

        internal static PolicyDto BuildDto(string profile)
        {
            var allowed = new List<string>();
            var denied = new List<string>();
            for (var i = 0; i < AllGroups.Length; i++)
            {
                var group = AllGroups[i];
                var mutating = group == "safe-write" || group == "authoring" || group == "play-mode" || group == "playtest" || group == "capture" || group == "tests" || group == "mesh" || group == "settings" || group == "shadergraph-preview";
                if (Allows(profile, group, mutating))
                {
                    allowed.Add(group);
                }
                else
                {
                    denied.Add(group);
                }
            }

            return new PolicyDto
            {
                Profile = string.IsNullOrEmpty(profile) ? "full-safe-local" : profile,
                AllowedEndpointGroups = allowed.ToArray(),
                DeniedEndpointGroups = denied.ToArray(),
            };
        }
    }

    internal static class ScenePortProtocol
    {
        internal const int Version = 3;
        internal const string CapabilitiesHash = "sceneport-staged-trust-v1";

        internal static readonly string[] EndpointGroups =
        {
            "status",
            "diagnostics",
            "scene-query",
            "perception",
            "typed-serialization",
            "scene-view",
            "runtime",
            "console",
            "console-stream",
            "profiler",
            "safe-write",
            "authoring",
            "mesh",
            "settings",
            "shadergraph-preview",
            "assets",
            "asset-graph",
            "tests",
            "proof",
            "capture",
            "play-mode",
            "playtest",
            "audit",
        };
    }
}
