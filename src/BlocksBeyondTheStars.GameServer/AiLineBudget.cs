// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2591: the per-world AI line budget — a token bucket (capacity 6, refilled at
/// <see cref="ServerConfig.AiLinesPerMinute"/>) plus a cap of 2 requests in flight. Each request costs one token; a
/// priority may only take one when its reserve stays in the bucket afterwards:
/// <code>
///   PlayerInitiated 0 · Personal 1 · Vega 1 · News 2 · Chatter 3
/// </code>
/// so with a full bucket of 6 chatter gets at most 3 lines before it waits for the refill, and the last token is
/// always kept for a player who is waiting. A refused request is simply not made (the authored line stays) — there
/// is no queue. A rate of 0 refuses everything. Thread-safe (requests finish on background threads).
/// </summary>
internal sealed class AiLineBudget
{
    public const double Capacity = 6.0;
    public const int MaxInFlight = 2;

    private readonly Lock _gate = new();
    private double _tokens = Capacity;
    private double _lastAt = double.NaN;
    private int _inFlight;

    /// <summary>Tokens that must stay in the bucket after a request of this priority takes one.</summary>
    public static int ReserveFor(AiLinePriority priority) => priority switch
    {
        AiLinePriority.PlayerInitiated => 0,
        AiLinePriority.Personal => 1,
        AiLinePriority.Vega => 1,
        AiLinePriority.News => 2,
        _ => 3,
    };

    /// <summary>Requests currently running.</summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>Takes one token + one in-flight slot when the priority's rule allows it. <paramref name="now"/> is
    /// the server uptime in seconds.</summary>
    public bool TryTake(AiLinePriority priority, double now, double perMinute)
    {
        lock (_gate)
        {
            Refill(now, perMinute);
            if (perMinute <= 0 || _inFlight >= MaxInFlight || _tokens + 1e-9 < 1.0 + ReserveFor(priority))
            {
                return false;
            }

            _tokens -= 1.0;
            _inFlight++;
            return true;
        }
    }

    /// <summary>Frees the in-flight slot of a finished request (the token stays spent).</summary>
    public void Release()
    {
        lock (_gate)
        {
            if (_inFlight > 0)
            {
                _inFlight--;
            }
        }
    }

    /// <summary>The bucket's level at <paramref name="now"/> (inspection/tests).</summary>
    public double TokensAt(double now, double perMinute)
    {
        lock (_gate)
        {
            Refill(now, perMinute);
            return _tokens;
        }
    }

    private void Refill(double now, double perMinute)
    {
        if (!double.IsNaN(_lastAt) && now > _lastAt && perMinute > 0)
        {
            _tokens = Math.Min(Capacity, _tokens + (now - _lastAt) * perMinute / 60.0);
        }

        if (double.IsNaN(_lastAt) || now > _lastAt)
        {
            _lastAt = now;
        }
    }
}
