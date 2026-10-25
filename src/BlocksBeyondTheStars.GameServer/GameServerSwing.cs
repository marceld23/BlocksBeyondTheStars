// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// One swing or shot of the held item — the part of an attack that does not depend on what is hit: the rhythm (a weapon's
/// cooldown, the bare hand's slower one), the suit energy a laser or plasma shot costs, and the damage. Shared by the
/// attack on a machine, a robber or an animal (<c>AttackCombatEntity</c>) and the attack on a player (#2972), so both
/// hit exactly alike.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether the held item may strike now — and if so, its cooldown starts. A swing too soon is ignored without
    /// a reject (no toast spam).</summary>
    private bool SwingReady(PlayerSession session, ToolProperties tool, bool isWeapon)
    {
        var p = session.State;
        if (isWeapon)
        {
            // A weapon swings on a cooldown, so it can't be spammed (B44). The per-weapon cooldown comes from the
            // item (machete = 1.5s); an energy-free melee weapon with no explicit cooldown falls back to the default.
            // Ranged energy weapons without a cooldown are still rate-limited by their suit-energy cost.
            double cd = (tool.CooldownSeconds > 0f ? tool.CooldownSeconds : (tool.EnergyPerUse <= 0f ? MeleeCooldown : 0.0))
                * Shared.Bio.PlayerEffects.CooldownFactor(p.Effects); // #2202: a reflex preparation shortens it
            if (cd > 0.0)
            {
                if (_meleeReadyAt.TryGetValue(p.PlayerId, out var readyAt) && _uptime < readyAt)
                {
                    return false; // still on cooldown — ignore the swing (no reject spam)
                }

                _meleeReadyAt[p.PlayerId] = _uptime + cd;
            }

            return true;
        }

        // #2280: the bare hand has its own, slower rhythm — it used to punch as fast as the player could click.
        // Its own entry, so switching to a weapon right after a punch is not held back by it (and vice versa).
        // A punch up to MeleeRules.FistJitterToleranceSeconds early still lands: the client gates on the exact
        // cooldown, so an early arrival is network jitter, not a faster fist. #2306: a drill, a scanner or a
        // gadget is a punch too (MeleeRules.IsPunch) and shares this timer — tools dig, weapons fight.
        double fistCd = MeleeRules.FistCooldownSeconds * Shared.Bio.PlayerEffects.CooldownFactor(p.Effects);
        if (_fistReadyAt.TryGetValue(p.PlayerId, out var fistReadyAt) && _uptime < fistReadyAt - MeleeRules.FistJitterToleranceSeconds)
        {
            return false; // too soon — ignore the punch (no reject spam), like a weapon on cooldown
        }

        _fistReadyAt[p.PlayerId] = _uptime + fistCd;
        return true;
    }

    /// <summary>Energy weapons (laser/plasma) draw suit energy per shot; false (with a reject) when the suit is too empty.</summary>
    private bool PayShot(PlayerSession session, ToolProperties tool, bool isWeapon)
    {
        var p = session.State;
        if (!isWeapon || tool.EnergyPerUse <= 0f)
        {
            return true;
        }

        if (p.SuitEnergy < tool.EnergyPerUse)
        {
            Reject(session, "attack", "@srv.attack.no_energy");
            return false;
        }

        p.SuitEnergy -= tool.EnergyPerUse;
        SendPlayerState(session);
        return true;
    }

    /// <summary>A crafted weapon uses its own damage; everything else is a punch at the fist's damage (#2280, #2306,
    /// MeleeRules) — the bare hand and every tool that is not a weapon alike. The old 15 + tier · 10 fallback made the
    /// titanium drill a 35-damage, uncooled, energy-free weapon; it costs no suit energy either way. #2202: strength is in
    /// the arm, not in a gun.</summary>
    private static float SwingDamage(PlayerState p, ToolProperties tool, bool isWeapon)
    {
        float damage = isWeapon
            ? (tool.Damage > 0f ? tool.Damage : 20f + tool.Tier * 15f)
            : MeleeRules.FistDamage;
        return MeleeRules.IsRanged(tool) ? damage : damage * Shared.Bio.PlayerEffects.MeleeFactor(p.Effects);
    }
}
