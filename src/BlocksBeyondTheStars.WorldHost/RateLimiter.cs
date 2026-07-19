// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Fixed-window rate limiter keyed by an arbitrary string (caller IP, account id). Deliberately simple —
/// it blunts scripted abuse (signup floods, upload hammering), it is not a fairness scheduler. Windows
/// are tracked per key in memory; a process restart forgives everyone, which is fine at this scale.
/// The clock is injectable so tests don't sleep.
/// </summary>
public sealed class RateLimiter
{
    private sealed class Window
    {
        public long StartUnix;
        public int Count;
    }

    private readonly int _maxPerWindow;
    private readonly long _windowSeconds;
    private readonly Func<long> _nowUnix;
    private readonly ConcurrentDictionary<string, Window> _windows = new();
    private long _lastSweepUnix;

    /// <summary>Number of keys currently tracked (test/inspection — the #426 S15 sweep keeps this bounded).</summary>
    public int TrackedKeyCount => _windows.Count;

    public RateLimiter(int maxPerWindow, TimeSpan window, Func<long>? nowUnix = null)
    {
        _maxPerWindow = maxPerWindow;
        _windowSeconds = (long)window.TotalSeconds;
        _nowUnix = nowUnix ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>True when the key's window budget is already spent — checked WITHOUT consuming an attempt.
    /// Lets callers meter only FAILED tries (register via <see cref="TryPass"/>) while still answering
    /// "are you in cooldown?" up front. A non-positive limit disables the limiter (always false).</summary>
    public bool IsExhausted(string key)
    {
        if (_maxPerWindow <= 0)
        {
            return false;
        }

        if (!_windows.TryGetValue(key ?? string.Empty, out var window))
        {
            return false;
        }

        lock (window)
        {
            return _nowUnix() - window.StartUnix < _windowSeconds && window.Count >= _maxPerWindow;
        }
    }

    /// <summary>True when the caller may proceed; false when the key exhausted its window budget.
    /// A non-positive limit disables the limiter (always true).</summary>
    public bool TryPass(string key)
    {
        if (_maxPerWindow <= 0)
        {
            return true;
        }

        long now = _nowUnix();
        SweepExpired(now);
        var window = _windows.GetOrAdd(key ?? string.Empty, _ => new Window { StartUnix = now });
        lock (window)
        {
            if (now - window.StartUnix >= _windowSeconds)
            {
                window.StartUnix = now;
                window.Count = 0;
            }

            if (window.Count >= _maxPerWindow)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>Amortized cleanup (#426 S15): without it, one entry per unique key ever seen lives until
    /// process restart — an unbounded control-plane cache growing with every visitor. At most one pass per
    /// window length, and only entries idle for ≥ 2 windows are dropped: those carry no live budget (their
    /// window would restart on next use anyway), so racing a concurrent <see cref="TryPass"/> on the same
    /// key can at worst hand that key one fresh window — irrelevant for abuse blunting.</summary>
    private void SweepExpired(long now)
    {
        long last = Interlocked.Read(ref _lastSweepUnix);
        if (now - last < _windowSeconds || Interlocked.CompareExchange(ref _lastSweepUnix, now, last) != last)
        {
            return; // swept recently, or another caller just took this sweep
        }

        foreach (var kv in _windows)
        {
            bool stale;
            lock (kv.Value)
            {
                stale = now - kv.Value.StartUnix >= _windowSeconds * 2;
            }

            if (stale)
            {
                _windows.TryRemove(kv.Key, out _);
            }
        }
    }
}
