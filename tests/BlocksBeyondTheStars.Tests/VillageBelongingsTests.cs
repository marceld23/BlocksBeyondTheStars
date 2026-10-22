// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// What is the villagers' and what is the player's in and around an intact village (a player building a big base around a
/// jungle village, 2026-10-17):
/// <list type="bullet">
/// <item>#2726 — an inhabited settlement spawns no scavenge cache (ruins still do), and a cache an older world already holds
/// in an intact village is the villagers': looting it is refused with whose it is, and the container list marks it.</item>
/// <item>#2727 — a block a player placed inside a village box can be mined again; the village's own blocks and its ground
/// stay protected, even when a player dyed them.</item>
/// <item>#2728 — a base whose zone takes a village in counts neither the villagers' beds nor their machines.</item>
/// </list>
/// </summary>
public sealed class VillageBelongingsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    /// <summary>A hand-made inhabited template with a "loot" marker (local 7,1,7), 9 × 6 × 9.</summary>
    private const string Roundhouse = "stone_roundhouse_home";

    private const string SettlementCache = "loot_settlement_";

    private static string? LastRejection(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;

    private static string Text(string key, int n = 0)
        => Content.CreateLocalizer(GameLocale.English).Get(key)
            .Replace("{n}", n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{max}", SvGameServer.BaseResidentCapForTest.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>A block of the village's own — written like the settlement stamp writes it, without an owner.</summary>
    private static void Village(NpcLifeWorld w, int x, int y, int z, string key, int shape = 0)
        => w.Server.World.SetBlock(new Vector3i(x, y, z), w.Block(key), 0, 0, shape);

    // ---------------------------------------------------------------- #2726: the loot caches

    [Fact]
    public void AnInhabitedTemplate_SpawnsNoCache_TheSameBuildingAsARuinStillDoes()
    {
        using var w = new NpcLifeWorld(Content, "village_no_cache");

        w.Server.StampSettlementForTest(Roundhouse, Cx - 20, w.PadY, Cz - 20);
        Assert.Contains(w.Server.SettlementMarkers, m => m.Type == "loot"); // the template still carries its marker
        Assert.DoesNotContain(w.Server.Containers, c => c.Id.StartsWith(SettlementCache, StringComparison.Ordinal));

        w.Server.StampSettlementForTest(Roundhouse, Cx + 6, w.PadY, Cz + 6, ruined: true, name: "Ruins of Testvale");
        var cache = Assert.Single(w.Server.Containers, c => c.Id.StartsWith(SettlementCache, StringComparison.Ordinal));
        Assert.Equal(new Vector3i(Cx + 6 + 7, w.PadY + 1, Cz + 6 + 7), cache.Position);
        Assert.NotEmpty(cache.Items);
    }

    [Fact]
    public void AnOldCacheInAnIntactVillage_IsTheVillagers_ACapsuleThereAndACacheOutsideAreAnybodys()
    {
        using var w = new NpcLifeWorld(Content, "village_old_cache");
        w.Server.RegisterTestSettlementForTest("Testvale", new Vector3i(Cx + 6, w.PadY, Cz - 6), new Vector3i(Cx + 16, w.PadY + 8, Cz + 6));
        string planet = w.Server.World.LocationId;
        static string CacheId(Vector3i c) => $"{SettlementCache}loot_{c.X}_{c.Y}_{c.Z}";
        var inVillage = new Vector3i(Cx + 10, w.Feet, Cz);
        var outside = new Vector3i(Cx - 10, w.Feet, Cz);
        var capsule = new Vector3i(Cx + 12, w.Feet, Cz + 3);
        foreach (var cell in new[] { inVillage, outside })
        {
            w.Server.AddContainerForTest(new StoredContainer
            {
                Id = CacheId(cell),
                Planet = planet,
                Kind = "salvage",
                Position = cell,
                Items = { new ItemStack("iron_plate", 3) },
            });
        }

        w.Server.AddContainerForTest(new StoredContainer
        {
            Id = "salvage_test",
            Planet = planet,
            Kind = "salvage_capsule", // a death capsule dropped in the village: still anybody's to loot
            Position = capsule,
            Items = { new ItemStack("cable", 2) },
        });

        // The container list marks the village's cache, so the HUD offers no "G: loot" there.
        var list = w.Transport.Sent.Select(s => s.Msg).OfType<ContainerList>().Last();
        Assert.Equal("Testvale", list.Containers.Single(c => c.Id == CacheId(inVillage)).OwnedBy);
        Assert.Equal(string.Empty, list.Containers.Single(c => c.Id == CacheId(outside)).OwnedBy);
        Assert.Equal(string.Empty, list.Containers.Single(c => c.Id == "salvage_test").OwnedBy);

        // G at the village's cache: refused, and the refusal says whose it is.
        var p = w.Player.State;
        int plates = p.Inventory.CountOf("iron_plate"), cables = p.Inventory.CountOf("cable");
        p.Position = new Vector3f(inVillage.X + 0.5f, w.Feet, inVillage.Z + 1.5f);
        w.Server.LootContainer(p.PlayerId, CacheId(inVillage));
        Assert.Equal("@srv.loot.village_owned:Testvale", LastRejection(w));
        Assert.Equal(plates, p.Inventory.CountOf("iron_plate"));
        Assert.Equal(3, w.Server.Containers.Single(c => c.Id == CacheId(inVillage)).Items.Sum(s => s.Count));

        // The capsule beside it and the cache out in the open are looted as ever.
        p.Position = new Vector3f(capsule.X + 0.5f, w.Feet, capsule.Z + 1.5f);
        w.Server.LootContainer(p.PlayerId, "salvage_test");
        Assert.Equal(cables + 2, p.Inventory.CountOf("cable"));
        p.Position = new Vector3f(outside.X + 0.5f, w.Feet, outside.Z + 1.5f);
        w.Server.LootContainer(p.PlayerId, CacheId(outside));
        Assert.Equal(plates + 3, p.Inventory.CountOf("iron_plate"));

        // The sentence fills its {name} in both mandatory languages (the client's generic server-token path).
        Assert.Contains("{name}", Content.CreateLocalizer(GameLocale.English).Get("srv.loot.village_owned"), StringComparison.Ordinal);
        Assert.Contains("{name}", Content.CreateLocalizer(GameLocale.German).Get("srv.loot.village_owned"), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- #2727: the player's own blocks

    [Fact]
    public void ABlockAPlayerPlacesInAVillage_IsMinedAgain_TheVillagesBlocksAndGroundStayProtected()
    {
        using var w = new NpcLifeWorld(Content, "village_own_blocks");
        int ox = Cx + 4, oz = Cz - 4;
        w.Server.StampSettlementForTest(Roundhouse, ox, w.PadY, oz);
        var p = w.Player.State;
        p.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        p.Inventory.Add("stone", 4, Content.MaxStackOf("stone"));

        // A free cell of the village box one above the floor that the layout leaves empty: a lane, a yard corner.
        Vector3i? free = null;
        for (int x = ox; x < ox + 9 && free is null; x++)
            for (int z = oz; z < oz + 9 && free is null; z++)
            {
                var c = new Vector3i(x, w.Feet, z);
                if (!w.Server.IsSettlementLayoutCellForTest(c) && w.Server.World.GetBlock(c).IsAir)
                {
                    free = c;
                }
            }

        Assert.NotNull(free);
        var lane = free!.Value;
        Assert.True(w.Server.IsSettlementBlock(lane));
        p.Position = new Vector3f(lane.X + 0.5f, w.Feet, lane.Z + 0.5f) + new Vector3f(lane.X < ox + 4 ? 2f : -2f, 0f, 0f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, lane.X, lane.Y, lane.Z, "stone");
        Assert.Equal("stone", Content.BlockById(w.Server.World.GetBlock(lane))?.Key);
        Assert.False(w.Server.IsSettlementProtectedForTest(lane));
        w.Server.MineBlock(NpcLifeWorld.Owner, lane.X, lane.Y, lane.Z);
        Assert.True(w.Server.World.GetBlock(lane).IsAir, $"the player's own block at {lane} could not be mined again ({LastRejection(w)})");

        // A wall of the village: protected — also after a player dyed it (a dye writes a player edit on the village's block).
        Vector3i? own = null;
        for (int x = ox; x < ox + 9 && own is null; x++)
            for (int z = oz; z < oz + 9 && own is null; z++)
            {
                var c = new Vector3i(x, w.Feet, z);
                if (w.Server.IsSettlementLayoutCellForTest(c) && !w.Server.World.GetBlock(c).IsAir)
                {
                    own = c;
                }
            }

        Assert.NotNull(own);
        var wall = own!.Value;
        w.Server.World.SetBlock(wall, w.Server.World.GetBlock(wall), 0, 0, w.Server.World.GetShape(wall), NpcLifeWorld.Owner);
        Assert.True(w.Server.IsSettlementProtectedForTest(wall));
        p.Position = new Vector3f(wall.X + 0.5f, w.Feet, wall.Z + 0.5f) + new Vector3f(wall.X < ox + 4 ? -1.5f : 1.5f, 0f, 0f);
        w.Server.MineBlock(NpcLifeWorld.Owner, wall.X, wall.Y, wall.Z);
        Assert.False(w.Server.World.GetBlock(wall).IsAir);
        Assert.Equal("@srv.protect.settlement", LastRejection(w));

        // The ground of the village — the floor row and the plinth under it — stays protected, dyed or not.
        foreach (var ground in new[] { new Vector3i(lane.X, w.PadY, lane.Z), new Vector3i(lane.X, w.PadY - 1, lane.Z) })
        {
            Assert.False(w.Server.World.GetBlock(ground).IsAir, $"no ground at {ground}");
            Assert.True(w.Server.IsSettlementProtectedForTest(ground));
            w.Server.World.SetBlock(ground, w.Server.World.GetBlock(ground), 0, 0, 0, NpcLifeWorld.Owner);
            Assert.True(w.Server.IsSettlementProtectedForTest(ground), $"dyed ground at {ground} came loose");
        }
    }

    /// <summary>The cells one above the floor of a roundhouse stamped at (ox, oz) that its layout leaves empty (lanes, yard).</summary>
    private static List<Vector3i> FreeVillageCells(NpcLifeWorld w, int ox, int oz)
    {
        var cells = new List<Vector3i>();
        for (int x = ox; x < ox + 9; x++)
            for (int z = oz; z < oz + 9; z++)
            {
                var c = new Vector3i(x, w.Feet, z);
                if (!w.Server.IsSettlementLayoutCellForTest(c) && w.Server.World.GetBlock(c).IsAir)
                {
                    cells.Add(c);
                }
            }

        return cells;
    }

    private static PaintBlockIntent Paint(Vector3i c) => new() { X = c.X, Y = c.Y, Z = c.Z, Pixels = new string('3', 1024) };

    private static int DesignAt(NpcLifeWorld w, Vector3i c) => ShapeCode.DesignOf(w.Server.World.GetShape(c));

    [Fact]
    public void APaintedVillageOrLodgeBlock_TakesThePaint_ButNeverBecomesThePlayers_APlacedBlockIsPaintedAndMined()
    {
        // Decoration is never forbidden: a village's and a lodge's blocks take paint. But a player's edit row is what makes a
        // block in a village box count as theirs and minable (#2727), so a stroke on a settlement's own block is written
        // without the painter as editor — paint-then-mine must never take a village apart.
        using var w = new NpcLifeWorld(Content, "village_paint");
        int ox = Cx + 4, oz = Cz - 4;
        w.Server.StampSettlementForTest(Roundhouse, ox, w.PadY, oz);
        w.Player.State.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));
        w.Player.State.Inventory.Add("stone", 4, Content.MaxStackOf("stone"));
        var free = FreeVillageCells(w, ox, oz);
        Assert.True(free.Count >= 2, "the roundhouse leaves no two free cells");
        Vector3f Beside(Vector3i c, float dx) => new(c.X + 0.5f + dx, w.Feet, c.Z + 0.5f);
        int Rejections() => w.Transport.Sent.Count(s => s.Msg is ActionRejected);

        void PaintAccepted(Vector3i cell, Vector3f stand)
        {
            w.Player.State.Position = stand;
            w.Player.Throttles.NextPaintAt = 0; // the brush's two-second spam guard is not what this test is about
            int before = Rejections();
            w.Server.PaintBlockForTest(w.Player, Paint(cell));
            Assert.Equal(before, Rejections());
            Assert.NotEqual(0, DesignAt(w, cell));
        }

        void StaysTheVillages(Vector3i cell, string refusal)
        {
            Assert.False(w.Server.CellPlayerEditedForTest(cell), $"the paint at {cell} signed the cell as the player's");
            Assert.True(w.Server.IsSettlementProtectedForTest(cell));
            ushort block = w.Server.World.GetBlock(cell).Value;
            w.Server.MineBlock(NpcLifeWorld.Owner, cell.X, cell.Y, cell.Z);
            Assert.Equal(block, w.Server.World.GetBlock(cell).Value);
            Assert.Equal(refusal, LastRejection(w));
        }

        // Sand that fell into a lane — the server's block, no owner: painted, and still the village's.
        var sand = free[0];
        w.Server.World.SetBlock(sand, w.Block("sand"));
        float side = sand.X < ox + 4 ? 2f : -2f;
        PaintAccepted(sand, Beside(sand, side));
        StaysTheVillages(sand, "@srv.protect.settlement");

        // A wall of the roundhouse: painted, still protected.
        Vector3i? ownWall = null;
        for (int x = ox; x < ox + 9 && ownWall is null; x++)
            for (int z = oz; z < oz + 9 && ownWall is null; z++)
            {
                var c = new Vector3i(x, w.Feet, z);
                if (w.Server.IsSettlementLayoutCellForTest(c) && !w.Server.World.GetBlock(c).IsAir)
                {
                    ownWall = c;
                }
            }

        Assert.NotNull(ownWall);
        var wall = ownWall!.Value;
        PaintAccepted(wall, Beside(wall, wall.X < ox + 4 ? -1.5f : 1.5f));
        StaysTheVillages(wall, "@srv.protect.settlement");

        // The rock of a lodge's yard (no part of the hut's layout): painted, still the lodge's.
        string lodge = w.Server.SpawnLodgeForTest(Cx - 22, w.PadY, Cz - 6, "miner", 1);
        var origin = w.Server.LodgesForTest.Single(l => l.Key == lodge).Origin;
        Vector3i? yardCell = null;
        for (int dx = 0; dx < 24 && yardCell is null; dx++)
            for (int dz = 0; dz < 24 && yardCell is null; dz++)
            {
                var c = new Vector3i(origin.X + dx, w.Feet, origin.Z + dz);
                if (w.Server.IsLodgeBlockForTest(c) && w.Server.World.GetBlock(c).IsAir)
                {
                    yardCell = c;
                }
            }

        Assert.NotNull(yardCell);
        var yard = yardCell!.Value;
        w.Server.World.SetBlock(yard, w.Block("stone"));
        PaintAccepted(yard, new Vector3f(yard.X + 0.5f, w.Feet, yard.Z + 2.5f));
        Assert.False(w.Server.CellPlayerEditedForTest(yard));
        Assert.True(w.Server.IsSettlementProtectedForTest(yard));

        // A block the player placed in the village is painted under their name — and comes out again.
        var own = free[1];
        w.Player.State.Position = Beside(own, own.X < ox + 4 ? 2f : -2f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, own.X, own.Y, own.Z, "stone");
        Assert.Equal("stone", Content.BlockById(w.Server.World.GetBlock(own))?.Key);
        PaintAccepted(own, w.Player.State.Position);
        Assert.True(w.Server.CellPlayerEditedForTest(own));
        w.Server.MineBlock(NpcLifeWorld.Owner, own.X, own.Y, own.Z);
        Assert.True(w.Server.World.GetBlock(own).IsAir, $"the player's painted block at {own} could not be mined ({LastRejection(w)})");

        // The ownerless strokes are stored like any paint: after a reload the designs are still on the village's blocks,
        // and the cells are still nobody's.
        int sandDesign = DesignAt(w, sand), wallDesign = DesignAt(w, wall);
        w.Restart();
        Assert.Equal(sandDesign, DesignAt(w, sand));
        Assert.Equal(wallDesign, DesignAt(w, wall));
        Assert.False(w.Server.CellPlayerEditedForTest(sand));
        Assert.False(w.Server.CellPlayerEditedForTest(wall));
    }

    // ---------------------------------------------------------------- #2728: a base around a village

    [Fact]
    public void ABaseAroundAVillage_CountsNeitherTheVillagersBedsNorTheirMachines_TheResidentCapStays()
    {
        using var w = new NpcLifeWorld(Content, "village_in_base");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, Cx, w.Feet, Cz, "base_core");
        int baseId = w.Server.BaseSnapshots.Single().Id;

        // The village reaches into the base's core zone; its hut is a closed room inside the base's reach.
        w.Server.RegisterTestSettlementForTest("Testvale", new Vector3i(Cx + 5, w.PadY, Cz - 6), new Vector3i(Cx + 17, w.PadY + 8, Cz + 6));
        int hx = Cx + 12, hz = Cz;
        for (int dx = -3; dx <= 3; dx++)
            for (int dz = -3; dz <= 3; dz++)
            {
                if (Math.Abs(dx) == 3 || Math.Abs(dz) == 3)
                {
                    for (int y = 0; y < 3; y++)
                    {
                        Village(w, hx + dx, w.Feet + y, hz + dz, "stone");
                    }
                }

                Village(w, hx + dx, w.Feet + 3, hz + dz, "stone"); // the roof
            }

        // Two villagers' beds and two of their terminals in the hut, a third terminal in the lane inside the core zone.
        int head = ShapeCode.Pack((int)BlockShape.BedHead, 0);
        foreach (int bx in new[] { hx - 2, hx + 1 })
        {
            Village(w, bx, w.Feet, hz - 2, "bed", head);
            Village(w, bx, w.Feet, hz - 1, "bed", FurnitureShapes.BedPartnerDescriptor(head));
        }

        Village(w, hx, w.Feet, hz + 2, "data_cache");
        Village(w, hx + 2, w.Feet, hz + 2, "data_cache");
        Village(w, Cx + 6, w.Feet, Cz + 4, "data_cache");

        w.Server.ScanBaseLifeForTest();
        Assert.Empty(w.Server.BaseResidentsForTest(baseId)); // three machines and two beds of the village found nothing
        Assert.Equal(0, w.Server.BaseFurnitureForTest(baseId).Beds);

        w.Player.State.Inventory.Add("bed", 8, 16);
        void PlaceBed(int x, int z, Vector3f stand)
        {
            w.Player.State.Position = stand;
            w.Server.PlaceBlock(NpcLifeWorld.Owner, x, w.Feet, z, "bed", yaw: 0);
            Assert.Equal("bed", Content.BlockById(w.Server.World.GetBlock(new Vector3i(x, w.Feet, z)))?.Key);
        }

        // The player's own bed in the very same hut counts (#2727: theirs, not the village's) and founds the base: the
        // settler and that bed's resident — not two more for the villagers' beds.
        PlaceBed(hx - 1, hz, new Vector3f(hx + 1.5f, w.Feet, hz + 1.5f));
        Assert.Contains(Text("srv.base.bed_founds", 2), w.MessagesToPlayer());
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);

        // Three more of the player's beds bring it to five residents — each counted, none "full" early for the villagers' beds.
        PlaceBed(Cx - 6, Cz + 2, new Vector3f(Cx - 7.5f, w.Feet, Cz + 2.5f));
        Assert.Contains(Text("srv.base.bed_counts", 3), w.MessagesToPlayer());
        PlaceBed(Cx - 3, Cz + 2, new Vector3f(Cx - 4.5f, w.Feet, Cz + 2.5f));
        Assert.Contains(Text("srv.base.bed_counts", 4), w.MessagesToPlayer());
        PlaceBed(Cx + 2, Cz + 2, new Vector3f(Cx + 0.5f, w.Feet, Cz + 2.5f));
        Assert.Contains(Text("srv.base.bed_counts", 5), w.MessagesToPlayer());
        Assert.DoesNotContain(Text("srv.base.bed_full"), w.MessagesToPlayer());
        Assert.Equal(5, w.Server.BaseResidentsForTest(baseId).Count);

        var core = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<BaseList>().Last()
            .Bases.Single(b => b.Id == baseId);
        Assert.Equal((5, SvGameServer.BaseResidentCapForTest), (core.Residents, core.ResidentCap));
        Assert.Equal(4, w.Server.BaseFurnitureForTest(baseId).Beds);
    }
}
