// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// Authored building modules (#1826 / #1827) and procedural interiors (#1828): a template with a role is
/// composed INTO a settlement plot or a city district instead of being rolled as a whole settlement; the
/// pick is a hash so every plot that stays procedural is byte-identical; rooms — procedural and authored —
/// get furniture that keeps the resident's spot, the door lane and the ladder clear; and the placement
/// record gates modules so an existing world's layout never changes under its blocks.
/// </summary>
public sealed class SettlementModuleTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SettlementModuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_modules_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    /// <summary>A hollow box module: floor, walls, roof, a 2-wide door gap on the −Z wall, plus markers.</summary>
    private static StructureTemplate Box(string key, string role, string tier, int w, int h, int l, string material,
        params (string Id, int X, int Y, int Z)[] markers)
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = tier, Kind = "settlement", Role = role, Width = w, Height = h, Length = l };
        for (int x = 0; x < w; x++)
            for (int z = 0; z < l; z++)
                for (int y = 0; y < h; y++)
                {
                    bool shell = y == 0 || y == h - 1 || x == 0 || x == w - 1 || z == 0 || z == l - 1;
                    bool door = z == 0 && (x == w / 2 - 1 || x == w / 2) && y >= 1 && y <= System.Math.Min(3, h - 2);
                    if (shell && !door)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = material });
                    }
                }

        foreach (var m in markers)
        {
            t.Cells.Add(new TemplateCell { X = m.X, Y = m.Y, Z = m.Z, Kind = "marker", Id = m.Id });
        }

        return t;
    }

    private static int Count(SettlementStructure s, ushort id)
    {
        int n = 0;
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    if (s.Get(x, y, z) == id) n++;
                }

        return n;
    }

    private HashSet<ushort> FurnitureIds()
    {
        var ids = new HashSet<ushort>();
        foreach (var key in new[] { "bed", "wood_crate", "crate", "torch", "flower_pot", "light_white", "data_cache", "algae_tank", "gaming_pc" })
        {
            if (_content.GetBlock(key) is { } b) ids.Add(b.NumericId.Value);
        }

        return ids;
    }

    private bool IsFurniture(SettlementStructure s, int x, int y, int z, HashSet<ushort> ids)
        => s.Get(x, y, z) != 0 && (ids.Contains(s.Get(x, y, z)) || s.GetShape(x, y, z) != 0);

    /// <summary>Air, or a walk-through prop: the garden pass scatters flora around (and into) a building, and
    /// flora never blocks a resident or a doorway — exactly what the server's NPC walk check says.</summary>
    private bool Passable(SettlementStructure s, int x, int y, int z)
    {
        ushort id = s.Get(x, y, z);
        if (id == 0) return true;
        var def = _content.BlockById(new BlockId(id));
        return def != null && (def.Key.StartsWith("flora_", StringComparison.Ordinal) || def.Key == "torch");
    }

    // ---------------- the contract ----------------

    [Fact]
    public void Role_LoadsFromTheShippedPool_AndModulesStayOutOfTheWholePools()
    {
        var cottage = _content.SettlementModuleByKey("timber_cottage");
        var flat = _content.SettlementModuleByKey("iron_flat");
        Assert.NotNull(cottage);
        Assert.NotNull(flat);
        Assert.Equal(StructureRoles.House, cottage!.Role);
        Assert.True(cottage.IsModule);
        Assert.Equal("village", cottage.Tier);
        Assert.Equal("town", flat!.Tier);
        Assert.Contains(cottage.Cells, c => c.Kind == "marker" && c.Id == SettlementGenerator.RoomMarker);

        // A module is never rolled as a whole settlement, however often we ask.
        Assert.Null(_content.SettlementTemplateByKey("timber_cottage"));
        var rng = new Random(5);
        for (int i = 0; i < 300; i++)
        {
            foreach (var tier in new[] { "hamlet", "village", "town", "city" })
            {
                var pick = _content.PickSettlementTemplate(tier, null, rng, "jungle");
                Assert.True(pick is null || !pick.IsModule, $"{pick?.Key} is a module and was rolled as a whole settlement");
            }
        }

        // The module list filters by pack and planet type.
        var all = _content.SettlementModulesFor(null, "jungle");
        Assert.Contains(all, m => m.Key == "timber_cottage");
        Assert.Contains(all, m => m.Key == "iron_flat");
        Assert.Empty(_content.SettlementModulesFor(new[] { "no_such_pack" }, "jungle"));
    }

    [Fact]
    public void ModuleLookup_FiltersByPlanetType()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var icy = Box("icy_hut", StructureRoles.House, "village", 6, 6, 6, "gold_block", ("npc", 3, 1, 3));
        icy.PlanetTypes.Add("ice");
        content.SetStructureTemplates(content.StationTemplates, content.SettlementTemplates.Concat(new[] { icy }).ToList());

        Assert.Contains(content.SettlementModulesFor(null, "ice"), m => m.Key == "icy_hut");
        Assert.DoesNotContain(content.SettlementModulesFor(null, "jungle"), m => m.Key == "icy_hut");
        Assert.Null(content.SettlementTemplateByKey("icy_hut"));
    }

    // ---------------- the settlement composer ----------------

    [Fact]
    public void HouseModule_LandsOnHousePlots_WithItsMarkers_NeverOnTheServicePlots()
    {
        ushort gold = Id("gold_block");
        var modules = new[]
        {
            Box("gold_house", StructureRoles.House, "village", 6, 7, 6, "gold_block",
                ("npc", 3, 1, 3), ("door_hinge", 2, 1, 0), (SettlementGenerator.RoomMarker, 2, 1, 2)),
        };

        int seedsWithModule = 0;
        for (long seed = 1; seed <= 30; seed++)
        {
            var s = SettlementGenerator.Generate("village", false, seed, "grass", _content, modules, 1.0);
            int goldCells = Count(s, gold);
            if (goldCells == 0)
            {
                continue; // a village whose dwelling plots were all skipped / greenhouses
            }

            seedsWithModule++;

            // Plots 0 and 1 (the vendor and the mission board) never take a HOUSE module.
            for (int x = 0; x <= SettlementGenerator.Plot; x++)
                for (int z = 0; z <= 2 * SettlementGenerator.Plot; z++)
                    for (int y = 0; y < s.Height; y++)
                    {
                        Assert.NotEqual(gold, s.Get(x, y, z));
                    }

            // The module's door marker hangs on a gold wall with the gap behind it, its resident stands on
            // the gold floor, and the resident's cell is air (the furnisher kept it).
            var door = s.Markers.First(m => m.Type == "door_hinge" && s.Get(m.LocalPos.X, 0, m.LocalPos.Z) == gold);
            Assert.True(Passable(s, door.LocalPos.X, door.LocalPos.Y, door.LocalPos.Z),
                $"seed {seed}: the module door at {door.LocalPos.X},{door.LocalPos.Y},{door.LocalPos.Z} holds block {s.Get(door.LocalPos.X, door.LocalPos.Y, door.LocalPos.Z)}");
            Assert.True(Passable(s, door.LocalPos.X, door.LocalPos.Y + 1, door.LocalPos.Z));
            var npc = s.Markers.First(m => m.Type == "npc" && s.Get(m.LocalPos.X, 0, m.LocalPos.Z) == gold);
            Assert.True(Passable(s, npc.LocalPos.X, npc.LocalPos.Y, npc.LocalPos.Z));

            // The authored room got furniture too (a room marker inside a hollow gold box).
            var ids = FurnitureIds();
            bool furnished = false;
            for (int x = 0; x < s.Width && !furnished; x++)
                for (int z = 0; z < s.Length && !furnished; z++)
                {
                    furnished = s.Get(x, 0, z) == gold && IsFurniture(s, x, 1, z, ids);
                }

            Assert.True(furnished, $"seed {seed}: the module's room marker should furnish its room");
        }

        Assert.True(seedsWithModule >= 20, $"a house module should land on most villages, got {seedsWithModule}/30");
    }

    [Fact]
    public void Plots_ThatStayProcedural_AreByteIdentical_WithOrWithoutModules()
    {
        var modules = new[]
        {
            Box("gold_market", StructureRoles.Market, "town", 6, 9, 6, "gold_block", ("vendor", 3, 1, 3), ("door_slide", 2, 1, 0)),
        };
        ushort gold = Id("gold_block");

        foreach (var tier in new[] { "town", "city" })
        {
            for (long seed = 1; seed <= 8; seed++)
            {
                var plain = SettlementGenerator.Generate(tier, false, seed, "stone", _content);
                var composed = SettlementGenerator.Generate(tier, false, seed, "stone", _content, modules, 1.0);
                var again = SettlementGenerator.Generate(tier, false, seed, "stone", _content, modules, 1.0);

                Assert.Equal(plain.Width, composed.Width);
                Assert.Equal(plain.Length, composed.Length);
                Assert.True(Count(composed, gold) > 0, "plot 0 should be the gold market module");
                Assert.Equal(0, Count(plain, gold));

                // Everything outside plot 0's stride box is identical: blocks, shapes and tints.
                for (int x = 0; x < plain.Width; x++)
                    for (int y = 0; y < plain.Height; y++)
                        for (int z = 0; z < plain.Length; z++)
                        {
                            if (x <= SettlementGenerator.Plot && z <= SettlementGenerator.Plot)
                            {
                                Assert.Equal(composed.Get(x, y, z), again.Get(x, y, z)); // deterministic
                                continue;
                            }

                            Assert.True(plain.Get(x, y, z) == composed.Get(x, y, z), $"{tier} seed {seed}: block differs at {x},{y},{z}");
                            Assert.Equal(plain.GetShape(x, y, z), composed.GetShape(x, y, z));
                            Assert.Equal(plain.GetModifier(x, y, z), composed.GetModifier(x, y, z));
                        }

                // The other plots' markers are the same set too.
                static string Key(SettlementMarker m) => $"{m.Type}@{m.LocalPos.X},{m.LocalPos.Y},{m.LocalPos.Z}";
                var plainOthers = plain.Markers.Where(m => m.LocalPos.X > SettlementGenerator.Plot || m.LocalPos.Z > SettlementGenerator.Plot).Select(Key).OrderBy(k => k).ToList();
                var composedOthers = composed.Markers.Where(m => m.LocalPos.X > SettlementGenerator.Plot || m.LocalPos.Z > SettlementGenerator.Plot).Select(Key).OrderBy(k => k).ToList();
                Assert.Equal(plainOthers, composedOthers);
            }
        }
    }

    [Fact]
    public void OversizedOrWrongStyleModules_AreNeverUsed()
    {
        ushort gold = Id("gold_block");
        var tooWide = Box("wide", StructureRoles.House, "village", 7, 6, 6, "gold_block", ("npc", 3, 1, 3));
        var tooTall = Box("tall", StructureRoles.House, "village", 6, 12, 6, "gold_block", ("npc", 3, 1, 3));
        var townStyle = Box("iron", StructureRoles.House, "town", 6, 6, 6, "gold_block", ("npc", 3, 1, 3));
        var modules = new[] { tooWide, tooTall, townStyle };

        for (long seed = 1; seed <= 10; seed++)
        {
            var s = SettlementGenerator.Generate("village", false, seed, "grass", _content, modules, 1.0);
            Assert.Equal(0, Count(s, gold));
        }
    }

    [Fact]
    public void MarketModule_WithoutAVendor_GetsOne_InAFreeCell()
    {
        ushort gold = Id("gold_block");
        var modules = new[] { Box("mute_market", StructureRoles.Market, "village", 6, 6, 6, "gold_block", ("door_hinge", 2, 1, 0)) };
        var s = SettlementGenerator.Generate("village", false, 3, "grass", _content, modules, 1.0);

        Assert.True(Count(s, gold) > 0);
        var vendor = Assert.Single(s.Markers, m => m.Type == "vendor");
        Assert.True(vendor.LocalPos.X <= SettlementGenerator.Plot && vendor.LocalPos.Z <= SettlementGenerator.Plot, "the vendor stands on plot 0");
        Assert.Equal(0, s.Get(vendor.LocalPos.X, vendor.LocalPos.Y, vendor.LocalPos.Z));
        Assert.NotEqual(0, s.Get(vendor.LocalPos.X, vendor.LocalPos.Y - 1, vendor.LocalPos.Z));
    }

    [Fact]
    public void Modules_AreOff_WhenTheChanceIsZero_OrTheListIsNull()
    {
        ushort gold = Id("gold_block");
        var modules = new[] { Box("gold_house", StructureRoles.House, "village", 6, 6, 6, "gold_block", ("npc", 3, 1, 3)) };
        Assert.Equal(0, Count(SettlementGenerator.Generate("village", false, 1, "grass", _content, modules, 0.0), gold));
        Assert.Equal(0, Count(SettlementGenerator.Generate("village", false, 1, "grass", _content, null, 1.0), gold));
    }

    // ---------------- interiors ----------------

    [Fact]
    public void ProceduralRooms_AreFurnished_AndKeepTheResidentDoorAndLadderClear()
    {
        var ids = FurnitureIds();
        foreach (var tier in new[] { "hamlet", "village", "town", "city" })
        {
            for (long seed = 1; seed <= 6; seed++)
            {
                var s = SettlementGenerator.Generate(tier, false, seed, "grass", _content);
                var same = SettlementGenerator.Generate(tier, false, seed, "grass", _content);

                int furniture = 0, upper = 0;
                for (int x = 0; x < s.Width; x++)
                    for (int y = 0; y < s.Height; y++)
                        for (int z = 0; z < s.Length; z++)
                        {
                            Assert.Equal(s.Get(x, y, z), same.Get(x, y, z));
                            Assert.Equal(s.GetShape(x, y, z), same.GetShape(x, y, z));
                            if (!IsFurniture(s, x, y, z, ids)) continue;
                            furniture++;
                            if (y >= 5) upper++;
                            Assert.True(y % 4 != 0, $"{tier} seed {seed}: furniture in a deck row at {x},{y},{z}");
                        }

                Assert.True(furniture > 0, $"{tier} seed {seed}: no furniture at all");
                if (StructureRoles.IsTownStyleTier(tier))
                {
                    Assert.True(upper > 0, $"{tier} seed {seed}: the upper storeys should be furnished too");
                }

                foreach (var m in s.Markers)
                {
                    var p = m.LocalPos;
                    if (System.Math.Abs(p.X - s.Width / 2) <= 3 && System.Math.Abs(p.Z - s.Length / 2) <= 3)
                    {
                        // Pre-existing: with an odd plot count the central feature (well ring, plaza lamps) lands
                        // inside a house — not the furnisher's doing, and not what this test guards.
                        continue;
                    }

                    if (m.Type is "npc" or "vendor" or "mission_board")
                    {
                        // (The garden pass and the central feature predate the furnisher and may touch these
                        // cells with flora or plaza blocks — what the furnisher guarantees is: no FURNITURE here.)
                        Assert.False(IsFurniture(s, p.X, p.Y, p.Z, ids), $"{tier} seed {seed}: {m.Type} at {p.X},{p.Y},{p.Z} stands in furniture {s.Get(p.X, p.Y, p.Z)}");
                        Assert.False(IsFurniture(s, p.X, p.Y + 1, p.Z, ids), $"{tier} seed {seed}: {m.Type} at {p.X},{p.Y},{p.Z} has furniture over its head");
                    }
                    else if (m.Type.StartsWith("door_", StringComparison.Ordinal))
                    {
                        // The gap itself and the first interior cell behind it are passable, two cells tall.
                        Assert.False(IsFurniture(s, p.X, p.Y, p.Z, ids), $"{tier} seed {seed}: door at {p.X},{p.Y},{p.Z} holds furniture {s.Get(p.X, p.Y, p.Z)}");
                        Assert.False(IsFurniture(s, p.X, p.Y + 1, p.Z, ids));
                        bool lane = false;
                        foreach (var (dx, dz) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                        {
                            int nx = p.X + dx, nz = p.Z + dz;
                            if (nx < 0 || nz < 0 || nx >= s.Width || nz >= s.Length) continue;
                            lane |= s.Get(nx, p.Y, nz) == 0 || (!IsFurniture(s, nx, p.Y, nz, ids) && Passable(s, nx, p.Y, nz));
                        }

                        Assert.True(lane, $"{tier} seed {seed}: door at {p.X},{p.Y},{p.Z} has no clear lane");
                    }
                }

                // Ladders keep the cell beside them free on every storey.
                ushort ladder = Id("ladder");
                for (int x = 0; x < s.Width; x++)
                    for (int y = 1; y < s.Height; y++)
                        for (int z = 0; z < s.Length; z++)
                        {
                            if (s.Get(x, y, z) != ladder || y % 4 == 0) continue;
                            Assert.False(IsFurniture(s, x + 1, y, z, ids) && IsFurniture(s, x, y, z + 1, ids),
                                $"{tier} seed {seed}: ladder at {x},{y},{z} boxed in by furniture");
                        }
            }
        }
    }

    [Fact]
    public void RoomMarker_FurnishesExactlyItsRoom_InAWholeTemplate()
    {
        var t = Box("hall", string.Empty, "village", 10, 6, 10, "stone",
            ("vendor", 5, 1, 5), ("door_hinge", 4, 1, 0), (SettlementGenerator.RoomMarker, 2, 1, 2));
        var a = SettlementGenerator.FromTemplate(t, _content);
        var b = SettlementGenerator.FromTemplate(t, _content);
        var ids = FurnitureIds();
        ushort stone = Id("stone");

        int furniture = 0;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    if (!IsFurniture(a, x, y, z, ids)) continue;
                    furniture++;
                    Assert.Equal(1, y); // on the floor level only
                    Assert.True(x >= 1 && x <= 8 && z >= 1 && z <= 8, $"furniture outside the room at {x},{y},{z}");
                    Assert.Equal(stone, a.Get(x, 0, z)); // on the floor
                }

        Assert.True(furniture >= 3, $"a 8×8 market room should hold several pieces, got {furniture}");
        Assert.Equal(0, a.Get(5, 1, 5)); // the vendor's cell
        Assert.Equal(0, a.Get(4, 1, 1)); // the lane behind the door
        Assert.Equal(0, a.Get(4, 1, 0)); // the door gap itself
        Assert.Equal(0, a.Get(5, 1, 0)); // … both of its columns
        Assert.Contains(a.Markers, m => m.Type == "vendor"); // the author's vendor, no fallback added
        Assert.Single(a.Markers, m => m.Type == "vendor");
        // Market furniture: a counter (slab) shows up as a shaped stone/wood cell.
        bool shaped = false;
        for (int x = 1; x <= 8; x++)
            for (int z = 1; z <= 8; z++)
            {
                shaped |= a.GetShape(x, 1, z) != 0;
            }

        Assert.True(shaped, "the market room should hold shaped furniture (counter / table)");
    }

    [Fact]
    public void RoomMarker_OnOpenGround_IsCapped()
    {
        // A room marker on a bare 40×40 floor floods only up to the cap — the furnisher must not carpet a field.
        var t = new StructureTemplate { Key = "field", Tier = "village", Width = 40, Height = 4, Length = 40 };
        for (int x = 0; x < 40; x++)
            for (int z = 0; z < 40; z++)
            {
                t.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = "stone" });
            }

        t.Cells.Add(new TemplateCell { X = 20, Y = 1, Z = 20, Kind = "marker", Id = SettlementGenerator.RoomMarker });
        var s = SettlementGenerator.FromTemplate(t, _content);
        var ids = FurnitureIds();
        int furniture = 0;
        for (int x = 0; x < 40; x++)
            for (int z = 0; z < 40; z++)
            {
                if (IsFurniture(s, x, 1, z, ids)) furniture++;
            }

        Assert.InRange(furniture, 0, 12);
    }

    // ---------------- the city composer ----------------

    private static IReadOnlyList<CityGenerator.OpenZone> StandardZones()
    {
        int c = CityGenerator.Footprint / 2;
        return new[]
        {
            new CityGenerator.OpenZone(c - 11, c - 11, c + 11, c + 11),
            new CityGenerator.OpenZone(c - 56 - 14, c + 56 - 14, c - 56 + 14, c + 56 + 14),
        };
    }

    [Fact]
    public void CityHousingModule_ReplacesHousingDistricts_AndKeepsTheOpenZonesClear()
    {
        ushort gold = Id("gold_block");
        var modules = new[]
        {
            Box("gold_block_of_flats", StructureRoles.CityHousing, StructureRoles.MetropolisTier, 12, 8, 12, "gold_block",
                ("npc", 6, 1, 6), ("door_slide", 5, 1, 0), (SettlementGenerator.RoomMarker, 2, 1, 2)),
        };
        var zones = StandardZones();
        var plain = CityGenerator.Generate(42, _content, zones);
        var a = CityGenerator.Generate(42, _content, zones, modules, 1.0);
        var b = CityGenerator.Generate(42, _content, zones, modules, 1.0);

        Assert.Equal(0, Count(plain, gold));
        Assert.True(Count(a, gold) > 0);
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                }

        // Every housing district holds the gold module, centred; no other district does.
        for (int gx = 0; gx < CityGenerator.Modules; gx++)
            for (int gz = 0; gz < CityGenerator.Modules; gz++)
            {
                var (mx, mz) = CityGenerator.ModuleOrigin(gx, gz);
                int goldHere = 0;
                for (int x = mx; x < mx + CityGenerator.ModuleSize; x++)
                    for (int z = mz; z < mz + CityGenerator.ModuleSize; z++)
                    {
                        if (a.Get(x, 0, z) == gold) goldHere++;
                    }

                bool housing = CityGenerator.RoleAt(gx, gz) == CityGenerator.Role.Housing;
                bool blockedByZone = zones.Any(zn => zn.Intersects(mx, mz, mx + CityGenerator.ModuleSize - 1, mz + CityGenerator.ModuleSize - 1));
                if (housing && !blockedByZone)
                {
                    Assert.Equal(12 * 12, goldHere);
                    Assert.Equal(gold, a.Get(mx + 10, 0, mz + 10)); // centred: (32 − 12) / 2 = 10
                }
                else
                {
                    Assert.Equal(0, goldHere);
                }
            }

        foreach (var zone in zones)
        {
            for (int x = zone.MinX; x <= zone.MaxX; x++)
                for (int z = zone.MinZ; z <= zone.MaxZ; z++)
                    for (int y = 1; y < a.Height; y++)
                    {
                        Assert.Equal(0, a.Get(x, y, z));
                    }
        }

        Assert.Contains(a.Markers, m => m.Type == "npc" && a.Get(m.LocalPos.X, 0, m.LocalPos.Z) == gold);
        Assert.Contains(a.Markers, m => m.Type == "door_slide" && a.Get(m.LocalPos.X, 0, m.LocalPos.Z) == gold);
    }

    [Fact]
    public void CityHouses_AreFurnished_WithoutFloorLamps()
    {
        var a = CityGenerator.Generate(7, _content, StandardZones());
        var ids = FurnitureIds();
        ushort lightWhite = Id("light_white");
        int furniture = 0;
        for (int x = 0; x < a.Width; x++)
            for (int y = 1; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    if (!IsFurniture(a, x, y, z, ids)) continue;
                    furniture++;
                    Assert.NotEqual(lightWhite, a.Get(x, y, z)); // the deck lamps light the rooms (#1808)
                }

        Assert.True(furniture >= 40, $"the city's houses should be furnished, got {furniture} cells");
    }

    // ---------------- the server: records gate modules ----------------

    private SvGameServer Started(GameContent content, string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
        };
        config.World.SettlementTemplateUse = Frequency.Frequent;
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FreshWorld_ComposesModules_RegistersTheirDoors_AndPinsTheDecision()
    {
        // A content whose ONLY modules are a gold house (village style) and a gold flat (town style), so a
        // stamped module is unmistakable in the world.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var whole = content.SettlementTemplates.Where(t => !t.IsModule).ToList();
        whole.Add(Box("gold_house", StructureRoles.House, "village", 6, 7, 6, "gold_block", ("npc", 3, 1, 3), ("door_hinge", 2, 1, 0)));
        whole.Add(Box("gold_flat", StructureRoles.House, "town", 6, 9, 6, "gold_block", ("npc", 3, 1, 3), ("door_slide", 2, 1, 0)));
        content.SetStructureTemplates(content.StationTemplates, whole);
        var gold = new BlockId(content.GetBlock("gold_block")!.NumericId.Value);

        for (long seed = 1; seed <= 12; seed++)
        {
            string world = $"modules_{seed}";
            var server = Started(content, world, seed, out var repo);
            using (repo)
            {
                var recs = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
                var boxes = server.SettlementsForTest;
                if (boxes.Count == 0)
                {
                    server.Stop();
                    continue;
                }

                Assert.All(recs, r => Assert.Equal(1, r.Modules)); // a fresh world pins "modules on"

                // Find a gold module in any inhabited settlement's box.
                (int X, int Y, int Z)? goldFloor = null;
                for (int i = 0; i < boxes.Count && goldFloor is null; i++)
                {
                    var box = boxes[i];
                    int gy = recs.First(r => r.X == box.MinX && r.Z == box.MinZ).GroundY;
                    for (int x = box.MinX; x <= box.MaxX && goldFloor is null; x++)
                        for (int z = box.MinZ; z <= box.MaxZ && goldFloor is null; z++)
                        {
                            if (server.World.GetBlock(new Vector3i(x, gy, z)) == gold)
                            {
                                goldFloor = (x, gy, z);
                            }
                        }
                }

                if (goldFloor is null)
                {
                    server.Stop();
                    continue; // only ruins / templates / skipped plots on this world — next seed
                }

                // The module's door is a real door entity and its resident a real NPC, both on gold ground.
                Assert.Contains(server.DoorSnapshots, d =>
                    server.World.GetBlock(new Vector3i((int)Math.Floor(d.Pos.X), (int)Math.Floor(d.Pos.Y) - 1, (int)Math.Floor(d.Pos.Z))) == gold);
                Assert.Contains(server.NpcSnapshots, n =>
                    server.World.GetBlock(new Vector3i((int)Math.Floor(n.Home.X), (int)Math.Floor(n.Home.Y) - 1, (int)Math.Floor(n.Home.Z))) == gold);
                server.Stop();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // An older save: the same world with its records marked "modules never" replays WITHOUT modules —
            // the layout under an existing world does not change.
            {
                var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, world));
                repo2.Initialize();
                var meta = repo2.LoadMetadata()!;
                foreach (var r in meta.Placements)
                {
                    r.Modules = 0;
                }

                repo2.SaveMetadata(meta);
                repo2.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                var server2 = Started(content, world, seed, out var repo3);
                using (repo3)
                {
                    Assert.All(server2.PlacementRecordsForTest.Where(r => r.Kind == "settlement"), r => Assert.Equal(0, r.Modules));
                    int goldCells = 0;
                    foreach (var box in server2.SettlementsForTest)
                    {
                        int gy = server2.PlacementRecordsForTest.First(r => r.Kind == "settlement" && r.X == box.MinX && r.Z == box.MinZ).GroundY;
                        for (int x = box.MinX; x <= box.MaxX; x++)
                            for (int z = box.MinZ; z <= box.MaxZ; z++)
                            {
                                if (server2.World.GetBlock(new Vector3i(x, gy, z)) == gold) goldCells++;
                            }
                    }

                    Assert.Equal(0, goldCells);
                    server2.Stop();
                }
            }

            return;
        }

        Assert.Fail("no seed in 1..12 stamped a gold module on meadowlands at Frequent — the composer should make this common");
    }
}
