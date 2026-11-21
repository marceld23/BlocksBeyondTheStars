// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The escort cruiser (#3900) — the space boss — in the numbers the server and the client must agree on. Its strength
/// (hull, shield, the mega laser) is data (<c>data/enemies.json</c>, key <see cref="EnemyKey"/>); what is here is how it
/// arrives, how it is recognised on the wire and when it gives up. On the wire it is a space entity of kind
/// <c>Cruiser</c> with <see cref="ModelScale"/>, and the entity a <c>BossStatus</c> of source
/// <see cref="BossSources.Space"/> names.
/// </summary>
public static class EscortCruiserRules
{
    /// <summary>The enemy key in <c>data/enemies.json</c> and the Codex.</summary>
    public const string EnemyKey = "escort_cruiser";

    /// <summary>The boss bar's name key.</summary>
    public const string NameKey = "enemy.escort_cruiser.name";

    /// <summary><c>SpaceWarpFx.Style</c> of its arrival from hyperspace and of its jump away.</summary>
    public const string WarpStyle = "escort_cruiser";

    /// <summary><c>NetCombatEntity.Scale</c> of the boss — the plain cruiser has 1.</summary>
    public const float ModelScale = 2.5f;

    /// <summary>Seconds between VEGA's warning ("a jump signature") and the warp-in.</summary>
    public const double WarnSeconds = 12.0;

    /// <summary>How far from the pilot it comes out of hyperspace, and the least distance to every other pilot.</summary>
    public const float WarpInDistance = 225f;

    /// <summary>The least distance to any pilot the arrival point keeps.</summary>
    public const float WarpInMinDistance = 220f;

    /// <summary>How far it notices a ship and comes for it (it warps in inside this, so it comes straight at the pilot).</summary>
    public const float NoticeRange = 240f;

    /// <summary>The ring it holds round its quarry — inside the damage aura, far enough for its long barrel.</summary>
    public const float StandOff = 48f;

    /// <summary>Its flight speed (units per second) before it enrages — a third of a ship's, so flying away always works.</summary>
    public const float Speed = 5f;

    /// <summary>With no pilot inside this many times <see cref="NoticeRange"/> for <see cref="LeaveSeconds"/> it jumps away.</summary>
    public const float LeaveRangeFactor = 2f;

    /// <summary>See <see cref="LeaveRangeFactor"/>.</summary>
    public const double LeaveSeconds = 45.0;

    /// <summary>The earliest and the latest a rolled ambush arrives after the flight began (seconds, the warning included).</summary>
    public const double AmbushDelayMin = 60.0;

    /// <summary>See <see cref="AmbushDelayMin"/>.</summary>
    public const double AmbushDelayMax = 150.0;

    /// <summary>The most the ambush chance of one flight can be, whatever the sliders say.</summary>
    public const double AmbushChanceCap = 0.25;

    /// <summary>The story chapter (of the VEGA pack) from which the rare ambush may happen.</summary>
    public const string AmbushFromChapter = "sleeps";

    /// <summary><c>BossStatus.Phase</c>: the fight's first phase.</summary>
    public const byte PhaseNormal = 1;

    /// <summary><c>BossStatus.Phase</c>: enraged (from half hull on) — faster, the mega laser comes more often.</summary>
    public const byte PhaseEnraged = 2;

    /// <summary>The chance that a flight meets the boss: by the space-hostiles slider, times the world's Danger factor,
    /// capped at <see cref="AmbushChanceCap"/>. Off (either of the two) is exactly 0.</summary>
    public static double AmbushChance(AlienActivity spaceNpcEnemies, double dangerFactor)
    {
        double chance = spaceNpcEnemies switch
        {
            AlienActivity.Rare => 0.04,
            AlienActivity.Normal => 0.08,
            AlienActivity.Frequent => 0.12,
            AlienActivity.Extreme => 0.16,
            _ => 0.0,
        };
        return System.Math.Min(AmbushChanceCap, chance * System.Math.Max(0.0, dangerFactor));
    }
}

/// <summary>The values of <c>BossStatus.Source</c>: which kind of boss the bar belongs to.</summary>
public static class BossSources
{
    /// <summary>A guardian giant (on a planet).</summary>
    public const string Giant = "giant";

    /// <summary>A space boss — the escort cruiser.</summary>
    public const string Space = "space";
}
