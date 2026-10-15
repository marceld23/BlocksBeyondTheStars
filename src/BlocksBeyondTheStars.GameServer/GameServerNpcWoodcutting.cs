// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The woodcutter's work core (#2644, wilderness folk #2639). Natural trees never regrow, but a planted sapling grows
/// into a tree (<see cref="TryGrowTree"/>) — so a woodcutter who fells a tree and plants a sapling keeps the forest
/// bounded and alive. Shared by the lodge residents (#2641) and the wanderers (#2646); the caller picks the tree, walks
/// there, swings for a few seconds and paces itself (one tree per call, no cadence here).
/// <list type="bullet">
/// <item><b>Which tree</b> (<see cref="ScanNpcTree"/>): a trunk of this content's ordinary wood standing on ground a
/// sapling can root in (so it can be replanted), with a crown of ordinary leaves (a log post, a fallen log or a dead
/// snag is no tree to fell), loaded all around, every cell inside the work box (±<see cref="NpcFellHalfWidth"/> ×
/// <see cref="NpcFellHeight"/> high from the trunk base, at most <see cref="NpcFellCellCap"/> cells — a giant or a jungle
/// tree is too big), no cell protected (<see cref="GatherCellProtected"/> — the lodge box deliberately not), no cell a
/// player ever edited (a player-built log tower, a player's grown sapling — its base cell carries the planter).
/// Structure stamps write no owner, so a lodge's stamped grove counts as natural.</item>
/// <item><b>Whose leaves</b>: crowns of neighbouring trees touch. A leaf belongs to the tree whose trunk column is
/// nearest (every log column within ±<see cref="NpcFellForeignReach"/> that is not this tree's own wood counts as
/// another trunk), so felling one tree leaves the neighbour's crown standing. Fruit hanging under this tree's leaves
/// goes with it.</item>
/// <item><b>The fall</b> (<see cref="TickNpcFelling"/>): fruit first, then top down — outer leaves before the trunk on
/// each level — a few cells per tick over about <see cref="NpcFellSeconds"/> s, a <c>BlockChanged</c> per cell, the
/// support and fluid hooks like a player's break, one positional <c>tree_fall</c> sound for the players within
/// <see cref="NpcFellSoundRange"/> when it starts. The logs (at most <see cref="NpcFellLogCap"/>) go to the bank as they
/// come down — leaves and fruit give nothing. Last, a sapling is planted on the trunk's ground cell, on the same growth
/// clock as a player's (persisted by the flora queue).</item>
/// <item><b>Interrupted</b>: a felling whose NPC is gone (a world reload re-derives the residents, a wanderer moved on)
/// comes down at once on the next tick, without banking, and still leaves its sapling. A cell that changed in the
/// meantime (a player mined or built there) is left alone.</item>
/// </list>
/// Per world: the running fellings are keyed by the world's location id (the tick runs once per occupied world, NPC
/// ids repeat from world to world). RAM only — a felling lasts seconds.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Half the width of the work box around the trunk base (7 × 7 columns).</summary>
    private const int NpcFellHalfWidth = 3;

    /// <summary>Height of the work box from the trunk base up (the base level included).</summary>
    private const int NpcFellHeight = 14;

    /// <summary>Most cells (wood, leaves and fruit) one felled tree may have.</summary>
    private const int NpcFellCellCap = 160;

    /// <summary>Most logs one felled tree banks.</summary>
    private const int NpcFellLogCap = 8;

    /// <summary>How far around the trunk other trunks are looked for (they keep their own leaves).</summary>
    private const int NpcFellForeignReach = 7;

    /// <summary>About how long a tree takes to come down.</summary>
    private const double NpcFellSeconds = 2.5;

    /// <summary>The slowest a tree comes down (a small one does not dawdle), and the most cells one tick removes.</summary>
    private const double NpcFellMinCellsPerSecond = 24.0;
    private const int NpcFellMaxCellsPerTick = 12;

    /// <summary>Players within this many blocks hear the tree come down (short-range and quiet, as every NPC sound).</summary>
    private const float NpcFellSoundRange = 48f;

    /// <summary>The client's cue id for a felled tree coming down (#2647).</summary>
    internal const string NpcTreeFallSoundId = "tree_fall";

    /// <summary>Largest search radius a woodcutter may ask for, and the column walk's reach above and below the centre.</summary>
    private const int NpcFellMaxRadius = 32;
    private const int NpcTreeScanUp = 6;
    private const int NpcTreeScanDown = 6;

    /// <summary>A tree a woodcutter may fell: its trunk base, its cells in the order they come down, its log count.</summary>
    private sealed class NpcTree
    {
        public Vector3i Base;
        public List<(Vector3i Cell, ushort Block)> Cells = new();
        public HashSet<Vector3i> CellSet = new();
        public int Height;
    }

    /// <summary>A tree coming down (#2644).</summary>
    private sealed class NpcFelling
    {
        public required ServerNpc Npc { get; init; }
        public required NpcTree Tree { get; init; }
        public required System.Action<string, int> Bank { get; init; }
        public int Next;
        public double Budget = 1.0; // the first cell comes down with the first tick
        public double CellsPerSecond;
        public int LogsBanked;
    }

    /// <summary>The running fellings per world location id.</summary>
    private readonly Dictionary<string, List<NpcFelling>> _npcFellings = new(System.StringComparer.Ordinal);

    private static readonly Vector3i[] NpcFellLogSteps =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>Trunk bases of natural trees around <paramref name="center"/> within <paramref name="radius"/> (at most
    /// <paramref name="max"/>, nearest first): this world's trunk ids on ground, no player-edited cell in the tree, not in a
    /// protected area, loaded chunks only.</summary>
    /// <remarks>Each column of the disc is walked down from a few blocks above the centre's feet to its first trunk base
    /// (a log on ground); the bases are then tried nearest first with <see cref="ScanNpcTree"/> — a second base of the
    /// same tree (a 2 × 2 trunk) and a tree already coming down are skipped, and at most a few more trees than asked for
    /// are scanned, so a search stays bounded in a dense wood. Canonical cells.</remarks>
    private List<Vector3i> FindNpcTrees(Vector3f center, int radius, int max)
    {
        var result = new List<Vector3i>();
        radius = System.Math.Clamp(radius, 0, NpcFellMaxRadius);
        max = System.Math.Min(max, 16);
        if (max <= 0)
        {
            return result;
        }

        int circ = _world.Circumference;
        int cx = (int)System.Math.Floor(center.X), cy = (int)System.Math.Floor(center.Y), cz = (int)System.Math.Floor(center.Z);
        var candidates = new List<(Vector3i Cell, int DistSq)>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radius * radius)
                {
                    continue;
                }

                for (int y = cy + NpcTreeScanUp; y >= cy - NpcTreeScanDown; y--)
                {
                    ushort id = _world.GetBlockIfLoaded(new Vector3i(cx + dx, y, cz + dz)).Value;
                    if (id == 0 || NpcTreeLeafIds.Contains(id) || IsFlora(id))
                    {
                        continue;
                    }

                    if (NpcTreeLogIds.Contains(id))
                    {
                        ushort below = _world.GetBlockIfLoaded(new Vector3i(cx + dx, y - 1, cz + dz)).Value;
                        if (below == 0 || NpcTreeLogIds.Contains(below))
                        {
                            continue; // still the trunk (or a log over a gap)
                        }

                        int dy = y - cy;
                        candidates.Add((WorldConstants.CanonicalBlock(new Vector3i(cx + dx, y, cz + dz), circ), dx * dx + dy * dy + dz * dz));
                    }

                    break; // the column's trunk base, or its ground
                }
            }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        var covered = new HashSet<Vector3i>();
        _npcFellings.TryGetValue(_world.LocationId, out var running);
        int budget = max * 3 + 6;
        foreach (var (cell, _) in candidates)
        {
            if (covered.Contains(cell) || (running is not null && running.Any(f => f.Tree.CellSet.Contains(cell))))
            {
                continue;
            }

            if (budget-- <= 0)
            {
                break;
            }

            if (ScanNpcTree(cell) is not { } tree)
            {
                continue;
            }

            covered.UnionWith(tree.CellSet);
            result.Add(tree.Base);
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Starts felling the tree at <paramref name="trunkBase"/>: its trunk and leaves come down over the next ticks
    /// (<see cref="TickNpcFelling"/>), the logs go to <paramref name="bank"/> and a sapling is planted on the trunk's ground
    /// cell. False when there is no natural tree there (any more) or this NPC is already felling one.</summary>
    /// <remarks><paramref name="bank"/> is called later, from <see cref="TickNpcFelling"/>, as each log comes down (at most
    /// <see cref="NpcFellLogCap"/> logs' drops) — not at once — and not at all for logs of a felling whose NPC is gone.
    /// Ask <see cref="NpcFellingActive"/> to know when the tree is down and the sapling stands. A tree another NPC is
    /// felling is refused too.</remarks>
    private bool NpcFellTree(ServerNpc npc, Vector3i trunkBase, System.Action<string, int> bank)
    {
        var b = WorldConstants.CanonicalBlock(trunkBase, _world.Circumference);
        if (_npcFellings.TryGetValue(_world.LocationId, out var running)
            && running.Any(f => ReferenceEquals(f.Npc, npc) || f.Tree.CellSet.Contains(b)))
        {
            return false;
        }

        if (ScanNpcTree(b) is not { } tree)
        {
            return false;
        }

        if (running is null)
        {
            running = new List<NpcFelling>();
            _npcFellings[_world.LocationId] = running;
        }

        running.Add(new NpcFelling
        {
            Npc = npc,
            Tree = tree,
            Bank = bank,
            CellsPerSecond = System.Math.Max(NpcFellMinCellsPerSecond, tree.Cells.Count / NpcFellSeconds),
        });

        // It starts to come down: one creak-and-crash for the players near enough to hear it.
        var at = new Vector3f(b.X + 0.5f, b.Y + tree.Height * 0.5f, b.Z + 0.5f);
        var fx = new SoundFx { SoundId = NpcTreeFallSoundId, X = at.X, Y = at.Y, Z = at.Z };
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, at) <= (double)NpcFellSoundRange * NpcFellSoundRange)
            {
                Send(s, fx);
            }
        }

        return true;
    }

    /// <summary>True while a felling this NPC started is still coming down (the caller keeps it standing and swinging).</summary>
    private bool NpcFellingActive(ServerNpc npc)
        => _npcFellings.TryGetValue(_world.LocationId, out var running) && running.Any(f => ReferenceEquals(f.Npc, npc));

    /// <summary>Per-tick step of every running felling (registered in the tick loop after <c>TickFlora</c>).</summary>
    private void TickNpcFelling(double dt)
    {
        if (_npcFellings.Count == 0 || !_npcFellings.TryGetValue(_world.LocationId, out var running))
        {
            return;
        }

        for (int i = running.Count - 1; i >= 0; i--)
        {
            var f = running[i];
            bool orphaned = !_npcs.Contains(f.Npc); // a reload re-derived the residents, a wanderer moved on
            int left = f.Tree.Cells.Count - f.Next;
            int n;
            if (orphaned)
            {
                n = left; // no half tree left standing for ever: the rest comes down now
            }
            else
            {
                f.Budget += dt * f.CellsPerSecond;
                n = System.Math.Min(System.Math.Min((int)f.Budget, NpcFellMaxCellsPerTick), left);
                f.Budget -= n;
            }

            for (int k = 0; k < n; k++)
            {
                FellNextCell(f, bank: !orphaned);
            }

            if (f.Next >= f.Tree.Cells.Count)
            {
                PlantFelledTreeSapling(f.Tree.Base);
                running.RemoveAt(i);
            }
        }

        FlushMirroredStationCells(); // one row write + design refresh per tick on a station world (no-op on a planet)
        if (running.Count == 0)
        {
            _npcFellings.Remove(_world.LocationId);
        }
    }

    /// <summary>Takes the felling's next cell down — when it still holds what the tree had there and no player has edited
    /// it since — and banks a log's drops while the cap allows.</summary>
    private void FellNextCell(NpcFelling f, bool bank)
    {
        var (cell, block) = f.Tree.Cells[f.Next++];
        if (_world.GetBlock(cell).Value != block || NpcCellPlayerEdited(cell))
        {
            return; // mined, burnt or built over meanwhile — not the tree's any more
        }

        _world.SetBlock(cell, BlockId.Air); // no owner: an NPC's work is no player edit
        _miningProgress.Remove(cell);
        MirrorStationCellDeferred(cell, BlockId.Air);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = BlockId.AirValue });
        if (HasFluidNeighbor(cell))
        {
            OnFluidRemoved(cell); // a crown over a pond: the water may fill the gap like after a player's break
        }

        OnSupportRemoved(cell); // snow on the crown settles; a regrowing fruit under a vanished leaf is dropped

        if (bank && f.LogsBanked < NpcFellLogCap && NpcTreeLogIds.Contains(block) && _world.Definition(new BlockId(block)) is { } def)
        {
            f.LogsBanked++;
            foreach (var drop in def.Drops)
            {
                f.Bank(drop.Item, drop.Count);
            }
        }
    }

    /// <summary>The woodcutter's replanting: a sapling on the felled trunk's ground cell when it is free, a host a sapling
    /// roots in and nobody's edit — on the very growth clock a player's sapling gets (<see cref="ScheduleSaplingGrowth"/>,
    /// persisted), and written without an owner, so the tree it grows into is a natural tree again.</summary>
    private void PlantFelledTreeSapling(Vector3i cell)
    {
        if (_saplingId == 0 || !_world.GetBlock(cell).IsAir || !IsValidFloraHost(_saplingId, cell) || InShipInterior(cell)
            || !IsFloraEnclosedForVoidWorld(cell) || NpcCellPlayerEdited(cell))
        {
            return;
        }

        var sapling = new BlockId(_saplingId);
        _world.SetBlock(cell, sapling);
        MirrorStationCellDeferred(cell, sapling);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = _saplingId });
        ScheduleSaplingGrowth(cell);
    }

    /// <summary>
    /// The tree whose trunk base is <paramref name="b"/> (canonical), or null when there is none a woodcutter may fell —
    /// see the class summary for the rules. Reads loaded chunks only. The cells come back in the order they fall: fruit,
    /// then top down with the outer cells of a level before the inner ones and leaves before wood.
    /// </summary>
    private NpcTree? ScanNpcTree(Vector3i b)
    {
        int circ = _world.Circumference;
        var logs = NpcTreeLogIds;
        var leaves = NpcTreeLeafIds;
        ushort Read(Vector3i p) => _world.GetBlockIfLoaded(p).Value;
        Vector3i Canon(Vector3i p) => WorldConstants.CanonicalBlock(p, circ);
        (int Dx, int Dy, int Dz) Rel(Vector3i p)
            => (WorldConstants.WrapDeltaX(p.X - b.X, circ), p.Y - b.Y, WorldConstants.WrapDeltaZ(p.Z - b.Z, circ));
        bool InBox(Vector3i p)
        {
            var (dx, dy, dz) = Rel(p);
            return System.Math.Abs(dx) <= NpcFellHalfWidth && System.Math.Abs(dz) <= NpcFellHalfWidth && dy >= 0 && dy < NpcFellHeight;
        }

        if (_saplingId == 0 || !logs.Contains(Read(b))
            || !_world.IsNeighbourhoodLoaded(new Vector3i(b.X, b.Y + NpcFellHeight / 2, b.Z), NpcFellForeignReach + 1))
        {
            return null;
        }

        // On ground a sapling roots in — the tree can be replanted (a log over a gap, water or another log is no base).
        var below = new Vector3i(b.X, b.Y - 1, b.Z);
        ushort ground = Read(below);
        if (ground == 0 || logs.Contains(ground) || leaves.Contains(ground) || !IsValidFloraHost(_saplingId, b))
        {
            return null;
        }

        // The wood: every log 6-connected to the base. A log of it outside the box means a tree too big to fell.
        var wood = new HashSet<Vector3i> { b };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(b);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in NpcFellLogSteps)
            {
                var n = Canon(new Vector3i(p.X + d.X, p.Y + d.Y, p.Z + d.Z));
                if (wood.Contains(n) || !logs.Contains(Read(n)))
                {
                    continue;
                }

                if (!InBox(n) || wood.Count >= NpcFellCellCap)
                {
                    return null; // the trunk goes on past the box (a giant, a jungle tree) — too big
                }

                wood.Add(n);
                queue.Enqueue(n);
            }
        }

        // The trunk column: at least two logs straight up (a lone log on the ground is no tree).
        int height = 0;
        while (height < NpcFellHeight && wood.Contains(Canon(new Vector3i(b.X, b.Y + height, b.Z))))
        {
            height++;
        }

        if (height < 2)
        {
            return null;
        }

        // Every trunk column around: this tree's own and the others' (every other log within reach).
        var ownColumns = new HashSet<(int, int)>();
        foreach (var w in wood)
        {
            var (dx, _, dz) = Rel(w);
            ownColumns.Add((dx, dz));
        }

        var otherColumns = new HashSet<(int, int)>();
        for (int dx = -NpcFellForeignReach; dx <= NpcFellForeignReach; dx++)
            for (int dz = -NpcFellForeignReach; dz <= NpcFellForeignReach; dz++)
            {
                if (ownColumns.Contains((dx, dz)))
                {
                    continue;
                }

                for (int dy = 0; dy < NpcFellHeight; dy++)
                {
                    if (logs.Contains(Read(new Vector3i(b.X + dx, b.Y + dy, b.Z + dz))))
                    {
                        otherColumns.Add((dx, dz));
                        break;
                    }
                }
            }

        static int Nearest(HashSet<(int, int)> columns, int x, int z)
        {
            int best = int.MaxValue;
            foreach (var (cx, cz) in columns)
            {
                int d = (cx - x) * (cx - x) + (cz - z) * (cz - z);
                if (d < best)
                {
                    best = d;
                }
            }

            return best;
        }

        // The crown: leaves 26-connected to this tree's wood that lie nearer its trunk than any other's.
        var crown = new HashSet<Vector3i>();
        foreach (var w in wood)
        {
            queue.Enqueue(w);
        }

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        if (ox == 0 && oy == 0 && oz == 0)
                        {
                            continue;
                        }

                        var n = Canon(new Vector3i(p.X + ox, p.Y + oy, p.Z + oz));
                        if (crown.Contains(n) || !leaves.Contains(Read(n)))
                        {
                            continue;
                        }

                        var (dx, _, dz) = Rel(n);
                        int own = Nearest(ownColumns, dx, dz), other = Nearest(otherColumns, dx, dz);
                        bool inBox = InBox(n);
                        if (own > other || (own == other && !inBox))
                        {
                            continue; // the neighbour's leaf (a leaf halfway between goes with the tree it fits)
                        }

                        if (!inBox || wood.Count + crown.Count >= NpcFellCellCap)
                        {
                            return null; // a crown wider or higher than the box — too big to fell cleanly
                        }

                        crown.Add(n);
                        queue.Enqueue(n);
                    }
        }

        if (crown.Count < 4)
        {
            return null; // a log post, a dead snag, a fallen log: no crown, no tree
        }

        // The fruit hanging under this crown goes with it (a fruit under a vanished leaf would pop into a loose drop).
        var fruit = new HashSet<Vector3i>();
        foreach (var leaf in crown)
        {
            var under = Canon(new Vector3i(leaf.X, leaf.Y - 1, leaf.Z));
            if (_fruitIds.Contains(Read(under)) && !crown.Contains(under))
            {
                fruit.Add(under);
            }
        }

        if (wood.Count + crown.Count + fruit.Count > NpcFellCellCap)
        {
            return null;
        }

        var tree = new NpcTree { Base = b, Height = height };
        tree.CellSet.UnionWith(wood);
        tree.CellSet.UnionWith(crown);
        tree.CellSet.UnionWith(fruit);

        // Nothing protected, nothing a player edited — one aggregate query over the tree's box first.
        var lo = new Vector3i(int.MaxValue, int.MaxValue, int.MaxValue);
        var hi = new Vector3i(int.MinValue, int.MinValue, int.MinValue);
        foreach (var c in tree.CellSet)
        {
            if (GatherCellProtected(c))
            {
                return null;
            }

            var (dx, dy, dz) = Rel(c);
            lo = new Vector3i(System.Math.Min(lo.X, dx), System.Math.Min(lo.Y, dy), System.Math.Min(lo.Z, dz));
            hi = new Vector3i(System.Math.Max(hi.X, dx), System.Math.Max(hi.Y, dy), System.Math.Max(hi.Z, dz));
        }

        if (NpcAreaHasPlayerEdits(new Vector3i(b.X + lo.X, b.Y + lo.Y, b.Z + lo.Z), new Vector3i(b.X + hi.X, b.Y + hi.Y, b.Z + hi.Z))
            && tree.CellSet.Any(NpcCellPlayerEdited))
        {
            return null;
        }

        foreach (var c in tree.CellSet)
        {
            tree.Cells.Add((c, Read(c)));
        }

        tree.Cells.Sort((x, y) =>
        {
            int fx = fruit.Contains(x.Cell) ? 0 : 1, fy = fruit.Contains(y.Cell) ? 0 : 1;
            if (fx != fy)
            {
                return fx.CompareTo(fy); // the fruit first
            }

            if (x.Cell.Y != y.Cell.Y)
            {
                return y.Cell.Y.CompareTo(x.Cell.Y); // top down
            }

            int wx = logs.Contains(x.Block) ? 1 : 0, wy = logs.Contains(y.Block) ? 1 : 0;
            if (wx != wy)
            {
                return wx.CompareTo(wy); // leaves before the wood on a level
            }

            var (ax, _, az) = Rel(x.Cell);
            var (bx, _, bz) = Rel(y.Cell);
            return (bx * bx + bz * bz).CompareTo(ax * ax + az * az); // outer before inner
        });

        return tree;
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#2644): <see cref="FindNpcTrees"/> on the active world.</summary>
    public IReadOnlyList<Vector3i> FindNpcTreesForTest(Vector3f center, int radius, int max) => FindNpcTrees(center, radius, max);

    /// <summary>Test seam (#2644): <see cref="NpcFellTree"/> by the NPC with this id; the banked drops are appended to
    /// <paramref name="banked"/> as the logs come down. False when there is no such NPC or no tree to fell.</summary>
    public bool NpcFellTreeForTest(int npcId, Vector3i trunkBase, List<(string Item, int Count)> banked)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && NpcFellTree(npc, trunkBase, (item, count) => banked.Add((item, count)));

    /// <summary>Test seam (#2644): <see cref="NpcFellingActive"/> for the NPC with this id.</summary>
    public bool NpcFellingActiveForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && NpcFellingActive(npc);

    /// <summary>Test seam (#2644): how many fellings run on the active world (an orphaned one included).</summary>
    public int NpcFellingCountForTest
        => _npcFellings.TryGetValue(_world.LocationId, out var running) ? running.Count : 0;
}
