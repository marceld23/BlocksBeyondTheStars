// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The per-line chat throttle (#3187): every chat line and every chat command that does work for the sender — <c>/bump</c>
/// and the <c>/report</c> family, the F1 bump report — spends one <see cref="PlayerSessionChat.MinLineIntervalMs"/> slot.
/// The clock is a stopwatch the server starts with itself: real time (chat works while a paused world holds the simulation
/// clock), 64-bit milliseconds that never wrap, and available to the netstandard2.1 in-process server as well. The old
/// 32-bit <c>Environment.TickCount</c> turned negative after 24.9 days of host uptime, and against a "last line" that
/// started at 0 every line then looked too fast — chat and <c>/report</c> went silent on long-running hosts.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The chat clock's source: started with the server, read in whole milliseconds.</summary>
    private readonly Stopwatch _chatClock = Stopwatch.StartNew();

    /// <summary>Test seam: the chat clock pinned to a value (ms), or null for the real one.</summary>
    private long? _chatClockForTestMs;

    /// <summary>Milliseconds on the chat clock (see the class summary).</summary>
    private long ChatClockMs => _chatClockForTestMs ?? _chatClock.ElapsedMilliseconds;

    /// <summary>Spends the sender's chat slot: true when the line or command may go ahead, false while the previous one is
    /// younger than <see cref="PlayerSessionChat.MinLineIntervalMs"/> (the line is dropped without an answer).</summary>
    private bool TakeChatSlot(PlayerSession session) => session.Chat.TryTakeSlot(ChatClockMs);

    /// <summary>Test seam (#3187): pins the chat clock to <paramref name="milliseconds"/>; null goes back to the real one.</summary>
    internal void SetChatClockForTest(long? milliseconds) => _chatClockForTestMs = milliseconds;
}
