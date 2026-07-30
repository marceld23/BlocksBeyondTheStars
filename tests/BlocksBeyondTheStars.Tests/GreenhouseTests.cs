// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Greenhouses (#626), the cultivated berry crop they grow (#627) and the station hydroponics bay (#628).
/// The promise the whole feature rests on is that a greenhouse is a RELIABLE food source: the berries must be
/// edible on every world (never the toxic variant a wild plant can roll), the plants must stand on hosts they
/// can actually regrow on, and the bay aboard a station must regrow them like any planet-side bed.
/// </summary>
public sealed class GreenhouseTests : IDisposable
{
    private const string CropKey = "flora_cropberry";
    private const string TrayKey = "hydro_tray";

    private readonly string _root;
    private readonly GameContent _content;

    public GreenhouseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_greenhouse_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private ushort Id(string key) => _content.GetBlock(key)?.NumericId.Value ?? 0;

    // ---------------------------------------------------------------- #627: the cultivated crop

    /// <summary>The crop must never appear in a world's flora roster. A roster entry would give it a rolled
    /// <c>Toxic</c> flag, and a toxic plant's berries are swapped for <c>toxic_berries</c> when it is broken —
    /// a village greenhouse would then poison the player on roughly a third of all worlds.</summary>
    [Fact]
    public void CultivatedCrop_NeverJoinsAWorldRoster()
    {
        foreach (var planet in _content.Planets.Values)
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                var roster = FloraGenerator.GenerateRoster(planet, seed);
                Assert.DoesNotContain(roster, fs => fs.BlockKey == CropKey);
            }
        }
    }

    /// <summary>Skipping the crop must not shift the WILD species' identities: each roster entry still carries
    /// the id of its own slot in the catalog, so the same seed keeps naming the same plants.</summary>
    [Fact]
    public void SkippingCrops_LeavesWildSpeciesIdsUnshifted()
    {
        var planet = _content.Planets.Values.First(p => !p.IsAirless && p.FloraDensity > 0);
        var roster = FloraGenerator.GenerateRoster(planet, 12345);
        Assert.NotEmpty(roster);

        foreach (var fs in roster)
        {
            int catalogIndex = -1;
            for (int i = 0; i < FloraCatalog.All.Count; i++)
            {
                if (FloraCatalog.All[i].Key == fs.BlockKey)
                {
                    catalogIndex = i;
                    break;
                }
            }

            Assert.Equal("fl" + catalogIndex, fs.Id);
        }
    }

    /// <summary>Guard the other way round: wild flora must STILL roll toxic. If this ever goes green because
    /// nothing is toxic any more, the exclusion above has been applied far too broadly.</summary>
    [Fact]
    public void WildFlora_StillRollsToxicSpecies()
    {
        var planet = _content.Planets.Values.First(p => !p.IsAirless && p.FloraDensity > 0);
        bool anyToxic = false;
        for (long seed = 1; seed <= 20 && !anyToxic; seed++)
        {
            anyToxic = FloraGenerator.GenerateRoster(planet, seed).Any(fs => fs.Toxic);
        }

        Assert.True(anyToxic, "Wild flora must keep rolling toxic species — only cultivated crops are exempt.");
    }

    /// <summary>The crop is real content: it drops the edible berry, and both its soil and its hydroponic tray
    /// count as flora hosts (without that the server refuses to plant it and never regrows it).</summary>
    [Fact]
    public void Crop_DropsEdibleBerries_AndItsHostsAreRegistered()
    {
        var crop = _content.GetBlock(CropKey);
        Assert.NotNull(crop);
        Assert.Contains(crop!.Drops, d => d.Item == "berries");
        Assert.DoesNotContain(crop.Drops, d => d.Item == "toxic_berries");
        Assert.True(FloraCatalog.IsCultivated(CropKey));

        foreach (var host in new[] { "dirt", TrayKey })
        {
            Assert.True(_content.GetBlock(host)?.FloraHost == true, $"'{host}' must be registered as a flora host.");
        }
    }

    // ---------------------------------------------------------------- #626: settlement greenhouses

    private static IEnumerable<(int X, int Y, int Z)> Cells(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    yield return (x, y, z);
                }
    }

    private int CountOf(SettlementStructure s, string blockKey)
    {
        ushort id = Id(blockKey);
        return id == 0 ? 0 : Cells(s).Count(c => s.Get(c.X, c.Y, c.Z) == id);
    }

    [Theory]
    [InlineData("village")]
    [InlineData("town")]
    [InlineData("city")]
    public void EveryInhabitedSettlement_GrowsBerriesInAGreenhouse(string tier)
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            var s = SettlementGenerator.Generate(tier, ruined: false, seed, "grass", _content);

            Assert.Contains(s.Markers, m => m.Type == "greenhouse");
            Assert.True(CountOf(s, CropKey) >= 4, $"{tier} (seed {seed}) should grow a bed of crops.");
            Assert.True(CountOf(s, "glass") >= 20, $"{tier} (seed {seed}) greenhouse should be glazed.");
        }
    }

    /// <summary>Every crop must stand on a host it can regrow on — otherwise the greenhouse is harvested once
    /// and stays bare for the rest of the save.</summary>
    [Theory]
    [InlineData("village")]
    [InlineData("city")]
    public void EveryCrop_StandsOnAValidHost(string tier)
    {
        var hosts = FloraCatalog.All.First(sp => sp.Key == CropKey).Hosts
            .Select(Id)
            .Where(id => id != 0)
            .ToHashSet();
        ushort crop = Id(CropKey);

        for (long seed = 1; seed <= 8; seed++)
        {
            var s = SettlementGenerator.Generate(tier, ruined: false, seed, "grass", _content);
            foreach (var (x, y, z) in Cells(s))
            {
                if (s.Get(x, y, z) != crop)
                {
                    continue;
                }

                Assert.True(y > 0, "A crop must never sit on the bottom row — it would have no host below.");
                Assert.Contains(s.Get(x, y - 1, z), hosts);
            }
        }
    }

    [Fact]
    public void VillageGrowsInSoilUnderTimber_CityRunsHydroponics()
    {
        var village = SettlementGenerator.Generate("village", ruined: false, 5, "grass", _content);
        Assert.True(CountOf(village, "dirt") >= 4, "A village greenhouse grows its berries in soil beds.");
        Assert.True(CountOf(village, "wood_log") >= 4, "A village greenhouse is framed in timber.");
        Assert.Equal(0, CountOf(village, TrayKey));

        var city = SettlementGenerator.Generate("city", ruined: false, 5, "grass", _content);
        Assert.True(CountOf(city, TrayKey) >= 4, "A city greenhouse runs hydroponic trays.");
        Assert.Equal(0, CountOf(city, "dirt"));
    }

    /// <summary>A city feeds more mouths than a village, so it keeps more than one glass house.</summary>
    [Fact]
    public void CityKeepsSeveralGreenhouses_VillageKeepsOne()
    {
        int GreenhouseCount(string tier, long seed) =>
            SettlementGenerator.Generate(tier, ruined: false, seed, "grass", _content)
                .Markers.Count(m => m.Type == "greenhouse");

        for (long seed = 1; seed <= 8; seed++)
        {
            Assert.Equal(1, GreenhouseCount("village", seed));
            Assert.InRange(GreenhouseCount("city", seed), 2, 3);
        }
    }

    /// <summary>The tier's own door hangs in the greenhouse doorway too — villagers swing theirs open by hand,
    /// a city bay slides. (Both are settlement-wide invariants, and the greenhouse must not break them.)</summary>
    [Fact]
    public void GreenhouseDoor_MatchesTheSettlementTier()
    {
        var village = SettlementGenerator.Generate("village", ruined: false, 3, "grass", _content);
        Assert.Contains(village.Markers, m => m.Type == "door_hinge");
        Assert.DoesNotContain(village.Markers, m => m.Type == "door_slide");

        var city = SettlementGenerator.Generate("city", ruined: false, 3, "grass", _content);
        Assert.Contains(city.Markers, m => m.Type == "door_slide");
        Assert.DoesNotContain(city.Markers, m => m.Type == "door_hinge");
    }

    /// <summary>A ruin is abandoned: no gardener, no tended crop, no marker claiming the place still feeds
    /// anyone. The decay pass turns the glass house into a shell, which is the point.</summary>
    [Fact]
    public void RuinedSettlements_TendNoCrops()
    {
        for (long seed = 1; seed <= 8; seed++)
        {
            var s = SettlementGenerator.Generate("town", ruined: true, seed, "grass", _content);
            Assert.DoesNotContain(s.Markers, m => m.Type == "greenhouse");
            Assert.Equal(0, CountOf(s, CropKey));
        }
    }

    [Fact]
    public void Greenhouses_AreDeterministic_ForTheSameSeed()
    {
        var a = SettlementGenerator.Generate("city", ruined: false, 909, "grass", _content);
        var b = SettlementGenerator.Generate("city", ruined: false, 909, "grass", _content);

        Assert.Equal(a.Markers.Count(m => m.Type == "greenhouse"), b.Markers.Count(m => m.Type == "greenhouse"));
        foreach (var (x, y, z) in Cells(a))
        {
            Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
        }
    }

    // ---------------------------------------------------------------- #628: the station hydroponics bay

    [Theory]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("colossal")]
    public void Stations_CarryAHydroponicsBay(string tier)
    {
        var s = StationGenerator.Generate(tier, 4242, _content);

        Assert.Contains(s.Modules, m => m.Type == "hydro");
        Assert.Contains(s.Markers, m => m.Type == "greenhouse");

        ushort crop = Id(CropKey), tray = Id(TrayKey);
        int planted = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) != crop)
                    {
                        continue;
                    }

                    planted++;
                    Assert.True(y > 0, "A crop must never sit on the bottom row of the station structure.");
                    Assert.Equal(tray, s.Get(x, y - 1, z)); // rooted in a tray, never floating in the room
                }

        Assert.True(planted >= 2, $"A {tier} station's bay should actually be planted (found {planted}).");
    }

    /// <summary>A stair shaft cut through a deck is a fall onto the room below, not a way out into space — so a
    /// crop beside one still counts as enclosed. Before #628 the check looked only one cell down, which barred
    /// plants from every multi-deck bay.</summary>
    [Fact]
    public void ShaftInADeck_DoesNotCountAsOpenSpace_ButARealLedgeStillDoes()
    {
        var server = StartedOnAPlanet(out var repo);
        using (repo)
        {
            ushort hull = Id("iron_wall");
            var cells = new Dictionary<(int, int, int), ushort>();
            ushort Get(int x, int y, int z) => cells.TryGetValue((x, y, z), out var b) ? b : (ushort)0;
            void Solid(int x, int y, int z) => cells[(x, y, z)] = hull;

            // Two decks: an upper floor (y=0) with a shaft hole at (3,0,2), and a lower deck 4 cells down.
            // The plant stands at (2,1,2), right beside the hole.
            for (int x = 0; x <= 4; x++)
                for (int z = 0; z <= 4; z++)
                {
                    if (x != 3 || z != 2)
                    {
                        Solid(x, 0, z);
                    }

                    Solid(x, -4, z); // the deck below catches anyone who drops through
                }

            for (int x = 0; x <= 4; x++) { Solid(x, 1, 0); Solid(x, 1, 4); }
            for (int z = 0; z <= 4; z++) { Solid(0, 1, z); Solid(4, 1, z); }

            Assert.False(server.FloraCellOpensToVoidForTest(Get, 2, 1, 2),
                "A crop beside a stair shaft is above a deck, not above space.");

            // Same hole, but now nothing below it at all — that IS the void, and the crop must be refused.
            for (int x = 0; x <= 4; x++)
                for (int z = 0; z <= 4; z++)
                {
                    cells.Remove((x, -4, z));
                }

            Assert.True(server.FloraCellOpensToVoidForTest(Get, 2, 1, 2),
                "A crop beside a hole that opens onto nothing must still count as open to the void.");
        }
    }

    /// <summary>The promise of #626 end to end: walk into a village greenhouse, pick the berries, and they grow
    /// back. Settlements are mining-PROTECTED, so without the harvest exemption the player could only look at
    /// the crop — the whole feature would be scenery.</summary>
    [Fact]
    public void SettlementCrops_CanBeHarvestedDespiteProtection_AndGrowBack()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "settle" + seed));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "settle" + seed,
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = true,
            };

            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            var marker = server.SettlementMarkers.FirstOrDefault(m => m.Type == "greenhouse");
            var cropId = _content.GetBlock(CropKey)!.NumericId;
            var cell = marker.Type == null ? null : FindNear(server, marker.Pos.ToBlock(), cropId.Value);
            if (cell is null)
            {
                repo.Dispose();
                continue; // this world rolled no settlement (or none near enough) — try the next seed
            }

            using (repo)
            {
                var player = server.AddLocalPlayer("Gardener");
                player.State.Position = new Vector3f(cell.Value.X + 0.5f, cell.Value.Y + 0.5f, cell.Value.Z + 0.5f);

                server.MineBlock(player.State.PlayerId, cell.Value.X, cell.Value.Y, cell.Value.Z);
                Assert.True(server.World.GetBlock(cell.Value).IsAir,
                    "A greenhouse crop must be harvestable even though the settlement around it is protected.");

                // The berries land in the player's hands, and the bed grows a new bush on its own.
                server.TickForTest(31.0);
                Assert.Equal(cropId.Value, server.World.GetBlock(cell.Value).Value);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No settlement greenhouse found across 40 seeds.");
    }

    /// <summary>Scans a small box around a point for the first cell holding <paramref name="block"/>.</summary>
    private static Vector3i? FindNear(SvGameServer server, Vector3i around, ushort block)
    {
        const int R = 6;
        for (int dx = -R; dx <= R; dx++)
            for (int dy = -3; dy <= 4; dy++)
                for (int dz = -R; dz <= R; dz++)
                {
                    var p = new Vector3i(around.X + dx, around.Y + dy, around.Z + dz);
                    if (server.World.GetBlock(p).Value == block)
                    {
                        return p;
                    }
                }

        return null;
    }

    private SvGameServer StartedOnAPlanet(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "greenhouse"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "greenhouse",
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>The end-to-end promise of #628: harvest a crop ABOARD A STATION and it grows back. Void worlds
    /// used to skip the flora registry and the regrow tick entirely, so a picked plant stayed gone forever.</summary>
    [Fact]
    public void CropHarvestedAboardAStation_GrowsBackOnItsTray()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stationcrop"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "stationcrop",
                Seed = 42,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
                World = new WorldDescription { SpaceStations = Frequency.Frequent },
            };
            config.Rules.FreeSpaceFlight = true;

            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            var pilot = server.AddLocalPlayer("Gardener");
            server.EnterSpace("Gardener");
            var station = server.SpaceEntitiesFor("Gardener").First(e => e.Kind == CombatEntityKind.SpaceStation);
            server.ShipMove("Gardener", station.Position.X, station.Position.Y, station.Position.Z - 8f);
            server.BoardStation("Gardener", station.Id);
            Assert.StartsWith("station:", server.World.LocationId);

            // Plant a tray + crop right where the player stands: sealed deep inside the hull, so this exercises
            // the regrow path itself rather than wherever the generator happened to put the bay (that is the
            // structure test's job). The spawn cell is guaranteed to have solid ground beneath it.
            var cropId = _content.GetBlock(CropKey)!.NumericId;
            var trayId = _content.GetBlock(TrayKey)!.NumericId;
            var feet = pilot.State.Position.ToBlock();
            var cell = new Vector3i(feet.X, feet.Y, feet.Z);
            server.World.SetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z), trayId);
            server.World.SetBlock(cell, cropId);

            server.MineBlock(pilot.State.PlayerId, cell.X, cell.Y, cell.Z);
            Assert.True(server.World.GetBlock(cell).IsAir, "The crop should be gone right after harvest.");

            server.TickForTest(31.0); // > FloraRegrowSeconds
            Assert.Equal(cropId.Value, server.World.GetBlock(cell).Value);
        }
    }
}
