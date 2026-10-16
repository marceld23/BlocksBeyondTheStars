// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The base index (#1865): what a founded base holds that its residents care about — beds (homes), chairs and
/// benches (the evening), trading posts and mission boards (posts to staff), crates (where yields go), workbenches
/// and forges (the craftsman), crops, hydro trays and saplings (the gardener) and sentry posts (the guard).
/// <list type="bullet">
/// <item><b>Read from the block-edit store, not the voxels.</b> Every one of those was placed by a player, so one
/// bounded, filtered query (<c>ListBlockEditsMatching</c>) answers what a voxel scan of a fortress-sized box — up to
/// 193 × 65 × 193 cells — would take a noticeable slice of a tick to find. The box follows the base's built extent
/// (<see cref="BaseWallReach"/>), capped at <see cref="BaseIndexHalfXZ"/>.</item>
/// <item><b>Only what is inside the base counts</b> (Marcel 2026-09-13: "core zone + room + yard"): the 17³ core
/// zone, a sealed base room, the walled yard (#1315/#1862) or a closed room — walls, a roof and a door, see
/// <see cref="InClosedRoom"/>. A bed in the open field brings nobody.</item>
/// <item><b>A leak never evicts a resident</b> (#2653, the bulkhead rule): once a bed or a post has counted as inside, it
/// keeps counting while its block stands at that cell within the base's reach (<c>BaseIndex.StickyInside</c>) — a hole in
/// a bedroom wall takes the room's air, not its sleeper.</item>
/// <item>The same inside rule decides the founding (#2677): a bed inside, or three machines inside — counted on demand
/// for a base nobody lives at yet (<see cref="BaseMachinesInside"/>), cached on the index.</item>
/// <item>Cached per base, marked dirty by any block set within the base's reach, rebuilt lazily by the base-life
/// scan; RAM only, never persisted — the store is the truth.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Largest horizontal half-extent the index looks at (a 193 × 193 footprint).</summary>
    private const int BaseIndexHalfXZ = 96;

    /// <summary>Vertical half-extent of the index box around the core (a tower or a basement still counts).</summary>
    private const int BaseIndexHalfY = 32;

    /// <summary>Rows one index rebuild may read from the store (a base with more matching blocks keeps the first ones).</summary>
    private const int BaseIndexRowLimit = 4096;

    /// <summary>Cells one closed-room flood may visit before the room counts as open (a 20 × 20 × 10 hall fits).</summary>
    private const int ClosedRoomCellBudget = 4096;

    /// <summary>Chebyshev reach of one closed-room flood from its start cell.</summary>
    private const int ClosedRoomReach = 24;

    /// <summary>Everything a base's residents use, found inside the base (see the class summary).</summary>
    internal sealed class BaseIndex
    {
        public int BaseId;
        public string WorldId = string.Empty;
        public readonly List<Vector3i> BedHeads = new();    // one entry per bed (the head cell; legacy one-cell beds too)
        public readonly List<Vector3i> Seats = new();       // chair / bench cells
        public readonly List<Vector3i> VendorPosts = new(); // station_vendor cells
        public readonly List<Vector3i> Boards = new();      // mission_board cells
        public readonly List<Vector3i> Containers = new();  // crate / wood_crate / station_container cells
        public readonly List<Vector3i> Workbenches = new(); // workbench cells
        public readonly List<Vector3i> Forges = new();      // forge cells
        public readonly List<Vector3i> Crops = new();       // cultivated crop cells standing right now (a standing crop is ripe)
        public readonly List<Vector3i> Trays = new();       // hydro tray cells (a crop grows on top)
        public readonly List<Vector3i> Saplings = new();    // planted sapling cells
        public readonly List<Vector3i> SentryPosts = new(); // sentry_post cells

        /// <summary>Profession post cells by job (2026-09), in index order.</summary>
        public readonly Dictionary<string, List<Vector3i>> ProfessionPosts = new(System.StringComparer.Ordinal);
        public readonly HashSet<string> MissionIds = new(); // the base board's coined missions (#1865)

        /// <summary>#2653 (bulkhead rule): cell → block of everything that once counted as inside — it keeps counting while
        /// that block stands there within the base's reach, so a hole in a bedroom wall takes the air, not the sleeper.
        /// RAM only; the residents' saved beds and posts seed it on the first build of a run (block
        /// <see cref="StickyAnyBlock"/>: whatever indexed block stands there).</summary>
        public Dictionary<Vector3i, ushort> StickyInside = new();
        public bool StickySeeded;

        /// <summary>Whether the base's area was loaded when the index was built (<see cref="BaseAreaLoaded"/>). The inside
        /// tests read only loaded chunks — a room in an unloaded chunk reads as open — so an index built without them is
        /// provisional: the scan keeps the residents' jobs, and the index is rebuilt as soon as the area is loaded.</summary>
        public bool BuiltLoaded;

        /// <summary>#2677: machines inside the base as the founding rule counts them (<see cref="BaseMachinesInside"/>), up
        /// to the count the first settler needs; -1 until asked after a rebuild. A base that is founded never asks.</summary>
        public int FoundingMachines = -1;
        public bool BoardStocked;
        public bool Dirty = true;
        public double ComputedAt = double.NegativeInfinity;
        public int Rebuilds; // test seam
    }

    private readonly Dictionary<int, BaseIndex> _baseIndex = new();

    private ushort _ixBed, _ixVendor, _ixBoard, _ixWorkbench, _ixForge, _ixTray, _ixSapling, _ixSentry;
    private readonly HashSet<ushort> _ixCrops = new();
    private readonly HashSet<ushort> _ixContainers = new();
    private readonly Dictionary<ushort, string> _ixProfessionPosts = new(); // post block id → profession job
    private ushort[]? _ixBlocks;
    private static readonly int[] SeatShapeIndices = { (int)BlockShape.Chair, (int)BlockShape.Bench };

    private void EnsureBaseIndexIds()
    {
        if (_ixBlocks != null)
        {
            return;
        }

        ushort Id(string key) => _content.GetBlock(key)?.NumericId.Value ?? 0;
        _ixBed = Id("bed");
        _ixVendor = Id("station_vendor");
        _ixBoard = Id("mission_board");
        _ixWorkbench = Id("workbench");
        _ixForge = Id("forge");
        _ixTray = Id("hydro_tray");
        _ixSapling = Id("flora_sapling");
        _ixSentry = Id(SentryBlockKey);
        foreach (var key in new[] { "flora_cropberry", "flora_cropgrain", "flora_cropshroom" })
        {
            ushort id = Id(key);
            if (id != 0)
            {
                _ixCrops.Add(id);
            }
        }

        foreach (var key in new[] { "crate", "wood_crate", "station_container" })
        {
            ushort id = Id(key);
            if (id != 0)
            {
                _ixContainers.Add(id);
            }
        }

        foreach (var profession in NpcProfessions.All)
        {
            ushort id = Id(profession.PostBlock);
            if (id != 0)
            {
                _ixProfessionPosts[id] = profession.Job;
            }
        }

        var all = new HashSet<ushort> { _ixBed, _ixVendor, _ixBoard, _ixWorkbench, _ixForge, _ixTray, _ixSapling, _ixSentry };
        all.UnionWith(_ixProfessionPosts.Keys);
        all.UnionWith(_ixCrops);
        all.UnionWith(_ixContainers);
        all.Remove(0);
        _ixBlocks = all.ToArray();
    }

    /// <summary>A seeded <c>StickyInside</c> entry: counts for whatever indexed block stands at the cell.</summary>
    private const ushort StickyAnyBlock = 0;

    /// <summary>The base's index, rebuilt first when a block changed within its reach since the last build — or when it was
    /// built while the base's area was not loaded and the area is loaded now (chunk loads change no block, so nothing
    /// else would mark it dirty).</summary>
    private BaseIndex RefreshBaseIndex(ServerBase b)
    {
        if (!_baseIndex.TryGetValue(b.Id, out var idx))
        {
            _baseIndex[b.Id] = idx = new BaseIndex { BaseId = b.Id };
        }

        _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var plan);
        bool loaded = BaseAreaLoaded(b, plan);
        if (!idx.Dirty && idx.WorldId == _world.LocationId && (idx.BuiltLoaded || !loaded))
        {
            return idx;
        }

        if (!idx.Dirty && idx.WorldId == _world.LocationId)
        {
            // Built over unloaded chunks, loaded now: the walled-yard fill may hold a verdict read over the same unloaded
            // ground (it recomputes on its own every few seconds) — judge the walls afresh with the index.
            MarkBaseWallsDirty(_world, b.Cell);
        }

        EnsureBaseIndexIds();
        idx.BuiltLoaded = loaded;
        idx.WorldId = _world.LocationId;
        idx.Dirty = false;
        idx.ComputedAt = _uptime;
        idx.Rebuilds++;
        idx.FoundingMachines = -1;
        idx.BedHeads.Clear();
        idx.Seats.Clear();
        idx.VendorPosts.Clear();
        idx.Boards.Clear();
        idx.Containers.Clear();
        idx.Workbenches.Clear();
        idx.Forges.Clear();
        idx.Crops.Clear();
        idx.Trays.Clear();
        idx.Saplings.Clear();
        idx.SentryPosts.Clear();
        idx.ProfessionPosts.Clear();

        // #2653/#2656: the residents' saved beds and posts counted as inside when they were saved — a reload must not evict a
        // sleeper whose bedroom sprang a leak while the server was down, nor take a job because the post's room is not
        // loaded yet (a dedicated server scans its bases before anybody walks there).
        if (!idx.StickySeeded)
        {
            idx.StickySeeded = true;
            foreach (var r in plan ?? new List<BaseResidentRecord>())
            {
                if (TryParseBedCell(r.Bed, out var bedCell))
                {
                    idx.StickyInside[bedCell] = StickyAnyBlock;
                }

                if (TryParseBedCell(r.Post, out var postCell))
                {
                    idx.StickyInside[postCell] = StickyAnyBlock;
                }
            }
        }

        var stickyBefore = idx.StickyInside;
        idx.StickyInside = new Dictionary<Vector3i, ushort>();
        var roomCache = new Dictionary<Vector3i, bool>();
        foreach (var e in BaseIndexEdits(b, _ixBlocks!, SeatShapeIndices, BaseIndexRowLimit))
        {
            ClassifyBaseIndexCell(b, idx, e.WorldPosition, e.Block, e.Shape, roomCache, stickyBefore);
        }

        // Deterministic order: residents take beds and seats in this order, the gardener walks its sites in it.
        System.Comparison<Vector3i> order = (p, q) => p.Y != q.Y ? p.Y.CompareTo(q.Y) : p.Z != q.Z ? p.Z.CompareTo(q.Z) : p.X.CompareTo(q.X);
        foreach (var list in new[] { idx.BedHeads, idx.Seats, idx.VendorPosts, idx.Boards, idx.Containers, idx.Workbenches, idx.Forges, idx.Crops, idx.Trays, idx.Saplings, idx.SentryPosts })
        {
            list.Sort(order);
        }

        foreach (var list in idx.ProfessionPosts.Values)
        {
            list.Sort(order);
        }

        return idx;
    }

    /// <summary>The block edits of <paramref name="blocks"/> (or of the <paramref name="shapes"/> forms) in a base's index box —
    /// its reach capped at ±<see cref="BaseIndexHalfXZ"/>, ±<see cref="BaseIndexHalfY"/> around the core — asked per canonical
    /// piece of the box (the store holds canonical columns), at most <paramref name="limit"/> rows in all.</summary>
    private List<BlocksBeyondTheStars.Persistence.BlockEdit> BaseIndexEdits(ServerBase b, IReadOnlyCollection<ushort> blocks,
        IReadOnlyCollection<int> shapes, int limit)
    {
        var result = new List<BlocksBeyondTheStars.Persistence.BlockEdit>();
        int circ = _world.Circumference;
        int reach = System.Math.Min(BaseWallReach(b), BaseIndexHalfXZ);
        int latPeriod = WorldConstants.LatitudePeriodFor(circ);
        foreach (var (xLo, xHi) in CanonicalRanges(b.Cell.X - reach, b.Cell.X + reach, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(b.Cell.Z - reach, b.Cell.Z + reach, -latPeriod / 2, latPeriod))
            {
                if (result.Count >= limit)
                {
                    return result;
                }

                result.AddRange(_repo.ListBlockEditsMatching(b.Planet,
                    new Vector3i(xLo, b.Cell.Y - BaseIndexHalfY, zLo), new Vector3i(xHi, b.Cell.Y + BaseIndexHalfY, zHi),
                    blocks, shapes, limit - result.Count));
            }
        }

        return result;
    }

    /// <summary>Rows one founding count may read from the store (#2677): it stops at the third machine inside anyway.</summary>
    private const int FoundingMachineRowLimit = 256;

    /// <summary>Every block of category <c>machine</c> but the base core (#2677: what the founding rule counts).</summary>
    private ushort[]? _foundingMachineIds;

    /// <summary>
    /// #2677: the machines (block category <c>machine</c>, the base core excluded) inside base <paramref name="b"/>, counted
    /// up to <see cref="BaseSettlerMachineCount"/> — enough to know whether the first settler comes. The 17³ core zone is
    /// read from the voxels as it always was (a machine there counts wherever it came from); beyond it a machine counts in
    /// any cell inside the base by the rule beds use (<see cref="BaseCellInside"/>: a sealed base room, the walled yard, a
    /// closed room), read from the block-edit store. Cached on the index until its next rebuild.
    /// </summary>
    private int BaseMachinesInside(ServerBase b, BaseIndex idx)
    {
        if (idx.FoundingMachines >= 0)
        {
            return idx.FoundingMachines;
        }

        int count = CountBaseMachines(b);
        if (count < BaseSettlerMachineCount)
        {
            _foundingMachineIds ??= _content.Blocks.Values.Where(d => d.Category == "machine" && d.Key != "base_core")
                .Select(d => d.NumericId.Value).Where(id => id != 0).ToArray();
            int circ = _world.Circumference;
            var roomCache = new Dictionary<Vector3i, bool>();
            foreach (var e in BaseIndexEdits(b, _foundingMachineIds, System.Array.Empty<int>(), FoundingMachineRowLimit))
            {
                var cell = e.WorldPosition;
                bool inCoreZone = System.Math.Abs(WorldConstants.WrapDeltaX(cell.X - b.Cell.X, circ)) <= BaseProtectionRadius
                    && System.Math.Abs(cell.Y - b.Cell.Y) <= BaseProtectionRadius
                    && System.Math.Abs(WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, circ)) <= BaseProtectionRadius;
                if (!inCoreZone && _world.GetBlockIfLoaded(cell).Value == e.Block && MachineCellInside(b, cell, roomCache)
                    && ++count >= BaseSettlerMachineCount)
                {
                    break; // the core zone was counted from the voxels above
                }
            }
        }

        idx.FoundingMachines = System.Math.Min(count, BaseSettlerMachineCount);
        return idx.FoundingMachines;
    }

    /// <summary>Whether a machine at <paramref name="cell"/> stands inside the base (#2677). A bed or a post is judged by the
    /// air above it; a machine is often stacked or set under a shelf, so the first free cell above it or beside it on its
    /// level is the room it stands in. A machine walled in on every side belongs to no room.</summary>
    private bool MachineCellInside(ServerBase b, Vector3i cell, Dictionary<Vector3i, bool> roomCache)
    {
        var above = new Vector3i(cell.X, cell.Y + 1, cell.Z);
        if (!IsCollidingBlock(_world.GetBlockIfLoaded(above), fluidsPass: false, foliagePasses: false))
        {
            return BaseCellInside(b, cell, above, roomCache);
        }

        foreach (var (dx, dz) in WallFillDirs)
        {
            var side = new Vector3i(cell.X + dx, cell.Y, cell.Z + dz);
            if (!IsCollidingBlock(_world.GetBlockIfLoaded(side), fluidsPass: false, foliagePasses: false))
            {
                return BaseCellInside(b, cell, side, roomCache);
            }
        }

        return false;
    }

    private void ClassifyBaseIndexCell(ServerBase b, BaseIndex idx, Vector3i cell, ushort block, int shape, Dictionary<Vector3i, bool> roomCache,
        Dictionary<Vector3i, ushort> stickyBefore)
    {
        int form = ShapeCode.ShapeOf(shape);
        bool crop = _ixCrops.Contains(block);
        bool sapling = block == _ixSapling;

        // A flora cell is itself the air an NPC would stand beside; everything else is judged by the cell above it.
        // #2653: what counted as inside before keeps counting while the same block stands there (the bulkhead rule).
        var probe = crop || sapling ? cell : new Vector3i(cell.X, cell.Y + 1, cell.Z);
        bool sticky = stickyBefore.TryGetValue(cell, out var was) && (was == block || was == StickyAnyBlock);
        if (!sticky && !BaseCellInside(b, cell, probe, roomCache))
        {
            return;
        }

        idx.StickyInside[cell] = block;

        if (block == _ixBed)
        {
            if (form != (int)BlockShape.BedFoot)
            {
                idx.BedHeads.Add(cell); // the head of a two-cell bed, or a legacy one-cell bed — never the foot (one bed, one resident)
            }
        }
        else if (block == _ixVendor)
        {
            idx.VendorPosts.Add(cell);
        }
        else if (block == _ixBoard)
        {
            idx.Boards.Add(cell);
        }
        else if (_ixContainers.Contains(block))
        {
            idx.Containers.Add(cell);
        }
        else if (block == _ixWorkbench)
        {
            idx.Workbenches.Add(cell);
        }
        else if (block == _ixForge)
        {
            idx.Forges.Add(cell);
        }
        else if (crop)
        {
            idx.Crops.Add(cell);
        }
        else if (block == _ixTray)
        {
            idx.Trays.Add(cell);
        }
        else if (sapling)
        {
            idx.Saplings.Add(cell);
        }
        else if (block == _ixSentry)
        {
            idx.SentryPosts.Add(cell);
        }
        else if (_ixProfessionPosts.TryGetValue(block, out var professionJob))
        {
            if (!idx.ProfessionPosts.TryGetValue(professionJob, out var posts))
            {
                idx.ProfessionPosts[professionJob] = posts = new List<Vector3i>();
            }

            posts.Add(cell);
        }

        if (FurnitureShapes.IsSeat(form) && block != _ixBed)
        {
            idx.Seats.Add(cell);
        }
    }

    /// <summary>Whether a placed thing at <paramref name="cell"/> belongs to base <paramref name="b"/>: inside the core
    /// zone, or its <paramref name="probe"/> air cell lies in a sealed base room, the walled yard or a closed room
    /// within the base's reach.</summary>
    private bool BaseCellInside(ServerBase b, Vector3i cell, Vector3i probe, Dictionary<Vector3i, bool>? roomCache = null)
    {
        int circ = _world.Circumference;
        if (System.Math.Abs(WorldConstants.WrapDeltaX(cell.X - b.Cell.X, circ)) <= BaseProtectionRadius
            && System.Math.Abs(cell.Y - b.Cell.Y) <= BaseProtectionRadius
            && System.Math.Abs(WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, circ)) <= BaseProtectionRadius)
        {
            return true;
        }

        // The walled-yard test reads "not reachable" — a solid cell would always pass, so it asks about a free cell
        // BESIDE the thing, on its own floor level: one level up, the yard's fill steps onto a two-high wall's top
        // and back down into the yard, so above a bed a walled yard read as open.
        foreach (var (dx, dz) in WallFillDirs)
        {
            var side = new Vector3i(cell.X + dx, cell.Y, cell.Z + dz);
            if (!IsCollidingBlock(_world.GetBlockIfLoaded(side), fluidsPass: false, foliagePasses: false))
            {
                if (InWalledBaseArea(side))
                {
                    return true;
                }

                break;
            }
        }

        // The room tests fill from the air the thing stands in.
        if (IsCollidingBlock(_world.GetBlockIfLoaded(probe), fluidsPass: false, foliagePasses: false))
        {
            return false;
        }

        return InSealedBaseRoom(probe) || InWalledBaseArea(probe) || InClosedRoom(probe, roomCache);
    }

    /// <summary>
    /// A closed room around <paramref name="start"/> (#1865): a 6-connected flood through cells a body could occupy
    /// (not colliding, not a fluid) that stops at every door — open or shut, a doorway is the room's door — and
    /// ends inside <see cref="ClosedRoomCellBudget"/> cells without stepping more than <see cref="ClosedRoomReach"/>
    /// from the start. A hut with walls, a roof and a wooden door is a room; a roofless yard or a hut with a hole in
    /// the wall is not. Unlike the air system (where mechanical doors leak) this is about "a home", not about air.
    /// The verdict is cached for every cell of the flood.
    /// </summary>
    private bool InClosedRoom(Vector3i start, Dictionary<Vector3i, bool>? cache = null)
    {
        if (cache != null && cache.TryGetValue(start, out bool known))
        {
            return known;
        }

        var doors = DoorCellsWhere(_ => true);
        int circ = _world.Circumference;
        var seen = new HashSet<Vector3i> { start };
        var frontier = new Queue<Vector3i>();
        frontier.Enqueue(start);
        bool open = false;
        while (frontier.Count > 0 && !open)
        {
            var c = frontier.Dequeue();
            for (int i = 0; i < 6; i++)
            {
                var n = i switch
                {
                    0 => new Vector3i(c.X + 1, c.Y, c.Z),
                    1 => new Vector3i(c.X - 1, c.Y, c.Z),
                    2 => new Vector3i(c.X, c.Y + 1, c.Z),
                    3 => new Vector3i(c.X, c.Y - 1, c.Z),
                    4 => new Vector3i(c.X, c.Y, c.Z + 1),
                    _ => new Vector3i(c.X, c.Y, c.Z - 1),
                };

                if (seen.Contains(n))
                {
                    continue;
                }

                if (doors.Contains(WorldConstants.CanonicalBlock(n, circ)))
                {
                    continue; // the room's door: a boundary whichever way it stands
                }

                var id = _world.GetBlockIfLoaded(n);
                if (IsCollidingBlock(id, fluidsPass: false, foliagePasses: false))
                {
                    continue; // a wall, the floor, the roof
                }

                if (System.Math.Abs(n.X - start.X) > ClosedRoomReach || System.Math.Abs(n.Y - start.Y) > ClosedRoomReach
                    || System.Math.Abs(n.Z - start.Z) > ClosedRoomReach || seen.Count >= ClosedRoomCellBudget)
                {
                    open = true; // escaped into the open (or too big to be a room)
                    break;
                }

                seen.Add(n);
                frontier.Enqueue(n);
            }
        }

        if (cache != null)
        {
            foreach (var c in seen)
            {
                cache[c] = !open;
            }
        }

        return !open;
    }

    /// <summary>A block changed on a resident world: every base whose reach holds the cell rebuilds its index on the
    /// next look (cheap — a flag; the rebuild is one store query).</summary>
    private void MarkBaseIndexDirty(ServerWorld world, Vector3i cell)
    {
        if (_baseIndex.Count == 0)
        {
            return;
        }

        int circ = world.Circumference;
        foreach (var b in _bases)
        {
            if (b.Planet != world.LocationId || !_baseIndex.TryGetValue(b.Id, out var idx) || idx.Dirty)
            {
                continue;
            }

            int reach = System.Math.Min(_baseWallReach.TryGetValue(b.Id, out int r) ? r : WalledReachCap, BaseIndexHalfXZ) + 1;
            if (System.Math.Abs(WorldConstants.WrapDeltaX(cell.X - b.Cell.X, circ)) <= reach
                && System.Math.Abs(WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, circ)) <= reach
                && System.Math.Abs(cell.Y - b.Cell.Y) <= BaseIndexHalfY + 1)
            {
                idx.Dirty = true;
            }
        }
    }

    /// <summary>Whether a base's area is loaded enough to judge what stands inside it: the chunk of its core and the chunk of
    /// every bed and post its residents hold (<paramref name="plan"/>). Right after a start nobody may have walked there yet
    /// — a dedicated server scans its bases anyway — and the inside tests read unloaded chunks as open air.</summary>
    private bool BaseAreaLoaded(ServerBase b, List<BaseResidentRecord>? plan)
    {
        int circ = _world.Circumference;
        bool Loaded(Vector3i cell) => _world.IsChunkLoaded(WorldConstants.WorldToChunk(WorldConstants.CanonicalBlock(cell, circ)));
        if (!Loaded(b.Cell))
        {
            return false;
        }

        foreach (var r in plan ?? new List<BaseResidentRecord>())
        {
            if ((TryParseBedCell(r.Bed, out var bed) && !Loaded(bed)) || (TryParseBedCell(r.Post, out var post) && !Loaded(post)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A dissolved base forgets its index.</summary>
    private void ForgetBaseIndex(int baseId)
    {
        _baseIndex.Remove(baseId);
        _basePatrol.Remove(baseId);
        _baseHarvest.Remove(baseId);
        _guardWarnedUntil.Remove(baseId);
    }

    /// <summary>Test seam (#1865): the index of a base on the active world, rebuilt if dirty.</summary>
    internal BaseIndex? BaseIndexForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId && b.Planet == _world.LocationId) is { } b ? RefreshBaseIndex(b) : null;

    /// <summary>Test seam (#1865): beds, seats and posts a base counts right now.</summary>
    public (int Beds, int Seats, int Vendors, int Boards, int Containers) BaseFurnitureForTest(int baseId)
        => BaseIndexForTest(baseId) is { } idx
            ? (idx.BedHeads.Count, idx.Seats.Count, idx.VendorPosts.Count, idx.Boards.Count, idx.Containers.Count)
            : (0, 0, 0, 0, 0);
}
