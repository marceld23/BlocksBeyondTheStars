// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The server crash uploader POSTs raw report JSON with the spam-gate header to a real local endpoint
/// (an <see cref="HttpListener"/> standing in for the website function), reports accept/reject truthfully, and
/// never throws — mirroring the player-feedback uploader but for the automatic server pipeline.</summary>
public sealed class CrashReportUploaderTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly Thread _serverThread;
    private volatile bool _running = true;

    private string _lastBody = string.Empty;
    private string _lastApiKey = string.Empty;
    private string _lastMethod = string.Empty;
    private const string ExpectedKey = "crash-key";

    public CrashReportUploaderTests()
    {
        int port = GetFreePort();
        _prefix = $"http://127.0.0.1:{port}/_functions/bugreport/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _serverThread = new Thread(ServeLoop) { IsBackground = true };
        _serverThread.Start();
    }

    private string Endpoint => _prefix.TrimEnd('/');

    private void ServeLoop()
    {
        while (_running)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { return; }

            try
            {
                var req = ctx.Request;
                _lastMethod = req.HttpMethod;
                _lastApiKey = req.Headers[CrashReportUploader.ApiKeyHeader] ?? string.Empty;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    _lastBody = reader.ReadToEnd();
                }

                int status = _lastApiKey == ExpectedKey ? 201 : 403;
                byte[] buf = Encoding.UTF8.GetBytes("{\"ok\":true}");
                ctx.Response.StatusCode = status;
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                try { ctx.Response.Abort(); } catch { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Send_PostsRawJsonWithKey_AndReportsAccepted()
    {
        var uploader = new CrashReportUploader(Endpoint, ExpectedKey);
        Assert.True(uploader.IsConfigured);

        bool ok = uploader.Send("{\"kind\":\"tick-fault\",\"system\":\"TickCreatures\"}");

        Assert.True(ok);
        Assert.Equal("POST", _lastMethod);
        Assert.Equal(ExpectedKey, _lastApiKey);
        Assert.Contains("TickCreatures", _lastBody);
    }

    [Fact]
    public void Send_WrongKey_ReportsRejected()
    {
        var uploader = new CrashReportUploader(Endpoint, "wrong-key");
        Assert.False(uploader.Send("{\"kind\":\"unhandled-exception\"}"));
    }

    [Fact]
    public void Send_NotConfigured_DoesNotSend()
    {
        Assert.False(new CrashReportUploader(Endpoint, "").IsConfigured);
        Assert.False(new CrashReportUploader("", ExpectedKey).IsConfigured);
        Assert.False(new CrashReportUploader("", "").Send("{}"));
    }

    [Fact]
    public void Send_UnreachableEndpoint_FailsWithoutThrowing()
    {
        var uploader = new CrashReportUploader($"http://127.0.0.1:{GetFreePort()}/_functions/bugreport", ExpectedKey);
        Assert.False(uploader.Send("{\"kind\":\"tick-fault\"}"));
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
    }
}
