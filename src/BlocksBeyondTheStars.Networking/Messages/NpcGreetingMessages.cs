// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

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
/// Server → client: a contextual greeting line to show as a speech bubble over an NPC (item 15). When an LLM
/// backend is enabled the <see cref="Text"/> is LLM-authored in the player's language; when AI is off/unreachable
/// the server sends an empty <see cref="Text"/> and the client shows a localized static fallback by role.
/// #2593: also carries passing greetings, welcomes, villager chatter, cheers, guests and alarms (<see cref="Kind"/>),
/// an optional gesture and the bubble's id (an AI upgrade reuses it).
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

    /// <summary>#2593: the bubble's identity — server-wide increasing; an AI upgrade of the same bubble reuses it, so the
    /// client replaces the text without playing the voice or the gesture again. 0 = an older server (the client then
    /// guesses: a second line for the same NPC within 25 s is an upgrade). Additive contractless field.</summary>
    public int GreetingId { get; set; }

    /// <summary>#2593: what kind of line this is (<see cref="NpcGreetingKinds"/>); 0 = <see cref="NpcGreetingKinds.Addressed"/>,
    /// which is also what an older server sends. For every kind but Addressed the server always fills <see cref="Text"/>
    /// (server-localized), so an older client still shows a sensible bubble. Additive contractless field.</summary>
    public byte Kind { get; set; }

    /// <summary>#2593: the gesture the NPC plays with the line (<see cref="NpcGestures"/>) — only on the FIRST message of a
    /// greeting, never on its AI upgrade. Additive contractless field.</summary>
    public byte Gesture { get; set; }
}

/// <summary>#2593: the kinds of an <see cref="NpcGreeting"/> (its <see cref="NpcGreeting.Kind"/>). The client picks the
/// voice priority, the gesture handling and the fallback line by kind.</summary>
public static class NpcGreetingKinds
{
    /// <summary>The player spoke to the NPC (E, a market stall, a mission board) — today's greeting.</summary>
    public const byte Addressed = 0;

    /// <summary>The NPC greets a player walking past.</summary>
    public const byte Passing = 1;

    /// <summary>"Welcome back" after a long time away.</summary>
    public const byte Welcome = 2;

    /// <summary>One line of two villagers talking to each other.</summary>
    public const byte Chatter = 3;

    /// <summary>A cheer (the village festival).</summary>
    public const byte Cheer = 4;

    /// <summary>A visitor at the player's base speaks.</summary>
    public const byte Guest = 5;

    /// <summary>An alarm (a guard spotting bandit scouts).</summary>
    public const byte Alert = 6;
}

/// <summary>#2593: the gestures an <see cref="NpcGreeting"/> can carry (its <see cref="NpcGreeting.Gesture"/>).</summary>
public static class NpcGestures
{
    /// <summary>No gesture.</summary>
    public const byte None = 0;

    /// <summary>A wave with the free hand (~1.2 s).</summary>
    public const byte Wave = 1;

    /// <summary>A short nod (~0.6 s).</summary>
    public const byte Nod = 2;

    /// <summary>Both arms up and bobbing (~1.2 s).</summary>
    public const byte Cheer = 3;
}
