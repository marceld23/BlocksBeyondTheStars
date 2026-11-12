// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3097: what a hunter shuttle is doing in space.</summary>
public enum HunterShuttlePhase : byte
{
    /// <summary>Circles its pilot at a distance and pecks at the shields with a weak gun.</summary>
    Skirmish,

    /// <summary>Stopped shooting and runs for the planet — shoot it down before it jumps away and lands.</summary>
    Fleeing,
}

/// <summary>#3097: a hunter shuttle's own state (only meaningful for <see cref="CombatEntityKind.HunterShuttle"/>).</summary>
public sealed partial class CombatEntity
{
    public HunterShuttlePhase ShuttlePhase { get; set; }

    /// <summary>Skirmish: seconds spent near its pilot. Fleeing: seconds since it turned to run.</summary>
    public double ShuttleClock { get; set; }

    /// <summary>Seconds of skirmish before it gives up and runs.</summary>
    public double ShuttlePatience { get; set; }

    /// <summary>Which way it circles its pilot (+1 / −1).</summary>
    public float ShuttleStrafe { get; set; } = 1f;
}

/// <summary>#3097: the hunter shuttle bookkeeping of a flight instance (one roll per instance, like the raider's).</summary>
public sealed partial class SpaceInstance
{
    public bool HunterShuttleRolled { get; set; }

    /// <summary>Server uptime the shuttle warps in at (0 = not coming).</summary>
    public double HunterShuttleAt { get; set; }

    public string HunterShuttleId { get; set; } = string.Empty;

    public double HunterShuttleSyncTimer { get; set; }
}

/// <summary>
/// #3097 (Justus, 10: "In space a hunter shuttle would be cool. It is bad at attacking in space, but when it lands and you
/// find it, there is a hunter and several robot spawners."): a weak Guardian craft that may warp in during a flight, circle
/// the ship at a distance with a feeble gun, and — once hurt, or after half a minute or so — turn and run. Shot down, it is
/// gone (salvage, no camp). If it gets away it lands on a planet of the system — the one the pilot flies at, if it can — and
/// leaves a camp there: its hull, a hunter on guard and two or three robot docks (<see cref="RegisterHunterSite"/>,
/// GameServerHunterSites.cs). Hard-gated like everything hostile: never in Peaceful or Creative, never with the machines or
/// the space hostiles off, never where the pilot cannot shoot back, never in the Guardian's own system or once the galaxy is
/// pacified.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far it notices its pilot (it warps in inside this, so it comes straight at you).</summary>
    internal const float HunterShuttleAggro = 170f;

    /// <summary>The ring it keeps round its pilot — just inside the damage aura's 70, so its weak gun reaches.</summary>
    internal const float HunterShuttleStandOff = 52f;

    private const float HunterShuttleWarpInDistance = 150f;
    private const float HunterShuttleNearRange = 120f;   // the skirmish clock runs while its pilot is this close
    private const float HunterShuttleStrafeSpeed = 5f;   // its sideways circling at the ring — a moving target
    private const float HunterShuttleFleeSpeed = 15f;
    private const double HunterShuttleFleeSeconds = 8.0; // the window to shoot it down before it jumps
    private const float HunterShuttleWarpOutDistance = 300f;
    private const double HunterShuttleDelayMin = 40.0;   // seconds after the instance opened before it may warp in
    private const double HunterShuttleDelayMax = 110.0;
    private const double HunterShuttlePatienceMin = 30.0;
    private const double HunterShuttlePatienceMax = 50.0;

    /// <summary>The <see cref="SpaceWarpFx.Style"/> of its arrival and departure: the client plays its own sting.</summary>
    internal const string HunterShuttleWarpStyle = "hunter_shuttle";

    private readonly System.Random _hunterShuttleRng = new();

    /// <summary>A hunter shuttle on the run — the hostile movement leaves it to <see cref="TickHunterShuttles"/>.</summary>
    private static bool ShuttleFleeing(CombatEntity e)
        => e.Kind == CombatEntityKind.HunterShuttle && e.ShuttlePhase == HunterShuttlePhase.Fleeing;

    /// <summary>Whether hunter shuttles may appear at all: the planet machines live (Survival, machines on — never Peaceful),
    /// the space hostiles on, space combat live and ship weapons usable against NPCs (the raider's lesson: no fight the
    /// player cannot win), the world's Danger not Off, and the galaxy not yet pacified (the hunters went with the Guardian).</summary>
    private bool HunterShuttlesAllowed => PlanetEnemiesActive && !RemnantEra
        && Rules.SpaceNpcEnemies != AlienActivity.Off
        && Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both
        && Rules.ShipWeapons is not (ShipWeaponMode.Off or ShipWeaponMode.MiningOnly)
        && _meta.Description.Danger.DangerFactor() > 0.0;

    /// <summary>The body a flight instance is anchored on (its launch body).</summary>
    private static string InstanceBodyId(SpaceInstance instance)
        => instance.Id.StartsWith("space:", System.StringComparison.Ordinal) ? instance.Id.Substring(6) : instance.Id;

    /// <summary>Per flight instance (from <see cref="TickSpace"/>): the one roll, the delayed warp-in, then the shuttle's
    /// skirmish or flight.</summary>
    private void TickHunterShuttles(SpaceInstance instance, double dt)
    {
        if (!instance.HunterShuttleRolled)
        {
            RollHunterShuttle(instance);
        }

        if (instance.HunterShuttleAt > 0 && _uptime >= instance.HunterShuttleAt && instance.HunterShuttleId.Length == 0)
        {
            instance.HunterShuttleAt = 0;
            if (HunterShuttlesAllowed)
            {
                SpawnHunterShuttle(instance);
            }
        }

        if (instance.HunterShuttleId.Length == 0)
        {
            return;
        }

        var shuttle = instance.Entities.FirstOrDefault(e => e.Id == instance.HunterShuttleId);
        if (shuttle is null)
        {
            instance.Structures.Remove(RaiderStructureId(instance.HunterShuttleId)); // no orphaned hull for late joiners
            instance.HunterShuttleId = string.Empty;
            return;
        }

        bool moved = shuttle.ShuttlePhase == HunterShuttlePhase.Skirmish
            ? SkirmishHunterShuttle(instance, shuttle, dt)
            : FleeHunterShuttle(instance, shuttle, dt);

        // Its own movement (the circling, the run) happens outside MoveSpaceHostiles, so it streams on its own beat — the
        // raider's cadence (#756).
        instance.HunterShuttleSyncTimer += dt;
        if (moved && instance.HunterShuttleId.Length > 0 && instance.HunterShuttleSyncTimer >= 0.2)
        {
            instance.HunterShuttleSyncTimer = 0;
            BroadcastSpaceState(instance);
        }
    }

    /// <summary>Rolls once per instance whether a shuttle comes, and when: the space-hostiles slider sets the odds, the
    /// world's Danger scales them, and a system with no planet it could land on never sees one.</summary>
    private void RollHunterShuttle(SpaceInstance instance)
    {
        instance.HunterShuttleRolled = true;
        instance.HunterShuttleAt = 0;
        if (!HunterShuttlesAllowed || IsGuardianSystemLocation(InstanceBodyId(instance)) || HunterLandingBodies(instance).Count == 0)
        {
            return;
        }

        double chance = Rules.SpaceNpcEnemies switch
        {
            AlienActivity.Rare => 0.08,
            AlienActivity.Normal => 0.15,
            AlienActivity.Frequent => 0.25,
            AlienActivity.Extreme => 0.35,
            _ => 0.0,
        };
        chance = System.Math.Min(0.5, chance * _meta.Description.Danger.DangerFactor());
        if (_hunterShuttleRng.NextDouble() < chance)
        {
            instance.HunterShuttleAt = _uptime + HunterShuttleDelayMin
                + _hunterShuttleRng.NextDouble() * (HunterShuttleDelayMax - HunterShuttleDelayMin);
        }
    }

    /// <summary>Warps the shuttle in near the first pilot it may hunt (never a Sandbox pilot) with its voxel hull, and VEGA
    /// tells everyone in the instance what it is.</summary>
    private void SpawnHunterShuttle(SpaceInstance instance)
    {
        if (FirstPilotIn(instance) is not { } mark)
        {
            return;
        }

        var at = PilotPositionIn(instance, mark.State.PlayerId);
        double ang = _hunterShuttleRng.NextDouble() * System.Math.PI * 2.0;
        var pos = new Vector3f(
            at.X + (float)System.Math.Cos(ang) * HunterShuttleWarpInDistance,
            at.Y + (float)(_hunterShuttleRng.NextDouble() * 16.0 - 8.0),
            at.Z + (float)System.Math.Sin(ang) * HunterShuttleWarpInDistance);

        var shuttle = NewEnemy(EnemyCatalog.HunterShuttle, EnemyTiers.Normal, CombatEntityKind.HunterShuttle, pos);
        shuttle.ShuttlePatience = HunterShuttlePatienceMin + _hunterShuttleRng.NextDouble() * (HunterShuttlePatienceMax - HunterShuttlePatienceMin);
        shuttle.ShuttleStrafe = _hunterShuttleRng.Next(2) == 0 ? -1f : 1f;
        FaceCourse(shuttle, at.X - pos.X, at.Z - pos.Z);
        instance.Entities.Add(shuttle);
        instance.HunterShuttleId = shuttle.Id;

        // The raider's path (#2357): an ownerless "ship" structure keyed like a raider's hull, so the flight view meshes it
        // through the same "ship_remote" design and late joiners get it with the join-time cross-send.
        var hull = BuildHunterShuttleSpaceHull(RaiderStructureId(shuttle.Id));
        if (hull.Cells.Count > 0)
        {
            hull.Position = pos;
            instance.Structures[hull.Id] = hull;
        }

        foreach (var s in PilotsIn(instance))
        {
            if (hull.Cells.Count > 0)
            {
                SendShipDesign(s, hull, "ship_remote");
            }

            SendVegaLine(s, "vega.hunter_shuttle.spotted", 3);
        }

        BroadcastToInstance(instance, new SpaceWarpFx { X = pos.X, Y = pos.Y, Z = pos.Z, Arriving = true, Style = HunterShuttleWarpStyle });
        BroadcastSpaceState(instance);
        _log.Info($"A hunter shuttle warped into '{instance.Id}'.");
    }

    /// <summary>The skirmish: the hostile movement brings it to its ring; here it circles there (a moving target). Its
    /// patience runs down all the while — at half pace while its pilot keeps away — so left alone it lands all the same.
    /// Hurt to half its hull, out of patience or alone, it runs. True when it moved.</summary>
    private bool SkirmishHunterShuttle(SpaceInstance instance, CombatEntity shuttle, double dt)
    {
        var mark = FirstPilotIn(instance);
        bool moved = false;
        if (mark is not null)
        {
            var at = ChasePosition(instance, shuttle);
            float dx = shuttle.Position.X - at.X;
            float dz = shuttle.Position.Z - at.Z;
            float distSq = shuttle.Position.DistanceSquared(at);
            shuttle.ShuttleClock += distSq <= HunterShuttleNearRange * HunterShuttleNearRange ? dt : dt * 0.5;

            float ring = HunterShuttleStandOff * 1.2f;
            float flat = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (distSq <= ring * ring && flat > 0.5f)
            {
                // Sideways round its pilot: the hostile pass holds it still inside the ring, so this is its only motion there.
                float px = -dz / flat * shuttle.ShuttleStrafe;
                float pz = dx / flat * shuttle.ShuttleStrafe;
                float step = (float)(HunterShuttleStrafeSpeed * dt);
                shuttle.Position = new Vector3f(shuttle.Position.X + px * step, shuttle.Position.Y, shuttle.Position.Z + pz * step);
                FaceCourse(shuttle, px, pz);
                moved = true;
            }
        }

        bool hurt = shuttle.Hull <= shuttle.HullMax * 0.5f;
        if (mark is null || hurt || shuttle.ShuttleClock >= shuttle.ShuttlePatience)
        {
            BeginHunterShuttleFlight(instance, shuttle);
        }

        return moved;
    }

    /// <summary>It stops shooting and runs — VEGA says so: shoot it down now, or it lands somewhere and builds a camp.</summary>
    private void BeginHunterShuttleFlight(SpaceInstance instance, CombatEntity shuttle)
    {
        shuttle.ShuttlePhase = HunterShuttlePhase.Fleeing;
        shuttle.ShuttleClock = 0;
        shuttle.DamagePerSecond = 0f; // no more pecking while it runs
        foreach (var s in PilotsIn(instance))
        {
            SendVegaLine(s, "vega.hunter_shuttle.fleeing", 3);
        }

        BroadcastSpaceState(instance);
    }

    /// <summary>The run: straight away from its pilot, faster than it fought; after <see cref="HunterShuttleFleeSeconds"/> (or
    /// far enough out) it jumps — and lands. Always moves.</summary>
    private bool FleeHunterShuttle(SpaceInstance instance, CombatEntity shuttle, double dt)
    {
        shuttle.ShuttleClock += dt;
        var from = ChasePosition(instance, shuttle);
        var away = AwayFrom(from, shuttle.Position);
        float step = (float)(HunterShuttleFleeSpeed * dt);
        shuttle.Position = new Vector3f(shuttle.Position.X + away.X * step, shuttle.Position.Y + away.Y * step, shuttle.Position.Z + away.Z * step);
        FaceCourse(shuttle, away.X, away.Z);
        if (shuttle.ShuttleClock >= HunterShuttleFleeSeconds
            || shuttle.Position.DistanceSquared(from) >= HunterShuttleWarpOutDistance * HunterShuttleWarpOutDistance)
        {
            LandHunterShuttle(instance, shuttle);
        }

        return true;
    }

    /// <summary>It got away: the warp flash, gone from the flight scene (no explosion — it was not destroyed), and a camp on
    /// the planet it picks. If no planet can take it (every one already has an open camp) it is simply gone.</summary>
    private void LandHunterShuttle(SpaceInstance instance, CombatEntity shuttle)
    {
        BroadcastToInstance(instance, new SpaceWarpFx
        {
            X = shuttle.Position.X,
            Y = shuttle.Position.Y,
            Z = shuttle.Position.Z,
            Arriving = false,
            Style = HunterShuttleWarpStyle,
        });
        instance.Entities.Remove(shuttle);
        instance.Structures.Remove(RaiderStructureId(shuttle.Id));
        instance.HunterShuttleId = string.Empty;
        BroadcastSpaceState(instance);

        string body = PickHunterLandingBody(instance);
        bool landed = body.Length > 0 && RegisterHunterSite(body, instance);
        foreach (var s in PilotsIn(instance))
        {
            if (landed)
            {
                SendVegaLine(s, "vega.hunter_shuttle.landed", 2, GalaxyBodyName(body));
            }
            else
            {
                SendVegaLine(s, "vega.hunter_shuttle.gone", 3);
            }
        }

        _log.Info(landed
            ? $"The hunter shuttle from '{instance.Id}' got away and landed on '{body}'."
            : $"The hunter shuttle from '{instance.Id}' got away (no planet to land on).");
    }

    /// <summary>Shot down before it could land (from the space kill path; a no-op for every other kind): its hull goes, the
    /// instance slot frees, and VEGA cheers — no camp from this one.</summary>
    private void OnHunterShuttleKilled(SpaceInstance instance, CombatEntity target)
    {
        if (target.Kind != CombatEntityKind.HunterShuttle)
        {
            return;
        }

        instance.Structures.Remove(RaiderStructureId(target.Id)); // the combat debris was cut from it already
        if (instance.HunterShuttleId == target.Id)
        {
            instance.HunterShuttleId = string.Empty;
        }

        foreach (var s in PilotsIn(instance))
        {
            SendVegaLine(s, "vega.hunter_shuttle.down", 3);
        }
    }

    /// <summary>The joined sessions flying in this instance.</summary>
    private List<PlayerSession> PilotsIn(SpaceInstance instance)
    {
        var list = new List<PlayerSession>();
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } s)
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>The planets and moons of the instance's system a shuttle could land on (see <see cref="HunterCanLandOn"/>).</summary>
    private List<CelestialBody> HunterLandingBodies(SpaceInstance instance)
    {
        var list = new List<CelestialBody>();
        string systemId = SystemIdOfInstance(instance);
        var system = _galaxy?.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system is null)
        {
            return list;
        }

        foreach (var b in system.Bodies)
        {
            if (HunterCanLandOn(b))
            {
                list.Add(b);
            }
        }

        return list;
    }

    /// <summary>A planet or moon with ground and machines: not a station world, not a gas giant, not a world without
    /// machines, not the Guardian's finale body.</summary>
    private bool HunterCanLandOn(CelestialBody b)
        => b.Kind is CelestialKind.Planet or CelestialKind.Moon
           && _content.GetPlanet(b.PlanetType ?? string.Empty) is { Void: false, IsGasWorld: false } type
           && type.EnemyDensity > 0.0
           && !IsGuardianSystemLocation(b.Id);

    /// <summary>Where a fleeing shuttle lands: the body the pilot flies at (the instance's own) if it can, else one where a
    /// pilot here has a base, else any — never a body that already has an open camp. Empty when none is left.</summary>
    private string PickHunterLandingBody(SpaceInstance instance)
    {
        var open = HunterLandingBodies(instance).Where(b => !HunterSiteOpenOn(b.Id)).ToList();
        if (open.Count == 0)
        {
            return string.Empty;
        }

        string here = InstanceBodyId(instance);
        var pick = open.FirstOrDefault(b => b.Id == here)
                   ?? open.FirstOrDefault(b => _bases.Any(x => x.Planet == b.Id && instance.Players.Contains(x.OwnerId)))
                   ?? open[_hunterShuttleRng.Next(open.Count)];
        return pick.Id;
    }

    /// <summary>The shuttle's voxel hull for the flight view (<see cref="HunterShuttleHull"/>), dyed in its livery.</summary>
    private SpaceStructure BuildHunterShuttleSpaceHull(string structureId)
    {
        var s = new SpaceStructure
        {
            Id = structureId,
            Kind = "ship",
            Width = HunterShuttleHull.Width,
            Height = HunterShuttleHull.Height,
            Length = HunterShuttleHull.Length,
        };
        foreach (var (cell, part) in HunterShuttleHull.Cells)
        {
            if (HunterShuttleBlock(part) is { } def)
            {
                s.Set(cell, def.NumericId, def.Tintable ? HunterShuttleHull.DyeOf(part) : 0, 0, 0);
            }
        }

        return s;
    }

    /// <summary>The existing block a hull part is made of (with fallbacks for a minimal test content), or null — the same
    /// mapping the camp's ground stamp uses (<see cref="HunterShuttleHull.BlockOf"/>).</summary>
    private BlockDefinition? HunterShuttleBlock(HunterShuttleHull.Part part) => HunterShuttleHull.BlockOf(_content, part);

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: warps a hunter shuttle into the player's flight instance now (bypasses the roll and the gates).</summary>
    public void SpawnHunterShuttleForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            instance.HunterShuttleRolled = true;
            SpawnHunterShuttle(instance);
        }
    }

    /// <summary>Test/util: the live hunter shuttle in the player's instance (null = none).</summary>
    public CombatEntity? HunterShuttleForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance)
            ? instance.Entities.FirstOrDefault(e => e.Kind == CombatEntityKind.HunterShuttle)
            : null;

    /// <summary>Test/util: advances only the hunter-shuttle logic of the player's instance.</summary>
    public void TickHunterShuttlesForTest(string playerId, double dt)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            TickHunterShuttles(instance, dt);
        }
    }

    /// <summary>Test/util: rolls the player's instance afresh and reports whether a shuttle is now on its way.</summary>
    public bool RollHunterShuttleForTest(string playerId)
    {
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance))
        {
            return false;
        }

        RollHunterShuttle(instance);
        return instance.HunterShuttleAt > 0;
    }

    /// <summary>Test/util: whether hunter shuttles may appear under the current rules.</summary>
    public bool HunterShuttlesAllowedForTest => HunterShuttlesAllowed;
}
