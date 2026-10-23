// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client asks to build a ship module from cargo materials (gated by blueprint + station).</summary>
public sealed class BuildShipModuleIntent
{
    public string ModuleKey { get; set; } = string.Empty;
}

/// <summary>Client asks to remove a fitted module from the active ship (#1269): aboard, at the workshop,
/// non-mandatory and not a hull station; the parts come back at the salvage rate.</summary>
public sealed class UninstallShipModuleIntent
{
    public string ModuleKey { get; set; } = string.Empty;
}

/// <summary>Client steps from the flight view (or an EVA) into the ship's walkable interior in space.</summary>
public sealed class EnterShipIntent
{
}

/// <summary>Client takes the helm from inside the ship — back to the flight view at the parked ship.</summary>
public sealed class ExitShipIntent
{
}

/// <summary>Client repairs its OWN active ship. <c>Mode</c> = "all" restores everything affordable at
/// once (hull stat + every missing design hull cell) — the cockpit "Repair ship" action. <c>Mode</c> =
/// "cell" refills one specific missing design cell (X/Y/Z, structure-local) — guided field/EVA repair.
/// Both pay an existing metal item (no new resource); the server derives the required block/material.</summary>
public sealed class RepairShipIntent
{
    public string Mode { get; set; } = "all"; // "all" | "cell"
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

/// <summary>Own-ship repair readout: current hull, count of missing design hull cells, whether anything
/// needs repair and whether the player can currently afford the full job, plus a compact material summary.
/// Sent when the player opens the cockpit and after each repair so the client can offer "Repair ship".</summary>
public sealed class ShipRepairStatus
{
    public float Hull { get; set; }
    public float HullMax { get; set; }

    /// <summary>Missing design (hull/structure) voxel cells — holes carved by EVA mining that should be filled.</summary>
    public int MissingCells { get; set; }

    /// <summary>True when hull &lt; max OR there is at least one missing design cell.</summary>
    public bool NeedsRepair { get; set; }

    /// <summary>True when the player can afford the FULL repair right now (a partial repair still runs otherwise).</summary>
    public bool CanAfford { get; set; }

    /// <summary>Compact "item:count,item:count" summary of the full repair cost (for the HUD hint).</summary>
    public string Needs { get; set; } = string.Empty;

    /// <summary>The live structure the missing cells below belong to ("ship:&lt;playerId&gt;") — the client maps
    /// them into the world through that structure's origin. Additive (#1368); empty from an older server.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>Structure-local coordinates of the missing design hull cells (parallel arrays, at most
    /// <see cref="MaxListedMissingCells"/> of them — <see cref="MissingCells"/> stays the true count) so the
    /// client can outline every breach in the world while the repair panel is up (#1368). Additive.</summary>
    public int[] MissingX { get; set; } = System.Array.Empty<int>();
    public int[] MissingY { get; set; } = System.Array.Empty<int>();
    public int[] MissingZ { get; set; } = System.Array.Empty<int>();

    /// <summary>Upper bound on the listed breach cells — a fully wrecked custom hull can have thousands.</summary>
    public const int MaxListedMissingCells = 256;
}

/// <summary>Client uses a ship station it is standing next to (medbay heal-tank, cockpit, quarters, ...).</summary>
public sealed class UseStationIntent
{
    public string Station { get; set; } = string.Empty;
}

/// <summary>Client crafts a new ship of the given type (data-driven `ships.json`).</summary>
public sealed class CraftShipIntent
{
    public string ShipType { get; set; } = string.Empty;
}

/// <summary>Client switches its active ship to one it owns.</summary>
public sealed class SwitchShipIntent
{
    public string ShipId { get; set; } = string.Empty;
}

/// <summary>Where the player's ship hull stands in the world (for the HUD minimap / compass).</summary>
public sealed class ShipPlacement
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>A station inside the ship the player can interact with.</summary>
public sealed class NetShipStation
{
    public string Type { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>The interactive stations inside the ship, sent on join.</summary>
public sealed class ShipStations
{
    public NetShipStation[] Stations { get; set; } = System.Array.Empty<NetShipStation>();
}

/// <summary>One ship the player owns.</summary>
public sealed class NetOwnedShip
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Active { get; set; }

    /// <summary>Flight stats for a ship the client cannot resolve from <c>data/ships.json</c> (a self-built
    /// "custom" ship, whose stats derive from its geometry). 0 = resolve from content as before.</summary>
    public float FlightSpeed { get; set; }
    public float Handling { get; set; }

    /// <summary>False while a self-built ship is still under construction (not switchable/flyable yet).</summary>
    public bool Commissioned { get; set; } = true;

    /// <summary>The ship's fitted module keys (#1268) — the Fleet tab lists what each hangar ship carries.
    /// New field on an existing contractless MessagePack message: an older client ignores it.</summary>
    public string[] Modules { get; set; } = System.Array.Empty<string>();
}

/// <summary>The player's owned ships and which is active (sent on join and on change).</summary>
public sealed class OwnedShips
{
    public NetOwnedShip[] Ships { get; set; } = System.Array.Empty<NetOwnedShip>();
}
