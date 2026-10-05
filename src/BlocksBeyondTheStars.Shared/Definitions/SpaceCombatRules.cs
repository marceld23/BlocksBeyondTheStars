// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Space-combat numbers the server and the client must agree on (#2284). The server applies them — hostiles hurt a
/// ship only inside the engage range — and the client draws from them: the enemy shots it shows, the "attacking"
/// classification of the target lock and the HUD's threat ticks all read the same range, so the two sides cannot
/// drift apart again (the client used to keep its own copy of the server's constant).
/// </summary>
public static class SpaceCombatRules
{
    /// <summary>A hostile ship or drone fires on a pilot only once it is this close (units of the flight instance).
    /// Beyond it a distant hostile cannot plink a ship forever, and flying clear of a fight really stops the damage
    /// and lets the shield recharge. The starter ship's laser (<c>ship_laser_basic</c>, <c>weapon_range</c> 60 in
    /// <c>data/ship_modules.json</c>) reaches to within ten units of it, so a child shot at from the edge of the
    /// aura has to fly only a little closer to shoot back — not the 25 units of the old 45 (#2284).</summary>
    public const float EngageRange = 70f;
}
