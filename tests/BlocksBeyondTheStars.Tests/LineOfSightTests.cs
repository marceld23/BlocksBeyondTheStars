// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Line-of-sight gating (early-game defence): a hostile can only bite / keep hunting a target it can actually
/// see, so taking cover behind a wall or dropping into a cave breaks the attack. Covers the raw sightline
/// sampler (incl. the vertical axis) and the end-to-end damage gate through a real tick.
/// </summary>
public sealed class LineOfSightTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LineOfSightTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_los_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "rocky",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Generated surface (first solid cell scanning down) at a column — used to stand combatants on the
    /// real terrain, since enemies snap to the generated surface height regardless of hand-placed blocks.</summary>
    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    [Fact]
    public void Open_air_has_a_clear_line_of_sight()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Well above any terrain, so the whole segment is empty air.
            Assert.True(server.HasLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(6, 300, 0)));
        }
    }

    [Fact]
    public void A_solid_wall_breaks_the_horizontal_line_of_sight()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            // A 2-tall pillar squarely on the eye-line (sight is lifted ~1.5 to the head, y 300 → ~301).
            server.World.SetBlock(new Vector3i(3, 301, 0), stone);
            server.World.SetBlock(new Vector3i(3, 302, 0), stone);

            Assert.False(server.HasLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(6, 300, 0)));
        }
    }

    [Fact]
    public void Solid_ground_breaks_the_vertical_line_of_sight_into_a_cave()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Same column, one combatant five blocks below the other: with empty air the sightline is clear,
            // but a solid ceiling between them (as when a player digs into a cave) cuts it.
            var high = new Vector3f(10, 300, 10);
            var low = new Vector3f(10, 295, 10);
            Assert.True(server.HasLineOfSightForTest(high, low));

            server.World.SetBlock(new Vector3i(10, 298, 10), _content.GetBlock("stone")!.NumericId);
            Assert.False(server.HasLineOfSightForTest(high, low));
        }
    }

    [Fact]
    public void A_wall_between_a_hunter_and_the_player_stops_the_bite()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hider");
            p.State.AboardShip = false;

            int sy = SurfaceY(server, 0, 0);
            p.State.Position = new Vector3f(0.5f, sy + 1, 0.5f);
            server.SpawnPlanetEnemyAtForTest(new Vector3f(1.5f, sy + 1, 0.5f), 40f);

            // Baseline: out in the open, the adjacent hunter's aura bites.
            p.State.Health = 100f;
            for (int i = 0; i < 3; i++)
            {
                server.Tick(0.5);
            }

            Assert.True(p.State.Health < 100f, "an adjacent hunter in the open should damage the player");

            // Drop a wall between them and heal up — behind cover the bite must stop entirely.
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int y = sy - 1; y <= sy + 4; y++)
            {
                server.World.SetBlock(new Vector3i(1, y, 0), stone);
            }

            p.State.Health = 100f;
            for (int i = 0; i < 6; i++)
            {
                server.Tick(0.5);
            }

            Assert.Equal(100f, p.State.Health);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
