// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3520: one ship from the ship editor's user-content folder, <c>usercontent/ship_types/&lt;key&gt;.json</c> — the definition
/// and its voxel layout in ONE file, with literal texts instead of locale keys (a player's ship has no locale entry). The
/// content loader turns it into a <see cref="UserShip"/> (<c>GameContent.SetUserShips</c>): checked, clamped to the editor's
/// ranges and filled with the defaults <c>tools/merge_ship.py</c> would fill. A file that fails a check is reported and
/// dropped; it never breaks the load.
/// </summary>
public sealed class UserShipFile
{
    /// <summary>The ship-type key; empty = the file name.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The literal display name (empty = the key).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The literal description.</summary>
    public string Description { get; set; } = string.Empty;

    public float BaseHull { get; set; } = 100f;
    public float BaseShield { get; set; }
    public float FlightSpeed { get; set; } = 1f;
    public float Handling { get; set; } = 1f;

    /// <summary>Blueprint a crafting player must have unlocked (empty = none). Only read when crafting is allowed.</summary>
    public string? RequiredBlueprint { get; set; }

    /// <summary>The crafting cost. Only read when crafting is allowed.</summary>
    public List<ItemAmount> CraftCost { get; set; } = new();

    /// <summary>Modules the ship is built with; empty = derived from the layout's station tiles.</summary>
    public List<string> StartModules { get; set; } = new();

    /// <summary>What the world may do with the ship (<see cref="UserShipUses"/>). Absent = <see cref="UserShipUses.Default"/>;
    /// an empty list = nothing (the ship only lives in the editor).</summary>
    public List<string>? Uses { get; set; }

    /// <summary>The structure pack it belongs to (empty = <c>default</c>): every pool that offers user ships takes it only on a
    /// world whose enabled packs include it.</summary>
    public string Pack { get; set; } = string.Empty;

    /// <summary>Draw weight inside the pools that offer user ships (a multiplier on the pool's own weight; at least 1).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>The voxel layout, the same shape as a <c>data/ship_layouts/*.json</c> file.</summary>
    public ShipLayout? Layout { get; set; }
}

/// <summary>The <see cref="UserShipFile.Uses"/> tokens: which consumers may pick a user ship.</summary>
public static class UserShipUses
{
    /// <summary>Bandit raiders and trader flights.</summary>
    public const string Npc = "npc";

    /// <summary>Wrecks of new worlds.</summary>
    public const string Wreck = "wreck";

    /// <summary>The craft list — only when the server also allows it (<c>ServerConfig.AllowUserShipCrafting</c>): otherwise
    /// any client could craft a free ship with a <c>CraftShipIntent</c>.</summary>
    public const string Craft = "craft";

    /// <summary>The uses of a file that names none: NPC flights only. <c>wreck</c> and <c>craft</c> must be opted in — a
    /// planet wreck can be repaired and claimed, so it hands the ship to a player just like crafting does.</summary>
    public static readonly IReadOnlyList<string> Default = new[] { Npc };

    /// <summary>True for a token this version knows.</summary>
    public static bool IsKnown(string use) => use == Npc || use == Wreck || use == Craft;
}

/// <summary>
/// #3520: a loaded user ship — its definition (resolved by <c>GameContent.GetShip</c> after the shipped ones), its layout
/// (<c>GameContent.GetShipLayout</c> under the same key), the uses and the pool weight. Never part of
/// <c>GameContent.Ships</c>: the wreck pools of existing worlds and the creative "all ships" grant enumerate that.
/// </summary>
public sealed class UserShip
{
    public UserShip(ShipDefinition definition, ShipLayout layout, IReadOnlyList<string> uses, string pack, int weight, bool fitsPad)
    {
        Definition = definition;
        Layout = layout;
        Uses = uses;
        Pack = pack;
        Weight = weight;
        FitsPad = fitsPad;
    }

    public string Key => Definition.Key;

    /// <summary>The checked, clamped definition (<see cref="ShipDefinition.DisplayName"/> carries the literal name).</summary>
    public ShipDefinition Definition { get; }

    /// <summary>The layout, keyed like the ship.</summary>
    public ShipLayout Layout { get; }

    /// <summary>The known use tokens, lower-case.</summary>
    public IReadOnlyList<string> Uses { get; }

    public string Pack { get; }

    public int Weight { get; }

    /// <summary>True when every cell lies inside a 17 × 17 landing square (x/z): only such a hull may set down on a pad.</summary>
    public bool FitsPad { get; }

    /// <summary>Whether the ship carries the use token <paramref name="use"/>.</summary>
    public bool Has(string use)
    {
        foreach (var u in Uses)
        {
            if (u == use)
            {
                return true;
            }
        }

        return false;
    }
}
