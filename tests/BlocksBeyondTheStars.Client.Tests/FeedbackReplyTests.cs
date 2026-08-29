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
using BlocksBeyondTheStars.Shared.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The pull side of the feedback channel (#1328): the reply-key derivation the client and the inbox
/// share, the local "which reports did I send" memory that gates polling, and the reply client against
/// a REAL local HTTP endpoint (an <see cref="HttpListener"/> standing in for the ReportHost) — both
/// directions: the right URL/headers/bodies go out, and every server answer (threads, empty, rejected,
/// unreachable) comes back as a result instead of an exception.
/// </summary>
public sealed class FeedbackReplyTests : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _prefix;
    private readonly Thread _serverThread;
    private readonly string _tempDir;
    private volatile bool _running = true;

    // Captured from the most recent request.
    private string _lastMethod = string.Empty;
    private string _lastPathAndQuery = string.Empty;
    private string _lastApiKey = string.Empty;
    private string _lastBody = string.Empty;

    // Server behaviour, settable per test.
    private int _status = 200;
    private string _responseJson = "{\"items\":[]}";

    public FeedbackReplyTests()
    {
        int port = GetFreePort();
        _prefix = $"http://127.0.0.1:{port}/api/replies/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _serverThread = new Thread(ServeLoop) { IsBackground = true };
        _serverThread.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "bbts_replies_" + Guid.NewGuid().ToString("N"));
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
                _lastPathAndQuery = req.Url?.PathAndQuery ?? string.Empty;
                _lastApiKey = req.Headers[FeedbackUploader.ApiKeyHeader] ?? string.Empty;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                {
                    _lastBody = reader.ReadToEnd();
                }

                byte[] buf = Encoding.UTF8.GetBytes(_responseJson);
                ctx.Response.StatusCode = _status;
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

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // ---------------- Key derivation (shared with the inbox) ----------------

    [Fact]
    public void ReplyKey_IsStableOneWayLowercaseHex_AndEmptyForEmptySecret()
    {
        string a = FeedbackReplyKey.Derive("token-abc");
        Assert.Equal(FeedbackReplyKey.Length, a.Length);
        Assert.True(FeedbackReplyKey.IsWellFormed(a));
        Assert.Equal(a, FeedbackReplyKey.Derive("token-abc"));
        Assert.NotEqual(a, FeedbackReplyKey.Derive("token-abd"));
        Assert.DoesNotContain("token-abc", a);
        Assert.Equal("", FeedbackReplyKey.Derive(""));
        Assert.Equal("", FeedbackReplyKey.Derive(null));

        Assert.False(FeedbackReplyKey.IsWellFormed(a.ToUpperInvariant()));
        Assert.False(FeedbackReplyKey.IsWellFormed(a.Substring(1)));
        Assert.False(FeedbackReplyKey.IsWellFormed(""));
        Assert.False(FeedbackReplyKey.IsWellFormed(null));
    }

    [Fact]
    public void Report_SerializesReplyKey()
    {
        var report = new FeedbackReport { Description = "x", ReplyKey = FeedbackReplyKey.Derive("s") };
        string json = FeedbackUploader.Serialize(report, null);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(report.ReplyKey, doc.RootElement.GetProperty("replyKey").GetString());
    }

    // ---------------- Sent-reports memory (the poll gate) ----------------

    [Fact]
    public void SentReportsLog_RecordsPrunesAndGatesPolling()
    {
        string path = Path.Combine(_tempDir, "feedback", "sent.json");
        var log = new SentReportsLog(path);
        const long now = 1_800_000_000;

        Assert.False(log.ShouldPoll(now));                       // fresh install: never polls
        Assert.False(log.Record("", "no id", now));
        Assert.True(log.Record("r1", "Hat eaten by door", now));
        Assert.True(File.Exists(path));
        Assert.True(log.ShouldPoll(now));

        // Survives a reload; entries older than MaxAgeDays are forgotten on the way in.
        var reloaded = new SentReportsLog(path);
        Assert.Single(reloaded.List(now));
        Assert.Equal("Hat eaten by door", reloaded.List(now)[0].Title);
        Assert.False(reloaded.ShouldPoll(now + (SentReportsLog.MaxAgeDays + 1) * 86400L));

        // Re-recording the same id replaces it; Forget drops it; the cap keeps the newest.
        Assert.True(reloaded.Record("r1", "renamed", now + 1));
        Assert.Single(reloaded.List(now + 1));
        reloaded.Forget("r1");
        Assert.False(reloaded.ShouldPoll(now + 1));
        for (int i = 0; i < SentReportsLog.MaxEntries + 5; i++)
        {
            reloaded.Record("id" + i, "t", now + i);
        }

        Assert.Equal(SentReportsLog.MaxEntries, reloaded.List(now + 100).Count);
        Assert.Equal("id" + (SentReportsLog.MaxEntries + 4), reloaded.List(now + 100)[^1].Id);

        // A corrupt file is "no memory", not a crash; an empty path disables the log.
        File.WriteAllText(path, "{not json");
        Assert.False(new SentReportsLog(path).ShouldPoll(now));
        Assert.False(new SentReportsLog("").Record("x", "y", now));
    }

    // ---------------- Reply client against the local endpoint ----------------

    [Fact]
    public void EndpointFor_DerivesRepliesRouteFromIngestRoute()
    {
        Assert.Equal("https://reports.example.com/api/replies", FeedbackReplyClient.EndpointFor("https://reports.example.com/api/bugreport"));
        Assert.Equal(FeedbackReplyClient.DefaultEndpoint, FeedbackReplyClient.EndpointFor(FeedbackUploader.DefaultEndpoint));
        Assert.Equal(FeedbackReplyClient.DefaultEndpoint, FeedbackReplyClient.EndpointFor("https://weird.example.com/other"));
        Assert.Equal(FeedbackReplyClient.DefaultEndpoint, FeedbackReplyClient.EndpointFor(""));
    }

    [Fact]
    public void Fetch_SendsKeyHeaderAndQuery_AndParsesThreads()
    {
        string key = FeedbackReplyKey.Derive("token-abc");
        _responseJson = """
            {"items":[{"reportId":"r1","title":"Hat eaten by door","status":"waiting_for_player","fixedInVersion":"","createdUnix":100,
              "replies":[{"id":7,"author":"dev","text":"Thanks!","isQuestion":false,"createdUnix":110,"seen":true},
                         {"id":8,"author":"dev","text":"Without the helmet too?","isQuestion":true,"createdUnix":120,"seen":false}],
              "unseenIds":[8]},
             {"reportId":"r2","title":"Done one","status":"done","fixedInVersion":"2026.8.23","createdUnix":200,
              "replies":[{"id":9,"author":"dev","text":"Shipped.","isQuestion":false,"createdUnix":210,"seen":false}],"unseenIds":[9]},
             {"title":"no id — skipped"}]}
            """;
        var client = new FeedbackReplyClient(Endpoint, "test-key");

        var result = client.Fetch(key, sinceUnix: 42);

        Assert.True(result.Ok);
        Assert.Equal("GET", _lastMethod);
        Assert.Equal("test-key", _lastApiKey);
        Assert.Contains("key=" + key, _lastPathAndQuery);
        Assert.Contains("since=42", _lastPathAndQuery);
        Assert.Equal(2, result.Threads.Count);

        var first = result.Threads[0];
        Assert.Equal("r1", first.ReportId);
        Assert.Equal(2, first.Replies.Count);
        Assert.True(first.Replies[1].IsQuestion);
        Assert.True(first.AwaitsAnswer);
        Assert.Equal(new List<long> { 8 }, first.UnseenIds);

        var second = result.Threads[1];
        Assert.False(second.AwaitsAnswer);
        Assert.Equal("2026.8.23", second.FixedInVersion);
    }

    [Fact]
    public void Ack_AndAnswer_PostTheExpectedBodies()
    {
        string key = FeedbackReplyKey.Derive("token-abc");
        var client = new FeedbackReplyClient(Endpoint, "test-key");
        _responseJson = "{\"ok\":true,\"acked\":2}";

        var ack = client.Ack(key, new long[] { 8, 9 });
        Assert.True(ack.Ok);
        Assert.Equal("POST", _lastMethod);
        Assert.EndsWith("/api/replies/ack", _lastPathAndQuery);
        using (var doc = JsonDocument.Parse(_lastBody))
        {
            Assert.Equal(key, doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("replyIds").GetArrayLength());
        }

        _status = 201;
        _responseJson = "{\"ok\":true,\"replyId\":10}";
        var answer = client.Answer(key, "r1", "  Yes, also without it.  ");
        Assert.True(answer.Ok);
        Assert.Equal(201, answer.StatusCode);
        Assert.EndsWith("/api/replies", _lastPathAndQuery);
        using (var doc = JsonDocument.Parse(_lastBody))
        {
            Assert.Equal("r1", doc.RootElement.GetProperty("reportId").GetString());
            Assert.Equal("Yes, also without it.", doc.RootElement.GetProperty("text").GetString());
        }

        Assert.Equal("empty_text", client.Answer(key, "r1", "   ").Error);
    }

    [Fact]
    public void RejectedOrUnreachable_ComeBackAsResults_NotExceptions()
    {
        string key = FeedbackReplyKey.Derive("token-abc");
        var client = new FeedbackReplyClient(Endpoint, "wrong-key");

        _status = 403;
        _responseJson = "{\"error\":\"forbidden\"}";
        var denied = client.Fetch(key);
        Assert.False(denied.Ok);
        Assert.Equal(403, denied.StatusCode);
        Assert.Equal("http_403", denied.Error);
        Assert.Empty(denied.Threads);

        _status = 409;
        Assert.Equal("http_409", client.Answer(key, "r1", "text").Error);

        // Unconfigured / no key: nothing is sent at all.
        Assert.Equal("not_configured", new FeedbackReplyClient(Endpoint, "").Fetch(key).Error);
        Assert.Equal("no_key", client.Fetch("").Error);

        // Nobody listening on that port → transport error, still a result.
        var offline = new FeedbackReplyClient($"http://127.0.0.1:{GetFreePort()}/api/replies", "test-key",
            new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) });
        var dead = offline.Fetch(key);
        Assert.False(dead.Ok);
        Assert.Equal(0, dead.StatusCode);
        Assert.NotEqual("", dead.Error);
    }

    [Fact]
    public void ParseThreads_ToleratesGarbage()
    {
        Assert.Empty(FeedbackReplyClient.ParseThreads(null));
        Assert.Empty(FeedbackReplyClient.ParseThreads("not json"));
        Assert.Empty(FeedbackReplyClient.ParseThreads("[1,2]"));
        Assert.Empty(FeedbackReplyClient.ParseThreads("{\"items\":\"nope\"}"));
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); _listener.Close(); } catch { /* best effort */ }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }
}
