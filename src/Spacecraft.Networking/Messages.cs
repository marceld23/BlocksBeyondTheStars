namespace Spacecraft.Networking.Messages;

// Wire messages. Plain classes with parameterless constructors and get/set properties so
// they serialize cleanly with MessagePack's contractless resolver (no attributes needed).
//
// Client -> Server messages are *intents*: the client asks; the server decides.
// Server -> Client messages are authoritative *state*.

// ---------------- Client -> Server (intents) ----------------

public sealed class JoinRequest
{
    public int ProtocolVersion { get; set; } = Spacecraft.Networking.Protocol.Version;
    public string PlayerName { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public sealed class MoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
}

public sealed class MineBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class PlaceBlockIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ItemKey { get; set; } = string.Empty;
}

public sealed class CraftIntent
{
    public string RecipeKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

public sealed class UnlockBlueprintIntent
{
    public string BlueprintKey { get; set; } = string.Empty;
}

public sealed class SelectHotbarIntent
{
    public int Slot { get; set; }
}

/// <summary>Client asks for the star map (cockpit). Server replies with <see cref="StarMapData"/>.</summary>
public sealed class RequestStarMap
{
}

/// <summary>
/// An admin/cheat command (technical requirements / `anf_admin_einstellungen.md` §10). The
/// server checks the player's role + that cheats are enabled, applies it authoritatively
/// and logs it. Commands: teleport_to_player, teleport_to_location, give_item, set_time,
/// set_weather, fly, godmode, instant_build.
/// </summary>
public sealed class AdminCommandIntent
{
    public string Command { get; set; } = string.Empty;
    public string? TargetPlayer { get; set; }
    public string? StringArg { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int IntArg { get; set; }
}

// --- Ship docking (anf_space_flight.md §13) ---

/// <summary>Client asks to dock with another player's ship.</summary>
public sealed class DockRequestIntent
{
    public string TargetPlayer { get; set; } = string.Empty;
}

/// <summary>The docking-request target accepts or declines a pending request.</summary>
public sealed class DockResponseIntent
{
    public string Requester { get; set; } = string.Empty;
    public bool Accept { get; set; }
}

/// <summary>Client asks to undock from its current docking partner.</summary>
public sealed class UndockIntent
{
}

// --- Ship building & space flight / combat (anf_space_flight.md §6-12) ---

/// <summary>Client asks to build a ship module from cargo materials (gated by blueprint + station).</summary>
public sealed class BuildShipModuleIntent
{
    public string ModuleKey { get; set; } = string.Empty;
}

/// <summary>Client asks to launch into free space flight around its current location.</summary>
public sealed class EnterSpaceIntent
{
}

/// <summary>Client asks to leave the space instance and return to the surface/base.</summary>
public sealed class LeaveSpaceIntent
{
}

/// <summary>Client fires a built ship weapon at a space entity. The server validates and resolves the hit.</summary>
public sealed class FireWeaponIntent
{
    public string WeaponKey { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
}

/// <summary>Client attacks a planet enemy with the held tool/weapon. The server resolves the hit.</summary>
public sealed class AttackEntityIntent
{
    public string EntityId { get; set; } = string.Empty;
}

/// <summary>Client eats/uses a consumable item (food heals, poison harms). The server applies it.</summary>
public sealed class ConsumeItemIntent
{
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client loots a nearby container (salvage capsule / corpse). Server validates proximity + transfers.</summary>
public sealed class LootContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>Client reports its ship's position while flying in space (server validates + checks collisions).</summary>
public sealed class ShipMoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Client dismantles a crafted item at the workshop, recovering some of its components.</summary>
public sealed class DisassembleIntent
{
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client loads food from its inventory into the suit's ration dispenser (auto-eaten when hungry).</summary>
public sealed class LoadRationIntent
{
    public string ItemKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

/// <summary>Client uses the suit teleporter to recall to its ship (server validates device/cooldown/energy).</summary>
public sealed class TeleportToShipIntent { }

/// <summary>Client scans a subject with the handheld scanner (creature species id / block key).</summary>
public sealed class ScanIntent
{
    public string SubjectType { get; set; } = string.Empty; // "creature" | "block"
    public string SubjectKey { get; set; } = string.Empty;
}

/// <summary>Client scans a space entity (asteroid) with the ship scanner to reveal its resources.</summary>
public sealed class ScanEntityIntent
{
    public string EntityId { get; set; } = string.Empty;
}

/// <summary>Result of a scan: a readout + threat, and any knowledge gained (first scan only).</summary>
public sealed class ScanResult
{
    public string Subject { get; set; } = string.Empty;
    public string Info { get; set; } = string.Empty;
    public string Threat { get; set; } = "—";
    public bool FirstTime { get; set; }
    public int KnowledgeGained { get; set; }
    public int KnowledgeTotal { get; set; }
}

/// <summary>An item + quantity in a trade offer.</summary>
public sealed class NetTradeItem
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Client asks another nearby player to trade.</summary>
public sealed class TradeRequestIntent
{
    public string TargetPlayer { get; set; } = string.Empty;
}

/// <summary>Client accepts/declines a pending trade request.</summary>
public sealed class TradeRespondIntent
{
    public bool Accept { get; set; }
}

/// <summary>Client sets the items on its side of the open trade (replaces the previous offer).</summary>
public sealed class TradeOfferIntent
{
    public NetTradeItem[] Items { get; set; } = System.Array.Empty<NetTradeItem>();
}

/// <summary>Client confirms ("ready") the current trade; the swap happens when both sides confirm.</summary>
public sealed class TradeConfirmIntent { }

/// <summary>Client cancels the open trade (or a pending request).</summary>
public sealed class TradeCancelIntent { }

/// <summary>Server view of the open trade for one participant (both offers + both ready states).</summary>
public sealed class TradeUpdate
{
    public string Partner { get; set; } = string.Empty;
    public NetTradeItem[] MyOffer { get; set; } = System.Array.Empty<NetTradeItem>();
    public NetTradeItem[] TheirOffer { get; set; } = System.Array.Empty<NetTradeItem>();
    public bool MyConfirmed { get; set; }
    public bool TheirConfirmed { get; set; }
}

/// <summary>A trade ended — completed (items swapped) or cancelled.</summary>
public sealed class TradeClosed
{
    public bool Completed { get; set; }
    public string Reason { get; set; } = string.Empty;
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

/// <summary>Client tells the server its avatar colours (packed 0xRRGGBB) so other players can see them.</summary>
public sealed class SetAppearanceIntent
{
    public int Skin { get; set; }
    public int Torso { get; set; }
    public int Arms { get; set; }
    public int Legs { get; set; }
}

// ---------------- Server -> Client (state) ----------------

public sealed class JoinAccepted
{
    public int ProtocolVersion { get; set; } = Spacecraft.Networking.Protocol.Version;
    public string PlayerId { get; set; } = string.Empty;
    public long WorldSeed { get; set; }
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>Friendly name of the body the player is on, and its star system (for the HUD).</summary>
    public string PlanetName { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
}

public sealed class JoinRejected
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ChunkDataMessage
{
    public int Cx { get; set; }
    public int Cy { get; set; }
    public int Cz { get; set; }
    public ushort[] Blocks { get; set; } = System.Array.Empty<ushort>();
}

public sealed class BlockChanged
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }
}

/// <summary>A single item stack in an inventory update.</summary>
public sealed class NetItemStack
{
    public int Slot { get; set; }
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class InventoryUpdate
{
    public NetItemStack[] Personal { get; set; } = System.Array.Empty<NetItemStack>();
    public NetItemStack[] Cargo { get; set; } = System.Array.Empty<NetItemStack>();
}

public sealed class PlayerStateUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float SuitEnergy { get; set; }
    public float Hunger { get; set; }

    /// <summary>Whether the player is currently inside their ship (enables cargo crafting, oxygen regen).</summary>
    public bool AboardShip { get; set; }
}

public sealed class CraftResult
{
    public bool Success { get; set; }
    public string RecipeKey { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class ActionRejected
{
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ServerMessage
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// The active world rule set, sent to a client right after it joins so it can show the
/// server's mode/rules and explain when an action is blocked by them.
/// </summary>
public sealed class ServerRules
{
    public string GameMode { get; set; } = string.Empty;
    public string Pvp { get; set; } = string.Empty;
    public string WeaponMode { get; set; } = string.Empty;
    public string AggressiveAliens { get; set; } = string.Empty;
    public string EnvironmentalHazards { get; set; } = string.Empty;
    public string DeathPenalty { get; set; } = string.Empty;
    public bool KeepInventoryOnDeath { get; set; }
    public bool OxygenEnabled { get; set; }
    public bool AdminCheatsActive { get; set; }
}

// --- Missions ---

public sealed class RequestMissions { }

public sealed class AcceptMissionIntent
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class TurnInMissionIntent
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class NetMissionObjective
{
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
}

public sealed class NetReward
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Client-supplied mission when a player creates a mission for others.</summary>
public sealed class CreateMissionIntent
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NetMissionObjective[] Objectives { get; set; } = System.Array.Empty<NetMissionObjective>();
    public NetReward[] Rewards { get; set; } = System.Array.Empty<NetReward>();
}

public sealed class NetMission
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public NetMissionObjective[] Objectives { get; set; } = System.Array.Empty<NetMissionObjective>();
    public NetReward[] Rewards { get; set; } = System.Array.Empty<NetReward>();
}

public sealed class MissionList
{
    public NetMission[] Available { get; set; } = System.Array.Empty<NetMission>();
    public NetMission[] Active { get; set; } = System.Array.Empty<NetMission>();
}

public sealed class MissionResult
{
    public bool Success { get; set; }
    public string MissionId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

// --- Star map ---

public sealed class NetBody
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? PlanetType { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class NetStarSystem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float MapX { get; set; }
    public float MapY { get; set; }
    public NetBody[] Bodies { get; set; } = System.Array.Empty<NetBody>();
}

public sealed class StarMapData
{
    public NetStarSystem[] Systems { get; set; } = System.Array.Empty<NetStarSystem>();
    public string ActiveLocationId { get; set; } = string.Empty;
}

/// <summary>The player's current vitals are respawning at the ship heal-tank (Medbay).</summary>
public sealed class RespawnNotice
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool SalvageCapsuleDropped { get; set; }
}

// --- Ship docking (anf_space_flight.md §13) ---

/// <summary>Notifies a player that another player has requested to dock with them.</summary>
public sealed class DockRequestNotice
{
    public string Requester { get; set; } = string.Empty;
}

/// <summary>Authoritative docking state change for the receiving player.</summary>
public sealed class DockStatus
{
    /// <summary>The other player in the docking (the partner).</summary>
    public string Partner { get; set; } = string.Empty;

    /// <summary>True when now docked, false when undocked / declined.</summary>
    public bool Docked { get; set; }

    public string Reason { get; set; } = string.Empty;
}

// --- Space flight / combat & planet enemies (anf_space_flight.md §6-12) ---

/// <summary>A combat entity in space (asteroid, drone, ufo, cruiser) or a planet enemy.</summary>
public sealed class NetCombatEntity
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Hostile { get; set; }
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Authoritative ship hull/shield, sent on join and whenever they change.</summary>
public sealed class ShipCombatStatus
{
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float Shield { get; set; }
    public float ShieldMax { get; set; }
}

/// <summary>Snapshot of the space instance a player is in (sent on entry and on change).</summary>
public sealed class SpaceState
{
    public string InstanceId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public NetCombatEntity[] Entities { get; set; } = System.Array.Empty<NetCombatEntity>();
}

/// <summary>A space entity was destroyed (asteroid mined or enemy defeated).</summary>
public sealed class SpaceEntityDestroyed
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>The player has left space (manually, or because the ship was disabled — no permanent loss).</summary>
public sealed class SpaceClosed
{
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when the exit was forced by ship defeat (ship recovered to base, player respawned).</summary>
    public bool ShipDisabled { get; set; }
}

/// <summary>Snapshot of hostile entities near the player on the planet surface.</summary>
public sealed class PlanetEnemyList
{
    public NetCombatEntity[] Enemies { get; set; } = System.Array.Empty<NetCombatEntity>();
}

/// <summary>A planet enemy was defeated.</summary>
public sealed class PlanetEnemyDefeated
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// A live procedural creature near the player, with enough of its species descriptor for the
/// client's parametric blocky renderer to draw it (appearance) and label it (habitat/behaviour).
/// </summary>
public sealed class NetCreature
{
    public string Id { get; set; } = string.Empty;
    public string SpeciesId { get; set; } = string.Empty;
    public string NameKey { get; set; } = "creature.generic.name";

    public bool Hostile { get; set; }
    public bool Asleep { get; set; }
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    // Species traits (string enums) + parametric appearance.
    public string Habitat { get; set; } = "Land";
    public string Activity { get; set; } = "Diurnal";
    public string Temperament { get; set; } = "Passive";
    public float Size { get; set; } = 1f;
    public int Legs { get; set; } = 4;
    public bool HasWings { get; set; }
    public bool HasTail { get; set; }
    public int BodySegments { get; set; } = 1;
    public int ColorRgb { get; set; } = 0xFFFFFF;
    public bool Glows { get; set; }
}

/// <summary>Snapshot of live creatures (fauna) near the player on the planet surface.</summary>
public sealed class CreatureList
{
    public NetCreature[] Creatures { get; set; } = System.Array.Empty<NetCreature>();
}

/// <summary>A lootable container in the world — a salvage capsule or a defeated player's corpse.</summary>
public sealed class NetContainer
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "container";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int ItemCount { get; set; }
}

/// <summary>Lootable containers on the current planet (salvage capsules / corpses), sent on join + on change.</summary>
public sealed class ContainerList
{
    public NetContainer[] Containers { get; set; } = System.Array.Empty<NetContainer>();
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

/// <summary>Authoritative state of another player, for rendering them (avatar + nameplate).</summary>
public sealed class PlayerPresence
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    // Avatar colours, packed 0xRRGGBB.
    public int Skin { get; set; }
    public int Torso { get; set; }
    public int Arms { get; set; }
    public int Legs { get; set; }
}

/// <summary>A player left; the client removes their avatar.</summary>
public sealed class PlayerLeft
{
    public string PlayerId { get; set; } = string.Empty;
}

/// <summary>One ship the player owns.</summary>
public sealed class NetOwnedShip
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Active { get; set; }
}

/// <summary>The player's owned ships and which is active (sent on join and on change).</summary>
public sealed class OwnedShips
{
    public NetOwnedShip[] Ships { get; set; } = System.Array.Empty<NetOwnedShip>();
}

/// <summary>Authoritative day/night + weather + sun colour for the current planet (World systems).</summary>
public sealed class WorldEnvironment
{
    /// <summary>Time of day, 0..1 (0 = midnight, 0.5 = noon).</summary>
    public float TimeOfDay { get; set; }

    /// <summary>Full day length in seconds (the client advances time locally between updates).</summary>
    public float DayLengthSeconds { get; set; } = 600f;

    /// <summary>clear / clouds / rain / storm.</summary>
    public string Weather { get; set; } = "clear";

    /// <summary>0..1 weather strength.</summary>
    public float Intensity { get; set; }

    /// <summary>Sun/star light colour, packed 0xRRGGBB.</summary>
    public int SunColor { get; set; } = 0xFFF6E8;

    /// <summary>Whether the planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool Breathable { get; set; }

    /// <summary>Space sky on the surface (black + stars) — landable asteroids / airless bodies.</summary>
    public bool SpaceSky { get; set; }
}
