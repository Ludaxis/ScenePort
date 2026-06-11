using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// ScenePort Unity Editor bridge: owns the HTTP server lifecycle and marshals every
    /// request onto the Unity main thread. JSON, routing, request parsing, and per-endpoint
    /// handlers live in their own files; this class is lifecycle only.
    /// </summary>
    [InitializeOnLoad]
    public static class ScenePortBridge
    {
        public const int DefaultPort = 38987;
        public const int MaxPort = 38996;

        private const int MainThreadTimeoutSeconds = 30;
        private const double HeartbeatIntervalSeconds = 2;
        private const string AuthDisabledKey = "ScenePort.RequireAuthToken.Disabled";
        private const string PolicyProfileKey = "ScenePort.PolicyProfile";

        private static readonly object QueueLock = new object();
        private static readonly Queue<WorkItem> WorkQueue = new Queue<WorkItem>();
        private static readonly ScenePortConsoleBuffer ConsoleBuffer = new ScenePortConsoleBuffer(500);
        private static readonly ScenePortContext Context = new ScenePortContext { Console = ConsoleBuffer, Audit = new ScenePortAuditLog() };
        private static readonly ScenePortRouter Router = new ScenePortRouter(Context);
        private static readonly string OwnerLeaseId = Guid.NewGuid().ToString("N");
        private static readonly string StartedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        private static ScenePortHttpServer server;
        private static int mainThreadId;
        private static bool hooksRegistered;
        private static bool canHostBridge;
        private static double nextHeartbeatAt;

        /// <summary>
        /// Seam for the security workstream: a request gate that rejects a request before any
        /// main-thread work. Null means "allow all" (pre-auth behavior).
        /// </summary>
        internal static Func<HttpListenerRequest, ScenePortGateResult> RequestGate;

        static ScenePortBridge()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            canHostBridge = ScenePortEditorProcessRole.ShouldHostBridge();
            Context.OwnerLeaseId = OwnerLeaseId;
            Context.StartedUtc = StartedUtc;
            Context.EditorRole = ScenePortEditorProcessRole.CurrentRole();
            Context.ProcessName = ScenePortEditorProcessRole.CurrentProcessName();
            Context.Version = ResolveVersion();
            RequestGate = request => ScenePortRequestGate.EvaluateRequest(request, Context);
            if (!canHostBridge)
            {
                Debug.Log("ScenePort bridge disabled for Unity process role: " + Context.EditorRole);
                return;
            }

            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += OnQuitting;
            Application.logMessageReceived += CaptureLog;
            TestRunHandlers.RegisterCallbacks();
            hooksRegistered = true;
            Start();
        }

        internal static int BoundPort => Context.BoundPort;
        internal static bool IsRunning => server != null && server.IsRunning;
        internal static string CurrentToken => Context.Token;
        internal static bool AuthRequired => Context.TokenRequired;

        [MenuItem("Tools/ScenePort/Start Bridge")]
        public static void Start()
        {
            if (!canHostBridge)
            {
                Debug.Log("ScenePort bridge not started for Unity process role: " + Context.EditorRole);
                return;
            }

            if (IsRunning)
            {
                return;
            }

            var projectPath = ScenePortPaths.ProjectPath();
            if (string.IsNullOrEmpty(Context.Token))
            {
                Context.Token = ScenePortDiscoveryFile.TryReadToken(projectPath) ?? ScenePortAuth.GenerateToken();
            }

            Context.TokenRequired = IsAuthRequired();
            Context.PolicyProfile = ResolvePolicyProfile();
            Context.TokenStorage = Environment.GetEnvironmentVariable("SCENEPORT_TOKEN") != null ? "env" : "library";
            Context.TokenFingerprint = ScenePortAuth.Fingerprint(Context.Token);

            for (var port = DefaultPort; port <= MaxPort; port++)
            {
                var candidate = new ScenePortHttpServer(port, Router, ExecuteOnMainThread, RequestGate);
                try
                {
                    candidate.Start();
                    server = candidate;
                    Context.BoundPort = port;
                    WriteDiscoveryFile(projectPath, port);
                    nextHeartbeatAt = EditorApplication.timeSinceStartup + HeartbeatIntervalSeconds;
                    Debug.Log("ScenePort bridge listening at http://127.0.0.1:" + port);
                    return;
                }
                catch (HttpListenerException)
                {
                    candidate.Stop();
                }
                catch (SocketException)
                {
                    candidate.Stop();
                }
                catch (Exception ex)
                {
                    candidate.Stop();
                    Debug.LogError("ScenePort failed to start on port " + port + ": " + ex.Message);
                    return;
                }
            }

            Debug.LogError(
                "ScenePort could not bind any port in " + DefaultPort + "-" + MaxPort +
                ". Close other Unity instances or whatever owns those ports.");
        }

        [MenuItem("Tools/ScenePort/Stop Bridge")]
        public static void Stop()
        {
            try
            {
                server?.Stop();
            }
            catch
            {
                // Ignore shutdown races during domain reload.
            }

            server = null;
            if (hooksRegistered)
            {
                ScenePortDiscoveryFile.DeleteIfOwner(ScenePortPaths.ProjectPath(), OwnerLeaseId);
            }
        }

        [MenuItem("Tools/ScenePort/Copy Bridge URL")]
        public static void CopyBridgeUrl()
        {
            EditorGUIUtility.systemCopyBuffer = "http://127.0.0.1:" + Context.BoundPort;
            Debug.Log("ScenePort bridge URL copied to clipboard.");
        }

        [MenuItem("Tools/ScenePort/Require Auth Token", false, 100)]
        public static void ToggleRequireAuth()
        {
            var disabled = EditorUserSettings.GetConfigValue(AuthDisabledKey) == "true";
            EditorUserSettings.SetConfigValue(AuthDisabledKey, disabled ? "false" : "true");
            Context.TokenRequired = IsAuthRequired();
            WriteDiscoveryFile(ScenePortPaths.ProjectPath(), Context.BoundPort);
            Debug.Log("ScenePort auth token requirement: " + (Context.TokenRequired ? "ON" : "OFF"));
        }

        [MenuItem("Tools/ScenePort/Require Auth Token", true)]
        public static bool ToggleRequireAuthValidate()
        {
            Menu.SetChecked("Tools/ScenePort/Require Auth Token", IsAuthRequired());
            return true;
        }

        private static bool IsAuthRequired()
        {
            return EditorUserSettings.GetConfigValue(AuthDisabledKey) != "true";
        }

        private static void WriteDiscoveryFile(string projectPath, int port)
        {
            if (port <= 0)
            {
                return;
            }

            ScenePortDiscoveryFile.Write(projectPath, new ScenePortDiscoveryFile.BridgeInfo
            {
                bridgeVersion = Context.Version,
                protocolVersion = Context.ProtocolVersion,
                capabilitiesHash = Context.CapabilitiesHash,
                url = "http://127.0.0.1:" + port,
                port = port,
                token = Context.Token,
                projectPath = projectPath,
                projectId = PlayerSettings.productGUID.ToString("N"),
                projectName = Application.productName,
                unityVersion = Application.unityVersion,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                processName = Context.ProcessName,
                startedUtc = StartedUtc,
                heartbeatUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                expiresUtc = DateTime.UtcNow.AddSeconds(HeartbeatIntervalSeconds * 4).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ownerLeaseId = OwnerLeaseId,
                editorRole = Context.EditorRole,
                policyProfile = Context.PolicyProfile,
                tokenStorage = Context.TokenStorage,
                tokenRef = Context.TokenStorage == "env" ? "SCENEPORT_TOKEN" : "Library/ScenePort/bridge.json",
                tokenFingerprint = Context.TokenFingerprint,
            });
        }

        private static string ResolvePolicyProfile()
        {
            var fromEnv = Environment.GetEnvironmentVariable("SCENEPORT_POLICY_PROFILE");
            var value = string.IsNullOrEmpty(fromEnv) ? EditorUserSettings.GetConfigValue(PolicyProfileKey) : fromEnv;
            switch (value)
            {
                case "read-only":
                case "team-safe":
                case "playtest":
                case "full-safe-local":
                    return value;
                default:
                    return "full-safe-local";
            }
        }

        private static void OnQuitting()
        {
            ScenePortDiscoveryFile.DeleteIfOwner(ScenePortPaths.ProjectPath(), OwnerLeaseId);
        }

        private static string ExecuteOnMainThread(Func<string> action)
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                return action();
            }

            var item = new WorkItem { Action = action };
            lock (QueueLock)
            {
                WorkQueue.Enqueue(item);
            }

            if (!item.Done.Wait(TimeSpan.FromSeconds(MainThreadTimeoutSeconds)))
            {
                throw new TimeoutException("Unity main-thread execution timed out.");
            }

            if (item.Error != null)
            {
                throw item.Error;
            }

            return item.Result;
        }

        private static void Pump()
        {
            if (IsRunning && EditorApplication.timeSinceStartup >= nextHeartbeatAt)
            {
                WriteDiscoveryFile(ScenePortPaths.ProjectPath(), Context.BoundPort);
                nextHeartbeatAt = EditorApplication.timeSinceStartup + HeartbeatIntervalSeconds;
            }

            while (true)
            {
                WorkItem item = null;
                lock (QueueLock)
                {
                    if (WorkQueue.Count > 0)
                    {
                        item = WorkQueue.Dequeue();
                    }
                }

                if (item == null)
                {
                    break;
                }

                try
                {
                    item.Result = item.Action();
                }
                catch (Exception ex)
                {
                    item.Error = ex;
                }
                finally
                {
                    item.Done.Set();
                }
            }
        }

        private static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            ConsoleBuffer.Add(condition, stackTrace, type.ToString());
        }

        private static string ResolveVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ScenePortBridge).Assembly);
                return info != null && !string.IsNullOrEmpty(info.version) ? info.version : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private sealed class WorkItem
        {
            public Func<string> Action;
            public string Result;
            public Exception Error;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }
    }

    internal static class ScenePortEditorProcessRole
    {
        internal static bool ShouldHostBridge()
        {
            return ShouldHostBridge(CurrentProcessName(), Environment.GetCommandLineArgs(), Application.isBatchMode);
        }

        internal static bool ShouldHostBridge(string processName, string[] args, bool isBatchMode)
        {
            return RoleFor(processName, args, isBatchMode) != "asset-import-worker";
        }

        internal static string CurrentRole()
        {
            return RoleFor(CurrentProcessName(), Environment.GetCommandLineArgs(), Application.isBatchMode);
        }

        internal static string RoleFor(string processName, string[] args, bool isBatchMode)
        {
            if (ContainsAssetImportWorker(processName) || ArgsContainAssetImportWorker(args))
            {
                return "asset-import-worker";
            }

            if (isBatchMode && ArgsContain(args, "-runTests"))
            {
                return "batchmode-tests";
            }

            return isBatchMode ? "batchmode" : "editor";
        }

        internal static string CurrentProcessName()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            }
            catch
            {
                return "unknown";
            }
        }

        private static bool ArgsContainAssetImportWorker(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (ContainsAssetImportWorker(args[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ArgsContain(string[] args, string value)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsAssetImportWorker(string value)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf("AssetImportWorker", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
