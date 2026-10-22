// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
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

    // ---------------- fluids are murk, not a wall (#1698) ----------------

    /// <summary>A few cells of water no longer close the line. This is the report that started it: a player
    /// diving in her own moat aimed at an animal a few blocks away and got "no clear line of fire" every
    /// time, because every cell between the two of them was water.</summary>
    [Fact]
    public void A_few_cells_of_water_leave_the_line_of_sight_open()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var water = _content.GetBlock("water")!.NumericId;
            // Flood the whole eye-line band between the two points — shooter and target are both submerged.
            for (int x = 0; x <= 4; x++)
                for (int y = 300; y <= 303; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, 0), water);
                }

            Assert.True(server.HasLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(4, 300, 0)));
        }
    }

    /// <summary>…but a real body of water still hides what is on the far side of it, which is exactly what
    /// the old hard block was protecting: no aggro across a lake.</summary>
    [Fact]
    public void A_whole_lake_still_breaks_the_line_of_sight()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var water = _content.GetBlock("water")!.NumericId;
            for (int x = 0; x <= 30; x++)
                for (int y = 300; y <= 303; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, 0), water);
                }

            Assert.False(server.HasLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(30, 300, 0)));
        }
    }

    /// <summary>Glass is Solid, so it keeps blocking on the very first cell — the murk budget is for fluids
    /// alone and must not have loosened cover.</summary>
    [Fact]
    public void A_glass_pane_still_breaks_the_line_of_sight_at_once()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var glass = _content.GetBlock("glass")!.NumericId;
            server.World.SetBlock(new Vector3i(3, 301, 0), glass);
            server.World.SetBlock(new Vector3i(3, 302, 0), glass);

            Assert.False(server.HasLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(6, 300, 0)));
        }
    }

    // ---------------- the player's line of fire passes what the player walks through (#2533) ----------------

    /// <summary>A solid-on-paper small plant: the case the Solid flag alone gets wrong.</summary>
    private string SolidFloraKey()
        => _content.Blocks.Values.First(b => b.Key.StartsWith(WalkThroughBlocks.FloraPrefix, StringComparison.Ordinal) && b.Solid).Key;

    /// <summary>A two-tall screen of <paramref name="key"/> squarely on the eye-line between (0, 300, 0) and (6, 300, 0).</summary>
    private void Screen(SvGameServer server, string key)
    {
        var id = _content.GetBlock(key)!.NumericId;
        server.World.SetBlock(new Vector3i(3, 301, 0), id);
        server.World.SetBlock(new Vector3i(3, 302, 0), id);
    }

    [Fact]
    public void A_players_shot_passes_tree_leaves_but_an_enemys_sight_does_not()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Screen(server, TreeFoliage.LeavesKey);
            var a = new Vector3f(0, 300, 0);
            var b = new Vector3f(6, 300, 0);
            Assert.True(server.HasAttackLineOfSightForTest(a, b), "the player's shot must pass leaves it can walk through");
            Assert.False(server.HasLineOfSightForTest(a, b), "a crown must stay cover against the machines and animals");
            Assert.False(server.HasLineOfSightForTest(b, a));
        }
    }

    [Fact]
    public void A_players_shot_passes_grass_and_flora_and_a_torch()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = new Vector3f(0, 300, 0);
            var b = new Vector3f(6, 300, 0);
            Screen(server, SolidFloraKey());
            Assert.True(server.HasAttackLineOfSightForTest(a, b));
            Assert.False(server.HasLineOfSightForTest(a, b)); // the enemies' rule is unchanged

            Screen(server, "torch");
            Assert.True(server.HasAttackLineOfSightForTest(a, b));
        }
    }

    [Fact]
    public void A_players_shot_from_inside_a_crown_reaches_a_target_outside()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // The report's case: standing IN a tree, the shooter's own cells are leaves.
            var leaves = _content.GetBlock(TreeFoliage.LeavesKey)!.NumericId;
            for (int x = -1; x <= 2; x++)
                for (int y = 300; y <= 303; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), leaves);
                    }

            Assert.True(server.HasAttackLineOfSightForTest(new Vector3f(0.5f, 300, 0.5f), new Vector3f(6.5f, 300, 0.5f)));
        }
    }

    [Theory]
    [InlineData("stone")]
    [InlineData("glass")]
    public void A_players_shot_is_still_stopped_by_a_wall_or_glass(string key)
    {
        var server = Started(out var repo);
        using (repo)
        {
            Screen(server, key);
            Assert.False(server.HasAttackLineOfSightForTest(new Vector3f(0, 300, 0), new Vector3f(6, 300, 0)));
        }
    }

    [Fact]
    public void A_players_attack_through_leaves_lands_through_glass_does_not()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Gunner");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0f, 300f, 0f);
            p.State.SuitEnergy = 100f;
            p.State.Inventory.SetSlot(0, new ItemStack("laser_pistol", 1));
            p.State.SelectedHotbarSlot = 0;
            server.SpawnPlanetEnemyAtForTest(new Vector3f(10f, 300f, 0f));
            var enemy = server.PlanetEnemies[^1];

            // Glass on the eye-line (sight runs at ~y 301.5): refused.
            for (int y = 300; y <= 302; y++)
            {
                server.World.SetBlock(new Vector3i(5, y, 0), _content.GetBlock("glass")!.NumericId);
            }

            server.AttackEntity("Gunner", enemy.Id, dirX: 1f, dirY: 0f, dirZ: 0f);
            Assert.Equal(enemy.HullMax, enemy.Hull);

            // The same screen as leaves: the shot lands.
            for (int y = 300; y <= 302; y++)
            {
                server.World.SetBlock(new Vector3i(5, y, 0), _content.GetBlock(TreeFoliage.LeavesKey)!.NumericId);
            }

            server.AttackEntity("Gunner", enemy.Id, dirX: 1f, dirY: 0f, dirZ: 0f);
            Assert.True(enemy.Hull < enemy.HullMax, "a shot through leaves must land");
        }
    }

    /// <summary>One list for everyone (#2533): the walk-through props exist in the content, the crowns are walk-through
    /// for the player but not a "prop" (ground creatures still bump into them), trunks/walls/glass are neither.</summary>
    [Fact]
    public void The_walk_through_list_matches_the_content()
    {
        foreach (var key in new[] { "torch", "lantern", "torch_off", "lantern_off", "ladder", "fire", "energy_gate", SolidFloraKey() })
        {
            Assert.True(_content.GetBlock(key) != null, $"'{key}' is missing from data/blocks.json");
            Assert.True(WalkThroughBlocks.IsProp(key), key);
            Assert.True(WalkThroughBlocks.IsWalkThrough(key), key);
        }

        foreach (var key in TreeFoliage.Keys)
        {
            Assert.False(WalkThroughBlocks.IsProp(key), key);
            Assert.True(WalkThroughBlocks.IsWalkThrough(key), key);
        }

        foreach (var key in new[] { "stone", "glass", "wood_log", "water", "lava", "air" })
        {
            Assert.False(WalkThroughBlocks.IsWalkThrough(key), key);
        }

        Assert.False(WalkThroughBlocks.IsWalkThrough(null));
    }

    /// <summary>The client's collision reads the same Shared list as the server (#2533) — the two copies drifted before
    /// (the server missed fire, the energy gate and the unlit torch/lantern). The Unity client cannot be compiled here,
    /// so this pins the source: no private key list may grow back in <c>IsCollidingKey</c>.</summary>
    [Fact]
    public void The_client_collision_and_weapon_trace_use_the_shared_list()
    {
        string src = TestPaths.PlayerControllerSources();
        int at = src.IndexOf("internal static bool IsCollidingKey(string key)", StringComparison.Ordinal);
        Assert.True(at >= 0, "PlayerController.IsCollidingKey not found");
        int end = src.IndexOf(';', at);
        string body = src.Substring(at, end - at);
        Assert.Contains("WalkThroughBlocks.IsProp(key)", body);
        Assert.DoesNotContain("\"torch\"", body);
        Assert.DoesNotContain("flora_", body);
        Assert.Contains("WalkThroughBlocks.IsWalkThrough(", src); // the weapon trace
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
