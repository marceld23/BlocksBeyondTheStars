// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The school club's landmarks (terrain generation 39, <see cref="WorldDescription.SchoolClubLandmarksGeneration"/>) — what the
/// stamper of GameServerLandmarks.cs does on such a world beside the classic roll:
/// <list type="bullet">
/// <item><b>Fixed landmarks</b> (<see cref="PlanetType.FixedLandmarks"/>, Soaniri: the castle and the singer statue): every body
/// of the type carries each of them, always, instead of a rolled one — each its own placement record (the index is its position
/// in the type's list), seed, turn and seat; the second keeps clear of the first through the footprint registry like of every
/// structure stamped before it.</item>
/// <item><b>The span seat</b> (<see cref="LandmarkRules.SeatSpan"/>, a bridge): an unbroken gap of water or a valley at least
/// <see cref="LandmarkRules.SpanGapDepth"/> below the deck row (the template's ground row), at least
/// <see cref="LandmarkRules.SpanGapLength"/> long with its middle in the middle third of the long axis, both ends on dry banks
/// within <see cref="LandmarkRules.SpanBankTolerance"/> of the deck row; both orientations are tried. No such spot → no seat,
/// and the body takes another candidate.</item>
/// </list>
/// Older worlds take neither (their stamps stay exactly as they were).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many spots the span search tries per orientation before it gives up.</summary>
    private const int SpanSeatAttempts = 2000;

    /// <summary>Whether the active world takes the school club's landmark rules: fixed landmarks, the span seat, protection from
    /// the template (terrain generation 39).</summary>
    private bool SchoolClubLandmarkWorld => _generator.TerrainGeneration >= WorldDescription.SchoolClubLandmarksGeneration;

    // ---------------------------------------------------------------- fixed landmarks

    /// <summary>Stamps every fixed landmark of the body's type (none recorded yet): on a body untouched at load each is planned,
    /// seated and committed in list order; one without a seat — or every one on a body visited before — records a skip.</summary>
    private void StampFixedLandmarks(IReadOnlyList<(int Index, StructureTemplate Template)> fixedOnes)
    {
        foreach (var (index, template) in fixedOnes)
        {
            var plan = _worlds.Active.VirginAtLoad ? PlanFixedLandmark(template, index) : null;
            if (plan is null)
            {
                RecordPlacementSkip(LandmarkRules.RecordKind, index); // decided once: this one has no place here
                SavePlacementRecords();
                continue;
            }

            CommitLandmark(plan);
        }
    }

    /// <summary>The fixed landmarks of a body that already carries records but none for some of its fixed indices: a crash
    /// cut the boot stamp short between two of them (or a content update added one to the type). Each missing one is planned
    /// now — its seat keeps clear of every player-built cell, since the body may have been played on — and committed, or a
    /// skip is recorded. A body whose fixed records are all there is left exactly as it was.</summary>
    private void StampMissingFixedLandmarks(IReadOnlyList<(int Index, StructureTemplate Template)> fixedOnes, List<StructurePlacementRecord> records)
    {
        foreach (var (index, template) in fixedOnes)
        {
            if (records.Exists(r => r.Index == index))
            {
                continue;
            }

            if (PlanFixedLandmark(template, index, avoidPlayerEdits: true) is { } plan)
            {
                CommitLandmark(plan);
                continue;
            }

            RecordPlacementSkip(LandmarkRules.RecordKind, index);
            SavePlacementRecords();
        }
    }

    /// <summary>A fixed landmark's plan: its seed and turn hashed from its key and the body, its seat — null (reported) when
    /// it does not fit the body, its source cannot build it or no seat is found.</summary>
    private LandmarkPlan? PlanFixedLandmark(StructureTemplate template, int index, bool avoidPlayerEdits = false)
    {
        string lane = "landmark:fixed:" + template.Key + ":" + _world.LocationId;
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = unchecked((long)StableHashing.Mixed(lane, _meta.Seed)),
            Turns = template.IsSource ? 0 : StableHashing.Index(lane + ":turn", _meta.Seed, 4),
            Index = index,
            Fresh = true,
        };
        if (template.IsSource || LandmarkFitsBody(template, _world.Circumference))
        {
            plan.Layout = BuildLandmarkLayout(plan);
        }

        if (plan.Layout is null || !TrySeatLandmark(plan, avoidPlayerEdits))
        {
            _log.Warn($"The fixed landmark '{template.Key}' (record {index.ToString(CultureInfo.InvariantCulture)}) found no place on '{_world.LocationId}'.");
            ReportStamp(LandmarkRules.RecordKind, 1, 0);
            return null;
        }

        return plan;
    }

    // ---------------------------------------------------------------- the span seat

    /// <summary>Seats a span landmark: the plan's turn first, then a quarter turn more (the long axis the other way). The plan
    /// takes the turn and the layout that found a seat; false when neither does.</summary>
    private bool TrySeatLandmarkSpan(LandmarkPlan plan)
    {
        var reserved = KeepClearFor(StructureTypeRules.Landmark, LandmarkReserved());
        for (int o = 0; o < 2; o++)
        {
            int turns = (plan.Turns + o) & 3;
            var layout = o == 0 ? plan.Layout
                : plan.Template.IsSource ? null // an entry stands as built: one orientation
                : LandmarkGenerator.Build(plan.Template, plan.Seed, turns, _content, LandmarkSurfaceBlock(), ruin: null, plan.Revision);
            if (layout is null)
            {
                continue;
            }

            var s = layout.Structure;
            bool alongX = s.Width > s.Length || (s.Width == s.Length && o == 0);
            if (FindSpanSeat(s.Width, s.Length, alongX, RngFor(plan.Seed, "span:" + o.ToString(CultureInfo.InvariantCulture)), reserved,
                    out int ox, out int oz, out int groundY))
            {
                plan.Turns = turns;
                plan.Layout = layout;
                (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (ox, oz, groundY, LandmarkRules.SeatSpan);
                return true;
            }
        }

        return false;
    }

    /// <summary>The span search of one orientation: spots drawn around pad 0 like the guaranteed search's rings, clear of every
    /// reserved box, the first one that <see cref="SpanFits"/> wins. Out: the footprint's origin corner and the deck row's world
    /// Y (the bank level).</summary>
    private bool FindSpanSeat(int w, int l, bool alongX, System.Random rng, List<(int Cx, int Cz, int Hw, int Hl)> reserved,
        out int originX, out int originZ, out int groundY)
    {
        (originX, originZ, groundY) = (0, 0, 0);
        var planet = _world.Planet;
        int circ = _world.Circumference;
        int latBand = System.Math.Max(8, (WorldConstants.LatitudePeriodFor(circ) / 2) - (System.Math.Max(w, l) / 2) - 16);
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        int maxDist = System.Math.Max(80, System.Math.Min(circ / 2, (int)(circ * 0.8)));
        for (int attempt = 0; attempt < SpanSeatAttempts; attempt++)
        {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            int dist = 40 + rng.Next(0, maxDist);
            int cx = WorldConstants.WrapX(pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist), circ);
            int cz = System.Math.Clamp(pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist), -latBand, latBand);
            if (OverlapsFootprint(cx, cz, (w / 2) + 1, (l / 2) + 1, reserved, SettlementCollisionMargin))
            {
                continue;
            }

            int ox = cx - (w / 2), oz = cz - (l / 2);
            if (SpanFits(planet, ox, oz, w, l, alongX, out int gy))
            {
                (originX, originZ, groundY) = (ox, oz, gy);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a span stands with its footprint at (<paramref name="ox"/>, <paramref name="oz"/>), looked at on three lines along
    /// the long axis (the middle and both edges): the outer eighth at each end is dry ground — no water, ice or lava — within
    /// <see cref="LandmarkRules.SpanBankTolerance"/> of the deck row (the mean of the two banks); between the ends the ground
    /// never rises higher than that above it and nothing is lava; and one unbroken GAP — water whose surface lies at least two
    /// below the deck row, or ground at least <see cref="LandmarkRules.SpanGapDepth"/> below it, on all three lines — runs for
    /// at least <see cref="LandmarkRules.SpanGapLength"/> blocks with its middle in the middle third. The pylons may stand in
    /// the water (their piers go down as piles). Out: the deck row's world Y.
    /// </summary>
    private bool SpanFits(PlanetType planet, int ox, int oz, int w, int l, bool alongX, out int groundY)
    {
        int n = alongX ? w : l, m = alongX ? l : w;
        (int X, int Z) At(int i, int a) => alongX ? (ox + i, oz + a) : (ox + a, oz + i);

        // The quick test first: the two outermost columns of the middle line set the deck row, and the middle third of the
        // middle line must hold a gap somewhere (five samples a quarter of the third apart — the gap is longer than that).
        var (ax, az) = At(0, m / 2);
        var (bx, bz) = At(n - 1, m / 2);
        int deckY = (_generator.SurfaceHeight(planet, ax, az) + _generator.SurfaceHeight(planet, bx, bz) + 1) / 2;
        groundY = deckY;
        bool any = false;
        for (int k = 0; k <= 4 && !any; k++)
        {
            var (x, z) = At((n / 3) + (k * n / 12), m / 2);
            any = SpanGapAt(planet, x, z, deckY);
        }

        int[] lines = m >= 5 ? new[] { 1, m / 2, m - 2 } : new[] { m / 2 };
        return any && SpanBanksHold(planet, n, lines, At, deckY) && SpanGapRunHolds(planet, n, lines, At, deckY);
    }

    /// <summary>The span's ends: on every line every column of the outer eighth at each end is a bank
    /// (<see cref="SpanBankAt"/>), checked before the long middle because a wrong spot mostly fails here.</summary>
    private bool SpanBanksHold(PlanetType planet, int n, int[] lines, System.Func<int, int, (int X, int Z)> at, int deckY)
    {
        int end = System.Math.Max(2, n / 8);
        foreach (int a in lines)
        {
            for (int i = 0; i < end; i++)
            {
                var (x0, z0) = at(i, a);
                var (x1, z1) = at(n - 1 - i, a);
                if (!SpanBankAt(planet, x0, z0, deckY) || !SpanBankAt(planet, x1, z1, deckY))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Between the ends (every second column): no lava, no ground higher than the bank tolerance above the deck row,
    /// and a run of columns that are a gap on every line, at least <see cref="LandmarkRules.SpanGapLength"/> long with its
    /// middle in the middle third.</summary>
    private bool SpanGapRunHolds(PlanetType planet, int n, int[] lines, System.Func<int, int, (int X, int Z)> at, int deckY)
    {
        int end = System.Math.Max(2, n / 8), need = LandmarkRules.SpanGapLength(n), runStart = -1, i = end;
        for (; i < n - end; i += 2)
        {
            bool gap = true;
            foreach (int a in lines)
            {
                var (x, z) = at(i, a);
                if (_generator.IsSurfaceLava(planet, x, z) || _generator.SurfaceHeight(planet, x, z) > deckY + LandmarkRules.SpanBankTolerance)
                {
                    return false;
                }

                gap = gap && SpanGapAt(planet, x, z, deckY);
            }

            if (gap)
            {
                runStart = runStart < 0 ? i : runStart;
            }
            else if (runStart >= 0)
            {
                if (SpanGapRunCounts(runStart, i, n, need))
                {
                    return true;
                }

                runStart = -1;
            }
        }

        return runStart >= 0 && SpanGapRunCounts(runStart, i, n, need);
    }

    /// <summary>Whether the gap run from column <paramref name="start"/> up to (not including) <paramref name="stop"/> is long
    /// enough and has its middle in the middle third of the long axis.</summary>
    private static bool SpanGapRunCounts(int start, int stop, int n, int need)
    {
        int mid = (start + stop) / 2;
        return stop - start >= need && mid >= n / 3 && mid < (2 * n) / 3;
    }

    /// <summary>Test seam: whether a stamped landmark's footprint still passes the span test along its long axis, its deck row
    /// at the recorded ground.</summary>
    internal bool SpanSeatHoldsForTest(LandmarkInstance lm)
    {
        var s = lm.Structure;
        return SpanFits(_world.Planet, lm.Origin.X, lm.Origin.Z, s.Width, s.Length, s.Width >= s.Length, out int deckY) && deckY == lm.GroundY;
    }

    // ---------------------------------------------------------------- writing a span

    /// <summary>Whether a span's column stands over its gap (the natural ground more than the bank tolerance below the deck
    /// row, or water) — no plinth is carried down there.</summary>
    private bool SpanGapUnder(LandmarkInstance lm, int x, int z)
    {
        int wx = lm.Origin.X + x, wz = lm.Origin.Z + z;
        return _generator.IsSurfaceWater(_world.Planet, wx, wz)
               || _generator.SurfaceHeight(_world.Planet, wx, wz) < lm.GroundY - LandmarkRules.SpanBankTolerance;
    }

    /// <summary>A span's piers (template blocks under the deck row that reach its bottom row) carried down to the bed or the
    /// valley floor like a pile, column by column (a pier is no wider than a few blocks, so every column of its foot goes
    /// down — a pier on every fourth column would miss a slim one), at most <see cref="LandmarkPileDepth"/> deep. Returns the
    /// cells written.</summary>
    private int WriteLandmarkSpanPiles(IStructureCellSink sink, LandmarkInstance lm)
    {
        var s = lm.Structure;
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int z = 0; z < s.Length; z++)
            {
                if (s.Get(x, 0, z) == 0)
                {
                    continue; // no pier reaches the template's bottom row here
                }

                var foot = new Shared.Primitives.BlockId(s.Get(x, 0, z));
                int wx = lm.Origin.X + x, wz = lm.Origin.Z + z;
                int floorY = System.Math.Max(_generator.SurfaceHeight(_world.Planet, wx, wz) + 1, lm.Origin.Y - LandmarkPileDepth);
                for (int y = lm.Origin.Y - 1; y >= floorY; y--)
                {
                    sink.Put(wx, y, wz, foot);
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>A bank column: dry (no water, no sea ice) and within the tolerance of the deck row.</summary>
    private bool SpanBankAt(PlanetType planet, int x, int z, int deckY)
        => !_generator.IsSurfaceWater(planet, x, z)
           && !(SeaIceIsWet && _generator.SurfaceIceThickness(planet, x, z) > 0)
           && System.Math.Abs(_generator.SurfaceHeight(planet, x, z) - deckY) <= LandmarkRules.SpanBankTolerance;

    /// <summary>A gap column: water (or sea ice) whose surface lies at least two blocks below the deck row, or dry ground at
    /// least <see cref="LandmarkRules.SpanGapDepth"/> below it — never lava.</summary>
    private bool SpanGapAt(PlanetType planet, int x, int z, int deckY)
    {
        if (_generator.IsSurfaceLava(planet, x, z))
        {
            return false;
        }

        if (SeaIceIsWet && _generator.TryGetIceSurface(planet, x, z, out int iceTop))
        {
            return iceTop <= deckY - 2;
        }

        if (_generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _))
        {
            return waterTop <= deckY - 2;
        }

        return _generator.SurfaceHeight(planet, x, z) <= deckY - LandmarkRules.SpanGapDepth;
    }
}
