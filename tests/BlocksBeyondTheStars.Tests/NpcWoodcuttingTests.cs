// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The woodcutter's work core (#2644, wilderness folk #2639): wilderness NPCs fell a natural tree from the top down over
/// a few ticks (a <c>tree_fall</c> cue for the players near it, a <c>BlockChanged</c> per cell), bank its logs (capped,
/// leaves give nothing) and plant a sapling on the trunk's ground cell that grows back into a tree on the normal sapling
/// clock. Player-built log structures, trees with a player-edited cell, giant and too-big trees and trees in a protected
/// area are never touched; a neighbour's crown stays standing; a felling whose NPC vanished comes down at once. The
/// cadence (one tree every 6–8 minutes per woodcutter) is the caller's (#2641/#2646) and tested there.
/// </summary>
public sealed class NpcWoodcuttingTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    private NpcLifeWorld Arena(string name)
    {
        var w = new NpcLifeWorld(_content, name);
        // The tree checks read loaded chunks only — load the air above the pad the way a player standing there would.
        for (int x = Cx - 32; x <= Cx + 32; x += 8)
            for (int z = Cz - 32; z <= Cz + 32; z += 8)
                for (int y = w.Feet - 8; y <= w.Feet + 24; y += 8)
                {
                    w.Server.World.GetBlock(new Vector3i(x, y, z));
                }

        return w;
    }

    /// <summary>A tree on its own grass tile of the pad: a trunk and the sapling tree's round crown (two full rings
    /// around the top two logs, a small ring, a cap). Written without an owner (as worldgen does), or with one.</summary>
    private static (Vector3i Base, List<Vector3i> Cells) Tree(NpcLifeWorld w, int x, int z, int trunk = 5, string owner = "",
        string log = "wood_log", string leaf = "tree_leaves", int crownR = 2)
    {
        w.Server.World.SetBlock(new Vector3i(x, w.PadY, z), w.Block("grass"));
        var cells = new List<Vector3i>();
        for (int dy = 0; dy < trunk; dy++)
        {
            var c = new Vector3i(x, w.Feet + dy, z);
            w.Server.World.SetBlock(c, w.Block(log), 0, 0, 0, owner);
            cells.Add(c);
        }

        int top = w.Feet + trunk - 1;
        for (int dy = -1; dy <= 2; dy++)
        {
            int r = dy <= 0 ? crownR : dy == 1 ? 1 : 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (r == crownR && r > 1 && System.Math.Abs(dx) == r && System.Math.Abs(dz) == r)
                    {
                        continue;
                    }

                    var c = new Vector3i(x + dx, top + dy, z + dz);
                    if (!w.Server.World.GetBlock(c).IsAir)
                    {
                        continue;
                    }

                    w.Server.World.SetBlock(c, w.Block(leaf), 0, 0, 0, owner);
                    cells.Add(c);
                }
        }

        return (new Vector3i(x, w.Feet, z), cells);
    }

    private static Vector3f Center(NpcLifeWorld w, int x = Cx, int z = Cz) => new(x + 0.5f, w.Feet, z + 0.5f);

    /// <summary>Ticks until the NPC's felling is done (at most <paramref name="seconds"/>).</summary>
    private static void TickUntilFelled(NpcLifeWorld w, int npc, double seconds = 15)
        => w.TickAt(0.45, seconds, until: () => !w.Server.NpcFellingActiveForTest(npc));

    [Fact]
    public void FindsNaturalTrees_AndSkipsPlayerBuiltLogStructures()
    {
        using var w = Arena("wood_find");
        var natural = Tree(w, Cx + 4, Cz);
        Tree(w, Cx - 6, Cz, owner: NpcLifeWorld.Owner); // a player's "tree" of logs and leaves
        for (int dy = 0; dy < 3; dy++)
        {
            w.Server.World.SetBlock(new Vector3i(Cx, w.Feet + dy, Cz + 7), w.Block("wood_log")); // a bare log post: no tree
        }

        Assert.Equal(new[] { natural.Base }, w.Server.FindNpcTreesForTest(Center(w), 16, 8));
    }

    [Fact]
    public void Felling_TakesTheTreeDownFromTheTop_BanksTheLogs_AndTheSaplingGrowsBack()
    {
        using var w = Arena("wood_fell");
        var (b, cells) = Tree(w, Cx + 4, Cz);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        var banked = new List<(string Item, int Count)>();
        int sentBefore = w.Transport.Sent.Count;

        Assert.True(w.Server.NpcFellTreeForTest(npc, b, banked));
        Assert.True(w.Server.NpcFellingActiveForTest(npc));
        Assert.False(w.Server.NpcFellTreeForTest(npc, b, banked), "one NPC fells one tree at a time");
        Assert.Contains(w.Transport.Sent.Skip(sentBefore),
            s => s.Conn == w.Player.ConnectionId && s.Msg is SoundFx { SoundId: "tree_fall" });

        TickUntilFelled(w, npc);
        Assert.False(w.Server.NpcFellingActiveForTest(npc), "the tree never finished coming down");

        foreach (var c in cells.Where(c => !c.Equals(b)))
        {
            Assert.True(w.Server.World.GetBlock(c).IsAir, $"{c} of the tree is still standing");
        }

        Assert.Equal(5, banked.Where(x => x.Item == "wood_log").Sum(x => x.Count)); // the trunk's five logs
        Assert.All(banked, x => Assert.Equal("wood_log", x.Item));                   // leaves give nothing

        // From the top: the first cell to fall is the highest one, the trunk base is the last.
        var removed = w.Transport.Sent.Skip(sentBefore).Select(s => s.Msg).OfType<BlockChanged>()
            .Where(m => m.Block == 0 && cells.Contains(new Vector3i(m.X, m.Y, m.Z))).ToList();
        Assert.Equal(cells.Count, removed.Count);
        Assert.Equal(cells.Max(c => c.Y), removed[0].Y);
        Assert.Equal(b, new Vector3i(removed[^1].X, removed[^1].Y, removed[^1].Z));

        // The sapling stands on the trunk's ground cell, on the player's sapling clock, and grows into a tree.
        Assert.Equal(w.Block("flora_sapling"), w.Server.World.GetBlock(b));
        Assert.True(w.Server.FloraRegrowTimerForTest(b.X, b.Y, b.Z) > 100, "the sapling has no growth clock");
        Assert.False(w.Server.IsBlockPlayerEditedForTest(b), "the NPC's sapling was recorded as a player's planting");
        w.TickAt(0.45, 400, dt: 1.0, until: () => w.Server.World.GetBlock(b) == w.Block("wood_log"));
        Assert.Equal(w.Block("wood_log"), w.Server.World.GetBlock(b));
        Assert.Contains(b, w.Server.FindNpcTreesForTest(Center(w), 12, 4)); // a natural tree again
    }

    [Fact]
    public void TheLogsBanked_AreCapped()
    {
        using var w = Arena("wood_cap");
        var (b, _) = Tree(w, Cx + 4, Cz, trunk: 10);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        var banked = new List<(string Item, int Count)>();

        Assert.True(w.Server.NpcFellTreeForTest(npc, b, banked));
        TickUntilFelled(w, npc);

        Assert.Equal(8, banked.Where(x => x.Item == "wood_log").Sum(x => x.Count));
        for (int dy = 0; dy < 10; dy++)
        {
            Assert.NotEqual(w.Block("wood_log"), w.Server.World.GetBlock(new Vector3i(b.X, b.Y + dy, b.Z)));
        }
    }

    [Fact]
    public void ATreeWithAPlayerEditedCell_IsNeverTouched()
    {
        using var w = Arena("wood_edited");
        var (b, cells) = Tree(w, Cx + 4, Cz);
        var leaf = cells.Last();
        w.Set(leaf.X, leaf.Y, leaf.Z, "tree_leaves"); // the player put one leaf back with their own hands
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));

        Assert.Empty(w.Server.FindNpcTreesForTest(Center(w), 16, 8));
        Assert.False(w.Server.NpcFellTreeForTest(npc, b, new List<(string Item, int Count)>()));
        w.TickAt(0.45, 3);
        Assert.All(cells, c => Assert.False(w.Server.World.GetBlock(c).IsAir));
    }

    [Fact]
    public void GiantTooTallTooWideAndProtectedTrees_AreSkipped()
    {
        using var w = Arena("wood_skip");
        w.FoundBase(); // the core at the pad's centre: its zone is protected
        Tree(w, Cx + 3, Cz + 3);                                                     // in the base
        Tree(w, Cx - 14, Cz, trunk: 15);                                             // taller than the work box
        Tree(w, Cx - 14, Cz + 12, crownR: 4);                                        // wider than the work box
        Tree(w, Cx + 14, Cz - 12, log: "giant_log", leaf: "giant_leaves");           // a giant's wood
        var fine = Tree(w, Cx + 14, Cz + 12);

        Assert.Equal(new[] { fine.Base }, w.Server.FindNpcTreesForTest(Center(w), 24, 8));
    }

    [Fact]
    public void FellingOneTree_LeavesTheNeighboursCrownStanding()
    {
        using var w = Arena("wood_neighbour");
        var a = Tree(w, Cx + 4, Cz);
        var b = Tree(w, Cx + 8, Cz); // the two crowns touch at x = Cx + 6
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));

        Assert.True(w.Server.NpcFellTreeForTest(npc, a.Base, new List<(string Item, int Count)>()));
        TickUntilFelled(w, npc);

        Assert.Equal(w.Block("flora_sapling"), w.Server.World.GetBlock(a.Base));
        foreach (var c in b.Cells.Where(c => c.X >= Cx + 7))
        {
            Assert.False(w.Server.World.GetBlock(c).IsAir, $"{c} of the neighbour went down with the tree");
        }

        Assert.Contains(b.Base, w.Server.FindNpcTreesForTest(Center(w), 16, 8)); // still a whole tree to fell
    }

    [Fact]
    public void ATreeAPlayerBuiltOntoOrBeside_IsNeverFelled()
    {
        using var w = Arena("wood_built_onto");
        // Plants pass the "wild box" rule — only the player edit beside the tree's own cells refuses these two.
        var capTree = Tree(w, Cx + 4, Cz);
        var onCap = new Vector3i(Cx + 4, capTree.Cells.Max(c => c.Y) + 1, Cz);
        w.Set(onCap.X, onCap.Y, onCap.Z, "flora_flower");      // a player's flower on the crown's cap (it would pop)
        var footTree = Tree(w, Cx - 6, Cz);
        w.Set(Cx - 5, w.Feet, Cz, "flora_flower");             // a player's flower at the trunk's foot, under the crown
        var fine = Tree(w, Cx + 4, Cz + 10);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));

        Assert.Equal(new[] { fine.Base }, w.Server.FindNpcTreesForTest(Center(w), 16, 8));
        Assert.False(w.Server.NpcFellTreeForTest(npc, capTree.Base, new List<(string Item, int Count)>()));
        Assert.False(w.Server.NpcFellTreeForTest(npc, footTree.Base, new List<(string Item, int Count)>()));
        Assert.Equal(0, w.Server.NpcFellingCountForTest);
    }

    [Fact]
    public void AHutOfLogsAndLeaves_OrARuinsFrame_IsNoTreeToFell()
    {
        using var w = Arena("wood_structures");
        var natural = Tree(w, Cx + 4, Cz);

        // Stamped like a structure (no owner): a log post under a leaf roof with a window pane beside it ...
        var glassHut = Tree(w, Cx - 6, Cz, trunk: 3);
        w.Server.World.SetBlock(new Vector3i(Cx - 5, w.Feet, Cz), w.Block("glass"));
        // ... and one with a stone wall block standing in the box (terrain the generator never put there).
        var stoneHut = Tree(w, Cx, Cz + 10, trunk: 4);
        w.Server.World.SetBlock(new Vector3i(Cx + 1, w.Feet + 1, Cz + 10), w.Block("stone"));
        // A whole tree standing in a ruined village: unprotected, but no woodcutter's.
        var inRuin = Tree(w, Cx - 14, Cz - 12);
        w.Server.RegisterTestRuinForTest("Oldtown", new Vector3i(Cx - 18, w.Feet - 2, Cz - 16), new Vector3i(Cx - 10, w.Feet + 12, Cz - 8));

        var found = w.Server.FindNpcTreesForTest(Center(w), 24, 8);
        Assert.Equal(new[] { natural.Base }, found);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        foreach (var b in new[] { glassHut.Base, stoneHut.Base, inRuin.Base })
        {
            Assert.False(w.Server.NpcFellTreeForTest(npc, b, new List<(string Item, int Count)>()), $"{b} was felled");
        }
    }

    [Fact]
    public void AtEveryTickBoundary_WhatStandsIsATreeToFell_OrASapling_NeverABareStump()
    {
        using var w = Arena("wood_no_stump");
        var (b, cells) = Tree(w, Cx + 4, Cz);
        var trunk = cells.Where(c => w.Server.World.GetBlock(c) == w.Block("wood_log")).ToList();
        var leaves = cells.Except(trunk).ToList();
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        Assert.True(w.Server.NpcFellTreeForTest(npc, b, new List<(string Item, int Count)>()));

        // A crash (or a world unload) can strike between any two ticks: what it would leave must always heal.
        int steps = 0;
        while (w.Server.NpcFellingActiveForTest(npc) && steps++ < 400)
        {
            w.Server.RunNpcFellingForTest(0.05); // about one cell per step
            if (w.Server.World.GetBlock(b) == w.Block("wood_log"))
            {
                int leafCount = leaves.Count(c => w.Server.World.GetBlock(c) == w.Block("tree_leaves"));
                int logCount = trunk.Count(c => w.Server.World.GetBlock(c) == w.Block("wood_log"));
                Assert.True(leafCount >= 4 && logCount >= 2, $"step {steps}: a stump of {logCount} logs with {leafCount} leaves");
            }
            else
            {
                Assert.Equal(w.Block("flora_sapling"), w.Server.World.GetBlock(b)); // the base log went: the sapling with it
                Assert.All(cells.Where(c => !c.Equals(b)), c => Assert.True(w.Server.World.GetBlock(c).IsAir));
            }
        }

        Assert.False(w.Server.NpcFellingActiveForTest(npc));
        Assert.Equal(w.Block("flora_sapling"), w.Server.World.GetBlock(b));
    }

    [Fact]
    public void AFellingOfAnUnloadedWorld_IsDropped_AndAFellingNeverLoadsAChunk()
    {
        using var w = Arena("wood_unloaded");
        var (b, cells) = Tree(w, Cx + 4, Cz);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        Assert.True(w.Server.NpcFellTreeForTest(npc, b, new List<(string Item, int Count)>()));

        // Its world went (the last player left): the felling is forgotten on the next step, the tree untouched.
        w.Server.ParkNpcFellingsOnForTest("a-world-nobody-is-on");
        w.Server.RunNpcFellingForTest(0.5);
        Assert.Empty(w.Server.NpcFellingLocationsForTest);
        Assert.All(cells, c => Assert.False(w.Server.World.GetBlock(c).IsAir));

        // A felling whose tree lies in chunks nobody streams any more: its NPC is gone, so it ends at once — without
        // loading a single chunk, leaving the unloaded cells as they are.
        Assert.True(w.Server.NpcFellTreeForTest(npc, b, new List<(string Item, int Count)>()));
        w.Server.RemoveNpcForTest(npc);
        w.Server.World.ForgetChunksIn(new Vector3i(b.X - 4, b.Y - 2, b.Z - 4), new Vector3i(b.X + 4, b.Y + 16, b.Z + 4));
        int loaded = w.Server.World.LoadedChunkCount;
        w.Server.RunNpcFellingForTest(0.5);
        Assert.Equal(0, w.Server.NpcFellingCountForTest);
        Assert.Equal(loaded, w.Server.World.LoadedChunkCount);
        Assert.All(cells, c => Assert.False(w.Server.World.GetBlock(c).IsAir)); // reloaded from the store: all still there
    }

    [Fact]
    public void AFellingWhoseNpcVanished_ComesDownAtOnce_AndStillLeavesItsSapling()
    {
        using var w = Arena("wood_orphan");
        var (b, cells) = Tree(w, Cx + 4, Cz);
        int npc = w.Server.AddNpcForTest(Center(w, Cx + 2));
        var banked = new List<(string Item, int Count)>();

        Assert.True(w.Server.NpcFellTreeForTest(npc, b, banked));
        w.Server.RemoveNpcForTest(npc); // as a world reload does
        w.TickAt(0.45, 0.25);

        Assert.Equal(0, w.Server.NpcFellingCountForTest);
        Assert.All(cells.Where(c => !c.Equals(b)), c => Assert.True(w.Server.World.GetBlock(c).IsAir));
        Assert.Equal(w.Block("flora_sapling"), w.Server.World.GetBlock(b));
        Assert.Empty(banked); // nobody left to carry the logs home
    }
}
