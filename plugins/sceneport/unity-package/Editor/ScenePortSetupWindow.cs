using System;
using System.Diagnostics;
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
        private ScenePortSetup.StatusModel status;
        private string statusMessage = string.Empty;
        private MessageType statusMessageType = MessageType.Info;
        private string doctorOutput = string.Empty;

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

            var key = AutoOpenShownKeyPrefix + Application.productGUID;
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

            EditorGUILayout.LabelField("Connect your AI tool", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Paste one of the configs below into Claude Code or Codex.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Claude Code (one-liner)", EditorStyles.miniBoldLabel);
            var claudeCommand = ScenePortSetup.ClaudeAddCommand(projectPath);
            SelectableTextArea(claudeCommand, 2);
            if (GUILayout.Button("Copy Claude command"))
            {
                CopyToClipboard(claudeCommand, "Claude command");
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Claude Code (.mcp.json)", EditorStyles.miniBoldLabel);
            var claudeJson = ScenePortSetup.ClaudeConfigJson(projectPath);
            SelectableTextArea(claudeJson, 7);
            if (GUILayout.Button("Copy Claude config"))
            {
                CopyToClipboard(claudeJson, "Claude config");
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Codex (config.toml)", EditorStyles.miniBoldLabel);
            var codexToml = ScenePortSetup.CodexConfigToml(projectPath);
            SelectableTextArea(codexToml, 6);
            if (GUILayout.Button("Copy Codex config"))
            {
                CopyToClipboard(codexToml, "Codex config");
            }
        }

        private void DrawOneClickSection()
        {
            EditorGUILayout.LabelField("One-click setup", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Runs the Claude CLI to register ScenePort for you (requires 'claude' on PATH).",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Write Claude config (run claude mcp add-json)"))
            {
                WriteClaudeConfig();
            }
        }

        private void DrawDiagnosticsSection()
        {
            EditorGUILayout.LabelField("Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Runs the MCP server doctor (npx -y sceneport-mcp doctor --json).",
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
                EditorPrefs.DeleteKey(AutoOpenShownKeyPrefix + Application.productGUID);
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

        private void WriteClaudeConfig()
        {
            var projectPath = status != null ? status.ProjectPath : SafeProjectPath();
            var json = ScenePortSetup.NpxServerConfigJson(projectPath);
            try
            {
                var result = RunProcess("claude", new[] { "mcp", "add-json", ScenePortSetup.ServerName, json }, 30);
                if (!result.Launched)
                {
                    var fallback = ScenePortSetup.ClaudeAddCommand(projectPath);
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
                var fallback = ScenePortSetup.ClaudeAddCommand(projectPath);
                EditorGUIUtility.systemCopyBuffer = fallback;
                SetMessage("Could not run claude (" + ex.Message + "). Command copied to clipboard.", MessageType.Warning);
            }
        }

        private void RunDoctor()
        {
            var command = ScenePortSetup.DoctorCommand();
            try
            {
                var result = RunProcess(command.FileName, command.Args, DoctorTimeoutSeconds);
                if (!result.Launched)
                {
                    doctorOutput =
                        "Could not launch '" + command.FileName + "'. Make sure Node.js/npx is installed.\n\n" +
                        "Run manually in a terminal:\n  npx -y " + ScenePortSetup.NpmPackage + " doctor --json";
                    SetMessage("Doctor could not launch (npx not found).", MessageType.Warning);
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
                doctorOutput =
                    "Doctor failed: " + ex.Message + "\n\n" +
                    "Run manually in a terminal:\n  npx -y " + ScenePortSetup.NpmPackage + " doctor --json";
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
    }
}
