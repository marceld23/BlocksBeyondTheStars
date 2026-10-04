// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One rule for server and client (#2280): what a punch with the bare hand does. Fists are the weakest way to fight —
/// 5 damage at most once every 1.2 s (≈ 4.2 per second) sits just under the machete (7 damage, 1.5 s ≈ 4.7 per
/// second), so the first crafted weapon is always an upgrade. Fists used to hit for 15 with no cooldown at all, which
/// out-damaged the machete and most early weapons when clicked fast. The server applies the damage and holds back a
/// punch thrown too early; the client gates its own swing on the same cooldown so the animation never promises a hit
/// the server drops. A tool that is not a weapon (a drill, a scanner) keeps the server's tier-scaled fallback.
/// </summary>
public static class MeleeRules
{
    /// <summary>Damage of one bare-hand punch, before the bio lab's strength factor.</summary>
    public const float FistDamage = 5f;

    /// <summary>Seconds between two bare-hand punches, before the bio lab's reflex factor.</summary>
    public const float FistCooldownSeconds = 1.2f;

    /// <summary>How much earlier than <see cref="FistCooldownSeconds"/> the server still accepts a punch. The client gates
    /// its swing on the exact cooldown, but network jitter can deliver two punches closer together than they were thrown
    /// — without this slack the second one would be dropped although the client showed it. Even punching on the very
    /// edge of the slack stays weaker per second than the machete.</summary>
    public const float FistJitterToleranceSeconds = 0.1f;

    /// <summary>True when the held "tool" is the bare hand: nothing in the slot, or an item without tool properties
    /// (a block, food, a material) — the server resolves both to <see cref="ToolKind.None"/>.</summary>
    public static bool IsBareHand(ToolProperties? tool) => tool is null || tool.Kind == ToolKind.None;
}
