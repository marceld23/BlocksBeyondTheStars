// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

public sealed class CraftIntent
{
    public string RecipeKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

/// <summary>
/// The always-available "Dye"/"Glow" crafting action: re-colour a building material the player holds.
/// <see cref="Tint"/> recolours the block's surface; <see cref="Glow"/> turns it into a coloured light
/// source (which the server gates behind a luminescent ingredient). At least one colour is non-zero;
/// both may be set for a glowing dyed block. The output is the same item carrying the colour in its key.
/// </summary>
public sealed class TintCraftIntent
{
    /// <summary>Source item to recolour (base key, or an already-coloured key — the server re-bases it).</summary>
    public string SourceItemKey { get; set; } = string.Empty;

    /// <summary>Surface tint as 0xRRGGBB, or 0 for none.</summary>
    public int Tint { get; set; }

    /// <summary>Light colour as 0xRRGGBB, or 0 for no glow.</summary>
    public int Glow { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>Inventory slot the output should land in (the hotbar slot the action was invoked on),
    /// or -1 for the legacy "first free slot" behaviour. Old clients send nothing → -1 via the
    /// contractless decoder, so the wire stays compatible.</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>
/// The always-available "Shape" crafting action: re-form a held building material into another geometric
/// shape (sphere, dome, pyramid, ramp, …) that still behaves like a block. The output is the same item with
/// the shape index encoded in its key (<see cref="BlocksBeyondTheStars.Shared.World.BlockShape"/>); placing
/// it stamps the shape on the cell. The placement orientation is derived from the player's facing, not here.
/// </summary>
public sealed class ShapeCraftIntent
{
    /// <summary>Source item to re-form (base key, or an already coloured/shaped key — the server re-bases it,
    /// preserving any colour).</summary>
    public string SourceItemKey { get; set; } = string.Empty;

    /// <summary>Target shape index (see <see cref="BlocksBeyondTheStars.Shared.World.BlockShape"/>); 0 = back to a cube.</summary>
    public int Shape { get; set; }

    public int Count { get; set; } = 1;

    /// <summary>Preferred output slot (hotbar action), or -1 for the legacy first-free-slot behaviour.</summary>
    public int Slot { get; set; } = -1;
}

public sealed class UnlockBlueprintIntent
{
    public string BlueprintKey { get; set; } = string.Empty;
}

/// <summary>Client dismantles a crafted item at the workshop, recovering some of its components.</summary>
public sealed class DisassembleIntent
{
    public string ItemKey { get; set; } = string.Empty;
}

public sealed class CraftResult
{
    public bool Success { get; set; }
    public string RecipeKey { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

/// <summary>
/// Server → client: which crafting/production stations the player can use RIGHT NOW (#1070). The server is
/// the single source of truth for "am I at a station" — the client used to guess from ship station markers
/// only, so a base workbench, a placed forge and even hand recipes read as blocked in the Tab menu while
/// the server would have crafted happily. Sent on join and whenever the set changes.
/// </summary>
public sealed class StationsInReach
{
    /// <summary>Lower-case <c>CraftingStation</c> names usable now ("workshop", "refinery", …). "hand" is
    /// always usable and therefore never listed; "market"/"factory" keep their own client-side checks.</summary>
    public string[] Available { get; set; } = System.Array.Empty<string>();

    /// <summary>Research (Tech tab) is possible: aboard the ship and at its cockpit (#1074).</summary>
    public bool ResearchOk { get; set; }

    /// <summary>Ship modules can be built: aboard the ship and a workshop module is fitted (#1074).</summary>
    public bool ShipBuildOk { get; set; }
}

/// <summary>Client → server: "where is the nearest station that would unlock this?" (#1072). The station is
/// a lower-case <c>CraftingStation</c> name, or "research" (the cockpit) / "shipbuild" (the workshop module).</summary>
public sealed class LocateStationIntent
{
    public string Station { get; set; } = string.Empty;
}

/// <summary>Server → client: the answer to <see cref="LocateStationIntent"/> — the nearest matching block
/// (or ship station) position, so the menu can say "Workbench · 12 m ↗" and show it on the compass.</summary>
public sealed class StationLocation
{
    public string Station { get; set; } = string.Empty;

    public bool Found { get; set; }

    /// <summary>"block" (a placed world block) or "ship" (a station/module aboard your parked ship).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The block key found (for the icon + name), e.g. "workbench"; a ship hit names the station cell.</summary>
    public string BlockKey { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }
}
