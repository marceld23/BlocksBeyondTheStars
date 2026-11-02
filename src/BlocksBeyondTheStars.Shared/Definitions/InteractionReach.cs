// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Reaches the client's prompts and the server's checks share (#3087, #3089), so a prompt never offers what the server
/// would then refuse: the client measures with these exact numbers, the server accepts them (the talk with a little slack
/// for the position the server last heard of).
/// </summary>
public static class InteractionReach
{
    /// <summary>How far a player talks to a person (E, the "Talk to …" prompt and the server's talk check).</summary>
    public const float Talk = 5f;

    /// <summary>How close a player stands to a mission board — of a settlement, a station, a base or the own ship — to take
    /// and hand in its jobs; the "Jobs" prompt at a board in the crosshair uses the same reach.</summary>
    public const float MissionBoard = 4f;

    /// <summary>The block key of the mission board (#3087).</summary>
    public const string MissionBoardBlock = "mission_board";
}
