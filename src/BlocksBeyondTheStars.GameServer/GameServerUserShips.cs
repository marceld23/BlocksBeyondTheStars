// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3520: the user ships (<c>usercontent/ship_types/</c>, <c>GameContent.UserShips</c>) on the server. <c>GetShip</c> and
/// <c>GetShipLayout</c> resolve them like shipped ships, so a hull, a landing and a flight structure need nothing extra; what
/// lives here is who may PICK one: the raiders and trader flights take the <c>npc</c> ones (appended after the shipped
/// designs, so a server without user ships draws exactly as before; a hull wider than a landing pad never sets down),
/// and the craft list takes the <c>craft</c> ones only with <see cref="Shared.Configuration.ServerConfig.AllowUserShipCrafting"/>.
/// The wreck pools and the creative "all ships" grant stay on the shipped ships.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Why <paramref name="shipType"/> may not be crafted although it resolves — null for a shipped ship, and for a
    /// user ship that names the <c>craft</c> use on a server that allows it. Without both, a <c>CraftShipIntent</c> naming a
    /// user ship would hand out whatever the file's cost says (possibly nothing).</summary>
    private string? UserShipCraftRefusal(string shipType)
    {
        if (_content.UserShipByKey(shipType) is not { } user)
        {
            return null;
        }

        if (!_config.AllowUserShipCrafting)
        {
            return "User ships cannot be crafted on this server.";
        }

        return user.Has(UserShipUses.Craft) ? null : "This user ship is not craftable.";
    }

    /// <summary>The designs of the user ships NPCs may fly (the <c>npc</c> use), in load order — for a landing only those
    /// whose hull fits a landing pad.</summary>
    private IEnumerable<ShipDefinition> NpcUserShipDesigns(bool landing)
        => _content.UserShipsFor(UserShipUses.Npc).Where(u => !landing || u.FitsPad).Select(u => u.Definition);

    /// <summary>A design's weight factor inside an NPC pool: a user ship's own weight, 1 for every shipped design.</summary>
    private int UserShipWeight(ShipDefinition design) => _content.UserShipByKey(design.Key)?.Weight ?? 1;

    /// <summary>True when <paramref name="shipType"/> is a user ship too wide for a landing pad (it never sets down).</summary>
    private bool UserShipTooWideToLand(string shipType) => _content.UserShipByKey(shipType) is { FitsPad: false };

    /// <summary>A user ship in the fleet message: the client's content does not know it, so its name and flight values ride
    /// along (the #949 pattern of the self-built ships). One whose type no longer resolves (its file is gone or dropped) keeps
    /// the name it joined the fleet with (<see cref="ShipState.DisplayName"/>) instead of a locale key nobody has.</summary>
    private void FillUserShipFleetEntry(NetOwnedShip entry, ShipState ship)
    {
        if (_content.UserShipByKey(ship.ShipType) is { } user)
        {
            entry.Name = user.Definition.DisplayName ?? user.Key;
            entry.FlightSpeed = user.Definition.FlightSpeed;
            entry.Handling = user.Definition.Handling;
            ship.DisplayName = entry.Name; // the latest name, should the file go away later
        }
        else if (_content.GetShip(ship.ShipType) is null)
        {
            entry.Name = ship.DisplayName.Length > 0 ? ship.DisplayName : ship.ShipType;
        }
    }

    /// <summary>
    /// The flight hull of the served player's ship when its type resolves to nothing — a user ship whose file was removed or
    /// dropped by the loader. Its stored cells (<see cref="ShipState.BuiltCells"/>) stand when the fleet entry has them; else
    /// the starter hull stands in WITHOUT the ship's persisted edits (they belong to the other hull and would be stamped and
    /// kept over the starter's cells). Warned once per type and server run; the ship keeps its type, so it comes back whole
    /// when the file returns.
    /// </summary>
    private SpaceStructure BuildUnresolvedShipStructure(string ownerId)
    {
        bool cells = !string.IsNullOrEmpty(_ship.BuiltCells);
        if (_warnedLayoutIds.Add("ownedship:" + _ship.ShipType))
        {
            _log.Warn($"Ship type '{_ship.ShipType}' ('{_ship.DisplayName}') is no longer in the content (a removed or dropped user ship) — "
                + (cells ? "it stands from its stored cells." : "the starter hull stands in for it, without its edits."));
        }

        return cells
            ? BuildCustomShipStructure("ship:" + ownerId, ownerId, _ship, commissioned: true, applyEdits: true)
            : BuildShipStructureFrom("ship:" + ownerId, ownerId, _content.GetShip("starter"), persistEdits: false);
    }

    /// <summary>Test seam (#3520): the flight hull the served player's active ship gets now.</summary>
    internal SpaceStructure ActiveShipStructureForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
        }

        return BuildShipStructure(playerId);
    }

    /// <summary>Test seam: the raider design candidates in pool order.</summary>
    internal IReadOnlyList<string> RaiderDesignPoolForTest() => RaiderDesignPool().Select(d => d.Key).ToList();

    /// <summary>Test seam: the trader ship candidates in pool order (<paramref name="landing"/> = a planet-side arrival).</summary>
    internal IReadOnlyList<string> TraderShipPoolForTest(bool landing) => TraderShipPool(landing).Select(d => d.Key).ToList();

    /// <summary>Test seam: the voxel hull an NPC flying <paramref name="shipType"/> gets.</summary>
    internal SpaceStructure NpcShipStructureForTest(string shipType) => BuildNpcShipStructure("test_npc", shipType);

    /// <summary>Test seam: the fleet message the served player would get now.</summary>
    internal OwnedShips OwnedShipsMessageForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            Serve(session);
        }

        return BuildOwnedShips();
    }
}
