// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Client → server (tag 355): the game events the sender's playtest task waits for (the "Test Pilot" page). The
/// server reports every such event the SENDER causes back as a <see cref="PlaytestEvent"/> — and nothing else: it
/// stores nothing, pays nothing and counts nothing. Which step that ticks is the client's own bookkeeping, kept per
/// install; a playtest badge buys nothing in the world, so the server has no say in it.
/// <para>Each entry is a published event key (<c>GameEventKeys</c>) or a prefix pattern ending in <c>*</c>; at most
/// sixteen, anything else is left out of the answer. A new list replaces the old one; an empty list ends the watch.
/// The server answers with <see cref="PlaytestWatchAck"/>. An older server drops the unknown tag — no answer comes,
/// and the client lets the player tick those steps off by hand.</para>
/// </summary>
public sealed class PlaytestWatchIntent
{
    /// <summary>The event keys or patterns to watch.</summary>
    public List<string> Patterns { get; set; } = new();
}

/// <summary>Server → client (tag 356): the patterns of a <see cref="PlaytestWatchIntent"/> the server now watches —
/// the proof that this server reports playtest events at all.</summary>
public sealed class PlaytestWatchAck
{
    /// <summary>The accepted patterns (a subset of what was asked for).</summary>
    public List<string> Patterns { get; set; } = new();
}

/// <summary>Server → client (tag 357): a watched game event the receiver caused. Events of one tick with the same key
/// and argument arrive as one message with the amounts added up.</summary>
public sealed class PlaytestEvent
{
    /// <summary>The event key (<c>space:enter</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The event's argument (<c>launch</c>), or empty.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>How often it happened.</summary>
    public int Amount { get; set; } = 1;
}
