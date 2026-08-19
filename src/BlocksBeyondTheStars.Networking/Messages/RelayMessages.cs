// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One commissioned player station's SPS relay meter (#1125): the bill of materials as parallel
/// arrays (item key / required / contributed so far) plus whether the conversion is complete. The client
/// renders the meter in the star-map detail pane and offers contribute buttons; all amounts are
/// server-authoritative.</summary>
public sealed class NetRelayStation
{
    /// <summary>The station's id ("pstation:…") — also its star-map body id.</summary>
    public string StationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The star system the relay counts for (lane endpoints are systems, not stations).</summary>
    public string SystemId { get; set; } = string.Empty;

    public string[] Items { get; set; } = System.Array.Empty<string>();
    public int[] Required { get; set; } = System.Array.Empty<int>();
    public int[] Contributed { get; set; } = System.Array.Empty<int>();

    /// <summary>True once every cost line is met — the station IS a relay.</summary>
    public bool Completed { get; set; }
}

/// <summary>Server → client (#1125): the save's whole SPS relay network — every commissioned player
/// station's relay meter plus the jump lanes the completed relays currently form (parallel arrays of
/// system-id endpoints). Sent on join and whenever a contribution lands or a lane forms. Empty (and
/// <see cref="Enabled"/> false) when the data folder ships no relay definition.</summary>
public sealed class RelayNetworkState
{
    /// <summary>False when <c>data/relay.json</c> is absent — the client hides every relay surface.</summary>
    public bool Enabled { get; set; }

    public NetRelayStation[] Relays { get; set; } = System.Array.Empty<NetRelayStation>();

    /// <summary>Jump-lane endpoints as parallel arrays: lane i links <see cref="LaneSystemA"/>[i] ↔
    /// <see cref="LaneSystemB"/>[i]. Travel between two lane systems needs no jump generator.</summary>
    public string[] LaneSystemA { get; set; } = System.Array.Empty<string>();
    public string[] LaneSystemB { get; set; } = System.Array.Empty<string>();
}

/// <summary>Client → server (#1125): pour items into a station's relay conversion (co-op — any player may
/// contribute, not just the owner). The server clamps <see cref="Count"/> to what is still missing AND to
/// what the contributor actually holds, so "give everything I have" is simply a large count. Requires being
/// at the station (aboard it, in its space instance, or at its host body) — materials are delivered in
/// person, not beamed across the galaxy.</summary>
public sealed class ContributeRelayIntent
{
    public string StationId { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}
