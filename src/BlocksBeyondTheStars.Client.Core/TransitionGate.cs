// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Core;

/// <summary>What an arrival cover waits for (#3140/#3142).</summary>
public enum TransitionArrival
{
    /// <summary>A planet landing: the server sets the player down with a touchdown snap (<c>RespawnNotice</c>) — on the
    /// world the client already holds (back on the launch body) as well as after a world reset (another body).</summary>
    Landing,

    /// <summary>A station docking: the station is always a world of its own, so the arrival is its world reset.</summary>
    Docking,
}

/// <summary>
/// When an arrival cover may open (#3139/#3140/#3142) — the one rule behind the seamless landing and docking. The cover
/// (clouds, a glare, the hangar doors) closes over the flight view at the end of the descent or the dock approach, and the
/// world behind it is only worth showing once the server has really put the player there AND the ground has settled
/// under their feet.
/// <para>
/// The rule is about signals, not time: a destination world the server first has to build can take seconds, and before
/// #3139 the loading veil only armed on the world reset — a landing whose <c>SpaceClosed</c> raced ahead of it showed the
/// player for a moment on the planet they had just left. Here the cover holds until the arrival signal for its kind came
/// in AFTER the cover began, and the caller's "settled" (the world ready, the snap consumed, the player standing) is true.
/// A short minimum keeps a cover from flickering; <see cref="GiveUp"/> means a lost message can never trap the player.
/// </para>
/// Unity-free so the rule is covered by plain .NET tests; the client's transition director holds the live instance.
/// </summary>
public sealed class TransitionGate
{
    /// <summary>Seconds a cover stays shut even when everything has already arrived (no flicker).</summary>
    public const float MinClosed = 0.6f;

    /// <summary>A landing whose world reset came but whose touchdown snap did not (an older server) counts as placed
    /// after this many seconds.</summary>
    public const float PlacementGrace = 6f;

    /// <summary>The cover opens after this many seconds whatever happened — the same cap as the loading veil.</summary>
    public const float GiveUp = 25f;

    private float _sinceWorldLoad = -1f;

    /// <summary>True while an arrival is covered.</summary>
    public bool Active { get; private set; }

    /// <summary>What this arrival waits for.</summary>
    public TransitionArrival Kind { get; private set; }

    /// <summary>Seconds since the cover began.</summary>
    public float Elapsed { get; private set; }

    /// <summary>A world reset arrived since the cover began.</summary>
    public bool WorldLoadSeen => _sinceWorldLoad >= 0f;

    /// <summary>The touchdown snap arrived since the cover began.</summary>
    public bool PlacedSeen { get; private set; }

    /// <summary>The transition was refused or abandoned — the cover opens on whatever is there.</summary>
    public bool Cancelled { get; private set; }

    /// <summary>Starts covering an arrival of <paramref name="kind"/>; every earlier signal is forgotten.</summary>
    public void Begin(TransitionArrival kind)
    {
        Active = true;
        Kind = kind;
        Elapsed = 0f;
        _sinceWorldLoad = -1f;
        PlacedSeen = false;
        Cancelled = false;
    }

    /// <summary>The client began loading a new world (a world reset).</summary>
    public void NoteWorldLoad()
    {
        if (Active && _sinceWorldLoad < 0f)
        {
            _sinceWorldLoad = 0f;
        }
    }

    /// <summary>The server set the player down (the touchdown snap).</summary>
    public void NotePlaced()
    {
        if (Active)
        {
            PlacedSeen = true;
        }
    }

    /// <summary>The server refused the transition (or it never got going): open on what is there.</summary>
    public void Cancel()
    {
        if (Active)
        {
            Cancelled = true;
        }
    }

    /// <summary>Advances the clocks.</summary>
    public void Tick(float dt)
    {
        if (!Active || !(dt > 0f))
        {
            return;
        }

        Elapsed += dt;
        if (_sinceWorldLoad >= 0f)
        {
            _sinceWorldLoad += dt;
        }
    }

    /// <summary>True when the server has moved the player to the destination, as far as this kind of arrival can tell.</summary>
    public bool Arrived => Kind == TransitionArrival.Docking
        ? WorldLoadSeen
        : PlacedSeen || _sinceWorldLoad >= PlacementGrace;

    /// <summary>True once the cover may open. <paramref name="settled"/> is the caller's "the world is ready and the
    /// player stands on it" (the world ready, no snap pending, not settling).</summary>
    public bool CanOpen(bool settled)
    {
        if (!Active || Cancelled || Elapsed >= GiveUp)
        {
            return true;
        }

        return Elapsed >= MinClosed && Arrived && settled;
    }

    /// <summary>The arrival is over (the cover opened).</summary>
    public void End() => Active = false;
}
