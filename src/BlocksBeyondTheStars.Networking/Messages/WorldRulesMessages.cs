// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// The active world rule set, sent to a client right after it joins so it can show the
/// server's mode/rules and explain when an action is blocked by them.
/// </summary>
public sealed class ServerRules
{
    public string GameMode { get; set; } = string.Empty;
    public string Pvp { get; set; } = string.Empty;

    /// <summary>#2972: allies and crew mates can hurt each other in PvP. EMPTY from a server that predates on-foot PvP — the
    /// client then hides the PvP rows.</summary>
    public string FriendlyFire { get; set; } = string.Empty;
    public string AggressiveAliens { get; set; } = string.Empty;
    public string EnvironmentalHazards { get; set; } = string.Empty;
    public string DeathPenalty { get; set; } = string.Empty;
    public bool KeepInventoryOnDeath { get; set; }

    /// <summary>When true, a ship defeated in space combat is recovered intact to base (casual safety net);
    /// when false it is left a wreck the owner must repair before flying again.</summary>
    public bool KeepShipOnDeath { get; set; } = true;
    public bool OxygenEnabled { get; set; }
    public bool AdminCheatsActive { get; set; }

    // World options (live-editable by the world admin; shown in the in-game settings section).
    public string CreatureAbundance { get; set; } = string.Empty;
    public string PlanetEnemies { get; set; } = string.Empty;
    public string SpaceNpcEnemies { get; set; } = string.Empty;
    public string AlienUfos { get; set; } = string.Empty;
    public string Bandits { get; set; } = string.Empty;

    /// <summary>Instant Travel world option: when true the travel screen may quick-travel anywhere; when
    /// false it is limited to bodies the player has already landed on (default).</summary>
    public bool InstantTravel { get; set; }

    /// <summary>Auto-aim world option (#693): when true (default) weapons acquire targets in a forward cone
    /// automatically; when false only the entity under the crosshair can be hit (manual aiming).</summary>
    public bool AutoAim { get; set; } = true;

    /// <summary>Starter-teleporter world option (#1056): when true every joining player is handed a suit
    /// teleporter (multiplayer convenience); default false.</summary>
    public bool StarterTeleporter { get; set; }

    /// <summary>"Back to my ship" world option (#2286): "On" when the pause menu may offer the rescue teleport back aboard
    /// the own ship, "Off" when the world has it switched off. EMPTY from a server that predates the rule (it would drop
    /// the intent anyway) — the client then hides the button and the world-options row, like <see cref="WorldTextures"/>.</summary>
    public string ReturnToShip { get; set; } = string.Empty;

    /// <summary>#2269: machines catch up for at most this many minutes of a world's absence (0 = off); -1 from a server
    /// that predates the rule — the client then hides the row.</summary>
    public int MachineCatchUpMinutes { get; set; } = -1;

    /// <summary>World textures (#1958): "Admins" when the world's admins may publish textures for everyone,
    /// "Off" when the world has them switched off. EMPTY from a server that predates the feature — the client
    /// then hides everything about world textures.</summary>
    public string WorldTextures { get; set; } = string.Empty;

    /// <summary>Frontier-danger world option (#1122): when true, machines in the outermost frontier tier
    /// spawn as the tougher variant. Opt-in risk dial; the frontier's richness is unconditional.</summary>
    public bool FrontierDanger { get; set; }

    /// <summary>Visitors-at-the-base world option (#1224): when true, bandit scouts occasionally look at a
    /// founded base whose owner is home (zone edge only, never inside, never destructive). Opt-in.</summary>
    public bool BaseVisitors { get; set; }

    /// <summary>"Friendly visitors" world option (#2602): "On" when a neighbour, a travelling merchant or a lost traveller
    /// now and then visits a base whose owner is home, "Off" when the world switched them off. EMPTY from a server that
    /// predates the rule — the client then hides the world-options row.</summary>
    public string FriendlyVisitors { get; set; } = string.Empty;

    /// <summary>The world's tutorial rule (#2466): "Auto", "OptIn", "Off" or "Courses" (#2498, "Short courses only" — an older
    /// client does not know the word and hides the row). EMPTY from a server that predates it.</summary>
    public string TutorialPolicy { get; set; } = string.Empty;

    /// <summary>The world's story pace (#2472): "Sparse", "Normal" or "Dense" (<c>GameRules.StoryDensity</c>). EMPTY from a
    /// server that predates the live edit — the client then hides the world-rules row.</summary>
    public string StoryDensity { get; set; } = string.Empty;

    /// <summary>The world's main story (#2506): the running world story's pack id, or "none" — filled only for a world admin,
    /// and only by a server whose <see cref="StorySelectIntent"/> keeps progress (switching back restores the story where it
    /// stopped). EMPTY from an older server, whose switch started the story over, and for everyone else — the client then
    /// shows no "Main story" row.</summary>
    public string MainStory { get; set; } = string.Empty;

    /// <summary>Whether the server accepts/relays live voice chat (opt-in; default off on dedicated servers).
    /// When false the client keeps voice capture disabled and shows voice comms as unavailable.</summary>
    public bool VoiceChatEnabled { get; set; }

    /// <summary>The EFFECTIVE chat screening mode of this world ("Open", "Filtered" or "Safe" — the world rule
    /// after the operator's server-wide switch, #1207), so a client can label the chat box accordingly. Older
    /// servers leave it empty.</summary>
    public string ChatMode { get; set; } = string.Empty;

    /// <summary>Per-player mode overrides (#1121), parallel to <see cref="PlayerModeValues"/> — the online
    /// players' names, filled only when the RECEIVER is a world admin (they feed the Character-tab
    /// "player modes" rows). Empty for everyone else. Note <see cref="GameMode"/> above is already the
    /// receiver's EFFECTIVE mode, override included.</summary>
    public string[] PlayerModeNames { get; set; } = System.Array.Empty<string>();

    /// <summary>Current override per player in <see cref="PlayerModeNames"/>: "None", "Survival" or "Creative".</summary>
    public string[] PlayerModeValues { get; set; } = System.Array.Empty<string>();
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
    public string Bandits { get; set; } = string.Empty;

    /// <summary>Instant Travel toggle: "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string InstantTravel { get; set; } = string.Empty;

    /// <summary>Keep-inventory-on-death toggle: "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string KeepInventoryOnDeath { get; set; } = string.Empty;

    /// <summary>Keep-ship-on-death toggle: "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string KeepShipOnDeath { get; set; } = string.Empty;

    /// <summary>Environmental-hazards tier: "Off"/"Light"/"Normal"/"Hard" to set it, empty to leave
    /// unchanged (#670) — the live switch for the temperature survival hazard.</summary>
    public string EnvironmentalHazards { get; set; } = string.Empty;

    /// <summary>Auto-aim toggle (#693): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string AutoAim { get; set; } = string.Empty;

    /// <summary>Starter-teleporter toggle (#1056): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string StarterTeleporter { get; set; } = string.Empty;

    /// <summary>"Back to my ship" toggle (#2286): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string ReturnToShip { get; set; } = string.Empty;

    /// <summary>#2972: the PvP mode — "Off"/"DuelsOnly"/"GroupBased"/"On" — empty to leave unchanged.</summary>
    public string Pvp { get; set; } = string.Empty;

    /// <summary>#2972: friendly fire, "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string FriendlyFire { get; set; } = string.Empty;

    /// <summary>#2269: the machines' catch-up in minutes (one of <c>CrystalNetRules.CatchUpChoicesMinutes</c>); -1 = unchanged.</summary>
    public int MachineCatchUpMinutes { get; set; } = -1;

    /// <summary>World-textures toggle (#1958): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string WorldTextures { get; set; } = string.Empty;

    /// <summary>Frontier-danger toggle (#1122): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string FrontierDanger { get; set; } = string.Empty;

    /// <summary>Visitors-at-the-base toggle (#1224): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string BaseVisitors { get; set; } = string.Empty;

    /// <summary>Friendly-visitors toggle (#2602): "On"/"Off" to set it, empty to leave unchanged.</summary>
    public string FriendlyVisitors { get; set; } = string.Empty;

    /// <summary>The tutorial rule (#2466): "Auto"/"OptIn"/"Off"/"Courses" (#2498) to set it, empty to leave unchanged.</summary>
    public string TutorialPolicy { get; set; } = string.Empty;

    /// <summary>The story pace (#2472): "Sparse"/"Normal"/"Dense" to set it, empty to leave unchanged.</summary>
    public string StoryDensity { get; set; } = string.Empty;
}
