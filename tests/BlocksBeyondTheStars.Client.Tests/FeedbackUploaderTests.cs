// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using BlocksBeyondTheStars.Client.Feedback;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Verifies the player-feedback uploader end to end against a REAL local HTTP endpoint (a
/// <see cref="HttpListener"/> standing in for the Wix/Velo function) — the "simulierte lokale
/// Schnittstelle" from the task. We assert both directions: the client sends the right JSON +
/// API-key header, and it correctly reports back what the server answered (accepted / rejected /
/// unreachable), all without ever throwing into the game.
/// </summary>
public sealed class FeedbackUploaderTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly Thread _serverThread;
    private volatile bool _running = true;

    // Captured from the most recent received request, for assertions.
    private string _lastBody = string.Empty;
    private string _lastApiKey = string.Empty;
    private string _lastMethod = string.Empty;
    private string _lastContentType = string.Empty;

    // Server behaviour, settable per-test before the request arrives.
    private string _expectedKey = "test-key";

    public FeedbackUploaderTests()
    {
        // Bind an ephemeral loopback port. HttpListener needs a trailing slash on the prefix.
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
            try
            {
                ctx = _listener.GetContext();
            }
            catch
            {
                return; // listener stopped
            }

            try
            {
                var req = ctx.Request;
                _lastMethod = req.HttpMethod;
                _lastApiKey = req.Headers[FeedbackUploader.ApiKeyHeader] ?? string.Empty;
                _lastContentType = req.ContentType ?? string.Empty;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    _lastBody = reader.ReadToEnd();
                }

                int status;
                string responseJson;
                if (_lastApiKey != _expectedKey)
                {
                    status = 403;
                    responseJson = "{\"error\":\"Invalid API key\"}";
                }
                else if (string.IsNullOrWhiteSpace(GetJsonString(_lastBody, "description")))
                {
                    status = 400;
                    responseJson = "{\"error\":\"Missing description\"}";
                }
                else
                {
                    status = 201;
                    responseJson = "{\"ok\":true,\"bugReportId\":\"abc123\"}";
                }

                byte[] buf = Encoding.UTF8.GetBytes(responseJson);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
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

    private static FeedbackReport SampleReport(string description = "The station dock crashed the game.") => new FeedbackReport
    {
        Title = "Dock crash",
        Description = description,
        Email = "pilot@example.com",
        GameVersion = "0.1.0-alpha",
        BuildNumber = "42",
        PlayerId = "anon-install-id",
        PlayerName = "Pilot",
        SessionId = "session-1",
        Platform = "WindowsPlayer",
        ClientTimestamp = "2026-06-20T12:34:56Z",
        ReportJson = new Dictionary<string, object> { ["location"] = "Sol · Terra", ["health"] = 80 },
    };

    [Fact]
    public void Upload_ValidReport_IsAcceptedAndReturnsId()
    {
        var uploader = new FeedbackUploader(Endpoint, "test-key");
        byte[] shot = Encoding.UTF8.GetBytes("fake-jpg-bytes");

        var result = uploader.Upload(SampleReport(), shot);

        Assert.True(result.Ok, $"expected success, got {result.StatusCode} {result.Error}");
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("abc123", result.ReportId);

        // The server saw the right header, method and content type.
        Assert.Equal("test-key", _lastApiKey);
        Assert.Equal("POST", _lastMethod);
        Assert.Contains("application/json", _lastContentType);
    }

    [Fact]
    public void Upload_SendsCamelCaseJson_WithScreenshotRoundtrip()
    {
        var uploader = new FeedbackUploader(Endpoint, "test-key");
        byte[] shot = Encoding.UTF8.GetBytes("hello-screenshot");

        var result = uploader.Upload(SampleReport(), shot);
        Assert.True(result.Ok);

        using var doc = JsonDocument.Parse(_lastBody);
        var root = doc.RootElement;
        // camelCase field names match what the Wix backend expects.
        Assert.Equal("Dock crash", root.GetProperty("title").GetString());
        Assert.Equal("The station dock crashed the game.", root.GetProperty("description").GetString());
        Assert.Equal("pilot@example.com", root.GetProperty("email").GetString());
        Assert.Equal("0.1.0-alpha", root.GetProperty("gameVersion").GetString());
        Assert.Equal("Sol · Terra", root.GetProperty("reportJson").GetProperty("location").GetString());

        // Screenshot survived the base64 round-trip.
        var screenshot = root.GetProperty("screenshot");
        Assert.Equal("image/jpeg", screenshot.GetProperty("mimeType").GetString());
        byte[] decoded = Convert.FromBase64String(screenshot.GetProperty("base64").GetString()!);
        Assert.Equal("hello-screenshot", Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void Upload_WrongApiKey_IsRejectedGracefully()
    {
        var uploader = new FeedbackUploader(Endpoint, "wrong-key");

        var result = uploader.Upload(SampleReport(), null);

        Assert.False(result.Ok);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("http_403", result.Error);
    }

    [Fact]
    public void Upload_EmptyDescription_DoesNotSend()
    {
        var uploader = new FeedbackUploader(Endpoint, "test-key");

        var result = uploader.Upload(SampleReport(description: "   "), null);

        Assert.False(result.Ok);
        Assert.Equal("empty_description", result.Error);
        Assert.Equal(0, result.StatusCode); // never hit the network
    }

    [Fact]
    public void Upload_NoApiKey_IsNotConfigured()
    {
        var uploader = new FeedbackUploader(Endpoint, "");
        Assert.False(uploader.IsConfigured);

        var result = uploader.Upload(SampleReport(), null);

        Assert.False(result.Ok);
        Assert.Equal("not_configured", result.Error);
    }

    [Fact]
    public void Upload_OversizeScreenshot_IsDroppedButTextStillSends()
    {
        var uploader = new FeedbackUploader(Endpoint, "test-key");
        // Big enough that its base64 exceeds the cap → image dropped, text report still delivered.
        byte[] huge = new byte[FeedbackUploader.MaxScreenshotBase64Length]; // base64 is ~33% larger than this

        var result = uploader.Upload(SampleReport(), huge);

        Assert.True(result.Ok);
        using var doc = JsonDocument.Parse(_lastBody);
        Assert.False(doc.RootElement.TryGetProperty("screenshot", out _),
            "oversize screenshot should be omitted, not sent");
    }

    [Fact]
    public void Upload_UnreachableEndpoint_FailsWithoutThrowing()
    {
        // Point at a port nothing is listening on.
        var uploader = new FeedbackUploader($"http://127.0.0.1:{GetFreePort()}/_functions/bugreport", "test-key");

        var result = uploader.Upload(SampleReport(), null);

        Assert.False(result.Ok);
        Assert.Equal(0, result.StatusCode);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    private static string GetJsonString(string body, string prop)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
