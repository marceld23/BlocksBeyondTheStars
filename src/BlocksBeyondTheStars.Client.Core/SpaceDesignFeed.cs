// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3612: the ONE place the client turns received <see cref="SpaceShipDesign"/> messages into whole designs. A giant wreck's
/// hull arrives in parts (<see cref="SpaceShipDesign.PartCount"/>); every consumer — the flight view's stations, wrecks and
/// asteroids, the own ship and the remote pilots' hulls — subscribes to <see cref="DesignReady"/> instead of the raw network
/// event, so all of them see only complete designs and a single-part design exactly as before (the very same object).
/// <para>The parts are dropped when they can no longer complete: on a new session (join), a new connection, a closed flight
/// and a world change (<see cref="Reset"/>), and per structure when it leaves the flight (<see cref="Forget"/>). The server
/// sends all parts of a design in one tick on one channel, so none of these falls between two parts of a design it
/// wanted to keep.</para>
/// </summary>
public sealed class SpaceDesignFeed
{
    private readonly SpaceDesignAssembler _assembler = new();

    /// <summary>A whole design arrived (a single-part one as it came, a parted one joined).</summary>
    public event Action<SpaceShipDesign>? DesignReady;

    /// <summary>How many structures have a design half arrived (diagnostics, tests).</summary>
    public int PendingCount => _assembler.PendingCount;

    /// <summary>A feed fed by hand (tests) — <see cref="Attach"/> wires one to a client.</summary>
    public SpaceDesignFeed()
    {
    }

    /// <summary>Wires the feed to <paramref name="client"/>: every received design goes through it, and the session, the
    /// connection, the flight and the world it belongs to reset it.</summary>
    public static SpaceDesignFeed Attach(NetworkClient client)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var feed = new SpaceDesignFeed();
        client.SpaceShipDesignReceived += feed.Feed;
        client.JoinAccepted += _ => feed.Reset();
        client.Disconnected += feed.Reset;
        client.SpaceClosed += _ => feed.Reset();
        client.WorldResetReceived += _ => feed.Reset();
        client.SpaceEntityDestroyed += m => feed.Forget(m.Id);
        return feed;
    }

    /// <summary>Takes one received design message; raises <see cref="DesignReady"/> when it completes a design.</summary>
    public void Feed(SpaceShipDesign message)
    {
        if (message is null)
        {
            return;
        }

        var whole = _assembler.Add(message);
        if (whole is not null)
        {
            DesignReady?.Invoke(whole);
        }
    }

    /// <summary>Drops one structure's unfinished design (it left the flight).</summary>
    public void Forget(string? id) => _assembler.Forget(id ?? string.Empty);

    /// <summary>Drops every unfinished design.</summary>
    public void Reset() => _assembler.Clear();
}
