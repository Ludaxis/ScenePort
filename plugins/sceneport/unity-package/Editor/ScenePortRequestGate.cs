using System;
using System.Net;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Pure request gate that rejects unsafe requests before any body read or main-thread
    /// work. The Origin and Host checks together defeat browser-initiated CSRF and DNS
    /// rebinding; the token check covers non-browser local access. Kept free of
    /// HttpListener types so it can be unit-tested directly.
    /// </summary>
    internal static class ScenePortRequestGate
    {
        internal static ScenePortGateResult EvaluateRequest(HttpListenerRequest request, ScenePortContext ctx)
        {
            var path = ScenePortRouter.Normalize(request.Url.AbsolutePath);
            return Evaluate(
                request.HttpMethod,
                request.UserHostName,
                request.Headers["Origin"],
                request.ContentType,
                request.HasEntityBody,
                path,
                request.Headers[ScenePortAuth.TokenHeader],
                ctx.TokenRequired,
                ctx.Token);
        }

        internal static ScenePortGateResult Evaluate(
            string method,
            string host,
            string origin,
            string contentType,
            bool hasBody,
            string path,
            string tokenHeader,
            bool tokenRequired,
            string expectedToken)
        {
            method = (method ?? string.Empty).ToUpperInvariant();
            if (method == "OPTIONS")
            {
                // We deliberately never satisfy a CORS preflight.
                return Reject(403, "ScenePort does not accept CORS preflight requests.");
            }

            if (method != "GET" && method != "POST")
            {
                return Reject(405, "Method not allowed.");
            }

            // Browsers attach an Origin header to cross-origin requests and all POSTs;
            // native clients (Node fetch, curl) send none. Rejecting it kills CSRF.
            if (!string.IsNullOrEmpty(origin))
            {
                return Reject(403, "Requests with an Origin header are rejected. ScenePort does not accept browser-initiated requests.");
            }

            if (!IsLoopbackHost(host))
            {
                return Reject(403, "Requests must target 127.0.0.1 or localhost.");
            }

            if (method == "POST" && hasBody && !StartsWithJson(contentType))
            {
                return Reject(415, "POST bodies must be application/json.");
            }

            if (tokenRequired && path != "/health")
            {
                if (!ScenePortAuth.FixedTimeEquals(tokenHeader, expectedToken))
                {
                    return Reject(401,
                        "Missing or invalid ScenePort token. Update both the ScenePort Unity package and the " +
                        "ScenePort MCP server to v0.3+, then restart your MCP client. The token is read automatically " +
                        "from <project>/Library/ScenePort/bridge.json — set SCENEPORT_PROJECT_PATH if your MCP client " +
                        "does not run from inside the Unity project.");
                }
            }

            return null;
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            var hostName = host;
            var colon = host.LastIndexOf(':');
            if (colon >= 0)
            {
                hostName = host.Substring(0, colon);
            }

            return string.Equals(hostName, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithJson(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                && contentType.TrimStart().StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static ScenePortGateResult Reject(int status, string message)
        {
            return new ScenePortGateResult(status, ScenePortJson.Serialize(new ErrorResponse(message)));
        }
    }
}
