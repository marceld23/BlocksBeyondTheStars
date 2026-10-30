// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Counting trees, not cells (#3023, epic #3019): "scan 25 different conifers" must not be done on one crown of a hundred
/// needle blocks. A scanned tree block (a leaf or a log) is walked to its trunk's foot — the lowest log of the nearest trunk
/// column under the crown — and that cell is the tree's identity, remembered per player
/// (<see cref="Shared.State.PlayerState.ScannedTrees"/>). The tree's kind is named by its crown: <c>conifer</c> for pine
/// needles, <c>broadleaf</c> for the common leaves, <c>palm</c> for fronds, else the crown block's key.
/// <para>Only natural trees count: a crown without a trunk below it, or a trunk whose foot a player placed, raises nothing —
/// a stack of placed logs and needles is not a fir. The search is bounded (a 7×7 column square, 24 cells down, a trunk of at
/// most 40 logs) and runs at most twice a second per player (the hand scanner's cooldown).</para>
/// </summary>
public sealed partial class GameServer
{
    private const int TreeCrownRadius = 3;
    private const int TreeSearchDepth = 24;
    private const int TreeMaxTrunk = 40;

    /// <summary>Raises <c>scan:tree</c> once per tree and player, and counts it for the <c>scan_tree:&lt;kind&gt;</c> achievements.
    /// <paramref name="cell"/> holds a block of this world's trees (checked by the caller).</summary>
    private void RaiseScanTreeOnce(PlayerSession session, Vector3i cell, string blockKey)
    {
        if (!TryFindTreeFoot(cell, blockKey, out var foot, out string crownKey) || NpcCellPlayerEdited(foot))
        {
            return;
        }

        foot = Shared.World.WorldConstants.CanonicalBlock(foot, _world.Circumference); // the same tree after a lap round the world

        long packed = ((long)foot.X << 42) ^ ((long)(foot.Y & 0xFFFFF) << 21) ^ (long)(foot.Z & 0x1FFFFF);
        var trees = session.State.ScannedTrees;
        if (trees.Contains(packed))
        {
            return;
        }

        trees.Add(packed);
        if (trees.Count > Shared.State.PlayerState.ScannedTreesCap)
        {
            trees.RemoveRange(0, trees.Count - Shared.State.PlayerState.ScannedTreesCap);
        }

        string kind = TreeKindOfCrown(crownKey);
        Advance(session, AchievementCounters.ScanTree(kind));
        RaiseGameEvent(session, GameEventKeys.ScanTree, kind);
    }

    /// <summary>The tree kind a crown block names.</summary>
    internal static string TreeKindOfCrown(string crownKey) => crownKey switch
    {
        "pine_needles" => "conifer",
        "tree_leaves" => "broadleaf",
        "palm_frond" => "palm",
        "" => "tree",
        _ => crownKey,
    };

    /// <summary>Finds the trunk's foot for a scanned tree block and the tree's crown block. A log is walked down to its foot
    /// and up to its top, where the crown sits; a leaf looks for the nearest trunk column below it.</summary>
    private bool TryFindTreeFoot(Vector3i cell, string blockKey, out Vector3i foot, out string crownKey)
    {
        foot = default;
        crownKey = string.Empty;
        var def = _content.GetBlock(blockKey);
        if (def is null || TreeSpeciesForBlock(blockKey) is null)
        {
            return false;
        }

        if (IsTreeLeaf(def))
        {
            crownKey = blockKey;
            return TryFindTrunkBelow(cell, out foot);
        }

        foot = WalkTrunk(cell, -1);
        crownKey = CrownAbove(WalkTrunk(cell, +1));
        return true;
    }

    private static bool IsTreeLeaf(BlockDefinition def) => string.Equals(def.Category, "flora", System.StringComparison.Ordinal);

    /// <summary>Whether the cell holds a log of this world's trees (a tree block that is not a leaf).</summary>
    private bool IsTreeLog(Vector3i cell)
    {
        var id = _world.GetBlockIfLoaded(cell);
        if (id.Value == 0 || _content.BlockById(id) is not { } def)
        {
            return false;
        }

        return !IsTreeLeaf(def) && TreeSpeciesForBlock(def.Key) is not null;
    }

    /// <summary>Follows a trunk down (−1) or up (+1) as long as logs continue, and returns its last log.</summary>
    private Vector3i WalkTrunk(Vector3i cell, int step)
    {
        var at = cell;
        for (int i = 0; i < TreeMaxTrunk; i++)
        {
            var next = new Vector3i(at.X, at.Y + step, at.Z);
            if (!IsTreeLog(next))
            {
                break;
            }

            at = next;
        }

        return at;
    }

    /// <summary>The leaf block on or around the cell above a trunk's top, or empty.</summary>
    private string CrownAbove(Vector3i top)
    {
        var offsets = new[] { (0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 2, 0) };
        foreach (var (dx, dy, dz) in offsets)
        {
            var id = _world.GetBlockIfLoaded(new Vector3i(top.X + dx, top.Y + dy, top.Z + dz));
            if (id.Value != 0 && _content.BlockById(id) is { } def && IsTreeLeaf(def) && TreeSpeciesForBlock(def.Key) is not null)
            {
                return def.Key;
            }
        }

        return string.Empty;
    }

    /// <summary>The foot of the nearest trunk column under a crown cell (nearest column first, then from the leaf down).</summary>
    private bool TryFindTrunkBelow(Vector3i leaf, out Vector3i foot)
    {
        foot = default;
        for (int ring = 0; ring <= TreeCrownRadius; ring++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != ring)
                    {
                        continue; // only this ring's border — the inner rings were searched already
                    }

                    for (int dy = 0; dy >= -TreeSearchDepth; dy--)
                    {
                        var c = new Vector3i(leaf.X + dx, leaf.Y + dy, leaf.Z + dz);
                        if (IsTreeLog(c))
                        {
                            foot = WalkTrunk(c, -1);
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }
}
