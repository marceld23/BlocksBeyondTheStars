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
/// Sealed-room life support for founded bases (#794; one rule with stations since #2649). Beyond the unconditional
/// radius-8 cube (#782), a base breathes air into every enclosed room it owns, however big and however many:
/// <list type="bullet">
/// <item>A <b>pocket</b> is a maximal region of open cells (air, flora, loose dirt and sand, shaped cells — leaks flow
/// right through those), bounded by airtight full cubes and by <b>every door cell</b> of every kind, open or closed
/// (#2651). It is <b>sealed</b> when its fill ends without reaching the outside: the base's <b>sky line</b> — an open
/// column up past <c>max(core + reach, highest build + margin)</c>. There is no size limit and no reach limit: the fill
/// runs on the shared air engine (<see cref="AirFill"/>, #2650) in vertical spans, time-sliced over ticks.</item>
/// <item>The base core stays the air machine: the rooms of a base are the pockets seeded from the core's neighbours and
/// from both sides of every door within the base's reach box, then from the doors of every SEALED pocket found — at
/// any distance. A leaky pocket never continues the chain, so the search never spreads over a planet, and a room no
/// base finds never breathes.</item>
/// <item><b>Doors are bulkheads</b> (#2653): every door separates two pockets and every door in reach is its own seed, so
/// a leak empties only its own room — the room behind a leaky one keeps its air.</item>
/// <item><b>Partners</b> (#2541): a base pools its air with the bases of its owner's allies on the same body (the union
/// of their seeds and reach boxes).</item>
/// <item><b>Unloaded chunks are unknown</b>, not a leak (see <see cref="PlanetAirSpace"/>): an unloaded wing keeps its
/// air.</item>
/// </list>
/// The result is derived state, kept per partner group and per body. A block set inside the group's area, a door change,
/// a partner's reach or sky line moving, or the <see cref="BaseAirBackstopInterval"/> marks it stale; a new fill then
/// starts at most every <see cref="SealedRoomRecomputeInterval"/> and runs within the per-tick air budget while the
/// previous result keeps answering — nobody loses air mid-fill. The query that starts it always works a fixed minimum of
/// it (#2870), so an ordinary room's refill is answered at once, whatever the clock. A fill past the emergency brake keeps
/// the previous result and logs once. When rooms drop out on a world without breathable air, the players in or at a
/// damaged room are told where the air gets out, and only when every room is gone does everyone in the base hear
/// <c>@base_air_lost</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The minimum Chebyshev half-extent of a base's reach box — the door seeds' box and the wall ring's. Since
    /// #2540 the box grows with what the base's players built (<see cref="BaseWallReach"/>, #1862); this is its floor.</summary>
    private const int SealedRoomMaxReach = 48;

    /// <summary>Minimum seconds between the starts of two fills of one group, however often it is marked dirty — rapid
    /// building or flowing water inside a base refills at most this often.</summary>
    private const double SealedRoomRecomputeInterval = 1.5;

    /// <summary>Seconds a group's result is trusted without a recorded change (#2543). Covers what no block set reports:
    /// chunks loading or unloading and alliances forming or breaking. A braked group waits for a real change instead.</summary>
    private const double BaseAirBackstopInterval = 8.0;

    /// <summary>The longest the backstop grows to for a group whose expensive fills keep changing nothing (#2649 review).</summary>
    private const double BaseAirBackstopMax = 32.0;

    /// <summary>A fill holding at least this many cells counts as expensive for the backstop's backoff.</summary>
    private const long BaseAirExpensiveFillCells = 100_000;

    /// <summary>Minimum seconds between two on-the-spot diagnoses for one player leaving base air (#2542).</summary>
    private const double BaseAirDiagnoseInterval = 3.0;

    /// <summary>Seconds within which the leak is not told to the same player again when they walk out of base air
    /// (#2542) — someone who leaves through the same door into a leaking hall hears it once, not every time.</summary>
    private const double BaseAirRetellInterval = 300.0;

    /// <summary>Seconds a group of a body nobody queries is kept before its result is dropped (memory, #2650).</summary>
    private const double BaseAirIdleDropSeconds = 120.0;

    /// <summary>What <see cref="DiagnoseBaseAir"/> found at a cell (#2542, #2654).</summary>
    internal enum BaseAirStatus : byte
    {
        /// <summary>No base reaches the cell.</summary>
        NoBase,

        /// <summary>Inside a core's 17³ zone: always air.</summary>
        Zone,

        /// <summary>In a room the base supplies.</summary>
        Supplied,

        /// <summary>The room around the cell reaches the outside (or the cell is inside a wall or a doorway).</summary>
        Leak,

        /// <summary>A closed room no door joins to the base, or one the server cannot judge yet (the diagnosis braked).</summary>
        Unknown,
    }

    /// <summary>A diagnosis of the base air at one cell: its status, the cells of the pocket (or the group's supplied
    /// cells), the group's supplied rooms and its widest reach. For a leak (#2614, #2654) also where the air gets out —
    /// <paramref name="LeakCell"/> (canonical) holding <paramref name="LeakBlock"/>, air for a hole — and whether the spot
    /// is clearly a room (<paramref name="LooksLikeRoom"/>: the pocket touches a door, or something stands above the
    /// cell), which is when walking in says so.</summary>
    internal readonly record struct BaseAirDiagnosis(BaseAirStatus Status, int Cells, int Rooms, int Reach,
        Vector3i? LeakCell = null, BlockId LeakBlock = default, bool LooksLikeRoom = false);

    /// <summary>The supplied air cells of a group (the count feeds the core's readout, <c>NetBase.AirCells</c>).</summary>
    internal readonly struct AirCellsView
    {
        public AirCellsView(long count) => Count = (int)System.Math.Min(int.MaxValue, count);

        public int Count { get; }
    }

    /// <summary>A partner group's air: the committed fill (the answer), a pending fill (the next answer) and the triggers.</summary>
    private sealed class BaseAirVolume
    {
        public string Body = string.Empty;            // world the group stands on
        public AirFill? Committed;                    // the last finished fill — what every query answers from
        public PlanetAirSpace? CommittedSpace;
        public HashSet<Vector3i> DoorCells = new();   // doorway cells beside a supplied room (standing IN a door breathes)
        public long CellCount;                        // supplied cells (rooms + their doorways)
        public int Rooms;                             // supplied (sealed) pockets
        public AirFill? Job;                          // a fill in progress (time-sliced)
        public PlanetAirSpace? JobSpace;
        public double StartedAt = double.NegativeInfinity;  // the minimum interval counts from here
        public double ComputedAt = double.NegativeInfinity; // the last commit — the backstop counts from here
        public bool Computed;                         // at least one fill committed — the first one never warns
        public bool Dirty = true;                     // a block set inside the area since the last fill started
        public int DoorsVersion = -1;                 // _baseAirDoorsVersion at the last fill
        public long Signature = long.MinValue;        // the partners' reaches + build tops at the last fill
        public bool Braked;                           // the last fill hit the emergency brake (#2650)
        public bool BrakeLogged;
        public double Backstop = BaseAirBackstopInterval; // grows while expensive fills change nothing (#2649 review)
        public bool BroadcastPending;                 // the readout changed in a fill no query was waiting for
        public double LastUsed;
        public List<ServerBase> Partners = new();     // the bases whose cores supply, ordered by id
        public int[] Reaches = System.Array.Empty<int>(); // their reaches at the last fill, same order
        public int SkyY;                              // the sky line of the last fill

        /// <summary>The supplied cells (count only — the readout).</summary>
        public AirCellsView Cells => new(CellCount);

        /// <summary>Whether a canonical cell holds base air in the committed result.</summary>
        public bool Supplies(Vector3i canonical)
            => Committed?.PocketAt(canonical) is { Sealed: true } || DoorCells.Contains(canonical);
    }

    /// <summary>Supplied volumes per partner group (key: the partners' ids — unique across bodies).</summary>
    private readonly Dictionary<string, BaseAirVolume> _baseAirGroups = new(System.StringComparer.Ordinal);

    /// <summary>Base id → the group volume it was last resolved to.</summary>
    private readonly Dictionary<int, BaseAirVolume> _baseAirOf = new();

    /// <summary>Base id → the highest player-edited Y over its horizontal reach cap (#2651), at any height — the sky line
    /// follows a tower taller than the reach cap. Read from the block-edit store once, grown live by
    /// <see cref="GrowBaseAirTop"/>.</summary>
    private readonly Dictionary<int, int> _baseAirTop = new();

    /// <summary>Bumped whenever the door registry changes (#2543): a door built, removed or rebuilt changes the pockets
    /// without any block being set.</summary>
    private int _baseAirDoorsVersion;

    /// <summary>Player id → when the leak was last told about base air (#2542/#2614).</summary>
    private readonly Dictionary<string, double> _baseAirTold = new(System.StringComparer.Ordinal);

    /// <summary>Players who breathed base air on their last oxygen tick.</summary>
    private readonly HashSet<string> _baseAirHad = new(System.StringComparer.Ordinal);

    /// <summary>Player id → uptime of their last on-the-spot diagnosis.</summary>
    private readonly Dictionary<string, double> _baseAirDiagnosedAt = new(System.StringComparer.Ordinal);

    /// <summary>Test seam (#2543): how many group fills started since start.</summary>
    public int BaseAirComputesForTest { get; private set; }

    /// <summary>Test seam (#2543): how many cells the last committed group fill holds, over all its pockets.</summary>
    public int BaseAirCellsVisitedForTest { get; private set; }

    /// <summary>Test seam (#2650): how many spans the last committed group fill holds, over all its pockets.</summary>
    public int BaseAirSpansVisitedForTest { get; private set; }

    /// <summary>True if the cell lies inside some founded base's supplied room on the current world. Ownership is ignored
    /// just like <see cref="InAnyBaseZone"/> — visitors breathe too. The radius-8 cube is checked separately (and
    /// unconditionally); this only adds the rooms beyond it.</summary>
    private bool InSealedBaseRoom(Vector3i cell)
    {
        string body = _world.LocationId;
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == body && MayHoldBaseAir(b, canonical) && RefreshBaseAir(b, broadcastOnChange: true).Supplies(canonical))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A cheap pre-check before refreshing a base's group for a cell: the cell lies in a partner's reach box or in the area
    /// of the group's fills — or the group has a real change waiting (a block set in its area, a door change, a fill
    /// running), which must start or move a fill whatever is asked (#2651: a base's rooms may lie anywhere along its door
    /// chains, beyond any box a fill has covered yet). A base never resolved is always resolved. What it skips is the
    /// backstop refill for questions about far-away cells, so an idle base still costs nothing (#2543).
    /// </summary>
    private bool MayHoldBaseAir(ServerBase b, Vector3i canonical)
    {
        if (!_baseAirOf.TryGetValue(b.Id, out var known) || known.Body != b.Planet || known.Partners.Count == 0)
        {
            // Not resolved yet (or dropped with a body left behind): only a question from inside its reach box resolves it —
            // a creature check across the planet must not run a base's first fill (#2649 review). Arriving players resolve
            // every base on the body anyway, through the base list.
            return WithinBaseWallReach(b, canonical);
        }

        return known.Job is not null || known.Dirty || known.DoorsVersion != _baseAirDoorsVersion
            || InAirBoxes(known.Partners, canonical) || known.Committed?.InExtent(canonical, 1) == true;
    }

    /// <summary>
    /// The id of the sealed room (air pocket) holding <paramref name="cell"/> — a base room on a planet or a pocket of the
    /// active player station — or null when the cell lies in no sealed room (#2649). Stable within one computed air result,
    /// so two cells compare equal exactly when they share a room; the bed assignment uses it to prefer a bed in the same
    /// room as an NPC's work post.
    /// </summary>
    internal int? AirRoomIdAt(Vector3i cell)
    {
        string body = _world.LocationId;
        if (IsSealedAirStationWorld(body))
        {
            if (_stationsById.TryGetValue(body.Substring("station:".Length), out var station) && station.Stamped
                && StationPocketFor(station, cell, out var pocket) && pocket is not null)
            {
                return pocket.Id;
            }

            return null;
        }

        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var b in _bases)
        {
            if (b.Planet == body && MayHoldBaseAir(b, canonical)
                && RefreshBaseAir(b, broadcastOnChange: true).Committed?.PocketAt(canonical) is { Sealed: true } room)
            {
                return room.Id;
            }
        }

        return null;
    }

    /// <summary>The base's group volume — its committed result — after starting or continuing a fill when it is stale (see
    /// the class doc). With <paramref name="broadcastOnChange"/> a changed cell/room count re-sends the base list on this
    /// body, so the core's air readout on every client stays current (#1267) — the caller building that very list passes
    /// false. <paramref name="complete"/> (test seams) starts a stale fill without waiting for the minimum interval and runs
    /// it to the end at once.</summary>
    private BaseAirVolume RefreshBaseAir(ServerBase b, bool broadcastOnChange, bool complete = false)
    {
        string body = _world.LocationId;
        if (_baseAirOf.TryGetValue(b.Id, out var known) && known.Body == body)
        {
            known.LastUsed = _uptime;
            if (known.Job is null
                && ((!complete && _uptime - known.StartedAt < SealedRoomRecomputeInterval) || !NeedsAirRefresh(known)))
            {
                return known;
            }

            if (known.Job is not null)
            {
                ContinueBaseAir(known, broadcastOnChange, complete);
                return known;
            }
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
            if (known is not null && known.Body == body && known.Committed is not null)
            {
                // A new partner set (an alliance formed or broke): the last result answers until the first fill is done.
                group.Committed = known.Committed;
                group.CommittedSpace = known.CommittedSpace;
                group.DoorCells = known.DoorCells;
                group.CellCount = known.CellCount;
                group.Rooms = known.Rooms;
                group.Computed = true;
            }
        }

        _baseAirOf[b.Id] = group;
        group.LastUsed = _uptime;
        bool started = false;
        if (group.Job is null && (group.StartedAt == double.NegativeInfinity
            || (NeedsAirRefresh(group) && (complete || _uptime - group.StartedAt >= SealedRoomRecomputeInterval))))
        {
            StartBaseAir(group, partners);
            started = true;
        }

        if (group.Job is { } job)
        {
            if (!complete && !group.Computed)
            {
                // The very first fill has no previous result to answer with: it works up to AirFirstFillBudgetMs at once,
                // so a typical base answers right away; a huge one goes on sliced instead of stalling the tick.
                StepFirstAirFill(job);
                if (job.Done)
                {
                    CommitBaseAir(group, broadcastOnChange);
                }
            }
            else
            {
                // The query that starts a refill always works a fixed minimum of it (#2870), like the tick driver: an
                // ordinary room is answered right here on a fast and on a loaded machine alike — the clock alone let a
                // busy runner answer one more tick from the old result.
                ContinueBaseAir(group, broadcastOnChange, complete, guaranteeProgress: started);
            }
        }

        return group;
    }

    /// <summary>Works on the group's pending fill within this tick's budget — with <paramref name="guaranteeProgress"/> at
    /// least the per-tick minimum, with <paramref name="complete"/> to the end — and commits it once done.</summary>
    private void ContinueBaseAir(BaseAirVolume group, bool broadcastOnChange, bool complete, bool guaranteeProgress = false)
    {
        var job = group.Job!;
        StepAirFill(job, guaranteeProgress, complete);
        if (job.Done)
        {
            CommitBaseAir(group, broadcastOnChange);
        }
    }

    /// <summary>Whether a group's result is stale: a block set inside its area, a door change, a partner's reach or build top
    /// moved, or the backstop elapsed (#2543) — the backstop not for a braked group, which waits for a real change.</summary>
    private bool NeedsAirRefresh(BaseAirVolume vol)
        => vol.Dirty || vol.DoorsVersion != _baseAirDoorsVersion
           || (!vol.Braked && _uptime - vol.ComputedAt >= vol.Backstop)
           || AirSignature(vol.Partners) != vol.Signature;

    /// <summary>The partners' reaches and build tops folded into one number — a change moves the seeds' boxes or the sky.</summary>
    private long AirSignature(List<ServerBase> partners)
    {
        long signature = 17;
        foreach (var p in partners)
        {
            signature = signature * 31 + BaseWallReach(p);
            signature = signature * 31 + BaseAirTop(p);
        }

        return signature;
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

    /// <summary>The highest player-edited Y over a base's horizontal reach cap, at any height (#2651) — int.MinValue when
    /// nobody built there. Asked of the block-edit store once per base (per canonical piece across the seams).</summary>
    private int BaseAirTop(ServerBase b)
    {
        if (_baseAirTop.TryGetValue(b.Id, out int top))
        {
            return top;
        }

        int circ = _world.Circumference;
        int cap = WalledReachCapFor(_world);
        top = int.MinValue;
        foreach (var (xLo, xHi) in CanonicalRanges(b.Cell.X - cap, b.Cell.X + cap, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(b.Cell.Z - cap, b.Cell.Z + cap,
                         -WorldConstants.LatitudePeriodFor(circ) / 2, WorldConstants.LatitudePeriodFor(circ)))
            {
                if (_repo.TryGetPlayerBlockEditBounds(b.Planet, new Vector3i(xLo, b.Cell.Y - cap, zLo),
                        new Vector3i(xHi, MaxBuildY, zHi), out _, out var hi))
                {
                    top = System.Math.Max(top, hi.Y);
                }
            }
        }

        _baseAirTop[b.Id] = top;
        return top;
    }

    /// <summary>A player set a block on a resident world (#2651): the build tops it raises grow — a base's (its sky line
    /// follows the tallest tower) and the chunk column's (an unloaded chunk under that height is no open sky).</summary>
    private void GrowBaseAirTop(ServerWorld world, Vector3i cell)
    {
        var canonical = WorldConstants.CanonicalBlock(cell, world.Circumference);
        var column = (world.LocationId, canonical.X >> 4, canonical.Z >> 4);
        if (_airColumnTop.TryGetValue(column, out int columnTop) && canonical.Y > columnTop)
        {
            _airColumnTop[column] = canonical.Y;
        }

        if (_baseAirTop.Count == 0)
        {
            return;
        }

        int circ = world.Circumference;
        int cap = WalledReachCapFor(world);
        foreach (var b in _bases)
        {
            if (b.Planet == world.LocationId && _baseAirTop.TryGetValue(b.Id, out int top) && canonical.Y > top
                && System.Math.Abs(WorldConstants.WrapDeltaX(canonical.X - b.Cell.X, circ)) <= cap
                && System.Math.Abs(WorldConstants.WrapDeltaZ(canonical.Z - b.Cell.Z, circ)) <= cap
                && canonical.Y >= b.Cell.Y - cap)
            {
                _baseAirTop[b.Id] = canonical.Y;
            }
        }
    }

    /// <summary>The sky line of a group: an open column reaching it is the outside — up past the tallest reach box and past
    /// the highest thing any partner's players built.</summary>
    private int BaseAirSkyY(List<ServerBase> partners, int[] reaches)
    {
        int sky = int.MinValue, top = int.MinValue;
        for (int i = 0; i < partners.Count; i++)
        {
            sky = System.Math.Max(sky, partners[i].Cell.Y + reaches[i]);
            top = System.Math.Max(top, BaseAirTop(partners[i]));
        }

        return top == int.MinValue ? sky : System.Math.Max(sky, top + BaseAirSkyMargin);
    }

    /// <summary>The air engine's view of the active world for a group: its doors, its sky line, its chunk-column tops.</summary>
    private PlanetAirSpace NewPlanetAirSpace(int skyY)
    {
        string body = _world.LocationId;
        return new PlanetAirSpace(_world, AirtightTable(), AirDoorBlockTable(), AllDoorCells(), skyY, MinBuildY,
            (cx, cz) => AirColumnTop(body, cx, cz), BaseAirSkyMargin);
    }

    /// <summary>Starts a group fill (#2651): seeds at every partner core's neighbours and on both sides of every door within
    /// a partner's reach box; the engine chains through the doors of the sealed rooms it finds.</summary>
    private void StartBaseAir(BaseAirVolume group, List<ServerBase> partners)
    {
        int circ = _world.Circumference;
        var reaches = new int[partners.Count];
        for (int i = 0; i < partners.Count; i++)
        {
            reaches[i] = BaseWallReach(partners[i]);
        }

        int skyY = BaseAirSkyY(partners, reaches);
        var space = NewPlanetAirSpace(skyY);
        var job = new AirFill(space, _airLimits, chainDoors: true, WorldConstants.CanonicalBlock(partners[0].Cell, circ));
        foreach (var p in partners)
        {
            var core = WorldConstants.CanonicalBlock(p.Cell, circ);
            job.AddSeed(new Vector3i(core.X + 1, core.Y, core.Z));
            job.AddSeed(new Vector3i(core.X - 1, core.Y, core.Z));
            job.AddSeed(new Vector3i(core.X, core.Y + 1, core.Z));
            job.AddSeed(new Vector3i(core.X, core.Y - 1, core.Z));
            job.AddSeed(new Vector3i(core.X, core.Y, core.Z + 1));
            job.AddSeed(new Vector3i(core.X, core.Y, core.Z - 1));
        }

        var doorSeeds = new List<Vector3i>();
        var seededDoors = new HashSet<int>();
        for (int door = 0; door < _doors.Count; door++)
        {
            foreach (var c in space.DoorCells(door))
            {
                if (InAirBoxes(partners, reaches, c))
                {
                    space.AddDoorSeeds(door, doorSeeds); // both sides: every door in reach is its own seed (#2653)
                    seededDoors.Add(door);
                    break;
                }
            }
        }

        // The chain never breaks (#2653, review): every door that bordered a supplied room of the last result is a seed too,
        // however far out — so in core → A → door beyond the reach box → B, a leak in A leaves B its air.
        foreach (var c in group.DoorCells)
        {
            int door = space.DoorAt(c.X, c.Y, c.Z);
            if (door >= 0 && seededDoors.Add(door))
            {
                space.AddDoorSeeds(door, doorSeeds);
            }
        }

        foreach (var seed in doorSeeds)
        {
            job.AddSeed(seed);
        }

        group.Job = job;
        group.JobSpace = space;
        group.Partners = partners;
        group.Reaches = reaches;
        group.SkyY = skyY;
        group.Signature = AirSignature(partners);
        group.DoorsVersion = _baseAirDoorsVersion;
        group.Dirty = false;
        group.StartedAt = _uptime;
        BaseAirComputesForTest++;
    }

    /// <summary>Swaps a finished fill in as the group's answer — unless it braked, which keeps the previous answer (#2650) —
    /// tells the players in damaged rooms (#2653) and flags the readout for re-sending when it changed.</summary>
    private void CommitBaseAir(BaseAirVolume group, bool broadcastOnChange)
    {
        var job = group.Job!;
        var space = group.JobSpace!;
        group.Job = null;
        group.JobSpace = null;
        group.ComputedAt = _uptime;
        bool first = !group.Computed;
        group.Computed = true;
        if (job.Braked)
        {
            group.Braked = true;
            if (!group.BrakeLogged)
            {
                group.BrakeLogged = true;
                _log.Warn($"Base air: the fill for bases {string.Join(",", group.Partners.Select(p => "#" + p.Id))} on {group.Body} " +
                          $"passed the emergency brake ({job.SpanCount} spans, {job.CellCount} cells) — the last result is kept; " +
                          "nobody loses air until a fill fits again.");
            }

            return;
        }

        // The per-room brake (#2650, review): a room past the brake is unknown — it keeps what the last result knew of it
        // (carried below) while every other room is judged as usual; told once in the log, and the group then waits for a real
        // change instead of filling that room again on every backstop.
        group.Braked = job.BrakedPockets > 0;
        if (group.Braked && !group.BrakeLogged)
        {
            group.BrakeLogged = true;
            _log.Warn($"Base air: a room of bases {string.Join(",", group.Partners.Select(p => "#" + p.Id))} on {group.Body} " +
                      $"passed the per-room emergency brake ({job.BrakedPockets} room(s)) — it keeps its last known air.");
        }

        var old = group.Committed;
        var oldDoorCells = group.DoorCells;
        int oldRooms = group.Rooms;
        long oldCells = group.CellCount;

        // Unknown is not a leak (#2650): rooms this fill could not look at because unloaded chunks or a braked room stood in
        // the way keep their last known state — room by room, and never a room this fill found again (review).
        if (old is not null)
        {
            job.AdoptSealedFrom(old);
        }

        long cells = 0;
        int rooms = 0;
        bool carried = false;
        var doorCells = new HashSet<Vector3i>();
        foreach (var pocket in job.Pockets)
        {
            if (!pocket.Sealed)
            {
                continue;
            }

            rooms++;
            cells += pocket.Cells;
            carried |= pocket.Carried;
            if (pocket.Doors is not null)
            {
                foreach (int door in pocket.Doors)
                {
                    foreach (var c in space.DoorCells(door))
                    {
                        doorCells.Add(c); // standing IN the doorway of a supplied room must not read as no air
                    }
                }
            }
        }

        if (carried)
        {
            foreach (var c in oldDoorCells)
            {
                if (!doorCells.Contains(c) && BesideCarriedRoom(job, c))
                {
                    doorCells.Add(c);
                }
            }
        }

        group.Committed = job;
        group.CommittedSpace = space;
        group.DoorCells = doorCells;
        group.CellCount = cells + doorCells.Count;
        group.Rooms = rooms;
        BaseAirCellsVisitedForTest = (int)System.Math.Min(int.MaxValue, job.CellCount);
        BaseAirSpansVisitedForTest = job.SpanCount;

        // An expensive fill that changed nothing (a door into a huge natural cave whose far parts are unloaded) backs the
        // backstop off, up to BaseAirBackstopMax; a change or a cheap fill resets it (#2649 review). Block sets, doors and
        // reach changes still refill at once.
        bool unchanged = group.CellCount == oldCells && rooms == oldRooms;
        group.Backstop = unchanged && job.CellCount >= BaseAirExpensiveFillCells
            ? System.Math.Min(group.Backstop * 2, BaseAirBackstopMax)
            : BaseAirBackstopInterval;

        // Air just went away somewhere (a wall mined, a door pulled …): tell the players in the damaged rooms where it
        // gets out — but only where it matters, i.e. when the world's own air is NOT breathable and oxygen is on.
        if (!first && old is not null && Rules.OxygenEnabled && !AtmosphereBreathable)
        {
            WarnBaseAirChange(group, old, oldDoorCells, oldRooms);
        }

        if (group.CellCount != oldCells || rooms != oldRooms)
        {
            if (broadcastOnChange && group.Body == _world.LocationId)
            {
                BroadcastBasesOn(group.Body);
            }
            else
            {
                group.BroadcastPending = true;
            }
        }
    }

    /// <summary>Whether a doorway cell borders a room carried over from the last fill.</summary>
    private static bool BesideCarriedRoom(AirFill fill, Vector3i c)
        => fill.PocketAt(c.X + 1, c.Y, c.Z) is { Carried: true } || fill.PocketAt(c.X - 1, c.Y, c.Z) is { Carried: true }
           || fill.PocketAt(c.X, c.Y + 1, c.Z) is { Carried: true } || fill.PocketAt(c.X, c.Y - 1, c.Z) is { Carried: true }
           || fill.PocketAt(c.X, c.Y, c.Z + 1) is { Carried: true } || fill.PocketAt(c.X, c.Y, c.Z - 1) is { Carried: true };

    /// <summary>A block was set on a resident world (#2543): every group whose area holds the cell (its reach boxes or the
    /// box of its fills, plus one — the fill reads the ring just outside its open cells) refills when next due. On a
    /// sealed-air station world the station's air is marked instead (#2652).</summary>
    private void MarkBaseAirDirty(ServerWorld world, Vector3i cell)
    {
        if (IsSealedAirStationWorld(world.LocationId))
        {
            if (_stationAir.TryGetValue(world.LocationId.Substring("station:".Length), out var station))
            {
                station.Dirty = true;
            }

            return;
        }

        if (_baseAirGroups.Count == 0)
        {
            return;
        }

        var canonical = WorldConstants.CanonicalBlock(cell, world.Circumference);
        foreach (var g in _baseAirGroups.Values)
        {
            if (!g.Dirty && g.Body == world.LocationId
                && (InAirBoxes(g.Partners, g.Reaches, canonical, margin: 1, world.Circumference)
                    || g.Committed?.InExtent(canonical, 1) == true
                    || g.Job?.InExtent(canonical, 1) == true))
            {
                g.Dirty = true;
            }
        }
    }

    /// <summary>The door registry changed (#2543) — doors bound and link pockets without a block being set.</summary>
    private void BaseAirDoorsChanged() => _baseAirDoorsVersion++;

    /// <summary>Test/inspection: a base's supplied air (cells, sealed rooms) on the active world, a due fill run to the end.</summary>
    public (int Cells, int Rooms) BaseAirForTest(int baseId)
    {
        var b = _bases.FirstOrDefault(x => x.Id == baseId && x.Planet == _world.LocationId);
        if (b is null)
        {
            return (0, 0);
        }

        var vol = RefreshBaseAir(b, broadcastOnChange: false, complete: true);
        return (vol.Cells.Count, vol.Rooms);
    }

    /// <summary>Test seam (#2650): whether a base's group has a fill in progress right now.</summary>
    internal bool BaseAirFillPendingForTest(int baseId) => _baseAirOf.TryGetValue(baseId, out var g) && g.Job is not null;

    /// <summary>Test seam (#2650): whether a cell of the active world breathes base air, WITHOUT refreshing anything — the
    /// committed answer as it stands.</summary>
    internal bool BaseAirSuppliesNowForTest(int baseId, Vector3i cell)
        => _baseAirOf.TryGetValue(baseId, out var g) && g.Supplies(WorldConstants.CanonicalBlock(cell, _world.Circumference));

    /// <summary>Test seam (#2649 review): whether a base is resolved to a group right now (not dropped).</summary>
    internal bool BaseAirGroupKnownForTest(int baseId) => _baseAirOf.ContainsKey(baseId);

    /// <summary>Test seam (#2650): whether a base's group stopped at the emergency brake.</summary>
    internal bool BaseAirBrakedForTest(int baseId) => _baseAirOf.TryGetValue(baseId, out var g) && g.Braked;

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
    private bool InAirBoxes(List<ServerBase> partners, int[] reaches, Vector3i c, int margin = 0, int circ = 0)
    {
        if (circ <= 0)
        {
            circ = _world.Circumference;
        }

        for (int i = 0; i < partners.Count && i < reaches.Length; i++)
        {
            int r = reaches[i] + margin;
            var core = partners[i].Cell;
            if (System.Math.Abs(WorldConstants.WrapDeltaX(c.X - core.X, circ)) <= r && System.Math.Abs(c.Y - core.Y) <= r
                && System.Math.Abs(WorldConstants.WrapDeltaZ(c.Z - core.Z, circ)) <= r)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The cell beside a player — their feet, their head or a face neighbour of the feet — that held base air in
    /// <paramref name="old"/> and holds none now: the player stands in or at a room that just dropped out.</summary>
    private static Vector3i? LostAirCellAt(Vector3i feet, AirFill old, HashSet<Vector3i> oldDoorCells, BaseAirVolume now, int circ)
    {
        Vector3i[] around =
        {
            feet, new(feet.X, feet.Y + 1, feet.Z), new(feet.X + 1, feet.Y, feet.Z), new(feet.X - 1, feet.Y, feet.Z),
            new(feet.X, feet.Y, feet.Z + 1), new(feet.X, feet.Y, feet.Z - 1), new(feet.X, feet.Y - 1, feet.Z),
        };
        foreach (var raw in around)
        {
            var c = WorldConstants.CanonicalBlock(raw, circ);
            bool had = old.PocketAt(c) is { Sealed: true } || oldDoorCells.Contains(c);
            if (had && !now.Supplies(c))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells the players in or at a room that just lost its air (#2653) where the air gets out — resolved in their
    /// language, at the opening's height — or <c>@srv.base.air_room_leak</c> when the opening cannot be named. Everyone
    /// else hears nothing: a leak behind a door is that room's business. Only when every room of the group is gone does
    /// everyone in the base's area hear <c>@base_air_lost</c> ("air only reaches the core zone"). Transition-triggered (the
    /// fill after the change), so it cannot spam; a leak told here counts as told for the walk-out diagnosis.
    /// </summary>
    private void WarnBaseAirChange(BaseAirVolume group, AirFill old, HashSet<Vector3i> oldDoorCells, int oldRooms)
    {
        bool total = group.Rooms == 0 && oldRooms > 0;
        int circ = _world.Circumference;
        foreach (var session in JoinedInActiveWorld())
        {
            var pos = session.State.Position;
            var feet = WorldConstants.CanonicalBlock(new Vector3i(
                (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y), (int)System.Math.Floor(pos.Z)), circ);
            bool inArea = InAirBoxes(group.Partners, group.Reaches, feet) || old.InExtent(feet, 1)
                || group.Committed?.InExtent(feet, 1) == true;
            if (!inArea)
            {
                continue;
            }

            if (total)
            {
                Send(session, new Networking.Messages.ServerMessage { Text = "@base_air_lost" });
            }

            if (LostAirCellAt(feet, old, oldDoorCells, group, circ) is not { } lost)
            {
                continue;
            }

            var leak = LocateBaseLeak(group, lost);
            if (leak.Pocket is not { Leaky: true } pocket)
            {
                continue; // the room is closed but no longer joined (its door went) — no hole to name
            }

            string text = pocket.Opening is { } opening
                ? BaseAirLeakText(session.Locale, "srv.base.air_leak_at", "srv.base.air_leak_open_at", opening, leak.Block, feet)
                : "@srv.base.air_room_leak";
            Send(session, new Networking.Messages.ServerMessage { Text = text });
            _baseAirTold[session.State.PlayerId] = _uptime;
        }
    }

    /// <summary>Fills the pocket at (or, for a doorway cell, beside) <paramref name="cell"/> once, on demand, with the group's
    /// sky line — outward from the cell, so a leak found is the one nearest to it — and reads the block at its opening.</summary>
    private (AirPocket? Pocket, AirFill? Fill, BlockId Block) LocateBaseLeak(BaseAirVolume group, Vector3i cell)
    {
        int circ = _world.Circumference;
        var space = NewPlanetAirSpace(group.SkyY);
        var origin = group.Partners.Count > 0 ? WorldConstants.CanonicalBlock(group.Partners[0].Cell, circ) : cell;
        var fill = new AirFill(space, AirLocateLimits, chainDoors: false, origin);
        var start = cell;
        if (space.Classify(cell.X, cell.Y, cell.Z) != AirCell.Open)
        {
            // In a doorway or a wall: judge the air beside it, the head first (a doorway is walked through upright).
            Vector3i[] around =
            {
                new(cell.X, cell.Y + 1, cell.Z), new(cell.X + 1, cell.Y, cell.Z), new(cell.X - 1, cell.Y, cell.Z),
                new(cell.X, cell.Y, cell.Z + 1), new(cell.X, cell.Y, cell.Z - 1),
            };
            bool found = false;
            foreach (var c in around)
            {
                if (space.Classify(c.X, c.Y, c.Z) == AirCell.Open)
                {
                    start = c;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return (null, null, BlockId.Air);
            }
        }

        fill.AddSeed(start);
        fill.Step(-1);
        if (fill.Braked || fill.Pockets.Count == 0)
        {
            return (null, fill, BlockId.Air);
        }

        var pocket = fill.Pockets[0];
        var block = pocket.Opening is { } o ? _world.GetBlockIfLoaded(o) : BlockId.Air;
        return (pocket, fill, block);
    }

    /// <summary>#2614: whether something a body would bump into stands above the canonical cell below the sky line — a
    /// ceiling, however leaky. Tree crowns do not count: standing under a tree outside is not standing in a room.</summary>
    private bool RoofAbove(Vector3i canonical, int skyY)
    {
        // The column's own sky line (review): over a room above the group's line, look up to its own highest build too.
        int top = AirColumnTop(_world.LocationId, canonical.X >> 4, canonical.Z >> 4);
        if (top != int.MinValue)
        {
            skyY = System.Math.Max(skyY, top + BaseAirSkyMargin);
        }

        for (int y = canonical.Y + 1; y < skyY; y++)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(canonical.X, y, canonical.Z)), fluidsPass: false, foliagePasses: true))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Called on every oxygen tick with whether the player breathes base air (#2542). When they just walked out of it
    /// near a base on a world without breathable air — and nothing else keeps them breathing — the pocket at their feet
    /// is diagnosed once, and a leak is told when the spot is clearly a room (#2614: the pocket touches a door, or there is
    /// a roof above the player) — with the block and the opening the air gets out through (#2654). A leak under the open
    /// sky stays silent (that is simply "outside"), the client's "life support lost" line already covers it. Told once per
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
        if (diag.Status != BaseAirStatus.Leak || !diag.LooksLikeRoom || diag.LeakCell is null
            || (_baseAirTold.TryGetValue(id, out double told) && _uptime - told < BaseAirRetellInterval))
        {
            return;
        }

        _baseAirTold[id] = _uptime;
        Send(session, new Networking.Messages.ServerMessage
        {
            Text = BaseAirLeakText(session.Locale, "srv.base.air_leak_at", "srv.base.air_leak_open_at", diag, cell),
        });
    }

    /// <summary><see cref="BaseAirLeakText(string,string,string,Vector3i,BlockId,Vector3i)"/> for a diagnosis.</summary>
    private string BaseAirLeakText(string locale, string blockKey, string openKey, BaseAirDiagnosis diag, Vector3i from)
        => BaseAirLeakText(locale, blockKey, openKey, diag.LeakCell ?? from, diag.LeakBlock, from);

    /// <summary>
    /// #2614/#2654: where a leaking room's air gets out, resolved on the server in the player's language (the block name and
    /// the height phrase are localized too, which one "@srv.*:arg" token cannot carry): <paramref name="blockKey"/> when the
    /// air escapes through a block that does not seal ("through Sand at 30/130/0, 10 blocks above you"),
    /// <paramref name="openKey"/> when the opening is plain air (a hole). The cell is shown in the frame of
    /// <paramref name="from"/> (the player's cell), so a leak across the wrap seam reads next to the player's own
    /// coordinates; the height is measured from that cell too.
    /// </summary>
    private string BaseAirLeakText(string locale, string blockKey, string openKey, Vector3i leak, BlockId leakBlock, Vector3i from)
    {
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

        string text = Localize(locale, leakBlock.IsAir ? openKey : blockKey);
        if (!leakBlock.IsAir)
        {
            var def = _content.BlockById(leakBlock);
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
    /// What the base air is at a cell (#2542, #2654): the core zone, supplied by a group (with that group's cells and
    /// rooms), or — for the first group whose area holds the cell — what the pocket around it is, by filling just that
    /// pocket outward from the cell: a leak (with its opening, #2614) or a closed room no door joins to the base
    /// (<see cref="BaseAirStatus.Unknown"/>). <see cref="BaseAirStatus.NoBase"/> when no base reaches the cell. On demand
    /// only (<c>/basewalls</c>, a player walking out of base air), held to <see cref="AirLocateLimits"/>.
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
            if (!InAirBoxes(group.Partners, canonical) && group.Committed?.InExtent(canonical, 1) != true)
            {
                continue;
            }

            int reach = group.Reaches.Length > 0 ? group.Reaches.Max() : SealedRoomMaxReach;
            if (group.Supplies(canonical))
            {
                return new BaseAirDiagnosis(BaseAirStatus.Supplied, group.Cells.Count, group.Rooms, reach);
            }

            reaching ??= group;
        }

        if (reaching is null)
        {
            return new BaseAirDiagnosis(BaseAirStatus.NoBase, 0, 0, 0);
        }

        int widest = reaching.Reaches.Length > 0 ? reaching.Reaches.Max() : SealedRoomMaxReach;
        var space = NewPlanetAirSpace(reaching.SkyY);
        if (space.Classify(canonical.X, canonical.Y, canonical.Z) != AirCell.Open)
        {
            return new BaseAirDiagnosis(BaseAirStatus.Leak, 0, 0, widest); // inside a wall or a doorway: no pocket of its own
        }

        var (pocket, _, block) = LocateBaseLeak(reaching, canonical);
        if (pocket is null || !pocket.Complete)
        {
            return new BaseAirDiagnosis(BaseAirStatus.Unknown, 0, 0, widest);
        }

        if (pocket.Sealed)
        {
            return new BaseAirDiagnosis(BaseAirStatus.Unknown, (int)System.Math.Min(int.MaxValue, pocket.Cells), 0, widest);
        }

        // #2614: a leak keeps where the air gets out, and whether this is clearly a room — a pocket bordering a door, or a
        // roof over the cell (sand counts: that is exactly the ceiling that leaks). One column, no extra fill.
        bool room = pocket.Doors is { Count: > 0 } || RoofAbove(canonical, reaching.SkyY);
        return new BaseAirDiagnosis(BaseAirStatus.Leak, (int)System.Math.Min(int.MaxValue, pocket.Cells), 0, widest,
            pocket.Opening, block, room);
    }

    /// <summary>Test seam (#2542): the diagnosis at a cell of the active world.</summary>
    internal BaseAirDiagnosis DiagnoseBaseAirForTest(int x, int y, int z) => DiagnoseBaseAir(new Vector3i(x, y, z));

    /// <summary>Drops a removed base's cached air (called when its core is mined), with every group it was part of.</summary>
    private void ForgetBaseAir(int baseId)
    {
        _baseAirOf.Remove(baseId);
        _baseAirTop.Remove(baseId);
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
            DropBaseAirGroup(key);
        }
    }

    /// <summary>Drops one group and every base that resolved to it.</summary>
    private void DropBaseAirGroup(string key)
    {
        var group = _baseAirGroups[key];
        _baseAirGroups.Remove(key);
        foreach (int id in _baseAirOf.Where(kv => kv.Value == group).Select(kv => kv.Key).ToList())
        {
            _baseAirOf.Remove(id);
        }
    }

    /// <summary>Drops the groups nobody asked about for <see cref="BaseAirIdleDropSeconds"/> (a body left behind) — their
    /// fills hold spans in memory; the next query simply fills again.</summary>
    private void DropIdleBaseAir()
    {
        List<string>? idle = null;
        foreach (var kv in _baseAirGroups)
        {
            if (_uptime - kv.Value.LastUsed > BaseAirIdleDropSeconds)
            {
                (idle ??= new List<string>()).Add(kv.Key);
            }
        }

        if (idle is null)
        {
            return;
        }

        foreach (string key in idle)
        {
            DropBaseAirGroup(key);
        }
    }
}
