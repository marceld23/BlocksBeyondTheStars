// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The /bump debug command writes a persisted diagnostic snapshot (no comm radio needed), and the
/// screenshot variant additionally drops a JPG alongside it.</summary>
public sealed class BumpTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    // The server may route bumps to a shared <repo>/bugreports/server folder (when tests run inside the
    // working tree), so each test uses a unique world name and only ever touches its own files.
    private readonly string _world = "bumpworld_" + Guid.NewGuid().ToString("N");
    private string _bumpDir = null!;
    private SqliteWorldRepository _repo = null!;
    private LoopbackServerTransport _st = null!;
    private LoopbackClientTransport _client = null!;

    public BumpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bump_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void BumpCommand_WritesPersistedSnapshot()
    {
        var (server, client, paths) = StartWorld();

        Assert.Equal(0, server.BumpsWritten);

        // /bump works without a comm radio (it's intercepted before the chat gate).
        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump the ship interior glitches with flora" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);

        var files = MyBumpFiles(paths, "*.json");
        Assert.Single(files);

        string json = File.ReadAllText(files[0]);
        Assert.Contains("the ship interior glitches with flora", json); // description captured
        Assert.Contains("environment", json);                            // env snapshot present
        Assert.Contains("inventory", json);                              // player items captured
        Assert.Contains("surroundingsCensus", json);                     // wider block/flora census present
        Assert.Contains("\"inSpace\": false", json);                     // on-surface context flag
    }

    [Fact]
    public void BumpReport_WithScreenshot_WritesJpgAlongsideSnapshot()
    {
        var (server, client, paths) = StartWorld();

        var image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 }; // stand-in JPG bytes
        client.Send(NetCodec.Encode(new BumpReport { Description = "ufo wreck cannot be opened", Image = image }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);

        var jsonFiles = MyBumpFiles(paths, "*.json");
        var jpgFiles = MyBumpFiles(paths, "*.jpg");
        Assert.Single(jsonFiles);
        Assert.Single(jpgFiles);

        Assert.Equal(image, File.ReadAllBytes(jpgFiles[0]));

        string json = File.ReadAllText(jsonFiles[0]);
        Assert.Contains("ufo wreck cannot be opened", json);
        // The json references its screenshot file (same stem) so a dev can pair them.
        Assert.Contains(Path.GetFileName(jpgFiles[0]), json);
    }

    [Fact]
    public void BumpReport_WithConfiguredSink_ForwardsSnapshotWithScreenshot()
    {
        var (server, client, _) = StartWorld();
        var sink = new ForwardSink();
        server.CrashUploader = sink;

        var image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7, 6, 5 };
        client.Send(NetCodec.Encode(new BumpReport { Description = "[feedback] jetpack stuck in ceiling", Image = image, ClientVersion = "0.8.3" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // The send runs on a background task — wait for the sink, not a fixed sleep.
        Assert.True(sink.Sent.Wait(TimeSpan.FromSeconds(10)), "bump was not forwarded to the sink");

        string json = sink.LastJson!;
        Assert.Contains("[feedback] jetpack stuck in ceiling", json);       // description up front
        Assert.Contains("\"platform\":\"server\"", json);                   // wire shape matches crash reports
        using (var doc = System.Text.Json.JsonDocument.Parse(json))
        {
            // gameVersion is the reporter's client build (not the server's), so the inbox shows the
            // player's version — the whole point of BumpReport.ClientVersion (issue #389).
            Assert.Equal("0.8.3", doc.RootElement.GetProperty("gameVersion").GetString());

            var reportJson = doc.RootElement.GetProperty("reportJson");
            Assert.Equal("bump", reportJson.GetProperty("reportType").GetString());
            // No reportJson.kind — the ReportHost triages any kind as category "crash", and a bump
            // must stay category "feedback" (source "server").
            Assert.False(reportJson.TryGetProperty("kind", out _));
            Assert.Equal("server", reportJson.GetProperty("source").GetString());

            // The screenshot travels as a top-level node in the ReportHost's F1 wire shape (base64 JPG +
            // mimeType), so ReportIngest.ExtractScreenshot stores it and the admin detail view shows it.
            var shot = doc.RootElement.GetProperty("screenshot");
            Assert.Equal("image/jpeg", shot.GetProperty("mimeType").GetString());
            Assert.Equal(Convert.ToBase64String(image), shot.GetProperty("base64").GetString());
        }
        Assert.Contains("inventory", json);                                  // rich snapshot rides in reportJson

        // The local snapshot is still written — forwarding is an addition, not a replacement.
        Assert.Equal(1, server.BumpsWritten);
    }

    [Fact]
    public void BumpCommand_WithoutImage_ForwardsNullScreenshot()
    {
        var (server, client, _) = StartWorld();
        var sink = new ForwardSink();
        server.CrashUploader = sink;

        // A plain /bump (chat command) carries no screenshot — the forwarded wire must still be valid and
        // simply carry a null screenshot node, which the ReportHost ingest skips.
        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump no picture here" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.True(sink.Sent.Wait(TimeSpan.FromSeconds(10)), "bump was not forwarded to the sink");

        using var doc = System.Text.Json.JsonDocument.Parse(sink.LastJson!);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("screenshot").ValueKind);
        // A text-only /bump (ChatIntent) carries no client version, so gameVersion falls back to the
        // server's version — never left empty.
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("gameVersion").GetString()));
        Assert.Equal(1, server.BumpsWritten);
    }

    [Fact]
    public void BumpCommand_WithoutSink_StillWritesLocalSnapshotOnly()
    {
        var (server, client, _) = StartWorld();
        Assert.Null(server.CrashUploader); // default: nothing configured, nothing sent

        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump no sink here" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);
    }

    /// <summary>Captures the forwarded wire JSON and signals the test thread (the send is fire-and-forget
    /// on a background task, so the test waits on the semaphore instead of sleeping).</summary>
    private sealed class ForwardSink : ICrashReportSink
    {
        public string? LastJson;
        public System.Threading.SemaphoreSlim Sent { get; } = new(0);

        public bool IsConfigured => true;

        public bool Send(string json)
        {
            LastJson = json;
            Sent.Release();
            return true;
        }
    }

    private (SvGameServer server, LoopbackClientTransport client, SaveGamePaths paths) StartWorld()
    {
        var paths = new SaveGamePaths(_root, _world);
        _repo = new SqliteWorldRepository(paths);
        var link = new LoopbackLink();
        _st = new LoopbackServerTransport(link);
        _client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = _world, Seed = 1, AutoSaveIntervalMinutes = 9999 };

        var server = new SvGameServer(config, _content, _st, _repo);
        server.Start();
        _client.Connect("loopback", 0);
        _client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Tester" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // Resolve the directory exactly as the server does, so the test works whether it lands in the repo's
        // bugreports folder or the per-world fallback.
        _bumpDir = BugReportPaths.Resolve(Path.Combine(paths.WorldDirectory, "bumps"));
        return (server, _client, paths);
    }

    private string[] MyBumpFiles(SaveGamePaths paths, string suffix)
        => Directory.Exists(_bumpDir)
            ? Directory.GetFiles(_bumpDir, $"bump_{_world}_{suffix}")
            : Array.Empty<string>();

    public void Dispose()
    {
        try
        {
            _client?.Dispose();
            _st?.Dispose();
            _repo?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Remove only this test's own bump files (the dir may be the shared repo bugreports folder).
            if (_bumpDir != null && Directory.Exists(_bumpDir))
            {
                foreach (var f in Directory.GetFiles(_bumpDir, $"bump_{_world}_*"))
                {
                    File.Delete(f);
                }
            }

            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
