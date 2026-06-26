using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Pure, UI-free logic backing the ScenePort Setup window. Everything here is
    /// deterministic and side-effect free so it can be unit-tested without opening an
    /// EditorWindow or touching the running bridge. The window calls these helpers and
    /// renders the result; the actual <see cref="System.Diagnostics.Process"/> launches,
    /// clipboard writes, and GUI live in <c>ScenePortSetupWindow</c>.
    /// </summary>
    internal static class ScenePortSetup
    {
        /// <summary>Canonical MCP server identifier registered with the AI tool.</summary>
        internal const string ServerName = "sceneport";

        /// <summary>npm package that hosts the MCP server entry point.</summary>
        internal const string NpmPackage = "sceneport-mcp";

        /// <summary>Environment variable the server reads to locate this Unity project.</summary>
        internal const string ProjectPathEnvVar = "SCENEPORT_PROJECT_PATH";

        internal const string DocsUrl = "https://github.com/Ludaxis/ScenePort#readme";
        internal const string RepoUrl = "https://github.com/Ludaxis/ScenePort";

        /// <summary>
        /// Immutable snapshot of bridge/project status rendered by the window. Assembled
        /// either from the live <c>ScenePortBridge</c> static state or from a discovery
        /// <c>BridgeInfo</c> when the bridge is owned by another editor process.
        /// </summary>
        internal sealed class StatusModel
        {
            public bool Running;
            public int Port;
            public string Url = string.Empty;
            public string TokenFingerprint = string.Empty;
            public bool AuthRequired;
            public string ProjectPath = string.Empty;
            public string ProjectName = string.Empty;
            public string UnityVersion = string.Empty;
            public string PolicyProfile = string.Empty;
            public string Source = "none";
        }

        /// <summary>
        /// Builds a status model from a discovery <see cref="ScenePortDiscoveryFile.BridgeInfo"/>.
        /// A null info yields an empty, not-running model. <paramref name="fallbackProjectPath"/>
        /// is used when the discovery file is missing so the window can still show config.
        /// </summary>
        internal static StatusModel BuildStatus(
            ScenePortDiscoveryFile.BridgeInfo info,
            string fallbackProjectPath,
            bool authRequiredFallback)
        {
            if (info == null)
            {
                return new StatusModel
                {
                    Running = false,
                    Port = 0,
                    Url = string.Empty,
                    TokenFingerprint = string.Empty,
                    AuthRequired = authRequiredFallback,
                    ProjectPath = fallbackProjectPath ?? string.Empty,
                    ProjectName = string.Empty,
                    UnityVersion = string.Empty,
                    PolicyProfile = string.Empty,
                    Source = "none",
                };
            }

            return new StatusModel
            {
                Running = info.port > 0,
                Port = info.port,
                Url = string.IsNullOrEmpty(info.url) ? UrlForPort(info.port) : info.url,
                TokenFingerprint = info.tokenFingerprint ?? string.Empty,
                // bridge.json carries the token but not the auth-required flag; the live
                // editor passes the real value, otherwise fall back to the caller's guess.
                AuthRequired = authRequiredFallback,
                ProjectPath = string.IsNullOrEmpty(info.projectPath) ? (fallbackProjectPath ?? string.Empty) : info.projectPath,
                ProjectName = info.projectName ?? string.Empty,
                UnityVersion = info.unityVersion ?? string.Empty,
                PolicyProfile = info.policyProfile ?? string.Empty,
                Source = "discovery",
            };
        }

        internal static string UrlForPort(int port)
        {
            return port > 0 ? "http://127.0.0.1:" + port : string.Empty;
        }

        /// <summary>
        /// Derives a stable, distinct registration key from a project path so several Unity
        /// projects can each register their own MCP server without colliding on the default
        /// "sceneport" key. Slugifies the project folder name, e.g.
        /// "/Users/me/Games/My Game" -> "sceneport-my-game". Falls back to the bare prefix
        /// when no slug can be derived.
        /// </summary>
        internal static string InstanceName(string projectPath, string prefix = ServerName)
        {
            var trimmed = (projectPath ?? string.Empty).TrimEnd('/', '\\');
            var sep = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            var baseName = sep >= 0 ? trimmed.Substring(sep + 1) : trimmed;

            var sb = new StringBuilder(baseName.Length);
            foreach (var ch in baseName.ToLowerInvariant())
            {
                sb.Append((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') ? ch : '-');
            }

            var slug = sb.ToString();
            while (slug.Contains("--"))
            {
                slug = slug.Replace("--", "-");
            }
            slug = slug.Trim('-');

            return string.IsNullOrEmpty(slug) ? prefix : prefix + "-" + slug;
        }

        /// <summary>
        /// The npx-based MCP server config object (without an outer name key). This is the
        /// inner object used both in the Claude one-liner and the Codex/Claude config blocks.
        /// Shape: <c>{ "command": "npx", "args": ["-y","sceneport-mcp"], "env": { ... } }</c>.
        /// </summary>
        internal static JObject NpxServerConfig(string projectPath)
        {
            return new JObject
            {
                ["command"] = "npx",
                ["args"] = new JArray("-y", NpmPackage),
                ["env"] = new JObject
                {
                    [ProjectPathEnvVar] = projectPath ?? string.Empty,
                },
            };
        }

        /// <summary>
        /// The compact JSON for the inner server config, used as the argument to
        /// <c>claude mcp add-json sceneport '&lt;json&gt;'</c>.
        /// </summary>
        internal static string NpxServerConfigJson(string projectPath)
        {
            return NpxServerConfig(projectPath).ToString(Formatting.None);
        }

        /// <summary>
        /// The exact shell command that registers ScenePort with Claude Code via the CLI.
        /// Example: <c>claude mcp add-json sceneport '{"command":"npx",...}'</c>.
        /// </summary>
        internal static string ClaudeAddCommand(string projectPath, string name = ServerName)
        {
            return "claude mcp add-json " + name + " '" + NpxServerConfigJson(projectPath) + "'";
        }

        /// <summary>
        /// Pretty-printed Claude Code config block: the standard <c>mcpServers</c> map with
        /// ScenePort under it. Suitable for pasting into <c>.mcp.json</c> / settings.
        /// </summary>
        internal static string ClaudeConfigJson(string projectPath, string name = ServerName)
        {
            var root = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    [name] = NpxServerConfig(projectPath),
                },
            };
            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Codex MCP config block in TOML form. Codex reads <c>~/.codex/config.toml</c> with
        /// <c>[mcp_servers.&lt;name&gt;]</c> tables.
        /// </summary>
        internal static string CodexConfigToml(string projectPath, string name = ServerName)
        {
            var safePath = (projectPath ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var lines = new List<string>
            {
                "[mcp_servers." + name + "]",
                "command = \"npx\"",
                "args = [\"-y\", \"" + NpmPackage + "\"]",
                "",
                "[mcp_servers." + name + ".env]",
                ProjectPathEnvVar + " = \"" + safePath + "\"",
            };
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Codex MCP config block in JSON form, for Codex builds that accept JSON config.
        /// Mirrors the Claude shape under an <c>mcpServers</c> map.
        /// </summary>
        internal static string CodexConfigJson(string projectPath, string name = ServerName)
        {
            return ClaudeConfigJson(projectPath, name);
        }

        /// <summary>
        /// The doctor command line used for diagnostics. Returns the executable plus its
        /// argument array so the window can spawn it without re-parsing a string.
        /// </summary>
        internal static (string FileName, string[] Args) DoctorCommand()
        {
            return ("npx", new[] { "-y", NpmPackage, "doctor", "--json" });
        }

        // ---- Local (bundled server) helpers --------------------------------
        // These wire the AI tool to the server bundled inside this UPM package
        // (Server~/index.js) via a local `node <serverPath>` command. No npm,
        // no clone, no publish. The window resolves the bundled path and node
        // executable; these helpers stay pure and take the path as an argument.

        /// <summary>
        /// The local (bundled) MCP server config object (without an outer name key):
        /// <c>{ "command": "node", "args": ["&lt;serverPath&gt;"], "env": { ... } }</c>.
        /// </summary>
        internal static JObject LocalServerConfig(string serverPath, string projectPath)
        {
            return new JObject
            {
                ["command"] = "node",
                ["args"] = new JArray(serverPath ?? string.Empty),
                ["env"] = new JObject
                {
                    [ProjectPathEnvVar] = projectPath ?? string.Empty,
                },
            };
        }

        /// <summary>
        /// Compact JSON for the inner local server config, used as the argument to
        /// <c>claude mcp add-json sceneport '&lt;json&gt;'</c>.
        /// </summary>
        internal static string LocalServerConfigJson(string serverPath, string projectPath)
        {
            return LocalServerConfig(serverPath, projectPath).ToString(Formatting.None);
        }

        /// <summary>
        /// The exact shell command that registers the bundled ScenePort server with
        /// Claude Code via the CLI, e.g.
        /// <c>claude mcp add-json sceneport '{"command":"node","args":["…/index.js"],…}'</c>.
        /// </summary>
        internal static string ClaudeLocalAddCommand(string serverPath, string projectPath, string name = ServerName)
        {
            return "claude mcp add-json " + name + " '" + LocalServerConfigJson(serverPath, projectPath) + "'";
        }

        /// <summary>
        /// Pretty-printed Claude Code config block for the bundled server: the standard
        /// <c>mcpServers</c> map with ScenePort under it using <c>node &lt;serverPath&gt;</c>.
        /// </summary>
        internal static string ClaudeLocalConfigJson(string serverPath, string projectPath, string name = ServerName)
        {
            var root = new JObject
            {
                ["mcpServers"] = new JObject
                {
                    [name] = LocalServerConfig(serverPath, projectPath),
                },
            };
            return root.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Codex MCP config block in TOML form for the bundled server. Mirrors
        /// <see cref="CodexConfigToml"/> but with the <c>node &lt;serverPath&gt;</c> command.
        /// </summary>
        internal static string CodexLocalConfigToml(string serverPath, string projectPath, string name = ServerName)
        {
            var safePath = TomlEscape(projectPath);
            var safeServer = TomlEscape(serverPath);
            var lines = new List<string>
            {
                "[mcp_servers." + name + "]",
                "command = \"node\"",
                "args = [\"" + safeServer + "\"]",
                "",
                "[mcp_servers." + name + ".env]",
                ProjectPathEnvVar + " = \"" + safePath + "\"",
            };
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Codex MCP config block in JSON form for the bundled server, for Codex builds
        /// that accept JSON config. Mirrors the Claude shape under an <c>mcpServers</c> map.
        /// </summary>
        internal static string CodexLocalConfigJson(string serverPath, string projectPath, string name = ServerName)
        {
            return ClaudeLocalConfigJson(serverPath, projectPath, name);
        }

        /// <summary>
        /// The doctor command line for the bundled server. Returns the node executable
        /// plus its argument array (<c>node &lt;serverPath&gt; doctor --json</c>).
        /// </summary>
        internal static (string FileName, string[] Args) LocalDoctorCommand(string serverPath)
        {
            return ("node", new[] { serverPath ?? string.Empty, "doctor", "--json" });
        }

        private static string TomlEscape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
