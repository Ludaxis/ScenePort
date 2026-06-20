using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScenePort.McpBridge.Editor
{
    internal static class EditorStateHandlers
    {
        internal static object Health(ScenePortRequest req, ScenePortContext ctx)
        {
            var scene = SceneManager.GetActiveScene();
            return new HealthResponse
            {
                Version = ctx.Version,
                UnityVersion = Application.unityVersion,
                ProjectPath = ScenePortPaths.ProjectPath(),
                ActiveScene = scene.name,
                IsPlaying = EditorApplication.isPlaying,
                IsCompiling = EditorApplication.isCompiling,
                IsUpdating = EditorApplication.isUpdating,
                Port = ctx.BoundPort,
                ProjectId = PlayerSettings.productGUID.ToString("N"),
                ProjectName = Application.productName,
                TokenRequired = ctx.TokenRequired,
                ProtocolVersion = ctx.ProtocolVersion,
                CapabilitiesHash = ctx.CapabilitiesHash,
                OwnerLeaseId = ctx.OwnerLeaseId,
                HeartbeatUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                StartedUtc = ctx.StartedUtc,
                EditorRole = ctx.EditorRole,
                ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                ProcessName = ctx.ProcessName,
            };
        }

        internal static object Capabilities(ScenePortRequest req, ScenePortContext ctx)
        {
            return new CapabilitiesResponse
            {
                ProtocolVersion = ctx.ProtocolVersion,
                BridgeVersion = ctx.Version,
                CapabilitiesHash = ctx.CapabilitiesHash,
                EndpointGroups = ScenePortProtocol.EndpointGroups,
                Policy = ScenePortPolicy.BuildDto(ctx.PolicyProfile),
            };
        }

        internal static object Diagnostics(ScenePortRequest req, ScenePortContext ctx)
        {
            var health = (HealthResponse)Health(req, ctx);
            var capabilities = (CapabilitiesResponse)Capabilities(req, ctx);
            var response = new DiagnosticsResponse
            {
                Health = health,
                Capabilities = capabilities,
                Policy = capabilities.Policy,
                AuditPath = ctx.Audit?.Path,
                RecentAudit = ctx.Audit?.Snapshot(25) ?? new System.Collections.Generic.List<AuditLogEntryDto>(),
            };

            if (!ctx.TokenRequired)
            {
                response.Warnings.Add("Auth token requirement is disabled.");
            }
            if (ctx.PolicyProfile == "full-safe-local")
            {
                response.Warnings.Add("Policy profile allows safe writes and authoring tools.");
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                response.Warnings.Add("Unity is compiling or updating assets.");
            }

            return response;
        }

        internal static object AuthRotate(ScenePortRequest req, ScenePortContext ctx)
        {
            ctx.Token = ScenePortAuth.GenerateToken();
            ctx.TokenRequired = true;
            ctx.TokenStorage = "library";
            ctx.TokenFingerprint = ScenePortAuth.Fingerprint(ctx.Token);
            var projectPath = ScenePortPaths.ProjectPath();
            ScenePortDiscoveryFile.Write(projectPath, new ScenePortDiscoveryFile.BridgeInfo
            {
                bridgeVersion = ctx.Version,
                protocolVersion = ctx.ProtocolVersion,
                capabilitiesHash = ctx.CapabilitiesHash,
                url = "http://127.0.0.1:" + ctx.BoundPort,
                port = ctx.BoundPort,
                token = ctx.Token,
                projectPath = projectPath,
                projectId = PlayerSettings.productGUID.ToString("N"),
                projectName = Application.productName,
                unityVersion = Application.unityVersion,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                processName = ctx.ProcessName,
                startedUtc = ctx.StartedUtc,
                heartbeatUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                expiresUtc = DateTime.UtcNow.AddSeconds(8).ToString("o", CultureInfo.InvariantCulture),
                ownerLeaseId = ctx.OwnerLeaseId,
                editorRole = ctx.EditorRole,
                policyProfile = ctx.PolicyProfile,
                tokenStorage = ctx.TokenStorage,
                tokenRef = "Library/ScenePort/bridge.json",
                tokenFingerprint = ctx.TokenFingerprint,
            });

            return new { status = "ok", tokenStorage = ctx.TokenStorage, tokenFingerprint = ctx.TokenFingerprint };
        }

        internal static object Console(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = req.GetInt("limit", 100);
            var type = req.GetString("type", "all");
            return new ConsoleResponse { Logs = ctx.Console.Snapshot(limit, type) };
        }

        internal static object CompilationStatus(ScenePortRequest req, ScenePortContext ctx)
        {
            var response = new CompilationStatusResponse
            {
                IsCompiling = EditorApplication.isCompiling,
                IsUpdating = EditorApplication.isUpdating,
                IsPlaying = EditorApplication.isPlaying,
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                TimeSinceStartup = EditorApplication.timeSinceStartup,
                ReloadEpoch = ScenePortCompilation.ReloadEpoch,
                RecentErrors = ctx.Console.ErrorSnapshot(50),
            };

            var records = ScenePortCompilation.CompilerMessages;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                response.CompilerMessages.Add(new CompilerMessageDto
                {
                    File = record.File,
                    Line = record.Line,
                    Column = record.Column,
                    Type = record.Type,
                    Message = record.Message,
                    Assembly = record.Assembly,
                });
            }

            return response;
        }

        internal static object AuditLog(ScenePortRequest req, ScenePortContext ctx)
        {
            var limit = Mathf.Clamp(req.GetInt("limit", 100), 1, 500);
            return new AuditLogResponse
            {
                Path = ctx.Audit?.Path,
                Entries = ctx.Audit?.Snapshot(limit) ?? new System.Collections.Generic.List<AuditLogEntryDto>(),
            };
        }

        internal static object PlayMode(ScenePortRequest req, ScenePortContext ctx)
        {
            var action = req.ExtractString("action", req.GetString("action", "status")).ToLowerInvariant();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return new ErrorResponse(
                    EditorApplication.isCompiling ? "editor.busy.compiling" : "editor.busy.updating",
                    "Unity is compiling or updating assets. Play mode changes are blocked.",
                    "editor",
                    true,
                    1000,
                    "Wait for Unity compilation or asset refresh to finish, then retry.");
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

            return new PlayModeResponse
            {
                Action = action,
                IsPlaying = EditorApplication.isPlaying,
                IsPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
            };
        }

        internal static object CaptureGameView(ScenePortRequest req, ScenePortContext ctx)
        {
            var superSize = Mathf.Clamp(req.ExtractInt("superSize", req.GetInt("superSize", 1)), 1, 4);
            var fileName = req.ExtractString("fileName", req.GetString("fileName", null));
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "game-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            }

            var inline = req.ExtractBool("inline", req.GetBool("inline", true));
            var maxEdge = Mathf.Clamp(req.ExtractInt("maxEdge", req.GetInt("maxEdge", 1024)), 64, 4096);
            return CaptureGameViewFile(fileName, superSize, inline, maxEdge);
        }

        internal static CaptureGameViewResponse CaptureGameViewFile(string fileName, int superSize)
        {
            return CaptureGameViewFile(fileName, superSize, false, 1024);
        }

        internal static CaptureGameViewResponse CaptureGameViewFile(string fileName, int superSize, bool inline, int maxEdge)
        {
            superSize = Mathf.Clamp(superSize, 1, 4);
            fileName = ScenePortPaths.SanitizeFileName(fileName);
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".png";
            }

            var directory = Path.Combine(ScenePortPaths.ProjectPath(), "Temp", "ScenePort");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);

            // Outside play mode, ScreenCapture.CaptureScreenshotAsTexture throws and
            // CaptureScreenshot does not reliably render the Game View. Render the game
            // camera into a RenderTexture instead so capture works in edit mode too.
            if (!EditorApplication.isPlaying)
            {
                return CaptureGameCameraInEditMode(path, superSize, inline, maxEdge);
            }

            // Play mode: ScreenCapture captures the real Game View composition. It writes
            // asynchronously, so for inline bytes we additionally grab a synchronous texture.
            ScreenCapture.CaptureScreenshot(path, superSize);

            var response = new CaptureGameViewResponse
            {
                Path = path,
                SuperSize = superSize,
                Note = "Unity writes screenshots asynchronously; the file may appear after a short delay.",
            };

            if (inline)
            {
                var texture = ScreenCapture.CaptureScreenshotAsTexture(superSize);
                try
                {
                    var encoded = ScenePortImage.EncodeBase64(texture, maxEdge);
                    if (!string.IsNullOrEmpty(encoded.Base64))
                    {
                        response.ImageBase64 = encoded.Base64;
                        response.Width = encoded.Width;
                        response.Height = encoded.Height;
                    }
                }
                finally
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }

            return response;
        }

        private static CaptureGameViewResponse CaptureGameCameraInEditMode(string path, int superSize, bool inline, int maxEdge)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameras = Camera.allCameras;
                if (cameras != null && cameras.Length > 0)
                {
                    camera = cameras[0];
                }
            }

            if (camera == null)
            {
                return new CaptureGameViewResponse
                {
                    Path = path,
                    SuperSize = superSize,
                    Note = "No active camera found to capture the Game View in edit mode.",
                };
            }

            var width = Mathf.Clamp((camera.pixelWidth > 0 ? camera.pixelWidth : 1024) * superSize, 64, 4096);
            var height = Mathf.Clamp((camera.pixelHeight > 0 ? camera.pixelHeight : 768) * superSize, 64, 4096);

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var response = new CaptureGameViewResponse
            {
                Path = path,
                SuperSize = superSize,
                Note = "Captured game camera in edit mode (full Game View composition requires play mode).",
            };

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());

                if (inline)
                {
                    var encoded = ScenePortImage.EncodeBase64(texture, maxEdge);
                    if (!string.IsNullOrEmpty(encoded.Base64))
                    {
                        response.ImageBase64 = encoded.Base64;
                        response.Width = encoded.Width;
                        response.Height = encoded.Height;
                    }
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            return response;
        }
    }
}
