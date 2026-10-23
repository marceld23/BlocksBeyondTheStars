// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

// Wire messages. Plain classes with parameterless constructors and get/set properties so
// they serialize cleanly with MessagePack's contractless resolver (no attributes needed).
//
// Client -> Server messages are *intents*: the client asks; the server decides.
// Server -> Client messages are authoritative *state*.

public sealed class JoinRequest
{
    public int ProtocolVersion { get; set; } = BlocksBeyondTheStars.Networking.Protocol.Version;
    public string PlayerName { get; set; } = string.Empty;
    public string? Password { get; set; }

    /// <summary>Per-install secret proving name ownership (name verification): the first join under a
    /// name claims it; later joins must present the same token or are rejected. Only its hash is stored
    /// server-side. Optional — clients without one leave the name unclaimed.</summary>
    public string? Token { get; set; }

    /// <summary>Opaque control-plane join token for hosted worlds (see <c>HostedJoinToken</c> in Shared).
    /// Required only when the server runs with a JoinTokenSecret; everywhere else it is ignored. Additive
    /// field — both codecs are contractless, so clients without it stay wire-compatible.</summary>
    public string? HostedToken { get; set; }

    /// <summary>The player's chosen UI language ("en"/"de"). The server remembers it so dynamic, server-authored
    /// text (item 15: LLM NPC greetings) is generated in the player's language. Defaults to English.</summary>
    public string Locale { get; set; } = "en";

    /// <summary>The client's chosen render distance in chunks (the in-game View Distance slider). The server
    /// streams this player's terrain at this radius (clamped server-side) instead of the host config, so a wider
    /// slider extends the visible terrain on dedicated servers too. 0 (default) = let the server decide.</summary>
    public int ViewDistanceChunks { get; set; }

    /// <summary>Arcade install id (glitch.fun) — the only identity a guest without a portal account has, and
    /// therefore the only thing that makes their <c>/report</c> actionable for an operator (#1222). Additive
    /// field on a contractless codec: older clients simply leave it empty. Never shown to other players.</summary>
    public string? InstallId { get; set; }

    /// <summary>The fingerprint of the client's block palette (#2222, v9 — <c>GameContent.BlockFingerprint</c>).
    /// Chunks travel as raw numeric block ids, so the server refuses a join whose fingerprint is missing or
    /// differs from its own: the client would draw the wrong blocks. Empty = none sent.</summary>
    public string ContentFingerprint { get; set; } = string.Empty;

    /// <summary>What this client understands beyond the protocol version (#2466, synthesis §5.7) — <see cref="ClientFeatures"/>
    /// words. A client without <see cref="ClientFeatures.Packs1"/> gets the legacy tutorial mode (old keys, no pack lines
    /// it cannot localize). Additive on a contractless codec: an older client sends none.</summary>
    public string[] Features { get; set; } = System.Array.Empty<string>();
}

/// <summary>The words of <see cref="JoinRequest.Features"/> (#2466).</summary>
public static class ClientFeatures
{
    /// <summary>The client understands story packs: pack lines and chip keys, <see cref="ShipAiLine.PackId"/>,
    /// <see cref="ShipAiLine.ObjectiveSource"/> and the pack messages.</summary>
    public const string Packs1 = "packs1";

    /// <summary>The client plays data-driven cutscenes (step 9 announces it).</summary>
    public const string Cutscenes1 = "cutscenes1";

    /// <summary>The client draws <see cref="ShipAiLine.Choices"/> as buttons in VEGA's panel (#2499) — the Unity client
    /// announces it; a headless client or an older build does not, and gets the plain lines it always got.</summary>
    public const string Choices1 = "choices1";

    /// <summary>#2616: the client's NPC dialogue panel takes the server's close (<see cref="NpcDialogState.ActionClose"/>)
    /// and sends the leave when the player closes it — the Unity client announces it. An older build gets no close: its
    /// panel would pop open again to show a line the player already closed.</summary>
    public const string DialogClose1 = "dialogclose1";
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

/// <summary>Client asks the server to flush the world + all players to disk now (an explicit save, on top
/// of the periodic autosave).</summary>
public sealed class SaveGameIntent
{
}

public sealed class JoinAccepted
{
    public int ProtocolVersion { get; set; } = BlocksBeyondTheStars.Networking.Protocol.Version;
    public string PlayerId { get; set; } = string.Empty;
    public long WorldSeed { get; set; }
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>Friendly name of the body the player is on, and its star system (for the HUD).</summary>
    public string PlanetName { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;

    /// <summary>The server's id of the joined world (#1534, v5) — see <see cref="ChunkDataMessage.WorldId"/>.</summary>
    public int WorldId { get; set; }

    /// <summary>Total seconds this world has been played (server-accumulated, see
    /// <c>WorldMetadata.CumulativePlaytimeSeconds</c>); shown alongside the per-session timer in the HUD.</summary>
    public long CumulativePlaytimeSeconds { get; set; }

    /// <summary>Whether this save was created with continents (#704) — the client's preview generators
    /// must apply the same gate or orbit/minimap textures would show the wrong coastlines.</summary>
    public bool TerrainContinents { get; set; }

    /// <summary>The terrain generation this save was created with (#1644) — the client's preview generators
    /// must apply the same generation as the server, like <see cref="TerrainContinents"/>. Contractless
    /// MessagePack: an older client ignores it, an older server leaves it 0 (the classic generators).</summary>
    public int TerrainGeneration { get; set; }

    /// <summary>Whether this save was created with lava-core volcanoes (#1631) — the planet map previews (#2172) must
    /// apply it like <see cref="TerrainContinents"/> or volcanic worlds would show the wrong relief. Appended
    /// contractless field: an older server leaves it false.</summary>
    public bool TerrainLavaCoreVolcanoes { get; set; }

    /// <summary>The fingerprint of the server's block palette (#2222, v9) — the client compares it with its own and
    /// leaves when it is missing or different, the mirror of the server's check on <see cref="JoinRequest"/>.</summary>
    public string ContentFingerprint { get; set; } = string.Empty;
}

public sealed class JoinRejected
{
    public string Reason { get; set; } = string.Empty;
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
/// Operator/admin maintenance announcement ("new version releasing, server restarts in 10 minutes").
/// Unlike <see cref="ServerMessage"/> (a transient HUD toast) the client renders this as a prominent
/// banner/modal that cannot be dismissed accidentally. Restart countdowns are re-broadcast at shrinking
/// thresholds; each broadcast carries the authoritative remaining seconds so clients re-sync their timers.
/// </summary>
public sealed class MaintenanceNotice
{
    public const byte KindInfo = 0;
    public const byte KindRestartCountdown = 1;
    public const byte KindCancelled = 2;

    public byte Kind { get; set; }

    /// <summary>Optional locale key for a standard message (e.g. "ui.maint.restart_in") so the client
    /// renders it in the player's language. Empty when only <see cref="Text"/> is set.</summary>
    public string MessageKey { get; set; } = string.Empty;

    /// <summary>Operator free text, shown verbatim (already sanitized server-side). May be empty.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Seconds until the restart for <see cref="KindRestartCountdown"/> (0 = restarting now);
    /// -1 for notices without a countdown.</summary>
    public int SecondsRemaining { get; set; } = -1;
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

    /// <summary>The server's id of the world whose stream follows (#1534, v5) — see <see cref="ChunkDataMessage.WorldId"/>.</summary>
    public int WorldId { get; set; }

    /// <summary>#2723: the player id of the host when the receiver is loaded into a FOREIGN ship interior as a docking
    /// guest; empty in the own ship interior and on every other world. Additive — an older server never sets it.</summary>
    public string ShipOwnerId { get; set; } = string.Empty;
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
