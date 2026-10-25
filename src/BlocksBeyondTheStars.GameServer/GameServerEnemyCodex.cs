// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The hostiles from <c>data/enemies.json</c> and the Codex's "Enemies" chapter (#2971, Justus: "a tablet with the enemies —
/// damage, health, abilities — with names"). Every drone, UFO, cruiser, raider ship, Guardian machine and robber is built
/// by <see cref="NewEnemy"/> from its tier, so the numbers the Codex shows are the numbers the fight uses. A player MEETS an
/// enemy when it comes within sight in a fight — within a ranged weapon's reach on the ground, within a ship weapon's
/// reach in space — which also covers every hit either side lands. The first meeting fills the Codex entry
/// (<see cref="Shared.State.PlayerState.EnemiesMet"/>, persisted); until then the Codex shows "???".
/// </summary>
public sealed partial class GameServer
{
    /// <summary>On the ground: the longest hand-weapon reach (32), so a shot from the farthest range already counts.</summary>
    private const float EnemyMeetRangeGround = 32f;

    /// <summary>In space: past a hostile's engage range (70) and a ship weapon's reach.</summary>
    private const float EnemyMeetRangeSpace = 160f;

    private double _enemyMeetTimer;

    /// <summary>Decided once per tick by <see cref="TickSpaceEnemyEncounters"/> (before the worlds): the per-world ground
    /// pass runs on the same 1 Hz beat in every occupied world.</summary>
    private bool _enemyMeetDue;

    /// <summary>A hostile of the given enemy and tier, hostile from the start, at <paramref name="at"/> — the caller adds
    /// what only it knows (a name, a robber's phase, a camp).</summary>
    private CombatEntity NewEnemy(string enemyKey, string tierName, CombatEntityKind kind, Vector3f at)
    {
        var tier = _content.Enemy(enemyKey).Tier(tierName);
        var entity = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = kind,
            EnemyKey = enemyKey,
            Hostile = true,
            Hull = tier.Hull,
            HullMax = tier.Hull,
            Position = at,
            DamagePerSecond = tier.DamagePerSecond,
        };
        foreach (var loot in tier.Loot)
        {
            entity.Loot.Add(new ItemAmount(loot.Item, loot.Count));
        }

        return entity;
    }

    /// <summary>A robber on foot — the blade or the gun — of the given tier (a camp guard carries more).</summary>
    private CombatEntity NewBandit(bool gunner, string tierName, Vector3f at)
        => NewEnemy(gunner ? EnemyCatalog.BanditGunner : EnemyCatalog.Bandit, tierName,
            gunner ? CombatEntityKind.BanditGunner : CombatEntityKind.Bandit, at);

    /// <summary>1 Hz, per occupied world: every joined player on this surface meets the Guardian machines and robbers in
    /// range.</summary>
    private void TickEnemyEncounters()
    {
        if (!_enemyMeetDue)
        {
            return;
        }

        const float rangeSq = EnemyMeetRangeGround * EnemyMeetRangeGround;
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Spectating || InSpace(s.State.PlayerId))
            {
                continue;
            }

            foreach (var e in _planetEnemies)
            {
                if (WrapDistSq(s.State.Position, e.Position) <= rangeSq)
                {
                    MeetEnemy(s, e);
                }
            }

            foreach (var e in _bandits)
            {
                if (WrapDistSq(s.State.Position, e.Position) <= rangeSq)
                {
                    MeetEnemy(s, e);
                }
            }
        }
    }

    /// <summary>1 Hz, before the worlds: decides the beat for the ground pass, then each pilot (or spacewalker) in every
    /// space instance meets the hostiles near them.</summary>
    private void TickSpaceEnemyEncounters(double dt)
    {
        _enemyMeetTimer += dt;
        _enemyMeetDue = _enemyMeetTimer >= 1.0;
        if (!_enemyMeetDue)
        {
            return;
        }

        _enemyMeetTimer = 0;
        const float rangeSq = EnemyMeetRangeSpace * EnemyMeetRangeSpace;
        foreach (var instance in _spaceInstances.Values)
        {
            foreach (var playerId in instance.Players)
            {
                if (FindSessionByPlayerId(playerId) is not { Spectating: false } session)
                {
                    continue;
                }

                var at = PilotPositionIn(instance, playerId);
                foreach (var e in instance.Entities)
                {
                    if (e.EnemyKey.Length > 0 && e.Position.DistanceSquared(at) <= rangeSq)
                    {
                        MeetEnemy(session, e);
                    }
                }
            }
        }
    }

    /// <summary>The player's first meeting with this kind of enemy fills its Codex entry: remembered, pushed to the
    /// client as a one-entry delta.</summary>
    private void MeetEnemy(PlayerSession session, CombatEntity enemy)
    {
        if (enemy.EnemyKey.Length == 0 || !session.State.EnemiesMet.Add(enemy.EnemyKey))
        {
            return;
        }

        Send(session, new EnemyLog { Keys = new[] { enemy.EnemyKey }, Full = false });
    }

    /// <summary>The whole list of met enemies — the Codex snapshot on join (sent with the Discoveries ledger).</summary>
    private void SendEnemyLog(PlayerSession session)
    {
        var keys = new string[session.State.EnemiesMet.Count];
        session.State.EnemiesMet.CopyTo(keys);
        Send(session, new EnemyLog { Keys = keys, Full = true });
    }

    /// <summary>Test seam: runs both encounter passes now.</summary>
    public void TickEnemyEncountersForTest()
    {
        TickSpaceEnemyEncounters(1.0);
        TickEnemyEncounters();
    }
}

/// <summary>#2971: the enemy kind a hostile was built as (<see cref="Shared.Definitions.EnemyCatalog"/>) — what the Codex
/// entry it fills is called. Empty for everything that is not an enemy (asteroids, creatures, wrecks …).</summary>
public sealed partial class CombatEntity
{
    public string EnemyKey { get; set; } = string.Empty;
}
