// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3615: the flight view's side of big structures, Unity-free — one feed turns the received design messages into whole
/// designs (a giant's parts joined, everything else passed through as it came), a changed cell re-meshes only the chunks it
/// touches, and a big hull's far skin is a coarse model of its outside only.
/// </summary>
public sealed class FlightStructureMeshTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static SpaceShipDesign Part(string id, int serial, int index, int count, params int[] xs) => new()
    {
        Id = id,
        Kind = "station",
        Serial = serial,
        PartIndex = index,
        PartCount = count,
        X = xs,
        Y = new int[xs.Length],
        Z = new int[xs.Length],
        Block = xs.Select(_ => (ushort)1).ToArray(),
    };

    [Fact]
    public void TheFeed_PassesAWholeDesignThrough_Unchanged()
    {
        var feed = new SpaceDesignFeed();
        var got = new List<SpaceShipDesign>();
        feed.DesignReady += got.Add;
        var whole = Part("station:a", 0, 0, 0, 1, 2, 3);

        feed.Feed(whole);

        Assert.Same(whole, Assert.Single(got)); // every older design, and every design that fits one message, is the very object
    }

    [Fact]
    public void TheFeed_RaisesAPartedDesignOnce_WhenItsLastPartArrived()
    {
        var feed = new SpaceDesignFeed();
        var got = new List<SpaceShipDesign>();
        feed.DesignReady += got.Add;

        feed.Feed(Part("derelict:giant-s-0", 4, 2, 3, 5));
        feed.Feed(Part("derelict:giant-s-0", 4, 0, 3, 1, 2));
        Assert.Empty(got);
        Assert.Equal(1, feed.PendingCount);
        feed.Feed(Part("derelict:giant-s-0", 4, 1, 3, 3, 4));

        var design = Assert.Single(got);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, design.X);
        Assert.Equal(0, feed.PendingCount);
    }

    [Fact]
    public void TheFeed_DropsHalfADesign_OnReset_AndWhenTheStructureLeaves()
    {
        var feed = new SpaceDesignFeed();
        var got = new List<SpaceShipDesign>();
        feed.DesignReady += got.Add;

        feed.Feed(Part("derelict:giant-s-0", 1, 0, 2, 1));
        feed.Reset(); // the flight closed, the world changed, a new session
        feed.Feed(Part("derelict:giant-s-0", 1, 1, 2, 2));
        Assert.Empty(got); // part 0 went with the reset: never a half design on screen

        feed.Feed(Part("derelict:giant-s-1", 2, 0, 2, 1));
        feed.Forget("derelict:giant-s-1");
        Assert.Equal(1, feed.PendingCount); // only s-0's lone part 1 still waits
        feed.Feed(Part("derelict:giant-s-1", 2, 1, 2, 2));
        Assert.Empty(got);
    }

    /// <summary>The wiring against a real server: a giant station's design reaches the client in parts, and the feed attached
    /// to that client hands out exactly one whole design — every cell the parts carried.</summary>
    [Fact]
    public void AnAttachedFeed_HandsOutAGiantStation_OnceAndWhole()
    {
        using var h = new ClientServerHarness(Content.Value, c => c.Rules.FreeSpaceFlight = true, passObjects: false);
        var feed = SpaceDesignFeed.Attach(h.Client);
        h.Join("Pilot");
        string pid = h.JoinAccepted!.PlayerId;
        ushort wall = Content.Value.GetBlock("iron_wall")!.NumericId.Value;
        var cells = SpaceDesignAssemblerTests.GiantCells().ToDictionary(c => c, _ => new BlockId(wall));
        const string id = "derelict:giant-feed-0";
        h.Server.RegisterDerivedStationForTest(id, "Feed Giant", cells, new Vector3i(54, 1, 2), new Vector3f(0f, 300f, -400f));

        int parts = 0;
        var whole = new List<SpaceShipDesign>();
        h.Client.SpaceShipDesignReceived += m => parts += m.Id == id ? 1 : 0;
        feed.DesignReady += m =>
        {
            if (m.Id == id)
            {
                whole.Add(m);
            }
        };

        h.Server.EnterSpace(pid);
        h.Server.ShowDerivedStationForTest(pid, id);
        Assert.True(h.PumpUntil(() => whole.Count > 0, maxTicks: 40), $"the design arrived whole ({parts} parts so far)");

        Assert.True(parts >= 2, $"{parts} parts");
        var design = Assert.Single(whole);
        Assert.Equal(1, design.PartCount);
        Assert.Equal(design.Block.Length, design.X.Length);
        Assert.Equal(0, feed.PendingCount);
    }

    [Theory]
    [InlineData(5, 5, 5, 1)]     // inside a chunk: only its own
    [InlineData(16, 5, 5, 2)]    // on the −X face of chunk 1: also chunk 0
    [InlineData(15, 15, 15, 4)]  // a corner cell on three +faces
    [InlineData(-1, 5, 5, 2)]    // the +X face of chunk −1: also chunk 0
    public void AChangedCell_ReachesItsChunk_AndTheNeighboursAcrossTheFacesItTouches(int x, int y, int z, int expected)
    {
        var set = new HashSet<ChunkCoord>();
        StructureChunkMath.FaceChunks(new Vector3i(x, y, z), set);
        Assert.Equal(expected, set.Count);
        Assert.Contains(StructureChunkMath.ChunkOf(new Vector3i(x, y, z)), set);
    }

    [Fact]
    public void ChunkOf_FloorsNegativeCells()
    {
        Assert.Equal(new ChunkCoord(-1, -1, 0), StructureChunkMath.ChunkOf(new Vector3i(-1, -16, 15)));
        Assert.Equal(new ChunkCoord(1, -2, 0), StructureChunkMath.ChunkOf(new Vector3i(16, -17, 0)));
    }

    [Fact]
    public void ALampsReach_CoversTheChunksItCanLight()
    {
        var reach = new HashSet<ChunkCoord>();
        StructureChunkMath.ReachChunks(new Vector3i(8, 8, 8), 10, reach);
        Assert.Equal(27, reach.Count); // 8 ± 10 spans chunks −1 … 1 on each axis
        reach.Clear();
        StructureChunkMath.ReachChunks(new Vector3i(8, 8, 8), 3, reach);
        Assert.Single(reach);
    }

    /// <summary>Corner shading reads a cell along an edge or a corner, so a changed cell reaches all 26 chunks around it it
    /// touches — at a chunk corner that is eight chunks, not the four its faces touch.</summary>
    [Fact]
    public void AChangedCell_ReachesTheChunksAcrossItsEdgesAndCorners()
    {
        var set = new HashSet<ChunkCoord>();
        StructureChunkMath.NeighbourChunks(new Vector3i(0, 0, 0), set);
        Assert.Equal(8, set.Count);
        Assert.Contains(new ChunkCoord(-1, -1, -1), set); // only a corner touches it

        set.Clear();
        StructureChunkMath.NeighbourChunks(new Vector3i(8, 8, 8), set);
        Assert.Single(set);
        set.Clear();
        StructureChunkMath.NeighbourChunks(new Vector3i(15, 8, 0), set);
        Assert.Equal(4, set.Count); // an edge: +X, −Z and the chunk across both
    }

    /// <summary>A wall opened or closed inside a lamp's flood changes the light wherever that lamp reaches — up to twice the
    /// lamp's reach from the cell. Only lamps near enough to light the cell count; a lamp that went (the list holds the lamps
    /// after the change) still re-lights its own reach.</summary>
    [Fact]
    public void ALightChange_ReachesEveryChunkTheNearLampsLight()
    {
        var lamps = new List<(Vector3i Pos, int Rgb)> { (new Vector3i(40, 0, 0), 0xFFFFFF) };
        var set = new HashSet<ChunkCoord>();
        StructureChunkMath.LightChunks(new Vector3i(31, 0, 0), lampChanged: false, lamps, set);
        Assert.Contains(StructureChunkMath.ChunkOf(new Vector3i(50, 0, 0)), set); // 19 blocks from the cell, in the lamp's reach
        Assert.DoesNotContain(new ChunkCoord(0, 0, 0), set); // beyond the lamp's reach

        set.Clear();
        StructureChunkMath.LightChunks(new Vector3i(29, 0, 0), lampChanged: false, lamps, set);
        Assert.Empty(set); // the lamp is too far to light this cell

        set.Clear();
        StructureChunkMath.LightChunks(new Vector3i(29, 0, 0), lampChanged: true, null, set);
        var own = new HashSet<ChunkCoord>();
        StructureChunkMath.ReachChunks(new Vector3i(29, 0, 0), StructureChunkMath.LampReach, own);
        Assert.True(own.SetEquals(set), "a lamp mined there: its own reach"); // nothing else lights it
        Assert.True(StructureChunkMath.LampReach > BlocksBeyondTheStars.Shared.Definitions.BlockLight.DefaultRadius);
    }

    [Fact]
    public void TheLampList_FollowsAnEdit()
    {
        var lamps = new List<(Vector3i Pos, int Rgb)> { (new Vector3i(1, 2, 3), 0xFF0000), (new Vector3i(5, 5, 5), 0x00FF00) };
        StructureChunkMath.UpdateLight(lamps, new Vector3i(1, 2, 3), 0x0000FF); // recoloured
        StructureChunkMath.UpdateLight(lamps, new Vector3i(5, 5, 5), 0);        // mined
        StructureChunkMath.UpdateLight(lamps, new Vector3i(9, 9, 9), 0xFFFFFF); // placed
        Assert.Equal(new[] { (new Vector3i(1, 2, 3), 0x0000FF), (new Vector3i(9, 9, 9), 0xFFFFFF) },
            lamps.OrderBy(l => l.Pos.X).Select(l => (l.Pos, l.Rgb)).ToArray());
    }

    /// <summary>A roof block opened or closed changes the skylight of the chunks that read its column: the ones beside it
    /// within the skylight's margin, from the chunks whose scan reaches up to it (48 blocks above their top) to the ones that
    /// still look 16 blocks below their bottom — and no further.</summary>
    [Fact]
    public void ASkyChange_ReachesTheChunksWhoseSkylightReadsTheColumn()
    {
        var set = new HashSet<ChunkCoord>();
        StructureChunkMath.SkyChunks(new Vector3i(8, 100, 8), set);
        Assert.Contains(new ChunkCoord(0, 3, 0), set);  // y 48…63: its scan reaches 64 + 48 = 112 ≥ 100
        Assert.DoesNotContain(new ChunkCoord(0, 2, 0), set); // y 32…47: its scan stops at 96
        Assert.Contains(new ChunkCoord(0, 7, 0), set);  // y 112…127: it looks 16 below its bottom, down to 96
        Assert.DoesNotContain(new ChunkCoord(0, 8, 0), set);
        Assert.Contains(new ChunkCoord(-1, 5, 1), set); // 8 − 10 and 8 + 10 sideways reach the next chunks
        Assert.DoesNotContain(new ChunkCoord(2, 5, 0), set);
        Assert.Equal(3 * 5 * 3, set.Count);
    }

    [Fact]
    public void ABigHull_BuildsNearestChunksFirst_AndOnlyChunksThatHoldCells()
    {
        var cells = new[] { new Vector3i(0, 0, 0), new Vector3i(3, 3, 3), new Vector3i(40, 0, 0), new Vector3i(80, 0, 0) };
        var occupied = StructureChunkMath.Occupied(cells);
        Assert.Equal(3, occupied.Count);

        var order = StructureChunkMath.NearestFirst(occupied, 90f, 0f, 0f);
        Assert.Equal(new[] { new ChunkCoord(5, 0, 0), new ChunkCoord(2, 0, 0), new ChunkCoord(0, 0, 0) }, order);
    }

    /// <summary>A closed 16³ box of one block, one block thick: at 4 blocks per model cell the hollow inside is filled, so the
    /// skin is the box's six 4 × 4 faces — 96 quads — and nothing of the hall inside.</summary>
    [Fact]
    public void TheFarSkin_OfAClosedHull_IsItsOutsideOnly()
    {
        var (xs, ys, zs) = Box(16, 16, 16, open: false);
        var skin = HullSilhouette.Build(xs, ys, zs, xs.Select(_ => (ushort)7).ToArray(), null);

        Assert.Equal(4, skin.Step);
        Assert.Equal((4, 4, 4), (skin.Model.Width, skin.Model.Height, skin.Model.Length));
        Assert.All(skin.Model.Cells, c => Assert.NotEqual(0, c));
        Assert.Equal(96, skin.Mesh.QuadCount);
        Assert.Equal(new ushort[] { 7 }, skin.Model.Blocks);
    }

    /// <summary>A box open on one side shows its inside too (open space reaches the hall), and the model sits where the cells
    /// begin — the corner of the cells' box, not the design's origin.</summary>
    [Fact]
    public void TheFarSkin_OfAnOpenHull_ShowsItsHall_AndSitsAtTheCellsCorner()
    {
        var (xs, ys, zs) = Box(16, 16, 16, open: true);
        var shifted = xs.Select(x => x + 100).ToArray();
        var skin = HullSilhouette.Build(shifted, ys, zs, xs.Select(_ => (ushort)7).ToArray(), null);

        Assert.Equal(100, skin.MinX);
        Assert.Contains(skin.Model.Cells, c => c == 0); // the hall stays open
        Assert.True(skin.Mesh.QuadCount > 96, $"{skin.Mesh.QuadCount} quads");
    }

    [Fact]
    public void TheFarSkin_ColoursACellByTheBlockMostOfItIs_AndTakesALargerStepForAHugeStructure()
    {
        // One 4³ model cell: 24 blocks of id 2 first, then 40 of id 9 — the cell takes 9, though 2 came first.
        var cells = new List<(int X, int Y, int Z, ushort B)>();
        for (int i = 0; i < 64; i++)
        {
            cells.Add((i % 4, i / 4 % 4, i / 16, i < 24 ? (ushort)2 : (ushort)9));
        }

        var skin = HullSilhouette.Build(cells.Select(c => c.X).ToArray(), cells.Select(c => c.Y).ToArray(),
            cells.Select(c => c.Z).ToArray(), cells.Select(c => c.B).ToArray(), null);
        Assert.Equal(9, skin.Model.Blocks[skin.Model.Cells[0] - 1]);

        var wide = HullSilhouette.Build(new[] { 0, 999 }, new[] { 0, 0 }, new[] { 0, 0 }, new ushort[] { 1, 1 }, null);
        Assert.True(wide.Model.Width <= HullSilhouette.MaxCellsPerAxis, $"{wide.Model.Width} cells along X");

        var none = HullSilhouette.Build(Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<ushort>(), null);
        Assert.Equal(0, none.Mesh.VertexCount);
    }

    /// <summary>The cells of a one-block-thick box; <paramref name="open"/> leaves its +Z wall out.</summary>
    private static (int[] X, int[] Y, int[] Z) Box(int w, int h, int l, bool open)
    {
        var x = new List<int>();
        var y = new List<int>();
        var z = new List<int>();
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                for (int k = 0; k < l; k++)
                {
                    bool wall = i == 0 || j == 0 || k == 0 || i == w - 1 || j == h - 1 || k == l - 1;
                    if (wall && !(open && k == l - 1))
                    {
                        x.Add(i);
                        y.Add(j);
                        z.Add(k);
                    }
                }
            }
        }

        return (x.ToArray(), y.ToArray(), z.ToArray());
    }
}
