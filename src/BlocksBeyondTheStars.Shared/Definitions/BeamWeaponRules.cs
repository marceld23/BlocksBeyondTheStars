// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The held beam of a weapon (#3898, the Guardian laser): a TAP of the fire control is the weapon's ordinary shot, and
/// while the control stays HELD the weapon fires a continuous beam. The numbers are the item's own
/// (<see cref="ToolProperties.BeamDamagePerSecond"/>, <see cref="ToolProperties.BeamEnergyPerSecond"/>,
/// <see cref="ToolProperties.BeamRange"/>, <see cref="ToolProperties.BeamTick"/>); the rules around them — what counts
/// as a hold, the cadence, the reserve the beam never touches — are here, one place the server and the client both read.
/// <para><b>Balance (the Guardian laser: 55 damage/s, 9 energy/s, 22 blocks, a hit every 0.25 s).</b> One tick is
/// 13.75 damage for 2.25 energy. A full suit (100) feeds the beam for 37 ticks = 9.25 s and about 509 damage before it
/// stops at the reserve of 15; with the suit battery (150) it is 56 ticks = 14 s and 770 damage. The same suit fires 50
/// plasma-blaster shots (2 energy, 55 damage: 2750 damage) or 50 of the laser's own shots (60 damage: 3000). So the
/// beam is the comfortable way to fight, never the strong one: about a fifth of the damage per energy, and 55 damage a
/// second where a quick finger does far more with single shots. What it buys is ease — nothing to click fast, it
/// follows the look, it sweeps from one machine to the next (a Guardian robot falls in three ticks, a hunter in five).</para>
/// <para><b>Kid-friendly.</b> The beam cannot strand anybody: it stops at <see cref="ReserveFraction"/> of the suit's
/// energy and says so, which leaves the jetpack and a handful of single shots. It never hurts what a shot may not
/// hurt (pets, the Qualis, friends where the world's PvP rules say no), because every tick goes down the shot's own
/// path on the server.</para>
/// </summary>
public static class BeamWeaponRules
{
    /// <summary>The share of the suit's MAXIMUM energy the beam leaves untouched ("so you are never stranded").</summary>
    public const float ReserveFraction = 0.15f;

    /// <summary>A press shorter than this is only the shot; holding past it starts the beam (client side).</summary>
    public const float HoldSeconds = 0.22f;

    /// <summary>Seconds between two beam hits when the item names none.</summary>
    public const float DefaultTick = 0.25f;

    /// <summary>The fastest tick the data may ask for — nothing resolves hits faster than this.</summary>
    public const float MinTick = 0.1f;

    /// <summary>The server drops a beam it has not heard a tick of for this long (a lost release, a closed game).</summary>
    public const float TimeoutSeconds = 0.6f;

    /// <summary>How much earlier than a full tick the server still resolves the next one. The client paces its ticks
    /// exactly, but the network can deliver two closer together than they were sent; the server's schedule moves on by
    /// whole ticks, so being early never adds up to more hits per second.</summary>
    public const float JitterToleranceSeconds = 0.06f;

    /// <summary>Seconds between two "the beam stops at the reserve" lines for one player.</summary>
    public const float ReserveLineSeconds = 6f;

    /// <summary>The sound the beam makes while it is held (a loop) and when it ends. A missing clip is silence.</summary>
    public const string LoopCue = "guardian_beam_loop";
    public const string EndCue = "guardian_beam_end";

    /// <summary>True for a weapon that fires a beam while the control is held.</summary>
    public static bool IsBeamWeapon(ToolProperties? tool)
        => tool is { Kind: ToolKind.Weapon } && tool.BeamDamagePerSecond > 0f && tool.BeamEnergyPerSecond > 0f;

    /// <summary>Seconds between two resolved hits of this weapon's beam.</summary>
    public static float Tick(ToolProperties tool)
        => tool.BeamTick > 0f ? Math.Max(MinTick, tool.BeamTick) : DefaultTick;

    /// <summary>The beam's reach in blocks (its own; the shot's when the item names none).</summary>
    public static float Range(ToolProperties tool) => tool.BeamRange > 0f ? tool.BeamRange : tool.Range;

    /// <summary>Damage of one tick.</summary>
    public static float DamagePerTick(ToolProperties tool) => tool.BeamDamagePerSecond * Tick(tool);

    /// <summary>Suit energy of one tick.</summary>
    public static float EnergyPerTick(ToolProperties tool) => tool.BeamEnergyPerSecond * Tick(tool);

    /// <summary>The energy the beam never touches, for a suit that holds <paramref name="maxSuitEnergy"/>.</summary>
    public static float Reserve(float maxSuitEnergy) => Math.Max(0f, maxSuitEnergy) * ReserveFraction;

    /// <summary>Whether the suit can pay one more tick without going below the reserve.</summary>
    public static bool CanPayTick(ToolProperties tool, float suitEnergy, float maxSuitEnergy)
        => suitEnergy - EnergyPerTick(tool) >= Reserve(maxSuitEnergy);

    /// <summary>How many ticks a suit at <paramref name="suitEnergy"/> pays before the beam stops at the reserve.</summary>
    public static int TicksUntilReserve(ToolProperties tool, float suitEnergy, float maxSuitEnergy)
    {
        float perTick = EnergyPerTick(tool);
        float spare = suitEnergy - Reserve(maxSuitEnergy);
        return perTick <= 0f || spare < perTick ? 0 : (int)Math.Floor((spare / perTick) + 1e-4f);
    }

    /// <summary>The tool values of ONE beam tick, as a weapon the shot's own path can resolve: the tick's damage as the
    /// shot's damage, its energy as the cost per use, the beam's reach as the range, and no cooldown (the cadence is
    /// the beam's). Ignition, tier, kind and look are the weapon's; the beam neither pushes nor dazes.</summary>
    public static ToolProperties TickProfile(ToolProperties tool) => new()
    {
        Kind = tool.Kind,
        Tier = tool.Tier,
        Ignites = tool.Ignites,
        Fx = tool.Fx,
        Damage = DamagePerTick(tool),
        EnergyPerUse = EnergyPerTick(tool),
        Range = Range(tool),
        BeamDamagePerSecond = tool.BeamDamagePerSecond,
        BeamEnergyPerSecond = tool.BeamEnergyPerSecond,
        BeamRange = tool.BeamRange,
        BeamTick = tool.BeamTick,
    };

    /// <summary>Content check: an item that names any beam value must be a ranged weapon that names damage, energy and
    /// a reach inside the shot's own, with a tick the server can keep.</summary>
    public static void Validate(ItemDefinition item, List<string> problems)
    {
        if (item.Tool is not { } tool
            || (tool.BeamDamagePerSecond == 0f && tool.BeamEnergyPerSecond == 0f && tool.BeamRange == 0f && tool.BeamTick == 0f))
        {
            return;
        }

        if (tool.Kind != ToolKind.Weapon || !MeleeRules.IsRanged(tool))
        {
            problems.Add($"Item '{item.Key}' has beam values but is not a ranged weapon.");
        }

        if (tool.BeamDamagePerSecond <= 0f || tool.BeamEnergyPerSecond <= 0f)
        {
            problems.Add($"Item '{item.Key}': a held beam needs beamDamagePerSecond and beamEnergyPerSecond above 0.");
        }

        if (tool.BeamRange <= MeleeRules.MeleeReach || tool.BeamRange > tool.Range)
        {
            problems.Add($"Item '{item.Key}': beamRange must be longer than the melee reach and no longer than the shot's range.");
        }

        if (tool.BeamTick != 0f && (tool.BeamTick < MinTick || tool.BeamTick > TimeoutSeconds * 0.5f))
        {
            problems.Add($"Item '{item.Key}': beamTick must lie between {MinTick} and {TimeoutSeconds * 0.5f} seconds.");
        }
    }
}
