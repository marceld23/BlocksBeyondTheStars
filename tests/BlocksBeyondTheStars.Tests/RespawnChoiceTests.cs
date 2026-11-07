// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The deferred respawn choice (issue #462): with a home spawn set (issue #461), dying does NOT relocate
/// immediately — the player lies at the death spot at 0 HP until they pick ship vs home (or the ~30 s
/// timeout picks the ship for them). Without a home spawn the classic instant respawn is unchanged.
/// </summary>
public sealed class RespawnChoiceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RespawnChoiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_respchoice_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, IServerTransport? transport = null, GameRules? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "respchoice"));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "respchoice",
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = rules ?? new GameRules(),
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Places a tank, sets the home spawn there and returns the home position.</summary>
    private Vector3f SetHome(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 64, 0.5f);
        server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
        server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
        Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId)); // sanity: the home is armed
        return p.State.CustomSpawnPoint;
    }

    [Fact]
    public void Death_WithHomeSpawn_DefersTheRespawn()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Chooser");
            SetHome(server, p);

            var deathSpot = new Vector3f(30, 64, 30);
            p.State.Position = deathSpot;
            p.State.Health = 0f;
            server.TickForTest(0.1);

            // Not relocated, not revived — the choice is pending; further ticks change nothing.
            Assert.Equal(0f, p.State.Health);
            Assert.Equal(deathSpot, p.State.Position);
            server.TickForTest(0.1);
            Assert.Equal(deathSpot, p.State.Position);
        }
    }

    [Fact]
    public void RespawnChoice_Home_WakesAtTheHomeSpawn()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Homer");
            var home = SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: true);

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(home, p.State.Position);
            Assert.False(p.State.AboardShip); // waking at the base, on foot — not in the ship's life support
        }
    }

    [Fact]
    public void RespawnChoice_Ship_WakesAtTheClassicRespawnPoint()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sailor");
            SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: false);

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
            Assert.True(p.State.AboardShip);
        }
    }

    [Fact]
    public void RespawnChoice_TimesOut_ToTheShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sleeper");
            SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            Assert.Equal(0f, p.State.Health); // pending

            // Uptime advances at the tick tail, so the deadline check sees it on the FOLLOWING tick.
            server.TickForTest(31.0); // > RespawnChoiceTimeout
            server.TickForTest(0.1);  // → the safe default kicks in

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
        }
    }

    [Fact]
    public void RespawnChoice_HomeGone_FallsBackToTheShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Refugee");
            SetHome(server, p);

            // The home tank is mined/destroyed after the spawn was set.
            server.World.SetBlock(new Vector3i(1, 64, 0), BlockId.Air);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: true);

            // Home refused (no tank) → ship fallback, never a dead end.
            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
            Assert.True(p.State.AboardShip);
        }
    }

    [Fact]
    public void Death_WithoutHomeSpawn_StaysInstant()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Classic");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);

            // No home spawn set → the classic immediate respawn (no pending state).
            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
        }
    }

    // ---------------- #3188: the body awaiting the choice is not killed again ----------------

    [Fact]
    public void ABodyAwaitingTheChoice_IsNotKilledAgain()
    {
        // A predator left beside the body kept biting it, and every bite ran the death again: a fresh RespawnOptions (the
        // client replays the death flash and sound for each), a reset deadline, and the salvage note overwritten with
        // "nothing dropped" although the capsule lies at the death spot.
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport, new GameRules { DeathPenalty = DeathPenalty.Normal });
        using (repo)
        {
            var p = server.AddLocalPlayer("Prey");
            SetHome(server, p);
            p.State.Inventory.Add("stone", 10, 1024); // the salvage capsule takes it
            p.State.Position = new Vector3f(30, 64, 30);

            server.KillPlayerForTest(p, "@srv.death.wildlife");
            double deadline = p.Respawn.RespawnChoiceDeadline;
            Assert.True(p.Respawn.ChoicePending);
            Assert.True(p.Respawn.PendingRespawnSalvaged);

            for (int i = 0; i < 5; i++)
            {
                server.AdvanceUptimeForTest(0.5);
                server.KillPlayerForTest(p, "@srv.death.wildlife"); // the next bite of the predator still beside the body
            }

            Assert.Single(transport.To<RespawnOptions>(p));
            Assert.Equal(deadline, p.Respawn.RespawnChoiceDeadline);
            Assert.True(p.Respawn.PendingRespawnSalvaged);
        }
    }

    [Fact]
    public void AMachineBesideTheBody_LeavesItAlone()
    {
        // The same through the real damage path: a planet machine parked beside the body keeps its damage aura on.
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Target");
            SetHome(server, p);
            p.State.Position = new Vector3f(30.5f, 200f, 30.5f); // free air all around: nothing blocks the line of sight
            p.State.Health = 1f;
            server.SpawnPlanetEnemyAtForTest(new Vector3f(31.5f, 200f, 30.5f), damagePerSecond: 100f);

            for (int i = 0; i < 10; i++)
            {
                server.TickEnemiesForTest(0.1);
            }

            Assert.Equal(0f, p.State.Health);
            Assert.True(p.Respawn.ChoicePending);
            Assert.Single(transport.To<RespawnOptions>(p));
        }
    }

    [Theory]
    [InlineData(true)]  // the player picks
    [InlineData(false)] // the timeout picks
    public void AfterTheChoice_TheNextDeathOffersItAgain(bool choose)
    {
        var transport = new RecordingTransport();
        var server = Started(out var repo, transport);
        using (repo)
        {
            var p = server.AddLocalPlayer("Twice");
            SetHome(server, p);
            p.State.Position = new Vector3f(30, 64, 30);
            server.KillPlayerForTest(p, "@srv.death.wildlife");
            if (choose)
            {
                server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: false);
            }
            else
            {
                server.TickForTest(31.0); // > RespawnChoiceTimeout; the deadline check sees it on the following tick
                server.TickForTest(0.1);
            }

            Assert.Equal(100f, p.State.Health);
            Assert.False(p.Respawn.ChoicePending);

            server.KillPlayerForTest(p, "@srv.death.wildlife");

            Assert.Equal(2, transport.To<RespawnOptions>(p).Count);
            Assert.True(p.Respawn.ChoicePending);
        }
    }

    // ---------------- #3370: the body awaiting the choice does nothing in the world ----------------

    [Fact]
    public void ABodyAwaitingTheChoice_CannotMineOrBuild()
    {
        // Milan's report (2026-11-07): under the respawn-choice screen every click on its buttons also reached the world, and
        // the server served it — a dead body mined and built for the whole 30 s.
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Milan");
            SetHome(server, p);
            var sand = _content.GetBlock("sand")!.NumericId;
            var alive = new Vector3i(29, 64, 30);
            var under = new Vector3i(30, 64, 30);
            var beside = new Vector3i(31, 65, 30);
            server.World.SetBlock(alive, sand);
            server.World.SetBlock(under, sand);
            server.World.SetBlock(beside, BlockId.Air);
            p.State.Position = new Vector3f(30.5f, 65f, 30.5f);
            p.State.Inventory.Add("iron_wall", 5, 1024);

            // Control: alive, the same hands at the same spot take the block beside the feet.
            MineUntilGone(server, p, alive);
            Assert.True(server.World.GetBlock(alive).IsAir);

            server.KillPlayerForTest(p, "@srv.death.fall");
            Assert.True(p.Respawn.ChoicePending);

            MineUntilGone(server, p, under);
            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(
                new PlaceBlockIntent { X = beside.X, Y = beside.Y, Z = beside.Z, ItemKey = "iron_wall" }));

            Assert.Equal(sand, server.World.GetBlock(under));
            Assert.True(server.World.GetBlock(beside).IsAir);
            Assert.Equal(5, p.State.Inventory.CountOf("iron_wall"));
            Assert.True(p.Respawn.ChoicePending);
        }
    }

    [Fact]
    public void ABodyAwaitingTheChoice_StillAnswersTheChoiceThroughTheDispatcher()
    {
        // The gate must not swallow the one intent the death screen exists for.
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Chooser");
            SetHome(server, p);
            p.State.Position = new Vector3f(30, 64, 30);
            server.KillPlayerForTest(p, "@srv.death.fall");
            Assert.True(p.Respawn.ChoicePending);

            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new RespawnChoiceIntent { UseCustomSpawn = false }));

            Assert.False(p.Respawn.ChoicePending);
            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
        }
    }

    [Fact]
    public void TheChoiceGate_KeepsTheControlPlane_AndDropsEveryAct()
    {
        // The answer, the keep-alive and the folding of the gear pass …
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new RespawnChoiceIntent()));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new MoveIntent()));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new SetJetpackIntent { Active = false }));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new SetGlidingIntent { Active = false }));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new SetSeatedIntent { Active = false }));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new TradeCancelIntent()));
        // … as does the control plane the held pause keeps (chat, the F1 report, saves).
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new ChatIntent()));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new BumpReport()));
        Assert.True(SvGameServer.ChoicePendingMayHandleForTest(new SaveGameIntent()));

        // Switching gear ON, and every act in the world, is dropped.
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new SetJetpackIntent { Active = true }));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new SetGlidingIntent { Active = true }));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new SetSeatedIntent { Active = true }));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new MineBlockIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new PlaceBlockIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new DoorInteractIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new ConsumeItemIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new CraftIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new LootContainerIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new AttackEntityIntent()));
        Assert.False(SvGameServer.ChoicePendingMayHandleForTest(new UseStationIntent()));
    }

    private static void MineUntilGone(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, Vector3i cell)
    {
        // Bare hands may need a few hits on sand — keep hitting like a held mine button.
        for (int hit = 0; hit < 12 && !server.World.GetBlock(cell).IsAir; hit++)
        {
            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
            server.AdvanceUptimeForTest(0.3);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
