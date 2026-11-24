// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity monorail (#2125, Justus' idea; Marcel's rules 2026-09-28: no tickets, no ID cards, no vending machines).
/// If a world (terrain generation 19+) has two inhabited towns or more, then with a chance of 60 % (80 % from generation 42 —
/// what changed with it: <see cref="IntercityMaxRouteFor"/>) one line joins the closest pair whose route fits, and it works:
/// a station at each town's edge facing the other, pylons along the route, a corridor cleared for the wagons, and a
/// <b>public</b> train that shuttles between the stations and waits at each one long enough to board.
/// <para>#3984: the line is planned and built in two separate steps. A <b>planner</b> turns a world into an
/// <see cref="IntercityPlan"/> — one line's ordered stops (their stations, spots and headings) and its pylon tops in line
/// order: the <b>rolled</b> planner (GameServerIntercityRailPlanner.cs) picks two towns and plans the line between them under
/// <see cref="IntercityLimits.Rolled"/>; the <b>given</b> planner (GameServerIntercityRailGivenRoute.cs) plans a route
/// handed in as stops and waypoints. A <b>builder</b> (GameServerIntercityRailBuilder.cs) takes plans — any number of stops
/// on a line, any number of lines on a world — pins them (records per line index), stamps their stations, pylons and
/// corridors, and the <b>runtime</b> (GameServerIntercityRailRuntime.cs) registers their graph and puts a public train on each.
/// The rolled line is the rolled planner plus the builder, bit-identical to the single flow it replaced.</para>
/// <list type="bullet">
/// <item><b>The plan.</b> Decided once, right after the settlements (<c>StampIntercityRail</c> in the stamp chain, before the
/// ruins, camps, factories and every other structure — they all keep clear of the stations and the route), on a lane of its
/// own (seed + body id), and pinned in the placement records: <c>rail_line</c>/line (placed or not, the pylon tops in line
/// order) and <c>rail_station</c>/<see cref="IntercityStationRecordIndex"/> (origin, heading, seat, the settlement's name).
/// Every later load replays the records; the search can evolve without moving a line under an existing world.</item>
/// <item><b>The route.</b> Each station stands on its town's side facing the partner (the dominant axis), slid along the
/// edge toward it; the line runs from the end pylon inside the station through its exit, a straight lead, then straight
/// to the partner's lead. Pylon tops stand <see cref="IntercityClearance"/> over the highest ground of their two spans
/// (water counts at its surface, ice on top), limited to a gentle grade from the stations and raised where a valley
/// would make it steeper; a route longer than the limit, a pylon taller than <see cref="IntercityMaxPylon"/>, lava under a
/// pylon, too much cut terrain, or a span over another settlement, a pad or the wreck site fails deterministically and the
/// next pair is tried.</item>
/// <item><b>The stamp.</b> Voxels once per line (<see cref="IntercityFeature"/>): the stations seated like settlements
/// (carve, foundation, skirt), the pylon columns from the ground up, and the corridor — the wagon box swept along every
/// span, the same sweep <c>LinkClear</c> checks, a little wider — carved where the world has something in it: a tree that
/// reaches into it goes as a whole (no stump, no floating crown), terrain and props go cell by cell, fluids stay. Reads only
/// where the line runs; the time is logged.</item>
/// <item><b>The runtime.</b> The graph (nodes at the pylon tops, the links) and the public trains are written into the rail
/// state after <c>LoadRails</c> when they are missing, and persist with every other line through <c>SaveRails</c>. The
/// stations' stops are Crystal-Net <c>RailStop</c> devices registered without an owner. The stations and the generated
/// pylons are protected like a settlement; the generated pylons refuse every link change.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const string IntercityLineKind = "rail_line";
    private const string IntercityStationKind = "rail_station";
    private const string IntercityFeature = "intercityrail";

    /// <summary>The longest free route (horizontal blocks between the two stations' leads) before terrain generation 42; from
    /// it on <see cref="IntercityMaxRouteFor"/> allows 2000 (#3629). It also bounds the chunks the stamp reads.</summary>
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

    /// <summary>The tallest pylon column (its top over its foot) of a rolled line — a deeper valley, a deeper sea, and the
    /// route fails.</summary>
    private const int IntercityMaxPylon = 36;

    /// <summary>The most terrain cells a rolled route may cut through (where the grade from a station meets rising ground).</summary>
    private const int IntercityMaxCutCells = 4000;

    /// <summary>Pairs tried, closest first.</summary>
    private const int IntercityPairTries = 6;

    /// <summary>The corridor's tree budget before generation 42 (then <see cref="IntercityVegetationCapFor"/>, #3630).</summary>
    private const int IntercityVegetationCap = 12000;

    /// <summary>Station records per line: the stop <c>s</c> of line <c>k</c> is <c>rail_station</c>/<c>k × this + s</c> (#3984;
    /// line 0's two stations stay records 0 and 1).</summary>
    private const int IntercityStopsPerLine = 64;

    /// <summary>The station record index of a line's stop.</summary>
    private static int IntercityStationRecordIndex(int line, int stop) => (line * IntercityStopsPerLine) + stop;

    /// <summary>The stamped-feature key of a line's voxels (line 0 keeps the key every older world marked).</summary>
    private static string IntercityFeatureOf(int line)
        => line == 0 ? IntercityFeature : IntercityFeature + ":" + line.ToString(CultureInfo.InvariantCulture);

    /// <summary>The limits a planner keeps: the longest free route, the tallest pylon and the most terrain cut (#3984). The
    /// rolled planner keeps the shipped ones; a given route may lift them.</summary>
    internal readonly record struct IntercityLimits(int MaxRoute, int MaxPylon, int MaxCutCells, int SeaPylonSpacing = IntercityPylonSpacing)
    {
        /// <summary>The rolled line's limits on a terrain generation (#3629: 2000 blocks of route from generation 42).</summary>
        public static IntercityLimits Rolled(int terrainGeneration)
            => new(IntercityMaxRouteFor(terrainGeneration), IntercityMaxPylon, IntercityMaxCutCells);

        /// <summary>A listed line's limits (#3985): a route of any length across the oceans, pylons standing on the sea bed
        /// however deep, the cut whatever the mountains ask, and a pylon every <see cref="IntercitySeaPylonSpacing"/> blocks
        /// over open water.</summary>
        public static IntercityLimits Listed => new(int.MaxValue, int.MaxValue, int.MaxValue, IntercitySeaPylonSpacing);
    }

    /// <summary>A station hall's shape (#3985): its platforms and whether it is a through station. The default is the rolled
    /// station's: both platforms, a terminus. Pinned in the station record's template after the heading —
    /// <c>heading=N</c> for the default (every rolled record), <c>heading=N;platforms=low;through</c> otherwise.</summary>
    internal readonly record struct IntercityHallShape(RailHallPlatforms Platforms, bool Through)
    {
        private const string HeadingPin = "heading=";
        private const string PlatformsPin = "platforms=";
        private const string ThroughPin = "through";

        /// <summary>The station record's template for a heading and this shape.</summary>
        public string Template(int heading)
        {
            string t = HeadingPin + heading.ToString(CultureInfo.InvariantCulture);
            if (Platforms != RailHallPlatforms.Both)
            {
                t += ";" + PlatformsPin + (Platforms == RailHallPlatforms.Low ? "low" : "high");
            }

            return Through ? t + ";" + ThroughPin : t;
        }

        /// <summary>Reads a station record's template: false when it names no heading 0..3 or an unknown part.</summary>
        public static bool TryParse(string template, out int heading, out IntercityHallShape shape)
        {
            heading = -1;
            shape = default;
            var platforms = RailHallPlatforms.Both;
            bool through = false;
            foreach (string part in template.Split(';'))
            {
                if (part.StartsWith(HeadingPin, StringComparison.Ordinal))
                {
                    if (!int.TryParse(part.Substring(HeadingPin.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out heading))
                    {
                        return false;
                    }
                }
                else if (part == PlatformsPin + "low" || part == PlatformsPin + "high")
                {
                    platforms = part.EndsWith("low", StringComparison.Ordinal) ? RailHallPlatforms.Low : RailHallPlatforms.High;
                }
                else if (part == ThroughPin)
                {
                    through = true;
                }
                else
                {
                    return false;
                }
            }

            shape = new IntercityHallShape(platforms, through);
            return heading is >= 0 and <= 3;
        }
    }

    /// <summary>One station of a line, in world space.</summary>
    internal sealed class IntercityStation
    {
        public string SettlementName = string.Empty;
        public int Heading;
        public Vector3i Origin;             // structure-local (0,0,0) in world space; Y = the floor row (the town's foundation row)
        public string Seat = "slope";

        /// <summary>The hall's platforms (#3985: a city's hall beside its street keeps one) — both for every rolled station.</summary>
        public RailHallPlatforms Platforms = RailHallPlatforms.Both;

        /// <summary>A stop in the middle of its line (#3985): its stop plate stands in the hall's middle.</summary>
        public bool Through;

        public SettlementStructure Structure = null!;
        public Vector3i EndPylon;           // canonical cells
        public Vector3i ExitPylon;
        public Vector3i Stop;

        public IntercityHallShape Shape => new(Platforms, Through);

        public int W => Structure.Width;
        public int L => Structure.Length;

        public Vector3f Centre => new(Origin.X + W * 0.5f, Origin.Y + 1f, Origin.Z + L * 0.5f);

        public (int Cx, int Cz, int Hw, int Hl) Rect => (Origin.X + W / 2, Origin.Z + L / 2, W / 2 + 1, L / 2 + 1);

        /// <summary>Whether a pylon top is one of this station's own (inside its hall).</summary>
        public bool OwnsPylon(Vector3i top) => top == EndPylon || top == ExitPylon;
    }

    /// <summary>One built line of the active world: its index (records, feature), its stops in line order, its pylon tops.</summary>
    internal sealed class IntercityLineState
    {
        public int Index;
        public List<IntercityStation> Stations { get; } = new();

        /// <summary>The pylon tops in line order (the first stop's end pylon … the last stop's end pylon), canonical cells.</summary>
        public List<Vector3i> Pylons { get; } = new();

        /// <summary>The plan the line was built from (the section stamp reads it).</summary>
        public IntercityPlan Plan = null!;

        /// <summary>A listed line's sections (#3985), each stamped the first time a player comes near; empty for a line
        /// stamped whole.</summary>
        public List<IntercitySection> Sections { get; } = new();
    }

    /// <summary>The lines of the active world (lives on <see cref="LoadedWorld"/>): empty on every world without one.</summary>
    internal sealed class IntercityRailState
    {
        public bool Active => Lines.Count > 0;

        public List<IntercityLineState> Lines { get; } = new();

        /// <summary>Every line's stations, in line order.</summary>
        public List<IntercityStation> Stations { get; } = new();

        /// <summary>Every line's pylon tops, canonical cells.</summary>
        public HashSet<Vector3i> PylonTops { get; } = new();

        /// <summary>Every cell of every generated pylon column outside the stations (canonical) — protected.</summary>
        public HashSet<Vector3i> ProtectedCells { get; } = new();

        /// <summary>The stations and the routes' spans as footprints later stampers keep clear of.</summary>
        public List<(int Cx, int Cz, int Hw, int Hl)> Reservations { get; } = new();

        public void Clear()
        {
            Lines.Clear();
            Stations.Clear();
            PylonTops.Clear();
            ProtectedCells.Clear();
            Reservations.Clear();
        }
    }

    /// <summary>A planned line before it is built (#3984): its index, its stops in line order (two or more), the pylon tops in
    /// line order (each stop's own pylons inside its hall, the free route between them), the route length and the cut estimate.</summary>
    internal sealed class IntercityPlan
    {
        public int LineIndex;
        public List<IntercityStation> Stops { get; } = new();
        public List<Vector3i> Pylons { get; } = new();
        public int CutCells;
        public float RouteLength;

        /// <summary>A line of the body's place list (#3985): stamped section by section, its corridor clears the land a city
        /// left to nature inside its box.</summary>
        public bool Listed;

        /// <summary>A listed line's name key (its place's <c>nameKey</c>); empty for every other line.</summary>
        public string NameKey = string.Empty;

        /// <summary>Whether pylon <paramref name="j"/> stands inside a stop's hall (its height is the station's floor).</summary>
        public bool IsStationPylon(int j) => Stops.Exists(s => s.OwnsPylon(Pylons[j]));

        /// <summary>Whether the span from pylon <paramref name="j"/> to the next runs inside one stop's hall.</summary>
        public bool IsStationSpan(int j) => Stops.Exists(s => s.OwnsPylon(Pylons[j]) && s.OwnsPylon(Pylons[j + 1]));
    }

    private IntercityRailState Intercity => _worlds.Active.IntercityRail;

    /// <summary>Whether a settlement may have a station: inhabited, a town or a city, on the ground (a sky island has no
    /// room for a line).</summary>
    private static bool IntercityEligible(SettlementInstance s)
        => !s.Ruined && !s.OnIsland && s.Tier is "town" or "city";

    // =====================================================================================================
    // The stamp-chain step
    // =====================================================================================================

    /// <summary>Decides (once, pinned) and builds (once) this world's lines, and re-derives their runtime state on every
    /// load: line 0 is the rolled line, every further pinned line is replayed, then the lines of the body's place list
    /// (#3985). Runs right after the settlements; <paramref name="rolled"/> is whether the settlements were stamped — the
    /// rolled line and the given ones belong to them, the listed lines stand on the cities' records whatever the
    /// settlement options say.</summary>
    private void StampIntercityRail(bool rolled)
    {
        Intercity.Clear();
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var lines = new List<IntercityPlan>();
        if (rolled && RolledIntercityLine(planet) is { } line0)
        {
            lines.Add(line0);
        }

        if (rolled)
        {
            lines.AddRange(ReplayFurtherIntercityLines());
        }

        lines.AddRange(ListedRailLines(planet));
        if (lines.Count > 0)
        {
            BuildIntercityLines(lines, planet, sw);
        }
    }

    /// <summary>Line 0, the rolled line: replayed from its record, or — on a world that never decided — planned by the rolled
    /// planner and pinned (a skip when nothing fits). Null when the world has none.</summary>
    private IntercityPlan? RolledIntercityLine(PlanetType planet)
    {
        var rec = FindPlacementRecord(IntercityLineKind, 0);
        if (rec is not null)
        {
            var replayed = rec.Placed ? ReplayIntercityLine(rec) : null;
            if (rec.Placed && replayed is null)
            {
                _log.Warn($"The intercity line record of '{_world.LocationId}' could not be replayed — the line stays off this load.");
            }

            return replayed;
        }

        if (!_config.PlaceIntercityRail || !StructureKindDue(StructureTypeRules.Intercity)) // #3517: on, generation 19+ as shipped
        {
            return null; // an older world never grows a line; a server that switched the feature off decides nothing
        }

        var plan = DecideIntercityRail(planet);
        if (plan is null)
        {
            RecordPlacementSkip(IntercityLineKind, 0);
            SavePlacementRecords();
            return null;
        }

        PinIntercityPlan(plan);
        SavePlacementRecords();
        return plan;
    }

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
}
