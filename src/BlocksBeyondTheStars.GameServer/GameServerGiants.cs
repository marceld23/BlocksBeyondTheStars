// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The giants (#1998–#2002, the school club's idea, terrain generation 9): the procedural <b>colossus</b> — a 40–60 block
/// quadruped on very flat, very light worlds, very rare — and the <b>sandworm</b> of the sand-sea worlds, which hears
/// vibrations and breaches through the sand. Both are defeatable but very tough, never change a block (effects only),
/// and are solid to the player (client colliders). They live in the creature list for the wire, the hits and the kill
/// path, but spawn, move, attack and leave by the rules here, on the Sreekmakra pattern: one per world (two sandworms on
/// a big one), placed near a player on foot, never far-pruned, a return time in the metadata after a defeat.
/// </summary>
public sealed partial class GameServer
{
    // ---- lifecycle ----
    private const float GiantSpawnMin = 130f, GiantSpawnMax = 210f;     // a landmark on the horizon, not on top of you
    private const float WormSpawnMin = 70f, WormSpawnMax = 170f;
    private const float GiantLeash = 700f;                                // farther from every player → it leaves (and comes back later)
    private const double GiantSpawnCheckSeconds = 2.0;
    private const int GiantReturnDays = 3;
    private const double GiantBroadcastSeconds = 0.5;

    // ---- colossus ----
    private const float ColossusTurnRate = 0.45f;          // rad/s — a giant turns in arcs
    private const float ColossusAggroRange = 140f;
    private const float ColossusFleeRange = 60f;
    private const double StompTelegraphSeconds = 1.4;
    private const double StompCooldownSeconds = 3.5;
    private const float StompRadius = 5.5f;
    private const double ColossusProvokeSeconds = 60.0;

    // ---- sandworm ----
    private const double WormCooldownMin = 10.0, WormCooldownJitter = 6.0;
    private const float WormStartDistance = 4f;            // the head surfaces this close to what it came for
    private const float WormRoamSpeedShare = 0.4f;
    private const double WormRumbleEvery = 1.5;
    private const double WormApproachGiveUp = 30.0;       // a worm that cannot reach what it heard gives up after this
    private const float WormApproachKeep = 0.3f;           // …or when the shaking has faded to this share of the threshold
    private const float WormBodyHitShare = 0.6f;           // a body sweep hurts less than the strike

    /// <summary>The thumper block (#2002).</summary>
    internal const string ThumperBlockKey = "thumper";
    private const double ThumperRunSeconds = 90.0;
    private const double ThumperPulseSeconds = 2.0;

    private GiantWorldState Giants => _worlds.Active.Giants;

    // =====================================================================================================
    // Species: which giants this world hosts (decided once, when the world's creatures are initialised).
    // =====================================================================================================

    /// <summary>Called at the end of <see cref="InitCreatures"/>: rolls this world's giant species (outside the
    /// procedural roster, so the spawner never picks them) and registers them for the wire, the scanner and the hits.</summary>
    private void InitGiants()
    {
        var gs = Giants;
        gs.Clear();
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        int generation = _meta.Description.TerrainGeneration;
        if (GiantRules.HostsColossus(planet, _worlds.Active.GravityFactor, generation, _meta.Seed, _world.LocationId))
        {
            var colossus = CreatureGenerator.GenerateColossus(_meta.Seed, _world.LocationId);
            gs.Colossus = colossus;
            RegisterGiantSpecies(colossus);
        }

        if (GiantRules.HostsSandworms(planet, generation))
        {
            var worm = CreatureGenerator.GenerateSandworm(_meta.Seed, _world.LocationId);
            gs.Sandworm = worm;
            gs.WormCount = GiantRules.SandwormCount(_world.Circumference);
            RegisterGiantSpecies(worm);
        }
    }

    private void RegisterGiantSpecies(CreatureSpecies sp)
    {
        _speciesById[sp.Id] = sp;
        _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
    }

    /// <summary>The live giants of the active world.</summary>
    private IEnumerable<CombatEntity> LiveGiants() => _creatures.Where(c => c.IsGiant);

    private string GiantSlotKey(CreatureBodyPlan kind, int slot)
        => _world.LocationId + "|" + (kind == CreatureBodyPlan.Colossus ? "colossus" : "sandworm" + slot);

    // =====================================================================================================
    // Tick
    // =====================================================================================================

    private void TickGiants(double dt)
    {
        if (_world.Planet is null || _world.Planet.Void)
        {
            return;
        }

        var gs = Giants;
        TickThumpers(dt);
        if (gs.Colossus is null && gs.Sandworm is null)
        {
            return;
        }

        var present = JoinedInActiveWorld().Where(s => !InSpace(s.State.PlayerId)).ToList();
        var onFoot = present.Where(s => !s.State.AboardShip).ToList();
        if (present.Count == 0 || Rules.CreatureAbundance == Shared.Configuration.AlienActivity.Off)
        {
            if (_creatures.RemoveAll(c => c.IsGiant) > 0)
            {
                BroadcastCreatures(); // nobody here (or wildlife off): the giants wander off — they return with the players
            }

            return;
        }

        gs.SpawnCheckIn -= dt;
        if (gs.SpawnCheckIn <= 0 && onFoot.Count > 0)
        {
            gs.SpawnCheckIn = GiantSpawnCheckSeconds;
            TrySpawnGiants(gs, onFoot);
        }

        bool changed = false;
        foreach (var giant in LiveGiants().ToList())
        {
            // Too far from everyone: it walks out of the story — and comes back near a player later.
            if (NearestPlayerPosition(present, giant.Position) is not { } near || WrapDistSq(near, giant.Position) > GiantLeash * GiantLeash)
            {
                _creatures.Remove(giant);
                changed = true;
                continue;
            }

            if (!_speciesById.TryGetValue(giant.SpeciesId, out var sp) || giant.Giant is not { } g)
            {
                continue;
            }

            changed |= g.Kind == CreatureBodyPlan.Colossus
                ? TickColossus(giant, g, sp, onFoot, dt)
                : TickSandworm(giant, g, sp, onFoot, dt);
        }

        gs.BroadcastIn -= dt;
        if (changed || gs.BroadcastIn <= 0)
        {
            gs.BroadcastIn = GiantBroadcastSeconds;
            if (LiveGiants().Any() || changed)
            {
                BroadcastCreatures(); // the regular heartbeat only runs while someone is on foot; a giant keeps moving
            }
        }
    }

    private void TrySpawnGiants(GiantWorldState gs, List<PlayerSession> onFoot)
    {
        long now = NowUnixSeconds;
        if (gs.Colossus is { } colossus && !LiveGiants().Any(c => c.Giant!.Kind == CreatureBodyPlan.Colossus)
            && (!_meta.GiantBackAt.TryGetValue(GiantSlotKey(CreatureBodyPlan.Colossus, 0), out long back) || now >= back))
        {
            TrySpawnColossus(colossus, onFoot);
        }

        if (gs.Sandworm is { } worm)
        {
            for (int slot = 0; slot < gs.WormCount; slot++)
            {
                if (LiveGiants().Any(c => c.Giant!.Kind == CreatureBodyPlan.Sandworm && c.Giant.Slot == slot)
                    || (_meta.GiantBackAt.TryGetValue(GiantSlotKey(CreatureBodyPlan.Sandworm, slot), out long wb) && now < wb))
                {
                    continue;
                }

                TrySpawnSandworm(worm, slot, onFoot);
            }
        }
    }

    private CombatEntity SpawnGiantEntity(CreatureSpecies sp, GiantRuntime g, Vector3f pos)
    {
        var e = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = sp.Hostile ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            SpeciesId = sp.Id,
            Hostile = sp.Hostile,
            Hull = sp.MaxHealth,
            HullMax = sp.MaxHealth,
            Position = pos,
            DamagePerSecond = 0f, // giants hurt by stomps and strikes, never by the proximity aura
            SizeScale = 1f,
            Giant = g,
        };
        e.Loot.Add(new ItemAmount(sp.DropItem, sp.DropCount));
        foreach (var extra in g.Kind == CreatureBodyPlan.Colossus ? ColossusLoot : SandwormLoot)
        {
            e.Loot.Add(extra);
        }

        _creatures.Add(e);
        BroadcastCreatures();
        return e;
    }

    private static readonly ItemAmount[] ColossusLoot =
    {
        new("crystal", 6), new("titanium_plate", 4), new("data_fragment", 2),
    };

    private static readonly ItemAmount[] SandwormLoot =
    {
        new("crystal", 8), new("silicate", 12), new("data_fragment", 2),
    };

    // =====================================================================================================
    // Colossus (#1999)
    // =====================================================================================================

    private bool TrySpawnColossus(CreatureSpecies sp, List<PlayerSession> onFoot)
    {
        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("colossus-spawn:" + _world.LocationId) ^ (long)_uptime)));
        var near = onFoot[rng.Next(onFoot.Count)].State.Position;
        var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = GiantSpawnMin + (float)rng.NextDouble() * (GiantSpawnMax - GiantSpawnMin);
            float x = near.X + (float)System.Math.Cos(angle) * dist;
            float z = near.Z + (float)System.Math.Sin(angle) * dist;
            float facing = (float)(rng.NextDouble() * System.Math.PI * 2.0);
            if (!ColossusFootprintClear(body, x, z, facing, out float groundY))
            {
                continue;
            }

            var g = new GiantRuntime { Kind = CreatureBodyPlan.Colossus, Facing = facing, Phase = "stand", PhaseStart = _uptime };
            SpawnGiantEntity(sp, g, new Vector3f(x, groundY, z));
            foreach (var s in onFoot)
            {
                SendVegaLine(s, "vega.sys.colossus_sighted", 3);
            }

            _log.Info($"A colossus ({sp.Name}, {sp.GiantHeight:0} blocks, {sp.Temperament}) walks on '{_world.LocationId}'.");
            return true;
        }

        return false;
    }

    /// <summary>Whether a colossus may stand with its centre at (x, z) facing <paramref name="facing"/>: every foot and
    /// the centre on dry, even ground, clear of settlements and player bases. <paramref name="groundY"/> is the mean
    /// ground under its feet — the body rides on the legs, the feet find the real ground on the client.</summary>
    private bool ColossusFootprintClear(ColossusBody body, float x, float z, float facing, out float groundY)
    {
        groundY = 0f;
        var planet = _world.Planet;
        int sea = _generator.SeaLevel(planet);
        var centre = new Vector3f(x, 0f, z);
        int lo = int.MaxValue, hi = int.MinValue;
        float sum = 0f;
        for (int leg = -1; leg < 4; leg++)
        {
            var (hx, _, hz) = leg < 0 ? (0f, 0f, 0f) : body.Hip(leg);
            var p = ColossusBody.ToWorld(centre, facing, hx, 0f, hz);
            int gx = (int)System.Math.Floor(p.X), gz = (int)System.Math.Floor(p.Z);
            int h = _generator.SurfaceHeight(planet, gx, gz);
            if (sea != int.MinValue && h < sea + 1)
            {
                return false; // no wading into a sea or a lake
            }

            lo = System.Math.Min(lo, h);
            hi = System.Math.Max(hi, h);
            sum += h;
        }

        if (hi - lo > body.LegLength * 0.35f)
        {
            return false; // too steep for its legs to span
        }

        if (NearSettlementOrBase(x, z, body.TorsoLength * 0.6f + 12f))
        {
            return false;
        }

        groundY = sum / 5f + 1f;
        return true;
    }

    private bool NearSettlementOrBase(float x, float z, float margin)
    {
        foreach (var s in _settlements)
        {
            if (x >= s.Min.X - margin && x <= s.Max.X + margin && z >= s.Min.Z - margin && z <= s.Max.Z + margin)
            {
                return true;
            }
        }

        foreach (var b in _bases)
        {
            if (b.Planet == _world.LocationId
                && WrapDistSq(new Vector3f(b.Cell.X, 0f, b.Cell.Z), new Vector3f(x, 0f, z)) <= (margin + 24f) * (margin + 24f))
            {
                return true;
            }
        }

        return false;
    }

    private bool TickColossus(CombatEntity e, GiantRuntime g, CreatureSpecies sp, List<PlayerSession> onFoot, double dt)
    {
        var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
        if (e.ProvokeTimer > 0)
        {
            e.ProvokeTimer = System.Math.Max(0, e.ProvokeTimer - dt);
        }

        // A stomp in progress: the body stands, the foot comes down at the announced time.
        if (g.Phase == "stomp")
        {
            if (_uptime >= g.PhaseStart + StompTelegraphSeconds)
            {
                LandStomp(e, g, sp);
                g.Phase = "stand";
                g.PhaseStart = _uptime;
                g.NextStompAt = _uptime + StompCooldownSeconds;
                return true;
            }

            return false;
        }

        bool hunts = PlanetEnemiesActive && (sp.Temperament == CreatureTemperament.Aggressive
            || (e.ProvokeTimer > 0 && sp.Temperament is CreatureTemperament.Territorial or CreatureTemperament.Aggressive));
        PlayerSession? prey = null;
        if (hunts)
        {
            prey = NearestTarget(onFoot, e.Position, ColossusAggroRange);
        }

        // Within a foot's reach of the prey: announce a stomp.
        if (prey is not null && _uptime >= g.NextStompAt)
        {
            int leg = NearestHip(body, e.Position, g.Facing, prey.State.Position, out var hipGround);
            float reach = body.FootReach + 2f;
            var preyFlat = new Vector3f(prey.State.Position.X, hipGround.Y, prey.State.Position.Z);
            if (WrapDistSq(hipGround, preyFlat) <= reach * reach)
            {
                var target = ClampToReach(hipGround, prey.State.Position, body.FootReach);
                g.Phase = "stomp";
                g.PhaseStart = _uptime;
                g.StompLeg = leg;
                g.StompAt = target;
                return true;
            }
        }

        // Where to go: the prey, away from a crowd (skittish, or a passive giant that was hurt), or its own route.
        Vector3f? goal = null;
        bool flee = (sp.Temperament == CreatureTemperament.Skittish || (sp.Temperament == CreatureTemperament.Passive && e.ProvokeTimer > 0))
                    && NearestTarget(onFoot, e.Position, ColossusFleeRange) is not null;
        if (prey is not null)
        {
            goal = prey.State.Position;
        }
        else if (flee && NearestPlayerPosition(onFoot, e.Position) is { } threat)
        {
            var away = Unwrapped(threat, e.Position);
            goal = new Vector3f(e.Position.X + (away.X - threat.X), e.Position.Y, e.Position.Z + (away.Z - threat.Z));
        }
        else
        {
            if (!g.HasRoute || _uptime >= g.RouteUntil || WrapDistSq(g.Route, e.Position) < 20f * 20f)
            {
                PickColossusRoute(e, g);
            }

            if (g.GrazeUntil > _uptime)
            {
                return SetPhase(g, "stand"); // grazing on a treetop
            }

            goal = g.Route;
        }

        // Turn toward the goal (arcs, never on the spot), then step if the ground ahead allows it.
        var to = Unwrapped(e.Position, goal.Value);
        float want = (float)System.Math.Atan2(to.Z - e.Position.Z, to.X - e.Position.X);
        g.Facing = TurnToward(g.Facing, want, ColossusTurnRate * (float)dt);
        float speed = sp.Speed * (prey is not null ? 1.1f : flee ? 1.2f : 1f);
        float step = speed * (float)dt;
        float nx = e.Position.X + (float)System.Math.Cos(g.Facing) * step;
        float nz = e.Position.Z + (float)System.Math.Sin(g.Facing) * step;
        if (!ColossusFootprintClear(body, nx, nz, g.Facing, out float groundY))
        {
            g.HasRoute = false; // blocked: pick another way on the next tick
            g.Facing += 0.6f;
            return SetPhase(g, "stand");
        }

        // Ease the body height so it never pops up a step.
        float y = e.Position.Y + (groundY - e.Position.Y) * (float)System.Math.Min(1.0, dt * 1.5);
        e.Position = new Vector3f((float)Shared.World.WorldConstants.WrapX(nx, _world.Circumference), y, (float)Shared.World.WorldConstants.WrapZ(nz, _world.Circumference));
        SetPhase(g, "walk");
        return false;
    }

    private void PickColossusRoute(CombatEntity e, GiantRuntime g)
    {
        var rng = new System.Random(unchecked((int)(WorldGenerator.StableHash(e.Id) ^ (long)(_uptime * 10))));
        double angle = g.Facing + (rng.NextDouble() - 0.5) * 2.2; // mostly onwards: a migration, not a pace
        float dist = 150f + (float)rng.NextDouble() * 150f;
        g.Route = new Vector3f(e.Position.X + (float)System.Math.Cos(angle) * dist, e.Position.Y, e.Position.Z + (float)System.Math.Sin(angle) * dist);
        g.HasRoute = true;
        g.RouteUntil = _uptime + 120.0;
        g.GrazeUntil = rng.NextDouble() < 0.35 ? _uptime + 6.0 + rng.NextDouble() * 8.0 : 0.0;
    }

    private static bool SetPhase(GiantRuntime g, string phase)
    {
        if (g.Phase == phase)
        {
            return false;
        }

        g.Phase = phase;
        return true;
    }

    /// <summary>The hip whose ground point is nearest the target, and that ground point.</summary>
    private static int NearestHip(ColossusBody body, Vector3f pos, float facing, Vector3f target, out Vector3f hipGround)
    {
        int best = 0;
        float bestSq = float.MaxValue;
        hipGround = pos;
        for (int leg = 0; leg < 4; leg++)
        {
            var (hx, _, hz) = body.Hip(leg);
            var p = ColossusBody.ToWorld(pos, facing, hx, 0f, hz);
            float dx = p.X - target.X, dz = p.Z - target.Z;
            float d = dx * dx + dz * dz;
            if (d < bestSq)
            {
                bestSq = d;
                best = leg;
                hipGround = p;
            }
        }

        return best;
    }

    private static Vector3f ClampToReach(Vector3f hipGround, Vector3f target, float reach)
    {
        float dx = target.X - hipGround.X, dz = target.Z - hipGround.Z;
        float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (d <= reach || d < 1e-4f)
        {
            return target;
        }

        float k = reach / d;
        return new Vector3f(hipGround.X + dx * k, target.Y, hipGround.Z + dz * k);
    }

    /// <summary>The announced foot lands: dust and a push for everyone near, damage for everyone in the radius who is not
    /// under a solid roof or down in a cave (no block changes — a house is a real shelter).</summary>
    private void LandStomp(CombatEntity e, GiantRuntime g, CreatureSpecies sp)
    {
        var at = g.StompAt;
        BroadcastToWorld(new WorldFx { Kind = "stomp", X = at.X, Y = at.Y, Z = at.Z, Strength = 1f, Radius = StompRadius });
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State;
            if (p.AboardShip || InSpace(p.PlayerId) || p.IgnoredByHostiles)
            {
                continue;
            }

            var un = Unwrapped(at, p.Position);
            float dx = un.X - at.X, dz = un.Z - at.Z;
            if (dx * dx + dz * dz > StompRadius * StompRadius || p.Position.Y < at.Y - 4f || Sheltered(p.Position))
            {
                continue;
            }

            HurtPlayer(s, sp.AttackDamage, "@srv.death.giant");
        }
    }

    /// <summary>True when solid rock or a roof stands over the player's head — a stomp cannot reach them there.</summary>
    private bool Sheltered(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X), z = (int)System.Math.Floor(pos.Z);
        int y = (int)System.Math.Floor(pos.Y);
        for (int dy = 2; dy <= 12; dy++)
        {
            if (IsSolidBlock(_world.GetBlock(new Vector3i(x, y + dy, z))))
            {
                return true;
            }
        }

        return false;
    }

    private void HurtPlayer(PlayerSession s, float amount, string deathReason)
    {
        var p = s.State;
        if (p.GodMode)
        {
            return;
        }

        if (TryGetDrivenSpeeder(p, out var speeder))
        {
            DamageSpeeder(speeder, amount * SpeederCreatureDamageShare, "wildlife");
            amount *= 1f - SpeederCreatureDamageShare;
        }

        p.Health = System.Math.Max(0f, p.Health - Mitigate(p, amount));
        MarkPlayerStateDirty(s);
        if (p.Health <= 0f)
        {
            RespawnPlayer(s, deathReason);
        }
    }

    private PlayerSession? NearestTarget(List<PlayerSession> onFoot, Vector3f from, float range)
    {
        PlayerSession? best = null;
        double bestSq = range * range;
        foreach (var s in onFoot)
        {
            if (s.State.IgnoredByHostiles)
            {
                continue;
            }

            double d = WrapDistSq(s.State.Position, from);
            if (d <= bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        return best;
    }

    private static float TurnToward(float current, float want, float maxStep)
    {
        float diff = want - current;
        while (diff > System.MathF.PI)
        {
            diff -= 2f * System.MathF.PI;
        }

        while (diff < -System.MathF.PI)
        {
            diff += 2f * System.MathF.PI;
        }

        return current + System.Math.Clamp(diff, -maxStep, maxStep);
    }

    // =====================================================================================================
    // Sandworm (#2001)
    // =====================================================================================================

    private bool TrySpawnSandworm(CreatureSpecies sp, int slot, List<PlayerSession> onFoot)
    {
        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("sandworm-spawn:" + _world.LocationId + slot) ^ (long)_uptime)));
        var near = onFoot[rng.Next(onFoot.Count)].State.Position;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = WormSpawnMin + (float)rng.NextDouble() * (WormSpawnMax - WormSpawnMin);
            int x = (int)System.Math.Floor(near.X + System.Math.Cos(angle) * dist);
            int z = (int)System.Math.Floor(near.Z + System.Math.Sin(angle) * dist);
            if (!_generator.IsSandSeaAt(_world.Planet, x, z))
            {
                continue;
            }

            var g = new GiantRuntime
            {
                Kind = CreatureBodyPlan.Sandworm,
                Slot = slot,
                Facing = (float)angle,
                Phase = "hidden",
                PhaseStart = _uptime,
                CooldownUntil = _uptime + 4.0,
            };
            SpawnGiantEntity(sp, g, new Vector3f(x + 0.5f, WormDepthY(sp, x, z), z + 0.5f));
            _log.Info($"A sandworm ({sp.Name}, {sp.GiantHeight:0} blocks, {sp.Temperament}) lurks under '{_world.LocationId}'.");
            return true;
        }

        return false;
    }

    /// <summary>How deep a hidden sandworm runs under the sea at (x, z).</summary>
    private float WormDepthY(CreatureSpecies sp, int x, int z)
        => _generator.SurfaceHeight(_world.Planet, x, z) + 1 - (sp.WormGirth * 1.4f + 2f);

    /// <summary>A vibration at <paramref name="at"/> (#2001). It carries through the sand only: a source whose ground is not
    /// sand-sea sand — rock, a landing pad, a player's floor — is heard by nothing. Every live sandworm in reach grows its
    /// attention; the latest loud source is what it comes for.</summary>
    internal void EmitVibration(Vector3f at, VibrationSource source, string playerId = "")
    {
        if (!Giants.HasWorms || !VibratesSand(at))
        {
            return;
        }

        foreach (var worm in LiveGiants())
        {
            var g = worm.Giant!;
            if (g.Kind != CreatureBodyPlan.Sandworm || g.Move != WormMove.None || !_speciesById.TryGetValue(worm.SpeciesId, out var sp))
            {
                continue;
            }

            float dist = (float)System.Math.Sqrt(WrapDistSq(new Vector3f(worm.Position.X, at.Y, worm.Position.Z), at));
            if (!GiantRules.Hears(source, sp.Hearing, dist))
            {
                continue;
            }

            g.Attention += GiantRules.Weight(source);
            g.Heard = at;
            g.HeardAt = _uptime;
            g.HeardThumper = source == VibrationSource.Thumper;
            g.HeardPlayerId = playerId;
        }
    }

    /// <summary>Whether ground at this spot carries a vibration: the sand of a sand sea, under the source's feet.</summary>
    private bool VibratesSand(Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        if (!_generator.IsSandSeaAt(_world.Planet, x, z))
        {
            return false;
        }

        // The real block under the source (a player may stand on a floor they built over the sand).
        int y = (int)System.Math.Floor(at.Y);
        for (int dy = 0; dy <= 2; dy++)
        {
            var id = _world.GetBlock(new Vector3i(x, y - dy, z));
            if (id.IsAir)
            {
                continue;
            }

            return _content.BlockById(id)?.Key == "sand";
        }

        return false;
    }

    private bool TickSandworm(CombatEntity e, GiantRuntime g, CreatureSpecies sp, List<PlayerSession> onFoot, double dt)
    {
        if (e.ProvokeTimer > 0)
        {
            e.ProvokeTimer = System.Math.Max(0, e.ProvokeTimer - dt);
        }

        if (g.Move != WormMove.None && g.Path is { } path)
        {
            return TickWormMove(e, g, sp, path, onFoot);
        }

        g.Attention = GiantRules.DecayAttention(g.Attention, (float)dt);
        bool cooling = _uptime < g.CooldownUntil;

        // Heard enough: come for it — and once on its way it keeps coming (hysteresis) until it arrives, the shaking has
        // all but faded, or it has been trying for too long.
        if (g.Approaching && (g.Attention < GiantRules.AttentionThreshold * WormApproachKeep || _uptime - g.ApproachSince > WormApproachGiveUp))
        {
            g.Approaching = false;
        }

        if (!cooling && (g.Approaching || g.Attention >= GiantRules.AttentionThreshold))
        {
            if (!g.Approaching)
            {
                g.Approaching = true;
                g.ApproachSince = _uptime;
                g.Phase = "approach";
                g.PhaseStart = _uptime;
                g.NextRumbleAt = _uptime;
                if (g.HeardPlayerId.Length > 0 && FindSessionByPlayerId(g.HeardPlayerId) is { } warned)
                {
                    SendVegaLine(warned, "vega.sys.sandworm_near", 3);
                }
            }

            var to = Unwrapped(e.Position, g.Heard);
            float dx = to.X - e.Position.X, dz = to.Z - e.Position.Z;
            float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (d <= WormStartDistance)
            {
                return StartWormMove(e, g, sp);
            }

            g.Facing = (float)System.Math.Atan2(dz, dx);
            if (!MoveWormUnder(e, sp, g.Facing, sp.Speed * (float)dt))
            {
                g.Approaching = false; // the sea ends between it and the source — it cannot get there under the sand
                g.Attention = 0f;
                g.CooldownUntil = _uptime + 4.0;
                return SetPhase(g, "hidden");
            }

            if (_uptime >= g.NextRumbleAt)
            {
                g.NextRumbleAt = _uptime + WormRumbleEvery;
                int sx = (int)System.Math.Floor(e.Position.X), sz = (int)System.Math.Floor(e.Position.Z);
                BroadcastToWorld(new WorldFx
                {
                    Kind = "rumble",
                    X = e.Position.X,
                    Y = _generator.SurfaceHeight(_world.Planet, sx, sz) + 1,
                    Z = e.Position.Z,
                    Strength = System.Math.Clamp(1f - d / 120f, 0.25f, 1f),
                });
            }

            return false;
        }

        // Roaming the deep sand between meals.
        bool changed = SetPhase(g, "hidden");
        if (!g.HasRoute || WrapDistSq(g.Route, e.Position) < 12f * 12f || _uptime >= g.RouteUntil)
        {
            var rng = new System.Random(unchecked((int)(WorldGenerator.StableHash(e.Id) ^ (long)(_uptime * 10))));
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = 60f + (float)rng.NextDouble() * 80f;
            g.Route = new Vector3f(e.Position.X + (float)System.Math.Cos(angle) * dist, e.Position.Y, e.Position.Z + (float)System.Math.Sin(angle) * dist);
            g.HasRoute = true;
            g.RouteUntil = _uptime + 60.0;
        }

        var r = Unwrapped(e.Position, g.Route);
        g.Facing = TurnToward(g.Facing, (float)System.Math.Atan2(r.Z - e.Position.Z, r.X - e.Position.X), 0.8f * (float)dt);
        if (!MoveWormUnder(e, sp, g.Facing, sp.Speed * WormRoamSpeedShare * (float)dt))
        {
            g.HasRoute = false; // the sea ends here — turn around
        }

        return changed;
    }

    /// <summary>Moves a hidden worm along its heading under the sea; refuses (and stays) where the sea ends.</summary>
    private bool MoveWormUnder(CombatEntity e, CreatureSpecies sp, float heading, float step)
    {
        float nx = e.Position.X + (float)System.Math.Cos(heading) * step;
        float nz = e.Position.Z + (float)System.Math.Sin(heading) * step;
        int ix = (int)System.Math.Floor(nx), iz = (int)System.Math.Floor(nz);
        if (!_generator.IsSandSeaAt(_world.Planet, ix, iz))
        {
            return false;
        }

        e.Position = new Vector3f((float)Shared.World.WorldConstants.WrapX(nx, _world.Circumference), WormDepthY(sp, ix, iz),
            (float)Shared.World.WorldConstants.WrapZ(nz, _world.Circumference));
        return true;
    }

    /// <summary>It is there: a thumper or (for an aggressive worm) the source is struck from a rearing tower; a territorial
    /// worm first breaches beside the source as a warning — it strikes when the shaking goes on.</summary>
    private bool StartWormMove(CombatEntity e, GiantRuntime g, CreatureSpecies sp)
    {
        bool strike = g.HeardThumper || sp.Temperament == CreatureTemperament.Aggressive || g.Warned;
        var target = g.Heard;
        int tx = (int)System.Math.Floor(target.X), tz = (int)System.Math.Floor(target.Z);
        float surfaceY = _generator.SurfaceHeight(_world.Planet, tx, tz) + 1;
        float dirX = (float)System.Math.Cos(g.Facing), dirZ = (float)System.Math.Sin(g.Facing);
        WormMove move = strike ? WormMove.Rear : WormMove.Breach;
        var anchor = new Vector3f(target.X, surfaceY, target.Z);
        float strikeDist = System.Math.Max(8f, sp.GiantHeight * 0.45f);
        if (move == WormMove.Breach)
        {
            // The warning arc passes beside the source, not over it.
            anchor = new Vector3f(target.X - dirZ * 14f, surfaceY, target.Z + dirX * 14f);
        }

        // The body must rise out of the sea: the rising spot and the anchor both on sand-sea ground, else try the other way.
        if (!WormMoveFits(move, anchor, dirX, dirZ, strikeDist))
        {
            dirX = -dirX;
            dirZ = -dirZ;
            if (!WormMoveFits(move, anchor, dirX, dirZ, strikeDist))
            {
                g.Approaching = false;
                g.Attention = 0f;
                g.CooldownUntil = _uptime + 4.0;
                g.Phase = "hidden";
                return true;
            }
        }

        g.Approaching = false;
        g.Move = move;
        g.Path = SandwormPath.Build(move, anchor, dirX, dirZ, sp.GiantHeight, sp.WormLength, sp.WormGirth, strikeDist);
        g.MoveAnchor = anchor;
        g.MoveDirX = dirX;
        g.MoveDirZ = dirZ;
        g.MoveStrike = strikeDist;
        g.Phase = move == WormMove.Rear ? "rear" : "breach";
        g.PhaseStart = _uptime;
        g.StrikeDone = false;
        g.HitThisMove.Clear();
        g.Warned = !strike; // a territorial worm strikes on its next approach if the shaking goes on
        g.Attention = strike ? 0f : GiantRules.AttentionThreshold * 0.4f;
        BroadcastToWorld(new WorldFx { Kind = "breach", X = anchor.X, Y = anchor.Y, Z = anchor.Z, Strength = 1f });
        return true;
    }

    private bool WormMoveFits(WormMove move, Vector3f anchor, float dirX, float dirZ, float strikeDist)
    {
        float back = move == WormMove.Rear ? strikeDist : 0f;
        int ax = (int)System.Math.Floor(anchor.X), az = (int)System.Math.Floor(anchor.Z);
        int rx = (int)System.Math.Floor(anchor.X - dirX * back), rz = (int)System.Math.Floor(anchor.Z - dirZ * back);
        return _generator.IsSandSeaAt(_world.Planet, rx, rz)
               && (move == WormMove.Breach || _generator.IsSandSeaAt(_world.Planet, ax, az) || IsThumperAt(anchor));
    }

    private bool TickWormMove(CombatEntity e, GiantRuntime g, CreatureSpecies sp, SandwormPath path, List<PlayerSession> onFoot)
    {
        float t = (float)(_uptime - g.PhaseStart);
        e.Position = path.HeadAt(t);

        // The strike: the head comes down on its target — a thumper is swallowed, a player there is hit hard.
        if (g.Move == WormMove.Rear && !g.StrikeDone && t >= path.StrikeTime)
        {
            g.StrikeDone = true;
            var at = g.MoveAnchor;
            float radius = sp.WormGirth * 0.9f + 1.5f;
            BroadcastToWorld(new WorldFx { Kind = "strike", X = at.X, Y = at.Y, Z = at.Z, Strength = 1f, Radius = radius + 2f });
            SwallowThumpersNear(at, radius);
            foreach (var s in onFoot)
            {
                var p = s.State;
                if (p.IgnoredByHostiles || !PlanetEnemiesActive)
                {
                    continue;
                }

                var un = Unwrapped(at, p.Position);
                float dx = un.X - at.X, dz = un.Z - at.Z;
                if (dx * dx + dz * dz <= radius * radius && System.Math.Abs(p.Position.Y - at.Y) < sp.WormGirth + 3f)
                {
                    g.HitThisMove.Add(p.PlayerId);
                    HurtPlayer(s, sp.AttackDamage, "@srv.death.giant");
                }
            }
        }

        // The body sweeping through the air knocks aside whoever it touches (once per move).
        if (PlanetEnemiesActive)
        {
            var spheres = path.ExposedSpheres(t);
            foreach (var s in onFoot)
            {
                var p = s.State;
                if (p.IgnoredByHostiles || g.HitThisMove.Contains(p.PlayerId))
                {
                    continue;
                }

                foreach (var (c, r) in spheres)
                {
                    // Before the strike the head's own target zone belongs to the strike — one full blow, not two.
                    if (g.Move == WormMove.Rear && !g.StrikeDone)
                    {
                        float ax = c.X - g.MoveAnchor.X, az = c.Z - g.MoveAnchor.Z;
                        float keep = sp.WormGirth * 0.9f + 3.5f;
                        if (ax * ax + az * az < keep * keep)
                        {
                            continue;
                        }
                    }

                    var un = Unwrapped(c, p.Position);
                    float dx = un.X - c.X, dy = un.Y + 0.9f - c.Y, dz = un.Z - c.Z;
                    float reach = r + 0.8f;
                    if (dx * dx + dy * dy + dz * dz <= reach * reach)
                    {
                        g.HitThisMove.Add(p.PlayerId);
                        HurtPlayer(s, sp.AttackDamage * WormBodyHitShare, "@srv.death.giant");
                        BroadcastToWorld(new WorldFx { Kind = "stomp", X = c.X, Y = c.Y, Z = c.Z, Strength = 0.5f, Radius = reach + 1.5f });
                        break;
                    }
                }
            }
        }

        if (t < path.Duration)
        {
            return false;
        }

        // Back under the sand: cool down, then listen again.
        var end = path.HeadAt(path.Duration);
        int ex = (int)System.Math.Floor(end.X), ez = (int)System.Math.Floor(end.Z);
        e.Position = new Vector3f(end.X, WormDepthY(sp, ex, ez), end.Z);
        g.Move = WormMove.None;
        g.Path = null;
        g.Phase = "hidden";
        g.PhaseStart = _uptime;
        g.HasRoute = false;
        var rng = new System.Random(unchecked((int)(WorldGenerator.StableHash(e.Id) ^ (long)(_uptime * 10))));
        g.CooldownUntil = _uptime + WormCooldownMin + rng.NextDouble() * WormCooldownJitter;
        BroadcastToWorld(new WorldFx { Kind = "dive", X = end.X, Y = g.MoveAnchor.Y, Z = end.Z, Strength = 0.6f });
        return true;
    }

    // =====================================================================================================
    // Wire
    // =====================================================================================================

    /// <summary>Adds a giant's traits and its current phase to its snapshot (#1998): the client builds the body from the
    /// traits and runs the same stomp timing / sandworm curve from the phase fields.</summary>
    private NetCreature WithGiantWire(NetCreature n, CombatEntity e)
    {
        if (e.Giant is not { } g || !_speciesById.TryGetValue(e.SpeciesId, out var sp))
        {
            return n;
        }

        n.GiantHeight = sp.GiantHeight;
        n.BackFeature = sp.BackFeature;
        n.LegRatio = sp.LegRatio;
        n.Mandibles = sp.Mandibles;
        n.WormLength = sp.WormLength;
        n.WormGirth = sp.WormGirth;
        n.Facing = g.Facing;
        n.Phase = g.Phase;
        n.PhaseT = (float)(_uptime - g.PhaseStart);
        n.Asleep = false; // a giant never lies down
        n.Hostile = e.Hostile || e.ProvokeTimer > 0;
        if (g.Kind == CreatureBodyPlan.Colossus && g.Phase == "stomp")
        {
            n.PhaseDur = (float)StompTelegraphSeconds;
            n.EvX = g.StompAt.X;
            n.EvY = g.StompAt.Y;
            n.EvZ = g.StompAt.Z;
            n.EvLeg = g.StompLeg;
        }
        else if (g.Kind == CreatureBodyPlan.Sandworm && g.Move != WormMove.None && g.Path is { } path)
        {
            n.PhaseDur = path.Duration;
            n.EvX = g.MoveAnchor.X;
            n.EvY = g.MoveAnchor.Y;
            n.EvZ = g.MoveAnchor.Z;
            n.EvDirX = g.MoveDirX;
            n.EvDirZ = g.MoveDirZ;
            n.EvPeak = sp.GiantHeight;
            n.EvStrike = g.MoveStrike;
        }

        return n;
    }

    // =====================================================================================================
    // Hits and defeat
    // =====================================================================================================

    /// <summary>Whether a player's hit can land on this giant right now: a colossus always, a sandworm only while some of
    /// it is above the sand.</summary>
    private bool GiantHittable(CombatEntity giant)
    {
        var g = giant.Giant!;
        if (g.Kind == CreatureBodyPlan.Colossus)
        {
            return true;
        }

        return g.Move != WormMove.None && g.Path is { } path && path.Exposed((float)(_uptime - g.PhaseStart));
    }

    /// <summary>The point of the giant's body nearest <paramref name="from"/> — what a hit aims at, and what reach,
    /// sightline and the aim corridor are measured against (a 60-block body is not one point at its feet).</summary>
    private Vector3f GiantAimPoint(CombatEntity giant, Vector3f from)
    {
        var g = giant.Giant!;
        if (!_speciesById.TryGetValue(giant.SpeciesId, out var sp))
        {
            return giant.Position;
        }

        var eye = new Vector3f(from.X, from.Y + 1.5f, from.Z);
        var local = Unwrapped(eye, giant.Position);
        var shift = new Vector3f(local.X - giant.Position.X, 0f, local.Z - giant.Position.Z); // same lap as the player
        Vector3f best = giant.Position;
        float bestD = float.MaxValue;
        if (g.Kind == CreatureBodyPlan.Colossus)
        {
            var body = ColossusBody.For(sp.GiantHeight, sp.LegRatio, sp.NeckLength);
            foreach (var cap in body.Capsules(local, g.Facing, sp.Heads))
            {
                var p = cap.ClosestSurfacePoint(eye);
                float d = Dist(p, eye);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
        }
        else if (g.Path is { } path)
        {
            foreach (var (c, r) in path.ExposedSpheres((float)(_uptime - g.PhaseStart)))
            {
                var cc = c + shift;
                var cap = new GiantCapsule(cc, cc, r);
                var p = cap.ClosestSurfacePoint(eye);
                float d = Dist(p, eye);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
        }

        // The aim point is 1.5 below "eye height" for the sightline (HasLineOfSight lifts both ends by 1.5).
        return new Vector3f(best.X, best.Y - 1.5f, best.Z);
    }

    private static float Dist(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>A surviving hit: a territorial or aggressive colossus turns on the attacker for a minute, a peaceful one
    /// moves off; a sandworm hit mid-breach only grows angrier (it strikes on its next approach).</summary>
    private void OnGiantHit(CombatEntity giant)
    {
        giant.ProvokeTimer = System.Math.Max(giant.ProvokeTimer, ColossusProvokeSeconds);
        if (giant.Giant is { Kind: CreatureBodyPlan.Sandworm } g)
        {
            g.Warned = true;
        }
    }

    /// <summary>A giant fell: the return time is saved, everyone near sees it fall and learns what it was, the killer counts
    /// the achievement.</summary>
    private void OnGiantDefeated(CombatEntity dead, PlayerSession? killer)
    {
        var g = dead.Giant!;
        long back = NowUnixSeconds + (long)(GiantReturnDays * System.Math.Max(60.0, _world.Planet?.DayLengthSeconds ?? 600.0));
        _meta.GiantBackAt[GiantSlotKey(g.Kind, g.Slot)] = back;
        _repo.SaveMetadata(_meta);
        foreach (var s in JoinedInActiveWorld())
        {
            if (s != killer && WrapDistSq(s.State.Position, dead.Position) > 160f * 160f)
            {
                continue;
            }

            SendVegaLine(s, "vega.sys.giant_defeated", 3);
        }

        if (killer is not null)
        {
            Advance(killer, g.Kind == CreatureBodyPlan.Colossus ? "defeat:colossus" : "defeat:sandworm");
        }

        _log.Info($"A {(g.Kind == CreatureBodyPlan.Colossus ? "colossus" : "sandworm")} on '{_world.LocationId}' was defeated{(killer is null ? string.Empty : " by " + killer.State.Name)}.");
    }

    // =====================================================================================================
    // Vibration sources from the player (#2001)
    // =====================================================================================================

    /// <summary>Every accepted move: a player walking (not sneaking) or driving over the sand sends step pulses; a jetpack,
    /// a ship, flying and sitting send nothing.</summary>
    private void GiantsOnPlayerMoved(PlayerSession session, Vector3f before, Vector3f after)
    {
        if (!Giants.HasWorms)
        {
            return;
        }

        var p = session.State;
        double now = _uptime;
        double since = now - session.StepClockAt;
        session.StepClockAt = now;
        if (p.AboardShip || InSpace(p.PlayerId) || p.Jetpacking || p.Fly || p.Seated || since <= 0.0 || since > 1.0)
        {
            return;
        }

        var un = Unwrapped(before, after);
        float dx = un.X - before.X, dz = un.Z - before.Z;
        float moved = (float)System.Math.Sqrt(dx * dx + dz * dz);
        float speed = moved / (float)since;
        bool driving = TryGetDrivenSpeeder(p, out _);
        if (!driving && !GiantRules.WalkShakes(speed))
        {
            return; // sneaking: the sand stays quiet
        }

        session.StepDistance += moved;
        float spacing = driving ? GiantRules.StepSpacing * 2.5f : GiantRules.StepSpacing;
        if (session.StepDistance < spacing)
        {
            return;
        }

        session.StepDistance = 0f;
        EmitVibration(after, driving ? VibrationSource.Speeder : VibrationSource.Step, p.PlayerId);
    }

    // =====================================================================================================
    // Thumper (#2002)
    // =====================================================================================================

    private void StartThumper(PlayerSession session, Vector3i cell)
    {
        var gs = Giants;
        gs.Thumpers.RemoveAll(t => t.Cell == cell);
        var at = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
        gs.Thumpers.Add(new ThumperState { Cell = cell, Until = _uptime + ThumperRunSeconds, NextPulse = _uptime + 0.5, OwnerId = session.State.PlayerId });
        bool heard = _generator.IsSandSeaAt(_world.Planet, cell.X, cell.Z)
                     && _content.BlockById(_world.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)))?.Key == "sand";
        SendVegaLine(session, heard ? "vega.sys.thumper_on" : "vega.sys.thumper_rock", 3);
    }

    private void StopThumper(Vector3i cell) => Giants.Thumpers.RemoveAll(t => t.Cell == cell);

    private bool IsThumperAt(Vector3f at)
        => Giants.Thumpers.Any(t => System.Math.Abs(t.Cell.X + 0.5f - at.X) < 1.5f && System.Math.Abs(t.Cell.Z + 0.5f - at.Z) < 1.5f);

    private void TickThumpers(double dt)
    {
        var gs = Giants;
        if (gs.Thumpers.Count == 0)
        {
            return;
        }

        var thumperId = _content.GetBlock(ThumperBlockKey)?.NumericId;
        for (int i = gs.Thumpers.Count - 1; i >= 0; i--)
        {
            var t = gs.Thumpers[i];
            if (_uptime >= t.Until || thumperId is null || _world.GetBlock(t.Cell) != thumperId.Value)
            {
                gs.Thumpers.RemoveAt(i); // ran out, or it was mined / replaced
                continue;
            }

            if (_uptime < t.NextPulse)
            {
                continue;
            }

            t.NextPulse = _uptime + ThumperPulseSeconds;
            var at = new Vector3f(t.Cell.X + 0.5f, t.Cell.Y, t.Cell.Z + 0.5f);
            BroadcastToWorld(new WorldFx { Kind = "thump", X = at.X, Y = at.Y, Z = at.Z, Strength = 0.45f });
            // The pulse goes into the ground the thumper stands on: the cell below it.
            EmitVibration(new Vector3f(at.X, at.Y - 0.5f, at.Z), VibrationSource.Thumper, t.OwnerId);
        }
    }

    /// <summary>The strike landed on a thumper: the worm swallows it (the one block goes, without a drop).</summary>
    private void SwallowThumpersNear(Vector3f at, float radius)
    {
        var gs = Giants;
        var thumperId = _content.GetBlock(ThumperBlockKey)?.NumericId;
        for (int i = gs.Thumpers.Count - 1; i >= 0; i--)
        {
            var t = gs.Thumpers[i];
            float dx = t.Cell.X + 0.5f - at.X, dz = t.Cell.Z + 0.5f - at.Z;
            if (dx * dx + dz * dz > radius * radius)
            {
                continue;
            }

            gs.Thumpers.RemoveAt(i);
            if (thumperId is { } id && _world.GetBlock(t.Cell) == id)
            {
                _world.SetBlock(t.Cell, BlockId.Air);
                BroadcastToWorld(new BlockChanged { X = t.Cell.X, Y = t.Cell.Y, Z = t.Cell.Z, Block = BlockId.AirValue });
            }
        }
    }

    // =====================================================================================================
    // Admin (testing) and test seams
    // =====================================================================================================

    /// <summary>/giant colossus|sandworm — summons this world's giant of that kind (or a fresh one) near the admin.</summary>
    private void AdminSummonGiant(PlayerSession session, string? kind)
    {
        bool worm = string.Equals(kind, "sandworm", System.StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "worm", System.StringComparison.OrdinalIgnoreCase);
        var gs = Giants;
        var sp = worm
            ? gs.Sandworm ?? CreatureGenerator.GenerateSandworm(_meta.Seed, _world.LocationId)
            : gs.Colossus ?? CreatureGenerator.GenerateColossus(_meta.Seed, _world.LocationId);
        RegisterGiantSpecies(sp);
        if (worm)
        {
            gs.Sandworm ??= sp;
            gs.WormCount = System.Math.Max(1, gs.WormCount);
        }
        else
        {
            gs.Colossus ??= sp;
        }

        _creatures.RemoveAll(c => c.IsGiant && c.Giant!.Kind == sp.BodyPlan);
        var list = new List<PlayerSession> { session };
        bool ok = worm ? TrySpawnSandworm(sp, 0, list) : TrySpawnColossus(sp, list);
        Send(session, new ServerMessage { Text = ok ? "@srv.admin.giant_summoned" : "@srv.admin.giant_no_room" });
        CheatLog(session.State, $"summoned a {(worm ? "sandworm" : "colossus")} ({(ok ? "placed" : "no room")})");
    }

    /// <summary>Test seam: /giant without the chat — summons this world's giant of a kind near the player.</summary>
    public void SummonGiantForTest(string playerId, string kind)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            AdminSummonGiant(s, kind);
        }
    }

    /// <summary>Test seam: whether a column is sand sea on the active world.</summary>
    public bool IsSandSeaAtForTest(int x, int z) => _generator.IsSandSeaAt(_world.Planet, x, z);

    /// <summary>Test seam: the live giants of the active world (id, kind, phase, position, attention).</summary>
    public IReadOnlyList<(string Id, CreatureBodyPlan Kind, string Phase, Vector3f Position, float Attention)> GiantsForTest()
        => LiveGiants().Select(c => (c.Id, c.Giant!.Kind, c.Giant.Phase, c.Position, c.Giant.Attention)).ToList();

    /// <summary>Test seam: the giant species this world hosts.</summary>
    public (CreatureSpecies? Colossus, CreatureSpecies? Sandworm, int WormCount) GiantSpeciesForTest()
        => (Giants.Colossus, Giants.Sandworm, Giants.WormCount);

    /// <summary>Test seam: runs the giant tick now.</summary>
    public void TickGiantsForTest(double dt) => TickGiants(dt);

    /// <summary>Test seam: places this world's giant of a kind near a player (the spawn rules apply).</summary>
    public bool SpawnGiantNearForTest(string playerId, CreatureBodyPlan kind)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return false;
        }

        var list = new List<PlayerSession> { s };
        return kind == CreatureBodyPlan.Colossus
            ? Giants.Colossus is { } c && TrySpawnColossus(c, list)
            : Giants.Sandworm is { } w && TrySpawnSandworm(w, 0, list);
    }

    /// <summary>Test seam: forces a giant into place and state.</summary>
    public void SetGiantForTest(string id, Vector3f position, float facing)
    {
        if (_creatures.FirstOrDefault(c => c.Id == id) is { Giant: { } g } e)
        {
            e.Position = position;
            g.Facing = facing;
        }
    }

    /// <summary>Test seam: a vibration at a spot.</summary>
    public void EmitVibrationForTest(Vector3f at, VibrationSource source) => EmitVibration(at, source);

    /// <summary>Test seam: whether a giant can be hit right now, and where a hit from <paramref name="from"/> aims.</summary>
    public (bool Hittable, Vector3f AimPoint) GiantHitForTest(string id, Vector3f from)
        => _creatures.FirstOrDefault(c => c.Id == id) is { IsGiant: true } e ? (GiantHittable(e), GiantAimPoint(e, from)) : (false, default);

    /// <summary>Test seam: the active world's thumpers.</summary>
    public int ThumperCountForTest => Giants.Thumpers.Count;

    /// <summary>Test seam: makes the giant's hidden return clock run out (and clears it).</summary>
    public long GiantBackAtForTest(CreatureBodyPlan kind, int slot = 0)
        => _meta.GiantBackAt.TryGetValue(GiantSlotKey(kind, slot), out long at) ? at : 0;
}

/// <summary>A giant's own runtime state (#1998) — never persisted; only the return time after a defeat is.</summary>
public sealed class GiantRuntime
{
    public CreatureBodyPlan Kind { get; set; }
    public int Slot { get; set; }
    public float Facing { get; set; }
    public string Phase { get; set; } = string.Empty;
    public double PhaseStart { get; set; }

    // Routes (both kinds)
    public Vector3f Route { get; set; }
    public bool HasRoute { get; set; }
    public double RouteUntil { get; set; }
    public double GrazeUntil { get; set; }

    // Colossus
    public double NextStompAt { get; set; }
    public int StompLeg { get; set; }
    public Vector3f StompAt { get; set; }

    // Sandworm
    public float Attention { get; set; }
    public Vector3f Heard { get; set; }
    public double HeardAt { get; set; }
    public bool HeardThumper { get; set; }
    public string HeardPlayerId { get; set; } = string.Empty;
    public double CooldownUntil { get; set; }
    public double NextRumbleAt { get; set; }
    public bool Warned { get; set; }
    public bool Approaching { get; set; }
    public double ApproachSince { get; set; }
    public WormMove Move { get; set; }
    public SandwormPath? Path { get; set; }
    public Vector3f MoveAnchor { get; set; }
    public float MoveDirX { get; set; }
    public float MoveDirZ { get; set; }
    public float MoveStrike { get; set; }
    public bool StrikeDone { get; set; }
    public HashSet<string> HitThisMove { get; } = new();
}

/// <summary>A running thumper (#2002) — runtime only: a thumper left over a reload is just a block until placed again.</summary>
internal sealed class ThumperState
{
    public Vector3i Cell { get; set; }
    public double Until { get; set; }
    public double NextPulse { get; set; }
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>A world's giants (#1998): which species it hosts, its running thumpers and the tick clocks.</summary>
internal sealed class GiantWorldState
{
    public CreatureSpecies? Colossus { get; set; }
    public CreatureSpecies? Sandworm { get; set; }
    public int WormCount { get; set; }
    public double SpawnCheckIn { get; set; }
    public double BroadcastIn { get; set; }
    public List<ThumperState> Thumpers { get; } = new();

    public bool HasWorms => Sandworm is not null;

    public void Clear()
    {
        Colossus = null;
        Sandworm = null;
        WormCount = 0;
        SpawnCheckIn = 0;
        BroadcastIn = 0;
        Thumpers.Clear();
    }
}
