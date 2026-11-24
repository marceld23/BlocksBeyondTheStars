// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A listed rail line's voxels, section by section (#3985). Earth's two lines are about 17,000 blocks of track: stamped
/// whole, the corridor alone would read — generate — every chunk along both lines on the body's first boot. So a listed
/// line is cut into <see cref="IntercitySection"/>s of <see cref="IntercitySectionPylons"/> spans, and a section's
/// stations, pylon columns and corridor are stamped the first time a player on the body comes within their view of it
/// (<see cref="TickRailSections"/>: the player's streaming radius and a few chunks more — a rider on the train is a player
/// too, so a ride stamps the line ahead of it). Each section is its own stamped feature (<c>intercityrail:&lt;line&gt;:&lt;s&gt;</c>,
/// marked after its transaction), so the save says which sections stand. Chunks a player already holds in the section are
/// streamed to them again, so nobody keeps a chunk from before the stamp.
/// <para>The rest of the line does not wait for its voxels: the plan, the records, the graph, the rail line with its spline
/// and the public train exist for the whole line from the first load, and a train runs on its spline, not on blocks — so the
/// public train shuttles end to end on a line nobody has seen yet, and its riders see each section built before it streams
/// to them.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Spans per section: about 145 blocks over land (a pylon every 18), 255 over open sea (every 32). Measured on a
    /// real Earth: all 102 sections of both lines take about 35 s to stamp, one 20–1,200 ms (median 320 ms; its chunks
    /// generated on the tick thread, the deep sea and the city halls the dearest).</summary>
    private const int IntercitySectionPylons = 8;

    /// <summary>Chunks beyond a player's streaming radius at which a section is stamped.</summary>
    private const int IntercitySectionReachChunks = 3;

    /// <summary>How often the sections near the players are looked for (seconds).</summary>
    private const double IntercitySectionCheckSeconds = 0.25;

    /// <summary>At most this many sections are stamped in one tick (the rest come on the next).</summary>
    private const int IntercitySectionsPerTick = 1;

    /// <summary>One section of a listed line: its pylons <see cref="First"/>..<see cref="Last"/> (it stamps the spans
    /// between them and the stations whose hall span is one of them), the box round them and their stations (world X
    /// unwrapped from the first pylon), and whether its voxels stand.</summary>
    internal sealed class IntercitySection
    {
        public int Index;
        public int First;
        public int Last;
        public double MinX, MinZ, MaxX, MaxZ;
        public bool Built;
    }

    private double _railSectionClock;

    /// <summary>The stamp times of the sections built since the server started (milliseconds, a test seam).</summary>
    private readonly List<long> _railSectionMillis = new();

    /// <summary>The stamped-feature key of a listed line's section.</summary>
    private static string IntercitySectionFeature(int line, int section)
        => IntercityFeatureOf(line) + ":" + section.ToString(CultureInfo.InvariantCulture);

    /// <summary>A listed line's sections: every <see cref="IntercitySectionPylons"/> spans, a boundary moved on by one where it
    /// would split a hall, each with its box (its pylons and its stations' footprints, 8 blocks wider) and whether it stands.</summary>
    private List<IntercitySection> IntercitySections(IntercityPlan plan)
    {
        var sections = new List<IntercitySection>();
        int circ = _world.Circumference, n = plan.Pylons.Count;
        double x0 = plan.Pylons[0].X;
        for (int first = 0; first < n - 1;)
        {
            int last = Math.Min(n - 1, first + IntercitySectionPylons);
            while (last < n - 1 && plan.IsStationSpan(last))
            {
                last++; // never split a hall between two sections
            }

            var s = new IntercitySection { Index = sections.Count, First = first, Last = last, MinX = double.MaxValue, MinZ = double.MaxValue, MaxX = double.MinValue, MaxZ = double.MinValue };
            void Grow(double x, double z)
            {
                double ux = x0 + WorldConstants.WrapDeltaX(x - x0, circ);
                (s.MinX, s.MaxX, s.MinZ, s.MaxZ) = (Math.Min(s.MinX, ux), Math.Max(s.MaxX, ux), Math.Min(s.MinZ, z), Math.Max(s.MaxZ, z));
            }

            for (int j = first; j <= last; j++)
            {
                Grow(plan.Pylons[j].X, plan.Pylons[j].Z);
            }

            foreach (var st in plan.Stops.Where(st => plan.Pylons.IndexOf(st.EndPylon) is int a && a >= first && a <= last))
            {
                Grow(st.Origin.X, st.Origin.Z);
                Grow(st.Origin.X + st.W, st.Origin.Z + st.L);
            }

            (s.MinX, s.MinZ, s.MaxX, s.MaxZ) = (s.MinX - 8, s.MinZ - 8, s.MaxX + 8, s.MaxZ + 8);
            s.Built = FeatureStamped(IntercitySectionFeature(plan.LineIndex, s.Index));
            sections.Add(s);
            first = last;
        }

        return sections;
    }

    /// <summary>The horizontal route length of a section's spans (its share of the tree budget).</summary>
    private float IntercitySectionRoute(IntercityPlan plan, int first, int last)
    {
        float route = 0f;
        for (int j = first; j < last; j++)
        {
            float dx = WorldConstants.WrapDeltaX(plan.Pylons[j + 1].X - plan.Pylons[j].X, _world.Circumference);
            float dz = plan.Pylons[j + 1].Z - plan.Pylons[j].Z;
            route += (float)Math.Sqrt((dx * dx) + (dz * dz));
        }

        return route;
    }

    /// <summary>The boot log line of a listed line: its stops, pylons, route, sections (how many stand) and the time so far.</summary>
    private void LogListedRailLine(IntercityPlan line, Stopwatch sw)
    {
        var state = Intercity.Lines.First(l => l.Index == line.LineIndex);
        _log.Info($"Rail line {line.LineIndex.ToString(CultureInfo.InvariantCulture)} ({line.NameKey}) on '{_world.LocationId}': " +
                  $"{string.Join(" – ", line.Stops.Select(s => s.SettlementName))}, {line.Pylons.Count} pylons, route {line.RouteLength:F0} blocks, " +
                  $"{state.Sections.Count(s => s.Built)}/{state.Sections.Count} sections built, planned in {sw.ElapsedMilliseconds} ms.");
    }

    /// <summary>Per world, a few times a second: stamps the sections of the listed lines that a joined player has come near
    /// (at most <see cref="IntercitySectionsPerTick"/> per tick).</summary>
    private void TickRailSections(double dt)
    {
        _railSectionClock += dt;
        if (_railSectionClock < IntercitySectionCheckSeconds || !Intercity.Active)
        {
            return;
        }

        _railSectionClock = 0;
        int budget = IntercitySectionsPerTick;
        foreach (var line in Intercity.Lines)
        {
            foreach (var section in line.Sections)
            {
                if (budget == 0)
                {
                    return;
                }

                if (!section.Built && JoinedInActiveWorld().Any(s => SectionInReach(s, section)))
                {
                    StampIntercitySection(line, section);
                    budget--;
                }
            }
        }
    }

    /// <summary>Whether a player is within their streaming radius and <see cref="IntercitySectionReachChunks"/> chunks more
    /// of a section's box (wrap-aware).</summary>
    private bool SectionInReach(PlayerSession session, IntercitySection section)
    {
        double reach = (EffectiveViewRadius(session) + LoadAheadRings + IntercitySectionReachChunks) * (double)WorldConstants.ChunkSize;
        return SectionGap(section, session.State.Position.X, session.State.Position.Z) <= reach;
    }

    /// <summary>The horizontal distance from a point to a section's box (0 inside; X the short way round the seam).</summary>
    private double SectionGap(IntercitySection section, double x, double z)
    {
        double cx = (section.MinX + section.MaxX) * 0.5;
        double px = cx + WorldConstants.WrapDeltaX(x - cx, _world.Circumference);
        double dx = Math.Max(0, Math.Max(section.MinX - px, px - section.MaxX));
        double dz = Math.Max(0, Math.Max(section.MinZ - z, z - section.MaxZ));
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>Stamps one section in its own transaction, marks it after, and streams its chunks again to every player who
    /// holds one of them.</summary>
    private void StampIntercitySection(IntercityLineState line, IntercitySection section)
    {
        var clock = Stopwatch.StartNew();
        int carved = 0, trees = 0;
        string surface = IntercitySurface(_world.Planet);
        _repo.RunInTransaction(() => (carved, trees) = StampIntercityVoxels(line.Plan, surface, section.First, section.Last));
        MarkFeatureStamped(IntercitySectionFeature(line.Index, section.Index));
        section.Built = true;
        RestreamSection(section);
        _railSectionMillis.Add(clock.ElapsedMilliseconds);
        _log.Info($"Rail line {line.Index.ToString(CultureInfo.InvariantCulture)} on '{_world.LocationId}': section {section.Index.ToString(CultureInfo.InvariantCulture)} " +
                  $"(pylons {section.First.ToString(CultureInfo.InvariantCulture)}–{section.Last.ToString(CultureInfo.InvariantCulture)}) built, " +
                  $"{carved} corridor cells + {trees} tree cells cleared in {clock.ElapsedMilliseconds} ms.");
    }

    /// <summary>Drops every chunk of a section's columns from the players' sent sets: they are streamed again, whole.</summary>
    private void RestreamSection(IntercitySection section)
    {
        int columns = _world.Circumference / WorldConstants.ChunkSize;
        var cols = new HashSet<(int X, int Z)>();
        for (int cx = (int)Math.Floor(section.MinX / WorldConstants.ChunkSize); cx <= (int)Math.Floor(section.MaxX / WorldConstants.ChunkSize); cx++)
        {
            for (int cz = (int)Math.Floor(section.MinZ / WorldConstants.ChunkSize); cz <= (int)Math.Floor(section.MaxZ / WorldConstants.ChunkSize); cz++)
            {
                cols.Add((((cx % columns) + columns) % columns, cz));
            }
        }

        foreach (var session in JoinedInActiveWorld())
        {
            session.Streaming.SentChunks.RemoveWhere(c => cols.Contains((((c.X % columns) + columns) % columns, c.Z)));
        }
    }

    /// <summary>Test seam (#3985): stamps every section of the listed lines that the given point is in reach of (a player's
    /// default view), as <see cref="TickRailSections"/> would; returns how many were stamped.</summary>
    internal int StampRailSectionsNearForTest(Vector3f point, double reachBlocks)
    {
        int stamped = 0;
        foreach (var line in Intercity.Lines)
        {
            foreach (var section in line.Sections.Where(s => !s.Built))
            {
                if (SectionGap(section, point.X, point.Z) <= reachBlocks)
                {
                    StampIntercitySection(line, section);
                    stamped++;
                }
            }
        }

        return stamped;
    }

    /// <summary>Test seam (#3985): each listed line's sections (built or not).</summary>
    internal IReadOnlyList<(int Line, int Section, int First, int Last, bool Built)> RailSectionsForTest
        => Intercity.Lines.SelectMany(l => l.Sections.Select(s => (l.Index, s.Index, s.First, s.Last, s.Built))).ToList();

    /// <summary>Test seam (#3985): the stamp times of the sections built since the start (milliseconds).</summary>
    internal IReadOnlyList<long> RailSectionMillisForTest => _railSectionMillis;

    /// <summary>Test seam (#3985): runs the active world's trains for <paramref name="dt"/> seconds of uptime — the trains of
    /// a world nobody stands on, which the tick leaves alone.</summary>
    internal void RunTrainsForTest(double dt)
    {
        _uptime += dt;
        TickTrains(dt);
    }

    /// <summary>Test seam (#3985): the rail list the clients get.</summary>
    internal Networking.Messages.RailList RailListForTest => RailListMessage();
}
