using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
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

        // Collapsed by default: the raw config blocks and npx variants live behind this
        // foldout so the primary "Connect …" buttons are the first thing users see.
        private bool showAdvancedConnect;
        private ScenePortSetup.StatusModel status;
        private string statusMessage = string.Empty;
        private MessageType statusMessageType = MessageType.Info;
        private string doctorOutput = string.Empty;

        // Deferred work: button handlers in OnGUI must NOT block, open dialogs, or spawn
        // processes during the render pass (Unity throws "GUI Window tried to begin
        // rendering while something else had not finished rendering"). They enqueue an
        // Action here; Update() drains it OUTSIDE the OnGUI pass where blocking is safe.
        private System.Action pendingAction;

        // Environment resolution (executables / bundled server path) spawns blocking child
        // processes (login shell, `where`). These must never run during OnGUI. They are
        // resolved off the render thread into these cached fields; OnGUI only READS them.
        private string cachedNodePath;
        private string cachedClaudePath;
        private string cachedServerPath;
        private bool environmentResolved;

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

            // Resolve executables/paths OFF the render thread. ResolveExecutable() spawns a
            // blocking login shell / `where`, which must never run during OnGUI. Defer it so
            // it runs after the current import/compile and outside any GUI pass. Never in CI.
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        RefreshEnvironment();
                    }
                };
            }
        }

        private void Update()
        {
            // Drain deferred actions OUTSIDE the OnGUI render pass. Here it is safe to run
            // blocking processes (RunProcess) and modal dialogs (EditorUtility.DisplayDialog).
            if (pendingAction != null)
            {
                var action = pendingAction;
                pendingAction = null;
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    SetMessage("Action failed: " + ex.Message, MessageType.Error);
                }
                Repaint();
            }
        }

        private void OnGUI()
        {
            // Compute/read all non-layout state BEFORE opening any layout group so the
            // layout-rendering section below cannot throw and leave the layout-group stack
            // unbalanced. OnGUI only READS cached state — it never blocks, spawns a process,
            // resolves an executable, or opens a dialog. All side effects are deferred to
            // Update() via pendingAction.
            if (status == null)
            {
                RefreshStatus();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
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
                DrawDiagnosticsSection();
                EditorGUILayout.Space();
                DrawLinksSection();
            }
            finally
            {
                // Guarantee the scroll view is always closed, on every path, so the
                // layout-group stack stays balanced even if a section throws.
                EditorGUILayout.EndScrollView();
            }
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

                if (!environmentResolved)
                {
                    EditorGUILayout.LabelField("Node detected", "Resolving local tools…");
                    EditorGUILayout.LabelField("Bundled server", "Resolving local tools…");
                }
                else
                {
                    EditorGUILayout.LabelField("Node detected", string.IsNullOrEmpty(cachedNodePath) ? "not found" : cachedNodePath);
                    EditorGUILayout.LabelField("Bundled server", string.IsNullOrEmpty(cachedServerPath) ? "not found" : cachedServerPath);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                {
                    // Defer: RefreshEnvironment() spawns blocking processes; never in OnGUI.
                    pendingAction = () =>
                    {
                        RefreshStatus();
                        RefreshEnvironment();
                    };
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
            // Read cached value only; before resolution finishes treat as "no bundled server"
            // so the npx-based instructions are shown as a safe default.
            var serverPath = environmentResolved ? cachedServerPath : null;
            var hasBundled = !string.IsNullOrEmpty(serverPath);

            EditorGUILayout.LabelField("Connect your AI tool", EditorStyles.boldLabel);

            // ---- Primary action: bundled-local connect (zero-dependency path) ----
            if (!environmentResolved)
            {
                EditorGUILayout.HelpBox("Resolving local tools (node / claude / bundled server)…", MessageType.Info);
            }
            else if (hasBundled)
            {
                EditorGUILayout.LabelField(
                    "Recommended: the MCP server is bundled with this package. One click registers " +
                    "it with your AI tool — no npm install, clone, or publish.",
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Connect Claude (local)"))
                    {
                        // Defer: runs claude CLI + dialog; must run outside OnGUI.
                        pendingAction = () => ConnectClaudeLocal();
                    }

                    if (GUILayout.Button("Connect Codex (local)"))
                    {
                        // Defer: opens a dialog; must run outside OnGUI.
                        pendingAction = () => ConnectCodexLocal();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Bundled server not found. Use the npx configs under \"Advanced / other clients\" " +
                    "below, which require the " + ScenePortSetup.NpmPackage + " package to be published to npm.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            // ---- Advanced: raw config blocks + npx variants (collapsed by default) ----
            showAdvancedConnect = EditorGUILayout.Foldout(showAdvancedConnect, "Advanced / other clients", true);
            if (!showAdvancedConnect)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                if (hasBundled)
                {
                    EditorGUILayout.LabelField(
                        "Bundled-local configs (run the bundled server directly with Node).",
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
                }

                EditorGUILayout.LabelField(
                    "npx variants (fetch the published " + ScenePortSetup.NpmPackage +
                    " package — available after it is published to npm).",
                    EditorStyles.miniLabel);
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
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Register via Claude CLI (npx — requires npm publish):", EditorStyles.miniLabel);
                if (GUILayout.Button("Write Claude config (run claude mcp add-json, npx)"))
                {
                    // Defer: runs claude CLI + dialog; must run outside OnGUI.
                    pendingAction = () => WriteClaudeConfig();
                }
            }
        }

        private void DrawDiagnosticsSection()
        {
            // Read cached value only; treat unresolved as "no bundled server".
            var hasBundled = environmentResolved && !string.IsNullOrEmpty(cachedServerPath);

            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                hasBundled
                    ? "Runs the bundled MCP server doctor (node <bundled> doctor --json)."
                    : "Runs the MCP server doctor (npx -y " + ScenePortSetup.NpmPackage + " doctor --json).",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Run doctor"))
            {
                // Defer: runs a process (up to DoctorTimeoutSeconds); must run outside OnGUI.
                pendingAction = () => RunDoctor();
            }

            if (!string.IsNullOrEmpty(doctorOutput))
            {
                doctorScroll = EditorGUILayout.BeginScrollView(doctorScroll, GUILayout.MinHeight(80f), GUILayout.MaxHeight(220f));
                try
                {
                    EditorGUILayout.TextArea(doctorOutput, GUILayout.ExpandHeight(true));
                }
                finally
                {
                    EditorGUILayout.EndScrollView();
                }
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

        /// <summary>
        /// Resolves executables ("node", "claude") and the bundled server path into cached
        /// fields. MUST run OUTSIDE the OnGUI render pass because ResolveExecutable spawns a
        /// blocking login shell / `where` process. Called from OnEnable via delayCall and
        /// from the "Refresh" button via the deferred-action queue (Update()).
        /// </summary>
        private void RefreshEnvironment()
        {
            try
            {
                cachedNodePath = ResolveExecutable("node");
                cachedClaudePath = ResolveExecutable("claude");
                cachedServerPath = BundledServerPath();
            }
            catch
            {
                // Best-effort; leave whatever resolved.
            }
            finally
            {
                environmentResolved = true;
            }

            Repaint();
        }

        private void ConnectClaudeLocal()
        {
            var projectPath = status != null ? status.ProjectPath : SafeProjectPath();
            var serverPath = !string.IsNullOrEmpty(cachedServerPath) ? cachedServerPath : BundledServerPath();
            var fallbackCommand = ScenePortSetup.ClaudeLocalAddCommand(serverPath, projectPath);

            if (string.IsNullOrEmpty(serverPath))
            {
                SetMessage("Bundled server not found. Use the npx option below.", MessageType.Warning);
                return;
            }

            var claude = !string.IsNullOrEmpty(cachedClaudePath) ? cachedClaudePath : ResolveExecutable("claude");
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
            var serverPath = !string.IsNullOrEmpty(cachedServerPath) ? cachedServerPath : BundledServerPath();
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
                var claude = !string.IsNullOrEmpty(cachedClaudePath) ? cachedClaudePath : ResolveExecutable("claude");
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
            var serverPath = !string.IsNullOrEmpty(cachedServerPath) ? cachedServerPath : BundledServerPath();
            var useLocal = !string.IsNullOrEmpty(serverPath);

            // Prefer the bundled server with a resolved node; fall back to npx.
            string fileName;
            string[] args;
            if (useLocal)
            {
                var node = !string.IsNullOrEmpty(cachedNodePath) ? cachedNodePath : ResolveExecutable("node");
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
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ScenePortSetupWindow).Assembly);
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
