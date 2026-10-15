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
/// tree is too big), no cell protected (<see cref="GatherCellProtected"/> — the lodge box deliberately not, unless a
/// wanderer asks with <c>respectAllProtection</c>) nor in a ruined settlement's box, and no player edit anywhere in the
/// tree's box widened by one (a player-built log tower, a player's grown sapling — its base cell carries the planter — a
/// torch on a leaf, a lamp under the crown). The box may hold nothing a builder places (<see cref="NpcTreeBoxIsWild"/>):
/// a hut of logs and leaves is no tree. Structure stamps write no owner, so a lodge's stamped grove counts as
/// natural.</item>
/// <item><b>Whose leaves</b>: crowns of neighbouring trees touch. A leaf belongs to the tree whose trunk column is
/// nearest (every log column within ±<see cref="NpcFellForeignReach"/> that is not this tree's own wood counts as
/// another trunk), so felling one tree leaves the neighbour's crown standing. Fruit hanging under this tree's leaves
/// goes with it.</item>
/// <item><b>The fall</b> (<see cref="TickNpcFelling"/>): fruit first, then top down — outer leaves before the trunk on
/// each level — a few cells per tick over about <see cref="NpcFellSeconds"/> s, a <c>BlockChanged</c> per cell, the
/// support and fluid hooks like a player's break, one positional <c>tree_fall</c> sound for the players within
/// <see cref="NpcFellSoundRange"/> when it starts. The logs (at most <see cref="NpcFellLogCap"/>) go to the bank as they
/// come down — leaves and fruit give nothing. Last, a sapling is planted on the trunk's ground cell, on the same growth
/// clock as a player's (persisted by the flora queue). The last stretch — from the cell after which what stands would no
/// longer be a tree (<see cref="NpcTree.CollapseAt"/>) — comes down in one tick with the base log and the sapling, so a
/// crash mid-felling leaves a smaller tree to fell again or a sapling, never a bare stump.</item>
/// <item><b>Interrupted</b>: a felling whose NPC is gone (a wanderer moved on) comes down at once on the next tick,
/// without banking, and still leaves its sapling. A felling whose world was unloaded is dropped (the residents of a
/// reloaded world are new; what still stands is a tree to fell again). A cell that changed in the meantime (a player
/// mined or built there) is left alone, and so is a cell whose chunk (or a neighbour's) is not loaded: a felling never
/// loads a chunk.</item>
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

        /// <summary>The index in <see cref="Cells"/> from which on what still stands would no longer be a tree a woodcutter
        /// fells (fewer than four leaves, or a trunk shorter than two): from there the rest comes down in ONE tick, the base
        /// log and the sapling with it — so a crash or an unloaded world never leaves a bare stump behind (#2644).</summary>
        public int CollapseAt;
    }

    /// <summary>A tree coming down (#2644).</summary>
    private sealed class NpcFelling
    {
        public required ServerNpc Npc { get; init; }
        public required NpcTree Tree { get; init; }
        public required System.Action<string, int> Bank { get; init; }

        /// <summary>The world instance it started on: a world unloaded and loaded again is a new instance, and a felling of
        /// the old one is dropped (its NPC is gone, its cells may be in chunks nobody streams).</summary>
        public required ServerWorld World { get; init; }
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
    /// are scanned, so a search stays bounded in a dense wood. Canonical cells.
    /// <paramref name="respectAllProtection"/> is a wanderer's search (#2646): a tree with a cell
    /// <see cref="IsSettlementBlock"/> protects (a lodge's grove included) is skipped too. A lodge resident keeps the
    /// default: it fells its own grove.</remarks>
    private List<Vector3i> FindNpcTrees(Vector3f center, int radius, int max, bool respectAllProtection = false)
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

            if (ScanNpcTree(cell, respectAllProtection) is not { } tree)
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
    /// felling is refused too. <paramref name="respectAllProtection"/> is the wanderer's stricter rule (see
    /// <see cref="FindNpcTrees"/>).</remarks>
    private bool NpcFellTree(ServerNpc npc, Vector3i trunkBase, System.Action<string, int> bank, bool respectAllProtection = false)
    {
        var b = WorldConstants.CanonicalBlock(trunkBase, _world.Circumference);
        if (_npcFellings.TryGetValue(_world.LocationId, out var running)
            && running.Any(f => ReferenceEquals(f.Npc, npc) || f.Tree.CellSet.Contains(b)))
        {
            return false;
        }

        if (ScanNpcTree(b, respectAllProtection) is not { } tree)
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
            World = _world,
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
        if (_npcFellings.Count == 0)
        {
            return;
        }

        ForgetFellingsOfUnloadedWorlds();
        if (!_npcFellings.TryGetValue(_world.LocationId, out var running))
        {
            return;
        }

        for (int i = running.Count - 1; i >= 0; i--)
        {
            var f = running[i];
            if (!ReferenceEquals(f.World, _world))
            {
                // The world was unloaded and loaded again since: its residents were re-derived and nobody fells this tree
                // any more. Nothing is touched — the cells taken so far are in the store, and what still stands is a whole
                // tree a woodcutter can fell again (the last stretch, the base and the sapling go in one tick: CollapseAt).
                running.RemoveAt(i);
                continue;
            }

            bool orphaned = !_npcs.Contains(f.Npc); // a wanderer moved on, a resident was removed
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
                if (n > 0 && f.Next + n - 1 >= f.Tree.CollapseAt)
                {
                    n = left; // what would stand after this cell is no tree any more: the rest, base and sapling, in this tick
                }
            }

            for (int k = 0; k < n; k++)
            {
                FellNextCell(f, bank: !orphaned);
            }

            if (f.Next >= f.Tree.Cells.Count)
            {
                PlantFelledTreeSapling(f.Tree.Base); // the moment the base log went — in the same tick
                running.RemoveAt(i);
            }
        }

        FlushMirroredStationCells(); // one row write + design refresh per tick on a station world (no-op on a planet)
        if (running.Count == 0)
        {
            _npcFellings.Remove(_world.LocationId);
        }
    }

    /// <summary>Drops the fellings of every world that is no longer resident (the last player left it, #2644 review): they
    /// would otherwise sit in RAM until that world is entered again — and then be dropped there anyway.</summary>
    private void ForgetFellingsOfUnloadedWorlds()
    {
        List<string>? gone = null;
        foreach (var loc in _npcFellings.Keys)
        {
            if (!_worlds.IsLoaded(loc))
            {
                (gone ??= new List<string>()).Add(loc);
            }
        }

        if (gone is not null)
        {
            foreach (var loc in gone)
            {
                _npcFellings.Remove(loc);
            }
        }
    }

    /// <summary>Whether a cell and its six neighbours lie in resident chunks — the felling's write and the support and
    /// fluid hooks it runs read nothing else, so no felling ever loads a chunk.</summary>
    private bool NpcCellAndFacesLoaded(Vector3i cell)
    {
        if (!NpcCellLoaded(cell))
        {
            return false;
        }

        foreach (var d in NpcFellLogSteps)
        {
            if (!NpcCellLoaded(new Vector3i(cell.X + d.X, cell.Y + d.Y, cell.Z + d.Z)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes the felling's next cell down — when it and its neighbours are loaded (an unloaded cell is skipped:
    /// a felling never loads a chunk), it still holds what the tree had there and no player has edited it since — and
    /// banks a log's drops while the cap allows.</summary>
    private void FellNextCell(NpcFelling f, bool bank)
    {
        var (cell, block) = f.Tree.Cells[f.Next++];
        if (!NpcCellAndFacesLoaded(cell) || _world.GetBlockIfLoaded(cell).Value != block || NpcCellPlayerEdited(cell))
        {
            return; // out of every streaming area, or mined, burnt or built over meanwhile — not the tree's any more
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
    /// persisted), and written without an owner, so the tree it grows into is a natural tree again. Only on loaded chunks
    /// (the cell and the ground under it) — never loads one.</summary>
    private void PlantFelledTreeSapling(Vector3i cell)
    {
        if (_saplingId == 0 || !NpcCellLoaded(cell) || !NpcCellLoaded(new Vector3i(cell.X, cell.Y - 1, cell.Z))
            || !_world.GetBlockIfLoaded(cell).IsAir || !IsValidFloraHost(_saplingId, cell) || InShipInterior(cell)
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
    /// <paramref name="respectAllProtection"/>: a cell <see cref="IsSettlementBlock"/> protects refuses the tree too (a
    /// wanderer's rule — a lodge's grove).
    /// </summary>
    private NpcTree? ScanNpcTree(Vector3i b, bool respectAllProtection = false)
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

        // Nothing protected — no protected area, no settlement box of any kind (a ruin's log frame is no tree either).
        var lo = new Vector3i(int.MaxValue, int.MaxValue, int.MaxValue);
        var hi = new Vector3i(int.MinValue, int.MinValue, int.MinValue);
        foreach (var c in tree.CellSet)
        {
            if (GatherCellProtected(c) || InRuinedSettlementBox(c) || (respectAllProtection && IsSettlementBlock(c)))
            {
                return null;
            }

            var (dx, dy, dz) = Rel(c);
            lo = new Vector3i(System.Math.Min(lo.X, dx), System.Math.Min(lo.Y, dy), System.Math.Min(lo.Z, dz));
            hi = new Vector3i(System.Math.Max(hi.X, dx), System.Math.Max(hi.Y, dy), System.Math.Max(hi.Z, dz));
        }

        // No player edit anywhere in the tree's box widened by one (#2644 review): not a cell of the tree, and nothing a
        // player built onto it or beside it — a torch on a leaf, a lamp under the crown, a ladder against the trunk — so a
        // felling never pops a player's thing through the support hook. One aggregate query (split only at a seam).
        var wideMin = new Vector3i(b.X + lo.X - 1, b.Y + lo.Y - 1, b.Z + lo.Z - 1);
        var wideMax = new Vector3i(b.X + hi.X + 1, b.Y + hi.Y + 1, b.Z + hi.Z + 1);
        if (NpcAreaHasPlayerEdits(wideMin, wideMax) || !NpcTreeBoxIsWild(b, lo, hi))
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

        // Top down, what still stands stays a tree a woodcutter fells (its lower crown round the shortened trunk) — until
        // the cell after which fewer than four leaves or fewer than two trunk logs would be left. From that cell on the
        // rest goes in one tick and the sapling with it: a crash or an unload mid-felling leaves a tree to fell again or
        // a sapling, never a bare stump that no rule would ever clear (#2644 review). Nothing is persisted for it.
        int leavesLeft = crown.Count, trunkLeft = height;
        tree.CollapseAt = tree.Cells.Count - 1;
        for (int i = 0; i < tree.Cells.Count; i++)
        {
            var c = tree.Cells[i].Cell;
            if (crown.Contains(c))
            {
                leavesLeft--;
            }
            else if (wood.Contains(c) && Rel(c) is (0, var dy, 0) && dy < height)
            {
                trunkLeft--;
            }

            if (leavesLeft < 4 || trunkLeft < 2)
            {
                tree.CollapseAt = i;
                break;
            }
        }

        return tree;
    }

    /// <summary>
    /// Whether the tree's box — its cells' bounds widened by one sideways and upward, from the trunk's base level up —
    /// holds only what grows round a wild tree (#2644 review): air, the trunks, crowns and fruit of this content's trees
    /// (its neighbours' too), plants, fluids, loose snow or sand, and the generator's own ground (a slope, a boulder). A
    /// lodge's hut, a ruined house or a camp's shack made of logs and leaves is no tree: any other block — planks, glass,
    /// a door, a fence, a crate, a lamp, a slab — means somebody built here, and so does terrain or ore with a row in the
    /// edit store (a stamped floor or wall, a refilled cell: the generator's ground has none; one aggregate query, asked
    /// only when the box holds terrain above the ground at all). Reads loaded chunks only.
    /// </summary>
    private bool NpcTreeBoxIsWild(Vector3i b, Vector3i lo, Vector3i hi)
    {
        List<ushort>? ground = null;
        for (int dx = lo.X - 1; dx <= hi.X + 1; dx++)
            for (int dz = lo.Z - 1; dz <= hi.Z + 1; dz++)
                for (int dy = 0; dy <= hi.Y + 1; dy++)
                {
                    ushort id = _world.GetBlockIfLoaded(new Vector3i(b.X + dx, b.Y + dy, b.Z + dz)).Value;
                    if (id == 0 || NpcTreeLogIds.Contains(id) || NpcTreeLeafIds.Contains(id) || _fruitIds.Contains(id)
                        || IsFlora(id) || IsSapling(id) || IsFluid(id))
                    {
                        continue;
                    }

                    if (_world.Definition(new BlockId(id)) is not { } def)
                    {
                        return false;
                    }

                    if (def.Granular || def.Category == "flora")
                    {
                        continue; // snow on a crown, a sand drift; a plant of any kind
                    }

                    if (def.Category is not ("terrain" or "ore"))
                    {
                        return false; // something a builder places
                    }

                    ground ??= new List<ushort>();
                    if (!ground.Contains(id))
                    {
                        ground.Add(id);
                    }
                }

        if (ground is null)
        {
            return true;
        }

        var min = new Vector3i(b.X + lo.X - 1, b.Y, b.Z + lo.Z - 1);
        var max = new Vector3i(b.X + hi.X + 1, b.Y + hi.Y + 1, b.Z + hi.Z + 1);
        return !AnyCanonicalBox(min, max, (l, h)
            => _repo.ListBlockEditsMatching(_world.LocationId, l, h, ground, System.Array.Empty<int>(), 1).Count > 0);
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#2644): <see cref="FindNpcTrees"/> on the active world.</summary>
    public IReadOnlyList<Vector3i> FindNpcTreesForTest(Vector3f center, int radius, int max, bool respectAllProtection = false)
        => FindNpcTrees(center, radius, max, respectAllProtection);

    /// <summary>Test seam (#2644): one <see cref="TickNpcFelling"/> step of the active world, without the rest of the tick
    /// (no chunk streaming around the player).</summary>
    public void RunNpcFellingForTest(double dt) => TickNpcFelling(dt);

    /// <summary>Test seam (#2644): files the active world's running fellings under another location id — as if their
    /// world had been unloaded meanwhile.</summary>
    public void ParkNpcFellingsOnForTest(string locationId)
    {
        if (_npcFellings.Remove(_world.LocationId, out var running))
        {
            _npcFellings[locationId] = running;
        }
    }

    /// <summary>Test seam (#2644): the location ids that hold running fellings.</summary>
    public IReadOnlyList<string> NpcFellingLocationsForTest => _npcFellings.Keys.ToList();

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
