// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The tank spider (#3096, Justus: "a mechanical spider with a tank instead of a hump — when you get too close its tank
/// inflates and it explodes, without destroying blocks, only with damage"). A walking Guardian machine that carries a
/// pressure tank where a spider has its abdomen. It hunts like the robots (sight-gated, around walls, out of deep water)
/// but skitters, and it never bites: once a player it may hurt comes within <see cref="TankSpiderRules.TriggerRange"/> it
/// stops and its tank swells for <see cref="TankSpiderRules.InflateSeconds"/> — the wire's <c>Inflating</c>, the client's
/// hiss and glow, the warning to run — and then it bursts. The burst (<c>WorldFx</c> <see cref="TankSpiderRules.BurstFx"/>)
/// hurts every such player within <see cref="TankSpiderRules.BurstRadius"/> whom no wall shelters from the tank — less
/// toward the edge — the client pushes its own player clear, and no block ever changes (the colossus stomp's rule, not the
/// terrain blaster's). The spider is gone with its burst and leaves no loot; shot before it bursts it is an ordinary defeat
/// that only pops (loot, story credit). It never comes among the first machines of a visit — only as a refill, one at a time
/// per world — so a new player meets the robots first.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Blocks/s while it hunts: quicker than a Guardian robot (3.1), still slower than a running player.</summary>
    private const float TankSpiderHuntSpeed = 3.4f;

    /// <summary>Its gait: a light machine on eight legs — quick to start and to turn, short pauses, a little weave.</summary>
    private static readonly LocomotionProfile TankSpiderProfile = new()
    {
        Style = LocomotionStyle.Prowler,
        CruiseSpeed = EnemyWanderSpeed * 1.2f,
        BurstSpeed = TankSpiderHuntSpeed,
        Accel = 6.0f,
        TurnRate = 3.2f,
        HoldMin = 2.0f,
        HoldMax = 5.0f,
        PauseChance = 0.4f,
        PauseMin = 0.5f,
        PauseMax = 1.5f,
        WeaveAmp = 0.18f,
        WeaveFreq = 1.6f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    /// <summary>Test seam: replaces <see cref="TankSpiderRules.RefillShare"/> (1 = every refill that may be one is one).</summary>
    internal double? TankSpiderShareForTest { get; set; }

    /// <summary>A tank spider at <paramref name="at"/> with <c>data/enemies.json</c>'s hull, loot and burst.</summary>
    private CombatEntity NewTankSpider(Vector3f at)
    {
        var spider = NewEnemy(EnemyCatalog.TankSpider, EnemyTiers.Normal, CombatEntityKind.TankSpider, at);
        spider.BurstDamage = _content.Enemy(EnemyCatalog.TankSpider).Tier(EnemyTiers.Normal).BurstDamage;
        return spider;
    }

    /// <summary>Whether this planet-machine spawn walks in as a tank spider: only once the world's first machines are out
    /// (a refill, never the quick start of a visit), only while none is about, and then for a stable share of the refills.</summary>
    private bool TankSpiderTurn()
    {
        if (!_worlds.Active.EnemyCapSeen)
        {
            return false;
        }

        foreach (var e in _planetEnemies)
        {
            if (e.Kind == CombatEntityKind.TankSpider)
            {
                return false;
            }
        }

        double roll = TankSpiderRoll(_worlds.Active.LocationId, _worlds.Active.EnemySpawnOrdinal, _meta.Seed);
        return TankSpiderShareForTest is { } share ? roll < share : TankSpiderRules.PicksSpider(roll);
    }

    /// <summary>The stable random (0..1) of one spawn ordinal of a world that decides whether it is a tank spider.</summary>
    private static double TankSpiderRoll(string locationId, int ordinal, long seed)
        => StableHashing.Unit($"tank-spider:{locationId}:{ordinal}", seed);

    /// <summary>Starts the tank swelling when a player it may hurt — not cloaked, god-moded, in Sandbox or warded by a
    /// companion — stands within reach and in sight of it. From then on nothing stops the fuse but its defeat.</summary>
    private void ArmTankSpider(CombatEntity spider, List<PlayerSession> targets, HashSet<string> warded)
    {
        if (spider.BurstAt > 0)
        {
            return;
        }

        foreach (var s in targets)
        {
            var p = s.State;
            if (HostilesIgnore(p) || warded.Contains(p.PlayerId))
            {
                continue;
            }

            var un = Unwrapped(spider.Position, p.Position);
            if (TankSpiderRules.InTriggerReach(un.X - spider.Position.X, un.Y - spider.Position.Y, un.Z - spider.Position.Z)
                && HasLineOfSight(spider.Position, p.Position))
            {
                spider.BurstAt = _uptime + TankSpiderRules.InflateSeconds;
                BroadcastPlanetEnemies(); // the client starts the swelling, the hiss and the glow now
                return;
            }
        }
    }

    /// <summary>Bursts every tank spider whose fuse has run out. Returns true when a burst carried a victim to another world
    /// (the active world changed under the tick).</summary>
    private bool BurstDueTankSpiders()
    {
        var world = _worlds.Active;
        for (int i = _planetEnemies.Count - 1; i >= 0; i--)
        {
            var e = _planetEnemies[i];
            if (e.Kind != CombatEntityKind.TankSpider || e.BurstAt <= 0 || _uptime < e.BurstAt)
            {
                continue;
            }

            _planetEnemies.RemoveAt(i);
            _enemyWander.Remove(e.Id);
            _enemySightSeenAt.Remove(e.Id);
            ForgetWadePlan(e.Id);
            BroadcastToWorld(new PlanetEnemyDefeated { Id = e.Id }); // the body breaks apart with the burst
            BroadcastPlanetEnemies();
            _enemySpawnTimer = System.Math.Min(_enemySpawnTimer, -EnemyKillSpawnGrace); // a breather, as after a defeat
            BurstTankSpider(e);
            if (!ReferenceEquals(_worlds.Active, world))
            {
                return true;
            }

            i = System.Math.Min(i, _planetEnemies.Count); // stay in range should a recovery ever have changed the list
        }

        return false;
    }

    /// <summary>The burst: the flash, smoke and push go to every client of the world; the damage, falling off from the
    /// centre, to every player it may hurt within <see cref="TankSpiderRules.BurstRadius"/> and in the tank's sight — a
    /// wall between them is cover. Nobody aboard or in space is touched, and no block changes.</summary>
    private void BurstTankSpider(CombatEntity spider)
    {
        var at = spider.Position;
        BroadcastToWorld(new WorldFx { Kind = TankSpiderRules.BurstFx, X = at.X, Y = at.Y, Z = at.Z, Strength = 1f, Radius = TankSpiderRules.BurstRadius });
        var world = _worlds.Active;
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State;
            if (p.AboardShip || InSpace(p.PlayerId) || HostilesIgnore(p) || WardedByCompanion(p))
            {
                continue;
            }

            var un = Unwrapped(at, p.Position);
            float dx = un.X - at.X, dy = un.Y - at.Y, dz = un.Z - at.Z;
            float amount = TankSpiderRules.DamageAt(spider.BurstDamage, (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
            if (amount <= 0f || !HasLineOfSight(at, p.Position))
            {
                continue;
            }

            HurtPlayer(s, spider, at, amount, "@srv.death.tank_spider");
            if (!ReferenceEquals(_worlds.Active, world))
            {
                return; // the burst knocked the victim out and the recovery took them to another world
            }
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: a tank spider at <paramref name="at"/> (with the data's stats), returned.</summary>
    public CombatEntity SpawnTankSpiderAtForTest(Vector3f at)
    {
        var spider = NewTankSpider(at);
        _planetEnemies.Add(spider);
        return spider;
    }

    /// <summary>Test seam: one walking-machine spawn near <paramref name="player"/> as a tank spider, by the spawner's rules.</summary>
    internal bool SpawnTankSpiderNearForTest(Shared.State.PlayerState player) => SpawnPlanetEnemyNear(player, asDrone: false, asTankSpider: true);

    /// <summary>Test seam: takes a planet machine away (as if it had wandered off), freeing its slot for a refill.</summary>
    public void RemovePlanetEnemyForTest(string id) => _planetEnemies.RemoveAll(e => e.Id == id);

    /// <summary>Test seam: the stable random that decides whether a world's spawn ordinal is a tank spider.</summary>
    internal static double TankSpiderRollForTest(string locationId, int ordinal, long seed) => TankSpiderRoll(locationId, ordinal, seed);
}

/// <summary>#3096: a tank spider's fuse and burst.</summary>
public sealed partial class CombatEntity
{
    /// <summary>Server uptime at which a tank spider's swelling tank bursts; 0 = not swelling. On the wire as
    /// <c>NetCombatEntity.Inflating</c>. Server-only runtime state, never persisted.</summary>
    public double BurstAt { get; set; }

    /// <summary>The burst's damage at its centre (from <c>data/enemies.json</c>'s <c>burstDamage</c>).</summary>
    public float BurstDamage { get; set; }
}
