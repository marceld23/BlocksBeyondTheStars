// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class PilotSim
{
    /// <summary>#3753: uptime until which hostile fire misses this pilot — the barrel roll's evade window.</summary>
    public double EvadeUntil { get; set; }

    /// <summary>#3753: uptime from which the next barrel roll is granted.</summary>
    public double RollReadyAt { get; set; }

    /// <summary>#3754: the boost charge the server owns (a field: the meter is a struct that ticks in place).</summary>
    public ShipBoostMeter Boost;

    /// <summary>#3754: the pilot holds the boost (the last <c>ShipManeuverIntent</c> said so).</summary>
    public bool BoostWanted { get; set; }

    /// <summary>#3754: uptime the boost last went off — the speed check keeps the boosted allowance a moment longer,
    /// while the ship coasts down.</summary>
    public double BoostOffAt { get; set; } = double.NegativeInfinity;

    /// <summary>The flown ship's manoeuvre values and speed factor, refreshed every tick from the pilot's ship.</summary>
    public ShipManeuverStats Stats { get; set; } = ShipManeuverStats.Default;

    public float FlightSpeed { get; set; } = 1f;

    /// <summary>False until the pilot's ship has been read once (the charge starts full).</summary>
    public bool StatsRead { get; set; }

    /// <summary>#3754: the last accepted ship pose and when it came — the baseline of the speed check.</summary>
    public Vector3f PosePos { get; set; }

    public double PoseAt { get; set; }

    public bool PoseSet { get; set; }

    /// <summary>#3758: uptime from which the point-defence turret may fire again.</summary>
    public double TurretReadyAt { get; set; }
}

public sealed partial class CombatEntity
{
    /// <summary>#3756: uptime at which this hostile starts charging its next heavy shot (0 = not scheduled yet).</summary>
    public double HeavyNextAt { get; set; }

    /// <summary>#3756: uptime at which the charging heavy shot fires; 0 = not charging.</summary>
    public double HeavyFireAt { get; set; }

    /// <summary>#3756: the pilot the charging heavy shot is aimed at.</summary>
    public string HeavyTargetId { get; set; } = string.Empty;

    /// <summary>True once this hostile has had its quarry in range for a heavy shot. The first contact starts the wait
    /// over — a hostile that had been in the system for a while was already due and charged the moment a pilot arrived.</summary>
    public bool HeavyContact { get; set; }

    /// <summary>#3756: the damage of this hostile's heavy shot and the seconds between two of them (from its tier);
    /// 0 = it has none.</summary>
    public float HeavyDamage { get; set; }

    public float HeavyInterval { get; set; }

    /// <summary>#3900: seconds this hostile's heavy shot charges (0 = <see cref="SpaceCombatRules.HeavyChargeSeconds"/>),
    /// how close its quarry must be for the charge to start (0 = the engage range), the most of the target's full hull
    /// one hit takes (0 = no cap) and the style the client draws — from its tier, like the damage.</summary>
    public float HeavyCharge { get; set; }

    public float HeavyRange { get; set; }

    public float HeavyHullCap { get; set; }

    public string HeavyStyle { get; set; } = string.Empty;

    /// <summary>#3756: which way round this hostile circles its quarry (+1 / -1; 0 = not picked yet).</summary>
    public int OrbitSign { get; set; }
}

/// <summary>
/// Space combat that moves (#3762): the pilot's two manoeuvres and what they matter against.
/// <list type="bullet">
/// <item><b>Barrel roll</b> (#3753) — a <see cref="ShipManeuverIntent"/> opens an evade window
/// (<see cref="ShipManeuverRules.RollEvadeSeconds"/>) in which hostile fire and a heavy shot miss the pilot; the server
/// keeps the cooldown, which comes from the ship's handling and its modules.</item>
/// <item><b>Boost</b> (#3754) — the server runs the pilot's <see cref="ShipBoostMeter"/>; the charge decides how far a
/// reported pose may be from the last one.</item>
/// <item><b>Speed check</b> (#3754) — <see cref="PlausibleShipPose"/> pulls a pose that is further away than the ship
/// can have flown back to what is plausible. Switched on by the server host (<see cref="ShipSpeedCheck"/>).</item>
/// <item><b>Circling hostiles</b> and the <b>heavy shot</b> (#3756) — a hostile that holds its stand-off ring circles
/// its quarry; the cruiser and the raider charge a heavy shot that a roll at the right moment makes miss.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3754: whether reported ship poses are checked against the ship's speed. The server host turns it on;
    /// a server embedded in a test stays off unless the test is about the check, because tests place ships by hand.</summary>
    public bool ShipSpeedCheck { get; set; }

    // Derived from the cursor ship in RecomputeShipCombatStats, like hull and shield.
    private ShipManeuverStats _shipManeuver = ShipManeuverStats.Default;
    private float _shipFlightSpeed = 1f;
    private float _shipWeaponDamage = 1f;

    /// <summary>The cursor ship's manoeuvre values, speed factor and weapon damage factor: from its design
    /// (<c>data/ships.json</c>), from its geometry for a self-built ship (#949), from its file for a user ship — with
    /// the modules' three manoeuvre stats, already summed by the caller.</summary>
    private void RecomputeShipManeuverStats(ShipDefinition? design, float boostSeconds, float recharge, float rollCut)
    {
        float handling = design?.Handling ?? 1f;
        float boostPower = design?.BoostPower ?? 0f;
        float boostBase = design?.BoostSeconds ?? 0f;
        _shipFlightSpeed = design?.FlightSpeed ?? 1f;
        _shipWeaponDamage = design is { WeaponDamage: > 0f } ? design.WeaponDamage : 1f;
        if (_ship.IsCustom)
        {
            var custom = CustomShipStatsFor(_ship);
            _shipFlightSpeed = custom.FlightSpeed;
            handling = custom.Handling;
            ShipManeuverRules.CustomBoost(custom.FlightSpeed, out boostPower, out boostBase);
        }
        else if (design is null && _content.UserShipByKey(_ship.ShipType) is { } user)
        {
            _shipFlightSpeed = user.Definition.FlightSpeed;
            handling = user.Definition.Handling;
        }

        _shipManeuver = ShipManeuverRules.Stats(boostPower, boostBase, design?.RollCooldown ?? 0f, handling, boostSeconds, recharge, rollCut);
    }

    /// <summary>Test/inspection: the cursor ship's manoeuvre values.</summary>
    public ShipManeuverStats ShipManeuverStatsForTest => _shipManeuver;

    /// <summary>Test/inspection: the cursor ship's weapon damage factor.</summary>
    public float ShipWeaponDamageForTest => _shipWeaponDamage;

    private void HandleShipManeuver(PlayerSession session, ShipManeuverIntent intent)
        => ShipManeuver(session.State.PlayerId, intent.Kind, intent.On);

    /// <summary>The pilot asks for a manoeuvre. A roll is granted when the pilot flies a ship (not a suit) and the
    /// cooldown has passed; the boost is a wish the meter follows while the charge lasts. Returns whether a roll was
    /// granted (always false for the boost).</summary>
    public bool ShipManeuver(string playerId, byte kind, bool on = false)
    {
        if (!_playerInstance.TryGetValue(playerId, out var instanceId)
            || !_spaceInstances.TryGetValue(instanceId, out var instance)
            || !instance.PilotSims.TryGetValue(playerId, out var sim)
            || FindSessionByPlayerId(playerId) is not { } pilot
            || pilot.State.InEva)
        {
            return false;
        }

        ReadPilotShip(pilot, sim);
        if (kind == ShipManeuverKinds.Boost)
        {
            sim.BoostWanted = on;
            return false;
        }

        if (kind != ShipManeuverKinds.Roll || _uptime < sim.RollReadyAt - ShipManeuverRules.RollCooldownSlack)
        {
            return false;
        }

        // The cooldown counts from when the roll was DUE, not from a grant the slack let through early: otherwise every
        // roll came a quarter of a second sooner than the last and the real period was the cooldown minus the slack.
        sim.RollReadyAt = System.Math.Max(_uptime, sim.RollReadyAt) + sim.Stats.RollCooldown;
        sim.EvadeUntil = _uptime + ShipManeuverRules.RollEvadeSeconds;

        // The other pilots see the roll: a cosmetic effect at the ship, its direction in the target's X (+1 right, -1 left).
        var at = PilotPositionIn(instance, playerId);
        RelayFxInSpace(pilot, FxActionKinds.Roll, string.Empty, at, new Vector3f(at.X + (on ? 1f : -1f), at.Y, at.Z), hit: false);
        RaiseGameEvent(pilot, GameEventKeys.ShipRolled); // #3831
        return true;
    }

    /// <summary>Points the ship cursor at the pilot and copies what the manoeuvres need into the pilot's tick state.</summary>
    private void ReadPilotShip(PlayerSession pilot, PilotSim sim)
    {
        SetCurrent(pilot);
        sim.Stats = _shipManeuver;
        sim.FlightSpeed = _shipFlightSpeed;
        if (!sim.StatsRead)
        {
            sim.StatsRead = true;
            sim.Boost.Fill(sim.Stats);
        }
    }

    /// <summary>One tick of a pilot's manoeuvre state (the ship cursor is already on the pilot): the boost charge
    /// drains or refills, and a boost that ran dry is over until the pilot asks again. Returns true in the tick the
    /// boost came on.</summary>
    private bool TickPilotManeuvers(PilotSim sim, double dt)
    {
        sim.Stats = _shipManeuver;
        sim.FlightSpeed = _shipFlightSpeed;
        if (!sim.StatsRead)
        {
            sim.StatsRead = true;
            sim.Boost.Fill(sim.Stats);
        }

        bool was = sim.Boost.Active;
        sim.Boost.Tick((float)dt, sim.BoostWanted, sim.Stats);
        if (was && !sim.Boost.Active)
        {
            sim.BoostOffAt = _uptime;
            sim.BoostWanted = false; // ran dry (or let go): a new boost needs a new press
        }

        return !was && sim.Boost.Active;
    }

    /// <summary>Whether hostile fire misses this pilot right now (#3753).</summary>
    private bool IsEvading(PilotSim sim) => _uptime < sim.EvadeUntil;

    /// <summary>Test seams (#3753/#3754): the pilot's evade window, boost charge and whether the boost is on.</summary>
    public bool IsEvadingForTest(string playerId) => PilotSimOf(playerId) is { } sim && IsEvading(sim);

    public float BoostChargeForTest(string playerId) => PilotSimOf(playerId)?.Boost.Charge ?? 0f;

    public bool BoostActiveForTest(string playerId) => PilotSimOf(playerId)?.Boost.Active ?? false;

    private PilotSim? PilotSimOf(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var inst)
           && inst.PilotSims.TryGetValue(playerId, out var sim)
            ? sim
            : null;

    private const double BoostCoastSeconds = 1.0;   // the boosted allowance outlives the boost while the ship coasts down
    private const double SpeedLogInterval = 30.0;
    private readonly Dictionary<string, double> _speedLoggedAt = new();

    /// <summary>#3754: the pose the server accepts for a flown ship. With <see cref="ShipSpeedCheck"/> on, a pose
    /// further from the last accepted one than the ship can have flown (<see cref="ShipManeuverRules.AllowedTravel"/>)
    /// is pulled back onto that distance, toward the reported point — so a ship that really is somewhere else (a move
    /// the check does not know) is caught up with at flying speed instead of being stuck, and a faster client gains
    /// nothing. A suit on a spacewalk is not checked, and stepping back into the ship starts a fresh baseline.</summary>
    private Vector3f PlausibleShipPose(SpaceInstance instance, string playerId, Vector3f reported, bool eva)
    {
        if (!instance.PilotSims.TryGetValue(playerId, out var sim))
        {
            return reported;
        }

        if (eva || !ShipSpeedCheck)
        {
            sim.PoseSet = false;
            return reported;
        }

        if (sim.PoseSet)
        {
            bool boosted = sim.Boost.Active || _uptime - sim.BoostOffAt < BoostCoastSeconds;
            float allowed = ShipManeuverRules.AllowedTravel(sim.FlightSpeed, boosted ? sim.Stats.BoostPower : 1f, _uptime - sim.PoseAt);
            float dist = (float)System.Math.Sqrt(reported.DistanceSquared(sim.PosePos));
            if (dist > allowed)
            {
                float k = allowed / dist;
                reported = new Vector3f(
                    sim.PosePos.X + ((reported.X - sim.PosePos.X) * k),
                    sim.PosePos.Y + ((reported.Y - sim.PosePos.Y) * k),
                    sim.PosePos.Z + ((reported.Z - sim.PosePos.Z) * k));
                if (!_speedLoggedAt.TryGetValue(playerId, out double at) || _uptime - at > SpeedLogInterval)
                {
                    _speedLoggedAt[playerId] = _uptime;
                    _log.Warn($"Ship pose of '{playerId}' was {dist:0} units from the last one, {allowed:0} are plausible — held back (instance '{instance.Id}').");
                }
            }
        }

        sim.PosePos = reported;
        sim.PoseAt = _uptime;
        sim.PoseSet = true;
        return reported;
    }

    // ---------------- Hostiles that circle (#3756) ----------------

    /// <summary>A hostile that holds its stand-off ring circles its quarry instead of standing still: sideways around
    /// the pilot at a share of its speed, keeping its distance, with a slow rise and fall so a pack does not fly one
    /// flat ring. The direction is fixed per entity.</summary>
    private Vector3f OrbitStep(CombatEntity e, Vector3f quarry, float speed, double dt)
    {
        if (e.OrbitSign == 0)
        {
            e.OrbitSign = (BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash(e.Id) & 1) == 0 ? 1 : -1;
        }

        float rx = e.Position.X - quarry.X;
        float ry = e.Position.Y - quarry.Y;
        float rz = e.Position.Z - quarry.Z;
        float flat = (float)System.Math.Sqrt((rx * rx) + (rz * rz));
        float dist = (float)System.Math.Sqrt((rx * rx) + (ry * ry) + (rz * rz));
        if (flat < 0.5f || dist < 0.5f)
        {
            return e.Position; // straight above or below the quarry: no side to circle to this tick
        }

        float step = speed * SpaceCombatRules.OrbitSpeedShare * (float)dt * e.OrbitSign;
        float nx = rx + (-rz / flat * step);
        float nz = rz + (rx / flat * step);
        float ny = ry + ((float)System.Math.Sin((_uptime * 0.6) + e.PatrolPhase) * speed * 0.12f * (float)dt);
        float len = (float)System.Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        float k = len > 0.001f ? dist / len : 1f; // back onto the ring it held
        FaceCourse(e, -nx, -nz);                  // a hull that turns (the raider) keeps its nose on the quarry
        return new Vector3f(quarry.X + (nx * k), quarry.Y + (ny * k), quarry.Z + (nz * k));
    }

    // ---------------- The heavy shot (#3756) ----------------

    /// <summary>The heavy shots of the instance's hostiles: a hostile whose tier has one (the cruiser, the raider)
    /// charges it at the pilot it hunts once that pilot is inside the engage range, and fires
    /// <see cref="SpaceCombatRules.HeavyChargeSeconds"/> later. It misses a pilot who is inside a barrel roll's evade
    /// window at that moment, or who has flown clear. Returns true when a ship was lost and the instance closed.</summary>
    private bool TickHeavyShots(SpaceInstance instance)
    {
        for (int i = 0; i < instance.Entities.Count; i++)
        {
            var e = instance.Entities[i];
            if (!e.Hostile || e.Hull <= 0f || e.HeavyDamage <= 0f)
            {
                continue;
            }

            if (e.HeavyFireAt > 0)
            {
                if (HeavyChargeLapsed(e))
                {
                    continue;
                }

                if (_uptime >= e.HeavyFireAt && FireHeavyShot(instance, e))
                {
                    return true;
                }

                continue;
            }

            if (IsStaggered(e) || ShuttleFleeing(e))
            {
                continue;
            }

            if (e.HeavyNextAt <= 0)
            {
                e.HeavyNextAt = _uptime + (e.HeavyInterval * 0.5); // the first one comes a little into the fight
                continue;
            }

            bool inRange = HeavyTargetInRange(instance, e, 1f, out string targetId);
            if (!inRange || HeavyFirstContact(e) || _uptime < e.HeavyNextAt)
            {
                continue;
            }

            e.HeavyTargetId = targetId;
            float charge = HeavyChargeOf(e);
            e.HeavyFireAt = _uptime + charge;
            BroadcastToInstance(instance, HeavyShotMessage(e, targetId, SpaceCombatRules.HeavyPhaseCharge, charge,
                PilotPositionIn(instance, targetId)));
        }

        return false;
    }

    /// <summary>Seconds this hostile's heavy shot charges: its tier's, else the classic warning.</summary>
    private static float HeavyChargeOf(CombatEntity e)
        => e.HeavyCharge > 0f ? e.HeavyCharge : SpaceCombatRules.HeavyChargeSeconds;

    /// <summary>How close its quarry must be for this hostile's heavy shot to charge: its tier's range, else the engage range.</summary>
    private static float HeavyRangeOf(CombatEntity e) => e.HeavyRange > 0f ? e.HeavyRange : ShipEngageRange;

    /// <summary>One <see cref="SpaceHeavyShot"/> of this hostile: the phase, where it comes from (the hostile, now) and
    /// where it points (#3900 — the aiming line and the beam are drawn from these).</summary>
    private static SpaceHeavyShot HeavyShotMessage(CombatEntity e, string targetId, byte phase, float seconds, Vector3f aim,
        bool blocked = false)
        => new()
        {
            EntityId = e.Id,
            TargetPlayerId = targetId,
            Phase = phase,
            Seconds = seconds,
            Style = e.HeavyStyle,
            OriginX = e.Position.X,
            OriginY = e.Position.Y,
            OriginZ = e.Position.Z,
            TargetX = aim.X,
            TargetY = aim.Y,
            TargetZ = aim.Z,
            Range = HeavyRangeOf(e) * SpaceCombatRules.HeavyReachFactor,
            Blocked = blocked,
        };

    /// <summary>How long after its due time a charged heavy shot still fires. A flight instance without a pilot at the
    /// helm is not ticked, so a charge can be found long overdue when somebody takes the helm again.</summary>
    private const double HeavyOverdueSeconds = 1.0;

    /// <summary>A charge that is overdue by more than a moment was never ticked — its pilot left the helm mid-charge.
    /// Fired now it would land a heavy shot nobody was warned of, so it lapses and the next one comes after the usual
    /// wait. True when it lapsed.</summary>
    private bool HeavyChargeLapsed(CombatEntity e)
    {
        if (_uptime - e.HeavyFireAt <= HeavyOverdueSeconds)
        {
            return false;
        }

        e.HeavyFireAt = 0;
        e.HeavyTargetId = string.Empty;
        e.HeavyNextAt = _uptime + (e.HeavyInterval * 0.5);
        return true;
    }

    /// <summary>The first time a hostile's quarry is in range the wait for its first heavy shot starts over (see
    /// <see cref="CombatEntity.HeavyContact"/>). True on that first contact — no charge yet.</summary>
    private bool HeavyFirstContact(CombatEntity e)
    {
        if (e.HeavyContact)
        {
            return false;
        }

        e.HeavyContact = true;
        e.HeavyNextAt = _uptime + (e.HeavyInterval * 0.5);
        return true;
    }

    /// <summary>Whether the pilot this hostile hunts can be hurt and is within the engage range times
    /// <paramref name="reach"/> of it.</summary>
    private bool HeavyTargetInRange(SpaceInstance instance, CombatEntity e, float reach, out string targetId)
    {
        targetId = e.HeavyFireAt > 0 ? e.HeavyTargetId : e.ChaseTargetId;
        if (targetId.Length == 0 || !instance.Players.Contains(targetId)
            || !instance.PlayerPoses.TryGetValue(targetId, out var pose) || !SpaceQuarry(targetId))
        {
            return false;
        }

        float range = HeavyRangeOf(e) * reach;
        return pose.Pos.DistanceSquared(e.Position) <= range * range;
    }

    /// <summary>#3900: whether a rock stands in the way of a mega laser (a heavy shot with a style): an asteroid whose
    /// centre lies within <see cref="SpaceCombatRules.HeavyCoverRadius"/> (times its scale) of the line from the hostile
    /// to its target, between the two. The classic heavy shot goes through as before. <paramref name="at"/> is the
    /// rock's position — where the beam ends.</summary>
    private static bool HeavyShotBlocked(SpaceInstance instance, CombatEntity e, Vector3f target, out Vector3f at)
    {
        at = target;
        if (e.HeavyStyle.Length == 0)
        {
            return false;
        }

        float lx = target.X - e.Position.X, ly = target.Y - e.Position.Y, lz = target.Z - e.Position.Z;
        float lenSq = (lx * lx) + (ly * ly) + (lz * lz);
        if (lenSq < 1f)
        {
            return false;
        }

        foreach (var rock in instance.Entities)
        {
            if (rock.Kind != CombatEntityKind.Asteroid || rock.Hull <= 0f)
            {
                continue;
            }

            float rx = rock.Position.X - e.Position.X, ry = rock.Position.Y - e.Position.Y, rz = rock.Position.Z - e.Position.Z;
            float t = ((rx * lx) + (ry * ly) + (rz * lz)) / lenSq;
            if (t <= 0.05f || t >= 0.98f)
            {
                continue; // behind the hostile, or behind (or at) the target
            }

            float px = rx - (lx * t), py = ry - (ly * t), pz = rz - (lz * t);
            float radius = SpaceCombatRules.HeavyCoverRadius * System.Math.Max(1f, rock.Scale);
            if ((px * px) + (py * py) + (pz * pz) <= radius * radius)
            {
                at = rock.Position;
                return true;
            }
        }

        return false;
    }

    /// <summary>#3900: a capped heavy shot lands on the cursor ship: the shield first, then the hull — at most
    /// <paramref name="maxHullShare"/> of the full hull, and never the last hull point of a ship above a quarter of its
    /// hull (<see cref="SpaceCombatRules.HeavyHullDamage"/>). VEGA's evasive manoeuvre (Mk3) can still dodge it all.</summary>
    private void ApplyCappedShipDamage(float amount, float maxHullShare)
    {
        if (VegaTryEvade())
        {
            return;
        }

        float toShield = System.Math.Min(_ship.Shield, amount);
        _ship.Shield -= toShield;
        float onHull = SpaceCombatRules.HeavyHullDamage(amount - toShield, _ship.Hull, _shipHullMax, maxHullShare);
        _ship.Hull = System.Math.Max(0f, _ship.Hull - onHull);
    }

    /// <summary>The charged shot fires: a hit on the hunted pilot's ship, or a miss. Returns true when the hit
    /// destroyed the ship (the instance is closed then).</summary>
    private bool FireHeavyShot(SpaceInstance instance, CombatEntity e)
    {
        // The pilot the charge was aimed at — read before the charge is cleared, or the range check would look at
        // whoever the hostile hunts by now.
        bool inReach = HeavyTargetInRange(instance, e, SpaceCombatRules.HeavyReachFactor, out string targetId);
        e.HeavyFireAt = 0;
        e.HeavyNextAt = _uptime + e.HeavyInterval;
        var pilot = inReach ? FindSessionByPlayerId(targetId) : null;
        PilotSim? sim = null;
        bool onTarget = pilot is not null && instance.PilotSims.TryGetValue(targetId, out sim) && !IsStaggered(e);
        // #3900: the aimed pilot's ship as far as it is known (it may have left the flight) — where the beam points.
        var aim = e.HeavyTargetId.Length > 0 && instance.PlayerPoses.TryGetValue(e.HeavyTargetId, out var aimed) ? aimed.Pos : e.Position;
        bool blocked = onTarget && HeavyShotBlocked(instance, e, aim, out aim); // a rock in the mega laser's way
        bool hit = onTarget && !blocked && sim is not null && !IsEvading(sim);
        if (onTarget && !blocked && !hit)
        {
            RaiseGameEvent(pilot, GameEventKeys.ShipEvaded, GameEventKeys.EvadedHeavy); // #3831: only the roll made it miss
        }

        BroadcastToInstance(instance, HeavyShotMessage(e, e.HeavyTargetId,
            hit ? SpaceCombatRules.HeavyPhaseHit : SpaceCombatRules.HeavyPhaseMiss, 0f, aim, blocked));
        e.HeavyTargetId = string.Empty;
        if (!hit || pilot is null)
        {
            return false;
        }

        SetCurrent(pilot); // the damage below must land on THIS pilot's ship
        if (e.HeavyHullCap > 0f)
        {
            ApplyCappedShipDamage(e.HeavyDamage, e.HeavyHullCap); // #3900: the mega laser never ends a flight in one hit
        }
        else
        {
            ApplyShipDamage(e.HeavyDamage);
        }

        if (_ship.Hull <= 0f)
        {
            DisableShip(instance);
            return true;
        }

        SendShipCombatStatus(pilot);
        return false;
    }

    /// <summary>Test seam: puts a hostile of the given enemy into the player's flight instance.</summary>
    public CombatEntity? SpawnSpaceHostileForTest(string playerId, string enemyKey, CombatEntityKind kind, Vector3f at)
    {
        if (!_playerInstance.TryGetValue(playerId, out var id) || !_spaceInstances.TryGetValue(id, out var instance))
        {
            return null;
        }

        var entity = NewEnemy(enemyKey, EnemyTiers.Normal, kind, at);
        instance.Entities.Add(entity);
        return entity;
    }

    /// <summary>Test seam (#3754): the ship pose the server holds for the player.</summary>
    public Vector3f? ShipPoseForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var inst)
           && inst.PlayerPoses.TryGetValue(playerId, out var pose)
            ? pose.Pos
            : null;

    /// <summary>Test seam (#3756): seconds until the entity's charging heavy shot fires; 0 = it is not charging.</summary>
    public double HeavyChargeLeftForTest(string playerId, string entityId)
    {
        foreach (var e in SpaceEntitiesFor(playerId))
        {
            if (e.Id == entityId)
            {
                return e.HeavyFireAt > 0 ? System.Math.Max(0.0001, e.HeavyFireAt - _uptime) : 0.0;
            }
        }

        return 0.0;
    }
}
