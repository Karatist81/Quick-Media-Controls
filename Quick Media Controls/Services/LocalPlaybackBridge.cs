using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Quick_Media_Controls.Services
{
    /// <summary>
    /// Lightweight localhost HTTP bridge for web players (userscripts) that cannot
    /// expose repeat/shuffle through GSMTC. Commands: QMC -> /poll, state: script -> /state.
    /// Event-driven accept loop, no polling threads.
    /// </summary>
    public static class LocalPlaybackBridge
    {
        private const string Prefix = "http://127.0.0.1:53157/";
        private static readonly ConcurrentQueue<string> PendingCommands = new();
        private static HttpListener? _listener;

        /// <summary>Last repeat mode reported by a web userscript (0=off, 1=track, 2=list).</summary>
        public static int? ReportedRepeatMode { get; private set; }
        /// <summary>Last shuffle state reported by a web userscript.</summary>
        public static bool? ReportedShuffle { get; private set; }

        public static void Start()
        {
            if (_listener != null || !HttpListener.IsSupported) return;
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add(Prefix);
                listener.Start();
                _listener = listener;
                _ = AcceptLoopAsync(listener);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bridge start failed (port busy?): {ex.Message}");
                _listener = null;
            }
        }

        public static void Stop()
        {
            var listener = _listener;
            _listener = null;
            if (listener == null) return;
            try { listener.Stop(); listener.Close(); } catch { }
            while (PendingCommands.TryDequeue(out _)) { }
            ReportedRepeatMode = null;
            ReportedShuffle = null;
        }

        public static void EnqueueCommand(string command) => PendingCommands.Enqueue(command);

        private static async Task AcceptLoopAsync(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                HandleRequest(ctx);
            }
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            string body = string.Empty;
            try
            {
                var url = ctx.Request.Url;
                if (url == null) { ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }

                if (url.AbsolutePath == "/poll")
                {
                    var sb = new StringBuilder();
                    while (PendingCommands.TryDequeue(out var cmd)) sb.Append(cmd).Append('\n');
                    body = sb.ToString();
                }
                else if (url.AbsolutePath == "/state")
                {
                    foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = pair.Split('=', 2);
                        if (kv.Length != 2) continue;
                        switch (kv[0])
                        {
                            case "repeat":
                                if (int.TryParse(kv[1], out var r) && r >= 0 && r <= 2) ReportedRepeatMode = r;
                                break;
                            case "shuffle":
                                if (bool.TryParse(kv[1], out var s)) ReportedShuffle = s;
                                break;
                        }
                    }
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var buffer = Encoding.UTF8.GetBytes(body);
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                ctx.Response.ContentLength64 = buffer.Length;
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bridge request error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }
    }
}