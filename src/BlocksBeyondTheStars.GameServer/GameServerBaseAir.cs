// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Sealed-room life support for founded bases (issue #794). Beyond the unconditional radius-8 cube
/// (issue #782), a base also breathes air into every room that is <b>sealed</b> — enclosed by airtight
/// full-cube blocks (<see cref="Shared.Definitions.BlockDefinition.Airtight"/>) — and <b>connected</b> to
/// the base. The model is per-POCKET, because the typical Grundstein stands in the open:
/// <list type="bullet">
/// <item>A <b>pocket</b> is a maximal 6-connected region of non-airtight cells (air, flora, loose dirt,
/// shaped cells — leaks flow right through those), bounded by airtight full-cube blocks and by ENERGY
/// door cells. Energy doors are walk-through air curtains (#793): they separate pockets without ever
/// depressurising on open/close; mechanical doors (wood/hinge/slide) are no boundary at all — they leak.</item>
/// <item>A pocket is <b>sealed</b> when its bounded fill terminates. Why it did not is recorded (#2542), because
/// "not sealed" alone sent a player looking for a hole in a closed hall that was merely too big: it grew past
/// <see cref="WorldConstants.BaseSealedRoomMaxCells"/> (<see cref="BaseAirLeak.TooLarge"/>), an open cell lay
/// outside every reach box (<see cref="BaseAirLeak.TooFar"/>), or it reached the open sky
/// (<see cref="BaseAirLeak.Open"/>). For a leak the fill also keeps WHERE (#2614): the first block on the way up that
/// does not seal (a sand roof under the open sky), or the last cell inside the reach box when that way is open air —
/// "leaky" alone sent a player hunting a cave hall for a hole that was its sand ceiling.</item>
/// <item>A sealed pocket is <b>supplied</b> when it touches the radius-8 cube of a <b>partner</b> core or connects
/// through an energy door to another supplied pocket — so airlocked room chains extend the base outward, door by
/// door.</item>
/// <item><b>Partners</b> (#2541): a base pools its air with the bases of its owner's allies on the same body. Any
/// partner's cube supplies, the leak box is the union of the partners' reach boxes and every partner's energy
/// doors seed the fill — so a colony two allies build together breathes as one.</item>
/// <item><b>Reach</b> (#2540): a base's box is the wall ring's (<see cref="BaseWallReach"/>, #1862) — at least
/// <see cref="SealedRoomMaxReach"/>, and as far out as its players built or dug, up to the cap. A cave the player
/// walled in or dug out is in reach without anything to learn.</item>
/// </list>
/// The supplied volume is derived state, recomputed straight from the world's blocks and kept per partner GROUP.
/// It is not recomputed blindly (#2543: a sealed 60k-cell hall costs tens of milliseconds, and a leaky pocket used to
/// run to the full budget every 1.5 s): a block set inside the group's boxes (<see cref="MarkBaseAirDirty"/> — every
/// mutation path goes through <see cref="ServerWorld.SetBlock"/>), an energy door built or removed, or a partner's
/// reach growing marks it stale; <see cref="BaseAirBackstopInterval"/> covers chunk loads and alliance changes, and
/// <see cref="SealedRoomRecomputeInterval"/> keeps rapid building from refilling every tick. A fill that reaches the
/// open sky stops at once (<see cref="AirOpenToSky"/>), so a core standing in the open costs a handful of cells.
/// Gated by the existing OxygenEnabled rule only (no new game rule). When previously supplied cells drop out on a
/// world without breathable air, everyone in reach is told why (the mined-wall case must never be silent — and since
/// #2542 neither may the case of one room dropping out while another still breathes).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The minimum Chebyshev half-extent of a base's reach box — for the air fill and the wall ring alike.
    /// Since #2540 the air box grows with what the base's players built, exactly like the wall ring's
    /// (<see cref="BaseWallReach"/>, #1862); this is its floor.</summary>
    private const int SealedRoomMaxReach = 48;

    /// <summary>Minimum seconds between recomputes of one group's supplied volume, however often it is marked
    /// dirty — rapid building or flowing water inside a base refills at most this often.</summary>
    private const double SealedRoomRecomputeInterval = 1.5;

    /// <summary>Seconds a group's volume is trusted without a recorded change (#2543). Covers what no block set
    /// reports: chunks loading or unloading in the box and alliances forming or breaking.</summary>
    private const double BaseAirBackstopInterval = 8.0;

    /// <summary>Every this many dequeued cells the fill also probes the open sky above the cell (#2543). A seed is
    /// always probed, and so is every power-of-two-th cell (the pocket under a core standing in the open is open after a
    /// step or two); the periodic probe catches a pocket that only reaches the surface after a while.</summary>
    private const int AirSkyProbeEvery = 512;

    /// <summary>Minimum seconds between two on-the-spot diagnoses for one player leaving base air (#2542).</summary>
    private const double BaseAirDiagnoseInterval = 3.0;

    /// <summary>Seconds within which the same cause is not told to the same player again when they walk out of base
    /// air (#2542) — someone who leaves through the same door into a huge cave hears it once, not every time.</summary>
    private const double BaseAirRetellInterval = 300.0;

    /// <summary>Why a pocket holds no base air (#2542).</summary>
    internal enum BaseAirLeak : byte
    {
        None,          // sealed
        TooLarge,      // the fill exceeded the cell budget
        TooFar,        // an open cell lay outside every partner's reach box
        Open,          // the fill reached the open sky
        Disconnected,  // sealed, but no partner cube and no energy door to a supplied pocket
    }

    /// <summary>What <see cref="DiagnoseBaseAir"/> found at a cell (#2542).</summary>
    internal enum BaseAirStatus : byte
    {
        NoBase,
        Zone,
        Supplied,
        TooLarge,
        TooFar,
        Leak,
        Disconnected,
    }

    /// <summary>A diagnosis of the base air at one cell: its status, the cells of the pocket (or the group's supplied
    /// cells), the group's supplied rooms and its widest reach. For a leak (#2614) also where the air gets out —
    /// <paramref name="LeakCell"/> (canonical) holding <paramref name="LeakBlock"/>, air when the way up is open — and
    /// whether the spot is clearly a room (<paramref name="LooksLikeRoom"/>: the pocket touches an energy door, or
    /// something stands above the cell), which is when walking in says so.</summary>
    internal readonly record struct BaseAirDiagnosis(BaseAirStatus Status, int Cells, int Rooms, int Reach,
        Vector3i? LeakCell = null, BlockId LeakBlock = default, bool LooksLikeRoom = false);

    /// <summary>A partner group's cached supplied-air volume (empty = nothing sealed / leaked / over budget).</summary>
    private sealed class BaseAirVolume
    {
        public string Body = string.Empty;            // world the fill was computed on
        public HashSet<Vector3i> Cells = new();       // canonical cells that hold base air
        public int Rooms;                             // supplied (sealed + connected) pockets in the last fill
        public double ComputedAt = double.NegativeInfinity;
        public bool Computed;                         // at least one fill done — the first one never warns
        public bool Dirty = true;                     // a block set inside the boxes since the last fill
        public int DoorsVersion = -1;                 // _baseAirDoorsVersion at the last fill
        public int ReachSignature = -1;               // sum of the partners' reaches at the last fill
        public List<ServerBase> Partners = new();     // the bases whose cubes supply, ordered by id
        public int[] Reaches = System.Array.Empty<int>(); // their reaches at the last fill, same order
    }

    /// <summary>Supplied volumes per partner group (key: the partners' ids). Cleared when the active world changes.</summary>
    private readonly Dictionary<string, BaseAirVolume> _baseAirGroups = new(System.StringComparer.Ordinal);

    /// <summary>Base id → the group volume it was last resolved to.</summary>
    private readonly Dictionary<int, BaseAirVolume> _baseAirOf = new();

    /// <summary>The world the cached groups belong to.</summary>
    private string _baseAirBody = string.Empty;

    /// <summary>Bumped whenever the door registry changes (#2543): an energy door built, removed or rebuilt
    /// changes the pockets without any block being set.</summary>
    private int _baseAirDoorsVersion;

    /// <summary>(Player id, cause) → when that cause was last told about base air (#2542). Per cause since #2614: with the
    /// leak told too, a player popping out of a cave base through two doors — a too-large hall behind one, a leaking
    /// passage behind the other — would otherwise hear both again on every alternation.</summary>
    private readonly Dictionary<(string Player, BaseAirLeak Cause), double> _baseAirTold = new();

    /// <summary>Players who breathed base air on their last oxygen tick.</summary>
    private readonly HashSet<string> _baseAirHad = new(System.StringComparer.Ordinal);

    /// <summary>Player id → uptime of their last on-the-spot diagnosis.</summary>
    private readonly Dictionary<string, double> _baseAirDiagnosedAt = new(System.StringComparer.Ordinal);

    /// <summary>Test seam (#2543): how many group fills ran since start.</summary>
    public int BaseAirComputesForTest { get; private set; }

    /// <summary>Test seam (#2543): how many cells the last group fill visited, over all its pockets.</summary>
    public int BaseAirCellsVisitedForTest { get; private set; }

    private static readonly int[] AirDx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] AirDy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] AirDz = { 0, 0, 0, 0, 1, -1 };

    /// <summary>True if the cell lies inside some founded base's SEALED room volume on the current world.
    /// Ownership is ignored just like <see cref="InAnyBaseZone"/> — visitors breathe too. The radius-8
    /// cube is checked separately (and unconditionally); this only adds the sealed rooms beyond it.</summary>
    private bool InSealedBaseRoom(Vector3i cell)
    {
        string body = _world.LocationId;
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet != body)
            {
                continue;
            }

            // Outside every partner's reach box the fill can never contain the cell — skip without refreshing.
            bool inBoxes = _baseAirOf.TryGetValue(b.Id, out var known) && known.Body == body && known.Partners.Count > 0
                ? InAirBoxes(known.Partners, canonical)
                : WithinBaseWallReach(b, canonical);
            if (!inBoxes)
            {
                continue;
            }

            if (RefreshBaseAir(b, broadcastOnChange: true).Cells.Contains(canonical))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The base's group supplied-air volume, recomputed when stale (see the class doc). With
    /// <paramref name="broadcastOnChange"/> a changed cell/room count re-sends the base list on this body, so the
    /// core's air readout on every client stays current (#1267) — the caller building that very list passes false.</summary>
    private BaseAirVolume RefreshBaseAir(ServerBase b, bool broadcastOnChange)
    {
        string body = _world.LocationId;
        if (body != _baseAirBody)
        {
            // A new active world: every group starts over, so returning to a body never compares against the cells
            // of the last visit (chunks still loading would read as a lost seal and warn on arrival).
            _baseAirGroups.Clear();
            _baseAirOf.Clear();
            _baseAirBody = body;
        }

        if (_baseAirOf.TryGetValue(b.Id, out var known)
            && (!NeedsAirRefresh(known) || _uptime - known.ComputedAt < SealedRoomRecomputeInterval))
        {
            return known;
        }

        var partners = AirPartners(b);
        var key = new System.Text.StringBuilder();
        foreach (var p in partners)
        {
            key.Append(p.Id).Append(',');
        }

        string groupKey = key.ToString();
        if (!_baseAirGroups.TryGetValue(groupKey, out var group))
        {
            _baseAirGroups[groupKey] = group = new BaseAirVolume { Body = body };
        }

        _baseAirOf[b.Id] = group;
        if (group.Computed && (!NeedsAirRefresh(group) || _uptime - group.ComputedAt < SealedRoomRecomputeInterval))
        {
            return group; // a partner with the same group computed it a moment ago
        }

        int cellsBefore = group.Cells.Count, roomsBefore = group.Rooms;
        bool first = !group.Computed;
        var reaches = new int[partners.Count];
        int signature = 0;
        for (int i = 0; i < partners.Count; i++)
        {
            reaches[i] = BaseWallReach(partners[i]);
            signature += reaches[i];
        }

        var cells = ComputeSuppliedCells(partners, reaches, first ? null : group.Cells, out int rooms, out var lost);
        group.Partners = partners;
        group.Reaches = reaches;
        group.ReachSignature = signature;
        group.DoorsVersion = _baseAirDoorsVersion;
        group.Dirty = false;
        group.ComputedAt = _uptime;
        group.Computed = true;
        group.Cells = cells;
        group.Rooms = rooms;

        // Air just went away somewhere (a wall mined, a door pulled, the hall dug past the budget …): tell everyone
        // in reach why — but only where it matters, i.e. when the world's own air is NOT breathable and oxygen is on
        // (issue: Marcel 2026-08-08; the cause since #2542).
        if (!first && lost != BaseAirLeak.None && Rules.OxygenEnabled && !AtmosphereBreathable)
        {
            WarnBaseAirChange(group, lost, total: cells.Count == 0);
        }

        if (broadcastOnChange && (cells.Count != cellsBefore || rooms != roomsBefore))
        {
            BroadcastBasesOn(body);
        }

        return group;
    }

    /// <summary>Whether a group's volume must be refilled: a block set inside its boxes, a door change, a partner's
    /// reach grown since the last fill, or the backstop elapsed (#2543).</summary>
    private bool NeedsAirRefresh(BaseAirVolume vol)
    {
        if (vol.Dirty || vol.DoorsVersion != _baseAirDoorsVersion || _uptime - vol.ComputedAt >= BaseAirBackstopInterval)
        {
            return true;
        }

        int signature = 0;
        foreach (var p in vol.Partners)
        {
            signature += BaseWallReach(p);
        }

        return signature != vol.ReachSignature;
    }

    /// <summary>The bases that pool their air with <paramref name="b"/> (#2541): itself and every base on the same body
    /// whose owner is the same player or allied with them (pairwise alliance or shared crew), ordered by id.</summary>
    private List<ServerBase> AirPartners(ServerBase b)
    {
        var list = new List<ServerBase>();
        foreach (var o in _bases)
        {
            if (o.Planet == b.Planet && (o.Id == b.Id || o.OwnerId == b.OwnerId || AreAllied(b.OwnerId, o.OwnerId)))
            {
                list.Add(o);
            }
        }

        list.Sort((x, y) => x.Id.CompareTo(y.Id));
        return list;
    }

    /// <summary>A block was set on a resident world (#2543): every group whose boxes hold the cell (plus one, the
    /// fill reads the ring just outside its open cells) is refilled on its next query.</summary>
    private void MarkBaseAirDirty(ServerWorld world, Vector3i cell)
    {
        if (_baseAirGroups.Count == 0 || world.LocationId != _baseAirBody)
        {
            return;
        }

        var canonical = WorldConstants.CanonicalBlock(cell, world.Circumference);
        foreach (var g in _baseAirGroups.Values)
        {
            if (!g.Dirty && InAirBoxes(g.Partners, g.Reaches, canonical, margin: 1))
            {
                g.Dirty = true;
            }
        }
    }

    /// <summary>The door registry changed (#2543) — energy doors bound and link pockets without a block being set.</summary>
    private void BaseAirDoorsChanged() => _baseAirDoorsVersion++;

    /// <summary>Test/inspection: a base's supplied air (cells, sealed rooms) on the active world, refreshed.</summary>
    public (int Cells, int Rooms) BaseAirForTest(int baseId)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId && x.Planet == _world.LocationId);
        if (b is null)
        {
            return (0, 0);
        }

        var vol = RefreshBaseAir(b, broadcastOnChange: false);
        return (vol.Cells.Count, vol.Rooms);
    }

    /// <summary>Shortest absolute longitude distance across the wrap seam (Y/Z use plain Math.Abs).</summary>
    private int WrapAbs(int dx) => System.Math.Abs(WorldConstants.WrapDeltaX(dx, _world.Circumference));

    /// <summary>Whether a canonical cell lies inside any partner's reach box at the partners' CURRENT reaches.</summary>
    private bool InAirBoxes(List<ServerBase> partners, Vector3i c)
    {
        foreach (var p in partners)
        {
            if (WithinBaseWallReach(p, c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a canonical cell lies inside any partner's reach box (reaches as fixed for one fill).</summary>
    private bool InAirBoxes(List<ServerBase> partners, int[] reaches, Vector3i c, int margin = 0)
    {
        for (int i = 0; i < partners.Count; i++)
        {
            int r = reaches[i] + margin;
            var core = partners[i].Cell;
            if (WrapAbs(c.X - core.X) <= r && System.Math.Abs(c.Y - core.Y) <= r && WrapAbsZ(c.Z - core.Z) <= r)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a canonical cell lies in any partner's radius-8 cube.</summary>
    private bool InAirCubes(List<ServerBase> partners, Vector3i c)
    {
        foreach (var p in partners)
        {
            if (WrapAbs(c.X - p.Cell.X) <= WorldConstants.BaseZoneRadius
                && System.Math.Abs(c.Y - p.Cell.Y) <= WorldConstants.BaseZoneRadius
                && WrapAbsZ(c.Z - p.Cell.Z) <= WorldConstants.BaseZoneRadius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes one group's supplied air volume: flood every pocket reachable from the partner cores' surroundings
    /// and from each energy door in reach, keep the SEALED ones, then propagate "supplied" from the radius-8 cubes
    /// outwards across energy-door connections. Returns the union of the supplied pockets' cells (empty when nothing
    /// seals). With <paramref name="previous"/> (the last fill's cells) <paramref name="lost"/> says why cells that held
    /// air no longer do (<see cref="BaseAirLeak.None"/> when none dropped out, or they turned solid). Reads blocks via
    /// <see cref="ServerWorld.GetBlockIfLoaded"/> so an idle base can never drag chunk generation; around an
    /// actively-used base the chunks are resident anyway (an unloaded cell reads as air, joins a fill and at worst
    /// leaks that pocket).
    /// </summary>
    private HashSet<Vector3i> ComputeSuppliedCells(List<ServerBase> partners, int[] reaches, HashSet<Vector3i>? previous,
        out int rooms, out BaseAirLeak lost)
    {
        rooms = 0;
        lost = BaseAirLeak.None;
        BaseAirComputesForTest++;
        int circ = _world.Circumference;
        var doorCells = EnergyDoorCells();            // canonical door cell → door index
        var cellPocket = new Dictionary<Vector3i, int>();
        var pockets = new List<Pocket>();

        // Seeds: each core's own neighbours (the pocket the Grundstein physically sits in — often open air, in which
        // case that pocket simply leaks) plus both sides of every energy door in reach.
        var seeds = new List<Vector3i>();
        foreach (var p in partners)
        {
            AddAirNeighbours(WorldConstants.CanonicalBlock(p.Cell, circ), seeds);
        }

        foreach (var doorCell in doorCells.Keys)
        {
            if (InAirBoxes(partners, reaches, doorCell))
            {
                AddAirNeighbours(doorCell, seeds);
            }
        }

        int visited = 0;
        foreach (var seed in seeds)
        {
            var c = WorldConstants.CanonicalBlock(seed, circ);
            if (!cellPocket.ContainsKey(c) && !doorCells.ContainsKey(c) && !IsAirtightCell(c))
            {
                var pocket = FillAirPocket(c, partners, reaches, doorCells, cellPocket, pockets.Count);
                pockets.Add(pocket);
                visited += pocket.Cells.Count;
            }
        }

        BaseAirCellsVisitedForTest = visited;

        // Supplied = sealed AND (touches a partner cube OR reachable from a supplied pocket through an energy door).
        // Fixpoint over the door graph — each round can light up the next room in a chain.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in pockets)
            {
                if (p.Supplied || !p.Sealed)
                {
                    continue;
                }

                bool connect = p.TouchesCube;
                if (!connect)
                {
                    foreach (int door in p.Doors)
                    {
                        foreach (var q in pockets)
                        {
                            if (q.Supplied && q.Doors.Contains(door))
                            {
                                connect = true;
                                break;
                            }
                        }

                        if (connect)
                        {
                            break;
                        }
                    }
                }

                if (connect)
                {
                    p.Supplied = true;
                    changed = true;
                }
            }
        }

        var supplied = new HashSet<Vector3i>();
        foreach (var p in pockets)
        {
            if (p.Supplied)
            {
                rooms++;
                supplied.UnionWith(p.Cells);
            }
        }

        // The door cells themselves breathe when a supplied pocket borders them (standing IN the air
        // curtain must not read as "no life support" for the head cell).
        var around = new List<Vector3i>(6);
        foreach (var doorCell in doorCells.Keys)
        {
            around.Clear();
            AddAirNeighbours(doorCell, around);
            foreach (var n in around)
            {
                if (supplied.Contains(WorldConstants.CanonicalBlock(n, circ)))
                {
                    supplied.Add(doorCell);
                    break;
                }
            }
        }

        if (previous is { Count: > 0 })
        {
            lost = LostAirCause(previous, supplied, cellPocket, pockets);
        }

        return supplied;
    }

    /// <summary>Why cells of the last fill no longer hold air (#2542): the failure of the pocket they fell into now —
    /// the informative causes (too large, too far) win over a plain leak, a leak over a lost connection. Cells the new
    /// fill never reached (built over, or past the point an over-budget fill stopped) say nothing.</summary>
    private static BaseAirLeak LostAirCause(HashSet<Vector3i> previous, HashSet<Vector3i> supplied,
        Dictionary<Vector3i, int> cellPocket, List<Pocket> pockets)
    {
        var found = BaseAirLeak.None;
        foreach (var c in previous)
        {
            if (supplied.Contains(c) || !cellPocket.TryGetValue(c, out int index))
            {
                continue;
            }

            var p = pockets[index];
            var why = p.Sealed ? BaseAirLeak.Disconnected : p.Leak;
            if (why is BaseAirLeak.TooLarge or BaseAirLeak.TooFar)
            {
                return why;
            }

            if (found == BaseAirLeak.None || (found == BaseAirLeak.Disconnected && why == BaseAirLeak.Open))
            {
                found = why;
            }
        }

        return found;
    }

    /// <summary>Floods the pocket around <paramref name="start"/> (a canonical, non-airtight, non-door cell) inside the
    /// partners' boxes and records whether — and why — it failed to seal. <paramref name="cellPocket"/> (optional)
    /// receives the pocket index of every cell the fill adds. With <paramref name="locate"/> (the on-demand diagnosis,
    /// #2614) EVERY dequeued cell probes the sky instead of every power-of-two-th one: the fill runs outward from the
    /// player, so the first open column it meets is the leak nearest to them — the cell under the sand patch, the column
    /// under the hole — and that column is what gets named. A per-column memo keeps it linear (see
    /// <see cref="ProbeSkyOnce"/>); the group fills keep the sparse probe.</summary>
    private Pocket FillAirPocket(Vector3i start, List<ServerBase> partners, int[] reaches, Dictionary<Vector3i, int> doorCells,
        Dictionary<Vector3i, int>? cellPocket, int index, bool locate = false)
    {
        int circ = _world.Circumference;
        var pocket = new Pocket();
        var frontier = new Queue<Vector3i>();
        var columns = locate ? new Dictionary<(int X, int Z), (int Lo, int Hi)>() : null;
        pocket.Cells.Add(start);
        if (cellPocket != null)
        {
            cellPocket[start] = index;
        }

        frontier.Enqueue(start);

        // A seed under the open sky (a core in the open, the outside of an entrance door) is decided at once.
        var leak = AirOpenToSky(start, partners, reaches, doorCells, pocket) ? BaseAirLeak.Open : BaseAirLeak.None;
        int dequeued = 0;
        while (leak == BaseAirLeak.None && frontier.Count > 0)
        {
            if (pocket.Cells.Count > WorldConstants.BaseSealedRoomMaxCells)
            {
                leak = BaseAirLeak.TooLarge;
                break;
            }

            var cell = frontier.Dequeue();
            dequeued++;
            bool open = columns != null
                ? ProbeSkyOnce(cell, partners, reaches, doorCells, pocket, columns)
                : ((dequeued & (dequeued - 1)) == 0 || dequeued % AirSkyProbeEvery == 0) && AirOpenToSky(cell, partners, reaches, doorCells, pocket);
            if (open)
            {
                leak = BaseAirLeak.Open;
                break;
            }

            if (!pocket.TouchesCube && InAirCubes(partners, cell))
            {
                pocket.TouchesCube = true;
            }

            for (int i = 0; i < 6; i++)
            {
                var n = WorldConstants.CanonicalBlock(new Vector3i(cell.X + AirDx[i], cell.Y + AirDy[i], cell.Z + AirDz[i]), circ);
                if (pocket.Cells.Contains(n))
                {
                    continue;
                }

                if (doorCells.TryGetValue(n, out int door))
                {
                    pocket.Doors.Add(door); // the curtain bounds the pocket AND links it onward
                    continue;
                }

                if (IsAirtightCell(n))
                {
                    continue; // a wall bounds the pocket wherever it stands — even just outside the box
                }

                if (!InAirBoxes(partners, reaches, n))
                {
                    // An open cell past every reach box: straight up through open cells is the sky, any other way
                    // out is a space that reaches farther than the base supports.
                    leak = AirOpenToSky(cell, partners, reaches, doorCells, pocket) ? BaseAirLeak.Open : BaseAirLeak.TooFar;
                    break;
                }

                pocket.Cells.Add(n);
                if (cellPocket != null)
                {
                    cellPocket[n] = index;
                }

                frontier.Enqueue(n);
            }
        }

        if (leak == BaseAirLeak.None && pocket.Cells.Count > WorldConstants.BaseSealedRoomMaxCells)
        {
            leak = BaseAirLeak.TooLarge;
        }

        pocket.Leak = leak;
        pocket.Sealed = leak == BaseAirLeak.None;
        return pocket;
    }

    /// <summary>Whether nothing but open cells lie above <paramref name="c"/> up past the top of every reach box
    /// (#2543). Exactly the column the fill would climb to leave the box, so a pocket holding such a cell can never
    /// seal — answering it now saves running the fill to the budget. Stops at the first airtight cube or door cell.
    /// When open, <paramref name="pocket"/> records where the air gets out (#2614, see <see cref="ColumnOpenToSky"/>).</summary>
    private bool AirOpenToSky(Vector3i c, List<ServerBase> partners, int[] reaches, Dictionary<Vector3i, int> doorCells, Pocket pocket)
        => ColumnOpenToSky(new Vector3i(c.X, c.Y + 1, c.Z), partners, reaches, doorCells, pocket);

    /// <summary><see cref="AirOpenToSky"/> for the locating fill (#2614), which probes every cell: <paramref name="columns"/>
    /// remembers per column a stretch [Lo, Hi] whose cells below Hi do not seal and whose Hi does, so a probe starting
    /// inside it is answered at once and one climbing into it stops there. Each column is scanned about once however
    /// many of its cells the fill dequeues. Only a "closed" answer is memoised — an open one ends the fill, and its
    /// column is scanned once more to name the leak.</summary>
    private bool ProbeSkyOnce(Vector3i c, List<ServerBase> partners, int[] reaches, Dictionary<Vector3i, int> doorCells,
        Pocket pocket, Dictionary<(int X, int Z), (int Lo, int Hi)> columns)
    {
        int from = c.Y + 1;
        var key = (c.X, c.Z);
        bool known = columns.TryGetValue(key, out var run);
        if (known && from >= run.Lo && from <= run.Hi)
        {
            return false;
        }

        for (int y = from; ; y++)
        {
            if (known && y == run.Lo)
            {
                columns[key] = (from, run.Hi); // climbed into a stretch already known to end at a seal
                return false;
            }

            var up = new Vector3i(c.X, y, c.Z);
            if (doorCells.ContainsKey(up) || IsAirtightCell(up))
            {
                columns[key] = (from, y);
                return false;
            }

            if (!InAirBoxes(partners, reaches, up))
            {
                return AirOpenToSky(c, partners, reaches, doorCells, pocket); // open: scan once more, naming the leak
            }
        }
    }

    /// <summary>The column scan behind <see cref="AirOpenToSky"/>, from <paramref name="bottom"/> itself upward. When it
    /// is open it writes the pocket's leak (#2614): the first cell on the way holding a block — one that does not seal,
    /// or the scan would have stopped there — or, when the whole way is air, the last cell inside the reach box.</summary>
    private bool ColumnOpenToSky(Vector3i bottom, List<ServerBase> partners, int[] reaches, Dictionary<Vector3i, int> doorCells,
        Pocket pocket)
    {
        Vector3i? through = null;
        var throughId = BlockId.Air;
        for (int y = bottom.Y; ; y++)
        {
            var up = new Vector3i(bottom.X, y, bottom.Z);
            if (doorCells.ContainsKey(up))
            {
                return false;
            }

            var id = _world.GetBlockIfLoaded(up);
            if (IsAirtightCell(up, id))
            {
                return false;
            }

            if (!InAirBoxes(partners, reaches, up))
            {
                pocket.LeakCell = through ?? new Vector3i(bottom.X, y - 1, bottom.Z);
                pocket.LeakBlock = throughId;
                return true;
            }

            if (through is null && !id.IsAir)
            {
                through = up;
                throughId = id;
            }
        }
    }

    /// <summary>An airtight full cube at a canonical cell (shaped cells leak; paint bits don't).</summary>
    private bool IsAirtightCell(Vector3i c) => IsAirtightCell(c, _world.GetBlockIfLoaded(c));

    /// <summary><see cref="IsAirtightCell(Vector3i)"/> with the cell's block already read.</summary>
    private bool IsAirtightCell(Vector3i c, BlockId id)
    {
        if (id.IsAir)
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def is { Airtight: true } && ShapeCode.IsCube(_world.GetShapeIfLoaded(c));
    }

    /// <summary>#2614: whether something a body would bump into stands above the canonical cell inside the boxes — a
    /// ceiling, however leaky. Tree crowns do not count: standing under a tree outside is not standing in a room.</summary>
    private bool RoofAbove(Vector3i canonical, List<ServerBase> partners, int[] reaches)
    {
        for (int y = canonical.Y + 1; ; y++)
        {
            var up = new Vector3i(canonical.X, y, canonical.Z);
            if (!InAirBoxes(partners, reaches, up))
            {
                return false;
            }

            if (IsCollidingBlock(_world.GetBlockIfLoaded(up), fluidsPass: false, foliagePasses: true))
            {
                return true;
            }
        }
    }

    private static void AddAirNeighbours(Vector3i pos, List<Vector3i> into)
    {
        for (int i = 0; i < 6; i++)
        {
            into.Add(new Vector3i(pos.X + AirDx[i], pos.Y + AirDy[i], pos.Z + AirDz[i]));
        }
    }

    /// <summary>One flood-filled air pocket (see the class doc for the model).</summary>
    private sealed class Pocket
    {
        public readonly HashSet<Vector3i> Cells = new();
        public readonly HashSet<int> Doors = new(); // energy-door indices bounding this pocket
        public bool Sealed;
        public BaseAirLeak Leak;
        public bool TouchesCube;
        public bool Supplied;
        public Vector3i? LeakCell;  // #2614: where an Open pocket's air gets out (canonical)
        public BlockId LeakBlock;   // the block there — air when the way up is open
    }

    /// <summary>Cells covered by ENERGY doors in the active world (ship hatches + player-built energy
    /// doors + stamped door_energy markers), keyed to the door's index: the door's width along its wall
    /// axis × the 3-tall doorway column. These bound pockets AND connect them — the field is an air
    /// curtain whether the panels are open or not, so auto-opening on approach never depressurises a
    /// room. Mechanical door kinds are deliberately absent: they leak.</summary>
    private Dictionary<Vector3i, int> EnergyDoorCells()
    {
        var cells = new Dictionary<Vector3i, int>();
        int circ = _world.Circumference;
        for (int i = 0; i < _doors.Count; i++)
        {
            var d = _doors[i];
            if (d.Kind != "energy")
            {
                continue;
            }

            // Reconstruct the covered cells from the gap centre + width (see MakeDoor): the lowest cell
            // sits (Width-1)/2 left of the centre. Round kills the float error in the exact .5 centres.
            int by = (int)System.Math.Floor(d.Pos.Y);
            int low = (int)System.Math.Round((d.AxisX ? d.Pos.X : d.Pos.Z) - 0.5f - (d.Width - 1f) * 0.5f);
            int fixedAxis = d.AxisX ? (int)System.Math.Floor(d.Pos.Z) : (int)System.Math.Floor(d.Pos.X);
            for (int w = 0; w < (int)d.Width; w++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    cells[WorldConstants.CanonicalBlock(d.AxisX
                        ? new Vector3i(low + w, by + dy, fixedAxis)
                        : new Vector3i(fixedAxis, by + dy, low + w), circ)] = i;
                }
            }
        }

        return cells;
    }

    /// <summary>Tells every player within the group's boxes why air just went away (#2542) — localized client-side
    /// via the generic <c>@srv.*</c> tokens (the total loss through a leak keeps its <c>@base_air_lost</c> line).
    /// Transition-triggered only (the next recompute after the change), so it cannot spam; it also counts as "told"
    /// for the walk-out diagnosis so a player standing in the room does not hear it twice.</summary>
    private void WarnBaseAirChange(BaseAirVolume group, BaseAirLeak cause, bool total)
    {
        string text = BaseAirWarning(cause, group.Reaches.Length > 0 ? group.Reaches.Max() : SealedRoomMaxReach, total);
        int circ = _world.Circumference;
        foreach (var session in JoinedInActiveWorld())
        {
            var pos = session.State.Position;
            var cell = WorldConstants.CanonicalBlock(new Vector3i(
                (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z)), circ);
            if (InAirBoxes(group.Partners, group.Reaches, cell))
            {
                Send(session, new Networking.Messages.ServerMessage { Text = text });
                _baseAirTold[(session.State.PlayerId, cause)] = _uptime;
            }
        }
    }

    /// <summary>The server token naming a base-air cause (#2542).</summary>
    private static string BaseAirWarning(BaseAirLeak cause, int reach, bool total) => cause switch
    {
        BaseAirLeak.TooLarge => "@srv.base.air_too_large:" + WorldConstants.BaseSealedRoomMaxCells.ToString(CultureInfo.InvariantCulture),
        BaseAirLeak.TooFar => "@srv.base.air_too_far:" + reach.ToString(CultureInfo.InvariantCulture),
        BaseAirLeak.Disconnected => "@srv.base.air_disconnected",
        _ => total ? "@base_air_lost" : "@srv.base.air_room_leak",
    };

    /// <summary>
    /// Called on every oxygen tick with whether the player breathes base air (#2542). When they just walked out of it
    /// near a base on a world without breathable air — and nothing else keeps them breathing — the pocket at their feet
    /// is diagnosed once and an informative cause is told: too large, too far, or sealed but not connected. A leak is
    /// told too when the spot is clearly a room (#2614: the pocket touches an energy door, or there is a roof above the
    /// player) — with the block and cell the air gets out through. A leak under the open sky stays silent (that is
    /// simply "outside"), the client's "life support lost" line already covers it. Each cause is told once per
    /// <see cref="BaseAirRetellInterval"/>.
    /// </summary>
    private void TrackBaseAirPresence(PlayerSession session, Vector3i cell, bool atBase, bool otherLifeSupport)
    {
        string id = session.State.PlayerId;
        if (atBase)
        {
            _baseAirHad.Add(id);
            return;
        }

        if (!_baseAirHad.Remove(id) || otherLifeSupport || !Rules.OxygenEnabledFor(session.State.ModeOverride) || AtmosphereBreathable)
        {
            return;
        }

        if (_baseAirDiagnosedAt.TryGetValue(id, out double at) && _uptime - at < BaseAirDiagnoseInterval)
        {
            return;
        }

        _baseAirDiagnosedAt[id] = _uptime;
        var diag = DiagnoseBaseAir(cell);
        var cause = diag.Status switch
        {
            BaseAirStatus.TooLarge => BaseAirLeak.TooLarge,
            BaseAirStatus.TooFar => BaseAirLeak.TooFar,
            BaseAirStatus.Disconnected => BaseAirLeak.Disconnected,
            BaseAirStatus.Leak when diag.LooksLikeRoom && diag.LeakCell is not null => BaseAirLeak.Open, // #2614
            _ => BaseAirLeak.None,
        };
        if (cause == BaseAirLeak.None
            || (_baseAirTold.TryGetValue((id, cause), out double told) && _uptime - told < BaseAirRetellInterval))
        {
            return;
        }

        _baseAirTold[(id, cause)] = _uptime;
        string text = cause == BaseAirLeak.Open
            ? BaseAirLeakText(session.Locale, "srv.base.air_leak_at", "srv.base.air_leak_open_at", diag, cell)
            : BaseAirWarning(cause, diag.Reach, total: false);
        Send(session, new Networking.Messages.ServerMessage { Text = text });
    }

    /// <summary>
    /// #2614: where a leaking room's air gets out, resolved on the server in the player's language (the block name and
    /// the height phrase are localized too, which one "@srv.*:arg" token cannot carry): <paramref name="blockKey"/> when
    /// the air escapes through a block that does not seal ("through Sand at 30/130/0, 10 blocks above you"),
    /// <paramref name="openKey"/> when the way up is open air. The cell is shown in the frame of
    /// <paramref name="from"/> (the player's cell), so a leak across the wrap seam reads next to the player's own
    /// coordinates; the height is measured from that cell too.
    /// </summary>
    private string BaseAirLeakText(string locale, string blockKey, string openKey, BaseAirDiagnosis diag, Vector3i from)
    {
        var leak = diag.LeakCell ?? from;
        int circ = _world.Circumference;
        int x = from.X + WorldConstants.WrapDeltaX(leak.X - from.X, circ);
        int z = from.Z + WorldConstants.WrapDeltaZ(leak.Z - from.Z, circ);
        int dy = leak.Y - from.Y;
        string height = dy switch
        {
            0 => Localize(locale, "srv.base.air_leak_level"),
            1 => Localize(locale, "srv.base.air_leak_above_one"),
            -1 => Localize(locale, "srv.base.air_leak_below_one"),
            > 1 => Localize(locale, "srv.base.air_leak_above"),
            _ => Localize(locale, "srv.base.air_leak_below"),
        };
        height = height.Replace("{n}", System.Math.Abs(dy).ToString(CultureInfo.InvariantCulture));

        string text = Localize(locale, diag.LeakBlock.IsAir ? openKey : blockKey);
        if (!diag.LeakBlock.IsAir)
        {
            var def = _content.BlockById(diag.LeakBlock);
            string name = LocalizedName(locale, def?.NameKey, def?.Key ?? "?");
            if (def is { Airtight: true })
            {
                // A sealing material that leaks only for its shape (a slab, a stair, a wedge).
                name = Localize(locale, "srv.base.air_leak_shaped").Replace("{block}", name);
            }

            text = text.Replace("{block}", name);
        }

        return text
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", leak.Y.ToString(CultureInfo.InvariantCulture))
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
            .Replace("{height}", height);
    }

    /// <summary>
    /// What the base air is at a cell (#2542): the core zone, supplied by a group (with that group's cells and rooms),
    /// or — for the first group whose boxes hold the cell — why the pocket around it has no air, by filling just that
    /// pocket. <see cref="BaseAirStatus.NoBase"/> when no base reaches the cell. On demand only (<c>/basewalls</c>, a
    /// player walking out of base air), so the one fill of up to the budget is affordable — and it locates a leak (#2614),
    /// probing every cell's column on the way out from the player (memoised per column, so still one fill's work).
    /// </summary>
    private BaseAirDiagnosis DiagnoseBaseAir(Vector3i cell)
    {
        string body = _world.LocationId;
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (InAnyBaseZone(canonical))
        {
            return new BaseAirDiagnosis(BaseAirStatus.Zone, 0, 0, 0);
        }

        BaseAirVolume? reaching = null;
        foreach (var b in _bases)
        {
            if (b.Planet != body)
            {
                continue;
            }

            var group = RefreshBaseAir(b, broadcastOnChange: true);
            if (!InAirBoxes(group.Partners, group.Reaches, canonical))
            {
                continue;
            }

            int reach = group.Reaches.Max();
            if (group.Cells.Contains(canonical))
            {
                return new BaseAirDiagnosis(BaseAirStatus.Supplied, group.Cells.Count, group.Rooms, reach);
            }

            reaching ??= group;
        }

        if (reaching is null)
        {
            return new BaseAirDiagnosis(BaseAirStatus.NoBase, 0, 0, 0);
        }

        int widest = reaching.Reaches.Max();
        var doorCells = EnergyDoorCells();
        if (IsAirtightCell(canonical) || doorCells.ContainsKey(canonical))
        {
            return new BaseAirDiagnosis(BaseAirStatus.Leak, 0, 0, widest); // inside a wall or a doorway: no pocket of its own
        }

        var pocket = FillAirPocket(canonical, reaching.Partners, reaching.Reaches, doorCells, null, 0, locate: true);
        var status = pocket.Leak switch
        {
            BaseAirLeak.TooLarge => BaseAirStatus.TooLarge,
            BaseAirLeak.TooFar => BaseAirStatus.TooFar,
            BaseAirLeak.Open => BaseAirStatus.Leak,
            _ => BaseAirStatus.Disconnected,
        };
        if (status != BaseAirStatus.Leak)
        {
            return new BaseAirDiagnosis(status, pocket.Cells.Count, 0, widest);
        }

        // #2614: a leak keeps where the air gets out, and whether this is clearly a room — a pocket bordering an energy
        // door, or a roof over the cell (sand counts: that is exactly the ceiling that leaks). One column, no extra fill.
        bool room = pocket.Doors.Count > 0 || RoofAbove(canonical, reaching.Partners, reaching.Reaches);
        return new BaseAirDiagnosis(status, pocket.Cells.Count, 0, widest, pocket.LeakCell, pocket.LeakBlock, room);
    }

    /// <summary>Test seam (#2542): the diagnosis at a cell of the active world.</summary>
    internal BaseAirDiagnosis DiagnoseBaseAirForTest(int x, int y, int z) => DiagnoseBaseAir(new Vector3i(x, y, z));

    /// <summary>Drops a removed base's cached air (called when its core is mined), with every group it was part of.</summary>
    private void ForgetBaseAir(int baseId)
    {
        _baseAirOf.Remove(baseId);
        var stale = new List<string>();
        foreach (var kv in _baseAirGroups)
        {
            if (kv.Value.Partners.Any(p => p.Id == baseId))
            {
                stale.Add(kv.Key);
            }
        }

        foreach (string key in stale)
        {
            var group = _baseAirGroups[key];
            _baseAirGroups.Remove(key);
            foreach (int id in _baseAirOf.Where(kv => kv.Value == group).Select(kv => kv.Key).ToList())
            {
                _baseAirOf.Remove(id);
            }
        }
    }
}
