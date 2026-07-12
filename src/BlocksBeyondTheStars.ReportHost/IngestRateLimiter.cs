// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Fixed-window per-key (client IP) rate limiter for the ingest endpoint. A window is one minute; the
/// count resets when the minute rolls over. Simple by design — the goal is blunting scripted floods,
/// not fairness — and time is injected so tests can drive the window edge deterministically.
/// </summary>
public sealed class IngestRateLimiter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long WindowMinute, int Count)> _counters = new(StringComparer.Ordinal);
    private readonly int _perMinute;

    public IngestRateLimiter(int perMinute) => _perMinute = perMinute;

    /// <summary>True when this request is within the key's budget for the current minute.</summary>
    public bool Allow(string key, long nowUnix)
    {
        if (_perMinute <= 0)
        {
            return true; // limiter disabled by config
        }

        long minute = nowUnix / 60;
        lock (_gate)
        {
            // Opportunistic cleanup: drop stale windows once the table gets big, so a wide scan of source
            // addresses can't grow memory without bound.
            if (_counters.Count > 10_000)
            {
                foreach (var stale in _counters.Where(kv => kv.Value.WindowMinute != minute).Select(kv => kv.Key).ToList())
                {
                    _counters.Remove(stale);
                }
            }

            if (!_counters.TryGetValue(key, out var entry) || entry.WindowMinute != minute)
            {
                _counters[key] = (minute, 1);
                return true;
            }

            if (entry.Count >= _perMinute)
            {
                return false;
            }

            _counters[key] = (minute, entry.Count + 1);
            return true;
        }
    }
}
