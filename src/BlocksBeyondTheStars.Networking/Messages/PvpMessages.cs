// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#2972): the players this player may hurt right now — the world's PvP rules, alliances, crews, duels and
/// every situational guard already applied. The client lets only these avatars into its crosshair and auto-aim; the server
/// still checks every hit. Sent when the list changes (at most once a second); empty when PvP is off.
/// </summary>
public sealed class PvpTargets
{
    public string[] PlayerIds { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// Client → server (#2972): a duel. <see cref="Action"/> is <c>challenge</c> (to <see cref="TargetPlayerId"/>),
/// <c>accept</c> / <c>decline</c> (the challenge from <see cref="TargetPlayerId"/>) or <c>end</c> (give up the running duel).
/// </summary>
public sealed class DuelIntent
{
    public string Action { get; set; } = string.Empty;

    public string TargetPlayerId { get; set; } = string.Empty;
}

/// <summary>
/// Server → client (#2972): a duel changed. <see cref="Kind"/> is <c>request</c> (<see cref="Other"/> challenges you),
/// <c>started</c>, <c>declined</c> (<see cref="Other"/> said no, or the challenge ran out) or <c>ended</c> (<see cref="Winner"/>
/// won; empty when it ended without a winner — given up, timed out, a player left).
/// </summary>
public sealed class DuelNotice
{
    public string Kind { get; set; } = string.Empty;

    public string Other { get; set; } = string.Empty;

    public string Winner { get; set; } = string.Empty;
}
