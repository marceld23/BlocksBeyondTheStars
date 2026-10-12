// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Shared.Story;

/// <summary>
/// The published vocabulary of server game events (#2465, synthesis §5.3/§5.5): the keys the server raises through
/// <c>GameServer.RaiseGameEvent(actor, key, arg, amount)</c> and a story-pack step may name in
/// <c>objective.event</c>. A key names the verb; the entity rides in the argument, so keys never grow with content
/// (<c>mine:any</c> + <c>iron_ore</c>, never <c>mine:iron_ore</c>). Where each one is raised — and what its argument
/// holds — is the table in <c>docs/developer/STORY_IMPLEMENTATION.md</c> ("Game events").
/// <para>Some keys share their spelling with an achievement counter (<c>mine:any</c>, <c>craft:any</c> …). They are
/// different channels: events are never written into <c>AchievementCounters</c> and persist nothing.</para>
/// </summary>
public static class GameEventKeys
{
    /// <summary>A block was mined by hand. Arg: the block key.</summary>
    public const string MineAny = "mine:any";

    /// <summary>A block was placed. Arg: the block key (a blueprint paste: <see cref="BlueprintPasteArg"/>, amount = blocks).</summary>
    public const string BuildAny = "build:any";

    /// <summary>A form or prop block was placed with an explicit turn of the rotate key. Arg: the block key.</summary>
    public const string BuildRotated = "build:rotated";

    /// <summary>A recipe was crafted (stations, markets, the ship console). Arg: the recipe key; amount: the batch count.</summary>
    public const string CraftAny = "craft:any";

    /// <summary>A building material was re-formed (built-in or player-designed form). Arg: the base item key.</summary>
    public const string CraftShape = "craft:shape";

    /// <summary>A building material was dyed or made to glow. Arg: the base item key.</summary>
    public const string CraftTint = "craft:tint";

    /// <summary>A building material was painted with a design. Arg: the base item key.</summary>
    public const string CraftPaint = "craft:paint";

    /// <summary>Real food was eaten (an item with hunger value). Arg: the item key.</summary>
    public const string EatAny = "eat:any";

    /// <summary>Any scan, first or repeat (hand scanner, ship scanner). Arg: the readout kind (<c>flora</c>, <c>creature</c>,
    /// <c>block</c>, <c>monument</c>, <c>asteroid</c>, <c>anomaly</c>, <c>wreck</c>, <c>wormhole</c> …).</summary>
    public const string ScanPerformed = "scan:performed";

    /// <summary>A first-time scan (a discovery — the achievement counter's moment). Arg: the readout kind.</summary>
    public const string ScanAny = "scan:any";

    /// <summary>A blueprint was researched. Arg: the blueprint key.</summary>
    public const string ResearchAny = "research:any";

    /// <summary>The player entered space flight. Arg: <see cref="SpaceLaunch"/>, <see cref="SpaceHyperjump"/>,
    /// <see cref="SpaceWormhole"/> or <see cref="SpaceResume"/>.</summary>
    public const string SpaceEnter = "space:enter";

    /// <summary>A space station was boarded. Arg: the station id.</summary>
    public const string StationBoarded = "station:boarded";

    /// <summary>A barter at a settlement or station market stall. Arg: the recipe key.</summary>
    public const string TradeBarter = "trade:barter";

    /// <summary>A completed trade with another player. Arg: the other player's id.</summary>
    public const string TradePlayer = "trade:player";

    /// <summary>A mission was accepted (board, chain step in conversation). Arg: the mission id.</summary>
    public const string MissionAccepted = "mission:accepted";

    /// <summary>A mission was turned in. Arg: the mission id.</summary>
    public const string MissionTurnedIn = "mission:turned_in";

    /// <summary>The player's ship set down on a body. Arg: <see cref="TravelFlight"/>, <see cref="TravelQuick"/> or
    /// <see cref="TravelAdmin"/>. The join placement raises nothing.</summary>
    public const string TravelLanded = "travel:landed";

    /// <summary>First arrival on a body (stations included). Arg: the body id.</summary>
    public const string VisitBody = "visit:body";

    /// <summary>First arrival in a star system (never the spawn system). Arg: the system id.</summary>
    public const string VisitSystem = "visit:system";

    /// <summary>A jump to another star system. Arg: the destination system id.</summary>
    public const string HyperjumpAny = "hyperjump:any";

    /// <summary>A ship module was built. Arg: the module key.</summary>
    public const string ModuleBuilt = "module:built";

    /// <summary>A self-built ship was commissioned. Arg: the ship id.</summary>
    public const string ShipCommissioned = "ship:commissioned";

    /// <summary>A player station was commissioned — raised for its owner. Arg: the station id.</summary>
    public const string StationCommissioned = "station:commissioned";

    /// <summary>A base core founded a base. Arg: the body id.</summary>
    public const string BaseFounded = "base:founded";

    /// <summary>A creature was tamed. Arg: the species id (<c>au_sreekmakra</c> for the shapeshifter — also when it
    /// follows the tame of the animal it imitated, which raises its own event first).</summary>
    public const string TameAny = "tame:any";

    /// <summary>A world container (not a player crate) was looted empty. Arg: the container kind.</summary>
    public const string LootAny = "loot:any";

    /// <summary>A hostile was defeated by the player. Arg: <c>creature</c>, <c>giant</c>, <c>machine</c>, <c>bandit</c>
    /// (planet) or <c>space_machine</c>, <c>raider</c> (space).</summary>
    public const string DefeatAny = "defeat:any";

    /// <summary>The player flew through a wormhole. Arg: the destination system id.</summary>
    public const string WormholeAny = "wormhole:any";

    /// <summary>An SPS relay the player contributed to was completed. Arg: the station id.</summary>
    public const string RelayCommissioned = "relay:commissioned";

    /// <summary>A jump lane opened through the player's contribution. Arg: <c>&lt;systemA&gt;|&lt;systemB&gt;</c>.</summary>
    public const string LaneLinked = "lane:linked";

    /// <summary>A story net fragment was handed to the player (picked up, mission thread, NPC). Arg: the fragment key.</summary>
    public const string StoryFragment = "story:fragment";

    /// <summary>#2476: the player read a lore text new to them (a rune inscription, a wreck log, a vault plaque, the
    /// observatory's last log — whatever opened in the reader). Arg: the site kind (<c>monument</c>, <c>vault</c>,
    /// <c>observatory</c>, <c>derelict</c>, <c>data_terminal</c> …).</summary>
    public const string LoreRead = "lore:read";

    /// <summary>The prefix of the Crystal Net circuit events: <c>crystal:&lt;event&gt;</c> with the event of
    /// <see cref="CircuitEvents"/> (raised for the circuit's owner). Arg: empty.</summary>
    public const string CrystalPrefix = "crystal:";

    /// <summary>A companion made a Guardian machine stand down near the player (raised at most every
    /// <c>GameServer.InsightEventCooldownSeconds</c> while it lasts). Arg: empty.</summary>
    public const string InsightCompanionWard = "insight:companion_ward";

    /// <summary>The player formed a non-cube block. Arg: the base item key.</summary>
    public const string InsightShapeNoncube = "insight:shape_noncube";

    // ---- #2469: the basic-training verbs (one line each at an existing handler; d1 §3.25 "Basic (v1)") ----

    /// <summary>The player walked on a surface. Amount: whole metres (sub-metre steps add up; jumps between reports
    /// longer than a few metres — a teleport, a respawn — never count).</summary>
    public const string MoveWalked = "move:walked";

    /// <summary>The player jumped or crouched on foot (at most once a second). Arg: <see cref="MoveJumpArg"/> or
    /// <see cref="MoveCrouchArg"/>.</summary>
    public const string MoveJump = "move:jump";

    /// <summary>The player picked another quick-bar slot. Arg: the slot number, 1-based.</summary>
    public const string HotbarSelect = "hotbar:select";

    /// <summary>A mined or harvested block's drops reached the player. Arg: the base item key; amount: the count.</summary>
    public const string ItemGained = "item:gained";

    /// <summary>The player moved an item between two slots of the backpack or the quick bar.</summary>
    public const string ItemMoved = "item:moved";

    /// <summary>The player put on a piece of gear. Arg: the base item key.</summary>
    public const string GearEquipped = "gear:equipped";

    /// <summary>The player stowed items into the ship's cargo hold. Arg: the item key, or <c>all</c> for "stow all".</summary>
    public const string CargoStowed = "cargo:stowed";

    /// <summary>The player's backpack has no free slot left (noticed once a second, like VEGA's <c>invfull</c> hint).</summary>
    public const string InventoryFull = "inventory:full";

    /// <summary>The player got back to their landed ship. Arg: <c>menu</c> ("Back to my ship"), <c>teleport</c> (the suit
    /// teleporter) or <c>walk</c> (walked back aboard or close to it after having been far away).</summary>
    public const string ShipReturned = "ship:returned";

    /// <summary>The suit lamp was switched. Arg: <c>on</c> or <c>off</c>.</summary>
    public const string LampToggled = "lamp:toggled";

    /// <summary>The suit fed a stored ration to a starving player. Arg: the item key.</summary>
    public const string SuitAutoFeed = "suit:auto_feed";

    /// <summary>The suit's climate control started fighting the weather. Arg: <c>cold</c> or <c>heat</c>.</summary>
    public const string SuitClimate = "suit:climate";

    /// <summary>The player woke up again after fainting (any respawn).</summary>
    public const string PlayerRespawned = "player:respawned";

    /// <summary>A block refused the swing because the tool tier is too low (and a better tool exists). Arg: the block key.</summary>
    public const string MineRefused = "mine:refused";

    /// <summary>Data for bigger blueprints turned up. Arg: <c>cache</c> (a data cache is in reach) or <c>fragment</c> (the
    /// player carries a data fragment).</summary>
    public const string DataFound = "data:found";

    /// <summary>The player's ship flew in space. Amount: whole flight units (a hyperjump or a warp never counts).</summary>
    public const string FlightFlown = "flight:flown";

    /// <summary>A job the player picked was accepted: a mission board's or a mission chain's (never a story character's
    /// errand, never a pack's own bridge). Arg: the mission id.</summary>
    public const string MissionBoardAccepted = "mission:board_accepted";

    /// <summary>First arrival on a planet, moon or asteroid (not a station — <see cref="VisitBody"/> counts those too).
    /// Arg: the body id.</summary>
    public const string VisitWorld = "visit:world";

    // ---- #2479: the deep dives "Research" and "Flying" (C2–C5; one line each at an existing handler) ----

    /// <summary>A finished run of a DataQube game from the shipped catalogue (every completed run, a replay too — the
    /// knowledge it pays is the handler's business). Arg: the game key.</summary>
    public const string MinigamePlayed = "minigame:played";

    /// <summary>A gadget did its work (the terrain scanner pulsed, the medkit healed …; a refused or empty use raises
    /// nothing). Arg: the gadget's item key.</summary>
    public const string GadgetUsed = "gadget:used";

    /// <summary>A world's overview card was read — the ship scanner held on a planet, or the Map's planet scan. Arg: the body id.</summary>
    public const string PlanetScanned = "planet:scanned";

    /// <summary>The player's ship weapon broke an asteroid (a big one splits into smaller ones — that counts too).</summary>
    public const string AsteroidDestroyed = "asteroid:destroyed";

    /// <summary>The pilot stepped from the helm (or in from a spacewalk) into the ship's walkable interior in space.</summary>
    public const string InteriorEntered = "interior:entered";

    /// <summary>A spacewalk began. Arg: <see cref="EvaHatch"/> (out through the hatch of the floating ship) or
    /// <see cref="EvaFlight"/> (from the flight view).</summary>
    public const string EvaStarted = "eva:started";

    /// <summary>The player floated up above a planet's atmosphere line on foot (raised right before VEGA's <c>zero_g</c> hint).</summary>
    public const string PlayerZeroG = "player:zero_g";

    /// <summary>The player undocked from a boarded station. Arg: the station id.</summary>
    public const string StationLeft = "station:left";

    /// <summary>A map marker was set (or moved) on a world, or a spot pinged. Arg: <see cref="MarkerSetArg"/> or
    /// <see cref="MarkerPingArg"/>.</summary>
    public const string MarkerSet = "marker:set";

    /// <summary>Salvage in space: a derelict or a debris field reached for the first time, or a salvage capsule emptied into
    /// the hold. Arg: <see cref="SalvageWreck"/>, <see cref="SalvageDebris"/> or <see cref="SalvageCapsule"/>.</summary>
    public const string SalvageFound = "salvage:found";

    // ---- #2480: the deep dives "Ship building" and "Base building" (C1, C6, C7, C17; one line each at an existing handler) ----

    /// <summary>The player repaired their own ship (the hull points, or a hole refilled). Arg: <see cref="ShipRepairAll"/> (the
    /// console's "repair everything affordable") or <see cref="ShipRepairCell"/> (one hole refilled on a walk-around or EVA).</summary>
    public const string ShipRepaired = "ship:repaired";

    /// <summary>A block was built onto (or into) the player's parked ship on foot — the ship grows. Arg: the block key.</summary>
    public const string ShipEdited = "ship:edited";

    /// <summary>A ship keel was laid: the construction site of a self-built ship. Arg: the new fleet id.</summary>
    public const string ShipKeelPlaced = "ship:keel_placed";

    /// <summary>The player switched their active ship in the fleet. Arg: the ship id switched to.</summary>
    public const string ShipSwitched = "ship:switched";

    /// <summary>A block was placed inside a base zone the player owns or shares with an ally (the core itself not). Arg: the
    /// block key.</summary>
    public const string BaseBuilt = "base:built";

    /// <summary>A base the player owns or shares with an ally holds air in a sealed room on the player's world (the 1 Hz check,
    /// only while a step listens). Arg: the body id.</summary>
    public const string BaseSealed = "base:sealed";

    /// <summary>The player stands inside a closed wall ring of a base they own or share with an ally (the 1 Hz check, only while
    /// a step listens). Arg: the body id.</summary>
    public const string BaseWalled = "base:walled";

    /// <summary>A hand-operated door (wood, hinge) was opened by the player. Arg: the door kind.</summary>
    public const string DoorOpened = "door:opened";

    /// <summary>A bed, a heal tank or a crew bunk became the player's spawn point. Arg: <see cref="SpawnBed"/>,
    /// <see cref="SpawnHealTank"/> or <see cref="SpawnBunk"/>.</summary>
    public const string SpawnSet = "spawn:set";

    /// <summary>A station core was deployed on a spacewalk — the start of a player station. Arg: the station structure id.</summary>
    public const string StationCoreDeployed = "station:core_deployed";

    /// <summary>Zero-g construction mode switched aboard a player station. Arg: <c>on</c> or <c>off</c>.</summary>
    public const string StationZeroG = "station:zero_g";

    // ---- #2481: the deep dives "Crystal net" (C8, C14; one line each at an existing handler) ----

    /// <summary>A recipe crafted, by the station it needs (raised next to <see cref="CraftAny"/>, also for a free Creative
    /// craft). Arg: the station — <c>CraftingStation</c>'s name in lower case (<c>hand</c>, <c>workshop</c>, <c>refinery</c>,
    /// <c>factory</c>, <c>campfire</c>, <c>market</c>, <c>algaetank</c> …); amount = the batch count. A factory terminal's craft
    /// is <c>factory</c>.</summary>
    public const string CraftStation = "craft:station";

    /// <summary>A spawned factory claimed with an access (SPS) code — it became the player's base. Arg: the factory's name.</summary>
    public const string FactoryClaimed = "factory:claimed";

    /// <summary><see cref="ShipRepaired"/> arg: everything affordable at once (the console / cockpit action).</summary>
    public const string ShipRepairAll = "all";

    /// <summary><see cref="ShipRepaired"/> arg: one missing hull cell refilled.</summary>
    public const string ShipRepairCell = "cell";

    /// <summary><see cref="SpawnSet"/> arg: a bed.</summary>
    public const string SpawnBed = "bed";

    /// <summary><see cref="SpawnSet"/> arg: a heal tank.</summary>
    public const string SpawnHealTank = "heal_tank";

    /// <summary><see cref="SpawnSet"/> arg: a crew bunk.</summary>
    public const string SpawnBunk = "crew_bunk";

    /// <summary><see cref="EvaStarted"/> arg: out through the hatch of the ship floating in space.</summary>
    public const string EvaHatch = "hatch";

    /// <summary><see cref="EvaStarted"/> arg: started from the flight view.</summary>
    public const string EvaFlight = "flight";

    /// <summary><see cref="MarkerSet"/> arg: a marker set (or moved) on the planet map.</summary>
    public const string MarkerSetArg = "marker";

    /// <summary><see cref="MarkerSet"/> arg: a "look here!" ping.</summary>
    public const string MarkerPingArg = "ping";

    /// <summary><see cref="SalvageFound"/> arg: a derelict ship reached.</summary>
    public const string SalvageWreck = "wreck";

    /// <summary><see cref="SalvageFound"/> arg: a debris field reached.</summary>
    public const string SalvageDebris = "debris";

    /// <summary><see cref="SalvageFound"/> arg: a salvage capsule emptied into the hold.</summary>
    public const string SalvageCapsule = "capsule";

    /// <summary><see cref="MoveJump"/> arg: a jump.</summary>
    public const string MoveJumpArg = "jump";

    /// <summary><see cref="MoveJump"/> arg: a crouch.</summary>
    public const string MoveCrouchArg = "crouch";

    /// <summary><see cref="TravelLanded"/> arg: a landing from flight (the helm, the landing map, an EVA set-down).</summary>
    public const string TravelFlight = "flight";

    /// <summary><see cref="TravelLanded"/> arg: the map's travel (quick travel, or a parked ship flying the trip by itself, #1614).</summary>
    public const string TravelQuick = "quick";

    /// <summary><see cref="TravelLanded"/> arg: an admin move (<c>/goto</c>, the finale shortcut).</summary>
    public const string TravelAdmin = "admin";

    /// <summary><see cref="SpaceEnter"/> arg: a take-off from a body (helm, transit, undocking).</summary>
    public const string SpaceLaunch = "launch";

    /// <summary><see cref="SpaceEnter"/> arg: arriving in another system's flight after a hyperjump.</summary>
    public const string SpaceHyperjump = "hyperjump";

    /// <summary><see cref="SpaceEnter"/> arg: coming out of a wormhole.</summary>
    public const string SpaceWormhole = "wormhole";

    /// <summary><see cref="SpaceEnter"/> arg: back to the helm without a take-off (leaving the ship interior, an EVA return).</summary>
    public const string SpaceResume = "resume";

    /// <summary><see cref="BuildAny"/> arg of a blueprint paste (many blocks at once; amount = the placed count).</summary>
    public const string BlueprintPasteArg = "blueprint_paste";

    /// <summary><see cref="DefeatAny"/> arg: a creature on a planet.</summary>
    public const string DefeatCreature = "creature";

    /// <summary><see cref="DefeatAny"/> arg: a giant (colossus, sandworm …).</summary>
    public const string DefeatGiant = "giant";

    /// <summary><see cref="DefeatAny"/> arg: a Guardian machine on a planet.</summary>
    public const string DefeatMachine = "machine";

    /// <summary><see cref="DefeatAny"/> arg: a bandit (robber, camp guard, base scout).</summary>
    public const string DefeatBandit = "bandit";

    /// <summary><see cref="DefeatAny"/> arg: a hostile machine in space (drone, UFO).</summary>
    public const string DefeatSpaceMachine = "space_machine";

    /// <summary><see cref="DefeatAny"/> arg: a raider ship.</summary>
    public const string DefeatRaider = "raider";

    /// <summary>The <c>crystal:&lt;event&gt;</c> key of one circuit event (the same string as the achievement counter).</summary>
    public static string Crystal(string circuitEvent) => CrystalPrefix + circuitEvent;

    /// <summary>Prefix of a client teaching flag (<c>ui:menu_open</c>, synthesis §3.6). Allowed only in a player pack's
    /// step objective — never in a condition — and never persisted. Raised by the server when the client reports a UI
    /// moment (<c>UiTutorialEvent</c>, #2469; the ids are <see cref="UiEvents"/>).</summary>
    public const string UiPrefix = "ui:";

    /// <summary>The "continue" acknowledgement of a VEGA line — what completes a <c>read</c> step.</summary>
    public const string UiVegaContinue = UiPrefix + UiVegaContinueId;

    /// <summary>The Tab menu was opened.</summary>
    public const string UiMenuOpen = UiPrefix + UiMenuOpenId;

    /// <summary>The Codex was opened.</summary>
    public const string UiCodexOpen = UiPrefix + UiCodexOpenId;

    /// <summary>A map was opened (the planet map tab or the flight chart).</summary>
    public const string UiMapOpen = UiPrefix + UiMapOpenId;

    /// <summary>A waypoint was set on the flight chart.</summary>
    public const string UiWaypointSet = UiPrefix + UiWaypointSetId;

    /// <summary>#2479: the Achievements tab (what you have done and what comes next) was opened.</summary>
    public const string UiProgressOpen = UiPrefix + UiProgressOpenId;

    /// <summary>The id of <see cref="UiVegaContinue"/> on the wire (<c>UiTutorialEvent.Id</c>).</summary>
    public const string UiVegaContinueId = "vega_continue";

    /// <summary>The id of <see cref="UiMenuOpen"/> on the wire.</summary>
    public const string UiMenuOpenId = "menu_open";

    /// <summary>The id of <see cref="UiCodexOpen"/> on the wire.</summary>
    public const string UiCodexOpenId = "codex_open";

    /// <summary>The id of <see cref="UiMapOpen"/> on the wire.</summary>
    public const string UiMapOpenId = "map_open";

    /// <summary>The id of <see cref="UiWaypointSet"/> on the wire.</summary>
    public const string UiWaypointSetId = "waypoint_set";

    /// <summary>The id of <see cref="UiProgressOpen"/> on the wire (#2479).</summary>
    public const string UiProgressOpenId = "progress_open";

    /// <summary>The teaching-flag ids a client reports (#2469). The server raises <c>ui:&lt;id&gt;</c> only for these; a pack
    /// step naming another <c>ui:</c> flag is reported by the validator (a warning — no client sends it).</summary>
    public static readonly IReadOnlyList<string> UiEvents = new[] { UiVegaContinueId, UiMenuOpenId, UiCodexOpenId, UiMapOpenId, UiWaypointSetId, UiProgressOpenId };

    /// <summary>Whether <paramref name="key"/> is a client teaching flag (<see cref="UiPrefix"/>).</summary>
    public static bool IsUiEvent(string? key) => key is not null && key.StartsWith(UiPrefix, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="id"/> (without the prefix) is one of the flags a client reports.</summary>
    public static bool IsKnownUiEventId(string? id)
    {
        foreach (string known in UiEvents)
        {
            if (string.Equals(known, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every key the server raises today (the crystal family expanded), for the pack validator: a step naming
    /// another key is reported (a warning — a pack may be authored before its event ships).</summary>
    public static readonly IReadOnlyList<string> All = BuildAll();

    /// <summary>Whether <paramref name="pattern"/> (a key, or a prefix ending in <c>*</c>) matches <paramref name="key"/>.</summary>
    public static bool Matches(string pattern, string key)
    {
        if (string.IsNullOrEmpty(pattern) || key is null)
        {
            return false;
        }

        return pattern[pattern.Length - 1] == '*'
            ? key.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
            : string.Equals(pattern, key, StringComparison.Ordinal);
    }

    private static string[] BuildAll()
    {
        var keys = new List<string>
        {
            MineAny, BuildAny, BuildRotated, CraftAny, CraftShape, CraftTint, CraftPaint, EatAny, ScanPerformed, ScanAny,
            ResearchAny, SpaceEnter, StationBoarded, TradeBarter, TradePlayer, MissionAccepted, MissionTurnedIn,
            TravelLanded, VisitBody, VisitSystem, HyperjumpAny, ModuleBuilt, ShipCommissioned, StationCommissioned,
            BaseFounded, TameAny, LootAny, DefeatAny, WormholeAny, RelayCommissioned, LaneLinked, StoryFragment,
            LoreRead,
            InsightCompanionWard, InsightShapeNoncube,
            MoveWalked, MoveJump, HotbarSelect, ItemGained, ItemMoved, GearEquipped, CargoStowed, InventoryFull,
            ShipReturned, LampToggled, SuitAutoFeed, SuitClimate, PlayerRespawned, MineRefused, DataFound, FlightFlown,
            MissionBoardAccepted, VisitWorld,
            MinigamePlayed, GadgetUsed, PlanetScanned, AsteroidDestroyed, InteriorEntered, EvaStarted, PlayerZeroG,
            StationLeft, MarkerSet, SalvageFound,
            ShipRepaired, ShipEdited, ShipKeelPlaced, ShipSwitched, BaseBuilt, BaseSealed, BaseWalled, DoorOpened, SpawnSet,
            StationCoreDeployed, StationZeroG,
            CraftStation, FactoryClaimed,
        };
        foreach (string circuitEvent in new[]
                 {
                     CircuitEvents.LampBySwitch, CircuitEvents.ChimeByPlate, CircuitEvents.LampByDaylight, CircuitEvents.DoorByGate,
                     CircuitEvents.MachineJob, CircuitEvents.PhaseOpen, CircuitEvents.NetOn, CircuitEvents.Chime,
                     CircuitEvents.GatePlaced, CircuitEvents.Craft, CircuitEvents.LaserDeep, CircuitEvents.LiftRide,
                     CircuitEvents.Vault,
                 })
        {
            keys.Add(Crystal(circuitEvent));
        }

        return keys.ToArray();
    }
}

/// <summary>
/// The per-player milestone keys of a story pack (#2465, synthesis §5.6) — one place for the strings the condition
/// context reads now and the step runtime writes later. All live in <c>PlayerState.Milestones</c>, so no save change.
/// </summary>
public static class StoryPackKeys
{
    /// <summary><c>story:&lt;pack&gt;:started</c> — the pack started for the player.</summary>
    public static string Started(string packId) => "story:" + packId + ":started";

    /// <summary><c>story:&lt;pack&gt;:done</c> — the player finished the pack.</summary>
    public static string Done(string packId) => "story:" + packId + ":done";

    /// <summary><c>story:&lt;pack&gt;:optout</c> — the player switched the pack off.</summary>
    public static string OptOut(string packId) => "story:" + packId + ":optout";

    /// <summary><c>story:&lt;pack&gt;:step:&lt;step&gt;</c> — the step is done (the step id carries its chapter, e.g. <c>b2.2</c>).</summary>
    public static string StepDone(string packId, string stepId) => "story:" + packId + ":step:" + stepId;

    /// <summary><c>story:&lt;pack&gt;:step:&lt;step&gt;:skipped</c> — the player skipped the step.</summary>
    public static string StepSkipped(string packId, string stepId) => StepDone(packId, stepId) + ":skipped";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:done</c> — the chapter is done.</summary>
    public static string ChapterDone(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":done";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:skipped</c> — the chapter was skipped.</summary>
    public static string ChapterSkipped(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":skipped";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:offered</c> — the optional chapter was offered once (#2466).</summary>
    public static string ChapterOffered(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":offered";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:declined</c> — the player said "Later" to the offer; the chapter stays
    /// listed and startable (#2466).</summary>
    public static string ChapterDeclined(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":declined";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:replay</c> — the chapter is being replayed: its steps run again with the
    /// evidence switched off, while its done key and step keys stay (conditions and gates keep holding) (#2466).</summary>
    public static string ChapterReplay(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":replay";

    /// <summary><c>story:&lt;pack&gt;:ch:&lt;chapter&gt;:active</c> — the player started an optional chapter that is not done
    /// yet (#2466; a non-optional chapter needs no key — it runs by the chapter order).</summary>
    public static string ChapterActive(string packId, string chapterId) => "story:" + packId + ":ch:" + chapterId + ":active";

    /// <summary><c>story:&lt;pack&gt;:replay:&lt;step&gt;</c> — the step was completed again during its chapter's replay
    /// (removed when the replay ends) (#2466).</summary>
    public static string StepReplay(string packId, string stepId) => "story:" + packId + ":replay:" + stepId;

    /// <summary><c>story:&lt;pack&gt;:migrated:v1</c> — the one-time migration of the pack's legacy keys ran (#2466,
    /// synthesis §5.3).</summary>
    public static string Migrated(string packId) => "story:" + packId + ":migrated:v1";

    /// <summary>The prefix of every per-player key of a pack (<c>story:&lt;pack&gt;:</c>).</summary>
    public static string Prefix(string packId) => "story:" + packId + ":";

    /// <summary><c>story:&lt;pack&gt;:beat:&lt;n&gt;</c> — the player heard beat <paramref name="index"/> of a world story (#2470:
    /// written when the beat reaches them, which is never before their story track started).</summary>
    public static string Beat(string packId, int index)
        => "story:" + packId + ":beat:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary><c>story:&lt;pack&gt;:lead:&lt;chapter&gt;:&lt;n&gt;</c> — the player followed lead <paramref name="number"/>
    /// (1-based, the chapter's lead list then its fallbacks — <see cref="StoryLeads.EffectiveLeads"/>) of a world-story chapter
    /// (#2475, synthesis §5.6). Per player: in multiplayer every player follows their own leads.</summary>
    public static string Lead(string packId, string chapterId, int number)
        => "story:" + packId + ":lead:" + chapterId + ":" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The marker between a pack and a cutscene id in the ledger keys.</summary>
    private const string CutsceneInfix = ":cut:";

    /// <summary>The suffix of the "committed" ledger key.</summary>
    private const string CutsceneSentSuffix = ":sent";

    /// <summary><c>story:&lt;pack&gt;:cut:&lt;id&gt;:sent</c> — the server committed the cutscene to the player (sent it, or
    /// queued it behind the cooldown); without the seen key it is re-offered at the next join (#2467, synthesis §5.6).</summary>
    public static string CutsceneSent(string packId, string cutsceneId) => CutsceneSeen(packId, cutsceneId) + CutsceneSentSuffix;

    /// <summary><c>story:&lt;pack&gt;:cut:&lt;id&gt;</c> — the player's client confirmed the cutscene (watched, skipped,
    /// downgraded or unsupported), or the client cannot play cutscenes and got the line instead (#2467).</summary>
    public static string CutsceneSeen(string packId, string cutsceneId) => "story:" + packId + CutsceneInfix + cutsceneId;

    /// <summary>Reads a <see cref="CutsceneSent"/> key back into its pack and cutscene (instance) id.</summary>
    public static bool TryParseCutsceneSent(string? key, out string packId, out string cutsceneId)
    {
        packId = cutsceneId = string.Empty;
        if (key is null || !key.StartsWith("story:", StringComparison.Ordinal) || !key.EndsWith(CutsceneSentSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int infix = key.IndexOf(CutsceneInfix, "story:".Length, StringComparison.Ordinal);
        int idStart = infix + CutsceneInfix.Length;
        int idEnd = key.Length - CutsceneSentSuffix.Length;
        if (infix <= "story:".Length || idEnd <= idStart)
        {
            return false;
        }

        packId = key.Substring("story:".Length, infix - "story:".Length);
        cutsceneId = key.Substring(idStart, idEnd - idStart);
        return true;
    }

    /// <summary>Reads a <see cref="CutsceneSeen"/> key (the confirmed form, not <c>:sent</c>) back into its pack and
    /// cutscene (instance) id (#2468). A cutscene id never holds a <c>:</c>, so the two forms cannot be confused.</summary>
    public static bool TryParseCutsceneSeen(string? key, out string packId, out string cutsceneId)
    {
        packId = cutsceneId = string.Empty;
        if (key is null || !key.StartsWith("story:", StringComparison.Ordinal) || key.EndsWith(CutsceneSentSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int infix = key.IndexOf(CutsceneInfix, "story:".Length, StringComparison.Ordinal);
        int idStart = infix + CutsceneInfix.Length;
        if (infix <= "story:".Length || idStart >= key.Length || key.IndexOf(':', idStart) >= 0)
        {
            return false;
        }

        packId = key.Substring("story:".Length, infix - "story:".Length);
        cutsceneId = key.Substring(idStart);
        return true;
    }

    /// <summary>The key of a running step counter in <c>PlayerState.StepProgress</c>: <c>&lt;pack&gt;:&lt;step&gt;</c>.</summary>
    public static string ProgressKey(string packId, string stepId) => packId + ":" + stepId;
}
