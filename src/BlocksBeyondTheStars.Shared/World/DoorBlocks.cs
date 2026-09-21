// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The door vocabulary shared by the server, the world renderer, the placement ghost and the build editors
/// (#1975): which placeable block keys become a door ENTITY instead of a voxel, which door kind a block or a
/// structure marker stands for, and which kinds swing by hand. Used to be private statics of the game server
/// with a copy of the "is it hinged" check on the client; one list here means a new door kind is added once.
/// </summary>
public static class DoorBlocks
{
    /// <summary>Sci-fi auto door (cities, stations, the ship): opens when a player comes near.</summary>
    public const string Slide = "slide";

    /// <summary>Metal hinge door: a leaf the player swings by hand with E.</summary>
    public const string Hinge = "hinge";

    /// <summary>The cheap early-game hinge door — same behaviour, wood instead of metal.</summary>
    public const string Wood = "wood";

    /// <summary>Auto-open like slide, a passable blue field: the one door that keeps a sealed room airtight (#793).</summary>
    public const string Energy = "energy";

    /// <summary>True for the placeable block keys that become a door ENTITY instead of a voxel block. Placement
    /// treats them apart: they need a genuinely empty (air) cell — a door cannot displace a fluid the way a solid
    /// block does, the water would simply flow back around it (#851).</summary>
    public static bool IsDoorBlock(string? blockKey)
        => blockKey is "door_hinge" or "door_slide" or "door_wood" or "door_energy";

    /// <summary>Door kind for a placeable door BLOCK key — the inverse of <see cref="ItemFor"/>. Used wherever a
    /// player places a door block (world cell or self-built ship), so the door entity keeps the behaviour of the
    /// block that was placed (#1021).</summary>
    public static string KindForBlock(string blockKey) => blockKey switch
    {
        "door_slide" => Slide,
        "door_wood" => Wood,
        "door_energy" => Energy,
        _ => Hinge,
    };

    /// <summary>Door kind for a structure door MARKER id (settlement/station templates + the editors). The energy
    /// door is the airtight one (#793) — village/city/station authors can place it explicitly.</summary>
    public static string KindForMarker(string markerType) => markerType switch
    {
        "door_hinge" => Hinge,
        "door_energy" => Energy,
        _ => Slide,
    };

    /// <summary>The item a door of this kind hands back when it is picked up again.</summary>
    public static string ItemFor(string kind) => kind switch
    {
        Slide => "door_slide",
        Wood => "door_wood",
        Energy => "door_energy",
        _ => "door_hinge",
    };

    /// <summary>Doors the player swings by hand with E (as opposed to slide/energy doors, which open on
    /// proximity). The wooden door is a cheap early-game hinge door — same behaviour, wood instead of metal.</summary>
    public static bool IsHandOperated(string? kind) => kind == Hinge || kind == Wood;

    /// <summary>True for the three structure marker ids that hang a door when a template is stamped.</summary>
    public static bool IsDoorMarker(string? markerType)
        => markerType is "door_slide" or "door_hinge" or "door_energy";
}
