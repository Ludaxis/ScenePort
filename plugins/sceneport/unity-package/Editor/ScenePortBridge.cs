using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    [InitializeOnLoad]
    public static class ScenePortBridge
    {
        public const int DefaultPort = 38987;

        private const int MaxLogs = 500;
        private const int MaxFailedTests = 50;
        private const int MainThreadTimeoutSeconds = 30;
        private static readonly object QueueLock = new object();
        private static readonly Queue<WorkItem> WorkQueue = new Queue<WorkItem>();
        private static readonly List<LogEntry> Logs = new List<LogEntry>();
        private static readonly object LogLock = new object();
        private static readonly object TestLock = new object();
        private static readonly ScenePortTestCallbacks TestCallbacks = new ScenePortTestCallbacks();

        private static HttpListener listener;
        private static Thread listenerThread;
        private static bool running;
        private static int mainThreadId;
        private static TestRunSummary lastEditModeTestRun = TestRunSummary.Empty("editmode");
        private static TestRunSummary lastPlayModeTestRun = TestRunSummary.Empty("playmode");

        static ScenePortBridge()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            Application.logMessageReceived += CaptureLog;
            TestRunnerApi.RegisterTestCallback(TestCallbacks);
            Start();
        }

        [MenuItem("Tools/ScenePort/Start Bridge")]
        public static void Start()
        {
            if (running)
            {
                return;
            }

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + DefaultPort + "/");
                listener.Start();
                running = true;

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "ScenePort MCP Bridge"
                };
                listenerThread.Start();
                Debug.Log("ScenePort bridge listening at http://127.0.0.1:" + DefaultPort);
            }
            catch (Exception ex)
            {
                running = false;
                Debug.LogError("ScenePort failed to start: " + ex.Message);
            }
        }

        [MenuItem("Tools/ScenePort/Stop Bridge")]
        public static void Stop()
        {
            running = false;

            try
            {
                if (listener != null)
                {
                    listener.Close();
                    listener = null;
                }
            }
            catch
            {
                // Ignore shutdown races during domain reload.
            }
        }

        [MenuItem("Tools/ScenePort/Copy Bridge URL")]
        public static void CopyBridgeUrl()
        {
            EditorGUIUtility.systemCopyBuffer = "http://127.0.0.1:" + DefaultPort;
            Debug.Log("ScenePort bridge URL copied to clipboard.");
        }

        private static void ListenLoop()
        {
            while (running && listener != null && listener.IsListening)
            {
                try
                {
                    var context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError("ScenePort listener error: " + ex.Message);
                }
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrEmpty(path))
                {
                    path = "/health";
                }

                string body = null;
                if (context.Request.HasEntityBody)
                {
                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                var response = ExecuteOnMainThread(() => Route(path, context.Request.Url.Query, body));
                Write(context, 200, response);
            }
            catch (Exception ex)
            {
                Write(context, 500, "{\"status\":\"error\",\"error\":\"" + Escape(ex.Message) + "\"}");
            }
        }

        private static string Route(string path, string queryString, string body)
        {
            var query = ParseQuery(queryString);

            switch (path)
            {
                case "/health":
                    return HealthJson();
                case "/scene":
                    return SceneJson();
                case "/scene-hierarchy":
                    return SceneHierarchyJson(
                        GetInt(query, "limit", 200),
                        GetInt(query, "maxDepth", 8));
                case "/selection":
                    return SelectionJson();
                case "/console":
                    return ConsoleJson(
                        GetInt(query, "limit", 100),
                        GetString(query, "type", "all"));
                case "/game-object":
                    return GameObjectJson(query);
                case "/components":
                    return ComponentsJson(query);
                case "/create-game-object":
                    return CreateGameObjectJson(body, query);
                case "/set-transform":
                    return SetTransformJson(body, query);
                case "/add-component":
                    return AddComponentJson(body, query);
                case "/set-serialized-property":
                    return SetSerializedPropertyJson(body, query);
                case "/asset-search":
                    return AssetSearchJson(query);
                case "/compilation-status":
                    return CompilationStatusJson();
                case "/run-tests":
                    return RunTestsJson(body, query);
                case "/tests-last":
                    return TestsLastJson(GetString(query, "mode", ExtractString(body, "mode", "editmode")));
                case "/capture-game-view":
                    return CaptureGameViewJson(body, query);
                case "/play-mode":
                    return PlayModeJson(body, query);
                case "/packages":
                    return PackagesJson();
                default:
                    return "{\"status\":\"error\",\"error\":\"Unknown endpoint: " + Escape(path) + "\"}";
            }
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

        private static string HealthJson()
        {
            var scene = SceneManager.GetActiveScene();
            return "{"
                + "\"status\":\"ok\","
                + "\"bridge\":\"sceneport\","
                + "\"version\":\"0.2.0\","
                + "\"unityVersion\":\"" + Escape(Application.unityVersion) + "\","
                + "\"projectPath\":\"" + Escape(ProjectPath()) + "\","
                + "\"activeScene\":\"" + Escape(scene.name) + "\","
                + "\"isPlaying\":" + Bool(EditorApplication.isPlaying) + ","
                + "\"isCompiling\":" + Bool(EditorApplication.isCompiling) + ","
                + "\"isUpdating\":" + Bool(EditorApplication.isUpdating) + ","
                + "\"port\":" + DefaultPort
                + "}";
        }

        private static string SceneJson()
        {
            var scene = SceneManager.GetActiveScene();
            return "{"
                + "\"status\":\"ok\","
                + "\"name\":\"" + Escape(scene.name) + "\","
                + "\"path\":\"" + Escape(scene.path) + "\","
                + "\"buildIndex\":" + scene.buildIndex + ","
                + "\"rootCount\":" + scene.rootCount + ","
                + "\"isDirty\":" + Bool(scene.isDirty) + ","
                + "\"isLoaded\":" + Bool(scene.isLoaded) + ","
                + "\"isValid\":" + Bool(scene.IsValid())
                + "}";
        }

        private static string SceneHierarchyJson(int limit, int maxDepth)
        {
            limit = Mathf.Clamp(limit, 1, 1000);
            maxDepth = Mathf.Clamp(maxDepth, 0, 32);

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var count = 0;
            var builder = new StringBuilder();

            builder.Append("{\"status\":\"ok\",\"scene\":\"").Append(Escape(scene.name)).Append("\",\"objects\":[");
            for (var i = 0; i < roots.Length && count < limit; i++)
            {
                AppendGameObjectSummary(builder, roots[i], 0, maxDepth, limit, ref count);
            }
            builder.Append("],\"truncated\":").Append(Bool(count >= limit)).Append("}");
            return builder.ToString();
        }

        private static void AppendGameObjectSummary(StringBuilder builder, GameObject go, int depth, int maxDepth, int limit, ref int count)
        {
            if (count >= limit)
            {
                return;
            }

            if (count > 0)
            {
                builder.Append(",");
            }

            count++;
            builder.Append("{")
                .Append("\"name\":\"").Append(Escape(go.name)).Append("\",")
                .Append("\"path\":\"").Append(Escape(GetPath(go.transform))).Append("\",")
                .Append("\"instanceId\":").Append(go.GetInstanceID()).Append(",")
                .Append("\"active\":").Append(Bool(go.activeSelf)).Append(",")
                .Append("\"activeInHierarchy\":").Append(Bool(go.activeInHierarchy)).Append(",")
                .Append("\"depth\":").Append(depth).Append(",")
                .Append("\"childCount\":").Append(go.transform.childCount).Append(",")
                .Append("\"components\":[");

            var components = go.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }
                builder.Append("\"").Append(Escape(ComponentTypeName(components[i]))).Append("\"");
            }

            builder.Append("]}");

            if (depth >= maxDepth)
            {
                return;
            }

            for (var i = 0; i < go.transform.childCount && count < limit; i++)
            {
                AppendGameObjectSummary(builder, go.transform.GetChild(i).gameObject, depth + 1, maxDepth, limit, ref count);
            }
        }

        private static string SelectionJson()
        {
            var selection = Selection.gameObjects;
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"objects\":[");
            for (var i = 0; i < selection.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }
                AppendGameObjectReference(builder, selection[i]);
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static string ConsoleJson(int limit, string type)
        {
            limit = Mathf.Clamp(limit, 1, 500);
            type = (type ?? "all").ToLowerInvariant();

            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"logs\":[");
            var written = 0;

            lock (LogLock)
            {
                for (var i = Logs.Count - 1; i >= 0 && written < limit; i--)
                {
                    var entry = Logs[i];
                    if (type != "all" && !string.Equals(type, entry.Type.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (written > 0)
                    {
                        builder.Append(",");
                    }

                    AppendLogEntry(builder, entry);
                    written++;
                }
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string GameObjectJson(Dictionary<string, string> query)
        {
            var go = ResolveGameObject(GetInt(query, "instanceId", 0), GetString(query, "path", null));
            if (go == null)
            {
                return ErrorJson("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var includeComponents = GetBool(query, "includeComponents", true);
            var propertyLimit = GetInt(query, "propertyLimit", 40);
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"object\":");
            AppendGameObjectDetail(builder, go, includeComponents, propertyLimit);
            builder.Append("}");
            return builder.ToString();
        }

        private static string ComponentsJson(Dictionary<string, string> query)
        {
            var go = ResolveGameObject(GetInt(query, "instanceId", 0), GetString(query, "path", null));
            if (go == null)
            {
                return ErrorJson("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var propertyLimit = GetInt(query, "propertyLimit", 80);
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"object\":");
            AppendGameObjectReference(builder, go);
            builder.Append(",\"components\":[");
            AppendComponents(builder, go, propertyLimit);
            builder.Append("]}");
            return builder.ToString();
        }

        private static string CreateGameObjectJson(string body, Dictionary<string, string> query)
        {
            var name = ExtractString(body, "name", GetString(query, "name", "ScenePort GameObject"));
            var parentPath = ExtractString(body, "parentPath", GetString(query, "parentPath", null));

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Create GameObject");

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindByPath(parentPath);
                if (parent != null)
                {
                    Undo.SetTransformParent(go.transform, parent.transform, "Parent " + name);
                }
            }

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            return "{\"status\":\"ok\",\"object\":" + GameObjectReferenceJson(go) + "}";
        }

        private static string SetTransformJson(string body, Dictionary<string, string> query)
        {
            var instanceId = ExtractInt(body, "instanceId", GetInt(query, "instanceId", 0));
            var go = ResolveGameObject(instanceId, null);

            if (go == null)
            {
                return ErrorJson("GameObject not found for instanceId " + instanceId);
            }

            Undo.RecordObject(go.transform, "ScenePort Set Transform");

            if (HasObject(body, "position"))
            {
                go.transform.localPosition = ExtractVector3(body, "position", go.transform.localPosition);
            }

            if (HasObject(body, "rotation"))
            {
                go.transform.localEulerAngles = ExtractVector3(body, "rotation", go.transform.localEulerAngles);
            }

            if (HasObject(body, "scale"))
            {
                go.transform.localScale = ExtractVector3(body, "scale", go.transform.localScale);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            return "{\"status\":\"ok\",\"object\":" + GameObjectReferenceJson(go) + "}";
        }

        private static string AddComponentJson(string body, Dictionary<string, string> query)
        {
            var instanceId = ExtractInt(body, "instanceId", GetInt(query, "instanceId", 0));
            var path = ExtractString(body, "path", GetString(query, "path", null));
            var typeName = ExtractString(body, "typeName", GetString(query, "typeName", null));
            var go = ResolveGameObject(instanceId, path);
            if (go == null)
            {
                return ErrorJson("GameObject not found. Provide instanceId or hierarchy path.");
            }

            var type = FindComponentType(typeName);
            if (type == null)
            {
                return ErrorJson("Component type not found: " + typeName);
            }

            if (go.GetComponent(type) != null && type.GetCustomAttributes(typeof(DisallowMultipleComponent), true).Length > 0)
            {
                return ErrorJson("GameObject already has a " + type.FullName + " component.");
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("ScenePort Add Component");
            var component = Undo.AddComponent(go, type);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Undo.CollapseUndoOperations(undoGroup);

            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"object\":");
            AppendGameObjectReference(builder, go);
            builder.Append(",\"component\":");
            AppendComponentDetail(builder, component, 40, Array.IndexOf(go.GetComponents<Component>(), component));
            builder.Append("}");
            return builder.ToString();
        }

        private static string SetSerializedPropertyJson(string body, Dictionary<string, string> query)
        {
            var instanceId = ExtractInt(body, "instanceId", GetInt(query, "instanceId", 0));
            var componentType = ExtractString(body, "componentType", GetString(query, "componentType", null));
            var componentIndex = ExtractInt(body, "componentIndex", GetInt(query, "componentIndex", -1));
            var propertyPath = ExtractString(body, "propertyPath", GetString(query, "propertyPath", null));
            if (string.IsNullOrEmpty(propertyPath))
            {
                return ErrorJson("propertyPath is required.");
            }

            var target = ResolveSerializedTarget(instanceId, componentType, componentIndex);
            if (target == null)
            {
                return ErrorJson("Serialized target not found for instanceId " + instanceId);
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return ErrorJson("SerializedProperty not found: " + propertyPath);
            }

            Undo.RecordObject(target, "ScenePort Set Serialized Property");
            var changed = ApplySerializedValue(property, body);
            if (!changed)
            {
                return ErrorJson("Unsupported SerializedProperty type for " + propertyPath + ": " + property.propertyType);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            var go = target is Component component ? component.gameObject : target as GameObject;
            if (go != null)
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            return "{\"status\":\"ok\",\"target\":" + ObjectReferenceJson(target) + ",\"propertyPath\":\"" + Escape(propertyPath) + "\",\"propertyType\":\"" + Escape(property.propertyType.ToString()) + "\"}";
        }

        private static string AssetSearchJson(Dictionary<string, string> query)
        {
            var search = GetString(query, "query", GetString(query, "q", null));
            if (string.IsNullOrEmpty(search))
            {
                return ErrorJson("query is required.");
            }

            var limit = Mathf.Clamp(GetInt(query, "limit", 100), 1, 500);
            var folders = SplitCsv(GetString(query, "folders", null)).Where(f => f.StartsWith("Assets", StringComparison.Ordinal)).ToArray();
            var guids = folders.Length > 0 ? AssetDatabase.FindAssets(search, folders) : AssetDatabase.FindAssets(search);
            var count = Mathf.Min(limit, guids.Length);
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",\"query\":\"").Append(Escape(search)).Append("\",\"count\":").Append(guids.Length).Append(",\"assets\":[");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                var guid = guids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                builder.Append("{\"guid\":\"").Append(Escape(guid)).Append("\",")
                    .Append("\"path\":\"").Append(Escape(path)).Append("\",")
                    .Append("\"name\":\"").Append(Escape(Path.GetFileNameWithoutExtension(path))).Append("\",")
                    .Append("\"type\":\"").Append(Escape(type == null ? "Unknown" : type.FullName)).Append("\",")
                    .Append("\"labels\":[");

                var labels = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(path));
                for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    if (labelIndex > 0)
                    {
                        builder.Append(",");
                    }
                    builder.Append("\"").Append(Escape(labels[labelIndex])).Append("\"");
                }
                builder.Append("]}");
            }

            builder.Append("],\"truncated\":").Append(Bool(guids.Length > count)).Append("}");
            return builder.ToString();
        }

        private static string CompilationStatusJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",")
                .Append("\"isCompiling\":").Append(Bool(EditorApplication.isCompiling)).Append(",")
                .Append("\"isUpdating\":").Append(Bool(EditorApplication.isUpdating)).Append(",")
                .Append("\"isPlaying\":").Append(Bool(EditorApplication.isPlaying)).Append(",")
                .Append("\"isPlayingOrWillChangePlaymode\":").Append(Bool(EditorApplication.isPlayingOrWillChangePlaymode)).Append(",")
                .Append("\"timeSinceStartup\":").Append(EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture)).Append(",")
                .Append("\"recentErrors\":[");

            var written = 0;
            lock (LogLock)
            {
                for (var i = Logs.Count - 1; i >= 0 && written < 50; i--)
                {
                    var entry = Logs[i];
                    if (entry.Type != "Error" && entry.Type != "Exception" && entry.Type != "Assert")
                    {
                        continue;
                    }

                    if (written > 0)
                    {
                        builder.Append(",");
                    }
                    AppendLogEntry(builder, entry);
                    written++;
                }
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static string RunTestsJson(string body, Dictionary<string, string> query)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return ErrorJson("Unity is compiling or updating assets. Wait before starting tests.");
            }

            var modeName = NormalizeMode(ExtractString(body, "mode", GetString(query, "mode", "editmode")));
            var mode = modeName == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter
            {
                testMode = mode,
                testNames = SplitCsv(ExtractString(body, "testNames", GetString(query, "testNames", null))),
                groupNames = SplitCsv(ExtractString(body, "groupNames", GetString(query, "groupNames", null))),
                categoryNames = SplitCsv(ExtractString(body, "categoryNames", GetString(query, "categoryNames", null))),
                assemblyNames = SplitCsv(ExtractString(body, "assemblyNames", GetString(query, "assemblyNames", null)))
            };

            var settings = new ExecutionSettings(filter)
            {
                runSynchronously = mode == TestMode.EditMode && ExtractBool(body, "runSynchronously", GetBool(query, "runSynchronously", false))
            };

            var summary = new TestRunSummary
            {
                Mode = modeName,
                RunId = "pending",
                Status = "scheduled",
                StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Filter = filter.ToString()
            };
            SetLastTestRun(summary);

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var runId = api.Execute(settings);
            summary.RunId = runId;
            summary.Status = "running";
            SetLastTestRun(summary);

            return "{\"status\":\"ok\",\"run\":" + TestRunSummaryJson(summary) + "}";
        }

        private static string TestsLastJson(string mode)
        {
            var modeName = NormalizeMode(mode);
            TestRunSummary summary;
            lock (TestLock)
            {
                summary = modeName == "playmode" ? lastPlayModeTestRun.Clone() : lastEditModeTestRun.Clone();
            }

            return "{\"status\":\"ok\",\"run\":" + TestRunSummaryJson(summary) + "}";
        }

        private static string CaptureGameViewJson(string body, Dictionary<string, string> query)
        {
            var superSize = Mathf.Clamp(ExtractInt(body, "superSize", GetInt(query, "superSize", 1)), 1, 4);
            var fileName = ExtractString(body, "fileName", GetString(query, "fileName", null));
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "game-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            }

            fileName = SanitizeFileName(fileName);
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var directory = Path.Combine(ProjectPath(), "Temp", "ScenePort");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            ScreenCapture.CaptureScreenshot(path, superSize);

            return "{\"status\":\"ok\",\"path\":\"" + Escape(path) + "\",\"superSize\":" + superSize + ",\"note\":\"Unity writes screenshots asynchronously; the file may appear after a short delay.\"}";
        }

        private static string PlayModeJson(string body, Dictionary<string, string> query)
        {
            var action = ExtractString(body, "action", GetString(query, "action", "status")).ToLowerInvariant();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return ErrorJson("Unity is compiling or updating assets. Play mode changes are blocked.");
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

            return "{"
                + "\"status\":\"ok\","
                + "\"action\":\"" + Escape(action) + "\","
                + "\"isPlaying\":" + Bool(EditorApplication.isPlaying) + ","
                + "\"isPlayingOrWillChangePlaymode\":" + Bool(EditorApplication.isPlayingOrWillChangePlaymode)
                + "}";
        }

        private static string PackagesJson()
        {
            var projectPath = ProjectPath();
            var manifestPath = Path.Combine(projectPath, "Packages", "manifest.json");
            var lockPath = Path.Combine(projectPath, "Packages", "packages-lock.json");
            var manifest = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";
            var builder = new StringBuilder();
            builder.Append("{\"status\":\"ok\",")
                .Append("\"manifestPath\":\"").Append(Escape(manifestPath)).Append("\",")
                .Append("\"packagesLockPath\":\"").Append(Escape(lockPath)).Append("\",")
                .Append("\"packagesLockExists\":").Append(Bool(File.Exists(lockPath))).Append(",")
                .Append("\"dependencies\":[");

            var dependencies = ParseManifestDependencies(manifest);
            for (var i = 0; i < dependencies.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }
                builder.Append("{\"name\":\"").Append(Escape(dependencies[i].Key)).Append("\",\"version\":\"").Append(Escape(dependencies[i].Value)).Append("\"}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void CaptureLog(string condition, string stackTrace, LogType type)
        {
            lock (LogLock)
            {
                Logs.Add(new LogEntry
                {
                    Message = condition,
                    StackTrace = stackTrace,
                    Type = type.ToString(),
                    Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });

                if (Logs.Count > MaxLogs)
                {
                    Logs.RemoveAt(0);
                }
            }
        }

        private static void RecordTestRunStarted(ITestAdaptor testsToRun)
        {
            var mode = ModeFromTestMode(testsToRun.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                summary.Status = "running";
                summary.TotalCount = testsToRun.TestCaseCount;
                summary.RootName = testsToRun.FullName;
                summary.StartedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
        }

        private static void RecordTestStarted(ITestAdaptor test)
        {
            var mode = ModeFromTestMode(test.TestMode);
            lock (TestLock)
            {
                GetLastTestRunLocked(mode).LastStartedTest = test.FullName;
            }
        }

        private static void RecordTestFinished(ITestResultAdaptor result)
        {
            var mode = ModeFromTestMode(result.Test.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                if (result.ResultState.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) && summary.FailedTests.Count < MaxFailedTests)
                {
                    summary.FailedTests.Add(new FailedTest
                    {
                        Name = result.FullName,
                        Message = result.Message,
                        StackTrace = result.StackTrace
                    });
                }
            }
        }

        private static void RecordTestRunFinished(ITestResultAdaptor result)
        {
            var mode = ModeFromTestMode(result.Test.TestMode);
            lock (TestLock)
            {
                var summary = GetLastTestRunLocked(mode);
                summary.Status = "finished";
                summary.ResultState = result.ResultState;
                summary.FinishedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                summary.Duration = result.Duration;
                summary.PassCount = result.PassCount;
                summary.FailCount = result.FailCount;
                summary.SkipCount = result.SkipCount;
                summary.InconclusiveCount = result.InconclusiveCount;
                summary.AssertCount = result.AssertCount;
                summary.Message = result.Message;
                summary.StackTrace = result.StackTrace;
            }
        }

        private static GameObject ResolveGameObject(int instanceId, string path)
        {
            if (instanceId != 0)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId);
                if (obj is GameObject go)
                {
                    return go;
                }

                if (obj is Component component)
                {
                    return component.gameObject;
                }
            }

            return FindByPath(path);
        }

        private static UnityEngine.Object ResolveSerializedTarget(int instanceId, string componentType, int componentIndex)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is Component)
            {
                return obj;
            }

            if (obj is GameObject go)
            {
                if (componentIndex >= 0)
                {
                    var components = go.GetComponents<Component>();
                    return componentIndex < components.Length ? components[componentIndex] : null;
                }

                if (!string.IsNullOrEmpty(componentType))
                {
                    return go.GetComponents<Component>().FirstOrDefault(component => MatchesComponentType(component, componentType));
                }

                return go;
            }

            return obj;
        }

        private static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var found = FindInChildren(roots[i].transform, path);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static Transform FindInChildren(Transform current, string path)
        {
            if (GetPath(current) == path)
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                var found = FindInChildren(current.GetChild(i), path);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            var direct = Type.GetType(typeName, false, true);
            if (direct != null && typeof(Component).IsAssignableFrom(direct) && !direct.IsAbstract)
            {
                return direct;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var assemblyType = assembly.GetType(typeName, false, true);
                    if (assemblyType != null && typeof(Component).IsAssignableFrom(assemblyType) && !assemblyType.IsAbstract)
                    {
                        return assemblyType;
                    }

                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(Component).IsAssignableFrom(type)
                            && !type.IsAbstract
                            && (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase)))
                        {
                            return type;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Ignore partially loaded assemblies.
                }
            }

            return null;
        }

        private static bool ApplySerializedValue(SerializedProperty property, string body)
        {
            var valueKind = ExtractString(body, "valueKind", string.Empty);
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Mathf.RoundToInt(ExtractFloat(body, "numberValue", property.intValue));
                    return true;
                case SerializedPropertyType.Boolean:
                    property.boolValue = ExtractBool(body, "boolValue", property.boolValue);
                    return true;
                case SerializedPropertyType.Float:
                    property.floatValue = ExtractFloat(body, "numberValue", property.floatValue);
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = ExtractString(body, "stringValue", property.stringValue);
                    return true;
                case SerializedPropertyType.Color:
                    property.colorValue = ExtractColor(body, "colorValue", property.colorValue);
                    return true;
                case SerializedPropertyType.ObjectReference:
                    var assetPath = ExtractString(body, "objectReferenceAssetPath", ExtractString(body, "stringValue", null));
                    property.objectReferenceValue = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    return true;
                case SerializedPropertyType.LayerMask:
                    property.intValue = Mathf.RoundToInt(ExtractFloat(body, "numberValue", property.intValue));
                    return true;
                case SerializedPropertyType.Enum:
                    var enumValue = ExtractString(body, "stringValue", ExtractString(body, "enumValue", null));
                    if (!string.IsNullOrEmpty(enumValue))
                    {
                        var index = Array.IndexOf(property.enumNames, enumValue);
                        if (index < 0)
                        {
                            index = Array.IndexOf(property.enumDisplayNames, enumValue);
                        }
                        if (index >= 0)
                        {
                            property.enumValueIndex = index;
                            return true;
                        }
                    }
                    property.enumValueIndex = Mathf.RoundToInt(ExtractFloat(body, "numberValue", property.enumValueIndex));
                    return true;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = ExtractVector2(body, valueKind == "vector2" ? "vector2Value" : "value", property.vector2Value);
                    return true;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = ExtractVector3(body, valueKind == "vector3" ? "vector3Value" : "value", property.vector3Value);
                    return true;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = ExtractVector4(body, valueKind == "vector4" ? "vector4Value" : "value", property.vector4Value);
                    return true;
                case SerializedPropertyType.Rect:
                    var rect = property.rectValue;
                    var vector4 = ExtractVector4(body, valueKind == "vector4" ? "vector4Value" : "value", new Vector4(rect.x, rect.y, rect.width, rect.height));
                    property.rectValue = new Rect(vector4.x, vector4.y, vector4.z, vector4.w);
                    return true;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = new Bounds(ExtractVector3(body, "center", property.boundsValue.center), ExtractVector3(body, "size", property.boundsValue.size));
                    return true;
                case SerializedPropertyType.Quaternion:
                    var rotation = ExtractVector4(body, valueKind == "vector4" ? "vector4Value" : "value", new Vector4(property.quaternionValue.x, property.quaternionValue.y, property.quaternionValue.z, property.quaternionValue.w));
                    property.quaternionValue = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
                    return true;
                default:
                    return false;
            }
        }

        private static void AppendGameObjectDetail(StringBuilder builder, GameObject go, bool includeComponents, int propertyLimit)
        {
            builder.Append("{");
            AppendGameObjectFields(builder, go);
            builder.Append(",\"tag\":\"").Append(Escape(go.tag)).Append("\",")
                .Append("\"layer\":").Append(go.layer).Append(",")
                .Append("\"scene\":\"").Append(Escape(go.scene.name)).Append("\",")
                .Append("\"transform\":");
            AppendTransform(builder, go.transform);

            if (includeComponents)
            {
                builder.Append(",\"components\":[");
                AppendComponents(builder, go, propertyLimit);
                builder.Append("]");
            }

            builder.Append("}");
        }

        private static void AppendGameObjectReference(StringBuilder builder, GameObject go)
        {
            builder.Append("{");
            AppendGameObjectFields(builder, go);
            builder.Append("}");
        }

        private static void AppendGameObjectFields(StringBuilder builder, GameObject go)
        {
            builder.Append("\"name\":\"").Append(Escape(go.name)).Append("\",")
                .Append("\"path\":\"").Append(Escape(GetPath(go.transform))).Append("\",")
                .Append("\"instanceId\":").Append(go.GetInstanceID()).Append(",")
                .Append("\"active\":").Append(Bool(go.activeSelf)).Append(",")
                .Append("\"activeInHierarchy\":").Append(Bool(go.activeInHierarchy)).Append(",")
                .Append("\"childCount\":").Append(go.transform.childCount);
        }

        private static string GameObjectReferenceJson(GameObject go)
        {
            var builder = new StringBuilder();
            AppendGameObjectReference(builder, go);
            return builder.ToString();
        }

        private static string ObjectReferenceJson(UnityEngine.Object obj)
        {
            var builder = new StringBuilder();
            builder.Append("{\"name\":\"").Append(Escape(obj == null ? string.Empty : obj.name)).Append("\",")
                .Append("\"instanceId\":").Append(obj == null ? 0 : obj.GetInstanceID()).Append(",")
                .Append("\"type\":\"").Append(Escape(obj == null ? "null" : obj.GetType().FullName)).Append("\"}");
            return builder.ToString();
        }

        private static void AppendComponents(StringBuilder builder, GameObject go, int propertyLimit)
        {
            var components = go.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }
                AppendComponentDetail(builder, components[i], propertyLimit, i);
            }
        }

        private static void AppendComponentDetail(StringBuilder builder, Component component, int propertyLimit, int index)
        {
            builder.Append("{\"index\":").Append(index).Append(",");
            if (component == null)
            {
                builder.Append("\"type\":\"MissingScript\",\"instanceId\":0,\"enabled\":null,\"properties\":[]}");
                return;
            }

            builder.Append("\"type\":\"").Append(Escape(component.GetType().Name)).Append("\",")
                .Append("\"fullType\":\"").Append(Escape(component.GetType().FullName)).Append("\",")
                .Append("\"assemblyQualifiedName\":\"").Append(Escape(component.GetType().AssemblyQualifiedName)).Append("\",")
                .Append("\"instanceId\":").Append(component.GetInstanceID()).Append(",");

            if (component is Behaviour behaviour)
            {
                builder.Append("\"enabled\":").Append(Bool(behaviour.enabled)).Append(",");
            }
            else if (component is Renderer renderer)
            {
                builder.Append("\"enabled\":").Append(Bool(renderer.enabled)).Append(",");
            }
            else
            {
                builder.Append("\"enabled\":null,");
            }

            builder.Append("\"properties\":[");
            AppendSerializedProperties(builder, component, propertyLimit);
            builder.Append("]}");
        }

        private static void AppendSerializedProperties(StringBuilder builder, UnityEngine.Object target, int propertyLimit)
        {
            propertyLimit = Mathf.Clamp(propertyLimit, 0, 300);
            if (propertyLimit == 0 || target == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.GetIterator();
            var enterChildren = true;
            var count = 0;
            while (property.NextVisible(enterChildren) && count < propertyLimit)
            {
                enterChildren = false;
                if (count > 0)
                {
                    builder.Append(",");
                }

                builder.Append("{\"path\":\"").Append(Escape(property.propertyPath)).Append("\",")
                    .Append("\"displayName\":\"").Append(Escape(property.displayName)).Append("\",")
                    .Append("\"type\":\"").Append(Escape(property.propertyType.ToString())).Append("\",")
                    .Append("\"editable\":").Append(Bool(property.editable)).Append(",")
                    .Append("\"value\":\"").Append(Escape(SerializedPropertyValue(property))).Append("\"}");
                count++;
            }
        }

        private static string SerializedPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue.ToString();
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null ? "null" : property.objectReferenceValue.name;
                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                        ? property.enumDisplayNames[property.enumValueIndex]
                        : property.enumValueIndex.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector2:
                    return property.vector2Value.ToString();
                case SerializedPropertyType.Vector3:
                    return property.vector3Value.ToString();
                case SerializedPropertyType.Vector4:
                    return property.vector4Value.ToString();
                case SerializedPropertyType.Rect:
                    return property.rectValue.ToString();
                case SerializedPropertyType.Bounds:
                    return property.boundsValue.ToString();
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue.eulerAngles.ToString();
                default:
                    return property.hasVisibleChildren ? "<object>" : string.Empty;
            }
        }

        private static void AppendTransform(StringBuilder builder, Transform transform)
        {
            builder.Append("{\"localPosition\":");
            AppendVector3(builder, transform.localPosition);
            builder.Append(",\"localEulerAngles\":");
            AppendVector3(builder, transform.localEulerAngles);
            builder.Append(",\"localScale\":");
            AppendVector3(builder, transform.localScale);
            builder.Append(",\"worldPosition\":");
            AppendVector3(builder, transform.position);
            builder.Append("}");
        }

        private static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            builder.Append("{\"x\":").Append(Float(value.x)).Append(",\"y\":").Append(Float(value.y)).Append(",\"z\":").Append(Float(value.z)).Append("}");
        }

        private static void AppendLogEntry(StringBuilder builder, LogEntry entry)
        {
            builder.Append("{\"type\":\"").Append(Escape(entry.Type)).Append("\",")
                .Append("\"utc\":\"").Append(Escape(entry.Utc)).Append("\",")
                .Append("\"message\":\"").Append(Escape(entry.Message)).Append("\",")
                .Append("\"stackTrace\":\"").Append(Escape(entry.StackTrace)).Append("\"}");
        }

        private static string TestRunSummaryJson(TestRunSummary summary)
        {
            var builder = new StringBuilder();
            builder.Append("{\"mode\":\"").Append(Escape(summary.Mode)).Append("\",")
                .Append("\"runId\":\"").Append(Escape(summary.RunId)).Append("\",")
                .Append("\"status\":\"").Append(Escape(summary.Status)).Append("\",")
                .Append("\"resultState\":\"").Append(Escape(summary.ResultState)).Append("\",")
                .Append("\"startedUtc\":\"").Append(Escape(summary.StartedUtc)).Append("\",")
                .Append("\"finishedUtc\":\"").Append(Escape(summary.FinishedUtc)).Append("\",")
                .Append("\"duration\":").Append(summary.Duration.ToString(CultureInfo.InvariantCulture)).Append(",")
                .Append("\"totalCount\":").Append(summary.TotalCount).Append(",")
                .Append("\"passCount\":").Append(summary.PassCount).Append(",")
                .Append("\"failCount\":").Append(summary.FailCount).Append(",")
                .Append("\"skipCount\":").Append(summary.SkipCount).Append(",")
                .Append("\"inconclusiveCount\":").Append(summary.InconclusiveCount).Append(",")
                .Append("\"assertCount\":").Append(summary.AssertCount).Append(",")
                .Append("\"rootName\":\"").Append(Escape(summary.RootName)).Append("\",")
                .Append("\"lastStartedTest\":\"").Append(Escape(summary.LastStartedTest)).Append("\",")
                .Append("\"message\":\"").Append(Escape(summary.Message)).Append("\",")
                .Append("\"stackTrace\":\"").Append(Escape(summary.StackTrace)).Append("\",")
                .Append("\"filter\":\"").Append(Escape(summary.Filter)).Append("\",")
                .Append("\"failedTests\":[");

            for (var i = 0; i < summary.FailedTests.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }
                builder.Append("{\"name\":\"").Append(Escape(summary.FailedTests[i].Name)).Append("\",")
                    .Append("\"message\":\"").Append(Escape(summary.FailedTests[i].Message)).Append("\",")
                    .Append("\"stackTrace\":\"").Append(Escape(summary.FailedTests[i].StackTrace)).Append("\"}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static TestRunSummary GetLastTestRunLocked(string mode)
        {
            return mode == "playmode" ? lastPlayModeTestRun : lastEditModeTestRun;
        }

        private static void SetLastTestRun(TestRunSummary summary)
        {
            lock (TestLock)
            {
                if (summary.Mode == "playmode")
                {
                    lastPlayModeTestRun = summary;
                }
                else
                {
                    lastEditModeTestRun = summary;
                }
            }
        }

        private static string ModeFromTestMode(TestMode mode)
        {
            return (mode & TestMode.PlayMode) == TestMode.PlayMode && (mode & TestMode.EditMode) != TestMode.EditMode ? "playmode" : "editmode";
        }

        private static string NormalizeMode(string mode)
        {
            return string.Equals(mode, "playmode", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "play", StringComparison.OrdinalIgnoreCase)
                ? "playmode"
                : "editmode";
        }

        private static bool MatchesComponentType(Component component, string typeName)
        {
            if (component == null || string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            var type = component.GetType();
            return string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.AssemblyQualifiedName, typeName, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComponentTypeName(Component component)
        {
            return component == null ? "MissingScript" : component.GetType().Name;
        }

        private static string GetPath(Transform transform)
        {
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }

        private static string ProjectPath()
        {
            return Application.dataPath.EndsWith("/Assets", StringComparison.Ordinal)
                ? Application.dataPath.Substring(0, Application.dataPath.Length - "/Assets".Length)
                : Application.dataPath;
        }

        private static Dictionary<string, string> ParseQuery(string queryString)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(queryString))
            {
                return result;
            }

            var trimmed = queryString.TrimStart('?');
            var parts = trimmed.Split('&');
            for (var i = 0; i < parts.Length; i++)
            {
                var pair = parts[i].Split(new[] { '=' }, 2);
                if (pair.Length == 2)
                {
                    result[Uri.UnescapeDataString(pair[0])] = Uri.UnescapeDataString(pair[1]);
                }
            }

            return result;
        }

        private static List<KeyValuePair<string, string>> ParseManifestDependencies(string manifest)
        {
            var result = new List<KeyValuePair<string, string>>();
            var dependenciesMatch = Regex.Match(manifest ?? string.Empty, "\"dependencies\"\\s*:\\s*\\{(?<body>.*?)\\}", RegexOptions.Singleline);
            if (!dependenciesMatch.Success)
            {
                return result;
            }

            var body = dependenciesMatch.Groups["body"].Value;
            foreach (Match match in Regex.Matches(body, "\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<version>(?:\\\\.|[^\"])*)\""))
            {
                result.Add(new KeyValuePair<string, string>(match.Groups["name"].Value, Regex.Unescape(match.Groups["version"].Value)));
            }

            return result;
        }

        private static int GetInt(Dictionary<string, string> query, string key, int fallback)
        {
            if (query.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool GetBool(Dictionary<string, string> query, string key, bool fallback)
        {
            if (query.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static string GetString(Dictionary<string, string> query, string key, string fallback)
        {
            return query.TryGetValue(key, out var value) ? value : fallback;
        }

        private static string ExtractString(string json, string key, string fallback)
        {
            if (string.IsNullOrEmpty(json))
            {
                return fallback;
            }

            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
            return match.Success ? Regex.Unescape(match.Groups["value"].Value) : fallback;
        }

        private static int ExtractInt(string json, string key, int fallback)
        {
            if (string.IsNullOrEmpty(json))
            {
                return fallback;
            }

            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(?<value>-?\\d+)");
            return match.Success && int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private static bool ExtractBool(string json, string key, bool fallback)
        {
            if (string.IsNullOrEmpty(json))
            {
                return fallback;
            }

            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);
            return match.Success && bool.TryParse(match.Groups["value"].Value, out var parsed) ? parsed : fallback;
        }

        private static bool HasObject(string json, string key)
        {
            return !string.IsNullOrEmpty(json) && Regex.IsMatch(json, "\"" + Regex.Escape(key) + "\"\\s*:");
        }

        private static string[] SplitCsv(string value)
        {
            return string.IsNullOrEmpty(value)
                ? null
                : value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
        }

        private static Vector2 ExtractVector2(string json, string key, Vector2 fallback)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            if (!match.Success)
            {
                return fallback;
            }

            var body = match.Groups["body"].Value;
            return new Vector2(
                ExtractFloat(body, "x", fallback.x),
                ExtractFloat(body, "y", fallback.y));
        }

        private static Vector3 ExtractVector3(string json, string key, Vector3 fallback)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            if (!match.Success)
            {
                return fallback;
            }

            var body = match.Groups["body"].Value;
            return new Vector3(
                ExtractFloat(body, "x", fallback.x),
                ExtractFloat(body, "y", fallback.y),
                ExtractFloat(body, "z", fallback.z));
        }

        private static Vector4 ExtractVector4(string json, string key, Vector4 fallback)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            if (!match.Success)
            {
                return fallback;
            }

            var body = match.Groups["body"].Value;
            return new Vector4(
                ExtractFloat(body, "x", fallback.x),
                ExtractFloat(body, "y", fallback.y),
                ExtractFloat(body, "z", fallback.z),
                ExtractFloat(body, "w", fallback.w));
        }

        private static Color ExtractColor(string json, string key, Color fallback)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            if (!match.Success)
            {
                return fallback;
            }

            var body = match.Groups["body"].Value;
            return new Color(
                ExtractFloat(body, "r", fallback.r),
                ExtractFloat(body, "g", fallback.g),
                ExtractFloat(body, "b", fallback.b),
                ExtractFloat(body, "a", fallback.a));
        }

        private static float ExtractFloat(string json, string key, float fallback)
        {
            var match = Regex.Match(json ?? string.Empty, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(?<value>-?\\d+(?:\\.\\d+)?)");
            return match.Success && float.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                builder.Append(invalid.Contains(value[i]) ? '-' : value[i]);
            }

            return builder.ToString();
        }

        private static void Write(HttpListenerContext context, int status, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.AddHeader("Access-Control-Allow-Origin", "http://127.0.0.1");
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        private static string ErrorJson(string message)
        {
            return "{\"status\":\"error\",\"error\":\"" + Escape(message) + "\"}";
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Float(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class WorkItem
        {
            public Func<string> Action;
            public string Result;
            public Exception Error;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private sealed class LogEntry
        {
            public string Message;
            public string StackTrace;
            public string Type;
            public string Utc;
        }

        private sealed class TestRunSummary
        {
            public string Mode;
            public string RunId;
            public string Status;
            public string ResultState;
            public string StartedUtc;
            public string FinishedUtc;
            public string RootName;
            public string LastStartedTest;
            public string Message;
            public string StackTrace;
            public string Filter;
            public int TotalCount;
            public int PassCount;
            public int FailCount;
            public int SkipCount;
            public int InconclusiveCount;
            public int AssertCount;
            public double Duration;
            public List<FailedTest> FailedTests = new List<FailedTest>();

            public static TestRunSummary Empty(string mode)
            {
                return new TestRunSummary
                {
                    Mode = mode,
                    RunId = string.Empty,
                    Status = "not-run",
                    ResultState = string.Empty,
                    StartedUtc = string.Empty,
                    FinishedUtc = string.Empty,
                    RootName = string.Empty,
                    LastStartedTest = string.Empty,
                    Message = string.Empty,
                    StackTrace = string.Empty,
                    Filter = string.Empty
                };
            }

            public TestRunSummary Clone()
            {
                return new TestRunSummary
                {
                    Mode = Mode,
                    RunId = RunId,
                    Status = Status,
                    ResultState = ResultState,
                    StartedUtc = StartedUtc,
                    FinishedUtc = FinishedUtc,
                    RootName = RootName,
                    LastStartedTest = LastStartedTest,
                    Message = Message,
                    StackTrace = StackTrace,
                    Filter = Filter,
                    TotalCount = TotalCount,
                    PassCount = PassCount,
                    FailCount = FailCount,
                    SkipCount = SkipCount,
                    InconclusiveCount = InconclusiveCount,
                    AssertCount = AssertCount,
                    Duration = Duration,
                    FailedTests = new List<FailedTest>(FailedTests)
                };
            }
        }

        private sealed class FailedTest
        {
            public string Name;
            public string Message;
            public string StackTrace;
        }

        private sealed class ScenePortTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                RecordTestRunStarted(testsToRun);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                RecordTestRunFinished(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
                RecordTestStarted(test);
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                RecordTestFinished(result);
            }
        }
    }
}
