namespace BlocksBeyondTheStars.Networking.Messages;

// Wire messages. Plain classes with parameterless constructors and get/set properties so
// they serialize cleanly with MessagePack's contractless resolver (no attributes needed).
//
// Client -> Server messages are *intents*: the client asks; the server decides.
// Server -> Client messages are authoritative *state*.

// ---------------- Client -> Server (intents) ----------------

public sealed class JoinRequest
{
    public int ProtocolVersion { get; set; } = BlocksBeyondTheStars.Networking.Protocol.Version;
    public string PlayerName { get; set; } = string.Empty;
    public string? Password { get; set; }

    /// <summary>Per-install secret proving name ownership (name verification): the first join under a
    /// name claims it; later joins must present the same token or are rejected. Only its hash is stored
    /// server-side. Optional — clients without one leave the name unclaimed.</summary>
    public string? Token { get; set; }

    /// <summary>The player's chosen UI language ("en"/"de"). The server remembers it so dynamic, server-authored
    /// text (item 15: LLM NPC greetings) is generated in the player's language. Defaults to English.</summary>
    public string Locale { get; set; } = "en";
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

    /// <summary>Optional label carried when placing a labelled block (a radio beacon); ignored otherwise.</summary>
    public string Label { get; set; } = string.Empty;
}

public sealed class CraftIntent
{
    public string RecipeKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

/// <summary>The client (which owns on-foot movement) reports a hard landing; the server applies fall damage
/// scaled by how far over a safe impact speed it was.</summary>
public sealed class FallDamageIntent
{
    public float ImpactSpeed { get; set; }
}

public sealed class UnlockBlueprintIntent
{
    public string BlueprintKey { get; set; } = string.Empty;
}

public sealed class SelectHotbarIntent
{
    public int Slot { get; set; }
}

/// <summary>
/// Client → server: rearrange the personal inventory by swapping two slots (B58 — customising the quick-bar,
/// which is just inventory slots 0–8). The server validates indices + swaps. <see cref="ToSlot"/> = -1 means
/// "stow out of the quick-bar": move the item into the first free backpack slot (≥ the quick-bar size).
/// </summary>
public sealed class MoveItemIntent
{
    public int FromSlot { get; set; }
    public int ToSlot { get; set; }
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

/// <summary>Client steps from the flight view (or an EVA) into the ship's walkable interior in space.</summary>
public sealed class EnterShipIntent
{
}

/// <summary>Client takes the helm from inside the ship — back to the flight view at the parked ship.</summary>
public sealed class ExitShipIntent
{
}

/// <summary>Client asks the server to flush the world + all players to disk now (an explicit save, on top
/// of the periodic autosave).</summary>
public sealed class SaveGameIntent
{
}

/// <summary>Client fires the ship's tractor beam as a manual sweep (quick-bar): pull nearby salvage in.</summary>
public sealed class TractorPullIntent
{
}

/// <summary>Client asks to leave the space instance and return to the surface/base.</summary>
public sealed class LeaveSpaceIntent
{
    /// <summary>The body to land on. Empty (or the current body) = land back where you launched. A
    /// different body in the same system = land there (you flew to it in system-scale flight). Cross-
    /// system landing is not offered — use a hyperspace jump (the star map) instead.</summary>
    public string DestinationBodyId { get; set; } = string.Empty;

    /// <summary>Which fixed landing pad to touch down on (item 38); -1 = auto-pick the first free pad.</summary>
    public int PadIndex { get; set; } = -1;
}

/// <summary>Client asks to travel to (and land on) another celestial body, picked from the star map.</summary>
public sealed class TravelIntent
{
    public string DestinationBodyId { get; set; } = string.Empty;

    /// <summary>Which fixed landing pad to touch down on (item 38); -1 = auto-pick the first free pad.</summary>
    public int PadIndex { get; set; } = -1;
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

/// <summary>Client right-clicks a held gadget (item 36 — field medkit / stasis projector / terrain blaster).
/// The server validates the gadget, suit energy + cooldown, then applies the keyed effect at the aim point.</summary>
public sealed class UseGadgetIntent
{
    public string GadgetKey { get; set; } = string.Empty;
    public float X { get; set; } // aim/target point (block-blast centre, stasis centre); ignored by the medkit
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Client loots a nearby container (salvage capsule / corpse). Server validates proximity + transfers.</summary>
public sealed class LootContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>Client stashes its loose materials into a nearby storage crate (Task 5 Stage 3b).</summary>
public sealed class DepositContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>Client reports its ship's position while flying in space (server validates + checks collisions).</summary>
public sealed class ShipMoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Heading of the ship (or the floating EVA suit) so other players in the instance can see which
    /// way you face.</summary>
    public float Yaw { get; set; }
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

/// <summary>Client toggles the stealth field (requires a stealth suit + energy).</summary>
public sealed class ToggleStealthIntent { }

/// <summary>Client → server: the player is (or is no longer) firing the jetpack. The server drains suit
/// energy while active and forces it off when the energy runs out (the client applies the thrust locally).</summary>
public sealed class SetJetpackIntent
{
    public bool Active { get; set; }
}

/// <summary>Client → server: the player starts (Active = true) or ends (Active = false) an EVA spacewalk —
/// floating outside the ship in space on foot. The server honours a start only while the player is actually
/// in a space instance; on EVA the suit's life support is off, so oxygen drains until they board again.</summary>
public sealed class SetEvaIntent
{
    public bool Active { get; set; }
}

/// <summary>Client docks with and boards a nearby space station from the current space instance.</summary>
public sealed class BoardStationIntent
{
    public string StationId { get; set; } = string.Empty;
}

/// <summary>Client leaves the currently boarded station and returns to the ship.</summary>
public sealed class LeaveStationIntent { }

/// <summary>Client repairs one damaged/missing wreck hull cell with a matching block item.</summary>
public sealed class RepairWreckIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client claims a fully repaired wreck into the owned ship fleet.</summary>
public sealed class ClaimWreckIntent { }

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

/// <summary>Client sets the knowledge points it offers to teach on its side of the open trade (item 11).
/// The giver never loses points; the server caps it to what may still be taught to this partner.</summary>
public sealed class TradeKnowledgeIntent
{
    public int Amount { get; set; }
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

    /// <summary>Knowledge points each side is teaching in this trade (item 11; the giver keeps theirs).</summary>
    public int MyKnowledgeOffered { get; set; }
    public int TheirKnowledgeOffered { get; set; }

    /// <summary>My current knowledge total, and the most I can still teach this partner (give-once cap +
    /// can't raise them above my own level) — drives the trade panel's knowledge control.</summary>
    public int MyKnowledge { get; set; }
    public int MyKnowledgeMax { get; set; }
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

    /// <summary>Ship hull colour (packed 0xRRGGBB) — tints the player's ship hull (item 32). 0 = unset.</summary>
    public int Hull { get; set; }
}

// ---------------- Server -> Client (state) ----------------

public sealed class JoinAccepted
{
    public int ProtocolVersion { get; set; } = BlocksBeyondTheStars.Networking.Protocol.Version;
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

/// <summary>Mining progress on a block (a harder block needs several drill hits): Fraction 0..1 of the
/// way to breaking. The client shows a crack overlay; the block stays until it breaks (BlockChanged).</summary>
public sealed class MiningProgress
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float Fraction { get; set; }
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

    /// <summary>Blueprint keys the player has unlocked — lets the client show craftable/locked status.</summary>
    public string[] UnlockedBlueprints { get; set; } = System.Array.Empty<string>();

    /// <summary>The player's current knowledge total (kept in sync so the HUD/menu/trade panel show it).</summary>
    public int KnowledgePoints { get; set; }
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

    /// <summary>True while the player is on an EVA spacewalk (floating in space on foot) — drives the
    /// suit float HUD and tells the client to skip the ship take-off animation when boarding again.</summary>
    public bool InEva { get; set; }

    /// <summary>True while the on-foot player has built/climbed above the atmosphere into space (item 10)
    /// — the client floats in zero-g and shows a space sky.</summary>
    public bool AboveAtmosphere { get; set; }

    /// <summary>Name of the space station the player is currently boarded on (empty when not on one).</summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>AI-core tier of the player's active ship (1 = bare VEGA, 2 = Mk2, 3 = Mk3) — gates the
    /// client-side autopilot assist and the companion panel's ability hints.</summary>
    public int AiCoreTier { get; set; } = 1;
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

    // World options (live-editable by the world admin; shown in the in-game settings section).
    public string CreatureAbundance { get; set; } = string.Empty;
    public string PlanetEnemies { get; set; } = string.Empty;
    public string SpaceNpcEnemies { get; set; } = string.Empty;
    public string AlienUfos { get; set; } = string.Empty;

    /// <summary>Instant Travel world option: when true the travel screen may quick-travel anywhere; when
    /// false it is limited to bodies the player has already landed on (default).</summary>
    public bool InstantTravel { get; set; }
}

/// <summary>Client → server (world admin only): live-edits the gameplay world options — creature
/// abundance and the three enemy activities. Values are <c>AlienActivity</c> names; empty = leave
/// unchanged. The server applies, persists into the save's rules and re-broadcasts ServerRules.</summary>
public sealed class SetWorldRulesIntent
{
    public string CreatureAbundance { get; set; } = string.Empty;
    public string PlanetEnemies { get; set; } = string.Empty;
    public string SpaceNpcEnemies { get; set; } = string.Empty;
    public string AlienUfos { get; set; } = string.Empty;

    /// <summary>Instant Travel toggle: "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string InstantTravel { get; set; } = string.Empty;
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

    /// <summary>Mission-giver NPC's name (item 13) — shown as "Mission from {GiverName}"; empty if none.</summary>
    public string GiverName { get; set; } = string.Empty;

    /// <summary>True when <see cref="Title"/>/<see cref="Description"/> are display TEXT (player missions,
    /// L3 LLM board texts) rather than locale keys — the client renders them verbatim then.</summary>
    public bool FreeText { get; set; }
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

    /// <summary>For a SpaceStation body: the owning player's name (empty for procedural/NPC stations and for
    /// non-station bodies). Lets the travel screen mark a station "yours" and otherwise show "Station of {owner}".</summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>System-space coordinates (star at origin) for the system-scale flight layer.</summary>
    public float SystemX { get; set; }
    public float SystemY { get; set; }
    public float SystemZ { get; set; }

    /// <summary>Fixed landing pads on this body (item 38): how many there are and how many are currently free
    /// (live occupancy). PadsFree == 0 means the body is full — landing there is refused.</summary>
    public int PadsTotal { get; set; }
    public int PadsFree { get; set; }
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

    /// <summary>Where every other online player currently is, so the star map shows the whole party.</summary>
    public NetPlayerLocation[] Players { get; set; } = System.Array.Empty<NetPlayerLocation>();

    /// <summary>Bodies THIS player has landed on (quick-travel targets when Instant Travel is off).</summary>
    public string[] LandedBodyIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Star systems THIS player has entered — known systems reveal their bodies + mini map; an
    /// unknown system is a single "hyperjump here" entry on the travel screen.</summary>
    public string[] KnownSystemIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Host bodies (planet/moon/asteroid) where THIS player has a commissioned space station orbiting —
    /// the travel screen badges them "you have a station here".</summary>
    public string[] MyStationBodyIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Bodies where THIS player has founded a base, with the base's name — the travel screen badges them
    /// "you have a base here: {name}" and offers a rename.</summary>
    public NetMapBase[] MyBases { get; set; } = System.Array.Empty<NetMapBase>();
}

/// <summary>A player's own base for the travel screen: which body it's on + its current name.</summary>
public sealed class NetMapBase
{
    public string BodyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>Client → server: hyperjump into a (possibly never-visited) star system, arriving in FLIGHT mode
/// in that system's space (not landed). Needs a jump generator. The way to reach a system whose bodies you
/// can't yet see on the travel screen.</summary>
public sealed class HyperjumpSystemIntent
{
    public string SystemId { get; set; } = string.Empty;
}

/// <summary>A player's current body (for the shared star map).</summary>
public sealed class NetPlayerLocation
{
    public string Name { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
}

/// <summary>
/// Tells the client the active world has changed (the player travelled to another planet): clear all
/// cached chunks/meshes and reload. Carries the new world's identity for the HUD; a fresh player state +
/// environment + chunk stream follow.
/// </summary>
public sealed class WorldReset
{
    public string PlanetType { get; set; } = string.Empty;
    public string PlanetName { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;

    /// <summary>True when the arrival was a hyperspace jump to a different star system (drives the client warp animation).</summary>
    public bool Hyperjump { get; set; }
}

/// <summary>The player's current vitals are respawning at the ship heal-tank (Medbay).</summary>
public sealed class RespawnNotice
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool SalvageCapsuleDropped { get; set; }

    /// <summary>True when this respawn follows an actual death (drives the client's red death flash + sound);
    /// false for non-death relocations like the void-fall rescue.</summary>
    public bool Died { get; set; }
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
    public string Name { get; set; } = string.Empty;
    public bool Hostile { get; set; }
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Visual model scale (stations: by size tier — a colossal hull dwarfs a small one).
    /// Contractless-additive: payloads without the field leave the default 1.</summary>
    public float Scale { get; set; } = 1f;
}

/// <summary>Authoritative ship hull/shield, sent on join and whenever they change.</summary>
public sealed class ShipCombatStatus
{
    public float Hull { get; set; }
    public float HullMax { get; set; }
    public float Shield { get; set; }
    public float ShieldMax { get; set; }

    /// <summary>Space-radar range in world units (base + radar-module bonus); drives the HUD radar scale.</summary>
    public float RadarRange { get; set; } = 130f;

    /// <summary>The active ship's fitted module keys — lets the flight HUD build its ship-systems quick-bar
    /// (weapon, tractor beam, …) from what the ship actually carries.</summary>
    public string[] Modules { get; set; } = System.Array.Empty<string>();
}

/// <summary>Snapshot of the space instance a player is in (sent on entry and on change).</summary>
public sealed class SpaceState
{
    public string InstanceId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public NetCombatEntity[] Entities { get; set; } = System.Array.Empty<NetCombatEntity>();

    /// <summary>True when the flight view should appear without the take-off sequence — you were already in
    /// space (e.g. taking the helm again from inside the ship), so there is no launch from a surface.</summary>
    public bool SkipLaunch { get; set; }

    /// <summary>True when the player arrived in this flight via a hyperjump into a new star system — the
    /// client plays the warp VFX as the view opens (there is no surface take-off).</summary>
    public bool Hyperjump { get; set; }

    /// <summary>The OTHER players sharing this space instance (excludes the recipient) — their ship or floating
    /// EVA suit, so everyone can see each other out here.</summary>
    public NetSpacePlayer[] Players { get; set; } = System.Array.Empty<NetSpacePlayer>();
}

/// <summary>The player's own ship as a voxel structure to render in the flight view (item 20, S1).
/// Seeded server-side from the ship-editor design and sent on entering space (and on ship switch); the
/// client meshes it 1:1 with the chunk mesher and uses it in place of the hand-built cube ship. The block
/// grid is sparse — parallel arrays of cell coordinates + the block id at each (only non-air cells).</summary>
public sealed class SpaceShipDesign
{
    /// <summary>Structure id (e.g. "ship:&lt;playerId&gt;" or an asteroid's entity id).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Structure kind: "ship" (the player's own ship — rides the live flight pose) or "asteroid" (a
    /// static voxel body at <see cref="PosX"/>/<see cref="PosY"/>/<see cref="PosZ"/>) — item 20 S3.</summary>
    public string Kind { get; set; } = "ship";

    /// <summary>World position in the flight scene for a static structure (asteroid). Ignored for the own ship.</summary>
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }

    /// <summary>Design bounding-box size in blocks (for centring the mesh on the structure pivot).</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>Per-cell block coordinates (design-local) and the block id placed there. Same length.</summary>
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();
}

/// <summary>Server → client: a player's ship parked on the current world as a placed voxel OBJECT
/// (ship-as-object — the hull is no longer stamped into the world grid). Sent on world join (one per
/// parked ship), on landing/ship-switch (placed/replaced) and on launch (<see cref="Removed"/>). Cell
/// edits afterwards ride the existing <see cref="StructureBlockChanged"/> keyed by <see cref="StructureId"/>.</summary>
public sealed class LandedShipState
{
    /// <summary>The owning player.</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Structure id ("ship:&lt;playerId&gt;") — the same id structure edits use.</summary>
    public string StructureId { get; set; } = string.Empty;

    /// <summary>True = the ship left (launch/owner logout); the client despawns the object.</summary>
    public bool Removed { get; set; }

    /// <summary>World position of the structure-local origin (cell 0,0,0).</summary>
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public int OriginZ { get; set; }

    /// <summary>The owner's hull paint colour (0xRRGGBB; 0 = default steel).</summary>
    public int Hull { get; set; }

    /// <summary>Design bounding-box size in blocks.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>Per-cell block coordinates (structure-local) and the block id placed there. Same length.
    /// Empty when <see cref="Removed"/>.</summary>
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();
}

/// <summary>Client → server: place or mine one block on a voxel <see cref="SpaceShipDesign"/> structure while
/// on an EVA spacewalk (item 20, S2). Coordinates are design-local cells (the same space the structure was sent
/// in). The free-space analogue of <see cref="PlaceBlockIntent"/>/<see cref="MineBlockIntent"/>.</summary>
public sealed class StructureEditIntent
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>True = mine the cell; false = place <see cref="ItemKey"/>'s block at it.</summary>
    public bool Mine { get; set; }

    /// <summary>The hotbar item whose block to place (ignored when mining).</summary>
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client → server: deploy a station core in front of the suit to start a player-built station (item
/// 20, S4). No payload — the server places it from the suit's pose.</summary>
public sealed class DeployStationCoreIntent
{
}

/// <summary>Server → client: one cell of a voxel structure changed (item 20, S2) — the free-space analogue of
/// <see cref="BlockChanged"/>. Broadcast to everyone in the space instance so they re-mesh the structure.</summary>
public sealed class StructureBlockChanged
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; } // 0 = air
}

/// <summary>A remote player's presence in a space instance: their ship (or floating EVA suit) position +
/// heading, for the flight view to render. Server → client, inside <see cref="SpaceState"/>.</summary>
public sealed class NetSpacePlayer
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    /// <summary>True = a floating suit on an EVA; false = piloting a ship.</summary>
    public bool Eva { get; set; }

    /// <summary>Ship hull colour (packed 0xRRGGBB) so other players see this pilot's ship in their colour
    /// (item 32). 0 = unset → the client falls back to the default steel tint.</summary>
    public int Hull { get; set; }
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

/// <summary>The player boarded a space station and is now walking inside its voxel interior.</summary>
public sealed class StationBoarded
{
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Repair progress for the currently stamped wreck.</summary>
public sealed class WreckRepairStatus
{
    public string WreckName { get; set; } = string.Empty;
    public string ShipType { get; set; } = string.Empty;
    public int Remaining { get; set; }
    public int Total { get; set; }
    public bool Claimable { get; set; }
    public bool Claimed { get; set; }

    /// <summary>Comma-separated distinct block keys still needed to repair the wreck (for the HUD hint).</summary>
    public string Needs { get; set; } = string.Empty;
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
    public string Name { get; set; } = string.Empty; // coined species name (shown on scan / look)

    public bool Hostile { get; set; }
    public bool Asleep { get; set; }
    public bool Frozen { get; set; } // held in stasis (item 36) — client tints it icy blue + still
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
    public int Eyes { get; set; } = 2;
    public int Horns { get; set; }
    public bool HasCrest { get; set; }
    public int BellyRgb { get; set; } = 0xFFFFFF;

    // item-21 morphology rest: tentacles, snail-like eyestalks, a translucent buoyancy gas-sac.
    public int Tentacles { get; set; }
    public bool EyeStalks { get; set; }
    public bool HasGasSac { get; set; }
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

/// <summary>A point of interest on the current planet (for the world map): settlement, wreck, …</summary>
public sealed class NetPoi
{
    public string Type { get; set; } = string.Empty; // settlement / settlement_ruin / wreck / landing
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Z { get; set; }
}

/// <summary>The known points of interest on the current planet, sent on join + world switch.</summary>
public sealed class PlanetPoiList
{
    public NetPoi[] Pois { get; set; } = System.Array.Empty<NetPoi>();
}

/// <summary>A player chat line (client→server). Requires a comm radio; range/rate enforced server-side.</summary>
public sealed class ChatIntent
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>A broadcast chat line (server→clients).</summary>
public sealed class ChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
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

    /// <summary>Stealth field active — other clients fade/hide the avatar + nameplate.</summary>
    public bool Stealthed { get; set; }

    /// <summary>Jetpack firing — other clients show the thrust flame under the avatar.</summary>
    public bool Jetpacking { get; set; }

    /// <summary>Equipped-gear bitmask shown on the avatar: 1=helmet, 2=chest, 4=legs, 8=pack, 16=lamp.</summary>
    public int Gear { get; set; }

    /// <summary>Item key currently held (selected hotbar slot), shown in the avatar's hand; empty if none.</summary>
    public string Held { get; set; } = string.Empty;
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

    /// <summary>Air temperature at the player, in °C (base per world + weather + day/night). Shown in the HUD.</summary>
    public float Temperature { get; set; } = 15f;

    /// <summary>Active precipitation form: none / rain / snow / hail / ash / sandstorm (climate-driven).</summary>
    public string Precipitation { get; set; } = "none";

    /// <summary>Sun/star light colour, packed 0xRRGGBB.</summary>
    public int SunColor { get; set; } = 0xFFF6E8;

    /// <summary>Base cloud tint for this planet, packed 0xRRGGBB (storms darken it on the client).</summary>
    public int CloudColor { get; set; } = 0xEDEFF2;

    /// <summary>The planet's uniform flora base hue, packed 0xRRGGBB. All flora on the planet is desaturated
    /// to its texture luminance and re-tinted by this colour, so a world's plant life shares one base colour
    /// (green / brown / pink / purple …) regardless of the underlying tile. White = no tint.</summary>
    public int FloraTint { get; set; } = 0xFFFFFF;

    /// <summary>This world's walkable east–west circumference in blocks (longitude wrap + day/night span).
    /// Varies by body size — asteroids small, planets large — so the client wraps/renders at the right size.</summary>
    public int Circumference { get; set; } = 6000;

    /// <summary>Latitude (Z) bound from the equator (the invisible pole barrier), = Circumference / 4.</summary>
    public int LatitudeLimit { get; set; } = 1500;

    /// <summary>0..1 cloud cover for this planet (frequency + thickness; 0 = clear skies).</summary>
    public float CloudDensity { get; set; } = 0.45f;

    /// <summary>Whether the planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool Breathable { get; set; }

    /// <summary>Space sky on the surface (black + stars) — landable asteroids / airless bodies.</summary>
    public bool SpaceSky { get; set; }

    /// <summary>Active planet/biome key (e.g. "jungle", "desert", "ice", "lava") for client ambience.</summary>
    public string Biome { get; set; } = "rock";
}

/// <summary>
/// Client → server: the player opened an interaction with a nearby NPC of this role ("vendor" / "quartermaster")
/// and would like its greeting line (item 15). The server picks the nearest matching NPC it can verify is in
/// reach, then replies with an <see cref="NpcGreeting"/>. Spam-safe: the server gates on proximity.
/// </summary>
public sealed class NpcGreetIntent
{
    /// <summary>The NPC role the player is interacting with: "vendor" or "quartermaster".</summary>
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Server → client: the result of a terrain-scanner pulse (Feature 40) — the positions + block ids of the
/// valuable blocks (ores / crystal / data caches) found in a sphere around the player. The client renders
/// them as through-wall glow markers for a few seconds. Server-authoritative (energy/cooldown validated).
/// </summary>
public sealed class OreScanResult
{
    public int[] X { get; set; } = System.Array.Empty<int>();
    public int[] Y { get; set; } = System.Array.Empty<int>();
    public int[] Z { get; set; } = System.Array.Empty<int>();

    /// <summary>Numeric block id per hit (parallel to X/Y/Z) — lets the client tint markers by ore type.</summary>
    public ushort[] Block { get; set; } = System.Array.Empty<ushort>();

    /// <summary>How long the markers stay visible, in seconds.</summary>
    public float Seconds { get; set; } = 8f;
}

/// <summary>
/// Server → client: a contextual greeting line to show as a speech bubble over an NPC (item 15). When an LLM
/// backend is enabled the <see cref="Text"/> is LLM-authored in the player's language; when AI is off/unreachable
/// the server sends an empty <see cref="Text"/> and the client shows a localized static fallback by role.
/// </summary>
public sealed class NpcGreeting
{
    /// <summary>The runtime NPC id (matches <see cref="NetNpc.Id"/>) the bubble belongs to.</summary>
    public int NpcId { get; set; }

    /// <summary>The NPC's coined name (for the fallback line / display); may be empty.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The NPC role ("vendor" / "quartermaster") — selects the client-side fallback line.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>The greeting to show. Empty ⇒ the client renders its own localized fallback for the role.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Server → client: a line from the player's SHIP AI ("VEGA") shown in the HUD companion panel, plus the
/// current onboarding/advisor objective for the objective chip. Lines are locale KEYS (the client localizes),
/// keeping the AI fully bilingual and offline-safe; <see cref="LineArg"/> fills an optional {0} placeholder
/// with a proper noun (world/item name). Strictly per-player — every player has their own VEGA.
/// </summary>
public sealed class ShipAiLine
{
    /// <summary>Locale key of the spoken line; empty ⇒ objective-only update (no new speech).</summary>
    public string LineKey { get; set; } = string.Empty;

    /// <summary>Optional {0} substitution for the line (already a display string, e.g. a world name).</summary>
    public string LineArg { get; set; } = string.Empty;

    /// <summary>LLM-authored display TEXT (VEGA banter) — when non-empty the client shows this verbatim
    /// instead of localizing <see cref="LineKey"/>. Always generated in the player's language.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Locale key of the active objective chip; empty ⇒ clear the chip.</summary>
    public string ObjectiveKey { get; set; } = string.Empty;

    /// <summary>Progress toward the objective (e.g. blocks mined so far). 0/0 ⇒ no counter shown.</summary>
    public int ObjectiveProgress { get; set; }

    public int ObjectiveTarget { get; set; }

    /// <summary>0 = onboarding, 1 = advisor hint, 2 = memory/story, 3 = system (module/ability notices).</summary>
    public byte Kind { get; set; }
}

/// <summary>Client → server: skip the VEGA onboarding (grants all stage milestones; the advisor hints
/// stay armed) — or, with <see cref="Restart"/>, wipe the stage milestones and run the tutorial again
/// from the intro (the way back after a skip).</summary>
public sealed class SkipOnboardingIntent
{
    public bool Restart { get; set; }
}
