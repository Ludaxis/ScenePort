using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Result of a request gate check. Null means "pass"; non-null short-circuits the
    /// request with the given HTTP status and body before any main-thread work happens.
    /// </summary>
    internal sealed class ScenePortGateResult
    {
        internal int StatusCode;
        internal string Body;

        internal ScenePortGateResult(int statusCode, string body)
        {
            StatusCode = statusCode;
            Body = body;
        }
    }

    /// <summary>
    /// Instantiable HttpListener wrapper. The bridge runs one on the bound port; tests run
    /// one on an ephemeral port. Requests are gated (auth/origin/host — wired by the bridge),
    /// then dispatched to the router on the Unity main thread via the supplied executor.
    /// </summary>
    internal sealed class ScenePortHttpServer
    {
        private const long MaxBodyBytes = 1024 * 1024;

        private readonly int port;
        private readonly ScenePortRouter router;
        private readonly Func<Func<string>, string> mainThreadExecutor;
        private readonly Func<HttpListenerRequest, ScenePortGateResult> gate;
        private readonly object stopGate = new object();

        private HttpListener listener;
        private Thread thread;
        private volatile bool running;

        internal ScenePortHttpServer(
            int port,
            ScenePortRouter router,
            Func<Func<string>, string> mainThreadExecutor,
            Func<HttpListenerRequest, ScenePortGateResult> gate = null)
        {
            this.port = port;
            this.router = router;
            this.mainThreadExecutor = mainThreadExecutor;
            this.gate = gate;
        }

        internal int Port => port;
        internal bool IsRunning => running;

        internal void Start()
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            running = true;

            thread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "ScenePort MCP Bridge",
            };
            thread.Start();
        }

        internal void Stop()
        {
            running = false;
            lock (stopGate)
            {
                try
                {
                    listener?.Close();
                }
                catch
                {
                    // Ignore shutdown races during domain reload.
                }

                listener = null;
            }
        }

        private void ListenLoop()
        {
            var local = listener;
            while (running && local != null && local.IsListening)
            {
                try
                {
                    var context = local.GetContext();
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

        private void Handle(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;

                // Gate runs before the body is read and before any main-thread work, so a
                // flood of rejected requests cannot stall the editor update pump.
                if (gate != null)
                {
                    var verdict = gate(request);
                    if (verdict != null)
                    {
                        Write(context, verdict.StatusCode, verdict.Body);
                        return;
                    }
                }

                if (request.ContentLength64 > MaxBodyBytes)
                {
                    Write(context, 413, ScenePortJson.Serialize(new ErrorResponse("Request body too large.")));
                    return;
                }

                string body = null;
                if (request.HasEntityBody)
                {
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                var path = request.Url.AbsolutePath;
                var query = request.Url.Query;
                var response = mainThreadExecutor(() => router.Dispatch(path, query, body));
                Write(context, 200, response);
            }
            catch (Exception ex)
            {
                Write(context, 500, ScenePortJson.Serialize(new ErrorResponse(ex.Message)));
            }
        }

        private static void Write(HttpListenerContext context, int status, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
    }
}
