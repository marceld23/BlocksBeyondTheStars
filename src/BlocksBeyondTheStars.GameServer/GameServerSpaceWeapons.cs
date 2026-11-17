// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3758: a missile on its way — the shot was accepted and paid for, its damage lands at <see cref="HitAt"/>
/// if the target still exists then.</summary>
internal readonly record struct MissileInFlight(string PlayerId, string TargetId, float Damage, float Stun, double HitAt);

public sealed partial class SpaceInstance
{
    /// <summary>#3758: the missiles flying in this instance. Transient — a flight that ends takes them with it.</summary>
    internal List<MissileInFlight> Missiles { get; } = new();
}

/// <summary>
/// Ship weapons with their own behaviour (#3758), all driven by module stats (<see cref="ShipWeaponRules"/>):
/// <list type="bullet">
/// <item>a shot with a <b>flight speed</b> (the missile) is accepted like any shot and lands when it has flown the
/// distance — on a target destroyed meanwhile it is wasted;</item>
/// <item>a shot with several <b>targets</b> (the scatter cannon) also hits the hostiles inside its cone around the
/// aimed one;</item>
/// <item>a shot with a <b>stun</b> (the ion pulse) makes the hit hostile hold still and hold its fire;</item>
/// <item>an <b>auto</b> weapon (the point-defence turret) fires by itself at the nearest hostile in its range.</item>
/// </list>
/// Every hit goes through <c>DamageSpaceTarget</c>, the same path a laser shot takes.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The cursor ship's weapon that fires by itself, or null (set in <c>RecomputeShipCombatStats</c>).</summary>
    private string? _shipTurretKey;

    private readonly List<CombatEntity> _scatterScratch = new();

    /// <summary>An accepted, paid shot takes effect: a missile starts its flight, anything else hits now — the aimed
    /// target and, for a scatter weapon, the hostiles in its cone.</summary>
    private void ResolveShipShot(SpaceInstance instance, PlayerSession? session, string playerId, WeaponSpec weapon,
        CombatEntity target, Vector3f shipPos)
    {
        if (weapon.FlightSpeed > 0f)
        {
            float dist = (float)System.Math.Sqrt(target.Position.DistanceSquared(shipPos));
            instance.Missiles.Add(new MissileInFlight(playerId, target.Id, weapon.Damage, weapon.Stun, _uptime + (dist / weapon.FlightSpeed)));
            return;
        }

        _scatterScratch.Clear();
        if (weapon.Targets > 1)
        {
            CollectScatterTargets(instance, weapon, target, shipPos);
        }

        HitSpaceTarget(instance, session, target, weapon.Damage, weapon.Stun);
        foreach (var other in _scatterScratch)
        {
            HitSpaceTarget(instance, session, other, weapon.Damage, weapon.Stun);
        }
    }

    /// <summary>The further targets of a scatter shot: hostiles the weapon may hit, in its range and inside its cone
    /// around the aimed target — the nearest first, up to the weapon's target count.</summary>
    private void CollectScatterTargets(SpaceInstance instance, WeaponSpec weapon, CombatEntity aimed, Vector3f shipPos)
    {
        float ax = aimed.Position.X - shipPos.X;
        float ay = aimed.Position.Y - shipPos.Y;
        float az = aimed.Position.Z - shipPos.Z;
        foreach (var e in instance.Entities)
        {
            if (ReferenceEquals(e, aimed) || !e.Hostile || e.Hull <= 0f
                || e.Position.DistanceSquared(shipPos) > weapon.Range * weapon.Range
                || !ShipWeaponRules.InCone(ax, ay, az, e.Position.X - shipPos.X, e.Position.Y - shipPos.Y, e.Position.Z - shipPos.Z, weapon.ConeCos)
                || !WeaponAllowedAgainst(weapon, e, out _))
            {
                continue;
            }

            _scatterScratch.Add(e);
        }

        _scatterScratch.Sort((a, b) => a.Position.DistanceSquared(shipPos).CompareTo(b.Position.DistanceSquared(shipPos)));
        if (_scatterScratch.Count > weapon.Targets - 1)
        {
            _scatterScratch.RemoveRange(weapon.Targets - 1, _scatterScratch.Count - (weapon.Targets - 1));
        }
    }

    /// <summary>One hit: the stun first (so a hostile that survives the damage is silenced), then the damage. A
    /// hostile that just came out of a stun cannot be stunned again for
    /// <see cref="ShipWeaponRules.StunImmuneSeconds"/> — it can still be hurt.</summary>
    private void HitSpaceTarget(SpaceInstance instance, PlayerSession? session, CombatEntity target, float damage, float stun)
    {
        if (target.Hull <= 0f || !instance.Entities.Contains(target))
        {
            return; // an earlier hit of the same shot already destroyed it
        }

        if (stun > 0f && target.Hostile && _uptime >= target.StaggerImmuneUntil)
        {
            target.Shield = 0f; // the pulse takes the shield down with it
            target.StaggerUntil = _uptime + stun;
            target.StaggerImmuneUntil = target.StaggerUntil + ShipWeaponRules.StunImmuneSeconds;
        }

        DamageSpaceTarget(instance, session, target, damage);
    }

    /// <summary>Once per tick and instance: missiles that have flown their distance land, and every pilot's
    /// point-defence turret fires when it is ready and a hostile is in its range.</summary>
    private void TickShipWeapons(SpaceInstance instance)
    {
        for (int i = instance.Missiles.Count - 1; i >= 0; i--)
        {
            var missile = instance.Missiles[i];
            if (_uptime < missile.HitAt)
            {
                continue;
            }

            instance.Missiles.RemoveAt(i);
            LandMissile(instance, missile);
        }

        _spacePlayerScratch.Clear();
        _spacePlayerScratch.AddRange(instance.Players);
        foreach (var pilotId in _spacePlayerScratch)
        {
            if (FindSessionByPlayerId(pilotId) is { } pilot
                && instance.PilotSims.TryGetValue(pilotId, out var sim)
                && instance.PlayerPoses.TryGetValue(pilotId, out var pose) && !pose.Eva)
            {
                FireTurret(instance, pilot, sim, pose.Pos);
            }
        }
    }

    /// <summary>A missile arrives. It is wasted when its target is gone, or when the pilot who fired it has left the
    /// flight (the loot of a kill would have no ship to go to).</summary>
    private void LandMissile(SpaceInstance instance, MissileInFlight missile)
    {
        if (!instance.Players.Contains(missile.PlayerId) || FindSessionByPlayerId(missile.PlayerId) is not { } shooter)
        {
            return;
        }

        CombatEntity? target = null;
        foreach (var e in instance.Entities)
        {
            if (e.Id == missile.TargetId)
            {
                target = e;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        SetCurrent(shooter); // loot and salvage of a kill go to the shooter's ship
        HitSpaceTarget(instance, shooter, target, missile.Damage, missile.Stun);
    }

    /// <summary>The point-defence turret of one pilot: when its cooldown has passed it hits the nearest hostile in its
    /// range that the world's combat and weapon rules let it hit, and draws its energy like any weapon.</summary>
    private void FireTurret(SpaceInstance instance, PlayerSession pilot, PilotSim sim, Vector3f shipPos)
    {
        if (_uptime < sim.TurretReadyAt)
        {
            return;
        }

        SetCurrent(pilot);
        if (_shipTurretKey is null || !TryGetWeapon(_shipTurretKey, out var weapon, auto: true))
        {
            sim.TurretReadyAt = _uptime + 1.0; // no turret aboard: look again in a second, not every tick
            return;
        }

        CombatEntity? nearest = null;
        float nearestSq = weapon.Range * weapon.Range;
        foreach (var e in instance.Entities)
        {
            if (!e.Hostile || e.Hull <= 0f)
            {
                continue;
            }

            float dsq = e.Position.DistanceSquared(shipPos);
            if (dsq <= nearestSq && WeaponAllowedAgainst(weapon, e, out _))
            {
                nearestSq = dsq;
                nearest = e;
            }
        }

        if (nearest is null || !TryDrawShipEnergy(pilot.State.PlayerId, weapon.Energy))
        {
            return;
        }

        sim.TurretReadyAt = _uptime + weapon.Cooldown;
        HitSpaceTarget(instance, pilot, nearest, weapon.Damage, weapon.Stun);
    }

    /// <summary>Test seam (#3758): whether an ion pulse holds the space hostile right now.</summary>
    public bool SpaceHostileSilencedForTest(string playerId, string entityId)
    {
        foreach (var e in SpaceEntitiesFor(playerId))
        {
            if (e.Id == entityId)
            {
                return IsStaggered(e);
            }
        }

        return false;
    }

    /// <summary>Test seam (#3758): how many missiles fly in the player's instance.</summary>
    public int MissilesInFlightForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var inst) ? inst.Missiles.Count : 0;
}
