// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The caller's side of the creature tick (#2057): a peaceful land animal within range of a pulsed caller block
/// comes over and circles it for a while, then trots off — the begging herd's orbit without the food. Runs right
/// after <c>TryBegIntent</c> and only when that one had nothing to say, so begging species keep their routine.
/// </summary>
public sealed partial class GameServer
{
    private bool TryCallerIntent(CombatEntity c, CreatureSpecies sp, Vector3f? nearestPlayer,
        ref LocomotionProfile profile, ref MoveMode intent, ref Vector3f? target)
    {
        if (c.IsCompanion || c.IsGiant || sp.Hostile || sp.Habitat != CreatureHabitat.Land
            || sp.Temperament is not (CreatureTemperament.Passive or CreatureTemperament.Skittish))
        {
            return false;
        }

        if (c.BegPhase is not (BegPhase.None or BegPhase.Called or BegPhase.Leave))
        {
            return false; // mid-beg (food in a hand): the begging routine owns it
        }

        if (c.FrozenTimer > 0 || c.PanicTimer > 0 || c.ProvokeTimer > 0)
        {
            if (c.BegPhase == BegPhase.Called)
            {
                c.BegPhase = BegPhase.None; // a startled animal forgets the call
            }

            return false;
        }

        if (c.BegPhase == BegPhase.Leave)
        {
            if (HerdRules.BegsForFood(sp))
            {
                return false; // the begging routine walks its own leave
            }

            if (_uptime >= c.BegUntil)
            {
                c.BegPhase = BegPhase.None;
                return false;
            }

            return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
        }

        var at = NearestActiveCaller(c.Position, CrystalNetRules.CallerRange);
        if (c.BegPhase == BegPhase.None)
        {
            if (at is null || _uptime < c.BegCooldownUntil)
            {
                return false;
            }

            c.BegPhase = BegPhase.Called;
            c.BegUntil = _uptime + CrystalNetRules.CallerHoldSeconds;
            return BegOrbit(c, sp, at.Value, ref intent, ref target);
        }

        if (at is null || _uptime >= c.BegUntil)
        {
            BeginLeave(c);
            return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
        }

        return BegOrbit(c, sp, at.Value, ref intent, ref target);
    }
}
