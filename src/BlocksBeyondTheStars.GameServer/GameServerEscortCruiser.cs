// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3900: what makes a space hostile a boss — its fight's state. Server-only, never persisted.</summary>
public sealed class SpaceBossRuntime
{
    /// <summary>The boss bar's name key.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>The story's cruiser: it carries VEGA's sister core ECHO (handed to the wreck hook when it falls).</summary>
    public bool CarriesEcho { get; set; }

    /// <summary>Its flight speed right now (it rises when it enrages).</summary>
    public float Speed { get; set; } = EscortCruiserRules.Speed;

    /// <summary>The second phase began (from <see cref="EnrageAtHull"/> of its hull on) — once, never back.</summary>
    public bool Enraged { get; set; }

    /// <summary>From the enemy's tier: the hull share it enrages at, the factor on its speed then, the seconds between
    /// two heavy shots then, and how many drones it calls once.</summary>
    public float EnrageAtHull { get; set; }

    public float EnrageSpeed { get; set; }

    public float EnrageHeavyInterval { get; set; }

    public int Reinforcements { get; set; }

    /// <summary>#3903: every pilot who hurt it in this fight — they share the loot, the kill credit and the event.</summary>
    public HashSet<string> Fighters { get; } = new(System.StringComparer.Ordinal);

    /// <summary>Seconds no pilot has been within its leave range (it jumps away at <see cref="EscortCruiserRules.LeaveSeconds"/>).</summary>
    public double AloneSeconds { get; set; }

    /// <summary>Seconds since the last <see cref="BossStatus"/>, and the numbers it carried.</summary>
    public double StatusAge { get; set; }

    public float StatusHealth { get; set; } = -1f;

    public float StatusShield { get; set; } = -1f;
}

public sealed partial class CombatEntity
{
    /// <summary>#3900: a space boss's state (the escort cruiser); null for every other entity.</summary>
    public SpaceBossRuntime? Boss { get; set; }

    /// <summary>#3900: a drone a boss called for help — it leaves with the boss and is no part of the sector's ambient wave.</summary>
    public bool CalledByBoss { get; set; }
}

/// <summary>#3900: the escort cruiser bookkeeping of a flight instance — one ambush roll per instance, like the hunter
/// shuttle's; a summon (the story, an admin) uses the same arrival.</summary>
public sealed partial class SpaceInstance
{
    /// <summary>True once this instance rolled its ambush dice (exactly once per instance).</summary>
    public bool BossRolled { get; set; }

    /// <summary>Server uptime the boss warps in at (0 = none is coming).</summary>
    public double BossArrivalAt { get; set; }

    /// <summary>VEGA's warning for the coming arrival went out.</summary>
    public bool BossWarned { get; set; }

    /// <summary>The coming boss was summoned (the story, an admin) — it skips the ambush's gates.</summary>
    public bool BossSummoned { get; set; }

    /// <summary>The coming boss carries ECHO.</summary>
    public bool BossCarriesEcho { get; set; }

    /// <summary>Entity id of the live boss in this instance (empty = none).</summary>
    public string BossId { get; set; } = string.Empty;
}

/// <summary>
/// The escort cruiser (#3900, canon C22) — the cruiser as a boss. It comes out of hyperspace during a flight: summoned
/// (<see cref="SummonEscortCruiser(string, bool)"/> — the story, at ECHO's last position; the admin verb <c>/cruiser</c>)
/// or as a rare ambush rolled once per flight instance. VEGA warns <see cref="EscortCruiserRules.WarnSeconds"/> before it
/// arrives, at least <see cref="EscortCruiserRules.WarpInMinDistance"/> units from every pilot. It hunts like every space
/// hostile (the pilots' own target rule, never a Sandbox or god-mode pilot), fires its steady guns inside the engage
/// range and its mega laser — the heavy shot of <c>GameServerSpaceManeuvers.cs</c> with a long charge, a longer reach and
/// a cap, all from <c>data/enemies.json</c>. At half hull it enrages once: faster, the mega laser more often, and two
/// drones called. Left alone (no pilot within twice its notice range for 45 s) it jumps away, and that flight never
/// rolls again. While it lives every pilot of the instance gets a <see cref="BossStatus"/>.
/// <para><b>The ambush's gates.</b> Space combat and the space hostiles on, ship weapons that hit NPCs allowed and one
/// fitted on a pilot's ship, the world's Danger not Off; never in the start system, the Guardian's finale system or a
/// desolate system; only once the world story reached the chapter <see cref="EscortCruiserRules.AmbushFromChapter"/> —
/// or it is off, or won. <b>After the finale</b> the ambush stays possible wherever it was (like the guardian giants the
/// cruisers never heard the shutdown): it deliberately does not ask <see cref="RemnantSpaceHostile"/>.</para>
/// </summary>
public sealed partial class GameServer
{
    private readonly System.Random _spaceBossRng = new();

    private const double BossStatusMinSeconds = 0.5;  // a changed bar is sent at most twice a second
    private const double BossStatusBeatSeconds = 1.0; // …and an unchanged one once a second (late joiners, a lost packet)
    private const double BossArrivalOverdueSeconds = 2.0; // an arrival this late was never ticked: warn again first

    /// <summary>Whether the world's rules allow a space boss at all (the story predicate <c>world:space_bosses</c>): space
    /// flight and PvE space combat on, the space hostiles not Off, and ship weapons that may hit NPCs — every leg keeps
    /// the pilots able to fight back.</summary>
    internal bool SpaceBossesAllowed => Rules.FreeSpaceFlight
        && Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both
        && Rules.SpaceNpcEnemies != AlienActivity.Off
        && Rules.ShipWeapons is not (ShipWeaponMode.Off or ShipWeaponMode.MiningOnly);

    /// <summary>The story gate of the ambush: the world story reached the chapter the cruisers appear in, or it is won,
    /// or the world runs no story (or one without that chapter — then nothing is spoiled).</summary>
    private bool EscortCruiserStoryReached()
    {
        if (_story is null || _storyState.GuardianDefeated)
        {
            return true;
        }

        int from = StoryChapters.IndexOf(_story, EscortCruiserRules.AmbushFromChapter);
        return from < 0 || WorldChapterIndex() >= from;
    }

    /// <summary>Whether a pilot of this instance flies a ship with a weapon that can hit a hostile.</summary>
    private bool AnyArmedPilotIn(SpaceInstance instance)
    {
        foreach (var s in PilotsIn(instance))
        {
            if (!s.Ships.TryGetValue(s.ActiveShipId, out var ship))
            {
                continue;
            }

            foreach (var key in ship.Modules)
            {
                if (_content.GetShipModule(key) is { } def && ShipWeaponRules.IsWeapon(def)
                    && (int)def.Stats.GetValueOrDefault("weapon_class", 1) >= 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Every gate of the rare ambush for this instance (see the class remarks). The Guardian's defeat is no gate.</summary>
    private bool EscortCruiserAmbushAllowedIn(SpaceInstance instance)
    {
        if (!SpaceBossesAllowed || _meta.Description.Danger.DangerFactor() <= 0.0 || !EscortCruiserStoryReached())
        {
            return false;
        }

        string systemId = SystemIdOfInstance(instance);
        return systemId.Length > 0
            && !IsStartSystem(systemId)
            && !IsGuardianSystemLocation(InstanceBodyId(instance))
            && SystemArchetypeOf(systemId) != SystemArchetype.Desolate
            && AnyArmedPilotIn(instance);
    }

    /// <summary>Whether a boss lives in the instance or is on its way.</summary>
    private static bool SpaceBossBusy(SpaceInstance instance) => instance.BossId.Length > 0 || instance.BossArrivalAt > 0;

    /// <summary>The one ambush roll of an instance: <paramref name="dice"/> (0..1) against the chance of
    /// <see cref="EscortCruiserRules.AmbushChance"/>. A hit schedules the arrival
    /// <see cref="EscortCruiserRules.AmbushDelayMin"/>–<see cref="EscortCruiserRules.AmbushDelayMax"/> seconds on.</summary>
    private void RollEscortCruiser(SpaceInstance instance, double dice)
    {
        instance.BossRolled = true;
        if (SpaceBossBusy(instance) || !EscortCruiserAmbushAllowedIn(instance))
        {
            return;
        }

        double chance = EscortCruiserRules.AmbushChance(Rules.SpaceNpcEnemies, _meta.Description.Danger.DangerFactor());
        if (dice < chance)
        {
            instance.BossSummoned = false;
            instance.BossCarriesEcho = false;
            instance.BossWarned = false;
            instance.BossArrivalAt = _uptime + EscortCruiserRules.AmbushDelayMin
                + (_spaceBossRng.NextDouble() * (EscortCruiserRules.AmbushDelayMax - EscortCruiserRules.AmbushDelayMin));
        }
    }

    /// <summary>The story's (and the admin's) call: an escort cruiser comes out of hyperspace in the pilot's flight —
    /// VEGA warns now, it arrives <see cref="EscortCruiserRules.WarnSeconds"/> later. <paramref name="carriesEcho"/> marks
    /// the story's cruiser (handed on to the wreck hook). False when the pilot is not flying, the world's rules allow
    /// no space boss (<see cref="SpaceBossesAllowed"/>) or a boss already lives or comes in that flight.</summary>
    public bool SummonEscortCruiser(string pilotId, bool carriesEcho)
        => _playerInstance.TryGetValue(pilotId, out var id) && _spaceInstances.TryGetValue(id, out var instance)
           && SummonEscortCruiser(instance, carriesEcho);

    /// <inheritdoc cref="SummonEscortCruiser(string, bool)"/>
    private bool SummonEscortCruiser(SpaceInstance instance, bool carriesEcho)
    {
        if (!SpaceBossesAllowed || SpaceBossBusy(instance))
        {
            return false;
        }

        instance.BossSummoned = true;
        instance.BossCarriesEcho = carriesEcho;
        instance.BossWarned = false;
        instance.BossArrivalAt = _uptime + EscortCruiserRules.WarnSeconds;
        WarnOfEscortCruiser(instance);
        return true;
    }

    /// <summary>VEGA's warning to every pilot of the instance: a jump signature — something big is coming.</summary>
    private void WarnOfEscortCruiser(SpaceInstance instance)
    {
        instance.BossWarned = true;
        foreach (var s in PilotsIn(instance))
        {
            SendVegaLine(s, "vega.cruiser.incoming", 3);
        }
    }

    /// <summary>Per flight instance (from <see cref="TickSpace"/>): the one roll, the warning and the arrival of a coming
    /// boss, then the live boss's phase, its patience and its status.</summary>
    private void TickSpaceBosses(SpaceInstance instance, double dt)
    {
        if (!instance.BossRolled)
        {
            RollEscortCruiser(instance, _spaceBossRng.NextDouble());
        }

        if (instance.BossArrivalAt > 0)
        {
            TickEscortCruiserArrival(instance);
        }

        if (instance.BossId.Length == 0)
        {
            return;
        }

        CombatEntity? boss = null;
        foreach (var e in instance.Entities)
        {
            if (e.Id == instance.BossId)
            {
                boss = e;
                break;
            }
        }

        if (boss?.Boss is not { } rt)
        {
            instance.BossId = string.Empty; // gone without a kill (an instance rebuilt under it) — no bar is left behind
            return;
        }

        TickSpaceBossPhase(instance, boss, rt);
        if (TickSpaceBossPatience(instance, boss, rt, dt))
        {
            return; // it jumped away
        }

        rt.StatusAge += dt;
        bool changed = System.Math.Abs(SpaceBossHealth(boss) - rt.StatusHealth) > 0.0005f
            || System.Math.Abs(ShieldFraction(boss) - rt.StatusShield) > 0.0005f;
        if ((changed && rt.StatusAge >= BossStatusMinSeconds) || rt.StatusAge >= BossStatusBeatSeconds)
        {
            SendSpaceBossStatus(instance, boss, active: true, defeated: false);
        }
    }

    /// <summary>A coming boss: the warning <see cref="EscortCruiserRules.WarnSeconds"/> ahead, then the warp-in. An
    /// ambush whose gates closed meanwhile (the rules changed, the armed pilot left) is called off without a word.</summary>
    private void TickEscortCruiserArrival(SpaceInstance instance)
    {
        // A flight nobody sits at a helm of is not ticked. An arrival found long overdue would come without a warning
        // anybody remembers — so VEGA warns afresh, and the boss comes the usual time after that.
        if (instance.BossWarned && _uptime - instance.BossArrivalAt > BossArrivalOverdueSeconds)
        {
            instance.BossWarned = false;
        }

        bool warnDue = !instance.BossWarned && _uptime >= instance.BossArrivalAt - EscortCruiserRules.WarnSeconds;
        if (!warnDue && _uptime < instance.BossArrivalAt)
        {
            return; // nothing to decide this tick (the gates are asked at the warning and at the arrival, not every tick)
        }

        bool allowed = instance.BossSummoned ? SpaceBossesAllowed : EscortCruiserAmbushAllowedIn(instance);
        if (!allowed)
        {
            instance.BossArrivalAt = 0;
            return;
        }

        if (warnDue)
        {
            if (!instance.BossSummoned && FirstPilotIn(instance) is null)
            {
                instance.BossArrivalAt = 0; // only pilots nothing may hurt are here — no ambush
                return;
            }

            WarnOfEscortCruiser(instance);
            instance.BossArrivalAt = System.Math.Max(instance.BossArrivalAt, _uptime + EscortCruiserRules.WarnSeconds); // never sooner after the warning
        }

        if (_uptime >= instance.BossArrivalAt)
        {
            instance.BossArrivalAt = 0;
            SpawnEscortCruiser(instance, instance.BossCarriesEcho, instance.BossSummoned);
        }
    }

    /// <summary>The warp-in: the boss appears <see cref="EscortCruiserRules.WarpInDistance"/> units from the pilot it will
    /// hunt (never a Sandbox pilot; a summoned one appears near any pilot when there is no other) and at least
    /// <see cref="EscortCruiserRules.WarpInMinDistance"/> from everyone, with its status, the flash and the new state.</summary>
    private CombatEntity? SpawnEscortCruiser(SpaceInstance instance, bool carriesEcho, bool summoned)
    {
        var mark = FirstPilotIn(instance) ?? (summoned ? PilotsIn(instance).FirstOrDefault() : null);
        if (mark is null || instance.BossId.Length > 0)
        {
            return null;
        }

        var at = PilotPositionIn(instance, mark.State.PlayerId);
        var pos = EscortCruiserArrivalPoint(instance, at);
        var tier = _content.Enemy(EnemyCatalog.EscortCruiser).Tier(EnemyTiers.Normal);
        var boss = NewEnemy(EnemyCatalog.EscortCruiser, EnemyTiers.Normal, CombatEntityKind.Cruiser, pos);
        boss.Scale = EscortCruiserRules.ModelScale;
        boss.Boss = new SpaceBossRuntime
        {
            NameKey = EscortCruiserRules.NameKey,
            CarriesEcho = carriesEcho,
            EnrageAtHull = tier.EnrageAtHull,
            EnrageSpeed = tier.EnrageSpeed,
            EnrageHeavyInterval = tier.EnrageHeavyShotInterval,
            Reinforcements = tier.Reinforcements,
        };
        FaceCourse(boss, at.X - pos.X, at.Z - pos.Z);
        instance.Entities.Add(boss);
        instance.BossId = boss.Id;

        // The status first: when the state with the new cruiser arrives, the client already knows it is the boss.
        SendSpaceBossStatus(instance, boss, active: true, defeated: false);
        BroadcastToInstance(instance, new SpaceWarpFx { X = pos.X, Y = pos.Y, Z = pos.Z, Arriving = true, Style = EscortCruiserRules.WarpStyle });
        BroadcastSpaceState(instance);
        _log.Info($"An escort cruiser warped into '{instance.Id}' ({(summoned ? "summoned" : "ambush")}{(carriesEcho ? ", carries ECHO" : string.Empty)}).");
        return boss;
    }

    /// <summary>Where it comes out of hyperspace: on a random bearing around the hunted pilot, the first of a few tries
    /// that keeps its distance to every pilot — else the try that keeps the most.</summary>
    private Vector3f EscortCruiserArrivalPoint(SpaceInstance instance, Vector3f mark)
    {
        Vector3f best = default;
        float bestSq = -1f;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            double ang = _spaceBossRng.NextDouble() * System.Math.PI * 2.0;
            var pos = new Vector3f(
                mark.X + ((float)System.Math.Cos(ang) * EscortCruiserRules.WarpInDistance),
                mark.Y + (float)((_spaceBossRng.NextDouble() * 20.0) - 10.0),
                mark.Z + ((float)System.Math.Sin(ang) * EscortCruiserRules.WarpInDistance));
            float nearestSq = float.MaxValue;
            foreach (var pose in instance.PlayerPoses)
            {
                if (instance.Players.Contains(pose.Key))
                {
                    nearestSq = System.Math.Min(nearestSq, pose.Value.Pos.DistanceSquared(pos));
                }
            }

            if (nearestSq > bestSq)
            {
                bestSq = nearestSq;
                best = pos;
            }

            if (nearestSq >= EscortCruiserRules.WarpInMinDistance * EscortCruiserRules.WarpInMinDistance)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>The second phase, once: from <see cref="SpaceBossRuntime.EnrageAtHull"/> of its hull on it flies faster,
    /// its mega laser comes more often (the next one soon), it calls its drones, VEGA says so and the bar changes.</summary>
    private void TickSpaceBossPhase(SpaceInstance instance, CombatEntity boss, SpaceBossRuntime rt)
    {
        if (rt.Enraged || rt.EnrageAtHull <= 0f || boss.Hull > boss.HullMax * rt.EnrageAtHull)
        {
            return;
        }

        rt.Enraged = true;
        if (rt.EnrageSpeed > 0f)
        {
            rt.Speed = EscortCruiserRules.Speed * rt.EnrageSpeed;
        }

        if (rt.EnrageHeavyInterval > 0f)
        {
            boss.HeavyInterval = rt.EnrageHeavyInterval;
            if (boss.HeavyFireAt <= 0 && boss.HeavyNextAt > _uptime + (boss.HeavyInterval * 0.5))
            {
                boss.HeavyNextAt = _uptime + (boss.HeavyInterval * 0.5); // a wait from the calm phase is cut short
            }
        }

        for (int i = 0; i < rt.Reinforcements; i++)
        {
            double ang = _spaceBossRng.NextDouble() * System.Math.PI * 2.0;
            var pos = new Vector3f(
                boss.Position.X + ((float)System.Math.Cos(ang) * 26f),
                boss.Position.Y + ((i % 2 == 0) ? 8f : -8f),
                boss.Position.Z + ((float)System.Math.Sin(ang) * 26f));
            var drone = NewEnemy(EnemyCatalog.SpaceDrone, EnemyTiers.Normal, CombatEntityKind.Drone, pos);
            drone.CalledByBoss = true;
            instance.Entities.Add(drone);
            BroadcastWarpFx(instance, pos, arriving: true);
        }

        foreach (var s in PilotsIn(instance))
        {
            SendVegaLine(s, "vega.cruiser.enraged", 3);
        }

        SendSpaceBossStatus(instance, boss, active: true, defeated: false);
        BroadcastSpaceState(instance);
    }

    /// <summary>Getting away: with no pilot within <see cref="EscortCruiserRules.LeaveRangeFactor"/> times its notice range
    /// for <see cref="EscortCruiserRules.LeaveSeconds"/> it jumps away (true). Any pilot counts, also one it does not hunt.</summary>
    private bool TickSpaceBossPatience(SpaceInstance instance, CombatEntity boss, SpaceBossRuntime rt, double dt)
    {
        float range = EscortCruiserRules.NoticeRange * EscortCruiserRules.LeaveRangeFactor;
        foreach (var pilotId in instance.Players)
        {
            if (instance.PlayerPoses.TryGetValue(pilotId, out var pose) && pose.Pos.DistanceSquared(boss.Position) <= range * range)
            {
                rt.AloneSeconds = 0;
                return false;
            }
        }

        rt.AloneSeconds += dt;
        if (rt.AloneSeconds < EscortCruiserRules.LeaveSeconds)
        {
            return false;
        }

        WarpOutEscortCruiser(instance, boss);
        return true;
    }

    /// <summary>It gives up the chase: the flash, gone from the flight (no explosion — it was not destroyed), the drones it
    /// called with it, VEGA's line and the bar's last status. The instance has rolled already, so it does not come back.</summary>
    private void WarpOutEscortCruiser(SpaceInstance instance, CombatEntity boss)
    {
        BroadcastToInstance(instance, new SpaceWarpFx
        {
            X = boss.Position.X,
            Y = boss.Position.Y,
            Z = boss.Position.Z,
            Arriving = false,
            Style = EscortCruiserRules.WarpStyle,
        });
        instance.Entities.Remove(boss);
        for (int i = instance.Entities.Count - 1; i >= 0; i--)
        {
            if (instance.Entities[i].CalledByBoss)
            {
                BroadcastWarpFx(instance, instance.Entities[i].Position, arriving: false);
                instance.Entities.RemoveAt(i);
            }
        }

        instance.BossId = string.Empty;
        instance.BossRolled = true;
        SendSpaceBossStatus(instance, boss, active: false, defeated: false);
        foreach (var s in PilotsIn(instance))
        {
            SendVegaLine(s, "vega.cruiser.gone", 2);
        }

        BroadcastSpaceState(instance);
        _log.Info($"The escort cruiser left '{instance.Id}' — nobody stayed to fight it.");
    }

    /// <summary>A boss's hull as the bar's share.</summary>
    private static float SpaceBossHealth(CombatEntity boss)
        => boss.HullMax > 0f ? System.Math.Clamp(boss.Hull / boss.HullMax, 0f, 1f) : 0f;

    /// <summary>The boss bar's numbers to every pilot of the instance.</summary>
    private void SendSpaceBossStatus(SpaceInstance instance, CombatEntity boss, bool active, bool defeated)
    {
        if (boss.Boss is not { } rt)
        {
            return;
        }

        rt.StatusAge = 0;
        rt.StatusHealth = defeated ? 0f : SpaceBossHealth(boss);
        rt.StatusShield = defeated ? 0f : ShieldFraction(boss);
        BroadcastToInstance(instance, new BossStatus
        {
            Id = boss.Id,
            NameKey = rt.NameKey,
            Health = rt.StatusHealth,
            Shield = rt.StatusShield,
            Phase = rt.Enraged ? EscortCruiserRules.PhaseEnraged : EscortCruiserRules.PhaseNormal,
            Source = BossSources.Space,
            Active = active,
            Defeated = defeated,
        });
    }

    // ---------------- Admin + test seams ----------------

    /// <summary>The admin verb <c>/cruiser</c> (<c>/cruiser echo</c> for the story's): summons the boss into the admin's
    /// own flight — with the warning, so the whole arrival can be watched.</summary>
    private void AdminSummonEscortCruiser(PlayerSession session, string? arg)
    {
        bool echo = string.Equals((arg ?? string.Empty).Trim(), "echo", System.StringComparison.OrdinalIgnoreCase);
        if (!InSpace(session.State.PlayerId))
        {
            Reject(session, "admin", "@srv.cruiser.not_flying");
            return;
        }

        if (!SummonEscortCruiser(session.State.PlayerId, echo))
        {
            Reject(session, "admin", SpaceBossesAllowed ? "@srv.cruiser.already" : "@srv.cruiser.rules_off");
            return;
        }

        Send(session, new ServerMessage { Text = "@srv.cruiser.summoned" });
        CheatLog(session.State, echo ? "summoned the escort cruiser that carries ECHO" : "summoned an escort cruiser");
    }

    /// <summary>Test seam: the live boss of the player's flight instance (null = none).</summary>
    public CombatEntity? SpaceBossForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var instance)
            ? instance.Entities.FirstOrDefault(e => e.Boss is not null)
            : null;

    /// <summary>Test seam: seconds until a coming boss arrives in the player's flight (0 = none is coming).</summary>
    public double SpaceBossArrivalInForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var instance) && instance.BossArrivalAt > 0
            ? System.Math.Max(0.0001, instance.BossArrivalAt - _uptime)
            : 0.0;

    /// <summary>Test seam: rolls the ambush of the player's flight afresh with the given dice (0..1) and reports whether a
    /// boss is now on its way — <c>0</c> passes whenever the gates do, so a test reads the gates from it.</summary>
    public bool RollEscortCruiserForTest(string playerId, double dice = 0.0)
    {
        if (!_playerInstance.TryGetValue(playerId, out var id) || !_spaceInstances.TryGetValue(id, out var instance))
        {
            return false;
        }

        instance.BossArrivalAt = 0;
        RollEscortCruiser(instance, dice);
        return instance.BossArrivalAt > 0;
    }

    /// <summary>Test seam: puts the boss into the player's flight now (no warning, no gates) at
    /// <paramref name="at"/>, or at its arrival point when none is given.</summary>
    public CombatEntity? SpawnEscortCruiserForTest(string playerId, bool carriesEcho = false, Vector3f? at = null)
    {
        if (!_playerInstance.TryGetValue(playerId, out var id) || !_spaceInstances.TryGetValue(id, out var instance))
        {
            return null;
        }

        instance.BossRolled = true;
        instance.BossArrivalAt = 0;
        var boss = SpawnEscortCruiser(instance, carriesEcho, summoned: true);
        if (boss is not null && at is { } pos)
        {
            boss.Position = pos;
        }

        return boss;
    }

    /// <summary>Test seam: whether the world's rules allow a space boss.</summary>
    public bool SpaceBossesAllowedForTest => SpaceBossesAllowed;
}
