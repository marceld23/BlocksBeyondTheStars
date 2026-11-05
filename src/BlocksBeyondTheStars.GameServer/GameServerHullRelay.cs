// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3196: the hull-colour relay. A repaint while the ship stands on a pad re-announces the parked ship object
/// (<see cref="Networking.Messages.LandedShipState"/> — every structure cell, since no smaller message carries a parked
/// ship's colour) to everyone on the world, and every client rebuilds that ship's whole mesh for it. Unlike the face,
/// body paint and tool look (#2672), FX (#2158) and voice (#3048) this relay had no throttle of its own: 400 scripted
/// hull intents (a browser sends JSON intents, so the console is enough) were 400 full structures to every player on
/// the world, and 400 mesh rebuilds on each of them.
/// <para>
/// Now each player has a small token bucket for these rebroadcasts. A change with a token goes out at once — the join's
/// own colour and a few quick clicks through the colours each show immediately. A change without one only updates the
/// stored colour and leaves a mark; <see cref="TickHullRelay"/> sends ONE rebroadcast as soon as a token is back, and that
/// one reads the colour at send time. So a flood costs at most <see cref="HullRelayBurst"/> structures at once and one
/// per <see cref="HullRelayRefillSeconds"/> after that, and the LAST colour still reaches everyone. That also keeps the
/// flood's victims far below the browser send-queue cap (#3195): the flooder cannot make the server queue structures for
/// them faster than one a second.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3196: how many hull rebroadcasts one player gets at once (also the bucket's starting fill): the join's
    /// colour plus a couple of quick clicks through the colours each show immediately.</summary>
    internal const double HullRelayBurst = 3.0;

    /// <summary>#3196: one hull rebroadcast token comes back every this many seconds — a ship's mesh rebuilt once a second
    /// at most on every client, and the last pick of a quick run of clicks lands within a second.</summary>
    internal const double HullRelayRefillSeconds = 1.0;

    private sealed class HullRelay
    {
        public double Budget = HullRelayBurst;
        public bool Pending;
    }

    /// <summary>Per player id; an entry is dropped once its bucket is full again and nothing waits, so the map holds only
    /// players who repainted in the last few seconds.</summary>
    private readonly Dictionary<string, HullRelay> _hullRelays = new(StringComparer.Ordinal);

    private readonly List<string> _hullRelaysDone = new();

    /// <summary>#3196: called by <see cref="HandleSetAppearance"/> after the stored hull colour changed. Rebroadcasts the
    /// parked ship now when a token is available, otherwise marks it for <see cref="TickHullRelay"/>.</summary>
    private void RelayHullChange(PlayerSession session)
    {
        string playerId = session.State.PlayerId;
        if (!_hullRelays.TryGetValue(playerId, out var relay))
        {
            _hullRelays[playerId] = relay = new HullRelay();
        }

        if (relay.Budget >= 1.0)
        {
            relay.Budget -= 1.0;
            relay.Pending = false;
            BroadcastParkedHull(session);
        }
        else
        {
            relay.Pending = true; // coalesced: the tick step sends the colour that is current once a token is back
        }
    }

    /// <summary>Ship-as-object: re-announces the player's parked ship to everyone on its world (the owner too), so the new
    /// hull colour shows. Nothing when the ship is not standing on the player's world (in flight, or not placed).</summary>
    private void BroadcastParkedHull(PlayerSession session)
    {
        if (session.Joined && SetActiveWorld(session.CurrentLocationId))
        {
            var rec = _worlds.Active.LandedFor(session.State.PlayerId);
            if (rec.Placed)
            {
                BroadcastToWorld(LandedShipMessage(session.State.PlayerId, rec, removed: false));
            }
        }
    }

    /// <summary>#3196 tick step (post-world and held): refills the hull buckets and sends each coalesced repaint once a
    /// token is back. Restores the world cursor it moved, so the steps after it see the world they expect.</summary>
    private void TickHullRelay(double deltaSeconds)
    {
        if (_hullRelays.Count == 0)
        {
            return;
        }

        string? cursor = _worlds.Active?.LocationId;
        _hullRelaysDone.Clear();
        foreach (var (playerId, relay) in _hullRelays)
        {
            relay.Budget = System.Math.Min(HullRelayBurst, relay.Budget + (deltaSeconds / HullRelayRefillSeconds));
            if (relay.Pending && relay.Budget >= 1.0)
            {
                relay.Pending = false;
                relay.Budget -= 1.0;
                if (FindSessionByPlayerId(playerId) is { } session)
                {
                    BroadcastParkedHull(session); // a player who left meanwhile took the ship with them — nothing to send
                }
            }

            if (!relay.Pending && relay.Budget >= HullRelayBurst)
            {
                _hullRelaysDone.Add(playerId);
            }
        }

        foreach (string playerId in _hullRelaysDone)
        {
            _hullRelays.Remove(playerId);
        }

        if (cursor is not null)
        {
            SetActiveWorld(cursor);
        }
    }
}
