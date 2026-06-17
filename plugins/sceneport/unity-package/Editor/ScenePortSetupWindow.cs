using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// "Easy setup" centerpiece: a Tools/ScenePort/Setup window that surfaces bridge status
    /// and one-click wiring for Claude Code / Codex. All non-UI logic lives in the pure
    /// <see cref="ScenePortSetup"/> class; this file only renders and runs best-effort
    /// processes. OnGUI is wrapped so it can never throw out into the editor loop.
    /// </summary>
    internal sealed class ScenePortSetupWindow : EditorWindow
    {
        // Persisted (per project) so the window auto-opens exactly once on first import.
        // Reset by deleting this EditorPrefs key, e.g. via the "Reset auto-open" button.
        private const string AutoOpenShownKeyPrefix = "ScenePort.SetupShown.";

        private const double DoctorTimeoutSeconds = 20;

        private Vector2 scroll;
        private Vector2 doctorScroll;
        private ScenePortSetup.StatusModel status;
        private string statusMessage = string.Empty;
        private MessageType statusMessageType = MessageType.Info;
        private string doctorOutput = string.Empty;

        // Resolved once per window session (best-effort; null when not found).
        private string bundledServerPath;
        private bool bundledServerResolved;

        // Cache for resolved executables ("claude", "node", ...) -> absolute path or null.
        private static readonly Dictionary<string, string> ExecutableCache =
            new Dictionary<string, string>();

        [MenuItem("Tools/ScenePort/Setup", false, 0)]
        internal static void Open()
        {
            var window = GetWindow<ScenePortSetupWindow>(false, "ScenePort Setup", true);
            window.minSize = new Vector2(420f, 480f);
            window.RefreshStatus();
            window.Show();
        }

        [InitializeOnLoadMethod]
        private static void MaybeAutoOpenOnce()
        {
            // Never pop UI in CI / batch runs.
            if (Application.isBatchMode)
            {
                return;
            }

            var key = AutoOpenShownKeyPrefix + PlayerSettings.productGUID.ToString("N");
            if (EditorPrefs.GetBool(key, false))
            {
                return;
            }

            EditorPrefs.SetBool(key, true);
            // Defer so we don't open during the import/compile that triggered this callback.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Open();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("ScenePort: could not auto-open setup window: " + ex.Message);
                }
            };
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void OnGUI()
        {
            try
            {
                DrawGui();
            }
            catch (Exception ex)
            {
                // OnGUI must never throw out into the editor. Surface it instead.
                EditorGUILayout.HelpBox("ScenePort setup window error: " + ex.Message, MessageType.Error);
            }
        }

        private void DrawGui()
        {
            if (status == null)
            {
                RefreshStatus();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ScenePort", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Connect Claude Code or Codex to this Unity project.", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusMessageType);
            }

            DrawStatusSection();
            EditorGUILayout.Space();
            DrawConnectSection();
            EditorGUILayout.Space();
            DrawOneClickSection();
            EditorGUILayout.Space();
            DrawDiagnosticsSection();
            EditorGUILayout.Space();
            DrawLinksSection();

            EditorGUILayout.EndScrollView();
        }

        // ---- Sections -------------------------------------------------------

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Bridge status", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                var running = status.Running;
                EditorGUILayout.LabelField("State", running ? "Running" : "Not running");
                EditorGUILayout.LabelField("Port", status.Port > 0 ? status.Port.ToString() : "-");
                EditorGUILayout.LabelField("URL", string.IsNullOrEmpty(status.Url) ? "-" : status.Url);
                EditorGUILayout.LabelField("Token fingerprint", string.IsNullOrEmpty(status.TokenFingerprint) ? "-" : status.TokenFingerprint);
                EditorGUILayout.LabelField("Auth required", status.AuthRequired ? "Yes" : "No");
                EditorGUILayout.LabelField("Policy profile", string.IsNullOrEmpty(status.PolicyProfile) ? "-" : status.PolicyProfile);
                EditorGUILayout.LabelField("Project", string.IsNullOrEmpty(status.ProjectName) ? "-" : status.ProjectName);
                SelectableRow("Project path", status.ProjectPath);
                EditorGUILayout.LabelField("Unity version", string.IsNullOrEmpty(status.UnityVersion) ? Application.unityVersion : status.UnityVersion);

                var nodePath = ResolveExecutable("node");
                EditorGUILayout.LabelField("Node detected", string.IsNullOrEmpty(nodePath) ? "not found" : nodePath);
                var serverPath = BundledServerPath();
                EditorGUILayout.LabelField("Bundled server", string.IsNullOrEmpty(serverPath) ? "not found" : serverPath);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                {
                    RefreshStatus();
                }

                using (new EditorGUI.DisabledScope(status.Port <= 0))
                {
                    if (GUILayout.Button("Copy Bridge URL"))
                    {
                        EditorGUIUtility.systemCopyBuffer = status.Url;
                        SetMessage("Bridge URL copied to clipboard.", MessageType.Info);
                    }
                }
            }
        }

        private void DrawConnectSection()
        {
            var projectPath = status.ProjectPath;
            var serverPath = BundledServerPath();

            EditorGUILayout.LabelField("Connect your AI tool", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(serverPath))
            {
                EditorGUILayout.LabelField(
                    "Recommended: the MCP server is bundled with this package. " +
                    "These configs run it directly with Node — no npm install, clone, or publish.",
                    EditorStyles.miniLabel);
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Claude Code (one-liner, local)", EditorStyles.miniBoldLabel);
                var claudeLocalCommand = ScenePortSetup.ClaudeLocalAddCommand(serverPath, projectPath);
                SelectableTextArea(claudeLocalCommand, 3);
                if (GUILayout.Button("Copy Claude command (local)"))
                {
                    CopyToClipboard(claudeLocalCommand, "Claude command");
                }
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Claude Code (.mcp.json, local)", EditorStyles.miniBoldLabel);
                var claudeLocalJson = ScenePortSetup.ClaudeLocalConfigJson(serverPath, projectPath);
                SelectableTextArea(claudeLocalJson, 7);
                if (GUILayout.Button("Copy Claude config (local)"))
                {
                    CopyToClipboard(claudeLocalJson, "Claude config");
                }
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Codex (config.toml, local)", EditorStyles.miniBoldLabel);
                var codexLocalToml = ScenePortSetup.CodexLocalConfigToml(serverPath, projectPath);
                SelectableTextArea(codexLocalToml, 6);
                if (GUILayout.Button("Copy Codex config (local)"))
                {
                    CopyToClipboard(codexLocalToml, "Codex config");
                }
                EditorGUILayout.Space();

                EditorGUILayout.LabelField(
                    "Alternative (after 'npm publish'): the npx-based configs below fetch " +
                    "the published " + ScenePortSetup.NpmPackage + " package.",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Bundled server not found. Using the npx configs below, which require the " +
                    ScenePortSetup.NpmPackage + " package to be published to npm.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Claude Code (one-liner, npx)", EditorStyles.miniBoldLabel);
            var claudeCommand = ScenePortSetup.ClaudeAddCommand(projectPath);
            SelectableTextArea(claudeCommand, 2);
            if (GUILayout.Button("Copy Claude command (npx)"))
            {
                CopyToClipboard(claudeCommand, "Claude command");
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Claude Code (.mcp.json, npx)", EditorStyles.miniBoldLabel);
            var claudeJson = ScenePortSetup.ClaudeConfigJson(projectPath);
            SelectableTextArea(claudeJson, 7);
            if (GUILayout.Button("Copy Claude config (npx)"))
            {
                CopyToClipboard(claudeJson, "Claude config");
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Codex (config.toml, npx)", EditorStyles.miniBoldLabel);
            var codexToml = ScenePortSetup.CodexConfigToml(projectPath);
            SelectableTextArea(codexToml, 6);
            if (GUILayout.Button("Copy Codex config (npx)"))
            {
                CopyToClipboard(codexToml, "Codex config");
            }
        }

        private void DrawOneClickSection()
        {
            var serverPath = BundledServerPath();
            var hasBundled = !string.IsNullOrEmpty(serverPath);

            EditorGUILayout.LabelField("One-click setup", EditorStyles.boldLabel);

            if (hasBundled)
            {
                EditorGUILayout.LabelField(
                    "Registers the bundled server with your AI tool (resolves 'claude'/'node' for you).",
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Connect Claude (local)"))
                    {
                        ConnectClaudeLocal();
                    }

                    if (GUILayout.Button("Connect Codex (local)"))
                    {
                        ConnectCodexLocal();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Needs npm publish:", EditorStyles.miniLabel);
                if (GUILayout.Button("Write Claude config (npx — requires npm publish)"))
                {
                    WriteClaudeConfig();
                }
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Runs the Claude CLI to register ScenePort for you (requires 'claude' on PATH " +
                    "and the published " + ScenePortSetup.NpmPackage + " package).",
                    EditorStyles.miniLabel);

                if (GUILayout.Button("Write Claude config (run claude mcp add-json)"))
                {
                    WriteClaudeConfig();
                }
            }
        }

        private void DrawDiagnosticsSection()
        {
            var hasBundled = !string.IsNullOrEmpty(BundledServerPath());

            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                hasBundled
                    ? "Runs the bundled MCP server doctor (node <bundled> doctor --json)."
                    : "Runs the MCP server doctor (npx -y " + ScenePortSetup.NpmPackage + " doctor --json).",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Run doctor"))
            {
                RunDoctor();
            }

            if (!string.IsNullOrEmpty(doctorOutput))
            {
                doctorScroll = EditorGUILayout.BeginScrollView(doctorScroll, GUILayout.MinHeight(80f), GUILayout.MaxHeight(220f));
                EditorGUILayout.TextArea(doctorOutput, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawLinksSection()
        {
            EditorGUILayout.LabelField("Help", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (LinkOrButton("Documentation"))
                {
                    Application.OpenURL(ScenePortSetup.DocsUrl);
                }

                if (LinkOrButton("Repository"))
                {
                    Application.OpenURL(ScenePortSetup.RepoUrl);
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Reset auto-open flag for this project"))
            {
                EditorPrefs.DeleteKey(AutoOpenShownKeyPrefix + PlayerSettings.productGUID.ToString("N"));
                SetMessage("Auto-open flag reset. The setup window will open on next import.", MessageType.Info);
            }
        }

        // ---- Actions --------------------------------------------------------

        private void RefreshStatus()
        {
            try
            {
                var projectPath = ScenePortPaths.ProjectPath();
                if (ScenePortBridge.IsRunning)
                {
                    // Live, in-process bridge: read authoritative static state.
                    status = new ScenePortSetup.StatusModel
                    {
                        Running = true,
                        Port = ScenePortBridge.BoundPort,
                        Url = ScenePortSetup.UrlForPort(ScenePortBridge.BoundPort),
                        TokenFingerprint = ScenePortAuth.Fingerprint(ScenePortBridge.CurrentToken),
                        AuthRequired = ScenePortBridge.AuthRequired,
                        ProjectPath = projectPath,
                        ProjectName = Application.productName,
                        UnityVersion = Application.unityVersion,
                        PolicyProfile = string.Empty,
                        Source = "live",
                    };
                }
                else
                {
                    // Bridge may be owned by another editor process: read the discovery file.
                    ScenePortDiscoveryFile.BridgeInfo info;
                    ScenePortDiscoveryFile.TryRead(projectPath, out info);
                    status = ScenePortSetup.BuildStatus(info, projectPath, ScenePortBridge.AuthRequired);
                    if (string.IsNullOrEmpty(status.ProjectName))
                    {
                        status.ProjectName = Application.productName;
                    }
                    if (string.IsNullOrEmpty(status.UnityVersion))
                    {
                        status.UnityVersion = Application.unityVersion;
                    }
                }
            }
            catch (Exception ex)
            {
                status = new ScenePortSetup.StatusModel
                {
                    ProjectPath = SafeProjectPath(),
                    ProjectName = Application.productName,
                    UnityVersion = Application.unityVersion,
                };
                SetMessage("Could not read bridge status: " + ex.Message, MessageType.Warning);
            }

            Repaint();
        }

        private void ConnectClaudeLocal()
        {
            var projectPath = status != null ? status.ProjectPath : SafeProjectPath();
            var serverPath = BundledServerPath();
            var fallbackCommand = ScenePortSetup.ClaudeLocalAddCommand(serverPath, projectPath);

            if (string.IsNullOrEmpty(serverPath))
            {
                SetMessage("Bundled server not found. Use the npx option below.", MessageType.Warning);
                return;
            }

            var claude = ResolveExecutable("claude");
            if (string.IsNullOrEmpty(claude))
            {
                EditorGUIUtility.systemCopyBuffer = fallbackCommand;
                EditorUtility.DisplayDialog(
                    "Claude CLI not found",
                    "Could not find 'claude' on PATH. The command has been copied to your clipboard so you " +
                    "can run it in a terminal:\n\n" + fallbackCommand,
                    "OK");
                SetMessage("'claude' not found. Command copied to clipboard.", MessageType.Warning);
                return;
            }

            var json = ScenePortSetup.LocalServerConfigJson(serverPath, projectPath);
            try
            {
                var result = RunProcess(claude, new[] { "mcp", "add-json", ScenePortSetup.ServerName, json }, 30);
                if (!result.Launched)
                {
                    EditorGUIUtility.systemCopyBuffer = fallbackCommand;
                    SetMessage("Could not launch 'claude'. Command copied to clipboard.", MessageType.Warning);
                    return;
                }

                var combined = (result.StdOut + "\n" + result.StdErr).Trim();
                if (result.ExitCode == 0)
                {
                    EditorUtility.DisplayDialog("ScenePort", "Connected the bundled ScenePort server to Claude Code.\n\n" + combined, "OK");
                    SetMessage("Claude connected (local server).", MessageType.Info);
                }
                else
                {
                    EditorUtility.DisplayDialog("ScenePort", "claude exited with code " + result.ExitCode + ":\n\n" + combined, "OK");
                    SetMessage("claude exited with code " + result.ExitCode + ". See dialog for details.", MessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                EditorGUIUtility.systemCopyBuffer = fallbackCommand;
                SetMessage("Could not run claude (" + ex.Message + "). Command copied to clipboard.", MessageType.Warning);
            }
        }

        private void ConnectCodexLocal()
        {
            // Codex has no register-by-CLI flow here; copy the TOML for the user to paste
            // into ~/.codex/config.toml.
            var projectPath = status != null ? status.ProjectPath : SafeProjectPath();
            var serverPath = BundledServerPath();
            if (string.IsNullOrEmpty(serverPath))
            {
                SetMessage("Bundled server not found. Use the npx option below.", MessageType.Warning);
                return;
            }

            var toml = ScenePortSetup.CodexLocalConfigToml(serverPath, projectPath);
            EditorGUIUtility.systemCopyBuffer = toml;
            EditorUtility.DisplayDialog(
                "Codex config copied",
                "The local Codex config has been copied to your clipboard. Paste it into " +
                "~/.codex/config.toml:\n\n" + toml,
                "OK");
            SetMessage("Codex config (local) copied to clipboard.", MessageType.Info);
        }

        private void WriteClaudeConfig()
        {
            var projectPath = status != null ? status.ProjectPath : SafeProjectPath();
            var json = ScenePortSetup.NpxServerConfigJson(projectPath);
            var fallback = ScenePortSetup.ClaudeAddCommand(projectPath);
            try
            {
                var claude = ResolveExecutable("claude");
                if (string.IsNullOrEmpty(claude))
                {
                    EditorGUIUtility.systemCopyBuffer = fallback;
                    EditorUtility.DisplayDialog(
                        "Claude CLI not found",
                        "Could not find 'claude' on PATH. The command has been copied to your clipboard so you can run it in a terminal:\n\n" + fallback,
                        "OK");
                    SetMessage("'claude' not found on PATH. Command copied to clipboard.", MessageType.Warning);
                    return;
                }

                var result = RunProcess(claude, new[] { "mcp", "add-json", ScenePortSetup.ServerName, json }, 30);
                if (!result.Launched)
                {
                    EditorGUIUtility.systemCopyBuffer = fallback;
                    EditorUtility.DisplayDialog(
                        "Claude CLI not found",
                        "Could not run 'claude'. The command has been copied to your clipboard so you can run it in a terminal:\n\n" + fallback,
                        "OK");
                    SetMessage("'claude' not found on PATH. Command copied to clipboard.", MessageType.Warning);
                    return;
                }

                var combined = (result.StdOut + "\n" + result.StdErr).Trim();
                if (result.ExitCode == 0)
                {
                    EditorUtility.DisplayDialog("ScenePort", "Registered ScenePort with Claude Code.\n\n" + combined, "OK");
                    SetMessage("Claude config written successfully.", MessageType.Info);
                }
                else
                {
                    EditorUtility.DisplayDialog("ScenePort", "claude exited with code " + result.ExitCode + ":\n\n" + combined, "OK");
                    SetMessage("claude exited with code " + result.ExitCode + ". See dialog for details.", MessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                EditorGUIUtility.systemCopyBuffer = fallback;
                SetMessage("Could not run claude (" + ex.Message + "). Command copied to clipboard.", MessageType.Warning);
            }
        }

        private void RunDoctor()
        {
            var serverPath = BundledServerPath();
            var useLocal = !string.IsNullOrEmpty(serverPath);

            // Prefer the bundled server with a resolved node; fall back to npx.
            string fileName;
            string[] args;
            if (useLocal)
            {
                var node = ResolveExecutable("node");
                if (string.IsNullOrEmpty(node))
                {
                    doctorOutput =
                        "Node not found on PATH — install Node 18+, or run the bundled server manually:\n\n" +
                        "  node \"" + serverPath + "\" doctor --json";
                    SetMessage("Node not found on PATH.", MessageType.Warning);
                    return;
                }
                var local = ScenePortSetup.LocalDoctorCommand(serverPath);
                fileName = node;
                args = local.Args;
            }
            else
            {
                var command = ScenePortSetup.DoctorCommand();
                fileName = ResolveExecutable(command.FileName) ?? command.FileName;
                args = command.Args;
            }

            try
            {
                var result = RunProcess(fileName, args, DoctorTimeoutSeconds);
                if (!result.Launched)
                {
                    doctorOutput = useLocal
                        ? "Could not launch node. Make sure Node.js 18+ is installed.\n\n" +
                          "Run manually in a terminal:\n  node \"" + serverPath + "\" doctor --json"
                        : "Could not launch '" + fileName + "'. Make sure Node.js/npx is installed.\n\n" +
                          "Run manually in a terminal:\n  npx -y " + ScenePortSetup.NpmPackage + " doctor --json";
                    SetMessage("Doctor could not launch.", MessageType.Warning);
                    return;
                }

                if (result.TimedOut)
                {
                    doctorOutput = "Doctor timed out after " + DoctorTimeoutSeconds + "s.\n\n" + result.StdOut + "\n" + result.StdErr;
                    SetMessage("Doctor timed out.", MessageType.Warning);
                    return;
                }

                var combined = (result.StdOut + "\n" + result.StdErr).Trim();
                doctorOutput = string.IsNullOrEmpty(combined) ? "(no output, exit code " + result.ExitCode + ")" : combined;
                SetMessage("Doctor finished (exit code " + result.ExitCode + ").", result.ExitCode == 0 ? MessageType.Info : MessageType.Warning);
            }
            catch (Exception ex)
            {
                var manual = useLocal
                    ? "  node \"" + serverPath + "\" doctor --json"
                    : "  npx -y " + ScenePortSetup.NpmPackage + " doctor --json";
                doctorOutput =
                    "Doctor failed: " + ex.Message + "\n\n" +
                    "Run manually in a terminal:\n" + manual;
                SetMessage("Doctor failed: " + ex.Message, MessageType.Warning);
            }
        }

        // ---- Process helper -------------------------------------------------

        private struct ProcessResult
        {
            public bool Launched;
            public bool TimedOut;
            public int ExitCode;
            public string StdOut;
            public string StdErr;
        }

        /// <summary>
        /// Best-effort, bounded process launch. Never throws for a missing executable
        /// (returns <c>Launched = false</c>) and always returns within the timeout.
        /// </summary>
        private static ProcessResult RunProcess(string fileName, string[] args, double timeoutSeconds)
        {
            var result = new ProcessResult { StdOut = string.Empty, StdErr = string.Empty };
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            Process process = null;
            try
            {
                process = new Process { StartInfo = psi };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                if (!process.Start())
                {
                    return result;
                }

                result.Launched = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timeoutMs = (int)Math.Max(1000, timeoutSeconds * 1000);
                if (!process.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    try { process.Kill(); }
                    catch { /* already gone */ }
                }
                else
                {
                    result.ExitCode = process.ExitCode;
                }

                result.StdOut = stdout.ToString();
                result.StdErr = stderr.ToString();
                return result;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Executable not found on PATH.
                result.Launched = false;
                return result;
            }
            catch (Exception ex)
            {
                result.Launched = result.Launched && true;
                result.StdErr = (result.StdErr ?? string.Empty) + ex.Message;
                return result;
            }
            finally
            {
                try { process?.Dispose(); }
                catch { /* ignore */ }
            }
        }

        // ---- GUI helpers ----------------------------------------------------

        private static void SelectableRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(value) ? "-" : value,
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static void SelectableTextArea(string value, int lines)
        {
            var height = EditorGUIUtility.singleLineHeight * Mathf.Max(1, lines);
            EditorGUILayout.SelectableLabel(value, EditorStyles.textArea, GUILayout.MinHeight(height));
        }

        private void CopyToClipboard(string value, string what)
        {
            EditorGUIUtility.systemCopyBuffer = value;
            SetMessage(what + " copied to clipboard.", MessageType.Info);
        }

        private static bool LinkOrButton(string label)
        {
#if UNITY_2021_2_OR_NEWER
            return EditorGUILayout.LinkButton(label);
#else
            return GUILayout.Button(label);
#endif
        }

        private void SetMessage(string message, MessageType type)
        {
            statusMessage = message;
            statusMessageType = type;
            Repaint();
        }

        private static string SafeProjectPath()
        {
            try
            {
                return ScenePortPaths.ProjectPath();
            }
            catch
            {
                return string.Empty;
            }
        }

        // ---- Bundled server / executable resolution -------------------------

        /// <summary>
        /// Cached accessor for the bundled server path; resolves once per window session.
        /// </summary>
        private string BundledServerPath()
        {
            if (!bundledServerResolved)
            {
                try
                {
                    bundledServerPath = ResolveBundledServerPath();
                }
                catch
                {
                    bundledServerPath = null;
                }
                bundledServerResolved = true;
            }
            return bundledServerPath;
        }

        /// <summary>
        /// Resolves the absolute path to the bundled MCP server (Server~/index.js) shipped
        /// inside this UPM package. Returns null if it cannot be found. Never throws.
        /// </summary>
        private static string ResolveBundledServerPath()
        {
            // 1) Installed package: ask the Package Manager for the resolved on-disk path.
            try
            {
                var pkg = PackageInfo.FindForAssembly(typeof(ScenePortSetupWindow).Assembly);
                if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
                {
                    var candidate = Path.Combine(pkg.resolvedPath, "Server~", "index.js");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Fall through to dev-path probing.
            }

            // 2) Dev fallback: this source file lives at
            //    .../unity-package/Editor/ScenePortSetupWindow.cs, so Server~/index.js is a
            //    sibling of the Editor folder. Walk up from the assembly location is not
            //    reliable in the editor, so probe relative to known package roots.
            try
            {
                foreach (var root in DevPackageRootCandidates())
                {
                    if (string.IsNullOrEmpty(root))
                    {
                        continue;
                    }
                    var candidate = Path.Combine(root, "Server~", "index.js");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        /// <summary>
        /// Candidate package roots for the dev/embedded layout (source repo, not an
        /// installed package). Best-effort; any may not exist.
        /// </summary>
        private static IEnumerable<string> DevPackageRootCandidates()
        {
            var dataPath = Application.dataPath; // <project>/Assets
            var projectRoot = Directory.GetParent(dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                // Embedded under Packages/ by name.
                yield return Path.Combine(projectRoot, "Packages", "io.sceneport.mcpbridge");
                // Embedded directly under the repo layout.
                yield return Path.Combine(projectRoot, "plugins", "sceneport", "unity-package");
            }
        }

        /// <summary>
        /// Resolves the absolute path to an executable, accounting for GUI apps on macOS not
        /// inheriting the login-shell PATH. Returns null if not found. Caches results; never throws.
        /// </summary>
        private static string ResolveExecutable(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (ExecutableCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            string resolved = null;
            try
            {
#if UNITY_EDITOR_WIN
                resolved = ResolveViaWhere(name);
#else
                resolved = ResolveViaLoginShell(name) ?? ProbeCommonDirs(name);
#endif
            }
            catch
            {
                resolved = null;
            }

            ExecutableCache[name] = resolved;
            return resolved;
        }

#if UNITY_EDITOR_WIN
        private static string ResolveViaWhere(string name)
        {
            var result = RunProcess("where", new[] { name }, 5);
            if (!result.Launched || string.IsNullOrEmpty(result.StdOut))
            {
                return null;
            }
            foreach (var line in result.StdOut.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && File.Exists(trimmed))
                {
                    return trimmed;
                }
            }
            return null;
        }
#else
        private static string ResolveViaLoginShell(string name)
        {
            // GUI apps launched from Finder/Dock do not inherit the shell PATH, so ask a
            // login shell to resolve the binary the way the user's terminal would.
            var shell = Environment.GetEnvironmentVariable("SHELL");
            var shells = new List<string>();
            if (!string.IsNullOrEmpty(shell))
            {
                shells.Add(shell);
            }
            shells.Add("/bin/zsh");
            shells.Add("/bin/bash");

            foreach (var sh in shells)
            {
                if (string.IsNullOrEmpty(sh) || !File.Exists(sh))
                {
                    continue;
                }
                var result = RunProcess(sh, new[] { "-l", "-c", "command -v " + name }, 5);
                if (!result.Launched)
                {
                    continue;
                }
                var path = (result.StdOut ?? string.Empty).Trim();
                // Use the last non-empty line in case the login shell prints noise.
                if (!string.IsNullOrEmpty(path))
                {
                    var lines = path.Split('\n');
                    var last = lines[lines.Length - 1].Trim();
                    if (!string.IsNullOrEmpty(last) && File.Exists(last))
                    {
                        return last;
                    }
                }
            }
            return null;
        }

        private static string ProbeCommonDirs(string name)
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            var dirs = new List<string>
            {
                "/opt/homebrew/bin",
                "/usr/local/bin",
                "/usr/bin",
            };
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, ".local", "bin"));
                dirs.Add(Path.Combine(home, ".npm-global", "bin"));
            }

            foreach (var dir in dirs)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // ~/.nvm/versions/node/*/bin (most relevant for node).
            if (!string.IsNullOrEmpty(home))
            {
                var nvm = Path.Combine(home, ".nvm", "versions", "node");
                try
                {
                    if (Directory.Exists(nvm))
                    {
                        foreach (var versionDir in Directory.GetDirectories(nvm))
                        {
                            var candidate = Path.Combine(versionDir, "bin", name);
                            if (File.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }
#endif
    }
}
