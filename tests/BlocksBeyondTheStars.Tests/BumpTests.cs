// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
    public void BumpSnapshot_NamesTheBodyThePlayerIsOn_NotOnlyTheSaveStartType()
    {
        var (server, client, paths) = StartWorld();

        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump which planet" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        string json = File.ReadAllText(Assert.Single(MyBumpFiles(paths, "*.json")));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var location = doc.RootElement.GetProperty("location");
        // #2276: planetType is the save's START type (kept); bodyPlanetType is the body the reporter stands on.
        Assert.True(location.TryGetProperty("planetType", out _));
        Assert.Equal(server.World.Planet.Key, location.GetProperty("bodyPlanetType").GetString());
    }

    /// <summary>#3101: the teleport report of 2026-11-01 could not say which shaped block the player was pressed against —
    /// the surroundings listed block keys only. A non-cube cell now carries its form (and yaw / up-face), and the census
    /// counts shaped cells apart.</summary>
    [Fact]
    public void BumpSnapshot_RecordsTheFormOfShapedCells()
    {
        var (server, _, paths) = StartWorld();
        var (connectionId, session) = server.Sessions.First(kv => kv.Value.Joined);
        var p = session.State;
        var cell = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(
            (int)Math.Floor(p.Position.X) + 1, (int)Math.Floor(p.Position.Y), (int)Math.Floor(p.Position.Z));
        int fence = BlocksBeyondTheStars.Shared.World.ShapeCode.Pack(BlocksBeyondTheStars.Shared.World.BlockShape.Fence, 1);
        server.World.SetBlock(cell, _content.GetBlock("stone")!.NumericId, shape: fence);

        server.HandlePayloadForTest(connectionId, NetCodec.Encode(new ChatIntent { Text = "/bump teleported at a fence" }));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(MyBumpFiles(paths, "*.json"))));
        var surroundings = doc.RootElement.GetProperty("surroundings").EnumerateArray().ToList();
        var entry = Assert.Single(surroundings,
            e => e.GetProperty("x").GetInt32() == cell.X && e.GetProperty("y").GetInt32() == cell.Y && e.GetProperty("z").GetInt32() == cell.Z);
        Assert.Equal("stone", entry.GetProperty("block").GetString());
        Assert.Equal("Fence", entry.GetProperty("shape").GetString());
        Assert.Equal(1, entry.GetProperty("yaw").GetInt32());
        Assert.DoesNotContain(surroundings, e => e.TryGetProperty("shape", out var s) && s.GetString() == "Cube"); // a cube carries none
        Assert.Contains(doc.RootElement.GetProperty("surroundingsCensus").EnumerateArray(),
            c => c.GetProperty("block").GetString() == "stone#Fence" && c.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public void BumpSnapshot_ListsTheMachinesAround_AndTheTrainingProgress()
    {
        // #2555: a "Guardian in the water" report could not be resolved — the snapshot listed only fauna, and nothing
        // said which training step the player was on or which tips VEGA had already given.
        var (server, _, paths) = StartWorld();
        var (connectionId, session) = server.Sessions.First(kv => kv.Value.Joined);
        var p = session.State;
        p.Milestones.Add("story:tutorial:step:b1.1");
        p.Milestones.Add("vega:hint:lamp_off");
        p.Milestones.Add("vega:mem:1"); // neither a tutorial nor a hint key — stays out
        p.StepProgress["tutorial:b1.2"] = 3;

        var at = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(p.Position.X + 3f, p.Position.Y, p.Position.Z);
        server.SpawnPlanetEnemyAtForTest(at, CombatEntityKind.Creature, damagePerSecond: 0f);
        string robotId = server.PlanetEnemies[^1].Id;
        server.SpawnBanditAtForTest(new BlocksBeyondTheStars.Shared.Geometry.Vector3f(p.Position.X - 4f, p.Position.Y, p.Position.Z), p.PlayerId);
        string banditId = server.Bandits[^1].Id;

        // Straight into the handler — no tick in between that could move or retire the test's machines.
        server.HandlePayloadForTest(connectionId, NetCodec.Encode(new ChatIntent { Text = "/bump a guardian in the pond" }));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(MyBumpFiles(paths, "*.json"))));
        var machines = doc.RootElement.GetProperty("nearby").GetProperty("machines").EnumerateArray().ToList();
        var robot = Assert.Single(machines, m => m.GetProperty("Id").GetString() == robotId);
        Assert.Equal("Creature", robot.GetProperty("kind").GetString());
        Assert.True(robot.GetProperty("Hostile").GetBoolean());
        Assert.Contains(robot.GetProperty("motion").GetString(), new[] { "patrolling", "chasing", "pausing", "fleeing", "dazed", "leavingWater" });
        Assert.Contains(robot.GetProperty("water").GetString(), new[] { "dry", "wading", "deep" });
        var bandit = Assert.Single(machines, m => m.GetProperty("Id").GetString() == banditId);
        Assert.Equal("Approach", bandit.GetProperty("banditPhase").GetString());

        var tutorial = doc.RootElement.GetProperty("tutorial");
        var milestones = tutorial.GetProperty("milestones").EnumerateArray().Select(m => m.GetString()).ToList();
        Assert.Contains("story:tutorial:step:b1.1", milestones);
        Assert.Contains("vega:hint:lamp_off", milestones);
        Assert.DoesNotContain("vega:mem:1", milestones);
        var steps = tutorial.GetProperty("activeSteps").EnumerateArray().ToList();
        var step = Assert.Single(steps, s => s.GetProperty("step").GetString() == "tutorial:b1.2");
        Assert.Equal(3, step.GetProperty("count").GetInt32());
    }

    /// <summary>#3386: "the cutscene did not come after I shut down the Guardian core" could not be answered from the
    /// snapshot. It now carries the world story's state and the reporter's story milestones — the cutscene ledger with its
    /// <c>:sent</c> keys and the finale reward — but not the basic training's keys (those stay in <c>tutorial</c>).</summary>
    [Fact]
    public void BumpSnapshot_CarriesTheWorldStory_AndThePlayersCutsceneLedger()
    {
        var (server, _, paths) = StartWorld();
        var (connectionId, session) = server.Sessions.First(kv => kv.Value.Joined);
        var p = session.State;
        const string Vega = "vega_protocol";
        p.Milestones.Add(BlocksBeyondTheStars.Shared.Story.StoryPackKeys.CutsceneSent(Vega, "fin.standdown"));
        p.Milestones.Add("reward:guardian_core");
        p.Milestones.Add("story:tutorial:step:b1.1");

        server.HandlePayloadForTest(connectionId, NetCodec.Encode(new ChatIntent { Text = "/bump no cutscene" }));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(MyBumpFiles(paths, "*.json"))));
        var story = doc.RootElement.GetProperty("story");
        Assert.False(string.IsNullOrEmpty(story.GetProperty("id").GetString()));
        Assert.True(story.TryGetProperty("worldChapter", out _));
        Assert.True(story.TryGetProperty("guardianSystemRevealed", out _));
        Assert.False(story.GetProperty("guardianDefeated").GetBoolean()); // a fresh world
        Assert.False(story.GetProperty("coreHacked").GetBoolean());
        Assert.Equal(-1, story.GetProperty("duelNode").GetInt32());
        Assert.True(story.TryGetProperty("trackStarted", out _));
        var milestones = story.GetProperty("milestones").EnumerateArray().Select(m => m.GetString()).ToList();
        Assert.Contains("story:vega_protocol:cut:fin.standdown:sent", milestones);
        Assert.Contains("reward:guardian_core", milestones);
        Assert.DoesNotContain("story:tutorial:step:b1.1", milestones); // the training stays in the tutorial block
    }

    [Fact]
    public void BumpSnapshot_InSpace_ListsThePartnerPilot_WithTheFloatingShip()
    {
        // #2710: two friends flew around one orbit without finding each other, and the reports could not say where the
        // partner was — pilots live in the pose table, and the space block listed only the instance's entities.
        var (server, _, paths) = StartWorld(c =>
        {
            c.PlaceStarterShip = false;
            c.Rules.FreeSpaceFlight = true;
        });
        var (connectionId, reporter) = server.Sessions.First(kv => kv.Value.Joined);
        string reporterId = reporter.State.PlayerId;
        reporter.State.AboardShip = true; // launching needs the pilot aboard
        server.EnterSpace(reporterId);
        Assert.True(server.InSpace(reporterId));
        server.ShipMove(reporterId, 10f, 0f, 0f);

        var partner = server.AddLocalPlayer("Partner");
        server.EnterSpace(partner.State.PlayerId);
        server.ShipMove(partner.State.PlayerId, -40f, 0f, 10f, 15f); // the partner's ship
        partner.State.InEva = true;                                     // ...and out on a spacewalk beside it
        server.ShipMove(partner.State.PlayerId, -44f, 1f, 12f);

        server.HandlePayloadForTest(connectionId, NetCodec.Encode(new ChatIntent { Text = "/bump where is my friend" }));

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(MyBumpFiles(paths, "*.json"))));
        var space = doc.RootElement.GetProperty("space");
        var pilot = Assert.Single(space.GetProperty("pilots").EnumerateArray().ToList());
        Assert.Equal(partner.State.PlayerId, pilot.GetProperty("id").GetString());
        Assert.Equal("Partner", pilot.GetProperty("name").GetString());
        Assert.True(pilot.GetProperty("eva").GetBoolean());
        Assert.Equal(-44f, pilot.GetProperty("x").GetSingle(), 2);
        Assert.Equal(12f, pilot.GetProperty("z").GetSingle(), 2);
        Assert.Equal(MathF.Sqrt((54f * 54f) + 1f + (12f * 12f)), pilot.GetProperty("dist").GetSingle(), 2);
        var ship = pilot.GetProperty("parkedShip");
        Assert.Equal(-40f, ship.GetProperty("x").GetSingle(), 2);
        Assert.Equal(10f, ship.GetProperty("z").GetSingle(), 2);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, space.GetProperty("floatingShip").ValueKind); // the reporter flies
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
        string replyKey = BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.Derive("install-secret");
        client.Send(NetCodec.Encode(new BumpReport { Description = "[feedback] jetpack stuck in ceiling", Image = image, ClientVersion = "0.8.3", ReplyKey = replyKey }), DeliveryMode.ReliableOrdered);
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
            // The reporter's reply key passes through untouched (#1359), so the inbox can pair this row with
            // the client-direct upload and route an answer on either one to the player.
            Assert.Equal(replyKey, doc.RootElement.GetProperty("replyKey").GetString());

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
    public void BumpReport_FullLengthFeedbackText_ReachesTheInboxTwinUncut()
    {
        // The F1 dialog accepts 4800 characters and prefixes "[feedback] " + an 80-character title + " — ". The
        // server used to cut the snapshot/forward at 2000, so a long idea arrived without its ending (2026-09-15).
        var (server, client, _) = StartWorld();
        var sink = new ForwardSink();
        server.CrashUploader = sink;

        string title = new string('t', 80);
        string body = new string('b', 4799) + "Z";
        string description = "[feedback] " + title + " — " + body;
        client.Send(NetCodec.Encode(new BumpReport { Description = description, ClientVersion = "2026.9.10" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.True(sink.Sent.Wait(TimeSpan.FromSeconds(10)), "bump was not forwarded to the sink");
        using var doc = System.Text.Json.JsonDocument.Parse(sink.LastJson!);
        Assert.Equal(description, doc.RootElement.GetProperty("description").GetString());
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
        // ...and no reply key: the server must never derive one from the player name (#1359).
        Assert.Equal("", doc.RootElement.GetProperty("replyKey").GetString());
        Assert.Equal(1, server.BumpsWritten);
    }

    [Fact]
    public void BumpReport_WithMalformedReplyKey_ForwardsAnEmptyKey()
    {
        var (server, client, _) = StartWorld();
        var sink = new ForwardSink();
        server.CrashUploader = sink;

        // Whatever a tampered client puts in the field, only a well-formed key (64 lowercase hex) is passed on —
        // the ReportHost would ignore anything else anyway, and an empty key means "not answerable here".
        client.Send(NetCodec.Encode(new BumpReport { Description = "[feedback] odd key", ReplyKey = "not-a-key" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.True(sink.Sent.Wait(TimeSpan.FromSeconds(10)), "bump was not forwarded to the sink");
        using var doc = System.Text.Json.JsonDocument.Parse(sink.LastJson!);
        Assert.Equal("", doc.RootElement.GetProperty("replyKey").GetString());
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

    private (SvGameServer server, LoopbackClientTransport client, SaveGamePaths paths) StartWorld(Action<ServerConfig>? configure = null)
    {
        var paths = new SaveGamePaths(_root, _world);
        _repo = new SqliteWorldRepository(paths);
        var link = new LoopbackLink();
        _st = new LoopbackServerTransport(link);
        _client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = _world, Seed = 1, AutoSaveIntervalMinutes = 9999 };
        configure?.Invoke(config);

        var server = new SvGameServer(config, _content, _st, _repo);
        server.Start();
        _client.Connect("loopback", 0);
        _client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Tester" }), DeliveryMode.ReliableOrdered);
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
