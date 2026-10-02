// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity monorail (#2125, Justus' idea; Marcel's rules 2026-09-28: no tickets, no ID cards, no vending machines).
/// If a world (terrain generation 19+) has at least two inhabited towns or cities, then with a chance of 60 % one train line
/// connects the closest pair of them whose route fits, and it works: a station at each town's edge on the side facing the
/// other, pylons along the route, a corridor cleared for the wagons, and a <b>public</b> train that shuttles between the
/// two stations and waits at each one long enough to board.
/// <list type="bullet">
/// <item><b>The plan.</b> Decided once, right after the settlements (<c>StampIntercityRail</c> in the stamp chain, before the
/// ruins, camps, factories and every other structure — they all keep clear of the stations and the route), on a lane of its
/// own (seed + body id), and pinned in the placement records: <c>rail_line</c>/0 (placed or not, the pylon tops in line
/// order) and <c>rail_station</c>/0,1 (origin, heading, seat, the settlement's name). Every later load replays the records;
/// the search can evolve without moving a line under an existing world.</item>
/// <item><b>The route.</b> Each station stands on its town's side facing the partner (the dominant axis), slid along the
/// edge toward it; the line runs from the end pylon inside the station through its exit, a straight lead, then straight
/// to the partner's lead. Pylon tops stand <see cref="IntercityClearance"/> over the highest ground of their two spans
/// (water counts at its surface, ice on top), limited to a gentle grade from both stations and raised where a valley
/// would make it steeper; a route longer than <see cref="IntercityMaxRoute"/>, a pylon taller than
/// <see cref="IntercityMaxPylon"/>, lava under a pylon, too much cut terrain, or a span over another settlement, a pad or
/// the wreck site fails deterministically and the next pair is tried.</item>
/// <item><b>The stamp.</b> Voxels once (<see cref="IntercityFeature"/>): both stations seated like settlements (carve,
/// foundation, skirt), the pylon columns from the ground up, and the corridor — the wagon box swept along every span, the
/// same sweep <c>LinkClear</c> checks, a little wider — carved where the world has something in it: a tree that reaches
/// into it goes as a whole (no stump, no floating crown), terrain and props go cell by cell, fluids stay. Reads only where
/// the line runs; the time is logged.</item>
/// <item><b>The runtime.</b> The graph (nodes at the pylon tops, the links) and the public train are written into the rail
/// state after <c>LoadRails</c> when they are missing (<see cref="EnsureIntercityRail"/>), and persist with every other line
/// through <c>SaveRails</c>. The stations' stops are Crystal-Net <c>RailStop</c> devices registered without an owner. The
/// stations and the generated pylons are protected like a settlement; the generated pylons refuse every link change.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const string IntercityLineKind = "rail_line";
    private const string IntercityStationKind = "rail_station";
    private const string IntercityFeature = "intercityrail";

    /// <summary>The longest free route (horizontal blocks between the two stations' leads). At the public train's 8 blocks/s
    /// that is a ride of two minutes at most; it also bounds the chunks the stamp reads.</summary>
    private const int IntercityMaxRoute = 960;

    /// <summary>The shortest free route: two towns closer than this share no line (their stations would crowd each other).</summary>
    private const int IntercityMinRoute = 16;

    /// <summary>Blocks between two pylons along the free route (well inside the player's auto-link range of 32).</summary>
    private const int IntercityPylonSpacing = 18;

    /// <summary>A straight lead beyond each station's exit before the route turns toward the partner, so the line runs
    /// straight through the hall.</summary>
    private const int IntercityLead = 10;

    /// <summary>A pylon top stands this far over the highest ground of its two spans (the wagon floor then floats 3.7 over it).</summary>
    private const int IntercityClearance = 2;

    /// <summary>The steepest rise per horizontal block between two pylons (1 in 4).</summary>
    private const float IntercityGrade = 0.25f;

    /// <summary>The tallest pylon column (its top over its foot) — a deeper valley, a deeper sea, and the route fails.</summary>
    private const int IntercityMaxPylon = 36;

    /// <summary>The most terrain cells a route may cut through (where the grade from a station meets rising ground).</summary>
    private const int IntercityMaxCutCells = 4000;

    /// <summary>Pairs tried, closest first.</summary>
    private const int IntercityPairTries = 6;

    /// <summary>The most tree cells one stamp clears out of the corridor (bounded boot time on a jungle world).</summary>
    private const int IntercityVegetationCap = 12000;

    /// <summary>One station of the line, in world space.</summary>
    internal sealed class IntercityStation
    {
        public string SettlementName = string.Empty;
        public int Heading;
        public Vector3i Origin;             // structure-local (0,0,0) in world space; Y = the floor row (the town's foundation row)
        public string Seat = "slope";
        public SettlementStructure Structure = null!;
        public Vector3i EndPylon;           // canonical cells
        public Vector3i ExitPylon;
        public Vector3i Stop;

        public int W => Structure.Width;
        public int L => Structure.Length;

        public Vector3f Centre => new(Origin.X + W * 0.5f, Origin.Y + 1f, Origin.Z + L * 0.5f);

        public (int Cx, int Cz, int Hw, int Hl) Rect => (Origin.X + W / 2, Origin.Z + L / 2, W / 2 + 1, L / 2 + 1);
    }

    /// <summary>The line of the active world (lives on <see cref="LoadedWorld"/>): empty on every world without one.</summary>
    internal sealed class IntercityRailState
    {
        public bool Active { get; set; }
        public List<IntercityStation> Stations { get; } = new();

        /// <summary>The pylon tops in line order (station A's end pylon … station B's end pylon), canonical cells.</summary>
        public List<Vector3i> Pylons { get; } = new();
        public HashSet<Vector3i> PylonTops { get; } = new();

        /// <summary>Every cell of every generated pylon column outside the stations (canonical) — protected.</summary>
        public HashSet<Vector3i> ProtectedCells { get; } = new();

        /// <summary>The stations and the route's spans as footprints later stampers keep clear of.</summary>
        public List<(int Cx, int Cz, int Hw, int Hl)> Reservations { get; } = new();

        public void Clear()
        {
            Active = false;
            Stations.Clear();
            Pylons.Clear();
            PylonTops.Clear();
            ProtectedCells.Clear();
            Reservations.Clear();
        }
    }

    /// <summary>A planned line before it is pinned: the stations, the pylon tops, the route's spans, the cut estimate.</summary>
    private sealed class IntercityPlan
    {
        public IntercityStation A = null!;
        public IntercityStation B = null!;
        public List<Vector3i> Pylons { get; } = new();
        public int CutCells;
        public float RouteLength;
    }

    private IntercityRailState Intercity => _worlds.Active.IntercityRail;

    /// <summary>Whether a settlement may have a station: inhabited, a town or a city, on the ground (a sky island has no
    /// room for a line).</summary>
    private static bool IntercityEligible(SettlementInstance s)
        => !s.Ruined && !s.OnIsland && s.Tier is "town" or "city";

    // =====================================================================================================
    // The stamp-chain step
    // =====================================================================================================

    /// <summary>Decides (once, pinned) and stamps (once) this world's intercity line, and re-derives its runtime state on
    /// every load. Runs right after the settlements.</summary>
    private void StampIntercityRail()
    {
        var ic = Intercity;
        ic.Clear();
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        IntercityPlan? plan;
        var rec = FindPlacementRecord(IntercityLineKind, 0);
        if (rec is null)
        {
            if (!_config.PlaceIntercityRail || _generator.TerrainGeneration < WorldDescription.IntercityRailGeneration)
            {
                return; // an older world never grows a line; a server that switched the feature off decides nothing
            }

            plan = DecideIntercityRail(planet);
            if (plan is null)
            {
                RecordPlacementSkip(IntercityLineKind, 0);
                SavePlacementRecords();
                return;
            }

            PinIntercityPlan(plan);
            SavePlacementRecords();
        }
        else if (!rec.Placed)
        {
            return;
        }
        else
        {
            plan = ReplayIntercityPlan(rec);
            if (plan is null)
            {
                _log.Warn($"The intercity line record of '{_world.LocationId}' could not be replayed — the line stays off this load.");
                return;
            }
        }

        IntercityPlan line = plan;
        ActivateIntercity(line);
        int carved = 0, trees = 0;
        if (!FeatureStamped(IntercityFeature))
        {
            var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
            _repo.RunInTransaction(() => (carved, trees) = StampIntercityVoxels(line, surface));
            MarkFeatureStamped(IntercityFeature); // after the transaction: a crash mid-stamp re-stamps, never half-marks
        }

        _log.Info($"Intercity line on '{_world.LocationId}': {line.A.SettlementName} ↔ {line.B.SettlementName}, " +
                  $"{line.Pylons.Count} pylons, route {line.RouteLength:F0} blocks, {carved} corridor cells + {trees} tree cells cleared " +
                  $"in {sw.ElapsedMilliseconds} ms.");
    }

    /// <summary>The fresh decision: at least two eligible towns, the chance on the line's own lane, then the closest pair
    /// whose route fits. Null when there is no line.</summary>
    private IntercityPlan? DecideIntercityRail(PlanetType planet)
    {
        if (planet.IsGasWorld || planet.CityWorld.Length > 0 || planet.RestrictStructures)
        {
            return null; // islands only / one composed city / a structure whitelist
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

        if (IntercityRoll(_meta.Seed, _world.LocationId) >= _config.IntercityRailChance)
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
        foreach (var (_, i, j) in pairs.Take(IntercityPairTries))
        {
            if (TryPlanIntercity(planet, eligible[i], eligible[j], out var plan, out string why))
            {
                return plan;
            }

            _log.Info($"Intercity line on '{_world.LocationId}': {eligible[i].Name} ↔ {eligible[j].Name} does not fit ({why}).");
        }

        return null;
    }

    private static (double X, double Z) CentreOf(SettlementInstance s) => ((s.Min.X + s.Max.X) * 0.5, (s.Min.Z + s.Max.Z) * 0.5);

    /// <summary>The world's roll for its line (see <see cref="LaneRoll"/>).</summary>
    internal static double IntercityRoll(long worldSeed, string locationId) => LaneRoll(worldSeed, "intercity:" + locationId);

    /// <summary>A world's roll on a lane of its own, uniform in [0, 1): the seed and the lane name (the feature and the body
    /// id) through a SplitMix64 finaliser. (The first draw of a <see cref="Random"/> seeded with <c>seed ^ hash</c> is not
    /// uniform across small world seeds: those seeds differ only in their low bits, and the legacy generator's first value
    /// follows the seed almost linearly — every probed test seed rolled the same side of 0.6.) The intercity line (#2125)
    /// and the abandoned station (#2166) each roll on their own lane.</summary>
    internal static double LaneRoll(long worldSeed, string lane)
    {
        unchecked
        {
            ulong z = (ulong)(worldSeed ^ WorldGenerator.StableHash(lane));
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }
    }

    /// <summary>Test seam (#2125): the chance roll of a world and body.</summary>
    public static double IntercityRollForTest(long worldSeed, string locationId) => IntercityRoll(worldSeed, locationId);

    // =====================================================================================================
    // Planning
    // =====================================================================================================

    /// <summary>Plans a line between two towns (see the class summary). Pure over the generator's queries — no chunk is read.</summary>
    private bool TryPlanIntercity(PlanetType planet, SettlementInstance a, SettlementInstance b, out IntercityPlan plan, out string why)
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

        // The stations: clear of every other settlement, the pads, the wreck site and each other; never in lava; seated.
        var others = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            others.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        others.Add((wreckX, wreckZ, WreckReservedHalfExtent, WreckReservedHalfExtent));
        var everySettlement = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
        var notA = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
        var notB = new List<(int Cx, int Cz, int Hw, int Hl)>(others);
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
        }

        {
            var (cx, cz, hw, hl) = sa.Rect;
            if (OverlapsFootprint(cx, cz, hw, hl, new List<(int, int, int, int)> { sb.Rect }, 2))
            {
                why = "the stations would crowd each other";
                return false;
            }
        }

        // The columns: A's end + exit, A's lead, the free route, B's lead, B's exit + end — unwrapped relative to A's end.
        var (hax, haz) = RailStationGenerator.HeadingVector(headA);
        var (hbx, hbz) = RailStationGenerator.HeadingVector(headB);
        double x0 = sa.EndPylon.X;
        double U(double x) => x0 + WorldConstants.WrapDeltaX(x - x0, circ);
        var cols = new List<(double X, double Z)>
        {
            (U(sa.EndPylon.X), sa.EndPylon.Z),
            (U(sa.ExitPylon.X), sa.ExitPylon.Z),
        };
        var leadA = (X: U(sa.ExitPylon.X) + hax * IntercityLead, Z: (double)sa.ExitPylon.Z + haz * IntercityLead);
        var leadB = (X: U(sb.ExitPylon.X) + hbx * IntercityLead, Z: (double)sb.ExitPylon.Z + hbz * IntercityLead);
        double rdx = leadB.X - leadA.X, rdz = leadB.Z - leadA.Z;
        double route = Math.Sqrt(rdx * rdx + rdz * rdz);
        plan.RouteLength = (float)route;
        if (route > IntercityMaxRoute)
        {
            why = $"the route is {route:F0} blocks long";
            return false;
        }

        if (route < IntercityMinRoute || rdx * hax + rdz * haz <= 0 || -(rdx * hbx + rdz * hbz) <= 0)
        {
            why = "the towns are too close";
            return false;
        }

        int n = Math.Max(1, (int)Math.Ceiling(route / IntercityPylonSpacing));
        for (int k = 0; k <= n; k++)
        {
            double t = k / (double)n;
            cols.Add((Math.Round(leadA.X + rdx * t), Math.Round(leadA.Z + rdz * t)));
        }

        cols.Add((U(sb.ExitPylon.X), sb.ExitPylon.Z));
        cols.Add((U(sb.EndPylon.X), sb.EndPylon.Z));
        int m = cols.Count;

        int latLimit = WorldConstants.LatitudePeriodFor(circ) / 2 - 8;
        if (cols.Any(c => Math.Abs(c.Z) > latLimit))
        {
            why = "the line would cross the latitude seam";
            return false;
        }

        // The spans outside the stations must keep clear of every settlement, pad and the wreck site.
        var spanRects = new List<(int Cx, int Cz, int Hw, int Hl)>();
        for (int j = 1; j < m - 2; j++)
        {
            var (p, q) = (cols[j], cols[j + 1]);
            var r = ((int)Math.Round((p.X + q.X) * 0.5), (int)Math.Round((p.Z + q.Z) * 0.5),
                (int)Math.Ceiling(Math.Abs(q.X - p.X) * 0.5) + 3, (int)Math.Ceiling(Math.Abs(q.Z - p.Z) * 0.5) + 3);
            spanRects.Add(r);
            if (j > 1 && j < m - 3 && OverlapsFootprint(r.Item1, r.Item2, r.Item3, r.Item4, everySettlement, 1))
            {
                why = "the route would cross another structure";
                return false;
            }

            // The leads leave their own station: they only have to keep clear of everything but their own town.
            if ((j == 1 && OverlapsFootprint(r.Item1, r.Item2, r.Item3, r.Item4, notA, 1))
                || (j == m - 3 && OverlapsFootprint(r.Item1, r.Item2, r.Item3, r.Item4, notB, 1)))
            {
                why = "a lead would cross another structure";
                return false;
            }
        }

        // The profile. Ground per span (the highest of three lines along it; water at its surface with the ice on top).
        var spanMax = new int[m - 1];
        for (int j = 0; j < m - 1; j++)
        {
            spanMax[j] = int.MinValue;
            if (j == 0 || j == m - 2)
            {
                continue; // inside a station: the station's own seat carves it
            }

            var (p, q) = (cols[j], cols[j + 1]);
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

        var dist = new double[m];
        for (int j = 1; j < m; j++)
        {
            var (p, q) = (cols[j - 1], cols[j]);
            dist[j] = dist[j - 1] + Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z));
        }

        int gyA = sa.Origin.Y, gyB = sb.Origin.Y;
        var y = new double[m];
        y[0] = y[1] = gyA;
        y[m - 1] = y[m - 2] = gyB;
        for (int j = 2; j <= m - 3; j++)
        {
            double desired = Math.Max(spanMax[j - 1], spanMax[j]) + IntercityClearance;
            double cone = Math.Min(gyA + IntercityGrade * (dist[j] - dist[1]), gyB + IntercityGrade * (dist[m - 2] - dist[j]));
            y[j] = Math.Min(desired, cone);
        }

        for (int j = 2; j <= m - 3; j++)
        {
            y[j] = Math.Max(y[j], y[j - 1] - IntercityGrade * (dist[j] - dist[j - 1]));
        }

        for (int j = m - 3; j >= 2; j--)
        {
            y[j] = Math.Max(y[j], y[j + 1] - IntercityGrade * (dist[j + 1] - dist[j]));
        }

        var tops = new int[m];
        for (int j = 0; j < m; j++)
        {
            tops[j] = (int)Math.Ceiling(y[j] - 1e-3);
        }

        // Pylon feet: never in lava, never taller than the cap.
        for (int j = 2; j <= m - 3; j++)
        {
            int px = (int)Math.Floor(cols[j].X), pz = (int)Math.Floor(cols[j].Z);
            if (_generator.IsSurfaceLava(planet, px, pz))
            {
                why = "a pylon would stand in lava";
                return false;
            }

            int foot = PylonFoot(planet, px, pz);
            if (tops[j] - foot + 1 > IntercityMaxPylon)
            {
                why = $"a pylon would be {tops[j] - foot + 1} blocks tall";
                return false;
            }
        }

        // The cut: terrain the corridor would run through outside the stations (the grade from a station meets a hill).
        int cut = 0;
        for (int j = 1; j < m - 2; j++)
        {
            var (p, q) = (cols[j], cols[j + 1]);
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

        plan.CutCells = cut;
        if (cut > IntercityMaxCutCells)
        {
            why = $"the route would cut {cut} blocks of terrain";
            return false;
        }

        if (!_worlds.Active.VirginAtLoad)
        {
            for (int j = 1; j < m - 2; j++)
            {
                var r = spanRects[j - 1];
                int lo = Math.Min(tops[j], tops[j + 1]) - 4;
                int hi = Math.Max(tops[j], tops[j + 1]) + 6;
                if (FootprintHasPlayerEdits(r.Cx - r.Hw, r.Cz - r.Hl, lo, r.Hw * 2, hi - lo, r.Hl * 2))
                {
                    why = "somebody built on the route";
                    return false;
                }
            }
        }

        plan.A = sa;
        plan.B = sb;
        for (int j = 0; j < m; j++)
        {
            plan.Pylons.Add(WorldConstants.CanonicalBlock(new Vector3i((int)Math.Floor(cols[j].X), tops[j], (int)Math.Floor(cols[j].Z)), circ));
        }

        why = string.Empty;
        return true;
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

        int ox, oz;
        switch (heading)
        {
            case 0:
                ox = s.Max.X + 1;
                oz = Cross(s.Min.Z, s.Max.Z, partnerZ, l);
                break;
            case 1:
                ox = s.Min.X - w;
                oz = Cross(s.Min.Z, s.Max.Z, partnerZ, l);
                break;
            case 2:
                oz = s.Max.Z + 1;
                ox = Cross(s.Min.X, s.Max.X, px, w);
                break;
            default:
                oz = s.Min.Z - l;
                ox = Cross(s.Min.X, s.Max.X, px, w);
                break;
        }

        return BuildStation(s.Name, heading, new Vector3i(ox, s.GroundY, oz), "slope", structure);
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

    // =====================================================================================================
    // Pinning and replaying
    // =====================================================================================================

    private void PinIntercityPlan(IntercityPlan plan)
    {
        RecordPlacement(IntercityLineKind, 0, plan.A.Origin, plan.A.Origin.Y, onIsland: false, "line",
            plan.A.SettlementName + " | " + plan.B.SettlementName,
            composition: plan.Pylons.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture) + "," + p.Z.ToString(CultureInfo.InvariantCulture)).ToList());
        int i = 0;
        foreach (var st in new[] { plan.A, plan.B })
        {
            RecordPlacement(IntercityStationKind, i++, st.Origin, st.Origin.Y, onIsland: false, st.Seat, st.SettlementName,
                template: "heading=" + st.Heading.ToString(CultureInfo.InvariantCulture));
        }
    }

    private IntercityPlan? ReplayIntercityPlan(StructurePlacementRecord line)
    {
        var plan = new IntercityPlan();
        var stations = new IntercityStation[2];
        for (int i = 0; i < 2; i++)
        {
            var rec = FindPlacementRecord(IntercityStationKind, i);
            if (rec is not { Placed: true } || !rec.Template.StartsWith("heading=", StringComparison.Ordinal)
                || !int.TryParse(rec.Template.Substring("heading=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int heading)
                || heading is < 0 or > 3)
            {
                return null;
            }

            stations[i] = BuildStation(rec.Name, heading, new Vector3i(rec.X, rec.GroundY, rec.Z), rec.Seat);
        }

        foreach (string s in line.Composition ?? new List<string>())
        {
            if (!TryCell(s, out var cell))
            {
                return null;
            }

            plan.Pylons.Add(cell);
        }

        if (plan.Pylons.Count < 4)
        {
            return null;
        }

        plan.A = stations[0];
        plan.B = stations[1];
        for (int j = 1; j < plan.Pylons.Count; j++)
        {
            float dx = (float)WorldConstants.WrapDeltaX((double)(plan.Pylons[j].X - plan.Pylons[j - 1].X), _world.Circumference);
            float dz = plan.Pylons[j].Z - plan.Pylons[j - 1].Z;
            plan.RouteLength += (float)Math.Sqrt(dx * dx + dz * dz);
        }

        return plan;
    }

    /// <summary>Makes the plan the active world's line: the stations, the pylon tops, the protected pylon columns and the
    /// footprints later stampers keep clear of.</summary>
    private void ActivateIntercity(IntercityPlan plan)
    {
        var ic = Intercity;
        var planet = _world.Planet;
        int circ = _world.Circumference;
        ic.Clear();
        ic.Active = true;
        ic.Stations.Add(plan.A);
        ic.Stations.Add(plan.B);
        ic.Pylons.AddRange(plan.Pylons);
        foreach (var p in plan.Pylons)
        {
            ic.PylonTops.Add(p);
        }

        for (int j = 2; j < plan.Pylons.Count - 2; j++)
        {
            var top = plan.Pylons[j];
            int foot = Math.Min(top.Y, PylonFoot(planet, top.X, top.Z));
            for (int yy = foot; yy <= top.Y; yy++)
            {
                ic.ProtectedCells.Add(WorldConstants.CanonicalBlock(new Vector3i(top.X, yy, top.Z), circ));
            }
        }

        foreach (var st in ic.Stations)
        {
            ic.Reservations.Add(st.Rect);
        }

        for (int j = 1; j < plan.Pylons.Count - 2; j++)
        {
            var p = plan.Pylons[j];
            var q = plan.Pylons[j + 1];
            int dx = WorldConstants.WrapDeltaX(q.X - p.X, circ), dz = q.Z - p.Z;
            ic.Reservations.Add((p.X + dx / 2, p.Z + dz / 2, Math.Abs(dx) / 2 + 3, Math.Abs(dz) / 2 + 3));
        }
    }

    // =====================================================================================================
    // Voxels (once)
    // =====================================================================================================

    /// <summary>Stamps the stations, the pylon columns and the corridor. Returns the corridor cells and tree cells cleared.
    /// Must run inside a repo transaction.</summary>
    private (int Carved, int Trees) StampIntercityVoxels(IntercityPlan plan, string surface)
    {
        var planet = _world.Planet;
        foreach (var st in new[] { plan.A, plan.B })
        {
            StampSettlementBlocks(new PlacedSettlement
            {
                Structure = st.Structure,
                Origin = st.Origin,
                GroundY = st.Origin.Y,
                Tier = RailStationGenerator.Tier,
                Ruined = false,
                OnIsland = false,
                Name = st.SettlementName,
                Rng = new Random(1),
                Seat = st.Seat,
            }, surface, crownRing: false);
        }

        var pylonId = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId ?? BlockId.Air;
        if (pylonId.IsAir)
        {
            return (0, 0);
        }

        for (int j = 2; j < plan.Pylons.Count - 2; j++)
        {
            var top = plan.Pylons[j];
            int foot = Math.Min(top.Y, PylonFoot(planet, top.X, top.Z));
            for (int yy = foot; yy <= top.Y; yy++)
            {
                _world.SetBlock(new Vector3i(top.X, yy, top.Z), pylonId);
            }
        }

        int carved = 0, trees = 0;
        var veg = SettlementVegetationIds;
        var seen = new HashSet<Vector3i>();
        for (int j = 0; j < plan.Pylons.Count - 1; j++)
        {
            foreach (var cell in IntercityCorridor(plan.Pylons[j], plan.Pylons[j + 1]))
            {
                var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
                if (!seen.Add(c) || !WithinBuildHeight(c.Y) || IsStationLayoutCell(c) || Intercity.PylonTops.Contains(c))
                {
                    continue; // the station's own blocks stand clear of the corridor by design; anything else in it goes
                }

                var id = _world.GetBlock(c);
                if (id.IsAir)
                {
                    continue;
                }

                var def = _content.BlockById(id);
                if (def is null || def.Liquid || IsFluid(id.Value)
                    || def.Key is RailRules.PylonBlockKey or RailRules.StopBlockKey || InAnySettlementBox(c))
                {
                    continue; // fluids stay (the line runs over them); the towns' blocks are never touched
                }

                if (veg.Contains(id.Value) && trees < IntercityVegetationCap)
                {
                    trees += ClearTreeAt(c, veg, IntercityVegetationCap - trees);
                    continue;
                }

                _world.SetBlock(c, BlockId.Air);
                carved++;
            }
        }

        return (carved, trees);
    }

    /// <summary>The corridor of one span: first exactly the cells <c>LinkClear</c> checks (so the generated link passes the
    /// player's own clearance rule), then the wagon's whole box (3 wide, 3.2 high over its floor) swept along the straight
    /// run between the two pylon tops half a block at a time, reaching to the box's edges.</summary>
    private IEnumerable<Vector3i> IntercityCorridor(Vector3i a, Vector3i b)
    {
        foreach (var cell in LinkClearCells(a, b))
        {
            yield return cell;
        }

        var ta = PylonTop(a);
        var tb = PylonTop(b);
        float dx = (float)WorldConstants.WrapDeltaX((double)(tb.X - ta.X), _world.Circumference);
        float dy = tb.Y - ta.Y, dz = tb.Z - ta.Z;
        float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = Math.Max(1, (int)Math.Ceiling(len * 2f));
        float hl = (float)Math.Sqrt(dx * dx + dz * dz);
        float rx = hl < 1e-3f ? 1f : dz / hl, rz = hl < 1e-3f ? 0f : -dx / hl;
        float[] sides = { -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f };
        float[] ups = { 0f, 1f, 2f, 3f, RailRules.WagonHeight + 0.1f };
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float cx = ta.X + dx * t, cy = ta.Y + dy * t + RailRules.HoverHeight, cz = ta.Z + dz * t;
            foreach (float side in sides)
            {
                foreach (float up in ups)
                {
                    yield return new Vector3i((int)Math.Floor(cx + rx * side), (int)Math.Floor(cy + up), (int)Math.Floor(cz + rz * side));
                }
            }
        }
    }

    /// <summary>Takes a whole tree out of the world from one of its cells: every connected trunk, crown and fruit cell within
    /// a tree's reach, so the corridor leaves neither a stump nor a floating crown. Never inside a town. Returns the cells cleared.</summary>
    private int ClearTreeAt(Vector3i seed, HashSet<ushort> veg, int budget)
    {
        const int Reach = 6, Rise = 26;
        const int PerTree = 600;
        int cleared = 0;
        var stack = new Stack<Vector3i>();
        var seen = new HashSet<Vector3i> { seed };
        stack.Push(seed);
        while (stack.Count > 0 && cleared < budget && cleared < PerTree)
        {
            var c = stack.Pop();
            if (!WithinBuildHeight(c.Y) || InAnySettlementBox(c) || IsStationLayoutCell(c))
            {
                continue;
            }

            var id = _world.GetBlock(c);
            if (!veg.Contains(id.Value))
            {
                continue;
            }

            _world.SetBlock(c, BlockId.Air);
            cleared++;
            foreach (var face in CrystalNetRules.Faces)
            {
                var n = WorldConstants.CanonicalBlock(c + face, _world.Circumference);
                if (Math.Abs(WorldConstants.WrapDeltaX(n.X - seed.X, _world.Circumference)) <= Reach && Math.Abs(n.Z - seed.Z) <= Reach
                    && Math.Abs(n.Y - seed.Y) <= Rise && seen.Add(n))
                {
                    stack.Push(n);
                }
            }
        }

        return cleared;
    }

    /// <summary>Whether a cell lies inside any settlement's box, ruins included — the line's stamp never touches a town.</summary>
    private bool InAnySettlementBox(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - s.Min.X, circ);
            if (lx >= 0 && lx <= s.Max.X - s.Min.X && pos.Z >= s.Min.Z && pos.Z <= s.Max.Z && pos.Y >= s.Min.Y - 48 && pos.Y <= s.Max.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a station's own layout puts a block at this cell (its floor, posts, roof, lamps, benches, pylons,
    /// the stop) — as opposed to whatever else stands inside its hall.</summary>
    private bool IsStationLayoutCell(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var st in Intercity.Stations)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - st.Origin.X, circ);
            int ly = pos.Y - st.Origin.Y;
            int lz = pos.Z - st.Origin.Z;
            if (st.Structure.InBounds(lx, ly, lz) && st.Structure.Get(lx, ly, lz) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a cell lies inside a station's protected volume (its hall and the plinth under it).</summary>
    private bool InIntercityStation(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var st in Intercity.Stations)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - st.Origin.X, circ);
            int lz = pos.Z - st.Origin.Z;
            if (lx >= 0 && lx < st.W && lz >= 0 && lz < st.L && pos.Y >= st.Origin.Y - 48 && pos.Y < st.Origin.Y + RailStationGenerator.Height)
            {
                return true;
            }
        }

        return false;
    }

    // =====================================================================================================
    // Protection, reservations, the map
    // =====================================================================================================

    /// <summary>Whether a block belongs to the intercity line: a station's hall or plinth, or a generated pylon column.
    /// Protected like a settlement (#2125) — so nobody breaks the line by accident.</summary>
    internal bool IsIntercityRailBlock(Vector3i pos)
    {
        var ic = Intercity;
        if (!ic.Active)
        {
            return false;
        }

        return ic.ProtectedCells.Contains(WorldConstants.CanonicalBlock(pos, _world.Circumference)) || InIntercityStation(pos);
    }

    /// <summary>Whether a cell is one of the two stations' stops.</summary>
    private bool IsIntercityStop(Vector3i cell) => Intercity.Active && Intercity.Stations.Any(s => s.Stop == cell);

    /// <summary>Whether a pylon top is one of the generated line's — its links are the line's, no player may change them.</summary>
    private bool IsGeneratedPylon(Vector3i top) => Intercity.Active && Intercity.PylonTops.Contains(top);

    /// <summary>Adds the stations and the route's spans to a stamper's reserved footprints (#2125).</summary>
    private void AppendIntercityReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        if (Intercity.Active)
        {
            reserved.AddRange(Intercity.Reservations);
        }
    }

    /// <summary>Whether a point (or a small area around it) lies on the stations or the route (#2125) — the surface
    /// stampers that ask <see cref="OverlapsAnySettlement"/> keep clear of the line the same way.</summary>
    private bool OverlapsIntercityRail(int x, int z, int halfExtent)
        => Intercity.Active && OverlapsFootprint(x, z, halfExtent, halfExtent, Intercity.Reservations, SettlementCollisionMargin);

    /// <summary>The two stations on the world map (#2125): "Karth Town Station" / "Bahnhof Karth Town".</summary>
    private void AppendIntercityPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var st in Intercity.Stations)
        {
            var c = st.Centre;
            pois.Add(new NetPoi
            {
                Type = "rail_station",
                Name = string.Format(CultureInfo.InvariantCulture, Localize(session.Locale, "poi.rail_station"), st.SettlementName),
                X = (float)WorldConstants.WrapX((double)c.X, _world.Circumference),
                Z = c.Z,
            });
        }
    }

    // =====================================================================================================
    // The runtime: the graph, the stops, the public train
    // =====================================================================================================

    /// <summary>After the rail graph is read from the metadata: the stations' stops as owner-less Crystal-Net
    /// <c>RailStop</c> devices, the generated nodes and links. Idempotent — the first load writes them, every later load
    /// finds them. Returns whether anything was added.</summary>
    private bool EnsureIntercityGraph()
    {
        var ic = Intercity;
        if (!ic.Active)
        {
            return false;
        }

        foreach (var st in ic.Stations)
        {
            if (!CrystalNet.Cells.ContainsKey(st.Stop))
            {
                RegisterCrystalCell(st.Stop, CrystalDeviceKind.RailStop, RailRules.StopBlockKey, RailRules.PublicOwnerId, 0, string.Empty, string.Empty, 0, persist: true);
            }
        }

        var rails = Rails;
        bool changed = false;
        foreach (var p in ic.Pylons)
        {
            if (!rails.Nodes.ContainsKey(p))
            {
                rails.Nodes[p] = new RailNode { Cell = p };
                changed = true;
            }
        }

        for (int j = 0; j < ic.Pylons.Count - 1; j++)
        {
            var a = ic.Pylons[j];
            var b = ic.Pylons[j + 1];
            if (!rails.Nodes[a].Links.Contains(b))
            {
                rails.Nodes[a].Links.Add(b);
                rails.Nodes[b].Links.Add(a);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>The generated line as the rail state knows it (null when it is not built, or not whole).</summary>
    private RailLine? IntercityLine()
        => Intercity.Active && Intercity.Pylons.Count > 0
            ? Rails.Lines.Values.FirstOrDefault(l => l.Pylons.Contains(Intercity.Pylons[0]))
            : null;

    /// <summary>After the lines are rebuilt: the public train on the generated line, when it has none — halted at the
    /// station with the lower arc as though it had just arrived there. Returns whether one was added.</summary>
    private bool EnsureIntercityTrain()
    {
        var line = IntercityLine();
        if (line is null)
        {
            return false;
        }

        var rails = Rails;
        if (rails.Trains.Any(t => RailRules.IsPublic(t.OwnerId) && t.LineId == line.Id))
        {
            return false;
        }

        var train = new ServerTrain
        {
            Id = "train-" + _world.LocationId + "-" + rails.NextTrainId++,
            OwnerId = RailRules.PublicOwnerId,
            LineId = line.Id,
            Speed = RailRules.PublicTrainSpeed,
            Direction = -1,
            Autopilot = true,
        };
        train.Wagons.AddRange(RailRules.PublicTrainWagons);
        if (line.Stops.Count > 0)
        {
            var (arc, cell) = line.Stops[0];
            train.Arc = arc;
            train.Halted = true;
            train.HaltUntil = _uptime + RailRules.PublicStopHaltSeconds;
            train.HaltStop = cell;
            train.LastStop = cell;
        }
        else
        {
            train.Arc = Math.Min(line.Spline.TotalArc, RailRules.WagonLength);
        }

        rails.Trains.Add(train);
        return true;
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#2125): the active world's intercity line — both stations (their town, heading, origin, footprint,
    /// stop cell and end pylon) and the pylon tops in line order; null when the world has none.</summary>
    public (IReadOnlyList<(string Settlement, int Heading, Vector3i Origin, int W, int L, Vector3i Stop, Vector3i EndPylon)> Stations, IReadOnlyList<Vector3i> Pylons)? IntercityRailForTest()
        => Intercity.Active
            ? (Intercity.Stations.Select(s => (s.SettlementName, s.Heading, s.Origin, s.W, s.L, s.Stop, s.EndPylon)).ToList(), Intercity.Pylons.ToList())
            : null;

    /// <summary>Test seam (#2125): the settlement names on this world, in stamp order (the order of the tier and box seams).</summary>
    public IReadOnlyList<string> SettlementNamesForTest => _settlements.Select(s => s.Name).ToList();

    /// <summary>Test seam (#2125): whether a cell is protected as part of the line.</summary>
    public bool IsIntercityRailBlockForTest(Vector3i pos) => IsIntercityRailBlock(pos);

    /// <summary>Test seam (#2125): the clearance check between two pylon tops, exactly as the player's links are checked.</summary>
    public bool RailLinkClearForTest(Vector3i a, Vector3i b) => LinkClear(a, b);

    /// <summary>Test seam (#2125): the train's remaining halt (seconds; 0 when it runs) and the stop it halts at.</summary>
    public (bool Halted, double Remaining, Vector3i? Stop) TrainHaltForTest(string trainId)
        => Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } t
            ? (t.Halted, t.Halted && t.HaltUntil > 0 ? Math.Max(0.0, t.HaltUntil - _uptime) : 0.0, t.HaltStop)
            : (false, 0.0, null);

    /// <summary>Test seam (#2125): uses the rail linker as a player on two pylon cells (pick, then couple/uncouple) and
    /// returns whether the second use was accepted.</summary>
    public bool RailLinkerForTest(string playerId, Vector3i a, Vector3i b)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return false;
        }

        Rails.LinkerPending.Remove(playerId);
        UseRailLinker(s, new Vector3f(a.X + 0.5f, a.Y + 0.5f, a.Z + 0.5f));
        return UseRailLinker(s, new Vector3f(b.X + 0.5f, b.Y + 0.5f, b.Z + 0.5f));
    }

    /// <summary>Test seam (#2125): couples a wagon as a player at a target point, returns whether it was accepted.</summary>
    public bool RailWagonForTest(string playerId, string wagonItem, Vector3f target)
        => FindSessionByPlayerId(playerId) is { } s && UseRailWagon(s, wagonItem, target);
}
