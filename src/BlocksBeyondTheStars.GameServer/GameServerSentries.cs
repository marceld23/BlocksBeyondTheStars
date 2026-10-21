// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The base sentry post (#1214). Before this, a base was scenery: nothing ever approached it and it never
/// did anything back. A <c>sentry_post</c> block inside a founded base zone shoots at hostile machines and
/// at bandits who have already chosen to fight — a small, quiet answer to "my base just stands there".
///
/// <para><b>Deliberately cheap.</b> There is no power system (rejected in #1101), no ammunition and no
/// per-sentry persisted state. The sentry cells are derived from the base-zone scan that
/// <see cref="CountBaseMachines"/> already walks, cached per base until a post or relay of its chain comes or
/// goes (#2879), and only for bases whose owner is actually joined and standing on that body — enemies spawn
/// 35–50 blocks from a player, so a sentry has nothing to shoot at while nobody is home anyway. The firing
/// pass runs at 2 Hz, not per tick.</para>
///
/// <para><b>Deliberately gentle.</b> It never targets players, companions, NPCs or traders, and it holds
/// fire on a robber who is still walking up or making demands — the talk-first rule of #1043 outranks it,
/// so a bandit encounter still opens as a conversation. On a Creative or Peaceful world it does nothing at
/// all, like every other hostile system.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Blocks the sentry can reach. Short on purpose: it defends the base, it does not clear the
    /// neighbourhood, and a player still has to deal with anything that keeps its distance. The number lives
    /// in <see cref="WorldConstants"/> since #1699 — the scan readout quotes it to the player.</summary>
    private const float SentryRange = WorldConstants.SentryRange;

    /// <summary>Hull removed per shot. A planet drone takes a handful of these — enough to matter next to a
    /// player's own weapon, not enough to make the player a spectator at their own base.</summary>
    private const float SentryDamage = 6f;

    /// <summary>Seconds between firing passes (2 Hz). Well below a fight's timescale, well above per-tick.</summary>
    private const double SentryFireInterval = 0.5;

    // #2226: the gate is per world (see LoadedWorld). It was the server's: every occupied world is ticked in the
    // same server tick, in which the uptime stands still, so the world that came first took every firing pass — base
    // or no base — and the posts on every other world never fired.
    private double _nextSentryFireAt { get => _worlds.Active.NextSentryFireAt; set => _worlds.Active.NextSentryFireAt = value; }

    /// <summary>Cached sentry block cells per base id, walked when the base's own world first needs them and kept until
    /// a post or relay of that chain comes or goes (<see cref="OnSentryChainCellSet"/>). One table for the server: base
    /// ids are unique across bodies.</summary>
    private readonly Dictionary<int, List<Vector3i>> _sentryCells = new();

    /// <summary>#2879: per cached base, the canonical cells of every post and relay its last walk found — the cells whose
    /// change makes the cache stale. Until #2879 every home base was walked again every ten seconds whether anything had
    /// changed or not, which at the relay cap is ~320k block reads (~18 ms on a desktop) per base per beat — a hitch the
    /// browser build, where the server shares the frame, would show.</summary>
    private readonly Dictionary<int, HashSet<Vector3i>> _sentryChainCells = new();

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
        bool enemiesChanged = false;
        bool creaturesChanged = false;
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId)
            {
                // #2226: another world's base. Its cache is that world's pass to keep or drop — dropping it from here
                // made two occupied worlds throw each other's cells away twice a second, each then re-walking its
                // bases (the O(r³) zone scan, relay hops included) on every firing pass.
                continue;
            }

            if (!OwnerIsHome(b))
            {
                DropSentryCells(b.Id); // nobody home: drop the cache rather than keep it warm
                continue;
            }

            if (!_sentryCells.ContainsKey(b.Id))
            {
                var chain = new HashSet<Vector3i>();
                _sentryCells[b.Id] = FindSentryCells(b, chain);
                _sentryChainCells[b.Id] = chain;
                _sentryRescansForTest++;
            }

            foreach (var cell in _sentryCells[b.Id])
            {
                var (enemies, creatures) = FireSentryLinked(cell, b); // #2053: a conduit can hold its fire and reads "has a target"
                enemiesChanged |= enemies;
                creaturesChanged |= creatures;
            }
        }

        if (enemiesChanged)
        {
            BroadcastPlanetEnemies();
        }

        if (creaturesChanged)
        {
            BroadcastCreatures(); // #1699: a shot animal's health bar and its death need the creature wire
        }
    }

    /// <summary>Whether the base's owner is joined and on this body. A sentry is a convenience for the
    /// person who built it, not a background simulation that runs on an empty world.</summary>
    private bool OwnerIsHome(ServerBase b)
        => _sessions.Values.Any(s => s.Joined && s.State.PlayerId == b.OwnerId
                                     && s.CurrentLocationId == b.Planet);

    /// <summary>
    /// Every powered sentry of a base: the ones standing in the base zone, plus the ones a chain of power
    /// relays reaches (#1714).
    /// <para>
    /// The zone alone was the whole rule, and a builder ran straight into it: her spaceport is around 80×80
    /// blocks, a sentry shoots 14 — and may only stand inside a 17-block-wide box around the core. She asked
    /// for exactly this: <i>"nichts wäre leichter als einen Energieversorgungsblock zu schaffen, den man neben
    /// einen Wachposten bauen müsste"</i>.
    /// </para>
    /// <para>
    /// Widening the zone was the obvious alternative and the wrong one: this walk is O(r³) and runs per base
    /// whenever its chain changes, so a radius that covered her compound would cost ~185× what the current one does.
    /// A chain keeps every hop the same cheap 17³ walk and grows only as far as the player actually builds —
    /// and it costs them something per hop, which is what makes it a decision rather than a free upgrade.
    /// </para>
    /// <para><paramref name="chainCells"/>, when given, receives the canonical cell of every post and relay found — what
    /// <see cref="OnSentryChainCellSet"/> watches (#2879).</para>
    /// </summary>
    private List<Vector3i> FindSentryCells(ServerBase b, HashSet<Vector3i>? chainCells = null)
    {
        var found = new List<Vector3i>();
        var visitedRelays = new HashSet<Vector3i>();
        var frontier = new Queue<Vector3i>();
        frontier.Enqueue(b.Cell);

        while (frontier.Count > 0)
        {
            var origin = frontier.Dequeue();
            ScanPowerZone(origin, found, visitedRelays, frontier);
        }

        if (chainCells != null)
        {
            int circ = _world.Circumference;
            foreach (var cell in found)
            {
                chainCells.Add(WorldConstants.CanonicalBlock(cell, circ));
            }

            foreach (var cell in visitedRelays)
            {
                chainCells.Add(WorldConstants.CanonicalBlock(cell, circ));
            }
        }

        return found;
    }

    /// <summary>
    /// #2879: a block changed on a resident world. A post or relay that went up anywhere, or a cell of a cached chain
    /// that changed, makes those bases walk again on their next firing pass — the only ways the powered set can change,
    /// since every block write goes through <see cref="ServerWorld.SetBlock"/>. A new post or relay drops every cache
    /// (it may join any base's chain, and it is a rare event); every other block change — fluid, fire, digging — costs a
    /// block read and a set lookup per cached base.
    /// </summary>
    private void OnSentryChainCellSet(ServerWorld world, Vector3i cell)
    {
        if (_sentryChainCells.Count == 0)
        {
            return;
        }

        var block = world.GetBlock(cell);
        bool chainBlock = !block.IsAir && (block == _sentryBlockId || block == _powerRelayBlockId);
        List<int>? stale = null;
        foreach (var (baseId, cells) in _sentryChainCells)
        {
            if (chainBlock || cells.Contains(cell))
            {
                (stale ??= new List<int>()).Add(baseId);
            }
        }

        if (stale != null)
        {
            foreach (int baseId in stale)
            {
                DropSentryCells(baseId);
            }
        }
    }

    private void DropSentryCells(int baseId)
    {
        _sentryCells.Remove(baseId);
        _sentryChainCells.Remove(baseId);
    }

    /// <summary>One hop of the power chain: walks the cube around <paramref name="origin"/>, collecting sentry
    /// cells and queueing any relay not seen yet. The relay budget caps the whole chain — a player who tiles a
    /// compound with relays must not turn a walk into a world sweep.</summary>
    private void ScanPowerZone(Vector3i origin, List<Vector3i> found, HashSet<Vector3i> visitedRelays, Queue<Vector3i> frontier)
    {
        // #2879: the ids the block-set hook compares against, resolved on the first walk (a cache only exists after
        // one); the walk compares them too instead of looking up each cell's definition and switching on its key.
        _sentryBlockId ??= _content.GetBlock(SentryBlockKey)?.NumericId ?? BlockId.Air;
        _powerRelayBlockId ??= _content.GetBlock(PowerRelayBlockKey)?.NumericId ?? BlockId.Air;
        var sentryId = _sentryBlockId.Value;
        var relayId = _powerRelayBlockId.Value;
        int r = BaseProtectionRadius;
        for (int x = -r; x <= r; x++)
            for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    var pos = new Vector3i(origin.X + x, origin.Y + y, origin.Z + z);
                    if (!WithinBuildHeight(pos.Y))
                    {
                        continue;
                    }

                    var block = _world.GetBlock(WorldConstants.CanonicalBlock(pos, _world.Circumference));
                    if (block.IsAir)
                    {
                        continue;
                    }

                    if (block == sentryId)
                    {
                        if (!found.Contains(pos))
                        {
                            found.Add(pos); // zones overlap along a chain — a sentry must not fire twice
                        }
                    }
                    else if (block == relayId)
                    {
                        if (visitedRelays.Count < MaxPowerRelaysPerBase && visitedRelays.Add(pos))
                        {
                            frontier.Enqueue(pos);
                        }
                    }
                }
    }

    /// <summary>The sentry and relay block ids, resolved on the first walk (#2879; see <see cref="OnSentryChainCellSet"/>).</summary>
    private BlockId? _sentryBlockId, _powerRelayBlockId;

    /// <summary>One sentry's shot. Returns true when a planet enemy's state changed, so the caller can
    /// broadcast once for the whole pass rather than once per shot. <paramref name="home"/> is the base the
    /// sentry belongs to — its owner is the person a kill is credited to.</summary>
    private (bool Enemies, bool Creatures) FireSentry(Vector3i cell, ServerBase home)
    {
        var muzzle = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        if (NearestSentryTarget(muzzle) is not { } target)
        {
            return (false, false);
        }

        bool isCreature = _creatures.Contains(target); // #1699: animals ride the creature wire, not the enemy list
        BroadcastToWorld(new SentryShot { X = muzzle.X, Y = muzzle.Y, Z = muzzle.Z, TargetId = target.Id });

        target.Hull -= SentryDamage;
        if (target.Hull > 0f)
        {
            if (isCreature)
            {
                target.AwakeOverrideTimer = CreatureWakeSeconds; // nothing sleeps through being shot at
            }

            return (!isCreature, isCreature);
        }

        KillBySentry(target, cell, home);
        return (!isCreature, isCreature);
    }

    /// <summary>The closest hostile a sentry may shoot: a Guardian machine, a <b>hostile animal</b> (#1699),
    /// or a bandit that has already committed to a fight. Bandits still walking up or making demands are left
    /// alone — the hold-up is a conversation the player gets to answer first (#1043) — and so is anything that
    /// is leaving.
    ///
    /// <para>Wildlife was missing here, while the game's own advice (<c>vega.hint.base_walls</c>) told the
    /// player that walls keep the ground animals out and that "for fliers and cave dwellers you still need
    /// the sentry post". They were never targets: the search walked the machines and the bandits and nothing
    /// else, so a player who fortified against attacking animals got a turret that watched them come.</para></summary>
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
            if (d <= bestDistSq && SentrySees(e.Position, muzzle))
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
            if (d <= bestDistSq && SentrySees(b.Position, muzzle))
            {
                best = b;
                bestDistSq = d;
            }
        }

        foreach (var c in _creatures)
        {
            if (!SentryMayShootCreature(c))
            {
                continue;
            }

            double d = WrapDistSq(c.Position, muzzle);
            if (d <= bestDistSq && SentrySees(c.Position, muzzle))
            {
                best = c;
                bestDistSq = d;
            }
        }

        return best;
    }

    /// <summary>A sentry's sightline (#2236): from the target's eye height to the post's own block centre — not to
    /// an eye height above the post, where a roof block or a second post stacked on top counted as cover against
    /// everything standing on the post's floor. The post's own cell is the shooter and is skipped.</summary>
    private bool SentrySees(Vector3f target, Vector3f muzzle)
        => HasLineOfSight(target, muzzle, SightEyeHeight, 0f, skipToCell: true);

    /// <summary>An animal the sentry is allowed to shoot (#1699): one that is actually a threat — a hostile
    /// species, or any creature the player has provoked into hunting them — and never a tamed companion.
    /// This is the same rule the client paints red on the health bar, so the turret shoots exactly what the
    /// player already reads as dangerous.</summary>
    private static bool SentryMayShootCreature(CombatEntity c)
        => !c.IsCompanion && !c.IsGiant && (c.Hostile || c.ProvokeTimer > 0); // #1998: a turret does not duel a giant

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
        bool isCreature = _creatures.Contains(target);
        _planetEnemies.Remove(target);
        _bandits.Remove(target);
        _creatures.Remove(target);
        _enemyWander.Remove(target.Id);
        ForgetWadePlan(target.Id); // #2554: a robot shot while wading out of deep water leaves no exit plan behind

        // #1699: an animal's remains are creature loot (the short-lived pickup packet), and its death rides
        // the creature list rather than the planet-enemy wire.
        SpillToGround(target.Position.ToBlock(), target.Loot, creatureLoot: isCreature);
        if (isCreature)
        {
            OnCreatureKilled(target, null); // 2026-09: a sentry may bring down the shapeshifter too
            ForgetClone(target); // #2214: a clone the turret brought down leaves its tank's list at once
            BroadcastCreatures();
            _log.Info($"Sentry at {sentryCell.X},{sentryCell.Y},{sentryCell.Z} brought down '{target.Name}' ({target.Id}).");
            return;
        }

        BroadcastToWorld(new PlanetEnemyDefeated { Id = target.Id });

        var owner = _sessions.Values.FirstOrDefault(s => s.Joined && s.State.PlayerId == home.OwnerId);
        if (target.IsBandit)
        {
            OnBanditKilled(target, owner); // camps still clear — no story credit, bandits are people (#2599: the owner's deed)
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

    /// <summary>The block that carries base power one zone further out (#1714).</summary>
    private const string PowerRelayBlockKey = "power_relay";

    /// <summary>Hard cap on the relays one base's chain may follow. Each relay costs another 17³ walk whenever the
    /// chain is walked, so this is what keeps a relay-tiled compound from turning a walk into a world sweep.
    /// Sixteen hops did not cover the 80×80 spaceport that asked for relays in the first place (#1750); thirty-two did
    /// not run a ring round the same builder's 95×95 one (#2879: a spoke of five plus a ring of ~40). Sixty-four is
    /// ~320k block reads per walk (~18 ms on a desktop) — fine for a walk that only follows a post or relay being built
    /// or taken away, not for a fixed beat. The relay's item text quotes this number — change both.</summary>
    private const int MaxPowerRelaysPerBase = 64;

    /// <summary>Tells the player straight away when a sentry block was placed where it can never fire (#1699):
    /// outside every base zone they own on this body. Three things can silence a post — no base zone, the owner
    /// away, nothing in range — and only this one is a mistake the player can still fix, at the moment they can
    /// still fix it cheaply. A post inside a zone says nothing at all: silence means "it works".</summary>
    private void WarnIfSentryOutsideBase(PlayerSession session, Vector3i pos)
    {
        foreach (var b in _bases)
        {
            if (b.Planet != _world.LocationId || b.OwnerId != session.State.PlayerId)
            {
                continue;
            }

            // #1714: the zone is no longer the only way to be powered — a relay chain counts too, so ask the
            // same question the firing pass asks rather than re-deriving a narrower one here. A post that will
            // work must never be warned about, or the warning becomes noise the player learns to ignore.
            if (FindSentryCells(b).Contains(pos))
            {
                return;
            }
        }

        Send(session, new ServerMessage { Text = "@srv.sentry.outside_base" });
    }

    /// <summary>Test/diagnostic: the base_core cell of a base, so a test can measure from it (#1714).</summary>
    public Vector3i BaseCellForTest(int baseId) => _bases.Single(b => b.Id == baseId).Cell;

    /// <summary>Test hook: run a firing pass right now, ignoring the 2 Hz gate. The cached cells stay as they are — a
    /// block set since the last pass has already dropped them if it touched the chain (#2879).</summary>
    public void TickSentriesForTest()
    {
        _nextSentryFireAt = 0;
        TickSentries();
    }

    private int _sentryRescansForTest;

    /// <summary>Test-only: how many times the firing pass has re-derived a base's sentry cells since start — once per
    /// home base when its chain changed, and not on the passes in between (#2226, #2879).</summary>
    public int SentryRescansForTest => _sentryRescansForTest;

    /// <summary>Test/inspection: how many sentry blocks the given base currently has.</summary>
    public int SentryCountForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b ? FindSentryCells(b).Count : 0;
}
