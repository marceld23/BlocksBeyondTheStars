// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3365: a wild animal's duel and pack-defence state on its live entity (server-only, never persisted).</summary>
public sealed partial class CombatEntity
{
    /// <summary>The player this animal is in a duel with (empty = none).</summary>
    public string DuelWithId { get; set; } = string.Empty;

    /// <summary>The species name the duel lines carry (kept from the start, so an end on another world still names it).</summary>
    public string DuelName { get; set; } = string.Empty;

    /// <summary>The only player this animal fights right now — its duel partner, or the attacker its pack defends against
    /// (empty = none: it fights whom its temperament says).</summary>
    public string FightTargetId { get; set; } = string.Empty;

    /// <summary>Its own bite before the fight (a duel or the pack defence sets a gentle one), given back when it calms down.</summary>
    public float FightBiteBefore { get; set; }

    /// <summary>Uptime when a defending pack member calms down again.</summary>
    public double DefendUntil { get; set; }
}

/// <summary>
/// Duel taming and pack defence (#3365, the wild Soundis on Soaniri; species flags <see cref="CreatureSpecies.DuelTame"/>
/// and <see cref="CreatureSpecies.PackDefender"/>, rules in <see cref="CreatureBehaviour"/>):
/// <list type="bullet">
/// <item><b>Duel taming:</b> the creature translator (the taming entry) on such an animal starts a <b>duel</b> instead of
/// the ritual. Only that animal fights, and only the duelling player (<see cref="CombatEntity.FightTargetId"/>: it hunts
/// and bites nobody else; nobody else's turret or robot shoots it). Its health never drops below
/// <see cref="CreatureBehaviour.DuelSurrenderShare"/>: there it <b>gives up</b> — it never dies — and
/// <c>CompleteTame</c> makes it a planet pet. The duel ends when the player walks
/// <see cref="CreatureBehaviour.DuelWalkAwayRange"/> blocks away, leaves the world, or dies and respawns; the animal is
/// whole again and can be challenged any time.</item>
/// <item><b>Pack defence:</b> hitting such an animal outside a duel turns every animal of its species within
/// <see cref="CreatureBehaviour.PackDefenceRange"/> on that one attacker for <see cref="CreatureBehaviour.PackDefenceSeconds"/>
/// — whatever its temperament, with a gentle bite — then they calm down. A duel animal never dies at a player's hand.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How a duel ended — which line the player hears.</summary>
    private enum DuelEnd
    {
        Quiet,      // the player left the server, or started another duel
        WalkedAway, // walked away or left the world
        Lost,       // died — try again any time
    }

    /// <summary>The pack's "they defend it!" line reaches an attacker at most this often.</summary>
    private const double PackDefenceLineCooldown = 10.0;

    /// <summary>Per attacker: the uptime of the last pack-defence line.</summary>
    private readonly Dictionary<string, double> _packDefenceToldAt = new(StringComparer.Ordinal);

    /// <summary>Whether a creature may fight (hunt, bite) a given player: anyone, unless a duel or its pack's defence locked
    /// it on one player.</summary>
    private static bool MayFight(CombatEntity creature, string playerId)
        => creature.FightTargetId.Length == 0 || creature.FightTargetId == playerId;

    // ---------------- Duel ----------------

    /// <summary>The translator on a duel-tamed animal: the duel starts — the animal is whole, awake and fights this player
    /// alone. Refused while it duels somebody else; a player's earlier duel ends quietly.</summary>
    private void StartCreatureDuel(PlayerSession session, CombatEntity creature, CreatureSpecies sp)
    {
        var p = session.State;
        _tameAttempts.Remove(p.PlayerId);
        Send(session, new TameProgress { Active = false }); // no ritual panel for a duel
        string name = string.IsNullOrEmpty(sp.Name) ? creature.Id : sp.Name;
        if (creature.DuelWithId.Length > 0)
        {
            Send(session, new ServerMessage
            {
                Text = (creature.DuelWithId == p.PlayerId ? "@srv.creature_duel.start:" : "@srv.creature_duel.busy:") + name,
            });
            return;
        }

        EndCreatureDuelOf(p.PlayerId, DuelEnd.Quiet);
        CalmPackAgainst(p.PlayerId); // a fair one-on-one: the rest of the pack lets go
        BeginFight(creature, sp, p.PlayerId);
        creature.DuelWithId = p.PlayerId;
        creature.DuelName = name;
        creature.Hull = creature.HullMax; // a fair duel starts fresh
        creature.FrozenTimer = 0;
        creature.DefendUntil = 0;
        Send(session, new ServerMessage { Text = "@srv.creature_duel.start:" + name });
        BroadcastCreatures();
    }

    /// <summary>Locks an animal on one player: awake, provoked, hunting and biting that player only, with the gentle fight
    /// bite (its own bite is kept for later).</summary>
    private void BeginFight(CombatEntity c, CreatureSpecies sp, string playerId)
    {
        if (c.FightTargetId.Length == 0)
        {
            c.FightBiteBefore = c.DamagePerSecond;
        }

        c.FightTargetId = playerId;
        c.DamagePerSecond = CreatureBehaviour.DuelBite(sp.AttackDamage);
        c.ProvokeTimer = Math.Max(c.ProvokeTimer, CreatureProvokeSeconds);
        c.AwakeOverrideTimer = Math.Max(c.AwakeOverrideTimer, CreatureWakeSeconds);
        c.PanicTimer = 0;
        c.GiveUpTimer = 0;
        c.ChaseTimer = 0;
    }

    /// <summary>A defending or duelling animal lets go: its own bite back, no target, no anger.</summary>
    private static void CalmFighter(CombatEntity c)
    {
        if (c.FightTargetId.Length > 0)
        {
            c.DamagePerSecond = c.FightBiteBefore;
        }

        c.FightTargetId = string.Empty;
        c.DefendUntil = 0;
        c.ProvokeTimer = 0;
        c.ChaseTimer = 0;
    }

    /// <summary>The player's hit landed on a duel-tamed or pack-defending animal (after the damage). Returns true when the
    /// duel took the hit over — the caller's ordinary path (startle, provoke, push, death) is skipped.</summary>
    private bool OnCreatureDuelHit(PlayerSession session, CombatEntity target)
    {
        if (target.IsGiant || !_speciesById.TryGetValue(target.SpeciesId, out var sp))
        {
            return false;
        }

        float floor = CreatureBehaviour.DuelSurrenderHull(target.HullMax);
        if (target.DuelWithId.Length > 0)
        {
            DuelHit(session, target, sp, floor);
            return true;
        }

        if (CreatureBehaviour.DefendsKin(sp))
        {
            DefendPack(target, sp, session);
        }

        if (CreatureBehaviour.TamesByDuel(sp) && target.Hull < floor)
        {
            target.Hull = floor; // kid-friendly: a duel animal never dies at a player's hand — it is won in a duel
        }

        return false;
    }

    /// <summary>A hit inside a duel: the partner's brings the animal toward giving up; anybody else's is no part of it.</summary>
    private void DuelHit(PlayerSession session, CombatEntity target, CreatureSpecies sp, float floor)
    {
        if (target.DuelWithId != session.State.PlayerId)
        {
            target.Hull = Math.Max(target.Hull, floor);
            Send(session, new ServerMessage { Text = "@srv.creature_duel.busy:" + target.DuelName });
            BroadcastCreatures();
            return;
        }

        target.AwakeOverrideTimer = Math.Max(target.AwakeOverrideTimer, CreatureWakeSeconds);
        if (target.Hull > floor)
        {
            BroadcastCreatures();
            return;
        }

        target.Hull = floor;
        SurrenderDuel(session, target, sp);
    }

    /// <summary>The animal gives up — it never dies — and becomes the player's planet pet through the ordinary tame (the
    /// six-pet cap, the first-tame knowledge, the name prompt).</summary>
    private void SurrenderDuel(PlayerSession session, CombatEntity creature, CreatureSpecies sp)
    {
        string name = creature.DuelName;
        creature.DuelWithId = string.Empty;
        CalmFighter(creature);
        creature.FrozenTimer = Math.Max(creature.FrozenTimer, CompanionHoldSeconds); // it sits down, out of breath
        Send(session, new ServerMessage { Text = "@srv.creature_duel.won:" + name });
        int required = RequiredTrust(sp, creature.Id, creature.SizeScale);
        var attempt = new TameAttempt { CreatureId = creature.Id, Required = required, Trust = required };
        _tameAttempts[session.State.PlayerId] = attempt;
        CompleteTame(session, attempt, creature, sp, "creature.tame.msg.duel");
        BroadcastCreatures();
    }

    /// <summary>Ends the duel a player is in, wherever its animal is (a death and respawn may come from any world).</summary>
    private void EndCreatureDuelOf(string playerId, DuelEnd how)
    {
        foreach (var world in _worlds.Loaded)
        {
            foreach (var c in world.Creatures)
            {
                if (c.DuelWithId == playerId)
                {
                    EndDuel(c, how, FindSessionByPlayerId(playerId));
                }
            }
        }
    }

    /// <summary>A duel ends without a winner: the animal calms down and is whole again; the player hears why.</summary>
    private void EndDuel(CombatEntity c, DuelEnd how, PlayerSession? session)
    {
        c.DuelWithId = string.Empty;
        CalmFighter(c);
        c.Hull = c.HullMax;
        c.GiveUpTimer = 0;
        if (session is not null && how != DuelEnd.Quiet)
        {
            string line = how == DuelEnd.Lost ? "@srv.creature_duel.lost:" : "@srv.creature_duel.walked_away:";
            Send(session, new ServerMessage { Text = line + c.DuelName });
        }
    }

    // ---------------- Pack defence ----------------

    /// <summary>A member of a defending pack was attacked outside a duel: it and every animal of its species within
    /// <see cref="CreatureBehaviour.PackDefenceRange"/> (not one in a duel) turn on the attacker for a while. The attack path
    /// broadcasts the creatures right after.</summary>
    private void DefendPack(CombatEntity target, CreatureSpecies sp, PlayerSession attacker)
    {
        string attackerId = attacker.State.PlayerId;
        float r2 = CreatureBehaviour.PackDefenceRange * CreatureBehaviour.PackDefenceRange;
        bool rallied = false;
        foreach (var other in _creatures)
        {
            if (other.IsCompanion || other.IsGiant || other.SpeciesId != target.SpeciesId || other.DuelWithId.Length > 0
                || (!ReferenceEquals(other, target) && WrapDistSq(other.Position, target.Position) > r2))
            {
                continue;
            }

            BeginFight(other, sp, attackerId);
            other.ProvokeTimer = Math.Max(other.ProvokeTimer, CreatureBehaviour.PackDefenceSeconds);
            other.AwakeOverrideTimer = Math.Max(other.AwakeOverrideTimer, CreatureBehaviour.PackDefenceSeconds);
            other.DefendUntil = _uptime + CreatureBehaviour.PackDefenceSeconds;
            rallied |= !ReferenceEquals(other, target);
        }

        if (rallied && (!_packDefenceToldAt.TryGetValue(attackerId, out double told) || _uptime - told >= PackDefenceLineCooldown))
        {
            _packDefenceToldAt[attackerId] = _uptime;
            Send(attacker, new ServerMessage { Text = "@srv.creature_duel.pack:" + sp.Name });
        }
    }

    /// <summary>Every pack member on this world that defends against <paramref name="playerId"/> calms down.</summary>
    private void CalmPackAgainst(string playerId)
    {
        foreach (var c in _creatures)
        {
            if (c.FightTargetId == playerId && c.DuelWithId.Length == 0)
            {
                CalmFighter(c);
            }
        }
    }

    // ---------------- The creature tick's part ----------------

    /// <summary>Called by the creature movement after the hunt range is known: an animal locked on one player (duel, pack
    /// defence) hunts that player — whatever its temperament, from further away in a duel — and nobody else; a duelling
    /// animal never tires of the chase.</summary>
    private void ApplyFightLock(CombatEntity creature, List<PlayerSession> targets, ref CreatureTemperament temperament,
        ref bool aggressor, ref Vector3f? prey, ref float aggro)
    {
        if (creature.FightTargetId.Length == 0)
        {
            return;
        }

        prey = null;
        foreach (var s in targets)
        {
            if (s.State.PlayerId == creature.FightTargetId && !HostilesIgnore(s.State))
            {
                prey = s.State.Position;
                break;
            }
        }

        temperament = CreatureTemperament.Aggressive;
        aggressor = true;
        bool duel = creature.DuelWithId.Length > 0;
        aggro = Math.Max(aggro, duel ? CreatureBehaviour.DuelWalkAwayRange : CreatureBehaviour.PackDefenceRange + 4f);
        if (duel)
        {
            creature.ChaseTimer = 0;
            creature.GiveUpTimer = 0;
        }
    }

    /// <summary>Every world tick: a duel goes on while its player is here, alive and near (the animal stays angry and
    /// awake), or ends; a defending pack member calms down when its time is up.</summary>
    private void TickCreatureFights()
    {
        bool changed = false;
        foreach (var c in _creatures)
        {
            if (c.DuelWithId.Length > 0)
            {
                changed |= TickDuel(c);
            }
            else if (c.FightTargetId.Length > 0 && _uptime >= c.DefendUntil)
            {
                CalmFighter(c);
                changed = true;
            }
        }

        if (changed)
        {
            BroadcastCreatures();
        }
    }

    /// <summary>One duel's tick; true when it ended.</summary>
    private bool TickDuel(CombatEntity c)
    {
        var session = FindSessionByPlayerId(c.DuelWithId);
        DuelEnd? end = session is null ? DuelEnd.Quiet
            : session.CurrentLocationId != _world.LocationId || InSpace(c.DuelWithId) ? DuelEnd.WalkedAway
            : session.Respawn.ChoicePending || session.State.Health <= 0f ? DuelEnd.Lost
            : WrapDistSq(session.State.Position, c.Position) > CreatureBehaviour.DuelWalkAwayRange * CreatureBehaviour.DuelWalkAwayRange
                ? DuelEnd.WalkedAway
                : null;
        if (end is { } how)
        {
            EndDuel(c, how, session);
            return true;
        }

        c.ProvokeTimer = Math.Max(c.ProvokeTimer, 2.0);
        c.AwakeOverrideTimer = Math.Max(c.AwakeOverrideTimer, 2.0);
        c.PanicTimer = 0;
        return false;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the player the creature duels (empty = none).</summary>
    public string CreatureDuelPartnerForTest(string creatureId)
        => _creatures.Find(c => c.Id == creatureId)?.DuelWithId ?? string.Empty;

    /// <summary>Test seam: the only player the creature fights right now (empty = none).</summary>
    public string CreatureFightTargetForTest(string creatureId)
        => _creatures.Find(c => c.Id == creatureId)?.FightTargetId ?? string.Empty;

    /// <summary>Test seam: whether the creature may hunt and bite the player (the duel and pack-defence lock).</summary>
    public bool CreatureMayFightForTest(string creatureId, string playerId)
        => _creatures.Find(c => c.Id == creatureId) is { } c && MayFight(c, playerId);

    /// <summary>Test seam: the player's hit on a creature as it reaches the duel (after the damage), without the swing's
    /// cooldown, reach and aim — returns whether the duel took the hit over.</summary>
    public bool DuelHitForTest(string playerId, string creatureId, float damage)
    {
        if (FindSessionByPlayerId(playerId) is not { } s || _creatures.Find(c => c.Id == creatureId) is not { } c)
        {
            return false;
        }

        c.Hull -= damage;
        return OnCreatureDuelHit(s, c);
    }
}
