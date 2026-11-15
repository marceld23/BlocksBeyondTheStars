// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3614: building a giant wreck in space never stalls the tick, and stays small on small hardware. The pure part of the build
/// — the ruined wreck (<see cref="GiantShipGenerator.BuildSpace"/>), the ship-mode decay (<see cref="DerelictGenerator.GenerateShip"/>),
/// the base build made from it and that build's visible shell — takes about a second for a big ship on a desktop (several on a
/// Raspberry Pi); the tick keeps only the cheap registration (the pin, the flight spot, <see cref="RegisterDerivedStation"/>).
/// <list type="bullet">
/// <item><b>Ahead, one at a time.</b> With background threads (<see cref="Shared.Configuration.ServerConfig.ChunkGenWorkers"/> above
/// 0): the start system's wrecks right at the boot, and once a second those of the systems where a player sits aboard their
/// ship (about to launch) or flies — queued, ONE builder thread at a time, below the tick's priority.
/// <see cref="EnsureGiantDerelict"/> takes the finished result and waits only if it is still running (a hyperjump straight into
/// a system's flight); a result nobody took within <see cref="GiantPrebuildKeepSeconds"/> is dropped (it is built again when
/// needed).</item>
/// <item><b>Without threads</b> (0 workers — the in-browser singleplayer — or a platform that cannot start one, the rule
/// <see cref="ChunkGenerationPool.TryStart"/> follows): the start system's wreck is built during the boot (the loading screen
/// covers it), any other at its first need; and the process cache is not used (its memory is not worth it there).</item>
/// <item><b>The process-wide cache</b> (<see cref="GiantWreckBuildCache"/>, with threads only) keeps the last few decayed wrecks by
/// template fingerprint, build seed, ruin and decay revision and the content's block signature: a second server in the same
/// process (a restart, the next test) builds the same wreck without redoing the work.</item>
/// <item><b>The shell</b> is built with the base and handed to the registered structure; the players' stored changes are
/// replayed over it (<see cref="HullShell.Changed"/>) — the first design sent never builds a shell on the tick.</item>
/// </list>
/// The worker touches nothing of the server: it reads the immutable content and template and writes only its job.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two looks at the builds (<see cref="TickGiantDerelictPrebuilds"/>).</summary>
    private const double GiantPrebuildInterval = 1.0;

    /// <summary>A finished background build nobody took is dropped after this many seconds.</summary>
    internal const double GiantPrebuildKeepSeconds = 600.0;

    private double _giantPrebuildAt = double.NegativeInfinity;

    /// <summary>Wrecks waiting for the (one) builder thread, in order.</summary>
    private readonly List<GiantDerelictSite> _giantBuildQueue = new();

    /// <summary>A giant wreck's build on a background thread (null parts until done).</summary>
    private sealed class GiantBuildJob
    {
        public Thread? Thread;
        public GiantBuildParts? Parts;
        public System.Exception? Error;

        /// <summary>Server time the tick first saw it finished (NaN before).</summary>
        public double FinishedAt = double.NaN;
    }

    /// <summary>What the pure part of a giant wreck's build makes: the decayed wreck, a fresh base build of it, and that build's
    /// visible shell.</summary>
    private sealed class GiantBuildParts
    {
        public GiantBuildParts(GiantDerelictLayout wreck, SpaceStructure build, HullShell shell)
        {
            Wreck = wreck;
            Build = build;
            Shell = shell;
        }

        public GiantDerelictLayout Wreck { get; }

        public SpaceStructure Build { get; }

        public HullShell Shell { get; }
    }

    /// <summary>The pure part of a giant wreck's build. Thread-safe: it reads only the template and the content, and the
    /// structure it makes is its own until it is taken. <paramref name="useCache"/>: through the process cache.
    /// <paramref name="abandonBeds"/>: the world's generation builds the wreck's beds as the abandoned twin (44).</summary>
    private static GiantBuildParts BuildGiantWreckParts(StructureTemplate t, long seed, GameContent content, string name, bool useCache,
        bool abandonBeds)
    {
        var wreck = GiantWreckBuildCache.Build(t, seed, content, useCache, abandonBeds);
        var build = GiantBaseBuild(name, wreck.Layout);
        var (min, max) = build.CellBox();
        var shell = HullShell.Build(min, max, build.Cells.Select(kv => new KeyValuePair<Vector3i, ushort>(kv.Key, kv.Value.Value)),
            p => build.Get(p).Value, p => build.Shapes.TryGetValue(p, out int v) ? v : 0, StationHull.SeeThroughIds(content)); // = EnsureShell
        return new GiantBuildParts(wreck, build, shell);
    }

    private bool GiantBuildThreads => _config.ChunkGenWorkers > 0;

    /// <summary>Queues the background build of every giant wreck of <paramref name="systemId"/> that is neither built nor being
    /// built, and starts the next one if the builder is free — nothing on a server without background threads.</summary>
    private void PrebuildGiantDerelicts(string? systemId)
    {
        if (systemId is null || !GiantBuildThreads || _giantDerelicts.Count == 0)
        {
            return;
        }

        foreach (var site in _giantDerelicts.Values.Where(s => s.SystemId == systemId).OrderBy(s => s.Index))
        {
            if (!site.Built && !site.Retired && site.Job is null && !_giantBuildQueue.Contains(site))
            {
                _giantBuildQueue.Add(site);
            }
        }

        PumpGiantBuilds();
    }

    /// <summary>Starts the next queued build when no builder thread runs (one at a time).</summary>
    private void PumpGiantBuilds()
    {
        if (_giantDerelicts.Values.Any(s => s.Job?.Thread is { IsAlive: true }))
        {
            return;
        }

        while (_giantBuildQueue.Count > 0)
        {
            var site = _giantBuildQueue[0];
            _giantBuildQueue.RemoveAt(0);
            if (!site.Built && !site.Retired && site.Job is null)
            {
                StartGiantBuild(site);
                return;
            }
        }
    }

    private void StartGiantBuild(GiantDerelictSite site)
    {
        var job = new GiantBuildJob();
        var template = site.Template;
        long seed = GiantWreckPlacer.BuildSeed(_meta.Seed, site.Id);
        var content = _content;
        string name = site.Body.Name;
        bool abandonBeds = GiantWreckBedsAbandoned;
        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    job.Parts = BuildGiantWreckParts(template, seed, content, name, useCache: true, abandonBeds);
                }
                catch (System.Exception e)
                {
                    job.Error = e; // the tick builds it itself and logs why
                }
            })
            {
                IsBackground = true,
                Name = "giant-wreck-build",
                Priority = ThreadPriority.BelowNormal, // the tick (and a client on the same machine) come first
            };
            thread.Start();
            job.Thread = thread;
            site.Job = job;
        }
        catch (System.Exception)
        {
            // No threads on this platform: the wreck is built inline at its first need, as before.
        }
    }

    /// <summary>The finished build of a giant wreck: the background one (waiting only while it still runs), else built here.
    /// <paramref name="how"/> says which, for the log.</summary>
    private GiantBuildParts TakeGiantBuild(GiantDerelictSite site, out string how)
    {
        var job = site.Job;
        site.Job = null;
        _giantBuildQueue.Remove(site);
        if (job?.Thread is { } thread)
        {
            how = thread.IsAlive ? "waited for its background build" : "built ahead in the background";
            thread.Join();
            if (job.Error is null && job.Parts is { } parts)
            {
                return parts;
            }

            _log.Warn($"Giant wreck '{site.Id}': its background build failed ({job.Error?.Message}) — building it here.");
        }

        how = "built inline";
        return BuildGiantWreckParts(site.Template, GiantWreckPlacer.BuildSeed(_meta.Seed, site.Id), _content, site.Body.Name,
            useCache: GiantBuildThreads, GiantWreckBedsAbandoned);
    }

    /// <summary>Whether this world builds a giant wreck's beds as the abandoned twin: a save of terrain generation 44 or later.
    /// The galaxy pass reaches older saves too — their wrecks keep the beds they were built with, so a stored wreck's base never
    /// changes under its changes.</summary>
    private bool GiantWreckBedsAbandoned => AbandonedBeds.Applies(_meta.Description.TerrainGeneration);

    /// <summary>Hands the prebuilt shell to a freshly registered giant wreck and replays the stored changes over it (the
    /// players' crates, holes, patches — the shell only ever grows, as it does live), so the first design send builds no
    /// shell on the tick.</summary>
    private void AdoptGiantShell(string id, SpaceStructure build, HullShell shell)
    {
        build.Shell = shell;
        if (_derivedStations.TryGetValue(id, out var d) && d.Delta.Count > 0)
        {
            var scratch = new List<Vector3i>();
            foreach (var cell in d.Delta.Keys)
            {
                shell.Changed(cell, scratch);
                scratch.Clear();
            }
        }

        build.LaidOpen.Clear();
        build.LaidOpenOverflow = false;
    }

    /// <summary>Once a second (from the tick): drops a finished build nobody took for <see cref="GiantPrebuildKeepSeconds"/>,
    /// queues the wrecks of the systems where a player sits aboard their ship (about to launch) or flies, and keeps the one
    /// builder going.</summary>
    private void TickGiantDerelictPrebuilds()
    {
        if (_giantDerelicts.Count == 0 || !GiantBuildThreads || _uptime < _giantPrebuildAt)
        {
            return;
        }

        _giantPrebuildAt = _uptime + GiantPrebuildInterval;
        foreach (var site in _giantDerelicts.Values.Where(s => s.Job is { Thread.IsAlive: false }).ToList())
        {
            var job = site.Job!;
            job.FinishedAt = double.IsNaN(job.FinishedAt) ? _uptime : job.FinishedAt;
            if (_uptime - job.FinishedAt > GiantPrebuildKeepSeconds)
            {
                site.Job = null; // nobody came: built again when needed
                _log.Info($"Giant wreck '{site.Id}': its build ahead was not needed — dropped.");
            }
        }

        foreach (var session in _sessions.Values)
        {
            if (session.Joined && (session.State.AboardShip || InSpace(session.State.PlayerId))
                && ResolveLocationBody(session.CurrentLocationId) is { } body)
            {
                PrebuildGiantDerelicts(body.SystemId);
            }
        }

        PumpGiantBuilds();
    }

    /// <summary>Test seam (#3614): whether a giant wreck's background build was started and has not been taken yet.</summary>
    internal bool GiantBuildPendingForTest(string id) => _giantDerelicts.TryGetValue(id, out var site) && site.Job is not null;

    /// <summary>Test seam (#3614): the wrecks waiting for the builder thread, and whether one is running.</summary>
    internal (int Queued, int Running) GiantBuildQueueForTest()
        => (_giantBuildQueue.Count, _giantDerelicts.Values.Count(s => s.Job?.Thread is { IsAlive: true }));

    /// <summary>Test seam (#3614): ages every finished, untaken build past <see cref="GiantPrebuildKeepSeconds"/> and runs the
    /// prebuild tick.</summary>
    internal void AgeGiantBuildsForTest()
    {
        foreach (var job in _giantDerelicts.Values.Select(s => s.Job).OfType<GiantBuildJob>())
        {
            job.Thread?.Join();
            job.FinishedAt = _uptime - GiantPrebuildKeepSeconds - 1.0;
        }

        _giantPrebuildAt = double.NegativeInfinity;
        TickGiantDerelictPrebuilds();
    }
}

/// <summary>
/// #3614: the process-wide cache of giant wreck builds — the decayed wreck (<see cref="GiantDerelictLayout"/>, read-only once
/// built: nothing ever writes into it) by template fingerprint, build seed, ruin and decay revision and the content's block
/// signature, the last <see cref="Capacity"/> of them. A server builds each wreck once per run anyway; the cache spares a second
/// server in the same process (a restart, the next test) the work. Only used where the server runs background threads.
/// Thread-safe.
/// </summary>
internal static class GiantWreckBuildCache
{
    /// <summary>How many builds are kept (a big ship's is a few MB).</summary>
    internal const int Capacity = 4;

    private static readonly Lock Gate = new();
    private static readonly LinkedList<KeyValuePair<string, GiantDerelictLayout>> Entries = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameContent, string> Signatures = new();
    private static long s_hits;
    private static long s_misses;

    /// <summary>Builds served from the cache / built afresh (diagnostics and tests).</summary>
    internal static long Hits => Interlocked.Read(ref s_hits);

    internal static long Misses => Interlocked.Read(ref s_misses);

    /// <summary>The decayed wreck of <paramref name="t"/> for <paramref name="seed"/> — from the cache when it holds it (and
    /// <paramref name="useCache"/>), else built (and kept, with <paramref name="useCache"/>).</summary>
    internal static GiantDerelictLayout Build(StructureTemplate t, long seed, GameContent content, bool useCache = true,
        bool abandonBeds = false)
    {
        if (!useCache)
        {
            return BuildFresh(t, seed, content, abandonBeds);
        }

        string key = KeyFor(t, seed, content, abandonBeds);
        lock (Gate)
        {
            for (var node = Entries.First; node is not null; node = node.Next)
            {
                if (node.Value.Key == key)
                {
                    Entries.Remove(node);
                    Entries.AddFirst(node); // most recently used first
                    Interlocked.Increment(ref s_hits);
                    return node.Value.Value;
                }
            }
        }

        Interlocked.Increment(ref s_misses);
        var wreck = BuildFresh(t, seed, content, abandonBeds);
        lock (Gate)
        {
            Entries.AddFirst(new KeyValuePair<string, GiantDerelictLayout>(key, wreck));
            while (Entries.Count > Capacity)
            {
                Entries.RemoveLast();
            }
        }

        return wreck;
    }

    private static GiantDerelictLayout BuildFresh(StructureTemplate t, long seed, GameContent content, bool abandonBeds)
    {
        var space = GiantShipGenerator.BuildSpace(t, seed, content, abandonBeds);
        return DerelictGenerator.GenerateShip(space, seed, content, GiantShipGenerator.IsAlien(t));
    }

    /// <summary>The cache key: what the build is a pure function of.</summary>
    internal static string KeyFor(StructureTemplate t, long seed, GameContent content, bool abandonBeds = false)
        => GiantShipGenerator.Fingerprint(t) + "|" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + (abandonBeds ? "|beds" : string.Empty)
           + "|r" + GiantShipGenerator.RuinRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + "|d" + DerelictGenerator.ShipDecayRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + "|" + Signature(content);

    /// <summary>Whether the cache holds a build under <paramref name="key"/> (tests).</summary>
    internal static bool Holds(string key)
    {
        lock (Gate)
        {
            return Entries.Any(e => e.Key == key);
        }
    }

    /// <summary>How many builds the cache holds now (tests).</summary>
    internal static int Count
    {
        get
        {
            lock (Gate)
            {
                return Entries.Count;
            }
        }
    }

    /// <summary>The content's block signature — every block's key, id and the properties the ruin and the decay read — once
    /// per content instance.</summary>
    private static string Signature(GameContent content) => Signatures.GetValue(content, ComputeSignature);

    private static string ComputeSignature(GameContent content)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in content.Blocks.Values.OrderBy(b => b.NumericId.Value))
        {
            sb.Append(b.Key).Append(':').Append(b.NumericId.Value).Append(':').Append(b.Category).Append(':')
                .Append(b.Solid ? 'S' : 's').Append(b.Liquid ? 'L' : 'l').Append(b.Airtight ? 'A' : 'a')
                .Append(b.Emission?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-").Append(';');
        }

        return Shared.World.StableHashing.Fnv64(sb.ToString()).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }
}
