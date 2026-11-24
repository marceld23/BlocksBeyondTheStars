// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Threading;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #1817: generates first-visit chunks on background threads. Minecraft moved world generation off its tick
/// thread; here the tick keeps its exact per-pass output (the same chunks, in the same order — every streaming
/// test and every client sees what it saw before) and only the <i>work</i> moves:
/// <list type="bullet">
/// <item><see cref="EnsureGenerated"/> takes a batch the streamer is about to send and makes every chunk of it
/// resident — adopting chunks a worker already finished, generating the rest in parallel on the workers AND the
/// calling thread, and waiting for those in flight.</item>
/// <item><see cref="Speculate"/> queues the next chunks of a view behind the batch, so by the next pass they are
/// usually ready and adopting them costs only the persisted-edit lookup.</item>
/// <item><see cref="Prefetch"/> queues chunks a stamper will write behind both (#3985: a rail section), kept until a worker
/// takes them; the stamper adopts each with <see cref="TryAdopt"/> and writes only once all are resident.</item>
/// </list>
/// Every worker owns its own <see cref="WorldGenerator"/> (a sibling of the server's): no mutable generator state is
/// shared between threads, and the generator's static caches are lock-guarded. The repository is never touched off
/// the tick thread — persisted edits are applied when a chunk is adopted (<see cref="ServerWorld.AdoptGenerated"/>).
/// </summary>
internal sealed class ChunkGenerationPool : IDisposable
{
    /// <summary>A result nobody adopted is dropped after this many streaming passes (the player turned away).</summary>
    private const int ReadyTtlPasses = 150;

    /// <summary>How long the tick waits for a worker's share of a batch before generating it itself.</summary>
    private const int WorkerStallMs = 10_000;

    /// <summary>Upper bound of speculative jobs queued at once, per worker.</summary>
    private const int SpeculativePerWorker = 6;

    /// <summary>A job's queue: the batch the tick waits for, the views' next chunks, the chunks asked for ahead (in that
    /// order of the workers' choice).</summary>
    private enum Lane
    {
        Priority,
        Speculative,
        Background,
    }

    private sealed class Job
    {
        public ServerWorld World = null!;
        public ChunkCoord Coord;
        public PlanetType Planet = null!;
        public int Circumference;
        public bool Cratered;
        public LandingPadFlatten[] Pads = Array.Empty<LandingPadFlatten>();
        public SiteFlat[] Sites = Array.Empty<SiteFlat>(); // generation 40 (#3569)
        public string LocationId = string.Empty;
        public double OreBoost;
        public Lane Lane;
        public bool Claimed;   // taken by the tick thread (inline) or a worker
        public bool Done;
        public ChunkData? Chunk;
        public Exception? Error;
        public long ReadyAtPass;
    }

    /// <summary>All a worker thread may reach: the two queues and the generator template — never the pool (#3799).
    /// A running thread is a GC root. While the threads ran a method of the pool they held <see cref="_jobs"/>, every
    /// finished job its <see cref="ServerWorld"/>, and the world's block events point back at the server: a server
    /// that was dropped without <c>Stop()</c> — about half of the test suite does that — stayed alive whole, threads,
    /// generators, worlds and chunks, and the test host grew by 1.5–2 GB a minute. Finished jobs are known to the pool
    /// only, so once the workers have drained the queues nothing here leads to a world.</summary>
    private sealed class Crew
    {
        public readonly object Lock = new();
        public readonly LinkedList<Job> Priority = new();
        public readonly LinkedList<Job> Speculative = new();
        public readonly LinkedList<Job> Background = new();
        public readonly WorldGenerator Template;

        /// <summary>The streaming pass the pool is in (guarded by <see cref="Lock"/>); stamps a finished job.</summary>
        public long Pass;

        /// <summary>Set once, under <see cref="Lock"/>: the workers leave.</summary>
        public bool Stopped;

        public Crew(WorldGenerator template) => Template = template;

        /// <summary>A worker thread's body.</summary>
        public void Run()
        {
            var generator = Template.CreateSibling();
            while (RunOne(ref generator))
            {
            }
        }

        /// <summary>Waits for a job, generates it and reports it; false once the crew is stopped. A method of its own
        /// and never inlined: while a thread waits, no frame of it may still name the job it finished last (a Debug
        /// build keeps a local to the end of its method) — that job names a world.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private bool RunOne(ref WorldGenerator generator)
        {
            Job job;
            lock (Lock)
            {
                while (!Stopped && Priority.Count == 0 && Speculative.Count == 0 && Background.Count == 0)
                {
                    Monitor.Wait(Lock);
                }

                if (Stopped)
                {
                    return false;
                }

                var list = Priority.Count > 0 ? Priority : Speculative.Count > 0 ? Speculative : Background;
                job = list.First!.Value;
                list.RemoveFirst();
                job.Claimed = true;
            }

            try
            {
                if (!generator.SharesGlobalSettingsWith(Template))
                {
                    generator = Template.CreateSibling(); // a setter ran on the server's generator after start
                }

                generator.SetWorldMode(job.Circumference, job.Cratered, job.Pads, job.LocationId, job.OreBoost, job.Sites);
                job.Chunk = generator.Generate(job.Planet, job.Coord);
            }
            catch (Exception e)
            {
                job.Error = e;
            }

            lock (Lock)
            {
                job.Done = true;
                job.ReadyAtPass = Pass;
                Monitor.PulseAll(Lock);
            }

            return true;
        }

        public void Stop()
        {
            lock (Lock)
            {
                Stopped = true;
                Monitor.PulseAll(Lock);
            }
        }
    }

    /// <summary>Stops the crew when the pool is collected without <see cref="ChunkGenerationPool.Dispose"/> — its
    /// server was dropped, not stopped (#3799). An object of its own on purpose: a finalizer keeps everything it can
    /// reach for one more collection, and that has to be the crew, not the pool with its finished jobs and their
    /// worlds.</summary>
    private sealed class DropGuard : IDisposable
    {
        private readonly Crew _crew;

        public DropGuard(Crew crew) => _crew = crew;

#pragma warning disable MA0055 // a finalizer on purpose: only the collector knows that the pool was dropped
        ~DropGuard() => _crew.Stop();
#pragma warning restore MA0055

        public void Dispose()
        {
            _crew.Stop();
            GC.SuppressFinalize(this);
        }
    }

    private readonly Crew _crew;
    private readonly DropGuard _guard;
    private readonly object _lock;                 // the crew's lock: every field below changes under it
    private readonly LinkedList<Job> _priority;    // the crew's queues, filled here and emptied by the workers
    private readonly LinkedList<Job> _speculative;
    private readonly LinkedList<Job> _background;
    private readonly Dictionary<(ServerWorld World, ChunkCoord Coord), Job> _jobs = new();
    private readonly List<Thread> _threads = new();
    private readonly int _workers;

    /// <summary>Chunks adopted from a worker vs generated on the calling thread (diagnostics + tests).</summary>
    public long AdoptedFromWorkers { get; private set; }
    public long GeneratedInline { get; private set; }

    public int Workers => _workers;

    /// <summary>The worker threads (tests, #3799).</summary>
    internal Thread[] ThreadsForTest => _threads.ToArray();

    private ChunkGenerationPool(WorldGenerator template, int workers)
    {
        _crew = new Crew(template);
        _guard = new DropGuard(_crew);
        _lock = _crew.Lock;
        _priority = _crew.Priority;
        _speculative = _crew.Speculative;
        _background = _crew.Background;
        _workers = workers;
    }

    /// <summary>Starts a pool, or returns null when threads are unavailable (0 workers, or a platform that cannot
    /// start one — the in-browser build): the caller then keeps the inline path.</summary>
    public static ChunkGenerationPool? TryStart(WorldGenerator template, int workers)
    {
        if (workers <= 0)
        {
            return null;
        }

        var pool = new ChunkGenerationPool(template, workers);
        try
        {
            for (int i = 0; i < workers; i++)
            {
                var thread = new Thread(pool._crew.Run) // the crew, not the pool: see Crew
                {
                    IsBackground = true,
                    Name = $"chunk-gen-{i}",
                    Priority = ThreadPriority.BelowNormal, // the tick thread (and a client on the same machine) come first
                };
                thread.Start();
                pool._threads.Add(thread);
            }
        }
        catch (Exception)
        {
            pool.Dispose();
            return null; // no threads on this platform — inline generation stays
        }

        return pool;
    }

    /// <summary>Advances the pass counter and drops stale results. Call once per streaming pass.</summary>
    public void BeginPass()
    {
        lock (_lock)
        {
            _crew.Pass++;
            if (_jobs.Count == 0)
            {
                return;
            }

            List<(ServerWorld, ChunkCoord)>? stale = null;
            foreach (var kv in _jobs)
            {
                if (kv.Value.Done && _crew.Pass - kv.Value.ReadyAtPass > ReadyTtlPasses)
                {
                    (stale ??= new List<(ServerWorld, ChunkCoord)>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                foreach (var key in stale)
                {
                    _jobs.Remove(key);
                }
            }
        }
    }

    /// <summary>Makes every chunk of <paramref name="coords"/> resident in <paramref name="world"/>: adopts finished
    /// results, generates the missing ones in parallel (workers + this thread) and waits for the ones in flight.
    /// Tick thread only. Chunk order and content are exactly what inline generation would give.</summary>
    public void EnsureGenerated(ServerWorld world, IReadOnlyList<ChunkCoord> coords)
    {
        List<Job>? waiting = null;
        List<Job>? inline = null;
        lock (_lock)
        {
            foreach (var raw in coords)
            {
                var coord = WorldConstants.CanonicalChunk(raw, world.Circumference);
                if (world.IsChunkLoaded(coord))
                {
                    continue;
                }

                if (_jobs.TryGetValue((world, coord), out var job))
                {
                    if (!job.Claimed)
                    {
                        // Still queued (speculative, most likely): promote it — the tick needs it now.
                        Unlink(job);
                        job.Lane = Lane.Priority;
                        _priority.AddFirst(job);
                    }

                    (waiting ??= new List<Job>()).Add(job);
                    continue;
                }

                var fresh = NewJob(world, coord, Lane.Priority);
                _jobs[(world, coord)] = fresh;
                _priority.AddLast(fresh);
                (waiting ??= new List<Job>()).Add(fresh);
            }

            if (waiting == null)
            {
                return;
            }

            Monitor.PulseAll(_lock);
        }

        // Help: claim any job of this batch no worker has started yet and generate it here, on the shared generator
        // through the ordinary inline path (which also applies edits and caches the chunk).
        foreach (var job in waiting)
        {
            bool mine = false;
            lock (_lock)
            {
                if (!job.Claimed)
                {
                    job.Claimed = true;
                    Unlink(job);
                    mine = true;
                }
            }

            if (mine)
            {
                (inline ??= new List<Job>()).Add(job);
                job.World.GetOrLoadChunk(job.Coord); // throws like the inline path always did
                lock (_lock)
                {
                    job.Done = true;
                    _jobs.Remove((job.World, job.Coord));
                }

                GeneratedInline++;
            }
        }

        // Wait for the workers' share of the batch, then adopt it.
        foreach (var job in waiting)
        {
            if (inline != null && inline.Contains(job))
            {
                continue;
            }

            bool timedOut = false;
            lock (_lock)
            {
                var started = System.Diagnostics.Stopwatch.StartNew();
                while (!job.Done && !_crew.Stopped)
                {
                    if (started.ElapsedMilliseconds > WorkerStallMs)
                    {
                        timedOut = true; // a worker that never answers must not hang the tick — generate it here
                        break;
                    }

                    Monitor.Wait(_lock, 250);
                }

                _jobs.Remove((job.World, job.Coord));
            }

            if (timedOut)
            {
                job.World.GetOrLoadChunk(job.Coord);
                GeneratedInline++;
                continue;
            }

            Adopt(job);
        }
    }

    /// <summary>Queues <paramref name="coords"/> (the view's next chunks, nearest first) for background generation.
    /// Replaces the speculative queue: jobs no worker started yet are dropped, so a player who turns around never
    /// leaves a backlog of chunks behind them. Tick thread only.</summary>
    public void Speculate(ServerWorld world, IReadOnlyList<ChunkCoord> coords)
    {
        lock (_lock)
        {
            for (var node = _speculative.First; node != null;)
            {
                var next = node.Next;
                if (!node.Value.Claimed && node.Value.World == world) // other worlds tick their own passes
                {
                    _jobs.Remove((node.Value.World, node.Value.Coord));
                    _speculative.Remove(node);
                }

                node = next;
            }

            int cap = _workers * SpeculativePerWorker * 2; // room for a second resident world's share
            foreach (var raw in coords)
            {
                if (_speculative.Count >= cap)
                {
                    break;
                }

                var coord = WorldConstants.CanonicalChunk(raw, world.Circumference);
                if (world.IsChunkLoaded(coord) || _jobs.ContainsKey((world, coord)))
                {
                    continue;
                }

                var job = NewJob(world, coord, Lane.Speculative);
                _jobs[(world, coord)] = job;
                _speculative.AddLast(job);
            }

            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Queues <paramref name="coords"/> for background generation behind the views' own chunks (#3985: a rail
    /// section's chunks before it is stamped). Unlike <see cref="Speculate"/>'s these stay queued until a worker takes them;
    /// resident chunks and chunks already queued are skipped, so asking again is cheap. Tick thread only.</summary>
    public void Prefetch(ServerWorld world, IEnumerable<ChunkCoord> coords)
    {
        lock (_lock)
        {
            bool any = false;
            foreach (var raw in coords)
            {
                var coord = WorldConstants.CanonicalChunk(raw, world.Circumference);
                if (world.IsChunkLoaded(coord) || _jobs.ContainsKey((world, coord)))
                {
                    continue;
                }

                var job = NewJob(world, coord, Lane.Background);
                _jobs[(world, coord)] = job;
                _background.AddLast(job);
                any = true;
            }

            if (any)
            {
                Monitor.PulseAll(_lock);
            }
        }
    }

    /// <summary>Adopts one chunk's finished result (tick thread): true when it was ready and is resident now; false while it is
    /// queued or in flight, or when nothing was asked for it (dropped — ask again with <see cref="Prefetch"/>).</summary>
    public bool TryAdopt(ServerWorld world, ChunkCoord coord)
    {
        Job? job;
        lock (_lock)
        {
            var key = (world, WorldConstants.CanonicalChunk(coord, world.Circumference));
            if (!_jobs.TryGetValue(key, out job) || !job.Done)
            {
                return false;
            }

            _jobs.Remove(key);
        }

        Adopt(job);
        return true;
    }

    /// <summary>Adopts every finished speculative result for <paramref name="world"/> (tick thread). Cheap when
    /// nothing is ready; keeps adopted chunks out of the next pass's generation work.</summary>
    public int AdoptReady(ServerWorld world, int max)
    {
        List<Job>? ready = null;
        lock (_lock)
        {
            foreach (var job in _jobs.Values)
            {
                if (job.Done && job.World == world)
                {
                    (ready ??= new List<Job>()).Add(job);
                    if (ready.Count >= max)
                    {
                        break;
                    }
                }
            }

            if (ready == null)
            {
                return 0;
            }

            foreach (var job in ready)
            {
                _jobs.Remove((job.World, job.Coord));
            }
        }

        foreach (var job in ready)
        {
            Adopt(job);
        }

        return ready.Count;
    }

    private void Adopt(Job job)
    {
        var world = job.World;
        // The mode the worker generated with must still be this world's (a pad planned in the meantime changes
        // the flattened terrain); otherwise, and on a worker error, fall back to the inline path.
        bool sameMode = job.Error == null && job.Chunk != null
            && job.Circumference == world.Circumference && job.Cratered == world.Cratered
            && job.OreBoost.Equals(world.FrontierOreBoost) && job.LocationId == world.LocationId
            && LandingPadFlatten.SameList(job.Pads, world.LandingPadFlats) && SiteFlat.SameList(job.Sites, world.SiteFlats);
        if (sameMode)
        {
            world.AdoptGenerated(job.Coord, job.Chunk!);
            AdoptedFromWorkers++;
        }
        else
        {
            world.GetOrLoadChunk(job.Coord);
            GeneratedInline++;
        }
    }

    private Job NewJob(ServerWorld world, ChunkCoord coord, Lane lane) => new()
    {
        World = world,
        Coord = coord,
        Planet = world.Planet,
        Circumference = world.Circumference,
        Cratered = world.Cratered,
        Pads = LandingPadFlatten.Snapshot(world.LandingPadFlats),
        Sites = SiteFlat.Snapshot(world.SiteFlats),
        LocationId = world.LocationId,
        OreBoost = world.FrontierOreBoost,
        Lane = lane,
    };

    private void Unlink(Job job)
    {
        var list = job.Lane switch
        {
            Lane.Priority => _priority,
            Lane.Speculative => _speculative,
            _ => _background,
        };
        list.Remove(job);
    }

    /// <summary>Stops the worker threads (the server's <c>Stop()</c>). A pool that is never disposed stops them too,
    /// once it is collected — see <see cref="DropGuard"/>.</summary>
    public void Dispose() => _guard.Dispose();
}
