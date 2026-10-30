// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The planet scan's settlers (#2998, Marcel 2026-10-29): who built on the scanned body — the players with bases on it and
/// with commissioned stations orbiting it — most bases first. Every scanner tier shows the list: it is a count, not a
/// location. A Quantum scan (tier 3) also finds every base there for the scanning player (#2997), so they appear on the
/// planet map.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The settler list of <paramref name="body"/>, sorted: most bases, most stations, the viewer, their allies, then
    /// by name.</summary>
    private NetPlanetSettler[] PlanetSettlers(string body, string viewerId)
    {
        var byOwner = new Dictionary<string, NetPlanetSettler>(StringComparer.Ordinal);
        NetPlanetSettler Entry(string owner)
        {
            if (!byOwner.TryGetValue(owner, out var e))
            {
                byOwner[owner] = e = new NetPlanetSettler
                {
                    Name = owner,
                    Relation = owner == viewerId ? NetBase.RelationOwn : AreAllied(owner, viewerId) ? NetBase.RelationAlly : NetBase.RelationOther,
                };
            }

            return e;
        }

        foreach (var kv in BaseCountsByOwner(body))
        {
            Entry(kv.Key).Bases = kv.Value;
        }

        foreach (var s in _playerStationCells.Values)
        {
            if (s.Boardable && !string.IsNullOrEmpty(s.OwnerId) && _stationHostBody.TryGetValue(s.Id, out var host) && host == body)
            {
                Entry(s.OwnerId).Stations++;
            }
        }

        return byOwner.Values
            .OrderByDescending(e => e.Bases)
            .ThenByDescending(e => e.Stations)
            .ThenBy(e => e.Relation)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Adds the settlers to a scan report; a Quantum scan also finds the body's bases for the player.</summary>
    private void AddPlanetSettlers(PlayerSession session, PlanetScanResult result, int tier)
    {
        result.SettlersKnown = true;
        result.Settlers = PlanetSettlers(result.BodyId, session.State.PlayerId);
        if (tier >= 3)
        {
            result.BasesRevealed = DiscoverAllBasesOn(session, result.BodyId);
        }
    }

    /// <summary>Test seam: the settler list of a body as the planet scan sends it to a player.</summary>
    public IReadOnlyList<NetPlanetSettler> PlanetSettlersForTest(string body, string viewerId) => PlanetSettlers(body, viewerId);

    /// <summary>Test seam: a Quantum scan's base discovery on a body (#2997).</summary>
    public int DiscoverAllBasesOnForTest(PlayerSession session, string body) => DiscoverAllBasesOn(session, body);
}
