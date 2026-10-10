// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>process-wide caches of per-world derived data (partial of <see cref="WorldGenerator"/>).</summary>
public sealed partial class WorldGenerator
{
    /// <summary>
    /// #2455: a bounded, process-wide cache of data derived once per world (the calibration, the river field), shared
    /// across generator instances because it is a pure function of the world key.
    /// <para>The shared lock only guards the bookkeeping. A missing entry is built OUTSIDE it, exactly once per key:
    /// callers that need the same key wait for that one build, every other world carries on. The caches used to build
    /// under the lock and take it on every lookup, so one test generating world after world starved the per-column
    /// lookups of every other world in the process (the test host runs four classes in parallel, the client bakes a
    /// fresh generator per preview texture).</para>
    /// </summary>
    private sealed class WorldDataCache<TValue>
        where TValue : class
    {
        private readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool, bool, int), TValue> _done = new();
        private readonly System.Collections.Generic.Queue<(long, string, int, bool, long, bool, bool, int)> _order = new();
        private readonly System.Collections.Generic.Dictionary<(long, string, int, bool, long, bool, bool, int), System.Lazy<TValue>> _building = new();
        private readonly object _lock = new object();
        private readonly int _cap;

        public WorldDataCache(int cap)
        {
            _cap = cap;
        }

        public bool TryGet((long, string, int, bool, long, bool, bool, int) key, out TValue value)
        {
            lock (_lock)
            {
                return _done.TryGetValue(key, out value!);
            }
        }

        public TValue GetOrBuild((long, string, int, bool, long, bool, bool, int) key, System.Func<TValue> build)
        {
            System.Lazy<TValue>? gate;
            lock (_lock)
            {
                if (_done.TryGetValue(key, out var hit))
                {
                    return hit;
                }

                if (!_building.TryGetValue(key, out gate))
                {
                    gate = new System.Lazy<TValue>(build, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
                    _building[key] = gate;
                }
            }

            TValue value;
            try
            {
                value = gate.Value; // only the callers that need THIS key wait here
            }
            catch
            {
                lock (_lock)
                {
                    ForgetGate(key, gate); // a failed build is not cached: the next caller tries again, as before
                }

                throw;
            }

            lock (_lock)
            {
                if (ForgetGate(key, gate))
                {
                    EvictOldest(_done, _order, _cap); // #1527: oldest-out, never the whole cache
                    _done[key] = value;
                    _order.Enqueue(key);
                }
            }

            return value;
        }

        /// <summary>Drops the build gate if it is still this one — true for the one caller that publishes the value.
        /// Call with <see cref="_lock"/> held.</summary>
        private bool ForgetGate((long, string, int, bool, long, bool, bool, int) key, System.Lazy<TValue> gate)
        {
            if (_building.TryGetValue(key, out var current) && ReferenceEquals(current, gate))
            {
                _building.Remove(key);
                return true;
            }

            return false;
        }
    }

    /// <summary>#2455: an instance's last world key and its value in ONE immutable object (like <see cref="WonderSlot"/>,
    /// #1723), so the lock-free read sees a matching pair or nothing. A generator asks for its own world on every
    /// column, so the shared cache is only touched when the world changes.</summary>
    private sealed class WorldSlot<TValue>
        where TValue : class
    {
        public readonly (long, string, int, bool, long, bool, bool, int) Key;
        public readonly TValue Value;

        public WorldSlot((long, string, int, bool, long, bool, bool, int) key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }
}
