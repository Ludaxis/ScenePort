using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace ScenePort.McpBridge.Editor
{
    internal static class ProofHandlers
    {
        internal static object TestsRun(ScenePortRequest req, ScenePortContext ctx)
        {
            return TestRunHandlers.RunTests(req, ctx);
        }

        internal static object TestsStatus(ScenePortRequest req, ScenePortContext ctx)
        {
            return TestRunHandlers.TestsLast(req, ctx);
        }

        internal static object TestsWait(ScenePortRequest req, ScenePortContext ctx)
        {
            return TestRunHandlers.TestsLast(req, ctx);
        }

        internal static object TestsArtifacts(ScenePortRequest req, ScenePortContext ctx)
        {
            var mode = req.ExtractString("mode", req.GetString("mode", "editmode"));
            var run = ((TestRunResponse)TestRunHandlers.TestsLast(new ScenePortRequest("mode=" + mode, ""), ctx)).Run;
            return WriteTestArtifacts(run, ctx);
        }

        internal static object AssertionsCatalog(ScenePortRequest req, ScenePortContext ctx)
        {
            return new
            {
                status = "ok",
                assertions = new[]
                {
                    "health.status",
                    "compilation.clean",
                    "console.errorCount",
                    "scene.objectExists",
                    "asset.exists",
                },
                operators = new[] { "equals", "notEquals", "contains", "exists", "absent", "lt", "lte", "gt", "gte" },
            };
        }

        internal static object AssertionsEvaluate(ScenePortRequest req, ScenePortContext ctx)
        {
            var response = new AssertionEvaluateResponse { Passed = true };
            var checks = req.Body["checks"] as JArray;
            if (checks == null || checks.Count == 0)
            {
                response.Results.Add(new AssertionResultDto { Id = "empty", Passed = true, Message = "No assertions supplied." });
            }
            else
            {
                for (var i = 0; i < checks.Count; i++)
                {
                    var obj = checks[i] as JObject;
                    if (obj == null)
                    {
                        continue;
                    }

                    var result = EvaluateAssertion(obj, ctx);
                    response.Results.Add(result);
                    response.Passed = response.Passed && result.Passed;
                }
            }

            var directory = ArtifactDirectory("assertions", null);
            Directory.CreateDirectory(directory);
            response.ArtifactPath = Path.Combine(directory, "assertions-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(response.ArtifactPath, ScenePortJson.Serialize(response));
            return response;
        }

        internal static object GoldenCapture(ScenePortRequest req, ScenePortContext ctx)
        {
            var baselineId = SafeId(req.ExtractString("baselineId", req.GetString("baselineId", "default")));
            var fileName = SafeId(req.ExtractString("fileName", "actual")) + ".png";
            var directory = ArtifactDirectory("golden", baselineId);
            Directory.CreateDirectory(directory);
            var inline = req.ExtractBool("inline", req.GetBool("inline", true));
            var maxEdge = Mathf.Clamp(req.ExtractInt("maxEdge", req.GetInt("maxEdge", 1024)), 64, 4096);
            var source = EditorStateHandlers.CaptureGameViewFile(fileName, req.ExtractInt("superSize", 1), inline, maxEdge);
            return new
            {
                status = "ok",
                baselineId,
                actualPath = source.Path,
                directory,
                note = source.Note,
                imageBase64 = source.ImageBase64,
                width = source.Width,
                height = source.Height,
            };
        }

        internal static object GoldenCompare(ScenePortRequest req, ScenePortContext ctx)
        {
            var baselinePath = req.ExtractString("baselinePath", req.GetString("baselinePath", null));
            var actualPath = req.ExtractString("actualPath", req.GetString("actualPath", null));
            if (string.IsNullOrEmpty(baselinePath) || string.IsNullOrEmpty(actualPath))
            {
                return new ErrorResponse("request.invalid", "baselinePath and actualPath are required.", "request", false);
            }
            if (!File.Exists(baselinePath) || !File.Exists(actualPath))
            {
                return new ErrorResponse("request.invalid", "baselinePath and actualPath must reference existing files.", "request", false);
            }

            // Per-channel tolerance (0..1) above which a channel counts as changed.
            var threshold = Mathf.Clamp01(req.ExtractFloat("threshold", 0.02f));
            // Max percent of changed pixels still allowed to "pass".
            var passThreshold = Mathf.Clamp(req.ExtractFloat("passThreshold", 0f), 0f, 100f);
            var maxEdge = Mathf.Clamp(req.ExtractInt("maxEdge", req.GetInt("maxEdge", 1024)), 64, 4096);

            var baseline = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var actual = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D diff = null;
            try
            {
                ImageConversion.LoadImage(baseline, File.ReadAllBytes(baselinePath));
                ImageConversion.LoadImage(actual, File.ReadAllBytes(actualPath));

                var dimensionsMatch = baseline.width == actual.width && baseline.height == actual.height;
                // Compare over the overlapping region so a size mismatch still yields a best-effort diff.
                var width = Mathf.Min(baseline.width, actual.width);
                var height = Mathf.Min(baseline.height, actual.height);

                var basePixels = baseline.GetPixels32();
                var actPixels = actual.GetPixels32();
                var tolerance = Mathf.RoundToInt(threshold * 255f);

                var totalPixels = width * height;
                var changedPixels = 0;
                var minX = int.MaxValue;
                var minY = int.MaxValue;
                var maxX = -1;
                var maxY = -1;

                diff = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false);
                var diffPixels = new Color32[Mathf.Max(1, width) * Mathf.Max(1, height)];
                var red = new Color32(255, 0, 0, 255);

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var baseIndex = (y * baseline.width) + x;
                        var actIndex = (y * actual.width) + x;
                        var b = basePixels[baseIndex];
                        var a = actPixels[actIndex];
                        var changed = Mathf.Abs(b.r - a.r) > tolerance
                            || Mathf.Abs(b.g - a.g) > tolerance
                            || Mathf.Abs(b.b - a.b) > tolerance
                            || Mathf.Abs(b.a - a.a) > tolerance;

                        var diffIndex = (y * width) + x;
                        if (changed)
                        {
                            changedPixels++;
                            diffPixels[diffIndex] = red;
                            minX = Mathf.Min(minX, x);
                            minY = Mathf.Min(minY, y);
                            maxX = Mathf.Max(maxX, x);
                            maxY = Mathf.Max(maxY, y);
                        }
                        else
                        {
                            // Dim unchanged pixels toward grayscale so the red diff pops.
                            var gray = (byte)(((a.r + a.g + a.b) / 3) * 0.5f);
                            diffPixels[diffIndex] = new Color32(gray, gray, gray, 255);
                        }
                    }
                }

                diff.SetPixels32(diffPixels);
                diff.Apply();

                var pixelDiffPercent = totalPixels > 0 ? 100.0 * changedPixels / totalPixels : 0.0;
                var hasChange = maxX >= 0;
                var changedBox = new
                {
                    minX = hasChange ? minX : 0,
                    minY = hasChange ? minY : 0,
                    maxX = hasChange ? maxX : 0,
                    maxY = hasChange ? maxY : 0,
                };

                var encoded = ScenePortImage.EncodeBase64(diff, maxEdge);
                var diffDirectory = ArtifactDirectory("golden", "diff");
                Directory.CreateDirectory(diffDirectory);
                var diffPath = Path.Combine(diffDirectory, "diff-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".png");
                var diffBytes = diff.EncodeToPNG();
                if (diffBytes != null && diffBytes.Length > 0)
                {
                    File.WriteAllBytes(diffPath, diffBytes);
                }

                var passed = dimensionsMatch && pixelDiffPercent <= passThreshold;
                return new
                {
                    status = "ok",
                    passed,
                    pixelDiffPercent,
                    changedPixels,
                    totalPixels,
                    changedBox,
                    baselineSize = new { width = baseline.width, height = baseline.height },
                    actualSize = new { width = actual.width, height = actual.height },
                    diffPath,
                    imageBase64 = encoded.Base64,
                    width = encoded.Width,
                    height = encoded.Height,
                    threshold,
                    passThreshold,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baseline);
                UnityEngine.Object.DestroyImmediate(actual);
                if (diff != null)
                {
                    UnityEngine.Object.DestroyImmediate(diff);
                }
            }
        }

        internal static object GoldenApprove(ScenePortRequest req, ScenePortContext ctx)
        {
            var actualPath = req.ExtractString("actualPath", req.GetString("actualPath", null));
            var baselinePath = req.ExtractString("baselinePath", req.GetString("baselinePath", null));
            if (string.IsNullOrEmpty(actualPath) || string.IsNullOrEmpty(baselinePath))
            {
                return new ErrorResponse("request.invalid", "actualPath and baselinePath are required.", "request", false);
            }
            if (!File.Exists(actualPath))
            {
                return new ErrorResponse("request.invalid", "actualPath does not exist.", "request", false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath));
            File.Copy(actualPath, baselinePath, true);
            return new { status = "ok", baselinePath };
        }

        internal static object ScenarioRun(ScenePortRequest req, ScenePortContext ctx)
        {
            var id = "scenario-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var directory = ArtifactDirectory("scenarios", id);
            Directory.CreateDirectory(directory);
            var reportPath = Path.Combine(directory, "report.json");
            var report = new
            {
                status = "ok",
                scenarioRunId = id,
                state = "preview",
                implemented = false,
                startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                note = "Scenario harness is a v1.0 preview — no steps were executed or asserted.",
            };
            File.WriteAllText(reportPath, ScenePortJson.Serialize(report));
            return new
            {
                status = "ok",
                scenarioRunId = id,
                state = "preview",
                implemented = false,
                note = "Scenario harness is a v1.0 preview — no steps were executed or asserted.",
                reportPath,
            };
        }

        internal static object ScenarioStatus(ScenePortRequest req, ScenePortContext ctx)
        {
            var runId = SafeId(req.ExtractString("scenarioRunId", req.GetString("scenarioRunId", "latest")));
            return new
            {
                status = "ok",
                scenarioRunId = runId,
                state = "preview",
                implemented = false,
                note = "Scenario harness is a v1.0 preview — no steps were executed or asserted.",
            };
        }

        internal static object ScenarioWait(ScenePortRequest req, ScenePortContext ctx)
        {
            return ScenarioStatus(req, ctx);
        }

        internal static object ScenarioReport(ScenePortRequest req, ScenePortContext ctx)
        {
            var runId = SafeId(req.ExtractString("scenarioRunId", req.GetString("scenarioRunId", "latest")));
            return new { status = "ok", scenarioRunId = runId, directory = ArtifactDirectory("scenarios", runId) };
        }

        internal static object Metrics(ScenePortRequest req, ScenePortContext ctx)
        {
            return BuildMetrics(null);
        }

        internal static object PerfProbe(ScenePortRequest req, ScenePortContext ctx)
        {
            var probeId = "perf-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var response = BuildMetrics(Path.Combine(ArtifactDirectory("perf", null), probeId + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(response.ArtifactPath));
            File.WriteAllText(response.ArtifactPath, ScenePortJson.Serialize(response));
            return response;
        }

        internal static object PerfCheckBudget(ScenePortRequest req, ScenePortContext ctx)
        {
            var metric = req.ExtractString("metric", req.GetString("metric", "totalAllocatedMemory"));
            var max = req.ExtractInt("max", req.GetInt("max", int.MaxValue));
            var metrics = BuildMetrics(null);

            // Only metrics we actually measured (the memory/counters in BuildMetrics) can be
            // evaluated. An unknown metric is reported as not-evaluated and never passed, so a
            // budget we cannot measure can never produce a false green.
            var evaluated = metrics.Metrics.ContainsKey(metric);
            if (!evaluated)
            {
                return new
                {
                    status = "ok",
                    metric,
                    max,
                    evaluated = false,
                    passed = false,
                    actual = (object)null,
                    note = "Metric '" + metric + "' is not measured by this bridge; budget was not evaluated.",
                    measuredMetrics = new List<string>(metrics.Metrics.Keys),
                };
            }

            var actual = Convert.ToInt64(metrics.Metrics[metric], CultureInfo.InvariantCulture);
            return new
            {
                status = "ok",
                metric,
                max,
                evaluated = true,
                passed = actual <= max,
                actual = (object)actual,
                measuredMetrics = new List<string>(metrics.Metrics.Keys),
            };
        }

        private static TestArtifactsResponse WriteTestArtifacts(TestRunSummaryDto run, ScenePortContext ctx)
        {
            var runId = string.IsNullOrEmpty(run.RunId) ? "not-run" : SafeId(run.RunId);
            var directory = ArtifactDirectory("test-runs", runId);
            Directory.CreateDirectory(directory);
            var summaryPath = Path.Combine(directory, "summary.json");
            var consolePath = Path.Combine(directory, "console.json");
            File.WriteAllText(summaryPath, ScenePortJson.Serialize(new TestRunResponse { Run = run }));
            File.WriteAllText(consolePath, ScenePortJson.Serialize(new ConsoleResponse { Logs = ctx.Console.Snapshot(200, "all") }));
            return new TestArtifactsResponse
            {
                RunId = runId,
                Directory = directory,
                SummaryPath = summaryPath,
                ConsolePath = consolePath,
            };
        }

        private static AssertionResultDto EvaluateAssertion(JObject check, ScenePortContext ctx)
        {
            var id = Value(check, "id", "assertion");
            var type = Value(check, "type", "health.status");
            switch (type)
            {
                case "health.status":
                    return new AssertionResultDto { Id = id, Passed = true, Message = "Bridge health handler is reachable.", Actual = "ok" };
                case "compilation.clean":
                    var clean = !EditorApplication.isCompiling && !EditorApplication.isUpdating && ctx.Console.ErrorSnapshot(1).Count == 0;
                    return new AssertionResultDto { Id = id, Passed = clean, Message = clean ? "Compilation is clean." : "Unity is busy or has recent errors.", Actual = clean };
                case "console.errorCount":
                    var max = IntValue(check, "max", 0);
                    var count = ctx.Console.ErrorSnapshot(500).Count;
                    return new AssertionResultDto { Id = id, Passed = count <= max, Message = "Console error count <= " + max + ".", Actual = count };
                case "scene.objectExists":
                    var path = Value(check, "path", null);
                    var exists = ScenePortObjects.FindByPath(path) != null;
                    return new AssertionResultDto { Id = id, Passed = exists, Message = "Scene object exists: " + path, Actual = exists };
                case "asset.exists":
                    var assetPath = Value(check, "path", null);
                    var assetExists = !string.IsNullOrEmpty(assetPath) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath));
                    return new AssertionResultDto { Id = id, Passed = assetExists, Message = "Asset exists: " + assetPath, Actual = assetExists };
                default:
                    return new AssertionResultDto { Id = id, Passed = false, Message = "Unknown assertion type: " + type };
            }
        }

        private static MetricsResponse BuildMetrics(string artifactPath)
        {
            var response = new MetricsResponse { ArtifactPath = artifactPath };
            response.Metrics["totalAllocatedMemory"] = Profiler.GetTotalAllocatedMemoryLong();
            response.Metrics["totalReservedMemory"] = Profiler.GetTotalReservedMemoryLong();
            response.Metrics["monoUsedSize"] = Profiler.GetMonoUsedSizeLong();
            response.Metrics["frameCount"] = Time.frameCount;
            response.Metrics["timeSinceStartup"] = EditorApplication.timeSinceStartup;
            return response;
        }

        private static string ArtifactDirectory(string kind, string id)
        {
            var path = Path.Combine(ScenePortPaths.ProjectPath(), "Temp", "ScenePort", kind);
            return string.IsNullOrEmpty(id) ? path : Path.Combine(path, SafeId(id));
        }

        private static string SafeId(string value)
        {
            return ScenePortPaths.SanitizeFileName(string.IsNullOrEmpty(value) ? "default" : value);
        }

        private static string Value(JObject obj, string key, string fallback)
        {
            var token = obj[key];
            return token == null || token.Type == JTokenType.Null ? fallback : token.ToString();
        }

        private static int IntValue(JObject obj, string key, int fallback)
        {
            var token = obj[key];
            return token == null ? fallback : token.Value<int>();
        }
    }
}
