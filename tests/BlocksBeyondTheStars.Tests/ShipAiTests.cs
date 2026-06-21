using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ship AI companion "VEGA": the onboarding stage chain (server-authoritative, per-player, persisted via
/// <see cref="PlayerState.Milestones"/>), the veteran auto-skip, the explicit skip intent, and the
/// memory-fragment story arc (redeemed aboard, knowledge reward, Mk3 blueprint at the arc's end).
/// </summary>
public sealed class ShipAiTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipAiTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_vega_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ServerConfig Config() => new()
    {
        WorldName = "vega",
        Seed = 123456,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false, // bare terrain at the spawn column (mining test digs straight down)
    };

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    /// <summary>Collects every VEGA line the client receives (handler must be attached before joining).</summary>
    private static List<ShipAiLine> CaptureVega(LoopbackClientTransport client)
    {
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };
        return lines;
    }

    [Fact]
    public void NewPlayer_BootsVega_AndMineStageAdvancesToCraft()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Rookie");

        // The intro + first objective arrived, and the chain starts at the mining stage.
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.Contains(lines, l => l.LineKey == "vega.s.mine.start");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey);
        Assert.Contains("vega:intro", server.MilestonesForTest("Rookie"));

        // Mine three nearby blocks (own + neighbour columns, a few cells below the head, all within reach —
        // the spawn column may sit over a cave, so straight-down alone can run out of reachable rock).
        var session = server.Sessions[1];
        int px = (int)Math.Floor(session.State.Position.X);
        int pz = (int)Math.Floor(session.State.Position.Z);
        int topY = (int)Math.Ceiling(session.State.Position.Y);
        int mined = 0;
        for (int dx = -2; dx <= 2 && mined < 3; dx++)
            for (int dz = -2; dz <= 2 && mined < 3; dz++)
                for (int y = topY; y > topY - 7 && mined < 3; y--)
                {
                    var pos = new Vector3i(px + dx, y, pz + dz);
                    if (server.World.GetBlock(pos).IsAir)
                    {
                        continue;
                    }

                    for (int hit = 0; hit < 12 && !server.World.GetBlock(pos).IsAir; hit++)
                    {
                        client.Send(NetCodec.Encode(new MineBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z }),
                            DeliveryMode.ReliableOrdered);
                        server.Tick(0.1);
                    }

                    if (server.World.GetBlock(pos).IsAir)
                    {
                        mined++;
                    }
                }

        Assert.Equal(3, mined);
        client.Poll();

        Assert.Contains("vega:stage:mine", server.MilestonesForTest("Rookie"));
        Assert.Contains(lines, l => l.LineKey == "vega.s.mine.done");
        Assert.Contains(lines, l => l.LineKey == "vega.s.craft.start");
        Assert.Equal("vega.obj.craft", lines.Last().ObjectiveKey);
    }

    [Fact]
    public void VeteranSave_AutoSkipsOnboarding_WithOneLine()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        repo.Initialize();

        // A save that has clearly played before: knowledge already earned.
        repo.SavePlayer(new PlayerState { PlayerId = "Vet", Name = "Vet", KnowledgePoints = 12 });

        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Vet");

        var milestones = server.MilestonesForTest("Vet");
        Assert.Contains("vega:stage:mine", milestones);
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.veteran");
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.intro.1");
        Assert.Equal(string.Empty, lines.Last().ObjectiveKey); // no objective chip for veterans
    }

    [Fact]
    public void SkipOnboardingIntent_GrantsTheWholeChain()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Skipper");

        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var milestones = server.MilestonesForTest("Skipper");
        Assert.Contains("vega:stage:mine", milestones);
        Assert.Contains("vega:stage:trade", milestones);
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.skip");
        Assert.Equal(string.Empty, lines.Last().ObjectiveKey);
    }

    [Fact]
    public void Restart_AfterSkip_RunsTheTutorialAgain()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Returner");

        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Contains("vega:stage:land", server.MilestonesForTest("Returner"));

        // The way back: restart wipes the stage chain and re-runs the intro + first objective.
        client.Send(NetCodec.Encode(new SkipOnboardingIntent { Restart = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var milestones = server.MilestonesForTest("Returner");
        Assert.DoesNotContain("vega:stage:mine", milestones);
        Assert.DoesNotContain("vega:stage:land", milestones);
        Assert.Contains("vega:intro", milestones); // re-armed by the fresh boot
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey); // back at lesson one
    }

    [Fact]
    public void MemoryFragments_RedeemAboard_PacedWithKnowledgeReward()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Archivist");

        var p = server.Sessions[1].State;
        Assert.True(p.AboardShip, "players start aboard the ship");
        int knowledgeBefore = p.KnowledgePoints;
        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 2));

        // The advisor poll runs at 1 Hz and redemption is paced (~6 s apart): tick well past both.
        for (int i = 0; i < 10; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        var milestones = server.MilestonesForTest("Archivist");
        Assert.Contains("vega:mem:1", milestones);
        Assert.Contains("vega:mem:2", milestones);
        Assert.Equal(0, p.Inventory.CountOf("ai_memory_fragment"));
        Assert.Equal(knowledgeBefore + 6, p.KnowledgePoints); // +3 per restored fragment
        Assert.Contains(lines, l => l.LineKey == "vega.mem.1" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.mem.2" && l.Kind == 2);
    }

    [Fact]
    public void TenthFragment_CompletesTheArc_AndTeachesTheMk3Blueprint()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Historian");

        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 9; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat); // nine beats already restored on earlier worlds
        }

        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        Assert.Contains("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.Contains("ai_core_mk3", p.UnlockedBlueprints);
        Assert.Contains(lines, l => l.LineKey == "vega.mem.10" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.sys.mk3bp");
    }

    [Fact]
    public void AiCoreTier_FollowsTheBuiltModules_InPlayerState()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var states = new List<PlayerStateUpdate>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is PlayerStateUpdate u)
            {
                states.Add(u);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Engineer");

        Assert.Equal(1, states.Last().AiCoreTier); // bare VEGA

        var session = server.Sessions[1];
        session.Ships[session.ActiveShipId].Modules.Add("ai_core_mk2");
        // Trigger a fresh authoritative state send (stealth toggle succeeds with a suit carried).
        session.State.Inventory.SetSlot(11, new ItemStack("stealth_suit", 1));
        client.Send(NetCodec.Encode(new ToggleStealthIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Equal(2, states.Last().AiCoreTier);
    }

    [Fact]
    public void Milestones_PersistAcrossReload()
    {
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Saver");
            client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            server.Stop();
        }

        SqliteWorldRepositoryReset();

        using (var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        {
            repo2.Initialize();
            var loaded = repo2.LoadPlayer("Saver");
            Assert.NotNull(loaded);
            Assert.Contains("vega:intro", loaded!.Milestones);
            Assert.Contains("vega:stage:land", loaded.Milestones);
        }
    }

    private static void SqliteWorldRepositoryReset()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
