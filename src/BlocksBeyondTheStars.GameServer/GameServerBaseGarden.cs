// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The base gardener's farm around the base (#3477, Lyxette 2026-11-11: a drained field beside the base turned into a garden
/// — fruit trees on one side, pumpkins and crops on the other — "a farmer whose harvest pays into what the base provides").
/// The gardener (#1868) worked only the crops inside the base; it now also works the OWNER's garden around it:
/// <list type="bullet">
/// <item><b>What</b>: the crops, the pumpkin (<see cref="FloraCatalog.PumpkinKey"/>) and the fruit of grown trees
/// (#2977: a fruit sapling's 3–5 fruit, the Fifi plant's berries), picked the way every harvest is
/// (<see cref="BreakBlockCore"/> without a player: the plant's drops, the fruit's colour into its regrow on its leaf).</item>
/// <item><b>Where</b>: within <see cref="BaseFocus.LumberjackRadius"/> of the core horizontally (the woodcutter's reach) and
/// the index's height band, inside or outside the walls. Never in a village, never in a stranger's base zone or pad, never
/// a festival's present, and a cell a partner base's core is nearer to is that base's (<see cref="BaseOwnsCell"/>).</item>
/// <item><b>Whose</b>: only what the owner planted or grew. A crop or a pumpkin is the owner's when the owner is the cell's
/// last editor (planting writes the planter; regrowth and NPC work keep it). A fruit is the owner's when the trunk it hangs
/// from stands on a cell the owner edited — a grown sapling's cell keeps its planter, a wild tree's root has no editor
/// (<see cref="FruitTreeRoot"/>). Wild flora and another player's field are left alone.</item>
/// <item><b>Cheap</b>: read from the block-edit store (a wild plant is never in it), one bounded query per index rebuild —
/// the index's own cadence — plus at most <see cref="GardenOwnerLookups"/> point lookups of a cell's editor; a site is asked
/// once more right before it is picked.</item>
/// <item><b>Where the harvest goes</b>: a base crate that takes it, as before; with no crate (or one that refuses it) into the
/// core's yield store like the woodcutter's logs, up to its capacity. No crate and a full core: the plant stays standing.</item>
/// </list>
/// The pacing (one harvest per base every 90 s), the day shift, the player-near rule and the gardener's work inside the base
/// are unchanged.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Rows one owner-garden scan may read from the store (a field larger than this keeps the first rows).</summary>
    private const int GardenScanRowLimit = 512;

    /// <summary>Most point lookups of a cell's editor one owner-garden scan makes (twice the gardener's site cap).</summary>
    private const int GardenOwnerLookups = 2 * GardenerSiteCap;

    /// <summary>How far beside a fruit (horizontally) its tree's trunk may stand: a palm's fronds reach three cells out.</summary>
    private const int FruitRootReach = 3;

    /// <summary>How far below a fruit its tree's trunk may start: the tallest fruit tree hangs its fruit five cells up.</summary>
    private const int FruitRootDepth = 8;

    private readonly HashSet<ushort> _ixGardenBlocks = new(); // #3477: crops, the pumpkin, every tree fruit
    private HashSet<ushort>? _gardenTreeLogs;                 // #3477: the trunks the sapling kinds grow

    private void EnsureGardenIds()
    {
        if (_gardenTreeLogs != null)
        {
            return;
        }

        EnsureBaseIndexIds();
        _ixGardenBlocks.Clear();
        _ixGardenBlocks.UnionWith(_ixCrops);
        _ixGardenBlocks.UnionWith(_fruitIds);
        if (_content.GetBlock(FloraCatalog.PumpkinKey) is { } pumpkin && !pumpkin.NumericId.IsAir)
        {
            _ixGardenBlocks.Add(pumpkin.NumericId.Value);
        }

        _gardenTreeLogs = new HashSet<ushort>(_saplings.Values.Select(s => s.Log.Value).Where(id => id != 0));
    }

    /// <summary>Whether the gardener picks this block (a crop, a pumpkin, a tree fruit) when it stands ripe.</summary>
    private bool IsGardenHarvest(ushort id)
    {
        EnsureGardenIds();
        return id != 0 && _ixGardenBlocks.Contains(id);
    }

    /// <summary>Fills <c>BaseIndex.OwnerGarden</c> (#3477, see the class summary): the candidates nearest the core first, each
    /// accepted when the owner planted or grew it, up to the gardener's site cap.</summary>
    private void CollectOwnerGarden(ServerBase b, BaseIndex idx, List<ServerBase> partners)
    {
        idx.OwnerGarden.Clear();
        EnsureGardenIds();
        if (_ixGardenBlocks.Count == 0)
        {
            return;
        }

        var roots = new Dictionary<Vector3i, bool>();
        int lookups = 0;
        foreach (var (cell, block) in OwnerGardenCandidates(b, idx, partners))
        {
            if (idx.OwnerGarden.Count >= GardenerSiteCap || lookups >= GardenOwnerLookups)
            {
                break;
            }

            if (IsOwnersGardenPlant(b, cell, block, roots, ref lookups))
            {
                idx.OwnerGarden.Add(cell);
            }
        }
    }

    /// <summary>The garden plants standing in the store around the base that pass the cheap tests (reach, standing and
    /// loaded, not regrowing, not already one of the crops inside, no village's, no stranger's base, no festival's present,
    /// not a partner base's), nearest the core first. Canonical cells.</summary>
    private List<(Vector3i Cell, ushort Block)> OwnerGardenCandidates(ServerBase b, BaseIndex idx, List<ServerBase> partners)
    {
        int circ = _world.Circumference;
        int reach = BaseFocus.LumberjackRadius;
        var inside = new HashSet<Vector3i>(idx.Crops);
        var found = new List<(Vector3i Cell, ushort Block, long DistSq)>();
        foreach (var e in BaseIndexEdits(b, _ixGardenBlocks, Array.Empty<int>(), GardenScanRowLimit, reach))
        {
            var cell = e.WorldPosition;
            long dx = WorldConstants.WrapDeltaX(cell.X - b.Cell.X, circ), dz = WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, circ);
            long distSq = (dx * dx) + (dz * dz);
            if (distSq > (long)reach * reach || inside.Contains(cell) || _world.GetBlockIfLoaded(cell).Value != e.Block
                || _floraRegrow.ContainsKey(cell) || IsFestivalDeposit(cell) || IsSettlementProtected(cell, new BlockId(e.Block))
                || IsBaseProtected(cell, b.OwnerId, false) || !BaseOwnsCell(b, cell, partners))
            {
                continue;
            }

            found.Add((cell, e.Block, distSq));
        }

        found.Sort((p, q) => p.DistSq != q.DistSq ? p.DistSq.CompareTo(q.DistSq)
            : p.Cell.Y != q.Cell.Y ? p.Cell.Y.CompareTo(q.Cell.Y)
            : p.Cell.Z != q.Cell.Z ? p.Cell.Z.CompareTo(q.Cell.Z) : p.Cell.X.CompareTo(q.Cell.X));
        return found.Select(f => (f.Cell, f.Block)).ToList();
    }

    /// <summary>Whether the owner planted (a crop, a pumpkin) or grew (a tree's fruit) the plant at <paramref name="cell"/> —
    /// see the class summary. <paramref name="roots"/> remembers the trees already asked about; <paramref name="lookups"/>
    /// counts the store lookups made.</summary>
    private bool IsOwnersGardenPlant(ServerBase b, Vector3i cell, ushort block, Dictionary<Vector3i, bool>? roots, ref int lookups)
    {
        if (!_fruitIds.Contains(block))
        {
            lookups++;
            return CellLastEditedBy(cell, b.OwnerId);
        }

        if (FruitTreeRoot(cell) is not { } root)
        {
            return false; // hanging from nothing we know as a trunk
        }

        if (roots != null && roots.TryGetValue(root, out bool known))
        {
            return known;
        }

        lookups++;
        bool mine = CellLastEditedBy(root, b.OwnerId);
        if (roots != null)
        {
            roots[root] = mine;
        }

        return mine;
    }

    /// <summary>Whether <paramref name="playerId"/> is the last player who edited the cell (the block-edit store's editor).</summary>
    private bool CellLastEditedBy(Vector3i cell, string playerId)
        => _repo.GetBlockAttribution(_world.LocationId, WorldConstants.CanonicalBlock(cell, _world.Circumference)) is { } edit
           && string.Equals(edit.Owner, playerId, StringComparison.Ordinal);

    /// <summary>The root of the tree a fruit hangs from (#3477): the foot of the nearest trunk of a sapling kind's wood within
    /// <see cref="FruitRootReach"/> beside the fruit and <see cref="FruitRootDepth"/> below it — on a grown sapling the
    /// sapling's own cell. Null when no trunk stands there. Loaded chunks only.</summary>
    private Vector3i? FruitTreeRoot(Vector3i fruit)
    {
        var logs = _gardenTreeLogs!;
        Vector3i? best = null;
        int bestDistSq = int.MaxValue;
        for (int dx = -FruitRootReach; dx <= FruitRootReach; dx++)
            for (int dz = -FruitRootReach; dz <= FruitRootReach; dz++)
            {
                int distSq = (dx * dx) + (dz * dz);
                if (distSq >= bestDistSq)
                {
                    continue;
                }

                for (int down = 0; down <= FruitRootDepth; down++)
                {
                    var c = new Vector3i(fruit.X + dx, fruit.Y - down, fruit.Z + dz);
                    if (logs.Contains(_world.GetBlockIfLoaded(c).Value))
                    {
                        best = TrunkFoot(c, logs);
                        bestDistSq = distSq;
                        break;
                    }
                }
            }

        return best;
    }

    /// <summary>The lowest log of the trunk <paramref name="log"/> belongs to (at most <see cref="FruitRootDepth"/> down).</summary>
    private Vector3i TrunkFoot(Vector3i log, HashSet<ushort> logs)
    {
        var foot = log;
        for (int i = 0; i < FruitRootDepth; i++)
        {
            var below = new Vector3i(foot.X, foot.Y - 1, foot.Z);
            if (!logs.Contains(_world.GetBlockIfLoaded(below).Value))
            {
                break;
            }

            foot = below;
        }

        return foot;
    }

    /// <summary>Whether a garden site is one the base counts inside (a crop of <c>BaseIndex.Crops</c>, or the air over one of
    /// its hydro trays) — those are the base's own, the owner's garden around it is asked again before a harvest.</summary>
    private static bool IsInsideGardenSite(BaseIndex idx, Vector3i canonical)
        => idx.Crops.Contains(canonical) || idx.Trays.Contains(new Vector3i(canonical.X, canonical.Y - 1, canonical.Z));

    /// <summary>
    /// The gardener picks one ripe plant (#3477): through the shared harvest (<see cref="BreakBlockCore"/> without a player —
    /// the plant's drops, a fruit's or pumpkin's colour into its regrow), then stores it in the first base crate that takes
    /// it, else in the core's yield store up to its capacity (like the woodcutter's logs); what fits nowhere — a full wood
    /// box and a full core — drops at the plant, nothing is lost. False, and nothing picked, when there is no crate and the
    /// core has no room for the plant's drops. <paramref name="inCrate"/> is how many items went into a crate.
    /// </summary>
    private bool HarvestGardenCell(ServerBase b, BaseIndex idx, Vector3i cell, BlockDefinition def, out int inCrate)
    {
        inCrate = 0;
        var expected = def.Drops.Select(d => new ItemAmount(d.Item, d.Count)).ToList();
        var crate = BaseCrateFor(idx, expected);
        if (crate is null && BaseCoreRoom(b) < expected.Sum(d => d.Count))
        {
            return false;
        }

        var picked = new List<ItemAmount>();
        BreakBlockCore(null, string.Empty, cell, def, null, (item, count) => picked.Add(new ItemAmount(item, count)));
        if (crate is not null && NpcDepositToContainer(crate, picked))
        {
            inCrate = picked.Sum(p => p.Count);
            return true;
        }

        BaseCoreRoom(b); // brings the store up to now before it takes the harvest
        var rest = new List<ItemAmount>();
        foreach (var p in picked)
        {
            int put = StoreInBaseCore(b, p.Item, p.Count);
            if (put < p.Count)
            {
                rest.Add(new ItemAmount(p.Item, p.Count - put));
            }
        }

        if (rest.Count > 0)
        {
            SpillToGround(cell, rest);
        }

        return true;
    }

    /// <summary>Room left in the base core's yield store, brought up to now first (the time it ran pays its own share).</summary>
    private int BaseCoreRoom(ServerBase b) => Math.Max(0, BaseRules.YieldCapacity - AccrueBaseYield(b, out _).CountReady());

    /// <summary>Puts up to <paramref name="count"/> of an item into the core's yield store, never past its capacity; returns
    /// how many went in. Shared by the woodcutter's logs (#3038) and the gardener's harvest (#3477).</summary>
    private int StoreInBaseCore(ServerBase b, string item, int count)
    {
        var store = YieldStore(BaseYieldKey(b));
        int put = Math.Min(Math.Max(0, BaseRules.YieldCapacity - store.CountReady()), count);
        if (put > 0)
        {
            store.Items[item] = store.Items.TryGetValue(item, out int n) ? n + put : put;
        }

        return Math.Max(0, put);
    }

    /// <summary>Where a gardener stands to work a site: beside it (its own level, one down or one up), else beside the
    /// column under it down to the ground — a tree's fruit hangs a few cells above the grass (#3477).</summary>
    private Vector3f? GardenApproachSpot(Vector3i site)
    {
        var none = new HashSet<Vector3i>();
        for (int down = 0; down <= NpcGatherReachAboveGround; down++)
        {
            if (SpotBeside(new Vector3i(site.X, site.Y - down, site.Z), none) is { } spot)
            {
                return spot;
            }
        }

        return null;
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#3477): the owner's garden around a base as its index holds it right now.</summary>
    internal IReadOnlyList<Vector3i> OwnerGardenForTest(int baseId)
        => BaseIndexForTest(baseId)?.OwnerGarden.ToList() ?? new List<Vector3i>();

    /// <summary>Test seam (#3477): what lies ready in a base core's yield store (a copy; not brought up to now).</summary>
    public IReadOnlyDictionary<string, int> BaseCoreStoreForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b && _meta.BaseYields.TryGetValue(BaseYieldKey(b), out var store)
            ? new Dictionary<string, int>(store.Items, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Test seam (#3477): puts items into a base core's yield store (to fill it up to its capacity).</summary>
    public void FillBaseCoreStoreForTest(int baseId, string item, int count)
    {
        if (_bases.FirstOrDefault(x => x.Id == baseId) is { } b)
        {
            var store = AccrueBaseYield(b, out _);
            store.Items[item] = store.Items.TryGetValue(item, out int n) ? n + count : count;
        }
    }
}
