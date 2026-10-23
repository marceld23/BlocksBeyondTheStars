// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Server → client on arriving on a body (#1113): the receiver's PERSISTED explored-map cells
/// for that body, so the planet map's fog stays lifted across sessions. One bit per 8×8-chunk cell
/// (<c>ExploredMap</c> in Shared derives the grid from the circumference on both sides); the client
/// overlays it with the live-streamed chunks. Empty <see cref="Cells"/> = nothing explored yet.</summary>
public sealed class ExploredMapData
{
    public string BodyId { get; set; } = string.Empty;
    public int Cols { get; set; }
    public int Rows { get; set; }
    public byte[] Cells { get; set; } = System.Array.Empty<byte>();
}

/// <summary>A point of interest on the current planet (for the world map): settlement, wreck, …</summary>
public sealed class NetPoi
{
    public string Type { get; set; } = string.Empty; // settlement / settlement_ruin / wreck / landing
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Z { get; set; }
}

/// <summary>The known points of interest on the current planet, sent on join + world switch.</summary>
public sealed class PlanetPoiList
{
    public NetPoi[] Pois { get; set; } = System.Array.Empty<NetPoi>();
}
