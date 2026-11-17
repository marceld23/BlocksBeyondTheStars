// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What happens once a player's blow has landed on a machine, a robber or an animal — the part of the attack after
/// every check (reach, aim, sightline, cooldown, energy). Split out of <c>AttackCombatEntity</c> so that a thrown block
/// (#3766, <c>GameServerCarryThrow</c>) is booked by the very same rules as a swing or a shot: the fight clock, the
/// special bodies (a guardian giant, the shapeshifter, a duel), the startled kin, a survivor's answer, and — at zero —
/// the loot, the sample, the achievements, the events and the story.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Books <paramref name="damage"/> on <paramref name="target"/> (already checked: it may be hit, and the
    /// blow reached it). <paramref name="knockback"/> is the weapon whose push applies to a survivor (the shock gloves),
    /// null for a hand or a thrown block.</summary>
    private void LandHit(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature, float damage, ToolProperties? knockback, Vector3f aimDir)
    {
        target.Hull -= damage;
        NoteCombat(session); // #2286: a blow landed — "Back to my ship" waits out the fight
        if (!isCreature && OnGuardianGiantDamaged(target, session.State.PlayerId))
        {
            return; // #3676: no push, no loot here — a guardian giant collapses at zero by its own rules
        }

        // 2026-09 (Valuma): a hit turns the shapeshifter on its attacker; at zero its disguise breaks instead of it dying.
        if (isCreature && IsSreekmakra(target) && OnSreekmakraHit(session, target))
        {
            return;
        }

        // #3365: a hit inside a duel (a wild Soundi gives up instead of dying), or one that makes a pack defend its member.
        if (isCreature && OnCreatureDuelHit(session, target))
        {
            return;
        }

        if (isCreature)
        {
            // Any hit — surviving or fatal — startles the victim's nearby kin (#653): non-retaliating
            // herd members scatter instead of grazing on beside the corpse; retaliators charge instead.
            StartleKin(target);
        }

        if (target.Hull > 0f)
        {
            HitSurvived(session, target, isCreature, knockback, aimDir);
            return;
        }

        HitDefeated(session, target, list, isCreature);
    }

    /// <summary>The target lives on: it answers the hit by its own rules.</summary>
    private void HitSurvived(PlayerSession session, CombatEntity target, bool isCreature, ToolProperties? knockback, Vector3f aimDir)
    {
        if (isCreature && target.IsGiant)
        {
            OnGiantHit(target); // #1998: a giant answers a hit by its own rules
            BroadcastCreatures();
            return;
        }

        // A surviving creature that retaliates (territorial / already hostile) is provoked:
        // for a while it hunts and bites back (and a pack-hunter rallies nearby kin).
        if (isCreature)
        {
            target.AwakeOverrideTimer = CreatureWakeSeconds; // a hit jolts any sleeping creature awake (then it acts per temperament)
            ProvokeCreature(target);
        }
        else if (target.IsBandit)
        {
            OnBanditAttacked(session, target); // attacking a robber counts as refusing its hold-up
        }

        // #2278: the shock gloves push the survivor away and daze it (never a player, a pet or a giant).
        if (knockback is not null)
        {
            ApplyKnockback(session, target, knockback, isCreature, aimDir);
        }

        if (isCreature) BroadcastCreatures(); else BroadcastPlanetEnemies();
        return;
    }

    /// <summary>The target is beaten: it leaves the world, and the player gets what a win brings.</summary>
    private void HitDefeated(PlayerSession session, CombatEntity target, List<CombatEntity> list, bool isCreature)
    {
        list.Remove(target);
        _enemyWander.Remove(target.Id); // drop the dead enemy's wander state
        ForgetWadePlan(target.Id);
        var pool = new MaterialPool(_content, session.State, _ship);
        // The kill already happened, so the loot cannot be refused — a full inventory drops it at the fallen
        // enemy's feet instead of losing it (#853).
        BankLoot(session, pool, target.Loot, spillAt: target.Position.ToBlock(), creatureLoot: isCreature);
        if (isCreature)
        {
            BioOnCreatureDefeated(session, target); // #2201: a sample of its species, with the loot
            ForgetClone(target); // #2214: a defeated clone does not come back beside its tank
        }

        SendInventory(session);
        OnAchievementDefeat(session);
        RaiseGameEvent(session, GameEventKeys.DefeatAny, // #2465
            isCreature ? (target.IsGiant ? GameEventKeys.DefeatGiant : GameEventKeys.DefeatCreature)
            : target.IsBandit ? GameEventKeys.DefeatBandit : GameEventKeys.DefeatMachine);
        if (isCreature)
        {
            OnCreatureKilled(target, session); // 2026-09: the shapeshifter's death, or one of its shape's kind
            if (target.IsGiant)
            {
                OnGiantDefeated(target, session); // #1998: the return clock, the achievement, the witnesses
            }

            BroadcastCreatures();
        }
        else
        {
            BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });
            if (target.IsBandit)
            {
                OnBanditKilled(target, session); // bandits are people, not machines — no story credit, but camps clear (#2599: a deed)
                OnMissionDefeat(session, DefeatTargetBandit); // #730: bounty objectives count the drive-off
                OnScoutDefeated(session, target); // #1224: a beaten base scout credits the homestead bounty
            }
            else
            {
                RecordStoryMachineKill(); // planet machine destroyed → advances the story (P4: combat-as-progress)
                OnMachineDefeated(session); // #1213: post-win only — the survey orders' Defeat step

                TryDropPlayerMemory(session); // a chance to release a personal memory (P4)
                // #740: a destroyed machine buys an extra breather on top of the refill interval — the
                // negative timer delays the freed slot, so a fight is followed by quiet, not reinforcements.
                _enemySpawnTimer = System.Math.Min(_enemySpawnTimer, -EnemyKillSpawnGrace);
            }

            BroadcastPlanetEnemies();
        }
    }
}
