// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Authoritative state of the player's ship: which modules are built and the shared
/// cargo hold. Effective ship stats are derived from the built modules' definitions.
/// </summary>
public sealed class ShipState
{
    /// <summary>Keys of ship modules currently built (see <c>data/ship_modules.json</c>).</summary>
    public List<string> Modules { get; set; } = new();

    /// <summary>The shared cargo hold; its slot count grows as cargo modules are built.</summary>
    public Inventory Cargo { get; set; } = new(48);

    /// <summary>Identifier of the system/planet/station the ship is currently at.</summary>
    public string CurrentLocationId { get; set; } = string.Empty;

    /// <summary>Ship type/design key (see <c>data/ships.json</c>); drives the hull design + base stats.</summary>
    public string ShipType { get; set; } = "starter";

    /// <summary>
    /// Current hull integrity (space combat, `anf_space_flight.md` §8.4). Reaching 0 disables
    /// the ship and recovers it to its base — there is no permanent ship loss (§8.5). The
    /// maximum is derived from built modules; the server clamps and restores this value.
    /// </summary>
    public float Hull { get; set; } = 100f;

    /// <summary>Current shield charge; regenerates out of combat up to the module-derived maximum.</summary>
    public float Shield { get; set; }

    /// <summary>
    /// True when this ship was destroyed in space combat under the <c>KeepShipOnDeath = false</c> rule:
    /// instead of the free full restore it is left a WRECK on the owner's home pad — hull at zero and a
    /// chunk of the hull carved away. The owner must repair it (the normal own-ship repair flow) before it
    /// can launch again; <see cref="GameServer"/> clears this once the ship is fully repaired.
    /// </summary>
    public bool Downed { get; set; }

    /// <summary>Ship-type key of a player-built ship (no <c>data/ships.json</c> entry — the geometry and the
    /// flight stats are derived from <see cref="BuiltCells"/> instead).</summary>
    public const string CustomShipType = "custom";

    /// <summary>True for a self-built ship (see <see cref="CustomShipType"/>).</summary>
    public bool IsCustom => ShipType == CustomShipType;

    /// <summary>
    /// A self-built ship's voxel hull, serialized as structure-local cells ("x:y:z:blockId;…" — the same
    /// format player stations persist). This is the ship's SOURCE OF TRUTH geometry: the landed object, the
    /// flight-view structure and the derived stats are all rebuilt from it. Empty for content-designed ships.
    /// </summary>
    public string BuiltCells { get; set; } = string.Empty;

    /// <summary>
    /// False while a self-built ship is still under construction: it cannot be switched to, launched or
    /// placed as a normal parked ship until it passes commissioning at its helm (airtight hull, helm,
    /// engine, door, size cap). Content ships are always commissioned.
    /// </summary>
    public bool Commissioned { get; set; } = true;

    /// <summary>World + anchor cell of the construction site while un-commissioned (so a rejoin re-places
    /// the half-built hull where the keel was laid). Unused once commissioned.</summary>
    public string BuildLocationId { get; set; } = string.Empty;
    public int BuildX { get; set; }
    public int BuildY { get; set; }
    public int BuildZ { get; set; }

    public bool HasModule(string moduleKey) => Modules.Contains(moduleKey);
}
