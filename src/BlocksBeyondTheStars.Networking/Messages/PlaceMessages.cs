// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// The lively places: one inhabited place of the client's current world — a settlement on a planet, or the station the
/// player is aboard. The map's point of interest says only where its middle is; this says where it begins and ends and
/// what stands in it, so the client can greet the player with the place's name, hang a sign over each door, play the
/// place's own sound layer, let its windows glow at night and show its lights and smoke from afar. Render-only: nothing
/// here decides anything — the blocks stream as chunks, the people as NPCs.
/// </summary>
public sealed class NetPlace
{
    /// <summary>A key that is stable for the place while the world lives (the settlement's place key, a station's id) — the
    /// client keys what it remembers about a place by it ("already greeted this visit").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary><see cref="PlaceKinds.Settlement"/> or <see cref="PlaceKinds.Station"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The place's name as the map shows it (a coined name, not a locale key).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The size tier (hamlet, village, town, city, metropolis; a station's size tier).</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>The footprint in world blocks, both ends included (X canonical, as the server stores the box).</summary>
    public int MinX { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxZ { get; set; }

    /// <summary>The ground row of the place (a settlement's foundation row, a station's deck).</summary>
    public int Y { get; set; }

    /// <summary>The settlement's own colour (0xRRGGBB; 0 = none) — its roofs and awnings carry it, the client's signs do too.</summary>
    public int Color { get; set; }

    /// <summary>The middle of the meeting place (the plaza, a village green, a station's hub).</summary>
    public int PlazaX { get; set; }
    public int PlazaY { get; set; }
    public int PlazaZ { get; set; }

    /// <summary>What the place offers, as building functions (market, tavern, workshop, clinic …) — the greeting names them
    /// with symbols.</summary>
    public string[] Trades { get; set; } = System.Array.Empty<string>();

    /// <summary>The signs over the doors.</summary>
    public NetPlaceSign[] Signs { get; set; } = System.Array.Empty<NetPlaceSign>();

    /// <summary>Points the client dresses with small effects (a fire's smoke column from afar, moths at a lantern,
    /// butterflies at a garden, sparks at a workbench).</summary>
    public NetPlacePoint[] Points { get; set; } = System.Array.Empty<NetPlacePoint>();

    /// <summary>What is going on right now (<see cref="PlaceStates"/>; empty = an ordinary day).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>A station's purpose (<c>StationPurposes</c>; empty = none, and for every settlement).</summary>
    public string Purpose { get; set; } = string.Empty;
}

/// <summary>One sign over a door of a <see cref="NetPlace"/>: where the door is, which way it faces, what is behind it.</summary>
public sealed class NetPlaceSign
{
    /// <summary>The doorway's cell (its lower one).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>The direction the door looks out of the building: 0 = −Z, 1 = +Z, 2 = −X, 3 = +X.</summary>
    public byte Side { get; set; }

    /// <summary>The building's function (market, tavern, workshop …) — the client picks the picture by it.</summary>
    public string Fn { get; set; } = string.Empty;
}

/// <summary>One effect point of a <see cref="NetPlace"/>.</summary>
public sealed class NetPlacePoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>What stands there (<see cref="PlacePointKinds"/>).</summary>
    public byte Kind { get; set; }
}

/// <summary>The kinds of a <see cref="NetPlace"/>.</summary>
public static class PlaceKinds
{
    public const string Settlement = "settlement";
    public const string Station = "station";
}

/// <summary>What can be going on at a place (<see cref="NetPlace.State"/>).</summary>
public static class PlaceStates
{
    public const string None = "";

    /// <summary>Market day: travelling merchants at the stalls, more people at the plaza.</summary>
    public const string Market = "market";

    /// <summary>The people have gathered for a festival.</summary>
    public const string Festival = "festival";
}

/// <summary>The kinds of a <see cref="NetPlacePoint"/>. An older client ignores a kind it does not know.</summary>
public static class PlacePointKinds
{
    /// <summary>An open fire (the fire place of a hamlet or a village).</summary>
    public const byte Fire = 1;

    /// <summary>A street lantern.</summary>
    public const byte Lamp = 2;

    /// <summary>A garden or a field's edge.</summary>
    public const byte Garden = 3;

    /// <summary>A chimney top (a tavern, a workshop).</summary>
    public const byte Chimney = 4;

    /// <summary>A workbench somebody works at.</summary>
    public const byte Workbench = 5;

    /// <summary>A console or a screen (stations).</summary>
    public const byte Screen = 6;

    /// <summary>A well.</summary>
    public const byte Well = 7;

    /// <summary>A market stall.</summary>
    public const byte Stall = 8;

    /// <summary>A bench.</summary>
    public const byte Bench = 9;
}

/// <summary>The inhabited places of the client's current world (server → client, on world entry and whenever a place's
/// <see cref="NetPlace.State"/> changes). An empty list clears what the client held from the world it came from.</summary>
public sealed class PlaceList
{
    public NetPlace[] Places { get; set; } = System.Array.Empty<NetPlace>();
}

/// <summary>
/// Server → client: an NPC plays a gesture without saying anything (<see cref="NpcGestures"/>) — two villagers nodding to
/// each other in passing, a dancer at a festival, somebody pointing at the landed trader. A line with a gesture still
/// travels as an <see cref="NpcGreeting"/>. Render-only.
/// </summary>
public sealed class NpcEmote
{
    /// <summary>The runtime NPC id (matches <see cref="NetNpc.Id"/>).</summary>
    public int NpcId { get; set; }

    /// <summary>The gesture (<see cref="NpcGestures"/>); an older client ignores a value it does not know.</summary>
    public byte Gesture { get; set; }
}
