using System;
using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

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
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_npc_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "npc_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "npc_" + seed, Seed = seed, StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = true, PlaceWrecks = false,
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
        for (long seed = 1; seed <= 80; seed++)
        {
            var server = Start(seed, out var repo);
            using (repo)
            {
                if (server.HasSettlement && server.SettlementRuined)
                {
                    Assert.Equal(0, server.NpcCount);
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No ruined settlement found across 80 seeds.");
    }

    [Fact]
    public void Npcs_Stroll_ButStayNearHome_WhilePlayerPresent()
    {
        var server = StartedInhabited(out var repo);
        using (repo)
        {
            // Put a player on the surface beside the settlement so the NPC simulation runs.
            var p = server.AddLocalPlayer("Visitor");
            p.State.Position = server.NpcSnapshots[0].Home;

            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(0.5);
            }

            var after = server.NpcSnapshots;

            // They move (idle stroll)…
            Assert.Contains(after, n => n.Pos.DistanceSquared(n.Home) > 0.01f);

            // …but never drift far from their home marker (leash ~2.5 → max ~3.6m).
            foreach (var n in after)
            {
                Assert.True(n.Pos.DistanceSquared(n.Home) <= 16f,
                    $"NPC '{n.Role}' wandered too far from home ({n.Pos.DistanceSquared(n.Home)}).");
            }
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
