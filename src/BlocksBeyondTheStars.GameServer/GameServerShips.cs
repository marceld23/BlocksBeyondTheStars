using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Ship types/designs and a player's fleet (data-driven `data/ships.json`): the player can
/// craft new ship types, owns multiple ships, and switches which one is **active** (the active
/// ship is the one flown and stamped into the world). Server-authoritative.
///
/// Slice scope: owned ships beyond the starter live in-memory for the session; the active ship
/// persists as today. Full per-ship persistence is a follow-up.
/// </summary>
public sealed partial class GameServer
{
    private readonly Dictionary<string, ShipState> _noFleet = new();

    /// <summary>The served player's owned ships (the ship cursor's fleet); empty placeholder if none.</summary>
    private Dictionary<string, ShipState> _ships => CurrentOrFirst()?.Ships ?? _noFleet;

    /// <summary>The served player's active ship id.</summary>
    private string _activeShipId
    {
        get => CurrentOrFirst()?.ActiveShipId ?? string.Empty;
        set { var s = CurrentOrFirst(); if (s != null) s.ActiveShipId = value; }
    }

    public IReadOnlyDictionary<string, ShipState> OwnedShips => _ships;
    public string ActiveShipId => _activeShipId;

    /// <summary>Installs a player's starter/loaded ship as their initial active ship (called on join).</summary>
    private void RegisterActiveShip(PlayerSession session, ShipState ship)
    {
        session.Ships.Clear();
        session.Ships[ShipId] = ship;
        session.ActiveShipId = ShipId;
    }

    private ShipState BuildShipFromDefinition(ShipDefinition def)
    {
        var ship = new ShipState
        {
            ShipType = def.Key,
            CurrentLocationId = _meta.DefaultPlanetType,
            Hull = def.BaseHull,
            Shield = 0f,
        };

        foreach (var moduleKey in def.StartModules)
        {
            if (_content.GetShipModule(moduleKey) is not null)
            {
                ship.Modules.Add(moduleKey);
            }
        }

        ResizeCargo(ship);
        return ship;
    }

    /// <summary>Adds a ship to the owned fleet without material cost (used by repaired wreck claims).</summary>
    private string AddOwnedShipFromDefinition(ShipDefinition def, string idPrefix)
    {
        string safePrefix = string.IsNullOrWhiteSpace(idPrefix) ? def.Key : idPrefix;
        string id = $"{safePrefix}_{def.Key}_{_ships.Count}";
        _ships[id] = BuildShipFromDefinition(def);
        BroadcastOwnedShips();
        return id;
    }

    /// <summary>Crafts a new ship of the given type (validates blueprint + materials). Returns success + message.</summary>
    public (bool Ok, string Message) CraftShip(string playerId, string shipType)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return (false, "No such player.");
        }

        Serve(session); // operate on this player's own fleet/ship

        var def = _content.GetShip(shipType);
        if (def is null)
        {
            return (false, "Unknown ship type.");
        }

        var p = session.State;
        if (!string.IsNullOrEmpty(def.RequiredBlueprint) && !p.UnlockedBlueprints.Contains(def.RequiredBlueprint!))
        {
            return (false, "Blueprint not unlocked.");
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        var pool = new MaterialPool(_content, p, _ship);
        if (!free)
        {
            if (!pool.Has(def.CraftCost))
            {
                return (false, "Missing materials.");
            }

            pool.Remove(def.CraftCost);
        }

        string id = AddOwnedShipFromDefinition(def, shipType);

        SendInventory(session);
        Send(session, new ServerMessage { Text = $"Crafted ship: {shipType}" });
        return (true, id);
    }

    /// <summary>Switches the active ship to one the player owns and applies the new design IMMEDIATELY wherever
    /// the player is — re-stamping the hull on a planet and rebuilding the flight-view voxel ship in space — so
    /// the change shows at once, not only after the next launch/landing.</summary>
    public bool SwitchShip(string shipId)
    {
        if (!_ships.TryGetValue(shipId, out var ship))
        {
            return false;
        }

        _activeShipId = shipId;
        RecomputeShipCombatStats(); // _ship now resolves to the newly active ship

        if (_shipPlaced)
        {
            // Landed: replace the parked ship object with the new design on the same pad — the placement
            // broadcast swaps the object on every client at once (no world re-stream needed).
            PlaceLandedShip();
            if (_current is { Joined: true })
            {
                SendShipStations(_current);
                SendDoors(_current);
            }
        }

        // In space (piloting or EVA): rebuild the ship's voxel structure for the new design + re-send it so the
        // flight view renders the new ship at once. The old structure is replaced under the same id.
        if (_current != null && _playerInstance.TryGetValue(_current.State.PlayerId, out var iid)
            && _spaceInstances.TryGetValue(iid, out var inst))
        {
            var rebuilt = BuildShipStructure(_current.State.PlayerId);
            inst.Structures[rebuilt.Id] = rebuilt;
            foreach (var pid in inst.Players)
            {
                if (FindSessionByPlayerId(pid) is { } s)
                {
                    SendShipDesign(s, rebuilt);
                }
            }
        }

        BroadcastOwnedShips();
        return true;
    }

    private void SendOwnedShips(PlayerSession session)
        => Send(session, BuildOwnedShips());

    private void BroadcastOwnedShips()
    {
        if (_current is { Joined: true })
        {
            Send(_current, BuildOwnedShips()); // a fleet is per-player — only its owner needs it
        }
    }

    private OwnedShips BuildOwnedShips()
    {
        var list = new List<NetOwnedShip>();
        foreach (var kv in _ships)
        {
            list.Add(new NetOwnedShip { Id = kv.Key, Type = kv.Value.ShipType, Active = kv.Key == _activeShipId });
        }

        return new OwnedShips { Ships = list.ToArray() };
    }

    private void HandleCraftShip(PlayerSession session, CraftShipIntent intent)
        => CraftShip(session.State.PlayerId, intent.ShipType);

    private void HandleSwitchShip(PlayerSession session, SwitchShipIntent intent)
    {
        if (!SwitchShip(intent.ShipId))
        {
            Reject(session, "switch_ship", "You don't own that ship.");
        }
    }
}
