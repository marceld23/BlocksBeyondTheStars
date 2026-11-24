// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A listed rail line's sections stamped by the tick without freezing it (#3985). Stamped in one go a section took a median
/// 320 ms and up to 1.2 s of one tick — its chunks generated on the tick thread. Now, a few times a second, the nearest
/// section a player has come near is taken up (one at a time) and stamped over the ticks that follow:
/// <list type="number">
/// <item><b>Its chunks first.</b> Every chunk its stations, pylon columns and corridor touch — the corridor's band with a
/// tree's reach round it over land, the corridor alone over water — is asked of the background chunk pool
/// (<see cref="ChunkGenerationPool.Prefetch"/>) and adopted once a worker has it, under the tick's budget. Without
/// background threads (the in-browser singleplayer) the chunks are generated on the tick, one or more per tick within the
/// budget. The chunks are pinned against the far-chunk sweep until the section stands.</item>
/// <item><b>Then its cells</b>, <see cref="IntercitySectionBudgetMs"/> per tick (the far tiles' way), each tick's slice in
/// its own transaction: a station hall worked out in one tick and written over the next, pylon by pylon, a few corridor
/// cells at a time. A step whose chunk is not resident (evicted after all, or outside the band) waits for it — the stamp
/// never writes into an unloaded chunk (a tree the corridor takes out may reach a little past the band; such a cell's chunk
/// is loaded on the spot).</item>
/// <item><b>Done</b> when its last cell is written: only then, on the next tick, is the section marked built (its feature),
/// streamed again and the next one free to be taken up. A restart in between finds the section unmarked and stamps it again
/// from its start — the same cells again.</item>
/// </list>
/// Measured on a real Earth (<c>EarthRailNetworkTests</c>, development machine): the longest tick 10–12 ms (the first
/// section of a server, its code still cold), else 4–8 ms; a section's whole stamp about 20–45 ms of tick time.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Wall-clock milliseconds a tick spends at most on the section being stamped (a step started is finished).</summary>
    private const double IntercitySectionBudgetMs = 4.0;

    /// <summary>Test seam: a smaller per-tick budget than <see cref="IntercitySectionBudgetMs"/> (null = the shipped one), so
    /// a test can catch a section half-written on a machine fast enough to stamp a short section in one tick.</summary>
    internal double? SectionBudgetMsForTest { get; set; }

    /// <summary>Blocks between two samples of a span's chunk band.</summary>
    private const int IntercityBandSampleStep = 4;

    /// <summary>A sample's reach sideways over water: the wagon's half width and the way to the next sample.</summary>
    private const int IntercityBandWetMargin = 3;

    /// <summary>The section the tick stamps: the stamp's work, every chunk it touches (pinned) and those not resident yet,
    /// and what it cost the tick.</summary>
    internal sealed class RailSectionStamp
    {
        public RailSectionStamp(IntercityLineState line, IntercitySection section, IntercityStampWork work, HashSet<ChunkCoord> chunks)
        {
            Line = line;
            Section = section;
            Work = work;
            Chunks = chunks;
        }

        public IntercityLineState Line { get; }
        public IntercitySection Section { get; }
        public IntercityStampWork Work { get; }
        public HashSet<ChunkCoord> Chunks { get; }
        public HashSet<ChunkCoord> Missing { get; } = new();
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public int Ticks { get; set; }
        public double TickMillis { get; set; }
        public double LongestSliceMillis { get; set; }
    }

    private readonly List<ChunkCoord> _railChunkScratch = new();

    /// <summary>Per world, every tick: a few times a second takes up the nearest section a joined player has come near (when
    /// none is being stamped), then stamps on at the one being stamped.</summary>
    private void TickRailSections(double dt)
    {
        if (Intercity.Active)
        {
            TickRailSections(dt, () => JoinedInActiveWorld().Select(s => ((double)s.State.Position.X, (double)s.State.Position.Z, SectionReach(s))).ToList());
        }
    }

    private void TickRailSections(double dt, Func<IReadOnlyList<(double X, double Z, double Reach)>> players)
    {
        var ic = Intercity;
        _railSectionClock += dt;
        if (ic.SectionStamp is null && _railSectionClock >= IntercitySectionCheckSeconds)
        {
            _railSectionClock = 0;
            if (NearestSectionInReach(players()) is { } near)
            {
                ic.SectionStamp = BeginSectionStamp(near.Line, near.Section);
                return; // its first slice on the next tick
            }
        }

        if (ic.SectionStamp is { } stamp)
        {
            AdvanceSectionStamp(stamp);
        }
    }

    /// <summary>A player's streaming radius and <see cref="IntercitySectionReachChunks"/> chunks more (blocks).</summary>
    private double SectionReach(PlayerSession session)
        => (EffectiveViewRadius(session) + LoadAheadRings + IntercitySectionReachChunks) * (double)WorldConstants.ChunkSize;

    /// <summary>The section not built yet that is nearest to a player within their reach of its box, or null.</summary>
    private (IntercityLineState Line, IntercitySection Section)? NearestSectionInReach(IReadOnlyList<(double X, double Z, double Reach)> players)
    {
        (IntercityLineState, IntercitySection)? best = null;
        double bestGap = double.MaxValue;
        foreach (var line in Intercity.Lines)
        {
            foreach (var section in line.Sections.Where(s => !s.Built))
            {
                foreach (var (x, z, reach) in players)
                {
                    double gap = SectionGap(section, x, z);
                    if (gap <= reach && gap < bestGap)
                    {
                        (best, bestGap) = ((line, section), gap);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Takes a section up: its chunk set, and the chunks of it that are not resident asked of the pool.</summary>
    private RailSectionStamp BeginSectionStamp(IntercityLineState line, IntercitySection section)
    {
        var work = NewIntercityStampWork(line.Plan, IntercitySurface(_world.Planet), section.First, section.Last);
        var stamp = new RailSectionStamp(line, section, work, IntercitySectionChunks(line.Plan, section.First, section.Last));
        foreach (var chunk in stamp.Chunks.Where(c => !_world.IsChunkLoaded(c)))
        {
            stamp.Missing.Add(chunk);
        }

        ChunkGenPool()?.Prefetch(_world, stamp.Missing);
        return stamp;
    }

    /// <summary>One tick's share of a section's stamp: its chunks adopted, then its cells written, within the budget — or,
    /// on the tick after its last cell, its finish (marked, streamed again, the next section free to come).</summary>
    private void AdvanceSectionStamp(RailSectionStamp stamp)
    {
        if (stamp.Work.Done)
        {
            Intercity.SectionStamp = null;
            FinishIntercitySection(stamp.Line, stamp.Section, (long)Math.Round(stamp.TickMillis),
                $"{stamp.Work.Carved} corridor cells + {stamp.Work.Trees} tree cells cleared over {stamp.Ticks.ToString(CultureInfo.InvariantCulture)} ticks " +
                $"in {stamp.Clock.Elapsed.TotalSeconds:F1} s ({stamp.Chunks.Count.ToString(CultureInfo.InvariantCulture)} chunks; {stamp.TickMillis:F0} ms on the tick, " +
                $"the longest tick {stamp.LongestSliceMillis:F1} ms)");
            return;
        }

        long start = Stopwatch.GetTimestamp();
        long deadline = start + (long)((SectionBudgetMsForTest ?? IntercitySectionBudgetMs) * Stopwatch.Frequency / 1000.0);
        if (LoadSectionChunks(stamp, deadline))
        {
            WriteSectionCells(stamp, deadline);
        }

        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        stamp.Ticks++;
        stamp.TickMillis += ms;
        stamp.LongestSliceMillis = Math.Max(stamp.LongestSliceMillis, ms);
    }

    /// <summary>Adopts what the pool finished of a section's missing chunks (or, without the pool, generates them here) until
    /// the deadline, and asks again for the rest; true when all are resident and time is left to write.</summary>
    private bool LoadSectionChunks(RailSectionStamp stamp, long deadline)
    {
        if (stamp.Missing.Count == 0)
        {
            return true;
        }

        var pool = ChunkGenPool();
        var scratch = _railChunkScratch;
        scratch.Clear();
        scratch.AddRange(stamp.Missing);
        foreach (var chunk in scratch)
        {
            if (_world.IsChunkLoaded(chunk) || pool?.TryAdopt(_world, chunk) == true)
            {
                stamp.Missing.Remove(chunk);
            }
            else if (pool is null)
            {
                _world.GetOrLoadChunk(chunk); // no background threads: generated here, as many as the budget holds
                stamp.Missing.Remove(chunk);
            }
            else
            {
                continue; // still on a worker
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                break;
            }
        }

        if (stamp.Missing.Count > 0)
        {
            pool?.Prefetch(_world, stamp.Missing); // anything the pool dropped meanwhile is asked for again
            return false;
        }

        return Stopwatch.GetTimestamp() < deadline;
    }

    /// <summary>Writes a section's cells in one transaction until the deadline; a step that needs a chunk that is not resident
    /// puts it on the missing list (pinned, asked of the pool) and waits for it.</summary>
    private void WriteSectionCells(RailSectionStamp stamp, long deadline)
    {
        var missing = _railChunkScratch;
        missing.Clear();
        _repo.RunInTransaction(() => AdvanceIntercityStamp(stamp.Work, deadline, missing));
        if (missing.Count > 0)
        {
            stamp.Chunks.UnionWith(missing);
            stamp.Missing.UnionWith(missing);
            ChunkGenPool()?.Prefetch(_world, missing);
        }
    }

    /// <summary>The chunks (canonical) a section's stamp touches: its stations', its free pylon columns' and its spans' bands.</summary>
    private HashSet<ChunkCoord> IntercitySectionChunks(IntercityPlan plan, int first, int last)
    {
        var chunks = new HashSet<ChunkCoord>();
        foreach (var st in plan.Stops.Where(st => StampsWithStretch(plan, st, first, last)))
        {
            chunks.UnionWith(IntercityStationChunks(st));
        }

        for (int j = first; j <= last; j++)
        {
            if (!plan.IsStationPylon(j))
            {
                var top = plan.Pylons[j];
                chunks.UnionWith(ColumnChunks(top.X, top.Z, Math.Min(top.Y, PylonFoot(_world.Planet, top.X, top.Z)), top.Y));
            }
        }

        for (int j = first; j < last; j++)
        {
            AddSpanBand(chunks, plan.Pylons[j], plan.Pylons[j + 1]);
        }

        return chunks;
    }

    /// <summary>The chunks of one span's corridor, sampled every <see cref="IntercityBandSampleStep"/> blocks along it: over
    /// land the corridor with a tree's reach round it (a tree it meets goes as a whole), over water the corridor alone.</summary>
    private void AddSpanBand(HashSet<ChunkCoord> chunks, Vector3i a, Vector3i b)
    {
        var planet = _world.Planet;
        var ta = PylonTop(a);
        var tb = PylonTop(b);
        float dx = (float)WorldConstants.WrapDeltaX((double)(tb.X - ta.X), _world.Circumference);
        float dy = tb.Y - ta.Y, dz = tb.Z - ta.Z;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / IntercityBandSampleStep));
        int wagon = (int)Math.Ceiling(RailRules.WagonHeight) + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = (int)Math.Floor(ta.X + dx * t), y = (int)Math.Floor(ta.Y + dy * t + RailRules.HoverHeight), z = (int)Math.Floor(ta.Z + dz * t);
            bool wet = _generator.TryGetWaterSurface(planet, x, z, out _, out _);
            int side = wet ? IntercityBandWetMargin : IntercityBandWetMargin + IntercityTreeReach + 1;
            int rise = wet ? 1 : IntercityTreeRise + 2;
            chunks.UnionWith(BoxChunks(x - side, x + side, y - rise, y + wagon + rise, z - side, z + side));
        }
    }

    /// <summary>The far-chunk sweep's pinned chunks: the anchored ones and, while a section is stamped, all of its chunks.</summary>
    private ISet<ChunkCoord> PinnedChunks(HashSet<ChunkCoord> anchored)
    {
        if (Intercity.SectionStamp is not { } stamp)
        {
            return anchored;
        }

        var pinned = new HashSet<ChunkCoord>(anchored);
        pinned.UnionWith(stamp.Chunks);
        return pinned;
    }

    /// <summary>Test seam (#3985): one tick of the sections as a player at <paramref name="point"/> with the given reach would
    /// drive it; returns whether a section is being stamped after it and how many in reach are not built yet.</summary>
    internal (bool Busy, int Unbuilt) TickRailSectionsForTest(Vector3f point, double reachBlocks, double dt)
    {
        TickRailSections(dt, () => new[] { ((double)point.X, (double)point.Z, reachBlocks) });
        int unbuilt = Intercity.Lines.Sum(l => l.Sections.Count(s => !s.Built && SectionGap(s, point.X, point.Z) <= reachBlocks));
        return (Intercity.SectionStamp is not null, unbuilt);
    }

    /// <summary>Test seam (#3985): the section the tick is stamping — its line, index, step and missing chunks — or null.</summary>
    internal (int Line, int Section, IntercityStampStep Step, int Missing)? RailSectionStampForTest
        => Intercity.SectionStamp is { } s ? (s.Line.Index, s.Section.Index, s.Work.Step, s.Missing.Count) : null;
}
