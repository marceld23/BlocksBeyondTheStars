// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1885 / #1886 / #1889: the shipped settlement and G.D.S. district modules, the modular kits and the furnished template
/// copies (Marcel 2026-09-14: villages and cities entirely from modules, walls following the planet, a bed for every
/// resident, taverns and workshops, human and alien variants).
/// </summary>
public sealed class SettlementModuleContentTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] KnownMarkers =
    {
        "vendor", "mission_board", "npc", "loot", "door_slide", "door_hinge", "door_energy", "room", "greenhouse", "chest",
        "data_terminal", "guard_post", "tavern", "workshop", "lounge",
    };

    private static IEnumerable<StructureTemplate> ModularModules()
        => Content.SettlementTemplates.Where(t => t.Kit is "settlement_modular" or "city_gds_modular");

    private static int MaxHeight(StructureTemplate t) => t.Tier switch
    {
        "village" => 7,
        "town" => 11,
        "city" => 15,
        _ => 19,
    };

    private static string? PostMarker(string function) => function switch
    {
        StructureRoles.Market => "vendor",
        StructureRoles.Board => "mission_board",
        StructureRoles.Tavern => SettlementGenerator.TavernMarker,
        StructureRoles.Workshop => SettlementGenerator.WorkshopMarker,
        StructureRoles.Greenhouse => "greenhouse",
        _ => null,
    };

    private static ModuleMaterials MaterialsFor(StructureTemplate t, string surface = "grass")
        => t.Tier == StructureRoles.MetropolisTier ? ModuleMaterials.ForCity(Content) : ModuleMaterials.ForSettlement(t.Tier, surface, t.IsAlienStyle, Content);

    private static int BedHeads(SettlementStructure s)
    {
        ushort bed = Content.GetBlock("bed")!.NumericId.Value;
        int n = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) == bed && ShapeCode.ShapeOf(s.GetShape(x, y, z)) != (int)BlockShape.BedFoot)
                    {
                        n++;
                    }
                }

        return n;
    }

    [Fact]
    public void ShippedModules_FitTheirEnvelopes_AndCarryDoorsPostsAndRooms()
    {
        var modules = ModularModules().ToList();
        Assert.True(modules.Count >= 60, $"expected the modular sets, found {modules.Count}");
        foreach (var t in modules)
        {
            bool district = t.Tier == StructureRoles.MetropolisTier;
            Assert.True(district ? t.Width <= 32 && t.Length <= 32 : t.Width == 8 && t.Length == 8, $"{t.Key}: {t.Width}×{t.Length}");
            Assert.True(t.Height <= MaxHeight(t), $"{t.Key}: height {t.Height} over {MaxHeight(t)}");
            Assert.True(district ? StructureRoles.IsCityRole(t.Function) : StructureRoles.IsPlotRole(t.Function), $"{t.Key}: function {t.Function}");

            var markers = t.Cells.Where(c => c.Kind == "marker").ToList();
            Assert.Contains(markers, c => c.Id.StartsWith("door_", StringComparison.Ordinal));
            if (t.Function != StructureRoles.CityTower)
            {
                Assert.Contains(markers, c => c.Id == "room");
            }

            if (PostMarker(t.Function) is { } post)
            {
                Assert.Contains(markers, c => c.Id == post);
            }

            foreach (var c in t.Cells)
            {
                Assert.True(c.X >= 0 && c.Y >= 0 && c.Z >= 0 && c.X < t.Width && c.Y < t.Height && c.Z < t.Length, $"{t.Key}: cell outside");
                if (c.Kind == "marker")
                {
                    Assert.Contains(c.Id, KnownMarkers);
                }
                else
                {
                    Assert.True(MaterialTokens.IsToken(c.Id) || Content.GetBlock(c.Id) != null, $"{t.Key}: unknown block '{c.Id}'");
                }
            }
        }

        // Two to three variants per function per style, each human and alien; G.D.S. districts two each.
        foreach (var style in new[] { "village", "town" })
        {
            foreach (var function in new[] { "house", "market", "board", "greenhouse", "tavern", "workshop" })
            {
                var set = modules.Where(m => m.Tier == style && m.Function == function).ToList();
                Assert.True(set.Count(m => !m.IsAlienStyle) >= 2, $"{style} {function}: human variants");
                Assert.Equal(set.Count(m => !m.IsAlienStyle), set.Count(m => m.IsAlienStyle));
            }
        }

        foreach (var role in StructureRoles.CityRoles)
        {
            Assert.Equal(2, modules.Count(m => m.Function == role));
        }
    }

    [Fact]
    public void EveryHomeModule_IsFurnished_WithABedPerResidentRoom()
    {
        foreach (var t in ModularModules())
        {
            var s = SettlementGenerator.FromTemplate(t, Content, MaterialsFor(t));
            int beds = BedHeads(s);
            if (t.Function == StructureRoles.CityTower)
            {
                Assert.Equal(0, beds); // the watchtower is the guardians' — machines do not sleep
                continue;
            }

            // Every module a resident lives in has at least one bed: the shopkeeper, the quartermaster, the innkeeper,
            // the craftsman and the gardener too (Marcel: "everyone has a bed").
            Assert.True(beds >= 1, $"{t.Key}: no bed");
        }
    }

    [Fact]
    public void EveryRoom_IsReachableOnFoot_FromTheEntrance()
    {
        foreach (var t in ModularModules().Where(m => m.Tier != StructureRoles.MetropolisTier))
        {
            var s = SettlementGenerator.FromTemplate(t, Content, MaterialsFor(t));
            bool Solid(Vector3i c) => c.X >= 0 && c.Y >= 0 && c.Z >= 0 && c.X < s.Width && c.Y < s.Height && c.Z < s.Length && s.Get(c.X, c.Y, c.Z) != 0;
            bool Inside(Vector3i c) => c.X >= 0 && c.Z >= 0 && c.X < s.Width && c.Z < s.Length && c.Y >= 0;
            bool Free(Vector3i c) => !Solid(c);
            bool Standable(Vector3i c) => Inside(c) && c.Y > 0 && Solid(new Vector3i(c.X, c.Y - 1, c.Z)) && Free(c) && Free(new Vector3i(c.X, c.Y + 1, c.Z));

            var door = t.Cells.First(c => c.Kind == "marker" && c.Id.StartsWith("door_", StringComparison.Ordinal) && c.Z == 0);
            var start = new Vector3i(door.X, 1, 1);
            Assert.True(Standable(start), $"{t.Key}: the cell inside the entrance is blocked");
            foreach (var room in t.Cells.Where(c => c.Kind == "marker" && c.Id == "room"))
            {
                var goal = new Vector3i(room.X, room.Y, room.Z);
                var path = NpcGridPath.Find(start, goal, Standable, Free, null, new NpcGridPath.Limits(16, 16, 6000), out _);
                Assert.True(path != null, $"{t.Key}: the room at ({room.X},{room.Y},{room.Z}) cannot be reached on foot");
            }
        }
    }

    [Fact]
    public void Entrances_AreThreeTall_AndTheLaneBehindThemStaysClear()
    {
        foreach (var t in ModularModules().Where(m => m.Tier != StructureRoles.MetropolisTier))
        {
            var s = SettlementGenerator.FromTemplate(t, Content, MaterialsFor(t));
            var door = t.Cells.First(c => c.Kind == "marker" && c.Id.StartsWith("door_", StringComparison.Ordinal) && c.Z == 0);
            foreach (int x in new[] { door.X, door.X + 1 })
                for (int z = 0; z <= 1; z++)
                    for (int y = 1; y <= 3; y++)
                    {
                        Assert.True(s.Get(x, y, z) == 0, $"{t.Key}: ({x},{y},{z}) blocks the entrance");
                    }
        }
    }

    [Fact]
    public void Tokens_FollowThePlanet_AndTheInhabitants()
    {
        ushort B(string key) => Content.GetBlock(key)!.NumericId.Value;
        Assert.Equal(B("sand"), ModuleMaterials.ForSettlement("village", "sand", false, Content).Resolve(MaterialTokens.Wall));
        Assert.Equal(B("ice"), ModuleMaterials.ForSettlement("hamlet", "ice", false, Content).Resolve(MaterialTokens.Wall));
        Assert.Equal(B("iron_wall"), ModuleMaterials.ForSettlement("town", "sand", false, Content).Resolve(MaterialTokens.Wall));
        Assert.Equal(B("crystal"), ModuleMaterials.ForSettlement("village", "grass", true, Content).Resolve(MaterialTokens.Accent));
        Assert.Equal(B("crystal"), ModuleMaterials.ForSettlement("village", "grass", true, Content).Resolve(MaterialTokens.Roof));
        Assert.Equal(B("glass"), ModuleMaterials.ForSettlement("city", "grass", false, Content).Resolve(MaterialTokens.Accent));
        Assert.Equal(B("iron_wall"), ModuleMaterials.ForCity(Content).Resolve(MaterialTokens.Wall));
        Assert.Equal(0, ModuleMaterials.ForCity(Content).Resolve("stone"));
    }

    [Theory]
    [InlineData("hamlet")]
    [InlineData("village")]
    [InlineData("town")]
    [InlineData("city")]
    public void ModularKits_BuildSettlements_EntirelyFromModules(string tier)
    {
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var kits = Content.KitsFor(StructureKit.KindSettlement, tier, null, null);
        Assert.Equal(2, kits.Count);
        Assert.All(kits, k => Assert.StartsWith(tier + "_modular_", k.Key, StringComparison.Ordinal));
        var seen = new HashSet<string>();
        foreach (var kit in kits)
        {
            foreach (var surface in new[] { "grass", "sand", "ice" })
            {
                for (long seed = 1; seed <= 12; seed++)
                {
                    var layout = SettlementLayoutSpec.FromKit(kit, tier, new Random((int)seed));
                    var composition = new List<string>();
                    var s = SettlementGenerator.Generate(tier, false, seed * 104729, surface, Content, null, 0, composition, null, layout, kit, pool);
                    bool alien = s.Inhabitant == "alien";
                    seen.Add(s.Inhabitant);
                    Assert.NotEmpty(composition);
                    Assert.All(composition, key => Assert.False(string.IsNullOrEmpty(key), $"{kit.Key} seed {seed}: a plot without a module"));
                    var used = composition.Select(k => pool.First(m => m.Key == k)).ToList();
                    Assert.All(used, m => Assert.Equal(alien, m.IsAlienStyle));
                    Assert.Contains(used, m => m.Function == StructureRoles.Market);
                    Assert.Contains(used, m => m.Function == StructureRoles.Board);
                    if (tier != "hamlet" && composition.Count >= 4)
                    {
                        Assert.Single(used, m => m.Function == StructureRoles.Tavern);
                    }

                    Assert.True(used.Count(m => m.Function == StructureRoles.Workshop) <= 1);
                    Assert.True(BedHeads(s) >= used.Count, $"{kit.Key} seed {seed}: fewer beds than buildings");

                    // The walls follow the planet: a village of sand / ice / grass builds with it.
                    if (!StructureRoles.IsTownStyleTier(tier))
                    {
                        ushort wall = Content.GetBlock(surface)!.NumericId.Value;
                        int count = 0;
                        for (int x = 0; x < s.Width; x++)
                            for (int z = 0; z < s.Length; z++)
                            {
                                if (s.Get(x, 2, z) == wall)
                                {
                                    count++;
                                }
                            }

                        Assert.True(count > 10, $"{kit.Key} on {surface}: walls do not follow the surface");
                    }

                    // A replay of the pinned composition and layout builds the same settlement.
                    Assert.True(SettlementLayoutSpec.TryParse(layout.Serialize(), out var pinned));
                    var again = SettlementGenerator.Generate(tier, false, seed * 104729, surface, Content, null, 0, new List<string>(composition), null, pinned, null, pool);
                    Assert.Equal(Hash(s), Hash(again));
                }
            }
        }

        Assert.Contains("human", seen);
        Assert.Contains("alien", seen);
    }

    [Fact]
    public void GdsModularKit_FillsEveryBuiltDistrict_WithADistrictModule()
    {
        var kit = Assert.Single(Content.KitsFor(StructureKit.KindCity, null, null, "gds_desert"));
        Assert.Equal("city_gds_modular_1", kit.Key);
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var spec = CityLayoutSpec.FromKit(kit);
        var composition = new List<string>();
        var city = CityGenerator.Generate(20260914, Content, Array.Empty<CityGenerator.OpenZone>(), null, 0, composition, null, spec, kit, pool);
        Assert.Equal(spec.Grid * spec.Grid, composition.Count);
        for (int d = 0; d < composition.Count; d++)
        {
            var role = CityGenerator.RoleAtFor(d / spec.Grid, d % spec.Grid, spec.Grid, spec.RoleMap);
            if (role is CityGenerator.Role.Plaza or CityGenerator.Role.Open)
            {
                Assert.Equal(string.Empty, composition[d]);
            }
            else
            {
                Assert.StartsWith("gds_", composition[d], StringComparison.Ordinal);
            }
        }

        Assert.True(BedHeads(city) > 40, "the G.D.S. city's homes carry beds");
        Assert.Contains(city.Markers, m => m.Type == "guard_post");
        Assert.Contains(city.Markers, m => m.Type == "vendor");
        Assert.Contains(city.Markers, m => m.Type == "mission_board");
    }

    [Fact]
    public void FurnishedCopies_HaveBeds_AndTheOriginalsStayOnlyForPinnedWorlds()
    {
        foreach (var key in new[] { "river_hamlet", "stone_roundhouse", "stilt_hamlet", "walled_market" })
        {
            var original = Content.SettlementTemplateByKey(key);
            var copy = Content.SettlementTemplateByKey(key + "_home");
            Assert.NotNull(original);
            Assert.NotNull(copy);
            Assert.True(original!.PinOnly);
            Assert.False(copy!.PinOnly);
            Assert.False(copy.LegacyPool);
            Assert.Equal(original.Tier, copy.Tier);
            Assert.Equal(0, BedHeads(SettlementGenerator.FromTemplate(original, Content)));
            Assert.True(BedHeads(SettlementGenerator.FromTemplate(copy, Content)) >= 1, key + "_home has no bed");
        }

        var rng = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            foreach (var tier in new[] { "hamlet", "village", "town" })
            {
                var picked = Content.PickSettlementTemplate(tier, null, rng, "jungle");
                Assert.NotNull(picked);
                Assert.EndsWith("_home", picked!.Key, StringComparison.Ordinal);
            }
        }

        // The pre-pinning replay universe is untouched.
        Assert.Equal("river_hamlet", Content.PickSettlementTemplate("village", null, rng, null, legacyOnly: true)!.Key);
    }

    [Fact]
    public void PinOnlyKits_AreNeverDrawn_ButStayFindable()
    {
        foreach (var key in new[] { "hamlet_default_1", "village_default_1", "town_default_1", "city_default_1", "city_gds_default_1" })
        {
            var kit = Content.KitByKey(key);
            Assert.NotNull(kit);
            Assert.True(kit!.PinOnly);
            Assert.DoesNotContain(Content.KitsFor(kit.KindOrDefault, kit.KindOrDefault == StructureKit.KindCity ? null : kit.Tier, null, "gds_desert"), k => k.Key == key);
        }
    }

    [Fact]
    public void LayoutRevision_RoundTrips_AndTheOldSixFieldFormStaysRevisionZero()
    {
        Assert.True(SettlementLayoutSpec.TryParse("3,2,10,8,1,1", out var old));
        Assert.Equal(0, old.Revision);
        Assert.Equal("3,2,10,8,1,1", old.Serialize());

        var fresh = new SettlementLayoutSpec(3, 2, 10, 8, 1, true, SettlementLayoutSpec.CurrentRevision);
        Assert.True(SettlementLayoutSpec.TryParse(fresh.Serialize(), out var back));
        Assert.Equal(SettlementLayoutSpec.CurrentRevision, back.Revision);
        Assert.Equal(fresh.Serialize(), back.Serialize());
    }

    [Fact]
    public void TemplateOrKitPick_TakesTheTableTheCoinAsksFor_AndFallsBackToTheOther()
    {
        var templates = new[] { new StructureTemplate { Key = "t1", Weight = 1 } };
        var kits = new[] { new StructureKit { Key = "k1", Weight = 1 } };
        var rng = new Random(1);
        Assert.Equal(("t1", ""), BlocksBeyondTheStars.GameServer.GameServer.PickTemplateOrKitForTest(templates, kits, true, rng));
        Assert.Equal(("", "k1"), BlocksBeyondTheStars.GameServer.GameServer.PickTemplateOrKitForTest(templates, kits, false, rng));
        Assert.Equal(("t1", ""), BlocksBeyondTheStars.GameServer.GameServer.PickTemplateOrKitForTest(templates, Array.Empty<StructureKit>(), false, rng));
        Assert.Equal(("", "k1"), BlocksBeyondTheStars.GameServer.GameServer.PickTemplateOrKitForTest(Array.Empty<StructureTemplate>(), kits, true, rng));
        Assert.Equal(("", ""), BlocksBeyondTheStars.GameServer.GameServer.PickTemplateOrKitForTest(Array.Empty<StructureTemplate>(), Array.Empty<StructureKit>(), true, rng));
    }

    private static long Hash(SettlementStructure s)
    {
        long h = 17;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    h = unchecked(h * 31 + s.Get(x, y, z) * 7 + s.GetShape(x, y, z));
                }

        foreach (var m in s.Markers)
        {
            h = unchecked(h * 31 + m.Type.GetHashCode(StringComparison.Ordinal) + m.LocalPos.X * 3 + m.LocalPos.Y * 5 + m.LocalPos.Z * 7);
        }

        return h;
    }
}
