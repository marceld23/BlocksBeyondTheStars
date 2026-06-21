using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Settlement NPCs: an inhabited settlement is populated with humanoid NPCs at its vendor / mission-
/// board / npc markers, while a ruin stays abandoned. NPCs idle near their home marker and drift only
/// within a short leash. Behaviour is server-authoritative; the client renders the projected avatars.
/// </summary>
public sealed class SettlementNpcTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SettlementNpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npc_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo, string planet = "jungle")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + "_npc_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "npc_" + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds a world with an inhabited (populated) settlement.</summary>
    private SvGameServer StartedInhabited(out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var server = Start(seed, out repo);
            if (server.HasSettlement && !server.SettlementRuined && server.NpcCount > 0)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with NPCs found across 80 seeds.");
    }

    [Fact]
    public void InhabitedSettlement_SpawnsNpcs_AtItsMarkers()
    {
        var server = StartedInhabited(out var repo);
        using (repo)
        {
            var npcs = server.NpcSnapshots;
            Assert.NotEmpty(npcs);

            foreach (var npc in npcs)
            {
                // Every NPC has a known role and stands exactly on a matching settlement marker.
                string markerType = npc.Role switch
                {
                    "vendor" => "vendor",
                    "quartermaster" => "mission_board",
                    "settler" => "npc",
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected NPC role '{npc.Role}'."),
                };

                // The NPC stands on a matching marker's column (its feet are grounded on the floor top, so
                // only the horizontal position must match the marker).
                Assert.Contains(
                    server.SettlementMarkers,
                    m => m.Type == markerType
                         && System.Math.Abs(m.Pos.X - npc.Home.X) < 0.001f
                         && System.Math.Abs(m.Pos.Z - npc.Home.Z) < 0.001f);

                // Freshly spawned (no tick yet): the NPC sits at its home marker.
                Assert.True(npc.Pos.DistanceSquared(npc.Home) < 0.001f);
            }

            // A populated settlement always staffs its market with a vendor.
            if (server.SettlementMarkers.Any(m => m.Type == "vendor"))
            {
                Assert.Contains(npcs, n => n.Role == "vendor");
            }
        }
    }

    [Fact]
    public void RuinedSettlement_IsAbandoned_NoNpcs()
    {
        // A harsh world (toxic, cold) is mostly ruins, so an all-ruin world is easy to find. When every
        // settlement on the world is a ruin, there are no inhabitants at all.
        for (long seed = 1; seed <= 160; seed++)
        {
            var server = Start(seed, out var repo, planet: "ice");
            using (repo)
            {
                if (server.HasSettlement && server.InhabitedSettlementCount == 0)
                {
                    Assert.Equal(0, server.NpcCount);
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No all-ruin world found across 160 seeds.");
    }

    [Fact]
    public void Npcs_Stroll_ButStayNearHome_WhilePlayerPresent()
    {
        // Search inhabited settlements for one that demonstrates strolling. Some settlements are cramped or sit
        // on a slope where every NPC is wall-bound by its building/terrain (a valid layout, just not a stroll
        // demo — and which settlement we land on depends on the galaxy seed/placement). For EVERY settlement we
        // simulate we still assert the stay-near-home leash holds for every NPC (the real invariant); we only
        // need ONE to also visibly stroll to prove the idle wander runs.
        for (long seed = 1; seed <= 80; seed++)
        {
            var server = Start(seed, out var repo);
            using (repo)
            {
                if (!server.HasSettlement || server.SettlementRuined || server.NpcCount == 0)
                {
                    continue; // not an inhabited settlement — skip
                }

                // Put a player on the surface beside the settlement so the NPC simulation runs.
                var p = server.AddLocalPlayer("Visitor");
                p.State.Position = server.NpcSnapshots[0].Home;

                // The stroll is a slow Lissajous arc, so check across the whole sim (not just the last frame).
                bool anyMoved = false;
                for (int i = 0; i < 60; i++)
                {
                    server.TickForTest(0.5);
                    foreach (var n in server.NpcSnapshots)
                    {
                        if (n.Pos.DistanceSquared(n.Home) > 0.01f) { anyMoved = true; }
                        Assert.True(n.Pos.DistanceSquared(n.Home) <= 16f, // leash ~1.6 → max ~3.6m
                            $"NPC '{n.Role}' wandered too far from home ({n.Pos.DistanceSquared(n.Home)}).");
                    }
                }

                if (anyMoved)
                {
                    return; // an inhabited settlement whose NPCs stroll within the leash — intent verified
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement had a strolling NPC across 80 seeds.");
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
