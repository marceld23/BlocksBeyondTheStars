// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A tiny loopback HTTP server that serves the bundled browser content (the in-game <c>wiki/</c> and the
    /// arcade <c>minigames/</c>) out of <see cref="Application.streamingAssetsPath"/>. The embedded browser
    /// (UnityWebBrowser) has no <c>file://</c> support, and <c>file://</c> breaks <c>fetch()</c>/ES modules
    /// anyway, so each page needs a real <c>http://</c> origin — this provides it on <c>127.0.0.1</c>.
    ///
    /// It is read-only, bound to loopback, and refuses any path outside the served root (no traversal). One
    /// dynamic route, <c>/wiki/wiki-state.json</c>, returns live player state (discovered systems/worlds +
    /// language) via <see cref="WikiStateProvider"/> so the wiki can gate those chapters; the provider is
    /// expected to return a string built on the main thread (it is read from the listener thread).
    /// </summary>
    public sealed class LocalContentServer : IDisposable
    {
        private HttpListener _listener;
        private Thread _thread;
        private string _root;
        private volatile bool _running;

        /// <summary>Returns the JSON body for <c>/wiki/wiki-state.json</c> (discovered systems/worlds + lang).
        /// Built on the main thread and cached as an immutable string, so reading it here is race-free.</summary>
        public Func<string> WikiStateProvider;

        public int Port { get; private set; }
        public bool Running => _running;

        /// <summary>Base URL with trailing slash, e.g. <c>http://127.0.0.1:48213/</c>.</summary>
        public string BaseUrl => $"http://127.0.0.1:{Port}/";

        /// <summary>Absolute loopback URL for a path under StreamingAssets, e.g. <c>Url("wiki/index.html")</c>.</summary>
        public string Url(string relativePath) => BaseUrl + relativePath.TrimStart('/');

        /// <summary>Starts serving StreamingAssets on a free loopback port. Safe to call once; no-op if running.</summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }

            _root = Path.GetFullPath(Application.streamingAssetsPath);
            Port = FindFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start(); // explicit loopback prefix needs no URL ACL / elevation on Windows
            _running = true;   // only after a successful Start() (a throw leaves Running=false)
            _thread = new Thread(Loop) { IsBackground = true, Name = "BBS-ContentServer" };
            _thread.Start();
            Debug.Log($"[LocalContentServer] serving {_root} at {BaseUrl}");
        }

        private static int FindFreePort()
        {
            // Bind to port 0 to let the OS hand us a free loopback port, then release it for the listener.
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { break; } // listener stopped/disposed
                try { Handle(ctx); }
                catch (Exception e) { Debug.LogWarning($"[LocalContentServer] {e.Message}"); }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string rel = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath).TrimStart('/');
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            ctx.Response.Headers["Cache-Control"] = "no-store";

            // Dynamic route: live wiki state (discovered systems/worlds + language).
            if (rel == "wiki/wiki-state.json")
            {
                WriteText(ctx, WikiStateProvider != null ? (WikiStateProvider() ?? "{}") : "{}", "application/json");
                return;
            }

            if (rel.Length == 0)
            {
                rel = "index.html";
            }
            else if (rel.EndsWith("/"))
            {
                rel += "index.html";
            }

            // Resolve + confine to the served root (reject any traversal outside StreamingAssets).
            string full = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                ctx.Response.StatusCode = 404;
                WriteText(ctx, "Not found", "text/plain");
                return;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch { ctx.Response.StatusCode = 500; WriteText(ctx, "Read error", "text/plain"); return; }

            ctx.Response.ContentType = MimeOf(full);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static void WriteText(HttpListenerContext ctx, string text, string mime)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentType = mime + "; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private static string MimeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html";
                case ".js": case ".mjs": return "text/javascript";
                case ".css": return "text/css";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".gif": return "image/gif";
                case ".ico": return "image/x-icon";
                case ".wav": return "audio/wav";
                case ".ogg": return "audio/ogg";
                case ".mp3": return "audio/mpeg";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            try { if (_thread != null && _thread.IsAlive) _thread.Join(500); } catch { }
            _listener = null;
            _thread = null;
        }
    }
}
