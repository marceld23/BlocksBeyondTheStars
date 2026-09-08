// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Lava and fire burn everybody, not just the player (#1700).
///
/// <para>A player building a fire moat around her base put it plainly: "everything that steps into the lava
/// ought to take damage". It did not. Contact damage lived in the player tick alone — <c>InLava</c> had
/// exactly one call site — so animals, robbers and Guardian machines waded through the melt untouched while
/// the player two blocks away burned at 15 health a second. A moat that hurts one side of a fight and not
/// the other is not a moat.</para>
///
/// <para><b>What still protects an animal.</b> The terrain gate (#1367) keeps a ground-bound creature OUT of
/// a lava column in the first place, so a fire trench goes on working mainly as a wall; this is what happens
/// to the ones the wall cannot stop — fliers and hoverers, anything that was standing in the trench when it
/// was flooded, and anything driven in. Lava fauna lives in the melt and is exempt, a tamed companion never
/// burns (it follows its owner everywhere and would die for it), and on a Creative world or one with
/// environmental hazards switched off nothing burns at all — the same switch that spares the player.</para>
///
/// <para><b>No credit.</b> An entity that burns to death drops its loot on the ground and is gone. There is
/// no story progress, no mission tick and no achievement: nobody landed the blow, and a player who funnels
/// the world's machines into a lava pit should get the tactical win, not the narrative one.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Health per second lost standing in lava — the same rate the player takes, before armour.
    /// A 30-hull animal has about two seconds to get out.</summary>
    private const float BurnLavaDps = 15f;

    /// <summary>…and in open fire, which is gentler, exactly as it is for the player.</summary>
    private const float BurnFireDps = 10f;

    /// <summary>Seconds between burn passes (2 Hz). Contact damage does not need per-tick resolution, and
    /// the pass walks every live entity — it stays off the hot path.</summary>
    private const double BurnInterval = 0.5;

    private double _nextBurnAt;
    private double _lastBurnAt;
    private bool _burnPrimed;

    /// <summary>Whether the world burns NPCs at all: the same gate that spares the player on a Creative world
    /// or one with environmental hazards switched off.</summary>
    private bool BurningActive => Rules.TemperatureHazardsEnabled;

    /// <summary>2 Hz: everything standing in lava or fire loses health, and anything that runs out of it dies
    /// where it stands.</summary>
    private void TickBurning()
    {
        if (!BurningActive || _uptime < _nextBurnAt)
        {
            return;
        }

        // Real elapsed time, not the nominal interval: a stalled or slow tick must not silently change the
        // burn rate. The very first pass after a start or a resume burns nothing — it has no interval yet.
        float dt = _burnPrimed ? (float)(_uptime - _lastBurnAt) : 0f;
        _burnPrimed = true;
        _lastBurnAt = _uptime;
        _nextBurnAt = _uptime + BurnInterval;
        if (dt <= 0f)
        {
            return;
        }

        bool creaturesChanged = BurnList(_creatures, dt, creatures: true);
        bool enemiesChanged = BurnList(_planetEnemies, dt, creatures: false);
        enemiesChanged |= BurnList(_bandits, dt, creatures: false);

        if (creaturesChanged)
        {
            BroadcastCreatures();
        }

        if (enemiesChanged)
        {
            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Burns one entity list, removing the dead. Returns true when anything changed, so the caller
    /// broadcasts the list once per pass instead of once per victim.</summary>
    private bool BurnList(List<CombatEntity> list, float dt, bool creatures)
    {
        List<CombatEntity>? dead = null;
        bool changed = false;
        foreach (var e in list)
        {
            float dps = BurnRateFor(e, creatures);
            if (dps <= 0f)
            {
                continue;
            }

            e.Hull -= dps * dt;
            changed = true;
            if (creatures)
            {
                e.AwakeOverrideTimer = CreatureWakeSeconds; // nothing sleeps through burning
            }

            if (e.Hull <= 0f)
            {
                (dead ??= new List<CombatEntity>()).Add(e);
            }
        }

        if (dead is null)
        {
            return changed;
        }

        foreach (var e in dead)
        {
            list.Remove(e);
            _enemyWander.Remove(e.Id);
            SpillToGround(e.Position.ToBlock(), e.Loot, creatureLoot: creatures);
            if (!creatures)
            {
                if (e.IsBandit)
                {
                    OnBanditKilled(e); // camp bookkeeping still has to clear — no story credit, nobody fired
                }

                BroadcastToWorld(new PlanetEnemyDefeated { Id = e.Id });
            }

            _log.Info($"'{e.Name}' ({e.Id}) burned to death at {e.Position.X:0},{e.Position.Y:0},{e.Position.Z:0}.");
        }

        return true;
    }

    /// <summary>Health per second this entity is losing to its surroundings right now, or 0 when it is not
    /// burning or is exempt: a tamed companion (it follows its owner into anything) and lava fauna, whose
    /// whole habitat is the melt.</summary>
    private float BurnRateFor(CombatEntity e, bool creature)
    {
        if (creature && e.IsCompanion)
        {
            return 0f;
        }

        bool lavaDweller = creature
            && e.SpeciesId.Length > 0
            && _speciesById.TryGetValue(e.SpeciesId, out var sp)
            && sp.Habitat == CreatureHabitat.Lava;

        if (!lavaDweller && InLava(e.Position))
        {
            return BurnLavaDps;
        }

        return InFire(e.Position) ? BurnFireDps : 0f;
    }

    /// <summary>Test hook: run a burn pass right now over the given elapsed time, ignoring the 2 Hz gate and
    /// the "first pass burns nothing" priming.</summary>
    public void TickBurningForTest(float dt)
    {
        _nextBurnAt = 0;
        _burnPrimed = true;
        _lastBurnAt = _uptime - dt;
        TickBurning();
    }
}
