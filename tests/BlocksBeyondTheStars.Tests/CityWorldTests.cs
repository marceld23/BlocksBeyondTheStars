// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// The city world (#1793): Justus's lava desert with ONE gigantic walled city. The planet type is data gated on
/// generation 7; the city is composed by <see cref="CityGenerator"/> from 32×32 modules, stamped once around
/// landing pad 0, kept cool under its roofs, and guarded by the friendly G.D.S. machines.
/// </summary>
public sealed class CityWorldTests : IDisposable
{
    private const string Key = "gds_desert";
    private readonly string _root;
    private readonly GameContent _content;

    public CityWorldTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_city_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string planet, out SqliteWorldRepository repo, long seed = 20260912)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "city_" + planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "city_" + planet,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static IReadOnlyList<CityGenerator.OpenZone> StandardZones()
    {
        int c = CityGenerator.Footprint / 2;
        return new[]
        {
            new CityGenerator.OpenZone(c - 11, c - 11, c + 11, c + 11),          // the pad ring
            new CityGenerator.OpenZone(c - 56 - 14, c + 56 - 14, c - 56 + 14, c + 56 + 14), // the wreck crash site
        };
    }

    // ---------------- the planet type ----------------

    [Fact]
    public void GdsDesert_IsDataComplete_AndGatedToGenerationSeven()
    {
        var p = _content.GetPlanet(Key);
        Assert.NotNull(p);
        Assert.True(p!.Selectable && p.Exotic);
        Assert.Equal(WorldDescription.CityWorldsGeneration, p.MinTerrainGeneration);
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, WorldDescription.CityWorldsGeneration);
        Assert.Equal("gds", p.CityWorld);
        Assert.Equal("breathable", p.Atmosphere);
        Assert.Equal(0.0, p.FloraDensity);
        Assert.Equal(0.0, p.WaterAbundance ?? 0.0);
        Assert.True((p.LavaAbundance ?? 0.0) > 0.0, "the desert's seas and rivers are lava");
        Assert.True(p.HasTag(TerrainTag.Volcanic));
        Assert.Equal(0.0, p.RuinsBias);
        Assert.Equal(0.0, p.FactoriesBias);
        Assert.Equal(6, p.NpcOutfitRgb.Length);
        Assert.InRange(p.SpawnWeight, 1, 2); // rare: about one planet in a few galaxies

        string en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        foreach (var key in new[] { $"planet.{Key}.name", $"planet.{Key}.desc", "vega.hint.world.gds", "npc.role.guardian", "npc.greet.guardian", "dlg.gds_guardian.prompt", "dlg.gds_settler.prompt" })
        {
            Assert.Contains($"\"{key}\"", en);
            Assert.Contains($"\"{key}\"", de);
        }
    }

    [Fact]
    public void GdsDesert_EntersOnlyGenerationSevenGalaxies()
    {
        var older = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.CityWorldsGeneration - 1 };
        var current = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.CityWorldsGeneration };
        current.PlanetTypeFrequencies[Key] = Frequency.Frequent; // the type is deliberately rare — weight it up so 40 seeds are enough
        int rolled = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var body in new UniverseGenerator(seed, older, _content).Generate().Systems.SelectMany(s => s.Bodies))
            {
                Assert.NotEqual(Key, body.PlanetType);
            }

            rolled += new UniverseGenerator(seed, current, _content).Generate().Systems.SelectMany(s => s.Bodies).Count(b => b.PlanetType == Key);
        }

        Assert.True(rolled > 0, "no generation-7 galaxy rolled the city world in 40 seeds");
    }

    // ---------------- the composer ----------------

    [Fact]
    public void CityComposer_IsDeterministic_AndKeepsTheOpenZonesClear()
    {
        var zones = StandardZones();
        var a = CityGenerator.Generate(42, _content, zones);
        var b = CityGenerator.Generate(42, _content, zones);
        var other = CityGenerator.Generate(43, _content, zones);

        Assert.Equal(CityGenerator.Footprint, a.Width);
        Assert.Equal(CityGenerator.Footprint, a.Length);
        Assert.Equal(CityGenerator.Tier, a.Tier);
        Assert.False(a.Ruined);
        Assert.Equal(Hash(a), Hash(b));
        Assert.NotEqual(Hash(a), Hash(other));

        // Every open zone is air above the floor row — the ship and the wreck own that ground.
        foreach (var zone in zones)
        {
            for (int x = zone.MinX; x <= zone.MaxX; x++)
                for (int z = zone.MinZ; z <= zone.MaxZ; z++)
                    for (int y = 1; y < a.Height; y++)
                    {
                        Assert.Equal(0, a.Get(x, y, z));
                    }
        }

        // The wall stands, the gate is open, and both G.D.S. colours are on the walls.
        ushort wall = _content.GetBlock("iron_wall")!.NumericId.Value;
        int mid = a.Width / 2;
        Assert.Equal(wall, a.Get(mid + 20, 2, 1));
        Assert.Equal(wall, a.Get(1, 2, mid + 20));
        Assert.Equal(0, a.Get(mid, 2, 1));
        Assert.Equal(CityGenerator.Purple, a.GetModifier(mid + 20, 2, 1).Tint);
        Assert.Equal(CityGenerator.Red, a.GetModifier(mid + 20, 3, 1).Tint);
    }

    [Fact]
    public void CityComposer_PlacesEveryRole_AndTheMarkersTheServerNeeds()
    {
        var a = CityGenerator.Generate(7, _content, StandardZones());
        var roles = new HashSet<CityGenerator.Role>();
        for (int gx = 0; gx < CityGenerator.Modules; gx++)
            for (int gz = 0; gz < CityGenerator.Modules; gz++)
            {
                roles.Add(CityGenerator.RoleAt(gx, gz));
            }

        Assert.Equal(CityGenerator.Role.Plaza, CityGenerator.RoleAt(3, 3));
        Assert.Equal(CityGenerator.Role.Tower, CityGenerator.RoleAt(0, 0));
        Assert.Equal(CityGenerator.Role.Tower, CityGenerator.RoleAt(6, 6));
        Assert.Equal(CityGenerator.Role.Market, CityGenerator.RoleAt(2, 3));
        Assert.Equal(CityGenerator.Role.Hall, CityGenerator.RoleAt(3, 2));
        Assert.Superset(new HashSet<CityGenerator.Role> { CityGenerator.Role.Plaza, CityGenerator.Role.Market, CityGenerator.Role.Hall, CityGenerator.Role.Garden, CityGenerator.Role.Tower, CityGenerator.Role.Housing }, roles);

        int Count(string type) => a.Markers.Count(m => m.Type == type);
        Assert.True(Count("vendor") >= 2, "two vendors in the markets");
        Assert.True(Count("mission_board") >= 1);
        Assert.True(Count("npc") >= 40, $"a gigantic city has many residents, found {Count("npc")}");
        Assert.True(Count("guard_post") >= 16, $"guards at the gates, the towers, the hall and the plaza, found {Count("guard_post")}");
        Assert.True(Count("door_slide") >= 20, "every house has a door the server can hang");
        Assert.True(a.BuildingCount >= 30);

        // Guard posts sit on the footprint's rim too: the G.D.S. patrol the ground around the city.
        Assert.Contains(a.Markers, m => m.Type == "guard_post" && (m.LocalPos.X == 0 || m.LocalPos.Z == 0 || m.LocalPos.X == a.Width - 1 || m.LocalPos.Z == a.Length - 1));

        // Gardens carry water and trees — the only green on the planet.
        ushort water = _content.GetBlock("water")!.NumericId.Value;
        ushort log = _content.GetBlock("wood_log")!.NumericId.Value;
        int pools = 0, trunks = 0;
        for (int x = 0; x < a.Width; x++)
            for (int z = 0; z < a.Length; z++)
            {
                if (a.Get(x, 0, z) == water) pools++;
                if (a.Get(x, 2, z) == log) trunks++;
            }

        Assert.True(pools >= 64, $"at least one 8×8 pool, found {pools} water cells");
        Assert.True(trunks >= 12, $"trees in the gardens, found {trunks} trunks");
    }

    private static ulong Hash(SettlementStructure s)
    {
        ulong h = 1469598103934665603UL;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    h = (h ^ s.Get(x, y, z)) * 1099511628211UL;
                    h = (h ^ (ulong)(uint)s.GetModifier(x, y, z).Tint) * 1099511628211UL;
                }

        foreach (var m in s.Markers)
        {
            h = (h ^ (ulong)(uint)string.GetHashCode(m.Type, StringComparison.Ordinal)) * 1099511628211UL;
            h = (h ^ (ulong)(uint)(m.LocalPos.X * 73856093 ^ m.LocalPos.Y * 19349663 ^ m.LocalPos.Z * 83492791)) * 1099511628211UL;
        }

        return h;
    }

    // ---------------- the server ----------------

    [Fact]
    public void CityWorld_StampsOneCity_AroundPadZero_WithGuardians_Wardrobe_ShadeAndLines()
    {
        var server = Start(Key, out var repo);
        using (repo)
        {
            Assert.Equal(Key, server.World.Planet.Key);

            // Exactly one settlement: the metropolis, pinned as index 0 with the composer as its template.
            var settlements = server.SettlementsForTest;
            Assert.Single(settlements);
            Assert.Equal(CityGenerator.Tier, server.SettlementTiersForTest[0]);
            Assert.False(settlements[0].Ruined);
            Assert.Equal(CityGenerator.Footprint - 1, settlements[0].MaxX - settlements[0].MinX);
            var record = Assert.Single(server.PlacementRecordsForTest.Where(r => r.Kind == "settlement"));
            Assert.True(record.Placed);
            Assert.Equal("city:gds", record.Template);
            Assert.StartsWith("G.D.S. ", record.Name, StringComparison.Ordinal);

            // Centred on pad 0 — the ship comes down inside the walls.
            var (padX, _, padZ) = server.LandingPadForTest(0);
            var city = server.CityFootprintForTest;
            Assert.NotNull(city);
            Assert.InRange(padX, city!.Value.MinX + 100, city.Value.MaxX - 100);
            Assert.InRange(padZ, city.Value.MinZ + 100, city.Value.MaxZ - 100);

            // Residents: guardians in the G.D.S. look, everyone else dressed from the planet's wardrobe.
            var looks = server.NpcLooksForTest;
            var guardians = looks.Where(n => n.Role == "guardian").ToList();
            Assert.True(guardians.Count >= 16, $"expected the guard posts to spawn guardians, got {guardians.Count}");
            Assert.All(guardians, g =>
            {
                Assert.True(g.IsRobot);
                Assert.Equal("gds_guard", g.Look);
                Assert.Equal(0x3A1F5Cu, g.SkinRgb);
            });
            var wardrobe = _content.GetPlanet(Key)!.NpcOutfitRgb;
            var civilians = looks.Where(n => n.Role != "guardian").ToList();
            Assert.True(civilians.Count >= 40, $"a gigantic city has many residents, got {civilians.Count}");
            Assert.All(civilians, c => Assert.Contains(c.OutfitRgb, wardrobe));
            Assert.All(civilians, c => Assert.Equal(string.Empty, c.Look));

            // The map names the city; VEGA's flavour id maps the type to its own line.
            var player = server.AddLocalPlayer("Justus");
            Assert.Contains(server.PlanetPoisForTest(player.State.PlayerId), poi => poi.Type == "settlement" && poi.Name.StartsWith("G.D.S. ", StringComparison.Ordinal));

            // Cool under a roof inside the walls, hot everywhere open: a settler at home is sheltered, a guard on
            // the open plaza is not — and a spot far outside the walls never is, roof or not.
            var homes = server.NpcSnapshots;
            var settlerHomes = homes.Where(n => n.Role == "settler").Select(n => n.Home).ToList();
            var guardHomes = homes.Where(n => n.Role == "guardian").Select(n => n.Home).ToList();
            Assert.Contains(settlerHomes, h => server.InCityShelterForTest(new Vector3f(h.X, h.Y + 0.5f, h.Z)));
            Assert.Contains(guardHomes, h => !server.InCityShelterForTest(new Vector3f(h.X, h.Y + 0.5f, h.Z)));
            Assert.False(server.InCityShelterForTest(new Vector3f(city.Value.MaxX + 400, 60f, city.Value.MaxZ + 400)));

            // The G.D.S. lines: a guardian opens with the creed, a settler with the city's mystery.
            var guard = server.NpcRosterForTest().First(n => n.Role == "guardian");
            player.State.Position = homes.First(n => n.Id == guard.Id).Home;
            server.TalkToNpcForTest(player.State.PlayerId, guard.Id);
            Assert.Equal(("gds_guardian", 0), server.ActiveDialogForTest(player.State.PlayerId));

            var settler = server.NpcRosterForTest().First(n => n.Role == "settler" && n.CharacterId.Length == 0);
            player.State.Position = homes.First(n => n.Id == settler.Id).Home;
            server.TalkToNpcForTest(player.State.PlayerId, settler.Id);
            Assert.Equal(("gds_settler", 0), server.ActiveDialogForTest(player.State.PlayerId));
        }
    }

    [Fact]
    public void OtherWorlds_KeepTheirSettlements_AndNeverOfferTheGdsLines()
    {
        var server = Start("jungle", out var repo, seed: 12345);
        using (repo)
        {
            Assert.Null(server.CityFootprintForTest);
            Assert.DoesNotContain(CityGenerator.Tier, server.SettlementTiersForTest);
            Assert.DoesNotContain(server.NpcLooksForTest, n => n.Role == "guardian" || n.Look.Length > 0);

            var settler = server.NpcRosterForTest().FirstOrDefault(n => n.Role == "settler" && n.CharacterId.Length == 0);
            if (settler.Id != 0)
            {
                var player = server.AddLocalPlayer("Marcel");
                player.State.Position = server.NpcSnapshots.First(n => n.Id == settler.Id).Home;
                server.TalkToNpcForTest(player.State.PlayerId, settler.Id);
                var active = server.ActiveDialogForTest(player.State.PlayerId);
                Assert.True(active is null || !active.Value.Key.StartsWith("gds_", StringComparison.Ordinal), "the G.D.S. lines belong to the city world only");
            }
        }
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
            // best effort
        }
    }
}
