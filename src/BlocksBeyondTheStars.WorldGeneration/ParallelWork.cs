// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Threading;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3068: runs a one-off batch of independent, index-addressed work items on a few short-lived threads plus the
/// calling thread — the routed river grid of a new world. Every thread works with its own state
/// (a <see cref="WorldGenerator"/> sibling: a generator instance is never shared between threads), takes the next
/// index from a shared counter and writes only that index's result, so the outcome is the serial one whatever the
/// interleaving. A platform that cannot start a thread (the in-browser build runs the server in-process) does the
/// whole batch on the caller, exactly as before — the fallback <c>ChunkGenerationPool.TryStart</c> uses (#1817).
/// </summary>
public static class ParallelWork
{
    /// <summary>Runs <paramref name="body"/> for every index in <c>[0, count)</c>. The caller works with
    /// <paramref name="callerState"/>; each of up to <paramref name="workers"/> extra threads makes its own with
    /// <paramref name="workerState"/>. Blocks until every item is done; the first exception is rethrown here.</summary>
    public static void For<TState>(int count, int workers, TState callerState, System.Func<TState> workerState,
        System.Action<int, TState> body)
    {
        int extra = System.Math.Min(workers, count - 1);
        if (extra <= 0)
        {
            for (int i = 0; i < count; i++)
            {
                body(i, callerState);
            }

            return;
        }

        var batch = new Batch<TState>(count, body);
        var threads = new List<Thread>(extra);
        for (int t = 0; t < extra; t++)
        {
            try
            {
                var thread = new Thread(() => batch.Run(workerState()))
                {
                    IsBackground = true,
                    Name = "worldgen-batch-" + t,
                };
                thread.Start();
                threads.Add(thread);
            }
            catch (System.Exception)
            {
                break; // no threads on this platform — the caller takes every remaining item
            }
        }

        batch.Run(callerState);
        foreach (var thread in threads)
        {
            thread.Join();
        }

        batch.ThrowIfFailed();
    }

    private sealed class Batch<TState>
    {
        private readonly int _count;
        private readonly System.Action<int, TState> _body;
        private int _next = -1;
        private System.Exception? _error;

        public Batch(int count, System.Action<int, TState> body)
        {
            _count = count;
            _body = body;
        }

        public void Run(TState state)
        {
            int i;
            while ((i = Interlocked.Increment(ref _next)) < _count)
            {
                if (Volatile.Read(ref _error) is not null)
                {
                    return; // another thread failed — the batch is lost anyway
                }

                try
                {
                    _body(i, state);
                }
                catch (System.Exception e)
                {
                    Interlocked.CompareExchange(ref _error, e, null);
                    return;
                }
            }
        }

        public void ThrowIfFailed()
        {
            if (_error is { } error)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }
        }
    }
}
