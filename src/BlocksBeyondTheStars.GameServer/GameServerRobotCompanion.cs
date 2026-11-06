// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3295: the friendly hunter robot's runtime state on its live entity (server-only, never persisted).</summary>
public sealed partial class CombatEntity
{
    /// <summary>The hostile the robot last shot at — the fight it started (empty = none).</summary>
    public string RobotTargetId { get; set; } = string.Empty;

    /// <summary>Uptime of the robot's last shot.</summary>
    public double RobotLastShotAt { get; set; } = double.MinValue;

    /// <summary>Uptime of the robot's last moment in a fight (a shot, or a hit taken) — it heals ten seconds after.</summary>
    public double RobotLastFightAt { get; set; } = double.MinValue;

    /// <summary>Uptime of the robot's last guard pass; negative before the first.</summary>
    public double RobotTickAt { get; set; } = -1.0;
}

/// <summary>
/// The friendly hunter robot (#3295, Justus' idea): the finale's robot core plus titanium ore at the workshop make the
/// <c>hunter_robot</c> item; used on the ground it switches the robot on — a companion on the tamed-companion system
/// (follow AI, leash, nameplate, rename, fetch, bandit ward) with a fixed robot species of the <see cref="CreatureBodyPlan.Robot"/>
/// plan (the client draws the hunter's armour with light-blue lights).
/// <list type="bullet">
/// <item><b>One per player</b>, refused in space, aboard and on a station; "power down" (the Companions tab's release)
/// puts the item back.</item>
/// <item><b>Follows its owner to every world</b> (<see cref="TamedCreature.Roams"/>) — but never onto a void (station)
/// world. Its bond is fixed at 100; it does not eat and cannot be scanned or bio-sampled.</item>
/// <item><b>Fights gently</b>, like a base sentry that walks along (<see cref="NearestSentryTarget"/>): only hostile machines
/// and creatures and robbers who already fight — never players, NPCs, traders or companions; it waits while a robber talks
/// and does nothing on a Creative or Peaceful world.</item>
/// <item><b>Knock-out only</b>: only in a fight it started does it take the attacked hostile's damage (inside its own pass —
/// the enemy AI is unchanged); ten seconds without a fight heal it. At 0 it breaks apart in sparks and an owner-locked crate
/// with the robot core and the recipe's other inputs appears (the same delivery as the finale reward, never lost) — one
/// craft rebuilds it.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The robot's item.</summary>
    internal const string HunterRobotItem = "hunter_robot";

    /// <summary>The robot's fixed species id — not a roster species, so it never collides with "sp0".."sp8".</summary>
    internal const string RobotSpeciesId = "robot_hunter";

    /// <summary>The name a freshly built robot answers to (the player renames it like any companion).</summary>
    private const string RobotDefaultName = "Robo";

    /// <summary>The robot's health — a few close fights in a row knock it out, one rarely does.</summary>
    private const float RobotMaxHull = 100f;

    /// <summary>Damage per shot — the base sentry's.</summary>
    private const float RobotShotDamage = SentryDamage;

    /// <summary>The guard pass runs at 2 Hz per world, like the sentries.</summary>
    private const double RobotGuardInterval = 0.5;

    /// <summary>Seconds without a fight after which a hurt robot is whole again.</summary>
    internal const double RobotHealAfterSeconds = 10.0;

    /// <summary>A fight the robot started ends when it has not shot for this long.</summary>
    private const double RobotFightMemorySeconds = 3.0;

    /// <summary>The attacked hostile hurts the robot only while it is this close (it hits what is near it).</summary>
    private const float RobotHitBackRange = 8f;

    /// <summary>Where the robot's shot leaves its body (its chest), above its feet.</summary>
    private const float RobotMuzzleHeight = 1.2f;

    /// <summary>The robot's standing size (the biped share rules turn it into ~1.7 blocks of height).</summary>
    private const float RobotSize = 2f;

    /// <summary>Per world: the uptime of its next guard pass.</summary>
    private readonly Dictionary<string, double> _robotGuardNextAt = new(StringComparer.Ordinal);

    internal static bool IsRobotCompanion(TamedCreature tc) => tc.Kind == TamedCreature.RobotKind;

    private static bool IsRobotEntity(CombatEntity c) => c.IsCompanion && c.SpeciesId == RobotSpeciesId;

    /// <summary>The robot's fixed species — built in code, so every robot looks and moves the same on every world.</summary>
    internal static CreatureSpecies RobotSpecies() => new()
    {
        Id = RobotSpeciesId,
        NameKey = "creature.robot.name",
        Name = RobotDefaultName,
        Habitat = CreatureHabitat.Land,
        Activity = CreatureActivity.Cathemeral, // never sleeps
        Temperament = CreatureTemperament.Passive,
        LocoStyle = LocomotionStyle.Strider,
        Size = RobotSize,
        MaxHealth = RobotMaxHull,
        Speed = 4.5f,
        Legs = 2,
        Arms = 2,
        HeadRatio = 0.75f,
        BodySegments = 1,
        ColorRgb = 0x8C94A3,  // the hunter's plating
        BellyRgb = 0x3A3F4B,  // its dark armour
        EyeRgb = 0x7FE8FF,    // light blue — the bandits' blue is #4dadff, the Guardian's red never
        Eyes = 0,             // one wide visor instead of eyes (the client draws it)
        Glows = true,
        BodyPlan = CreatureBodyPlan.Robot,
        VoiceSeed = 0x7FE8FF,
    };

    /// <summary>Whether a companion appears on <paramref name="body"/> while its owner is there: an animal on its home world,
    /// the robot everywhere but a void world — and never one that is knocked out.</summary>
    private bool CompanionLivesOn(TamedCreature tc, string body)
        => !tc.KnockedOut && (tc.Roams ? !_world.Planet.Void : tc.HomeBodyId == body);

    private static string CompanionDisplayName(TamedCreature tc) => string.IsNullOrEmpty(tc.Name) ? tc.Species.Name : tc.Name;

    /// <summary>What a knocked-out robot leaves in its crate: the inputs of the recipe that builds it (data-driven — the
    /// robot core plus the rest), so one craft rebuilds it; just the core when no recipe makes it.</summary>
    private List<ItemStack> RobotPartItems()
    {
        var recipe = _content.Recipes.Values.FirstOrDefault(r => r.Outputs.Any(o => o.Item == HunterRobotItem));
        return recipe is null
            ? new List<ItemStack> { new ItemStack(RobotCoreItem, 1) }
            : recipe.Inputs.Select(i => new ItemStack(i.Item, i.Count)).ToList();
    }

    /// <summary>Whether the player stands inside their own parked ship.</summary>
    private bool AboardParkedShip(PlayerState p)
        => p.AboardShip && _worlds.Active.LandedShips.TryGetValue(p.PlayerId, out var landed) && landed.Placed;

    // ---------------- Switch on / power down ----------------

    /// <summary>The <c>hunter_robot</c> gadget: switches the robot on beside the player. Refused (the item kept) in space, on a
    /// station, aboard or in a ship interior, and while the player already has a robot. Returns true when it came to life.</summary>
    private bool DeployRobot(PlayerSession session)
    {
        var p = session.State;
        string? refusal = InSpace(p.PlayerId) || InStation(p.PlayerId) || _world.Planet.Void ? "@srv.robot.surface_only"
            : AboardParkedShip(p) || InShipInterior(p.PlayerId) ? "@srv.robot.step_outside"
            : p.TamedCreatures.Any(IsRobotCompanion) ? "@srv.robot.already_active"
            : !p.Inventory.Has(HunterRobotItem, 1) ? "@srv.gadget.missing"
            : null;
        if (refusal is not null)
        {
            Reject(session, "gadget", refusal);
            return false;
        }

        long now = NowUnixMs();
        var tc = new TamedCreature
        {
            Id = "robot_" + Guid.NewGuid().ToString("N").Substring(0, 12),
            HomeBodyId = _world.LocationId,
            Name = RobotDefaultName,
            SpeciesId = RobotSpeciesId,
            Species = RobotSpecies(),
            SizeScale = 1f,
            Bond = 100,
            TamedAtUtc = now,
            LastFedUtc = now,
            Kind = TamedCreature.RobotKind,
            Roams = true,
        };
        _repo.RunInTransaction(() =>
        {
            p.Inventory.Remove(HunterRobotItem, 1);
            p.TamedCreatures.Add(tc);
            _repo.SavePlayer(p);
        });
        SpawnCompanionEntity(p.PlayerId, tc, p.Position);
        SendInventory(session);
        SendCompanions(session);
        BroadcastCreatures();
        Send(session, new ServerMessage { Text = "@srv.robot.deployed:" + tc.Name });
        return true;
    }

    /// <summary>"Release" on the robot powers it down: the robot leaves the world and the <c>hunter_robot</c> item comes back
    /// into the backpack — refused while the backpack is full, and while a knocked-out robot's crate is still on its way.</summary>
    private void PowerDownRobot(PlayerSession session, TamedCreature tc)
    {
        var p = session.State;
        if (tc.KnockedOut)
        {
            Reject(session, "companion", "@srv.robot.knocked_out:" + CompanionDisplayName(tc));
            return;
        }

        if (p.Inventory.Clone().Add(HunterRobotItem, 1, MaxStackOf(HunterRobotItem)) > 0)
        {
            Reject(session, "companion", "@inventory_full");
            return;
        }

        _repo.RunInTransaction(() =>
        {
            p.Inventory.Add(HunterRobotItem, 1, MaxStackOf(HunterRobotItem));
            p.TamedCreatures.Remove(tc);
            _repo.SavePlayer(p);
        });
        _creatures.RemoveAll(c => c.CompanionId == tc.Id);
        SendInventory(session);
        BroadcastCreatures();
        SendCompanions(session);
        Send(session, new ServerMessage { Text = "@srv.robot.powered_down:" + CompanionDisplayName(tc) });
    }

    // ---------------- The guard pass ----------------

    /// <summary>2 Hz per world: every robot whose owner is here takes the damage of the fight it is in, heals once the fight
    /// is over, falls when it reaches 0, and otherwise fires at the nearest hostile a sentry would shoot.</summary>
    private void TickRobotCompanions()
    {
        string world = _world.LocationId;
        if (_creatures.Count == 0 || (_robotGuardNextAt.TryGetValue(world, out double next) && _uptime < next))
        {
            return;
        }

        _robotGuardNextAt[world] = _uptime + RobotGuardInterval;
        bool creatures = false, enemies = false;
        foreach (var robot in _creatures.Where(IsRobotEntity).ToList())
        {
            if (CompanionOwnerHere(robot) is not { } owner)
            {
                continue;
            }

            var (c, e) = GuardRobot(robot, owner);
            creatures |= c;
            enemies |= e;
        }

        if (enemies)
        {
            BroadcastPlanetEnemies();
        }

        if (creatures)
        {
            BroadcastCreatures(); // the robot's health bar, a shot animal's, a knock-out
        }
    }

    /// <summary>One robot's pass. Returns which lists changed.</summary>
    private (bool Creatures, bool Enemies) GuardRobot(CombatEntity robot, PlayerSession owner)
    {
        double dt = robot.RobotTickAt < 0 ? 0 : Math.Min(1.0, _uptime - robot.RobotTickAt);
        robot.RobotTickAt = _uptime;
        bool creatures = RobotTakesFightDamage(robot, dt);
        if (robot.Hull <= 0f)
        {
            KnockOutRobot(robot, owner);
            return (true, false);
        }

        if (robot.Hull < robot.HullMax && _uptime - robot.RobotLastFightAt >= RobotHealAfterSeconds)
        {
            robot.Hull = robot.HullMax; // ten quiet seconds: whole again
            creatures = true;
        }

        if (!SentriesActive || robot.FrozenTimer > 0 || IsStaggered(robot))
        {
            return (creatures, false); // Creative / Peaceful: no shooting on either side
        }

        var (c, e) = RobotFires(robot, owner);
        return (creatures || c, e);
    }

    /// <summary>The fight the robot started hurts it: while it has shot within the last few seconds and the attacked hostile
    /// is alive and close, the robot loses that hostile's damage per second. Nothing else ever hurts a companion.</summary>
    private bool RobotTakesFightDamage(CombatEntity robot, double dt)
    {
        if (robot.RobotTargetId.Length == 0)
        {
            return false;
        }

        var target = FindRobotTarget(robot.RobotTargetId);
        if (target is null || target.Hull <= 0f || _uptime - robot.RobotLastShotAt > RobotFightMemorySeconds)
        {
            robot.RobotTargetId = string.Empty; // the fight is over
            return false;
        }

        if (dt <= 0 || target.DamagePerSecond <= 0f || WrapDistSq(target.Position, robot.Position) > RobotHitBackRange * RobotHitBackRange)
        {
            return false;
        }

        robot.Hull = Math.Max(0f, robot.Hull - (float)(target.DamagePerSecond * dt));
        robot.RobotLastFightAt = _uptime;
        return true;
    }

    private CombatEntity? FindRobotTarget(string id)
        => _planetEnemies.FirstOrDefault(e => e.Id == id)
           ?? _bandits.FirstOrDefault(b => b.Id == id)
           ?? _creatures.FirstOrDefault(c => c.Id == id);

    /// <summary>One shot at the nearest hostile a sentry would shoot (the same gentle rules), credited to the owner.</summary>
    private (bool Creatures, bool Enemies) RobotFires(CombatEntity robot, PlayerSession owner)
    {
        var muzzle = new Vector3f(robot.Position.X, robot.Position.Y + RobotMuzzleHeight, robot.Position.Z);
        if (NearestSentryTarget(muzzle) is not { } target)
        {
            return (false, false);
        }

        bool isCreature = _creatures.Contains(target);
        BroadcastToWorld(new SentryShot { X = muzzle.X, Y = muzzle.Y, Z = muzzle.Z, TargetId = target.Id }); // the cyan tracer
        target.Hull -= RobotShotDamage;
        robot.RobotTargetId = target.Id;
        robot.RobotLastShotAt = _uptime;
        robot.RobotLastFightAt = _uptime;
        if (target.Hull > 0f)
        {
            if (isCreature)
            {
                target.AwakeOverrideTimer = CreatureWakeSeconds; // nothing sleeps through being shot at
            }

            return (isCreature, !isCreature);
        }

        robot.RobotTargetId = string.Empty;
        KillBySentry(target, robot.Position.ToBlock(), owner.State.PlayerId, "Robot '" + robot.CustomName + "'");
        return (isCreature, !isCreature);
    }

    /// <summary>The robot is knocked out: it breaks apart in sparks and leaves the world; its record waits as
    /// <see cref="TamedCreature.KnockedOut"/> until its parts' owner-locked crate is out — right where it fell when there is a
    /// free spot, else in front of the owner by the delivery pass (with the backpack fallback).</summary>
    private void KnockOutRobot(CombatEntity robot, PlayerSession owner)
    {
        var p = owner.State;
        BroadcastCreatureDefeated(robot); // the client breaks it apart — sparks, not a despawn
        _creatures.Remove(robot);
        if (p.TamedCreatures.FirstOrDefault(t => t.Id == robot.CompanionId) is not { } tc)
        {
            return;
        }

        tc.KnockedOut = true;
        _repo.SavePlayer(p);
        string name = CompanionDisplayName(tc);
        Send(owner, new ServerMessage { Text = "@srv.robot.knocked_out:" + name });
        if (TryFindOwnerCrateSpot(robot.Position, 0f, p.PlayerId, out var cell)
            && PlaceOwnerCrate(owner, cell, RobotPartItems(), () => p.TamedCreatures.Remove(tc)))
        {
            SendVegaLine(owner, "vega.robot.parts_crate", 3, name);
        }

        SendCompanions(owner);
        _log.Info($"Robot '{name}' of '{p.Name}' was knocked out.");
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: uses the robot item as the gadget intent would (energy and cooldown aside). True = switched on.</summary>
    public bool DeployRobotForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && DeployRobot(s);

    /// <summary>Test seam: runs the robots' guard pass on the active world right now, skipping its 2 Hz gate.</summary>
    public void TickRobotCompanionsForTest()
    {
        _robotGuardNextAt.Remove(_world.LocationId);
        TickRobotCompanions();
    }

    /// <summary>Test seam: the live robot entity of a player on the active world, or null.</summary>
    public CombatEntity? RobotEntityForTest(string playerId)
        => _creatures.FirstOrDefault(c => IsRobotEntity(c) && c.OwnerId == playerId);
}
