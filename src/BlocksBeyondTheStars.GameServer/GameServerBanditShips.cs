// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Bandit ships in space: in seeded "pirate" systems a raider may warp in mid-flight, close on the
/// player's ship and hail it with a cargo demand (the same protocol as the lone robbers on foot,
/// drawn from inventory + cargo hold). Comply and it warps out for good; refuse, stall, or open fire
/// and it fights with the normal hostile AI. Spawns are hard-gated on rules that let the player shoot
/// back — an unkillable extortionist would repeat the old UFO-kill bug, so no fight the player can't
/// win is ever started.
/// </summary>
public sealed partial class GameServer
{
    private const float BanditShipHull = 55f;
    private const float BanditShipDps = 5f;
    private const float BanditHailRange = 60f;       // it closes to conversation range before demanding
    private const float BanditShipApproachSpeed = 9f;
    private const float BanditShipLeaveSpeed = 12f;
    private const float BanditWarpInDistance = 220f; // warps in well outside engage range — the approach is the warning
    private const float BanditWarpOutDistance = 320f;
    private const double BanditAmbushDelayMin = 30.0; // seconds after entry before the ambush can drop in
    private const double BanditAmbushDelayMax = 90.0;

    /// <summary>Whether bandit ships may operate at all: bandits on, survival, space combat live, ship
    /// weapons usable against NPCs, and the galaxy not yet pacified. Every leg of this keeps the player
    /// ABLE to fight back — the hard lesson from the unkillable-UFO bug.</summary>
    private bool BanditShipsAllowed => BanditsActive
        && Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both
        && Rules.ShipWeapons is not (ShipWeaponMode.Off or ShipWeaponMode.MiningOnly)
        && !_storyState.GuardianDefeated;

    /// <summary>Deterministic "pirate space" flag per star system (trader-traffic pattern): roughly a
    /// quarter of all systems, always the same ones for a given save.</summary>
    private bool BanditSystem(string systemId)
        => !string.IsNullOrEmpty(systemId)
           && (int)(((ulong)(_meta.Seed ^ WorldGenerator.StableHash("banditspace:" + systemId))) % 100) < 25;

    private string SystemIdOfInstance(SpaceInstance instance)
    {
        string locationId = instance.Id.StartsWith("space:", System.StringComparison.Ordinal)
            ? instance.Id.Substring(6)
            : instance.Id;
        return _galaxy.FindBody(locationId)?.SystemId ?? string.Empty;
    }

    private void TickBanditShips(SpaceInstance instance, double dt)
    {
        // Roll the ambush dice exactly once per instance (i.e. per flight from this body).
        if (!instance.BanditRolled)
        {
            instance.BanditRolled = true;
            instance.BanditAmbushAt = 0;
            if (BanditShipsAllowed && BanditSystem(SystemIdOfInstance(instance)))
            {
                double chance = Rules.Bandits switch
                {
                    AlienActivity.Rare => 0.15,
                    AlienActivity.Normal => 0.30,
                    AlienActivity.Frequent => 0.50,
                    AlienActivity.Extreme => 0.70,
                    _ => 0.0,
                };
                if (_banditRng.NextDouble() < chance)
                {
                    instance.BanditAmbushAt = _uptime + BanditAmbushDelayMin
                        + _banditRng.NextDouble() * (BanditAmbushDelayMax - BanditAmbushDelayMin);
                }
            }
        }

        // The delayed warp-in: reads as an ambush, and leaves room for VEGA's sector warning to land first.
        if (instance.BanditAmbushAt > 0 && _uptime >= instance.BanditAmbushAt && instance.BanditShipId.Length == 0)
        {
            instance.BanditAmbushAt = 0;
            if (BanditShipsAllowed)
            {
                SpawnBanditShip(instance);
            }
        }

        if (instance.BanditShipId.Length == 0)
        {
            return;
        }

        var raider = instance.Entities.FirstOrDefault(e => e.Id == instance.BanditShipId);
        if (raider is null)
        {
            instance.BanditShipId = string.Empty;
            return;
        }

        var mark = FirstPilotIn(instance);

        switch (raider.BanditPhase)
        {
            case BanditPhase.Approach:
                {
                    if (mark is null)
                    {
                        BeginBanditShipLeave(instance, raider);
                        break;
                    }

                    float distSq = raider.Position.DistanceSquared(instance.ShipPosition);
                    if (distSq <= BanditHailRange * BanditHailRange)
                    {
                        BeginBanditShipDemand(instance, raider, mark);
                        break;
                    }

                    StepToward(raider, instance.ShipPosition, BanditShipApproachSpeed, dt, stopAt: BanditHailRange * 0.9f);
                    break;
                }

            case BanditPhase.Demanding:
                {
                    if (mark is null || mark.BanditDemandId == 0 || !mark.BanditDemandFromShip)
                    {
                        // The pilot vanished (landed/disconnected) mid-hail — call it off.
                        BeginBanditShipLeave(instance, raider);
                        break;
                    }

                    if (_uptime > mark.BanditDemandDeadline)
                    {
                        ResolveBanditShipDemand(mark, comply: false, outcome: "expired");
                    }

                    break; // holds position while the ultimatum runs
                }

            case BanditPhase.Fighting:
                // MoveSpaceHostiles + the engage-range damage aura handle a hostile raider like any UFO.
                break;

            case BanditPhase.Leaving:
                {
                    var away = AwayFrom(instance.ShipPosition, raider.Position);
                    raider.Position = new Vector3f(
                        raider.Position.X + away.X * (float)(BanditShipLeaveSpeed * dt),
                        raider.Position.Y + away.Y * (float)(BanditShipLeaveSpeed * dt),
                        raider.Position.Z + away.Z * (float)(BanditShipLeaveSpeed * dt));
                    if (raider.Position.DistanceSquared(instance.ShipPosition) >= BanditWarpOutDistance * BanditWarpOutDistance)
                    {
                        BroadcastWarpFx(instance, raider.Position, arriving: false);
                        instance.Entities.Remove(raider);
                        instance.BanditShipId = string.Empty;
                        BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = raider.Id });
                        BroadcastSpaceState(instance);
                    }

                    break;
                }
        }
    }

    private PlayerSession? FirstPilotIn(SpaceInstance instance)
    {
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is { } s && s.Joined)
            {
                return s;
            }
        }

        return null;
    }

    private void SpawnBanditShip(SpaceInstance instance)
    {
        // Warp in on a random bearing well outside engage range, roughly at ship height.
        double ang = _banditRng.NextDouble() * System.Math.PI * 2.0;
        var pos = new Vector3f(
            instance.ShipPosition.X + (float)System.Math.Cos(ang) * BanditWarpInDistance,
            instance.ShipPosition.Y + (float)(_banditRng.NextDouble() * 20.0 - 10.0),
            instance.ShipPosition.Z + (float)System.Math.Sin(ang) * BanditWarpInDistance);

        var raider = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.BanditShip,
            Name = NameGenerator.Person(_banditRng),
            Hostile = false, // it talks first
            Hull = BanditShipHull,
            HullMax = BanditShipHull,
            Position = pos,
            DamagePerSecond = BanditShipDps,
            BanditPhase = BanditPhase.Approach,
            Loot = { new ItemAmount("data_fragment", 2), new ItemAmount("titanium_plate", 2) },
        };
        instance.Entities.Add(raider);
        instance.BanditShipId = raider.Id;
        BroadcastWarpFx(instance, pos, arriving: true);
        BroadcastSpaceState(instance);
    }

    private void BeginBanditShipDemand(SpaceInstance instance, CombatEntity raider, PlayerSession mark)
    {
        if (mark.BanditDemandId != 0)
        {
            return; // some other hold-up is already on this player's screen — wait a tick
        }

        SetCurrent(mark); // pin the ship cursor so the demand sees THIS pilot's cargo hold
        var demand = BuildBanditDemand(mark.State, includeCargo: true);
        if (demand.Count == 0)
        {
            // Empty hold, empty pockets: not worth the fuel. It scans you and warps off.
            BeginBanditShipLeave(instance, raider);
            return;
        }

        raider.BanditPhase = BanditPhase.Demanding;
        raider.BanditTargetId = mark.State.PlayerId;
        StartBanditDemand(mark, raider.Id, demand, fromShip: true, banditName: raider.Name,
            lineKey: "bandit.line.hail" + (System.Math.Abs(StableStringHash(raider.Id)) % 3 + 1));
        SendVegaLine(mark, "vega.sys.bandit_hail", 3);
    }

    /// <summary>Resolves the pending SHIP demand for this player (comply/refuse/expired).</summary>
    private void ResolveBanditShipDemand(PlayerSession session, bool comply, string? outcome = null)
    {
        if (!_playerInstance.TryGetValue(session.State.PlayerId, out var instanceId)
            || !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            ClearBanditDemand(session);
            return;
        }

        var raider = instance.Entities.FirstOrDefault(e => e.Id == session.BanditDemandBanditId);
        if (raider is null)
        {
            ClearBanditDemand(session);
            return;
        }

        if (comply)
        {
            SetCurrent(session); // the payment comes out of THIS pilot's inventory + cargo
            TakeBanditPayment(session, raider, includeCargo: true);
            BeginBanditShipLeave(instance, raider);
        }
        else
        {
            raider.Hostile = true;
            raider.BanditPhase = BanditPhase.Fighting;
        }

        SendBanditResult(session, outcome ?? (comply ? "paid" : "refused"));
        ClearBanditDemand(session);
        BroadcastSpaceState(instance);
    }

    private void BeginBanditShipLeave(SpaceInstance instance, CombatEntity raider)
    {
        raider.BanditPhase = BanditPhase.Leaving;
        raider.Hostile = false;
        BroadcastSpaceState(instance);
    }

    /// <summary>Shooting the hailing raider IS an answer: the hold-up resolves as refused, the fight is on.</summary>
    private void OnBanditShipAttacked(SpaceInstance instance, CombatEntity raider)
    {
        var mark = raider.BanditTargetId.Length > 0 ? FindSessionByPlayerId(raider.BanditTargetId) : FirstPilotIn(instance);
        if (mark is not null && mark.BanditDemandId != 0 && mark.BanditDemandBanditId == raider.Id)
        {
            SendBanditResult(mark, "refused");
            ClearBanditDemand(mark);
        }

        raider.Hostile = true;
        raider.BanditPhase = BanditPhase.Fighting;
    }

    /// <summary>The raider blew up: close any open hold-up UI and free the instance slot.</summary>
    private void OnBanditShipKilled(SpaceInstance instance, CombatEntity raider)
    {
        var mark = raider.BanditTargetId.Length > 0 ? FindSessionByPlayerId(raider.BanditTargetId) : FirstPilotIn(instance);
        if (mark is not null && mark.BanditDemandId != 0 && mark.BanditDemandBanditId == raider.Id)
        {
            SendBanditResult(mark, "fled");
            ClearBanditDemand(mark);
        }

        if (instance.BanditShipId == raider.Id)
        {
            instance.BanditShipId = string.Empty;
        }
    }

    private static void StepToward(CombatEntity e, Vector3f target, float speed, double dt, float stopAt)
    {
        float dx = target.X - e.Position.X;
        float dy = target.Y - e.Position.Y;
        float dz = target.Z - e.Position.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist <= stopAt || dist < 0.001f)
        {
            return;
        }

        float step = System.Math.Min((float)(speed * dt), dist - stopAt);
        e.Position = new Vector3f(e.Position.X + dx / dist * step, e.Position.Y + dy / dist * step, e.Position.Z + dz / dist * step);
    }

    private static Vector3f AwayFrom(Vector3f from, Vector3f pos)
    {
        float dx = pos.X - from.X;
        float dy = pos.Y - from.Y;
        float dz = pos.Z - from.Z;
        float len = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        return len < 0.001f ? new Vector3f(1f, 0f, 0f) : new Vector3f(dx / len, dy / len, dz / len);
    }

    // ---------------- VEGA pre-briefing (education BEFORE the first encounter) ----------------

    private const string BanditBriefMilestone = "vega:hint:bandit_brief";

    /// <summary>Called on entering space: VEGA warns about pirate space BEFORE any raider appears — the
    /// very first time with a full briefing (what bandits want, that paying is a safe option), afterwards
    /// with a short per-entry sector warning. Never fires where bandit ships can't spawn anyway.</summary>
    private void ShipAiBanditSectorWarning(PlayerSession session, SpaceInstance instance)
    {
        if (!BanditShipsAllowed || !BanditSystem(SystemIdOfInstance(instance)))
        {
            return;
        }

        var p = session.State;
        if (!p.Milestones.Contains(BanditBriefMilestone))
        {
            p.Milestones.Add(BanditBriefMilestone);
            SendVegaLine(session, "vega.brief.bandits", 1);
        }
        else
        {
            SendVegaLine(session, "vega.sys.bandit_sector", 3);
        }
    }

    /// <summary>Called from the ground tick, once per world per session: VEGA warns a landing player that
    /// this world has bandit activity (camp scans / robber reports) BEFORE anyone walks up to them. The
    /// first-ever warning uses the full briefing so the child knows the rules of a hold-up in advance.</summary>
    private void ShipAiBanditGroundWarning(PlayerSession session, bool hasCamps)
    {
        if (!session.BanditBriefedWorlds.Add(_world.LocationId))
        {
            return; // this world was already announced this session
        }

        var p = session.State;
        if (!p.Milestones.Contains(BanditBriefMilestone))
        {
            p.Milestones.Add(BanditBriefMilestone);
            SendVegaLine(session, "vega.brief.bandits", 1);
        }
        else
        {
            SendVegaLine(session, hasCamps ? "vega.sys.bandit_camp_near" : "vega.sys.bandit_region", 1);
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: whether a system id rolls as pirate space for this save.</summary>
    public bool BanditSystemForTest(string systemId) => BanditSystem(systemId);

    /// <summary>Test/util: force-spawn the bandit ship ambush in the player's current instance.</summary>
    public void SpawnBanditShipForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            SpawnBanditShip(instance);
        }
    }

    /// <summary>Test/util: the live bandit-ship entity in the player's instance (null = none).</summary>
    public CombatEntity? BanditShipForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance)
            ? instance.Entities.FirstOrDefault(e => e.Kind == CombatEntityKind.BanditShip)
            : null;

    /// <summary>Test/util: advance only the bandit-ship logic of the player's instance.</summary>
    public void TickBanditShipsForTest(string playerId, double dt)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            TickBanditShips(instance, dt);
        }
    }
}
