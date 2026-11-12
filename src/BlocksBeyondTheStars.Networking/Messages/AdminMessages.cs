// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

// The in-game admin view (epic #3502): admin mode, the players roster, the event log, the direct chat between an admin
// and a player, and the world's report list. Every message here is new (tags 336–347, NetCodec.Later.cs) and additive —
// no protocol bump: an older server drops the intents, an older client the state.

/// <summary>The admin mode a session is in (<see cref="PlayerStateUpdate.AdminMode"/>) or may enter
/// (<see cref="PlayerStateUpdate.AdminModeAvailable"/>), #3503.</summary>
public static class AdminModes
{
    /// <summary>Not in admin mode (or, as the available mode: no access to the admin view).</summary>
    public const byte Off = 0;

    /// <summary>The invisible admin mode: no presence, no damage, free flight, jump and follow (the observer mode).</summary>
    public const byte Invisible = 1;

    /// <summary>The visible variant of an official hosted world's owner: the panel only — players, log, chat, reports.</summary>
    public const byte Visible = 2;
}

/// <summary>The categories of an <see cref="NetAdminEvent"/> (#3506).</summary>
public static class AdminEventCategories
{
    public const string Pvp = "pvp";
    public const string Death = "death";
    public const string Join = "join";
    public const string Report = "report";
    public const string Moderation = "moderation";
    public const string Admin = "admin";
}

/// <summary>The kinds of an <see cref="NetAdminReport"/> (#3508): which route filed it.</summary>
public static class AdminReportKinds
{
    public const string Player = "player";
    public const string Paint = "paint";
    public const string Shape = "shape";
    public const string Texture = "texture";
    public const string Help = "help";
    public const string Feedback = "feedback";
}

/// <summary>What a player in the roster is doing (<see cref="NetAdminPlayer.State"/>, #3505).</summary>
public static class AdminPlayerStates
{
    public const string OnFoot = "foot";
    public const string Aboard = "aboard";
    public const string InSpace = "space";
    public const string Station = "station";
    public const string Vehicle = "vehicle";
    public const string Train = "train";
    public const string Eva = "eva";
    public const string Flying = "flying";
    public const string Dead = "dead";
    public const string Observer = "observer";
}

/// <summary>Client → server (#3503): enter (<see cref="On"/>) or leave admin mode. The server decides which variant
/// (<see cref="AdminModes"/>) the sender gets, or refuses it.</summary>
public sealed class AdminModeIntent
{
    public bool On { get; set; }
}

/// <summary>Client → server (#3505/#3506): the admin panel opened or closed. Only while it is open does the server stream
/// the roster (once a second), the live events and the report list; opening it sends the roster, the event backlog and the
/// report list at once. Needs admin mode.</summary>
public sealed class AdminPanelIntent
{
    public bool Open { get; set; }
}

/// <summary>Client → server (#3505): jump as the invisible admin — to a player (<see cref="PlayerId"/>: a free spot behind
/// and above them; another body is travelled to silently), or, with <see cref="PlayerId"/> empty and
/// <see cref="HasPosition"/>, to a place (<see cref="LocationId"/> + X/Y/Z, a log row or a report). Only in the invisible
/// admin mode.</summary>
public sealed class AdminJumpIntent
{
    public string PlayerId { get; set; } = string.Empty;

    public string LocationId { get; set; } = string.Empty;

    public bool HasPosition { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }
}

/// <summary>One online player in <see cref="AdminRoster"/> (#3505).</summary>
public sealed class NetAdminPlayer
{
    public string PlayerId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The player's role, <c>Shared.State.PlayerRole</c> as text ("Player", "Admin", "WorldAdmin" …).</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>A fleet admin (the operator of the installation).</summary>
    public bool FleetAdmin { get; set; }

    /// <summary>The body id the player is on, and its display name.</summary>
    public string LocationId { get; set; } = string.Empty;

    public string LocationName { get; set; } = string.Empty;

    /// <summary>Whether X/Y/Z hold the player's position. False for the visible variant of an official hosted world's owner
    /// (child safety): that viewer learns the body only, never where on it a player stands.</summary>
    public bool HasPosition { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float Health { get; set; }

    /// <summary>One of <see cref="AdminPlayerStates"/>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>In a duel, and with whom (the other player's name).</summary>
    public bool InDuel { get; set; }

    public string DuelWith { get; set; } = string.Empty;

    /// <summary>No other player can hurt this one right now (spawn protection, a ship, god mode, no PvP …).</summary>
    public bool Protected { get; set; }

    /// <summary>The player's chat is paused (an admin's /silence or the automatic mute).</summary>
    public bool Silenced { get; set; }

    /// <summary>Seconds since the player last moved.</summary>
    public int IdleSeconds { get; set; }
}

/// <summary>Server → client (#3505): every online player of the world on every body, once a second while the admin panel
/// is open. Observers are listed (flagged <see cref="AdminPlayerStates.Observer"/>) only to admins who may see them.</summary>
public sealed class AdminRoster
{
    public NetAdminPlayer[] Players { get; set; } = System.Array.Empty<NetAdminPlayer>();
}

/// <summary>One entry of the event log (#3506). The text is <see cref="TextKey"/> (a locale key) with
/// <see cref="Args"/> in its <c>{0}</c>, <c>{1}</c> … placeholders; an argument starting with a single <c>@</c> is itself a
/// locale key (a death cause). Anything a player wrote (a name, a note, a feedback title) is literal: the server doubles
/// its leading <c>@</c> (<c>@@…</c> reads as the text <c>@…</c>), and a placeholder inside an argument is never filled.</summary>
public sealed class NetAdminEvent
{
    /// <summary>Increasing per server run; the client keeps the newest first and drops duplicates by it.</summary>
    public long Id { get; set; }

    /// <summary>Wall-clock time of the event, Unix seconds (UTC).</summary>
    public long UnixTime { get; set; }

    /// <summary>One of <see cref="AdminEventCategories"/>.</summary>
    public string Category { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string ActorName { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    /// <summary>Where it happened (a body id), with <see cref="HasPosition"/> the place — the log row offers "Jump".</summary>
    public string LocationId { get; set; } = string.Empty;

    public bool HasPosition { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public string TextKey { get; set; } = string.Empty;

    public string[] Args { get; set; } = System.Array.Empty<string>();
}

/// <summary>Server → client (#3506): the event log as it stands, oldest first (up to 500), when the admin panel opens.</summary>
public sealed class AdminEventBacklog
{
    public NetAdminEvent[] Events { get; set; } = System.Array.Empty<NetAdminEvent>();
}

/// <summary>Server → client (#3506): one new event, live, to every admin whose panel is open.</summary>
public sealed class AdminEvent
{
    public NetAdminEvent Event { get; set; } = new();
}

/// <summary>Client → server (#3507): a direct line. An admin may write to any player at any time; a player may only answer
/// an admin who wrote to them in the last 30 minutes (no player-to-player whispers).</summary>
public sealed class DirectChatIntent
{
    public string ToPlayerId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>Server → client (#3507): a direct line, to the addressee, the sender and the admins whose panel is open.
/// <see cref="FromAdmin"/> marks the admin's side (the player's chat shows it as "Admin → you").</summary>
public sealed class DirectChatMessage
{
    public string FromId { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public string ToId { get; set; } = string.Empty;

    public string ToName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool FromAdmin { get; set; }

    /// <summary>Wall-clock time of the line, Unix seconds (UTC).</summary>
    public long UnixTime { get; set; }
}

/// <summary>One row of the world's report list (#3508).</summary>
public sealed class NetAdminReport
{
    public long Id { get; set; }

    /// <summary>One of <see cref="AdminReportKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string ReporterId { get; set; } = string.Empty;

    public string ReporterName { get; set; } = string.Empty;

    /// <summary>The reported player (a player report) or the owner of the reported content; empty for help and feedback.</summary>
    public string TargetId { get; set; } = string.Empty;

    public string TargetName { get; set; } = string.Empty;

    /// <summary>The reporter's note, the help request, the feedback title, or the reported design ("#12", a texture key).</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>The place, when known — "Jump there".</summary>
    public string LocationId { get; set; } = string.Empty;

    public bool HasPosition { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    /// <summary>The reported player's recent chat lines (a player report), as the other players saw them.</summary>
    public string[] Evidence { get; set; } = System.Array.Empty<string>();

    public long CreatedUnix { get; set; }

    public bool Done { get; set; }

    /// <summary>The admin who marked it done.</summary>
    public string HandledBy { get; set; } = string.Empty;
}

/// <summary>Server → client (#3508): the world's reports — the open ones first, then the newest done ones — when the admin
/// panel opens and whenever the list changes.</summary>
public sealed class AdminReportList
{
    public NetAdminReport[] Reports { get; set; } = System.Array.Empty<NetAdminReport>();
}

/// <summary>Client → server (#3508): an admin marks a report done (or open again).</summary>
public sealed class AdminReportAction
{
    public long Id { get; set; }

    public bool Done { get; set; }
}

/// <summary>Client → server (#3508): "Call an admin" — a help request without naming a player (stuck, lost, a question).
/// The pause menu's button sends it; the chat's <c>/admin Text</c> reaches the same handler as a chat line. Rate-limited
/// like <c>/report</c>.</summary>
public sealed class CallAdminIntent
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>Client → server (#3508): a player report by name. On an official hosted world the client files it with the
/// portal and ALSO sends it here with <see cref="AlsoFiledWithPortal"/>, so the world's own admins see it too and the server
/// does not forward it to the operators a second time.</summary>
public sealed class PlayerReportIntent
{
    public string TargetName { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public bool AlsoFiledWithPortal { get; set; }
}
