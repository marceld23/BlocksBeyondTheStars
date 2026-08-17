// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One achievement's state for the player's panel: how far along it is and whether it is earned.</summary>
public sealed class NetAchievement
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    /// <summary>Current tally, already clamped to <see cref="Target"/> so the bar never overfills.</summary>
    public int Progress { get; set; }
    public int Target { get; set; } = 1;
    public bool Earned { get; set; }
}

/// <summary>
/// The player's full achievement list with live progress — sent on join and whenever a watched counter moves,
/// so the panel can show "3/5 Eisen abgebaut" rather than just a locked/unlocked flag (which is what makes the
/// list double as a "what can I do next?" guide).
/// </summary>
public sealed class AchievementList
{
    public List<NetAchievement> Items { get; set; } = new();

    /// <summary>The player's raw lifetime counters (<c>mine:any</c>, <c>visit:body</c>, <c>research:any</c> …),
    /// unclamped — the "Journey" figures on the Progress page read these directly (#1103). Additive field on the
    /// contractless MessagePack body: an older client simply ignores it.</summary>
    public Dictionary<string, int> Counters { get; set; } = new();
}

/// <summary>
/// One achievement just earned — the cue for the client's celebration toast. Sent in addition to the refreshed
/// <see cref="AchievementList"/>, so the toast doesn't have to diff two lists to work out what changed.
/// </summary>
public sealed class AchievementUnlocked
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// An achievement is due but its reward has nowhere to go, so it was NOT marked earned — the player is told to
/// make room and it is awarded on the next counter bump. Deliberately deferred rather than unlocked-and-lost:
/// the whole point of the reward is that the player gets it.
/// </summary>
public sealed class AchievementRewardDeferred
{
    public string Key { get; set; } = string.Empty;
}
