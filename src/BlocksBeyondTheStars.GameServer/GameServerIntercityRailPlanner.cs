// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity line's planners (#3984; the main file is GameServerIntercityRail.cs). The <b>rolled</b> planner decides
/// whether a world gets a line and between which two towns, and lays out its columns; the <b>shared core</b> — the profile
/// (pylon heights), the pylon and cut checks, the player-edit check and the pylon list — is the same for every planner, the
/// given route's (GameServerIntercityRailGivenRoute.cs) included. Pure over the generator's queries — no chunk is read.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The columns of a planned line before their heights: positions in line order (X unwrapped along the line),
    /// and per column the stop whose hall holds it (−1 for a free pylon) with that stop's floor row.</summary>
    private sealed class IntercityColumns
    {
        public List<(double X, double Z)> Cols { get; } = new();
        public List<int> StationOf { get; } = new();
        public List<int> FloorOf { get; } = new();

        public int Count => Cols.Count;

        public void Add(double x, double z, int station = -1, int floorY = 0)
        {
            Cols.Add((x, z));
            StationOf.Add(station);
            FloorOf.Add(floorY);
        }

        /// <summary>A station's own pylon: its height is the station's floor.</summary>
        public bool IsFixed(int j) => StationOf[j] >= 0;

        /// <summary>The span from column <paramref name="j"/> to the next runs inside one station's hall.</summary>
        public bool IsStationSpan(int j) => StationOf[j] >= 0 && StationOf[j] == StationOf[j + 1];
    }

    // =====================================================================================================
    // The rolled planner
    // =====================================================================================================

    /// <summary>The fresh decision: at least two eligible towns, the chance on the line's own lane, then the closest pair
    /// whose route fits under the rolled limits. Null when there is no line.</summary>
    private IntercityPlan? DecideIntercityRail(PlanetType planet)
    {
        if (!StructureConditionsHold(StructureTypeRules.Intercity, planet) || planet.RestrictStructures)
        {
            return null; // islands only / one composed city (the kind's shipped conditions, #3517) / a structure whitelist
        }

        var eligible = new List<SettlementInstance>();
        foreach (var s in _settlements)
        {
            if (IntercityEligible(s))
            {
                eligible.Add(s);
            }
        }

        if (eligible.Count < 2)
        {
            return null;
        }

        if (IntercityRoll(_meta.Seed, _world.LocationId) >= StructureChance(StructureTypeRules.Intercity, _config.IntercityRailChance))
        {
            return null;
        }

        int circ = _world.Circumference;
        var pairs = new List<(double D, int I, int J)>();
        for (int i = 0; i < eligible.Count; i++)
        {
            for (int j = i + 1; j < eligible.Count; j++)
            {
                var (ax, az) = CentreOf(eligible[i]);
                var (bx, bz) = CentreOf(eligible[j]);
                double dx = WorldConstants.WrapDeltaX(bx - ax, circ), dz = bz - az;
                pairs.Add((Math.Sqrt(dx * dx + dz * dz), i, j));
            }
        }

        pairs.Sort((p, q) => p.D != q.D ? p.D.CompareTo(q.D) : p.I != q.I ? p.I.CompareTo(q.I) : p.J.CompareTo(q.J));
        var limits = IntercityLimits.Rolled(_generator.TerrainGeneration);
        foreach (var (_, i, j) in pairs.Take(IntercityPairTries))
        {
            if (TryPlanIntercity(planet, eligible[i], eligible[j], limits, out var plan, out string why))
            {
                return plan;
            }

            _log.Info($"Intercity line on '{_world.LocationId}': {eligible[i].Name} ↔ {eligible[j].Name} does not fit ({why}).");
        }

        return null;
    }

    private static (double X, double Z) CentreOf(SettlementInstance s) => ((s.Min.X + s.Max.X) * 0.5, (s.Min.Z + s.Max.Z) * 0.5);

    /// <summary>Plans a line between two towns (see the class summary of GameServerIntercityRail.cs): the stations, the
    /// columns, then the shared core under <paramref name="limits"/>.</summary>
    private bool TryPlanIntercity(PlanetType planet, SettlementInstance a, SettlementInstance b, IntercityLimits limits,
        out IntercityPlan plan, out string why)
    {
        plan = new IntercityPlan();
        int circ = _world.Circumference;
        var (acx, acz) = CentreOf(a);
        var (bcx, bcz) = CentreOf(b);
        double ddx = WorldConstants.WrapDeltaX(bcx - acx, circ), ddz = bcz - acz;
        int headA = Math.Abs(ddx) >= Math.Abs(ddz) ? (ddx >= 0 ? 0 : 1) : (ddz >= 0 ? 2 : 3);
        int headB = headA ^ 1; // the opposite heading (0↔1, 2↔3)

        var sa = StationFor(a, headA, acx + ddx, bcz);
        var sb = StationFor(b, headB, acx, acz);
        if (!RolledStationsFit(planet, a, b, sa, sb, out var notA, out var notB, out var everySettlement, out why))
        {
            return false;
        }

        var c = RolledColumns(sa, sb, limits, plan, out why);
        if (c is null || !IntercityLatitudeFits(c, out why) || !RolledSpansClear(c, everySettlement, notA, notB, out why))
        {
            return false;
        }

        plan.Stops.Add(sa);
        plan.Stops.Add(sb);
        return TryFinishIntercityPlan(planet, c, limits, plan, out why);
    }

    /// <summary>The rolled stations: clear of every other settlement, the pads, the wreck site and each other; never in lava;
    /// seated; not where somebody built. Out: the boxes the spans keep clear of — every settlement, and every one but the
    /// lead's own town.</summary>
    private bool RolledStationsFit(PlanetType planet, SettlementInstance a, SettlementInstance b, IntercityStation sa, IntercityStation sb,
        out List<(int Cx, int Cz, int Hw, int Hl)> notA, out List<(int Cx, int Cz, int Hw, int Hl)> notB,
        out List<(int Cx, int Cz, int Hw, int Hl)> everySettlement, out string why)
    {
        var others = IntercityKeepClear();
        everySettlement = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
        notA = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
        notB = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
        foreach (var s in _settlements)
        {
            var r = ((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2, (s.Max.X - s.Min.X) / 2 + 1, (s.Max.Z - s.Min.Z) / 2 + 1);
            everySettlement.Add(r);
            if (!ReferenceEquals(s, a))
            {
                notA.Add(r);
            }

            if (!ReferenceEquals(s, b))
            {
                notB.Add(r);
            }
        }

        foreach (var (st, rects) in new[] { (sa, notA), (sb, notB) })
        {
            var (cx, cz, hw, hl) = st.Rect;
            if (OverlapsFootprint(cx, cz, hw, hl, rects, 2))
            {
                why = "a station would stand on another structure";
                return false;
            }

            if (!IntercityStationSeats(planet, st, out why))
            {
                return false;
            }
        }

        var (acx, acz, ahw, ahl) = sa.Rect;
        if (OverlapsFootprint(acx, acz, ahw, ahl, new List<(int, int, int, int)> { sb.Rect }, 2))
        {
            why = "the stations would crowd each other";
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>What a line keeps clear of besides the towns: the pads and the wreck site — on generation 38 (#3514) every
    /// registered box but the towns (the crash site as large as its hull), as registered: the line keeps its own tight
    /// margins between the towns it joins.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> IntercityKeepClear()
    {
        if (UsesFootprintRegistry)
        {
            return Footprints.Reserved((_, _) => 0, 0, exceptKind: StructureTypeRules.Settlement);
        }

        var others = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            others.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        others.Add((wreckX, wreckZ, WreckReservedHalfExtent, WreckReservedHalfExtent));
        return others;
    }

    /// <summary>A station's seat (never in lava) and — on a body that was played on — no build where it would stand.</summary>
    private bool IntercityStationSeats(PlanetType planet, IntercityStation st, out string why)
    {
        if (!SeatIntercityStation(planet, st))
        {
            why = "a station would stand in lava";
            return false;
        }

        if (!_worlds.Active.VirginAtLoad && FootprintHasPlayerEdits(st.Origin.X, st.Origin.Z, st.Origin.Y, st.W, RailStationGenerator.Height, st.L))
        {
            why = "somebody built where a station would stand";
            return false;
        }

        why = string.Empty;
        return true;
    }

    /// <summary>The rolled line's columns: A's end + exit, A's lead, the free route, B's lead, B's exit + end — unwrapped
    /// relative to A's end. Null when the free route (lead to lead) is longer than the limit or the towns are too close.</summary>
    private IntercityColumns? RolledColumns(IntercityStation sa, IntercityStation sb, IntercityLimits limits, IntercityPlan plan, out string why)
    {
        int circ = _world.Circumference;
        var (hax, haz) = RailStationGenerator.HeadingVector(sa.Heading);
        var (hbx, hbz) = RailStationGenerator.HeadingVector(sb.Heading);
        double x0 = sa.EndPylon.X;
        double U(double x) => x0 + WorldConstants.WrapDeltaX(x - x0, circ);
        var c = new IntercityColumns();
        c.Add(U(sa.EndPylon.X), sa.EndPylon.Z, 0, sa.Origin.Y);
        c.Add(U(sa.ExitPylon.X), sa.ExitPylon.Z, 0, sa.Origin.Y);
        var leadA = (X: U(sa.ExitPylon.X) + hax * IntercityLead, Z: (double)sa.ExitPylon.Z + haz * IntercityLead);
        var leadB = (X: U(sb.ExitPylon.X) + hbx * IntercityLead, Z: (double)sb.ExitPylon.Z + hbz * IntercityLead);
        double rdx = leadB.X - leadA.X, rdz = leadB.Z - leadA.Z;
        double route = Math.Sqrt(rdx * rdx + rdz * rdz);
        plan.RouteLength = (float)route;
        if (route > limits.MaxRoute)
        {
            why = $"the route is {route:F0} blocks long";
            return null;
        }

        if (route < IntercityMinRoute || rdx * hax + rdz * haz <= 0 || -(rdx * hbx + rdz * hbz) <= 0)
        {
            why = "the towns are too close";
            return null;
        }

        int n = Math.Max(1, (int)Math.Ceiling(route / IntercityPylonSpacing));
        for (int k = 0; k <= n; k++)
        {
            double t = k / (double)n;
            c.Add(Math.Round(leadA.X + rdx * t), Math.Round(leadA.Z + rdz * t));
        }

        c.Add(U(sb.ExitPylon.X), sb.ExitPylon.Z, 1, sb.Origin.Y);
        c.Add(U(sb.EndPylon.X), sb.EndPylon.Z, 1, sb.Origin.Y);
        why = string.Empty;
        return c;
    }

    /// <summary>The rolled spans outside the stations keep clear of every settlement, pad and the wreck site; the leads leave
    /// their own station, so they only have to keep clear of everything but their own town.</summary>
    private bool RolledSpansClear(IntercityColumns c, List<(int Cx, int Cz, int Hw, int Hl)> everySettlement,
        List<(int Cx, int Cz, int Hw, int Hl)> notA, List<(int Cx, int Cz, int Hw, int Hl)> notB, out string why)
    {
        int m = c.Count;
        for (int j = 1; j < m - 2; j++)
        {
            var r = IntercitySpanRect(c, j);
            if (j > 1 && j < m - 3 && OverlapsFootprint(r.Cx, r.Cz, r.Hw, r.Hl, everySettlement, 1))
            {
                why = "the route would cross another structure";
                return false;
            }

            if ((j == 1 && OverlapsFootprint(r.Cx, r.Cz, r.Hw, r.Hl, notA, 1))
                || (j == m - 3 && OverlapsFootprint(r.Cx, r.Cz, r.Hw, r.Hl, notB, 1)))
            {
                why = "a lead would cross another structure";
                return false;
            }
        }

        why = string.Empty;
        return true;
    }

    // =====================================================================================================
    // The shared core
    // =====================================================================================================

    /// <summary>The box around a span (its columns' rectangle, three blocks wider on each side).</summary>
    private static (int Cx, int Cz, int Hw, int Hl) IntercitySpanRect(IntercityColumns c, int j)
    {
        var (p, q) = (c.Cols[j], c.Cols[j + 1]);
        return ((int)Math.Round((p.X + q.X) * 0.5), (int)Math.Round((p.Z + q.Z) * 0.5),
            (int)Math.Ceiling(Math.Abs(q.X - p.X) * 0.5) + 3, (int)Math.Ceiling(Math.Abs(q.Z - p.Z) * 0.5) + 3);
    }

    /// <summary>No column may cross the latitude seam.</summary>
    private bool IntercityLatitudeFits(IntercityColumns c, out string why)
    {
        int latLimit = WorldConstants.LatitudePeriodFor(_world.Circumference) / 2 - 8;
        why = c.Cols.Any(col => Math.Abs(col.Z) > latLimit) ? "the line would cross the latitude seam" : string.Empty;
        return why.Length == 0;
    }

    /// <summary>The shared core after a planner laid out its columns: the profile, the pylon feet, the cut, the player
    /// edits on the route; then the pylon tops in line order. The plan's stops must be set.</summary>
    private bool TryFinishIntercityPlan(PlanetType planet, IntercityColumns c, IntercityLimits limits, IntercityPlan plan, out string why)
    {
        var tops = IntercityProfile(planet, c);
        if (!IntercityPylonsFit(planet, c, tops, limits, out why))
        {
            return false;
        }

        // The cut: terrain the corridor would run through outside the stations (the grade from a station meets a hill).
        int cut = IntercityCut(planet, c, tops);
        plan.CutCells = cut;
        if (cut > limits.MaxCutCells)
        {
            why = $"the route would cut {cut} blocks of terrain";
            return false;
        }

        if (!_worlds.Active.VirginAtLoad && IntercityRouteHasPlayerEdits(c, tops))
        {
            why = "somebody built on the route";
            return false;
        }

        int circ = _world.Circumference;
        for (int j = 0; j < c.Count; j++)
        {
            plan.Pylons.Add(WorldConstants.CanonicalBlock(new Vector3i((int)Math.Floor(c.Cols[j].X), tops[j], (int)Math.Floor(c.Cols[j].Z)), circ));
        }

        why = string.Empty;
        return true;
    }

    /// <summary>The pylon tops: a free pylon stands <see cref="IntercityClearance"/> over the highest ground of its two spans,
    /// held under a gentle grade from the nearest station on each side, then raised where a valley would make the line
    /// steeper than the grade; a station's own pylons stand at its floor.</summary>
    private int[] IntercityProfile(PlanetType planet, IntercityColumns c)
    {
        int m = c.Count;
        var spanMax = IntercitySpanGround(planet, c);
        var dist = new double[m];
        for (int j = 1; j < m; j++)
        {
            var (p, q) = (c.Cols[j - 1], c.Cols[j]);
            dist[j] = dist[j - 1] + Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
        }

        var y = new double[m];
        for (int j = 0; j < m; j++)
        {
            y[j] = c.IsFixed(j) ? c.FloorOf[j] : 0.0;
        }

        for (int j = 0; j < m; j++)
        {
            if (!c.IsFixed(j))
            {
                int p = j - 1, n = j + 1;
                while (p > 0 && !c.IsFixed(p)) p--;
                while (n < m - 1 && !c.IsFixed(n)) n++;
                double desired = Math.Max(spanMax[j - 1], spanMax[j]) + IntercityClearance;
                double cone = Math.Min(y[p] + IntercityGrade * (dist[j] - dist[p]), y[n] + IntercityGrade * (dist[n] - dist[j]));
                y[j] = Math.Min(desired, cone);
            }
        }

        for (int j = 1; j < m; j++)
        {
            y[j] = c.IsFixed(j) ? y[j] : Math.Max(y[j], y[j - 1] - IntercityGrade * (dist[j] - dist[j - 1]));
        }

        for (int j = m - 2; j >= 0; j--)
        {
            y[j] = c.IsFixed(j) ? y[j] : Math.Max(y[j], y[j + 1] - IntercityGrade * (dist[j + 1] - dist[j]));
        }

        var tops = new int[m];
        for (int j = 0; j < m; j++)
        {
            tops[j] = (int)Math.Ceiling(y[j] - 1e-3);
        }

        return tops;
    }

    /// <summary>The ground per span outside the stations: the highest of three lines along it (water at its surface with the
    /// ice on top); <see cref="int.MinValue"/> inside a station (the station's own seat carves it).</summary>
    private int[] IntercitySpanGround(PlanetType planet, IntercityColumns c)
    {
        int m = c.Count;
        var spanMax = new int[m - 1];
        for (int j = 0; j < m - 1; j++)
        {
            spanMax[j] = int.MinValue;
            if (c.IsStationSpan(j))
            {
                continue;
            }

            var (p, q) = (c.Cols[j], c.Cols[j + 1]);
            double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
            double rx = len < 1e-6 ? 1 : (q.Z - p.Z) / len, rz = len < 1e-6 ? 0 : -(q.X - p.X) / len;
            int steps = Math.Max(1, (int)Math.Ceiling(len));
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                for (int side = -1; side <= 1; side++)
                {
                    int gx = (int)Math.Floor(p.X + (q.X - p.X) * t + 0.5 + rx * side * 1.5);
                    int gz = (int)Math.Floor(p.Z + (q.Z - p.Z) * t + 0.5 + rz * side * 1.5);
                    spanMax[j] = Math.Max(spanMax[j], IntercityGroundAt(planet, gx, gz));
                }
            }
        }

        return spanMax;
    }

    /// <summary>Pylon feet: never in lava, never taller than the limit.</summary>
    private bool IntercityPylonsFit(PlanetType planet, IntercityColumns c, int[] tops, IntercityLimits limits, out string why)
    {
        for (int j = 0; j < c.Count; j++)
        {
            if (c.IsFixed(j))
            {
                continue;
            }

            int px = (int)Math.Floor(c.Cols[j].X), pz = (int)Math.Floor(c.Cols[j].Z);
            if (_generator.IsSurfaceLava(planet, px, pz))
            {
                why = "a pylon would stand in lava";
                return false;
            }

            int foot = PylonFoot(planet, px, pz);
            if (tops[j] - foot + 1 > limits.MaxPylon)
            {
                why = $"a pylon would be {tops[j] - foot + 1} blocks tall";
                return false;
            }
        }

        why = string.Empty;
        return true;
    }

    /// <summary>The terrain cells the wagon's floor band would run through on the spans outside the stations.</summary>
    private int IntercityCut(PlanetType planet, IntercityColumns c, int[] tops)
    {
        int cut = 0;
        for (int j = 0; j < c.Count - 1; j++)
        {
            if (c.IsStationSpan(j))
            {
                continue;
            }

            var (p, q) = (c.Cols[j], c.Cols[j + 1]);
            double len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
            double rx = len < 1e-6 ? 1 : (q.Z - p.Z) / len, rz = len < 1e-6 ? 0 : -(q.X - p.X) / len;
            int steps = Math.Max(1, (int)Math.Ceiling(len));
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                int bottom = (int)Math.Floor(tops[j] + (tops[j + 1] - tops[j]) * t + RailRules.LineRise + RailRules.HoverHeight);
                for (int side = -1; side <= 1; side++)
                {
                    int gx = (int)Math.Floor(p.X + (q.X - p.X) * t + 0.5 + rx * side);
                    int gz = (int)Math.Floor(p.Z + (q.Z - p.Z) * t + 0.5 + rz * side);
                    int g = _generator.SurfaceHeight(planet, gx, gz);
                    cut += Math.Clamp(g - bottom + 1, 0, 4);
                }
            }
        }

        return cut;
    }

    /// <summary>Whether somebody built in the box of a span outside the stations, from four under to six over its pylons.</summary>
    private bool IntercityRouteHasPlayerEdits(IntercityColumns c, int[] tops)
    {
        for (int j = 0; j < c.Count - 1; j++)
        {
            if (c.IsStationSpan(j))
            {
                continue;
            }

            var r = IntercitySpanRect(c, j);
            int lo = Math.Min(tops[j], tops[j + 1]) - 4;
            int hi = Math.Max(tops[j], tops[j + 1]) + 6;
            if (FootprintHasPlayerEdits(r.Cx - r.Hw, r.Cz - r.Hl, lo, r.Hw * 2, hi - lo, r.Hl * 2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ground the line must clear at a column: the terrain, or a water body's surface with its ice on top.</summary>
    private int IntercityGroundAt(PlanetType planet, int x, int z)
    {
        int g = _generator.SurfaceHeight(planet, x, z);
        if (_generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _))
        {
            g = Math.Max(g, waterTop + _generator.SurfaceIceThickness(planet, x, z));
        }
        else if (_generator.IsSurfaceLava(planet, x, z))
        {
            g += 3; // the glow of a lava lake keeps its distance
        }

        return g;
    }

    /// <summary>The first cell of a pylon column: over the ground, or over the bed of the water it stands in.</summary>
    private int PylonFoot(PlanetType planet, int x, int z)
        => _generator.TryGetWaterSurface(planet, x, z, out _, out int seabed) ? seabed + 1 : _generator.SurfaceHeight(planet, x, z) + 1;

    // =====================================================================================================
    // Stations
    // =====================================================================================================

    /// <summary>A station at a town's edge on the side of <paramref name="heading"/>, slid along that edge toward the partner
    /// (<paramref name="partnerX"/>/<paramref name="partnerZ"/>, X unwrapped near the town).</summary>
    private IntercityStation StationFor(SettlementInstance s, int heading, double partnerX, double partnerZ)
    {
        int circ = _world.Circumference;
        var structure = RailStationGenerator.Generate(heading, _content);
        int w = structure.Width, l = structure.Length;
        double sCx = (s.Min.X + s.Max.X) * 0.5;
        double px = sCx + WorldConstants.WrapDeltaX(partnerX - sCx, circ);

        static int Cross(int lo, int hi, double target, int span)
        {
            int min = lo + span / 2, max = hi - span / 2;
            int c = min > max ? (lo + hi) / 2 : (int)Math.Clamp(Math.Round(target), min, max);
            return c - span / 2;
        }

        // #3594: a planned settlement's station docks where a road leaves the box on that side, at the road's height.
        var exit = RoadExitFor(s, heading, px, partnerZ);
        double tx = exit?.X ?? px, tz = exit?.Z ?? partnerZ;
        var (ox, oz) = heading switch
        {
            0 => (s.Max.X + 1, Cross(s.Min.Z, s.Max.Z, tz, l)),
            1 => (s.Min.X - w, Cross(s.Min.Z, s.Max.Z, tz, l)),
            2 => (Cross(s.Min.X, s.Max.X, tx, w), s.Max.Z + 1),
            _ => (Cross(s.Min.X, s.Max.X, tx, w), s.Min.Z - l),
        };

        return BuildStation(s.Name, heading, new Vector3i(ox, exit?.FloorY ?? s.GroundY, oz), "slope", structure);
    }

    /// <summary>A station from its pinned parts (heading, origin, seat): the structure is regenerated, the markers placed.</summary>
    private IntercityStation BuildStation(string settlementName, int heading, Vector3i origin, string seat, SettlementStructure? structure = null)
    {
        int circ = _world.Circumference;
        structure ??= RailStationGenerator.Generate(heading, _content);
        var st = new IntercityStation { SettlementName = settlementName, Heading = heading, Origin = origin, Seat = seat, Structure = structure };
        foreach (var m in structure.Markers)
        {
            var cell = WorldConstants.CanonicalBlock(new Vector3i(origin.X + m.LocalPos.X, origin.Y + m.LocalPos.Y, origin.Z + m.LocalPos.Z), circ);
            switch (m.Type)
            {
                case RailStationGenerator.EndPylonMarker:
                    st.EndPylon = cell;
                    break;
                case RailStationGenerator.ExitPylonMarker:
                    st.ExitPylon = cell;
                    break;
                case RailStationGenerator.StopMarker:
                    st.Stop = cell;
                    break;
            }
        }

        return st;
    }

    /// <summary>Picks the station's seat from the ground under it (the floor stays at the town's level): stilts over water,
    /// a shelf cut where the ground rises above the floor, a stepped slope otherwise. False over lava.</summary>
    private bool SeatIntercityStation(PlanetType planet, IntercityStation st)
    {
        var c = EvaluateFootprint(planet, st.Origin.X, st.Origin.Z, st.W, st.L);
        if (c.LavaSamples > 0)
        {
            return false;
        }

        st.Seat = c.WetSamples > 0 ? "stilts" : c.MaxY > st.Origin.Y + 2 ? "shelf" : "slope";
        return true;
    }
}
