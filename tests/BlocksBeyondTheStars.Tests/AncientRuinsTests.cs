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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The buried G.D.S. ruins of terrain generation 35 (#3334, with the placement parts of #3336, #3338 and #3342): an underground
/// city kit composed into an abandoned, broken, Ressaw-flooded ruin; always under the city of a <c>gds_desert</c> world, rarely
/// under any other planet; seated deep, sealed in a stone shell, stamped once, with at least four crates.
/// </summary>
public sealed class AncientRuinsTests : IDisposable
{
    private const string Kit = "ruin_gds_1";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_gdsruin_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private SvGameServer Start(string planet, out SqliteWorldRepository repo, long seed = 20261107,
        int generation = WorldDescription.CurrentTerrainGeneration, string? name = null, bool settlements = true, GameContent? content = null)
    {
        name ??= $"ruin_{planet}_{generation}_{seed}";
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, content ?? Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static StructureKit RuinKit() => Content.KitByKey(Kit)!;

    private static List<StructureTemplate> ModulePool() => Content.SettlementTemplates.Where(t => t.IsModule).ToList();

    private static AncientRuinLayout Compose(long seed, int crossI = 1, int crossJ = 1, IList<string>? composition = null)
    {
        var kit = RuinKit();
        return AncientRuinGenerator.Compose(seed, Content, CityLayoutSpec.FromKit(kit), kit, ModulePool(),
            composition ?? new List<string>(), crossI, crossJ);
    }

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    private static ulong Hash(AncientRuinLayout r)
    {
        ulong h = 1469598103934665603UL;
        for (int x = 0; x < r.Width; x++)
            for (int y = 0; y < r.Height; y++)
                for (int z = 0; z < r.Length; z++)
                {
                    var (tint, glow) = r.GetModifier(x, y, z);
                    h = (h ^ r.Get(x, y, z) ^ (ulong)(uint)tint ^ ((ulong)(uint)r.GetShape(x, y, z) << 24) ^ ((ulong)(uint)glow << 40)) * 1099511628211UL;
                }

        foreach (var c in r.Caches)
        {
            h = (h ^ (ulong)(uint)(c.X * 73856093 ^ c.Y * 19349663 ^ c.Z * 83492791)) * 1099511628211UL;
        }

        return h;
    }

    // ---------------------------------------------------------------- the content

    [Fact]
    public void TheKitAndItsModules_AreUnderground_AndNeverReachTheSurfacePools()
    {
        var kit = RuinKit();
        Assert.True(kit.Underground);
        Assert.Equal(StructureKit.KindCity, kit.KindOrDefault);
        Assert.Empty(kit.PlanetTypes);
        Assert.Contains(kit, Content.UndergroundKitsFor(null, "gds_desert"));
        Assert.Contains(kit, Content.UndergroundKitsFor(null, "jungle"));
        Assert.DoesNotContain(Content.KitsFor(StructureKit.KindCity, null, null, "gds_desert"), k => k.Key == Kit);

        var spec = CityLayoutSpec.FromKit(kit);
        foreach (var entry in kit.Entries)
        {
            var m = Content.TemplateByKey(StructureKit.KindSettlement, entry.Module);
            Assert.True(m != null, $"kit entry '{entry.Module}' has no module");
            Assert.StartsWith("gds_ruin_", m!.Key, StringComparison.Ordinal);
            Assert.Equal(Kit, m.Kit);
            Assert.True(m.Underground, $"{m.Key} must be flagged underground");
            Assert.Equal(StructureRoles.MetropolisTier, m.Tier);
            Assert.True(StructureRoles.IsCityRole(m.Function), $"{m.Key}: {m.Function}");
            Assert.True(m.Width <= spec.DistrictSize && m.Length <= spec.DistrictSize && m.Height <= spec.Height - 1, $"{m.Key} does not fit its district");
            Assert.DoesNotContain(m.Cells, c => c.Kind == "marker" && c.Id is "npc" or "vendor" or "mission_board" or "guard_post");
            Assert.Contains(m.Cells, c => c.Kind == "marker" && c.Id == "loot");
        }

        // The surface module pool of the G.D.S. world (and of any world) never offers a ruin district.
        Assert.DoesNotContain(Content.SettlementModulesFor(null, "gds_desert"), t => t.Key.StartsWith("gds_ruin_", StringComparison.Ordinal));
        Assert.DoesNotContain(Content.SettlementModulesFor(null, null), t => t.Key.StartsWith("gds_ruin_", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSurfaceGdsCity_NeverComposesARuinDistrict()
    {
        var surfaceModules = Content.SettlementModulesFor(null, "gds_desert");
        var cityKit = Assert.Single(Content.KitsFor(StructureKit.KindCity, null, null, "gds_desert"));
        for (long seed = 1; seed <= 6; seed++)
        {
            var composition = new List<string>();
            CityGenerator.Generate(seed, Content, Array.Empty<CityGenerator.OpenZone>(), surfaceModules, 1.0, composition, null,
                CityLayoutSpec.FromKit(cityKit), cityKit, ModulePool());
            Assert.DoesNotContain(composition, k => k.StartsWith("gds_ruin_", StringComparison.Ordinal));

            var procedural = new List<string>();
            CityGenerator.Generate(seed, Content, Array.Empty<CityGenerator.OpenZone>(), surfaceModules, 1.0, procedural);
            Assert.DoesNotContain(procedural, k => k.StartsWith("gds_ruin_", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------- the composer

    [Fact]
    public void TheComposer_IsDeterministic_FillsEveryDistrictWithARuinModule_AndReplaysItsPins()
    {
        var a = Compose(424242);
        var b = Compose(424242);
        Assert.Equal(Hash(a), Hash(b));
        Assert.NotEqual(Hash(a), Hash(Compose(424243)));

        var spec = a.Layout;
        Assert.Equal(spec.Grid * spec.Grid, a.Composition.Count);
        Assert.All(a.Composition, k => Assert.StartsWith("gds_ruin_", k, StringComparison.Ordinal));
        Assert.Equal(Kit, a.KitKey);
        Assert.Equal(spec.Footprint, a.Width);
        Assert.Equal(spec.Height + AncientRuinGenerator.Headroom, a.Height);

        var replay = Compose(424242, composition: new List<string>(a.Composition));
        Assert.Equal(Hash(a), Hash(replay));
    }

    [Fact]
    public void EveryRuin_HasAtLeastFourDryCrates_TheFirstFourNearestTheEntrance()
    {
        ushort crate = Id("crate"), ressaw = Id("ressaw");
        for (long seed = 1; seed <= 12; seed++)
        {
            var r = Compose(seed * 7919, 1 + (int)(seed % 2), 1 + (int)(seed / 2 % 2));
            Assert.True(r.Caches.Count >= AncientRuinGenerator.MinCaches, $"seed {seed}: {r.Caches.Count} crates");
            Assert.Equal(r.Caches.Count, r.Caches.Distinct().Count());
            foreach (var c in r.Caches)
            {
                Assert.Equal(crate, r.Get(c.X, c.Y, c.Z));
                ushort below = r.Get(c.X, c.Y - 1, c.Z);
                Assert.True(below != ressaw && Content.BlockById(new BlockId(below)) is { Solid: true }, $"seed {seed}: crate at {c} is not on dry floor");
            }

            // The diving suit (crates 0–3) lies on the ground floor wherever the ruin has enough ground-floor crates.
            int ground = r.Caches.Count(c => c.Y == 1);
            for (int n = 0; n < Math.Min(ground, AncientRuinGenerator.MinCaches); n++)
            {
                Assert.Equal(1, r.Caches[n].Y);
            }
        }
    }

    [Fact]
    public void TheRuin_IsFlooded_WithOreUnderTheRessaw_GlowingWalls_AndAnOpenEntrance()
    {
        ushort ressaw = Id("ressaw"), ladder = Id("ladder"), glow = Id("ressaw_block");
        for (long seed = 1; seed <= 8; seed++)
        {
            var r = Compose(seed * 104729);
            Assert.NotEmpty(r.BasinColumns);
            Assert.All(r.BasinColumns, c => Assert.Equal(ressaw, r.Get(c.X, 0, c.Z)));
            Assert.InRange(r.OreCells.Count, AncientRuinGenerator.MinOre, AncientRuinGenerator.MaxOre);
            var basins = r.BasinColumns.ToHashSet();
            Assert.All(r.OreCells, o =>
            {
                Assert.Equal(-AncientRuinGenerator.BasinDepth - 1, o.Y);
                Assert.Contains((o.X, o.Z), basins); // the ore lies right under a flooded column
            });

            int glowing = 0;
            for (int x = 0; x < r.Width; x++)
                for (int y = 1; y < r.Height; y++)
                    for (int z = 0; z < r.Length; z++)
                    {
                        glowing += r.Get(x, y, z) == glow ? 1 : 0;
                    }

            Assert.True(glowing >= AncientRuinGenerator.MinRessawBlocks, $"seed {seed}: {glowing} Ressaw blocks");
            Assert.Equal(glowing, r.RessawBlocks);

            // The tube: a ladder from the floor to the ceiling, a doorway onto the street, nothing in front of it.
            var (sx, sz) = r.Shaft;
            for (int y = 1; y < r.Height; y++)
            {
                Assert.Equal(ladder, r.Get(sx, y, sz));
            }

            int dx = r.DoorFace == 2 ? 1 : 0, dz = r.DoorFace == 4 ? 1 : 0;
            Assert.Equal(0, r.Get(sx + dx, 1, sz + dz));
            Assert.Equal(0, r.Get(sx + dx, 2, sz + dz));
            Assert.Equal(0, r.Get(sx + 2 * dx, 1, sz + 2 * dz));
        }
    }

    [Fact]
    public void TheRemains_LieInCorners_AndTheSkull_IsTheRarest()
    {
        ushort skull = Id("gds_skull"), helmet = Id("gds_diving_helmet"), head = Id("gds_guardian_head");
        int skulls = 0, helmets = 0, heads = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            var r = Compose(seed * 15485863);
            Assert.NotEmpty(r.Props);
            Assert.True(r.Props.Count <= AncientRuinGenerator.MaxProps);
            foreach (var (cell, block) in r.Props)
            {
                Assert.Equal(block, r.Get(cell.X, cell.Y, cell.Z));
                Assert.True(ShapeCode.IsCube(r.GetShape(cell.X, cell.Y, cell.Z)), "a prop stays a plain cube (its front rides in the descriptor)");
                skulls += block == skull ? 1 : 0;
                helmets += block == helmet ? 1 : 0;
                heads += block == head ? 1 : 0;
            }
        }

        Assert.True(helmets > 0 && heads > 0 && skulls > 0, $"helmets {helmets}, heads {heads}, skulls {skulls}");
        Assert.True(skulls < helmets && skulls < heads, $"the skull must be the rarest: helmets {helmets}, heads {heads}, skulls {skulls}");
    }

    // ---------------------------------------------------------------- the roll

    [Fact]
    public void AnOrdinaryPlanet_RollsARuinRarely_AndDeterministically()
    {
        int hits = 0, total = 0;
        for (long seed = 1; seed <= 200; seed++)
        {
            for (int body = 0; body < 20; body++)
            {
                string location = $"sys{seed % 7}-p{body}";
                bool rolled = SvGameServer.AncientRuinRolls(seed, location, 1.0);
                Assert.Equal(rolled, SvGameServer.AncientRuinRolls(seed, location, 1.0));
                hits += rolled ? 1 : 0;
                total++;
                Assert.False(SvGameServer.AncientRuinRolls(seed, location, 0.0)); // the Vaults option Off buries none
            }
        }

        double rate = hits / (double)total;
        Assert.InRange(rate, 0.025, 0.055);
    }

    [Fact]
    public void AWorldOfGeneration34_NeverGetsARuin_NotEvenTheCityWorld()
    {
        var server = Start("gds_desert", out var repo, generation: WorldDescription.AncientRuinsGeneration - 1);
        using (repo)
        {
            Assert.NotNull(server.CityFootprintForTest); // the city stands — gen 7 stuff
            Assert.Null(server.AncientRuinForTest);
            Assert.DoesNotContain(server.PlacementRecordsForTest, r => r.Kind == SvGameServer.UndergroundKind);
        }
    }

    // ---------------------------------------------------------------- the city world

    [Fact]
    public void TheCityWorld_AlwaysHidesARuin_UnderItsCity_SealedAndStampedOnce()
    {
        var server = Start("gds_desert", out var repo, name: "gds_once");
        Vector3i mined;
        SvGameServer.AncientRuinSite site;
        using (repo)
        {
            site = server.AncientRuinForTest!;
            Assert.NotNull(site);
            Assert.True(site.Hatch);
            Assert.True(site.Gds);
            Assert.Equal(SvGameServer.UndergroundCitySlot, site.Slot);
            Assert.Equal(Kit, site.Kit);

            // Under the city, the hatch in its street: glass over the ladder in a ring of red plates.
            var city = server.CityFootprintForTest!.Value;
            Assert.InRange(site.Origin.X, city.MinX, city.MaxX - site.Width + 1);
            Assert.InRange(site.Origin.Z, city.MinZ, city.MaxZ - site.Length + 1);
            var cityRec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == "settlement");
            Assert.Equal(cityRec.GroundY, site.Entrance.Y);
            Assert.True(site.Origin.Y + site.Height <= cityRec.GroundY - SvGameServer.AncientRuinCover);
            Assert.Equal(Id("glass"), server.World.GetBlock(site.Entrance).Value);
            Assert.Equal(Id("ladder"), server.World.GetBlock(new Vector3i(site.Entrance.X, site.Entrance.Y - 1, site.Entrance.Z)).Value);
            Assert.Equal(Id("iron_wall"), server.World.GetBlock(new Vector3i(site.Entrance.X + 1, site.Entrance.Y, site.Entrance.Z)).Value);
            Assert.DoesNotContain(cityRec.Composition ?? new List<string>(), k => k.StartsWith("gds_ruin_", StringComparison.Ordinal));

            var layout = server.ComposeAncientRuinForTest(site.Slot)!;
            AssertSealed(server, layout, site);
            AssertFloodedWithOre(server, layout, site);
            AssertCrates(server, site);
            AssertAbandoned(server, site);

            var wall = FirstWall(server, site);
            server.World.SetBlock(wall, BlockId.Air); // a player mined a wall of the ruin
            mined = wall;
            server.Stop();
        }

        // Re-entry: the same ruin re-derived from its record, nothing re-stamped — the mined wall stays mined.
        var again = Start("gds_desert", out var repo2, name: "gds_once");
        using (repo2)
        {
            var site2 = again.AncientRuinForTest!;
            Assert.Equal(site.Origin, site2.Origin);
            Assert.Equal(site.Entrance, site2.Entrance);
            Assert.Equal(site.Caches, site2.Caches);
            Assert.True(again.World.GetBlock(mined).IsAir, "a mined ruin wall came back on re-entry");
            Assert.All(site.Caches, cell => Assert.Single(again.Containers, c => c.Position.Equals(cell)));
        }
    }

    [Fact]
    public void AnOrdinaryPlanet_CarriesItsRuin_UnderAVentTower_AwayFromThePads_AndVegaPointsItOut()
    {
        var server = Start("jungle", out var repo, seed: 12345, settlements: false);
        using (repo)
        {
            int npcsBefore = server.NpcRosterForTest().Count;
            Assert.True(server.ForceAncientRuinForTest() || server.AncientRuinForTest != null);
            var site = server.AncientRuinForTest!;
            Assert.False(site.Hatch);
            Assert.True(site.Gds); // the shipped content's only underground kit
            Assert.Equal(SvGameServer.UndergroundRollSlot, site.Slot);
            Assert.Equal(npcsBefore, server.NpcRosterForTest().Count); // nobody lives down there

            var (padX, _, padZ) = server.LandingPadForTest(0);
            double fromPad = Math.Sqrt(Math.Pow(site.Entrance.X - padX, 2) + Math.Pow(site.Entrance.Z - padZ, 2));
            Assert.True(fromPad > 100, $"the ruin's entrance is only {fromPad:F0} blocks from the landing pad");
            Assert.True(site.Origin.Y + site.Height <= site.Entrance.Y - SvGameServer.AncientRuinCover);

            // The vent tower: purple walls, a red light on top, the ladder inside.
            var e = site.Entrance;
            Assert.Equal(Id("light_red"), server.World.GetBlock(new Vector3i(e.X, e.Y + SvGameServer.AncientRuinTowerHeight + 1, e.Z)).Value);
            Assert.Equal(Id("ladder"), server.World.GetBlock(new Vector3i(e.X, e.Y + 1, e.Z)).Value);

            var layout = server.ComposeAncientRuinForTest(site.Slot)!;
            AssertSealed(server, layout, site);
            AssertFloodedWithOre(server, layout, site);
            AssertCrates(server, site);

            // VEGA: nothing far away, the hint once near the tower.
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(e.X + 80, e.Y + 1, e.Z);
            server.TickForTest(1.1);
            Assert.DoesNotContain("vega:hint:gds_ruin", p.State.Milestones);
            p.State.Position = new Vector3f(e.X + 6, e.Y + 1, e.Z);
            server.TickForTest(1.1);
            Assert.Contains("vega:hint:gds_ruin", p.State.Milestones);
        }
    }

    // ---------------------------------------------------------------- authored underground structures

    /// <summary>The shipped content with the shipped underground kits taken out and the given authored ones put in, so they are
    /// the only candidates of the roll slot.</summary>
    private static GameContent ContentWith(StructureKit? kit, StructureTemplate? template)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        if (template != null)
        {
            content.SetStructureTemplates(content.StationTemplates, content.SettlementTemplates.Concat(new[] { template }).ToList());
        }

        var kits = content.StructureKits.Where(k => !k.Underground).ToList();
        if (kit != null)
        {
            kits.Add(kit);
        }

        content.SetStructureKits(kits);
        return content;
    }

    /// <summary>A 9 × 5 × 9 crypt an author flagged Underground: stone floor, iron walls (one purple), a hinged door on −Z,
    /// a roof, a loot marker and a resident's marker.</summary>
    private static StructureTemplate Crypt()
    {
        var cells = new List<TemplateCell>();
        for (int x = 0; x < 9; x++)
            for (int z = 0; z < 9; z++)
            {
                cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Id = "stone" });
                cells.Add(new TemplateCell { X = x, Y = 4, Z = z, Id = "iron_wall" });
                bool rim = x is 0 or 8 || z is 0 or 8;
                for (int y = 1; y <= 3 && rim; y++)
                {
                    if (!(x == 4 && z == 0 && y <= 2))
                    {
                        cells.Add(new TemplateCell { X = x, Y = y, Z = z, Id = "iron_wall", Tint = x == 0 && z == 4 && y == 2 ? 0x3A1F5C : 0 });
                    }
                }
            }

        cells.Add(new TemplateCell { X = 4, Y = 1, Z = 0, Kind = "marker", Id = "door_hinge" });
        cells.Add(new TemplateCell { X = 2, Y = 1, Z = 6, Kind = "marker", Id = "loot" });
        cells.Add(new TemplateCell { X = 6, Y = 1, Z = 6, Kind = "marker", Id = "npc" });
        return new StructureTemplate
        {
            Key = "crypt_test",
            Name = "Test Crypt",
            Tier = "village",
            Kind = "settlement",
            Underground = true,
            Width = 9,
            Height = 5,
            Length = 9,
            Cells = cells,
        };
    }

    [Fact]
    public void AnUndergroundWholeTemplate_IsStampedAsBuilt_UnderAVentTower_WithItsDoorAndItsLoot()
    {
        var crypt = Crypt();
        var server = Start("jungle", out var repo, seed: 12345, name: "crypt", settlements: false, content: ContentWith(null, crypt));
        using (repo)
        {
            int npcsBefore = server.NpcRosterForTest().Count;
            Assert.True(server.ForceAncientRuinForTest() || server.AncientRuinForTest != null);
            var site = server.AncientRuinForTest!;
            Assert.False(site.Gds);
            Assert.False(site.Hatch);
            Assert.Equal("template:crypt_test", site.Source);
            Assert.Empty(site.Caches);
            Assert.Equal(9 + 2 * AncientRuinGenerator.EnclosureMargin, site.Width);

            // As authored: the purple wall cell, the walls intact (no ruin pass), the door hung, the loot spawned, nobody home.
            var o = site.Origin;
            int m = AncientRuinGenerator.EnclosureMargin;
            var purple = new Vector3i(o.X + m, o.Y + 2, o.Z + m + 4);
            Assert.Equal(Id("iron_wall"), server.World.GetBlock(purple).Value);
            Assert.Equal(0x3A1F5C, server.World.GetModifier(purple).Tint);
            int walls = crypt.Cells.Count(c => c.Kind == "block" && c.Id == "iron_wall");
            int stamped = 0;
            for (int x = 0; x < 9; x++)
                for (int y = 0; y < 5; y++)
                    for (int z = 0; z < 9; z++)
                    {
                        stamped += server.World.GetBlock(new Vector3i(o.X + m + x, o.Y + y, o.Z + m + z)).Value == Id("iron_wall") ? 1 : 0;
                    }

            Assert.Equal(walls, stamped);
            Assert.Contains(server.DoorSnapshots, d => Math.Abs(d.Pos.X - (o.X + m + 4.5f)) < 1.01f && Math.Abs(d.Pos.Z - (o.Z + m + 0.5f)) < 1.01f);
            var lootCell = new Vector3i(o.X + m + 2, o.Y + 1, o.Z + m + 6);
            Assert.Contains(server.Containers, c => c.Position.Equals(lootCell));
            Assert.Equal(npcsBefore, server.NpcRosterForTest().Count);

            var layout = server.ComposeAncientRuinForTest(site.Slot)!;
            Assert.False(layout.IsGdsRuin);
            Assert.Empty(layout.BasinColumns);
            AssertSealed(server, layout, site);

            // A plain tower with a warm light, and VEGA's own line for it.
            var e = site.Entrance;
            Assert.Equal(Id("strip_light_warm"), server.World.GetBlock(new Vector3i(e.X, e.Y + SvGameServer.AncientRuinTowerHeight + 1, e.Z)).Value);
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(e.X + 6, e.Y + 1, e.Z);
            server.TickForTest(1.1);
            Assert.Contains("vega:hint:underground", p.State.Milestones);
        }
    }

    [Fact]
    public void AnUndergroundSettlementKit_IsComposedLikeOnTheSurface_AndStampedUnderground()
    {
        var kit = new StructureKit
        {
            Key = "crypt_village_test",
            Name = "Test Underground Village",
            Kind = StructureKit.KindSettlement,
            Tier = "village",
            Underground = true,
            ColsMin = 2,
            ColsMax = 2,
            RowsMin = 2,
            RowsMax = 2,
            PlotStride = 10,
            Building = 8,
            ModulesOnly = true,
            Entries =
            {
                new KitEntry { Module = "village_market_1", Required = true, Min = 1, Max = 1 },
                new KitEntry { Module = "village_market_1_alien", Required = true, Min = 1, Max = 1 },
                new KitEntry { Module = "village_house_1", Max = 4, Weight = 2 },
                new KitEntry { Module = "village_house_1_alien", Max = 4, Weight = 2 },
            },
        };
        var server = Start("jungle", out var repo, seed: 12345, name: "uvillage", settlements: false, content: ContentWith(kit, null));
        using (repo)
        {
            int npcsBefore = server.NpcRosterForTest().Count;
            Assert.True(server.ForceAncientRuinForTest() || server.AncientRuinForTest != null);
            var site = server.AncientRuinForTest!;
            Assert.False(site.Gds);
            Assert.Equal("settlement:village", site.Source);
            Assert.Equal(kit.Key, site.Kit);
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == SvGameServer.UndergroundKind && r.Placed);
            Assert.Contains(rec.Composition!, k => k.StartsWith("village_market_1", StringComparison.Ordinal)); // the human or the alien variant
            Assert.True(SettlementLayoutSpec.TryParse(rec.KitLayout, out _));

            // Every block of the composed village stands where it was composed, under the ground.
            var layout = server.ComposeAncientRuinForTest(site.Slot)!;
            var source = layout.Source!;
            var o = site.Origin;
            int m = layout.SourceOffset, compared = 0;
            for (int x = 0; x < source.Width; x += 3)
                for (int y = 1; y < source.Height; y++)
                    for (int z = 0; z < source.Length; z += 3)
                    {
                        ushort b = source.Get(x, y, z);
                        if (b != 0)
                        {
                            Assert.Equal(b, server.World.GetBlock(new Vector3i(o.X + m + x, o.Y + y, o.Z + m + z)).Value);
                            compared++;
                        }
                    }

            Assert.True(compared > 50, $"only {compared} blocks compared");
            Assert.True(o.Y + site.Height <= site.Entrance.Y - SvGameServer.AncientRuinCover);
            AssertSealed(server, layout, site);
            Assert.Contains(server.DoorSnapshots, d => d.Pos.X >= o.X && d.Pos.X < o.X + site.Width && d.Pos.Z >= o.Z && d.Pos.Z < o.Z + site.Length);
            Assert.Equal(npcsBefore, server.NpcRosterForTest().Count); // nobody moves in down there
        }
    }

    // ---------------------------------------------------------------- checks

    /// <summary>No open cell of the ruin (box, basins, shaft) touches air, a liquid or loose ground outside it — except the
    /// cell above the top of the shaft (the entrance).</summary>
    private static void AssertSealed(SvGameServer server, AncientRuinLayout layout, SvGameServer.AncientRuinSite site)
    {
        var world = server.World;
        var o = site.Origin;
        var basins = layout.BasinColumns.ToHashSet();
        var (sx, sz) = layout.Shaft;
        int shaftTop = site.Entrance.Y - 1;
        bool InCavity(int x, int y, int z)
        {
            int lx = x - o.X, ly = y - o.Y, lz = z - o.Z;
            return (lx >= 0 && lz >= 0 && lx < site.Width && lz < site.Length && ly >= 0 && ly < site.Height)
                   || (ly < 0 && ly >= -AncientRuinGenerator.BasinDepth && basins.Contains((lx, lz)))
                   || (lx == sx && lz == sz && ly >= site.Height && y <= shaftTop);
        }

        bool Holds(Vector3i c) => Content.BlockById(world.GetBlock(c)) is { Solid: true, Liquid: false, Granular: false };

        var leaks = new List<string>();
        void Check(int x, int y, int z)
        {
            if (Holds(new Vector3i(x, y, z)))
            {
                return;
            }

            foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            {
                var n = new Vector3i(x + dx, y + dy, z + dz);
                bool entrance = x == o.X + sx && z == o.Z + sz && y == shaftTop && dy == 1;
                if (!entrance && !InCavity(n.X, n.Y, n.Z) && !Holds(n) && leaks.Count < 10)
                {
                    leaks.Add($"{n} = {Content.BlockById(world.GetBlock(n))?.Key ?? "air"} beside ({x},{y},{z})");
                }
            }
        }

        for (int x = 0; x < site.Width; x++)
            for (int y = 0; y < site.Height; y++)
                for (int z = 0; z < site.Length; z++)
                {
                    Check(o.X + x, o.Y + y, o.Z + z);
                }

        foreach (var (bx, bz) in basins)
        {
            for (int y = -1; y >= -AncientRuinGenerator.BasinDepth; y--)
            {
                Check(o.X + bx, o.Y + y, o.Z + bz);
            }
        }

        for (int y = o.Y + site.Height; y <= shaftTop; y++)
        {
            Check(o.X + sx, y, o.Z + sz);
        }

        Assert.True(leaks.Count == 0, "the ruin is open to the outside:\n" + string.Join("\n", leaks));
    }

    private static void AssertFloodedWithOre(SvGameServer server, AncientRuinLayout layout, SvGameServer.AncientRuinSite site)
    {
        ushort ressaw = Id("ressaw"), ore = Id("ressaw_crystal_ore");
        var o = site.Origin;
        Assert.NotEmpty(layout.OreCells);
        foreach (var c in layout.OreCells)
        {
            Assert.Equal(ore, server.World.GetBlock(new Vector3i(o.X + c.X, o.Y + c.Y, o.Z + c.Z)).Value);
            Assert.Equal(ressaw, server.World.GetBlock(new Vector3i(o.X + c.X, o.Y + c.Y + 1, o.Z + c.Z)).Value);
        }

        foreach (var (x, z) in layout.BasinColumns.Take(20))
        {
            for (int y = 0; y >= -AncientRuinGenerator.BasinDepth; y--)
            {
                Assert.Equal(ressaw, server.World.GetBlock(new Vector3i(o.X + x, o.Y + y, o.Z + z)).Value);
            }
        }
    }

    private static void AssertCrates(SvGameServer server, SvGameServer.AncientRuinSite site)
    {
        Assert.True(site.Caches.Count >= AncientRuinGenerator.MinCaches);
        for (int n = 0; n < site.Caches.Count; n++)
        {
            var cell = site.Caches[n];
            Assert.Equal(Id("crate"), server.World.GetBlock(cell).Value);
            var container = Assert.Single(server.Containers, c => c.Id.EndsWith($"gds_cache_{n}_{cell.X}_{cell.Y}_{cell.Z}", StringComparison.Ordinal));
            Assert.Equal(cell, container.Position);
        }
    }

    /// <summary>Nobody lives in the ruin: no resident's home, no door, no vendor down there.</summary>
    private static void AssertAbandoned(SvGameServer server, SvGameServer.AncientRuinSite site)
    {
        var o = site.Origin;
        foreach (var npc in server.NpcSnapshots)
        {
            bool inside = npc.Home.X >= o.X && npc.Home.X < o.X + site.Width && npc.Home.Z >= o.Z && npc.Home.Z < o.Z + site.Length
                          && npc.Home.Y < o.Y + site.Height + 1;
            Assert.False(inside, $"NPC {npc.Id} ({npc.Role}) lives in the ruin at {npc.Home}");
        }
    }

    private static Vector3i FirstWall(SvGameServer server, SvGameServer.AncientRuinSite site)
    {
        ushort wall = Id("iron_wall");
        var o = site.Origin;
        for (int x = 6; x < site.Width; x++)
            for (int z = 6; z < site.Length; z++)
            {
                var c = new Vector3i(o.X + x, o.Y + 1, o.Z + z);
                if (server.World.GetBlock(c).Value == wall)
                {
                    return c;
                }
            }

        throw new InvalidOperationException("no wall in the ruin");
    }

    /// <summary>#3392: testing a buried ruin no longer means searching the world — "/tp ancient" lists and reaches it, beside
    /// the top of its shaft (the city's hatch here), standing in the open.</summary>
    [Fact]
    public void TheAdminTeleport_ListsTheRuin_BesideItsEntrance()
    {
        var server = Start("gds_desert", out var repo, name: "gds_tp");
        using (repo)
        {
            var site = server.AncientRuinForTest!;
            server.AddLocalPlayer("Ada");

            var ruins = server.TeleportTargetsForTest("Ada").Where(t => t.Kind == "ancient").ToList();
            Assert.NotEmpty(ruins);
            Assert.Equal(Enumerable.Range(1, ruins.Count), ruins.Select(t => t.Number)); // ancient1, ancient2 …
            static float Dist2(Vector3f p, Vector3i e)
                => ((p.X - (e.X + 0.5f)) * (p.X - (e.X + 0.5f))) + ((p.Z - (e.Z + 0.5f)) * (p.Z - (e.Z + 0.5f)));
            var target = ruins.OrderBy(t => Dist2(t.Position, site.Entrance)).First();
            Assert.True(Dist2(target.Position, site.Entrance) <= 9f, $"beside the entrance {site.Entrance}, not at {target.Position}");
            var feet = new Vector3i((int)Math.Floor(target.Position.X), (int)Math.Floor(target.Position.Y), (int)Math.Floor(target.Position.Z));
            Assert.True(server.World.GetBlock(feet).IsAir && server.World.GetBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)).IsAir);
        }
    }
}
