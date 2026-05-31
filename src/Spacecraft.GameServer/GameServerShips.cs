using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.State;

namespace Spacecraft.GameServer;

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
    private readonly Dictionary<string, ShipState> _ships = new();
    private string _activeShipId = ShipId;

    public IReadOnlyDictionary<string, ShipState> OwnedShips => _ships;
    public string ActiveShipId => _activeShipId;

    /// <summary>Registers the loaded/starter ship as the initial active ship (called from Start).</summary>
    private void RegisterActiveShip(ShipState ship)
    {
        _ships.Clear();
        _ships[ShipId] = ship;
        _activeShipId = ShipId;
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

    /// <summary>Switches the active ship to one the player owns; re-stamps the hull design.</summary>
    public bool SwitchShip(string shipId)
    {
        if (!_ships.TryGetValue(shipId, out var ship))
        {
            return false;
        }

        _activeShipId = shipId;
        _ship = ship;
        RecomputeShipCombatStats();
        if (_shipStamped)
        {
            StampShip(); // the new design may be a different size
        }

        BroadcastOwnedShips();
        return true;
    }

    private void SendOwnedShips(PlayerSession session)
        => Send(session, BuildOwnedShips());

    private void BroadcastOwnedShips()
        => Broadcast(BuildOwnedShips());

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
