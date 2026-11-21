// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// The abandoned bed (terrain generation 45; Justus' "Komische alte Betten" on the giant wreck Raphae, Marcel: a ruin's bed must
/// draw no settler). A ruin's or a wreck's bed is <c>bed_abandoned</c>: it looks like today's two-cell bed and works as none — no
/// home spawn, no rest heal, no settler, not even the founding one — while a bed a player builds (and a village's) works as
/// before. The giant wrecks, the ruined structures the settlement stamp writes and the buried G.D.S. ruins build their beds that
/// way from generation 45; every older world keeps its beds.
/// </summary>
public sealed class AbandonedBedTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_abandoned_bed_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a locked temp file is no test failure
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    // ---------------------------------------------------------------- the block

    [Fact]
    public void TheAbandonedBed_LooksLikeTheBed_ButIsNoMachine_AndYieldsAPlainBed()
    {
        var bed = Content.GetBlock(FurnitureShapes.BedKey)!;
        var abandoned = Content.GetBlock(FurnitureShapes.AbandonedBedKey)!;
        Assert.Equal(BlockFaceTextures.Picture, abandoned.TileKind);
        Assert.Equal("building", abandoned.Category); // a machine would count toward founding a base (#2677)
        Assert.False(abandoned.Airtight);
        Assert.Equal((int)BlockShape.BedHead, PropShapes.DefaultPlaceShape(abandoned.Key));
        Assert.True(PropShapes.IsStampedForm(abandoned.Key, (int)BlockShape.BedFoot));

        // Every part and side shows the bed's own tile over the bed's own region: the same drawing, so a texture pack's bed
        // dresses both.
        for (int p = 0; p < ShapeParts.PartCount; p++)
        {
            for (int s = 0; s < ShapeParts.SideCount; s++)
            {
                var mine = BlockFaceTextures.Resolve(abandoned.Faces, (ShapePart)p, (FaceSide)s);
                var its = BlockFaceTextures.Resolve(bed.Faces, (ShapePart)p, (FaceSide)s);
                Assert.Equal(its is null, mine is null);
                if (mine is not null)
                {
                    Assert.Equal(FurnitureShapes.BedKey, mine.Tile);
                    Assert.Equal(its!.Rect, mine.Rect);
                }
            }
        }

        var drop = Assert.Single(abandoned.Drops);
        Assert.Equal("bed", drop.Item);
        Assert.DoesNotContain(Content.Items.Values, i => i.PlacesBlock == abandoned.Key); // nobody crafts or places one
    }

    [Fact]
    public void TheAbandonedBeds_AreGeneration45_NewWorldsAreNewer_WithSaveVersion17()
    {
        Assert.Equal(45, WorldDescription.AbandonedBedsGeneration);
        Assert.True(WorldDescription.AbandonedBedsGeneration < WorldDescription.CurrentTerrainGeneration); // 46: the lively places
        Assert.True(AbandonedBeds.Applies(WorldDescription.CurrentTerrainGeneration));
        Assert.False(AbandonedBeds.Applies(WorldDescription.GuardianGiantsGeneration)); // 44: the guardian giants
        Assert.False(AbandonedBeds.Applies(WorldDescription.CivilizationsGeneration));
        Assert.True(BlocksBeyondTheStars.Shared.State.WorldMetadata.CurrentSaveVersion >= 17); // 18: the sky-lights' blocks (#3894)
    }

    // ---------------------------------------------------------------- the pass

    /// <summary>A 6 × 3 × 6 grid with a stone floor at row 0 and the given beds (cell, descriptor) on row 1.</summary>
    private static VoxelGrid Room(params (int X, int Z, int Shape)[] beds)
    {
        var g = new VoxelGrid(6, 3, 6);
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 6; z++)
            {
                g.Set(x, 0, z, Id("stone"));
            }
        }

        foreach (var (x, z, shape) in beds)
        {
            g.Set(x, 1, z, Id("bed"), shape: shape);
        }

        return g;
    }

    [Fact]
    public void TwoShortBedsInARow_BecomeOneTwoCellBed_OfTheAbandonedKind()
    {
        var g = Room((1, 1, 0), (1, 2, 0)); // two plain cube beds, as a giant ship's template placed them
        Assert.Equal(2, AbandonedBeds.Apply(g, Content, footAllowed: null));

        Assert.Equal(Id(FurnitureShapes.AbandonedBedKey), g.Get(1, 1, 1));
        Assert.Equal(Id(FurnitureShapes.AbandonedBedKey), g.Get(1, 1, 2));
        Assert.Equal(ShapeCode.Pack(BlockShape.BedHead, 0), g.ShapeAt(1, 1, 1));
        Assert.Equal(FurnitureShapes.BedPartnerDescriptor(g.ShapeAt(1, 1, 1)), g.ShapeAt(1, 1, 2));
        Assert.DoesNotContain(Id("bed"), g.Blocks);
    }

    [Fact]
    public void ALoneShortBed_GetsAFoot_OnlyWhereTheCallerAllowsOne()
    {
        // Not allowed: a head alone (pillow and headboard — today's bed, cut short), nothing grows into the room.
        var alone = Room((1, 1, 0));
        AbandonedBeds.Apply(alone, Content, footAllowed: null);
        Assert.Equal(ShapeCode.Pack(BlockShape.BedHead, 0), alone.ShapeAt(1, 1, 1));
        Assert.Equal(0, alone.Get(1, 1, 2));

        // Allowed: the foot on the free floor cell toward +Z (a cube says no direction; +Z first).
        var giant = Room((1, 1, 0));
        AbandonedBeds.Apply(giant, Content, (x, y, z) => true);
        Assert.Equal(Id(FurnitureShapes.AbandonedBedKey), giant.Get(1, 1, 2));
        Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(giant.ShapeAt(1, 1, 2)));

        // A cell the caller keeps (a marker's column) is skipped: the foot goes the other way.
        var kept = Room((1, 1, 0));
        AbandonedBeds.Apply(kept, Content, (x, y, z) => z != 2);
        Assert.Equal(0, kept.Get(1, 1, 2));
        Assert.Equal(Id(FurnitureShapes.AbandonedBedKey), kept.Get(1, 1, 0));
        Assert.True(FurnitureShapes.TryBedPartnerOffset(kept.ShapeAt(1, 1, 1), out int dx, out int dz));
        Assert.Equal((0, -1), (dx, dz));
    }

    [Fact]
    public void ATwoCellBed_KeepsItsHalves_AndAnOrientedShortBed_PairsOnlyAlongItsAxis()
    {
        int head = ShapeCode.Pack(BlockShape.BedHead, 3); // foot toward +X
        int slabAlongX = ShapeCode.Pack(PropShapes.BedSingleCell, 1); // a short bed lying along X
        var g = Room((1, 1, head), (2, 1, FurnitureShapes.BedPartnerDescriptor(head)), (3, 4, slabAlongX), (3, 5, slabAlongX));
        AbandonedBeds.Apply(g, Content, footAllowed: null);

        Assert.Equal(head, g.ShapeAt(1, 1, 1));
        Assert.Equal(FurnitureShapes.BedPartnerDescriptor(head), g.ShapeAt(2, 1, 1));

        // The two short beds lie side by side across Z, not end to end: each stays a bed of its own (a head alone).
        Assert.Equal(ShapeCode.Pack(BlockShape.BedHead, 1), g.ShapeAt(3, 1, 4));
        Assert.Equal(ShapeCode.Pack(BlockShape.BedHead, 1), g.ShapeAt(3, 1, 5));
        Assert.DoesNotContain(Id("bed"), g.Blocks);
    }

    // ---------------------------------------------------------------- the giant wrecks

    [Fact]
    public void AGiantWreck_OfAGeneration45World_HasOnlyAbandonedTwoCellBeds_AndItsMarkersStay()
    {
        var liner = Content.GetGiantShip("liner")!;
        var old = GiantShipGenerator.BuildSpace(liner, 5, Content);
        var now = GiantShipGenerator.BuildSpace(liner, 5, Content, abandonBeds: true);
        Assert.Contains(Id("bed"), old.Blocks); // the template's beds: plain cubes, the "old beds" of the report
        Assert.DoesNotContain(Id("bed"), now.Blocks);

        int heads = 0, feet = 0;
        for (int i = 0; i < now.Blocks.Length; i++)
        {
            if (now.Blocks[i] == Id(FurnitureShapes.AbandonedBedKey))
            {
                int form = ShapeCode.ShapeOf(now.Shapes.TryGetValue(i, out int s) ? s : 0);
                heads += form == (int)BlockShape.BedHead ? 1 : 0;
                feet += form == (int)BlockShape.BedFoot ? 1 : 0;
            }
        }

        Assert.True(heads > 0 && feet > 0, $"heads {heads}, feet {feet}");
        Assert.Equal(old.Markers.Select(m => (m.Type, m.LocalPos)), now.Markers.Select(m => (m.Type, m.LocalPos)));
        Assert.Equal(old.Entrance, now.Entrance);

        // On a planet the same: the chests, sounds and entrances a stamped wreck re-derives on every load never move.
        var planetOld = GiantShipGenerator.BuildPlanet(liner, 5, 1, Content, "grass", atmosphere: true);
        var planetNow = GiantShipGenerator.BuildPlanet(liner, 5, 1, Content, "grass", atmosphere: true, abandonBeds: true);
        Assert.Equal(planetOld.Structure.Markers.Select(m => (m.Type, m.LocalPos)),
            planetNow.Structure.Markers.Select(m => (m.Type, m.LocalPos)));
    }

    // ---------------------------------------------------------------- what a bed does — and an abandoned one does not

    private SvGameServer Started(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceLodges = false,
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AnAbandonedBed_SetsNoHomeSpawn_AndHealsNobody_WhileABedStillDoes()
    {
        var server = Started("abandoned_spawn", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Explorer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Health = 50f;
            server.TickForTest(2.0);
            float baseline = p.State.Health - 50f;

            server.World.SetBlock(new Vector3i(1, 64, 0), Content.GetBlock(FurnitureShapes.AbandonedBedKey)!.NumericId, 0, 0,
                ShapeCode.Pack(BlockShape.BedHead, 0));
            p.State.Health = 50f;
            server.TickForTest(2.0);
            Assert.True(p.State.Health - 50f < baseline + 1f, $"an abandoned bed must not heal (baseline {baseline}, got {p.State.Health - 50f})");

            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
            Assert.True(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "an abandoned bed is nobody's home");

            server.World.SetBlock(new Vector3i(1, 64, 0), Content.GetBlock(FurnitureShapes.BedKey)!.NumericId, 0, 0,
                ShapeCode.Pack(BlockShape.BedHead, 0));
            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "a bed still sets the home spawn");
        }
    }

    private static void AbandonedBed(NpcLifeWorld w, int x, int z)
    {
        int head = ShapeCode.Pack(BlockShape.BedHead, 0);
        w.Set(x, w.Feet, z, FurnitureShapes.AbandonedBedKey, head);
        w.Set(x, w.Feet, z + 1, FurnitureShapes.AbandonedBedKey, FurnitureShapes.BedPartnerDescriptor(head));
    }

    [Fact]
    public void AnAbandonedBed_InABase_DrawsNoSettler_NotEvenTheFirst_WhileABedDoes()
    {
        using var w = new NpcLifeWorld(Content, "abandoned_settlers");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core"); // no machines
        int baseId = w.Server.BaseSnapshots.Single(b => b.OwnerId == w.Player.State.PlayerId).Id;

        // Three abandoned beds in the core zone (even written as the player's): no bed of the base, no founding, nobody.
        AbandonedBed(w, NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        AbandonedBed(w, NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        AbandonedBed(w, NpcLifeWorld.Cx + 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(0, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Empty(w.Server.BaseResidentsForTest(baseId));

        // One bed the player built: the base is founded and its sleeper moves in.
        var bed = w.Bed(NpcLifeWorld.Cx + 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Bed == bed);
    }

    // ---------------------------------------------------------------- the ruined structures the settlement stamp writes

    private static (int Bed, int Abandoned) BedsIn(NpcLifeWorld w, int ox, int oz)
    {
        var t = Content.SettlementTemplateByKey("stone_roundhouse_home")!;
        int bed = 0, abandoned = 0;
        for (int x = 0; x < t.Width; x++)
        {
            for (int y = 0; y < t.Height; y++)
            {
                for (int z = 0; z < t.Length; z++)
                {
                    ushort b = w.Server.World.GetBlock(new Vector3i(ox + x, w.PadY + y, oz + z)).Value;
                    bed += b == Id("bed") ? 1 : 0;
                    abandoned += b == Id(FurnitureShapes.AbandonedBedKey) ? 1 : 0;
                }
            }
        }

        return (bed, abandoned);
    }

    [Fact]
    public void ARuinedVillage_OfAGeneration45World_HasAbandonedBeds_AnIntactOne_AndAnOlderWorldsRuin_KeepTheirBeds()
    {
        using (var w = new NpcLifeWorld(Content, "abandoned_ruin"))
        {
            w.Server.StampSettlementForTest("stone_roundhouse_home", NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20);
            w.Server.StampSettlementForTest("stone_roundhouse_home", NpcLifeWorld.Cx + 6, w.PadY, NpcLifeWorld.Cz + 6, ruined: true,
                name: "Ruins of Testvale");

            var intact = BedsIn(w, NpcLifeWorld.Cx - 20, NpcLifeWorld.Cz - 20);
            Assert.Equal(2, intact.Bed); // the villagers' two-cell bed works as it always did
            Assert.Equal(0, intact.Abandoned);
            var ruin = BedsIn(w, NpcLifeWorld.Cx + 6, NpcLifeWorld.Cz + 6);
            Assert.Equal(0, ruin.Bed);
            Assert.Equal(2, ruin.Abandoned); // the same bed, both halves, abandoned
        }

        using var older = new NpcLifeWorld(Content, "abandoned_ruin_44",
            configure: c => c.World.TerrainGeneration = WorldDescription.GuardianGiantsGeneration);
        older.Server.StampSettlementForTest("stone_roundhouse_home", NpcLifeWorld.Cx + 6, older.PadY, NpcLifeWorld.Cz + 6, ruined: true,
            name: "Ruins of Testvale");
        Assert.Equal((2, 0), BedsIn(older, NpcLifeWorld.Cx + 6, NpcLifeWorld.Cz + 6));
    }
}
