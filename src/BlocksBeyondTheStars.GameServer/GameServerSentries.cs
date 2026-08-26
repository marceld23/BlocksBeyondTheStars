// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The base sentry post (#1214). Before this, a base was scenery: nothing ever approached it and it never
/// did anything back. A <c>sentry_post</c> block inside a founded base zone shoots at hostile machines and
/// at bandits who have already chosen to fight — a small, quiet answer to "my base just stands there".
///
/// <para><b>Deliberately cheap.</b> There is no power system (rejected in #1101), no ammunition and no
/// per-sentry persisted state. The sentry cells are re-derived from the base-zone scan that
/// <see cref="CountBaseMachines"/> already walks, cached per base, and only for bases whose owner is
/// actually joined and standing on that body — enemies spawn 35–50 blocks from a player, so a sentry has
/// nothing to shoot at while nobody is home anyway. The firing pass runs at 2 Hz, not per tick.</para>
///
/// <para><b>Deliberately gentle.</b> It never targets players, companions, NPCs or traders, and it holds
/// fire on a robber who is still walking up or making demands — the talk-first rule of #1043 outranks it,
/// so a bandit encounter still opens as a conversation. On a Creative or Peaceful world it does nothing at
/// all, like every other hostile system.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Blocks the sentry can reach. Short on purpose: it defends the base, it does not clear the
    /// neighbourhood, and a player still has to deal with anything that keeps its distance.</summary>
    private const float SentryRange = 14f;

    /// <summary>Hull removed per shot. A planet drone takes a handful of these — enough to matter next to a
    /// player's own weapon, not enough to make the player a spectator at their own base.</summary>
    private const float SentryDamage = 6f;

    /// <summary>Seconds between firing passes (2 Hz). Well below a fight's timescale, well above per-tick.</summary>
    private const double SentryFireInterval = 0.5;

    /// <summary>Seconds between re-derivations of a base's sentry cells — the block layout changes rarely,
    /// and the walk is the same O(r³) zone scan the settler stage uses.</summary>
    private const double SentryRescanInterval = 10.0;

    private double _nextSentryFireAt;
    private double _nextSentryRescanAt;

    /// <summary>Cached sentry block cells per base id, refreshed on the rescan beat.</summary>
    private readonly Dictionary<int, List<Vector3i>> _sentryCells = new();

    /// <summary>Whether sentries do anything at all on this world — the same gate the machines themselves
    /// use, so a Peaceful or Creative world has no shooting on either side.</summary>
    private bool SentriesActive => PlanetEnemiesActive;

    /// <summary>2 Hz: every sentry of every "home" base fires at its nearest valid target.</summary>
    private void TickSentries()
    {
        if (!SentriesActive || _uptime < _nextSentryFireAt)
        {
            return;
        }

        _nextSentryFireAt = _uptime + SentryFireInterval;
        bool rescan = _uptime >= _nextSentryRescanAt;
        if (rescan)
        {
            _nextSentryRescanAt = _uptime + SentryRescanInterval;
        }

        bool enemiesChanged = false;
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId || !OwnerIsHome(b))
            {
                _sentryCells.Remove(b.Id); // nobody home: drop the cache rather than keep it warm
                continue;
            }

            if (rescan || !_sentryCells.ContainsKey(b.Id))
            {
                _sentryCells[b.Id] = FindSentryCells(b);
            }

            foreach (var cell in _sentryCells[b.Id])
            {
                enemiesChanged |= FireSentry(cell, b);
            }
        }

        if (enemiesChanged)
        {
            BroadcastPlanetEnemies();
        }
    }

    /// <summary>Whether the base's owner is joined and on this body. A sentry is a convenience for the
    /// person who built it, not a background simulation that runs on an empty world.</summary>
    private bool OwnerIsHome(ServerBase b)
        => _sessions.Values.Any(s => s.Joined && s.State.PlayerId == b.OwnerId
                                     && s.CurrentLocationId == b.Planet);

    /// <summary>The sentry blocks standing inside a base zone — the same walk as
    /// <see cref="CountBaseMachines"/>, looking for one specific key.</summary>
    private List<Vector3i> FindSentryCells(ServerBase b)
    {
        var found = new List<Vector3i>();
        int r = BaseProtectionRadius;
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    var pos = new Vector3i(b.Cell.X + x, b.Cell.Y + y, b.Cell.Z + z);
                    if (!WithinBuildHeight(pos.Y))
                    {
                        continue;
                    }

                    var block = _world.GetBlock(WorldConstants.CanonicalBlock(pos, _world.Circumference));
                    if (!block.IsAir && _content.BlockById(block) is { Key: SentryBlockKey })
                    {
                        found.Add(pos);
                    }
                }

        return found;
    }

    /// <summary>One sentry's shot. Returns true when a planet enemy's state changed, so the caller can
    /// broadcast once for the whole pass rather than once per shot. <paramref name="home"/> is the base the
    /// sentry belongs to — its owner is the person a kill is credited to.</summary>
    private bool FireSentry(Vector3i cell, ServerBase home)
    {
        var muzzle = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        if (NearestSentryTarget(muzzle) is not { } target)
        {
            return false;
        }

        BroadcastToWorld(new SentryShot { X = muzzle.X, Y = muzzle.Y, Z = muzzle.Z, TargetId = target.Id });

        target.Hull -= SentryDamage;
        if (target.Hull > 0f)
        {
            return true;
        }

        KillBySentry(target, cell, home);
        return true;
    }

    /// <summary>The closest hostile a sentry may shoot: a Guardian machine, or a bandit that has already
    /// committed to a fight. Bandits still walking up or making demands are left alone — the hold-up is a
    /// conversation the player gets to answer first (#1043) — and so is anything that is leaving.</summary>
    private CombatEntity? NearestSentryTarget(Vector3f muzzle)
    {
        CombatEntity? best = null;
        double bestDistSq = SentryRange * SentryRange;

        foreach (var e in _planetEnemies)
        {
            if (e.IsBandit ? !BanditIsFighting(e) : !e.Hostile)
            {
                continue;
            }

            double d = WrapDistSq(e.Position, muzzle);
            if (d <= bestDistSq && HasLineOfSight(e.Position, muzzle))
            {
                best = e;
                bestDistSq = d;
            }
        }

        foreach (var b in _bandits)
        {
            if (!BanditIsFighting(b))
            {
                continue;
            }

            double d = WrapDistSq(b.Position, muzzle);
            if (d <= bestDistSq && HasLineOfSight(b.Position, muzzle))
            {
                best = b;
                bestDistSq = d;
            }
        }

        return best;
    }

    /// <summary>A bandit the sentry is allowed to shoot: one that is actually fighting. Approach and
    /// Demanding are the talk phases, Leaving is someone walking away.</summary>
    private static bool BanditIsFighting(CombatEntity b)
        => b.IsBandit && b.BanditPhase is not (BanditPhase.Approach or BanditPhase.Demanding or BanditPhase.Leaving or BanditPhase.Scouting);

    /// <summary>A sentry finished a target. Mirrors the player kill path with the base OWNER standing in for
    /// the shooter (#1292): the drops land on the ground at the corpse (no inventory to bank into), the
    /// world-level consequences (camp bookkeeping, story credit, the post-kill spawn grace) happen exactly as
    /// for a player kill, and the owner's session takes the mission credit — Defeat objectives for bandits /
    /// machines, and for a base scout the homestead bounty + <c>base:defended</c>. What is NOT mirrored is the
    /// generic "defeat" achievement: the player did not land the blow, and a turret farming that counter would
    /// cheapen it. The owner is looked up by id rather than passed in from the tick because
    /// <see cref="OwnerIsHome"/> only proves they are joined; the session object is what the credit needs.</summary>
    private void KillBySentry(CombatEntity target, Vector3i sentryCell, ServerBase home)
    {
        _planetEnemies.Remove(target);
        _bandits.Remove(target);
        _enemyWander.Remove(target.Id);

        SpillToGround(target.Position.ToBlock(), target.Loot);
        BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });

        var owner = _sessions.Values.FirstOrDefault(s => s.Joined && s.State.PlayerId == home.OwnerId);
        if (target.IsBandit)
        {
            OnBanditKilled(target); // camps still clear — no story credit, bandits are people
            if (owner is not null)
            {
                OnMissionDefeat(owner, DefeatTargetBandit); // #730: the bounty counts the drive-off, whoever fired
                OnScoutDefeated(owner, target); // #1224: a beaten base scout credits the homestead bounty
            }
        }
        else
        {
            RecordStoryMachineKill();
            if (owner is not null)
            {
                OnMachineDefeated(owner); // #1213: post-win only — the survey orders' Defeat step
            }

            // #740: a destroyed machine buys the same breather as a player kill — a defended base is followed
            // by quiet, not by reinforcements the sentry then has to shoot again.
            _enemySpawnTimer = System.Math.Min(_enemySpawnTimer, -EnemyKillSpawnGrace);
        }

        _log.Info($"Sentry at {sentryCell.X},{sentryCell.Y},{sentryCell.Z} destroyed '{target.Name}' ({target.Id}).");
    }

    /// <summary>The block a sentry is.</summary>
    private const string SentryBlockKey = "sentry_post";

    /// <summary>Test hook: run a firing pass right now, ignoring the 2 Hz gate.</summary>
    public void TickSentriesForTest()
    {
        _nextSentryFireAt = 0;
        _nextSentryRescanAt = 0;
        TickSentries();
    }

    /// <summary>Test/inspection: how many sentry blocks the given base currently has.</summary>
    public int SentryCountForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b ? FindSentryCells(b).Count : 0;
}
