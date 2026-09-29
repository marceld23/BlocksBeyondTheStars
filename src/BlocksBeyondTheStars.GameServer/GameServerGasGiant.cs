// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The gas giant (#2112, Justus' "where are the gases?", generation 18): the world class with no solid surface. The
/// generator floods the whole heightfield with the <c>gas</c> block (a still liquid, <c>PlanetType.SeaFluid</c>) and the
/// floating islands are the only ground (#2134: under the gas lies <c>gas_dense</c> down to the floor, never rock); here
/// live the three server rules of the class — the <b>gas sea kills</b> (<see cref="GasContactDpsAt"/>, faster than lava,
/// the dense gas faster still, no armour), a <b>sky city breathes</b> (<see cref="InSkyCityAir"/>: a pocket
/// of air over every inhabited settlement on an island) — and the <b>sky giant</b>, the class's own giant: a 40–80 block
/// sailer that drifts on a slow lane between the islands, calls now and then, never lands and never strikes. It lives in
/// the giant slots like the colossus (<c>GameServerGiants</c>) and is hit along its trailing body.
/// </summary>
public sealed partial class GameServer
{
    // ---- the gas sea ----

    /// <summary>Health lost per second in the gas — twice the lava, and no armour helps (there is nothing to breathe).</summary>
    internal const float GasContactDps = 30f;

    /// <summary>Health lost per second in the dense gas under the gas sea (#2134) — heavier still: whoever sinks that deep
    /// is gone in about two seconds. No armour helps either.</summary>
    internal const float DenseGasContactDps = 45f;

    private ushort _gasId, _denseGasId;
    private bool _gasIdKnown;

    /// <summary>Resolves the gas and dense-gas block ids once (0 where the content has none).</summary>
    private void ResolveGasIds()
    {
        if (!_gasIdKnown)
        {
            _gasId = _content.GetBlock("gas")?.NumericId.Value ?? 0;
            _denseGasId = _content.GetBlock("gas_dense")?.NumericId.Value ?? 0;
            _gasIdKnown = true;
        }
    }

    /// <summary>The gas contact rule: the damage per second at a position — the feet cell, or the cell under the feet, in
    /// the dense gas (#2134) → <see cref="DenseGasContactDps"/>, in the gas sea → <see cref="GasContactDps"/>, else 0.</summary>
    private float GasContactDpsAt(Vector3f position)
    {
        ResolveGasIds();
        if (_gasId == 0 && _denseGasId == 0)
        {
            return 0f;
        }

        var feet = position.ToBlock();
        ushort at = _world.GetBlock(feet).Value;
        ushort under = _world.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).Value;
        if (_denseGasId != 0 && (at == _denseGasId || under == _denseGasId))
        {
            return DenseGasContactDps;
        }

        return _gasId != 0 && (at == _gasId || under == _gasId) ? GasContactDps : 0f;
    }

    // ---- the sky cities ----

    /// <summary>How far around an inhabited sky city's footprint its air reaches, and how high over its roofs.</summary>
    private const int SkyCityAirMargin = 6;
    private const int SkyCityAirHeight = 12;

    /// <summary>Whether a position lies inside a sky city's breathable pocket (#2112): on a gas giant every INHABITED settlement
    /// on an island (the abandoned ones have lost theirs) holds a pocket of air over its footprint — the base-air idea for a
    /// place nobody founded. Nothing on any other world.</summary>
    private bool InSkyCityAir(Vector3f pos)
    {
        if (_world.Planet is not { IsGasWorld: true } || _settlements.Count == 0)
        {
            return false;
        }

        int px = (int)System.Math.Floor(pos.X), py = (int)System.Math.Floor(pos.Y), pz = (int)System.Math.Floor(pos.Z);
        foreach (var s in _settlements)
        {
            if (s.Ruined || !s.OnIsland)
            {
                continue;
            }

            int dx = WorldConstants.WrapDeltaX(px - s.Min.X, _world.Circumference);
            if (dx >= -SkyCityAirMargin && dx <= s.Max.X - s.Min.X + SkyCityAirMargin
                && pz >= s.Min.Z - SkyCityAirMargin && pz <= s.Max.Z + SkyCityAirMargin
                && py >= s.GroundY - 2 && py <= s.Max.Y + SkyCityAirHeight)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam (#2112): whether a position breathes a sky city's air.</summary>
    public bool InSkyCityAirForTest(Vector3f pos) => InSkyCityAir(pos);

    // ---- the sky giant ----

    private const float SkyGiantSpawnMin = 120f, SkyGiantSpawnMax = 200f; // the lane's centre, from a player
    private const double SkyGiantCallMin = 25.0, SkyGiantCallJitter = 20.0;
    private const double SkyGiantLaneShiftSeconds = 120.0;               // how often the lane wanders to a new centre
    private const float SkyGiantLaneWander = 140f;                       // …and how far
    private const float SkyGiantLaneDriftSpeed = 1.2f;                   // the centre eases over, blocks/s
    private const float SkyGiantTrailSpacing = 3f;                       // the hit model's samples along the body

    private static readonly ItemAmount[] SkyGiantLoot =
    {
        new("crystal", 6), new("polymer", 8), new("data_fragment", 2),
    };

    /// <summary>Places the sky giant on a fresh lane 120–200 blocks from a player on foot: a ring of 70–130 blocks around a
    /// centre, 42–70 blocks over the gas — a landmark against the sky, never on top of anyone.</summary>
    private bool TrySpawnSkyGiant(CreatureSpecies sp, List<PlayerSession> onFoot)
    {
        var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("sky-giant-spawn:" + _world.LocationId) ^ (long)_uptime)));
        var near = onFoot[rng.Next(onFoot.Count)].State.Position;
        double angle = rng.NextDouble() * System.Math.PI * 2.0;
        float dist = SkyGiantSpawnMin + (float)rng.NextDouble() * (SkyGiantSpawnMax - SkyGiantSpawnMin);
        int sea = _generator.SeaLevel(_world.Planet);
        float floor = sea != int.MinValue ? sea : _generator.SurfaceHeight(_world.Planet, (int)near.X, (int)near.Z);
        var g = new GiantRuntime
        {
            Kind = CreatureBodyPlan.SkyGiant,
            Slot = 0,
            Phase = "drift",
            PhaseStart = _uptime,
            LaneX = (float)WorldConstants.WrapX(near.X + System.Math.Cos(angle) * dist, _world.Circumference),
            LaneZ = (float)WorldConstants.WrapZ(near.Z + System.Math.Sin(angle) * dist, _world.Circumference),
            LaneRadius = GiantRules.SkyGiantLaneRadiusMin + (float)rng.NextDouble() * (GiantRules.SkyGiantLaneRadiusMax - GiantRules.SkyGiantLaneRadiusMin),
            Altitude = floor + GiantRules.SkyGiantAltitudeMin + (float)rng.NextDouble() * (GiantRules.SkyGiantAltitudeMax - GiantRules.SkyGiantAltitudeMin),
            LaneAngle = (float)(rng.NextDouble() * System.Math.PI * 2.0),
            LaneClockwise = rng.NextDouble() < 0.5,
            LaneUntil = _uptime + SkyGiantLaneShiftSeconds,
            NextCallAt = _uptime + 8.0 + rng.NextDouble() * 10.0,
        };
        g.LaneTargetX = g.LaneX;
        g.LaneTargetZ = g.LaneZ;
        g.Facing = LaneHeading(g);
        SpawnGiantEntity(sp, g, LanePoint(g, 0f));
        _log.Info($"A sky giant ({sp.Name}, {sp.WormLength:0} blocks) drifts over '{_world.LocationId}'.");
        return true;
    }

    private Vector3f LanePoint(GiantRuntime g, float bob)
        => new((float)WorldConstants.WrapX(g.LaneX + System.Math.Cos(g.LaneAngle) * g.LaneRadius, _world.Circumference),
            g.Altitude + bob,
            (float)WorldConstants.WrapZ(g.LaneZ + System.Math.Sin(g.LaneAngle) * g.LaneRadius, _world.Circumference));

    /// <summary>The heading along the ring: the tangent, one way round or the other.</summary>
    private static float LaneHeading(GiantRuntime g)
        => g.LaneAngle + (g.LaneClockwise ? -System.MathF.PI * 0.5f : System.MathF.PI * 0.5f);

    /// <summary>The drift: round its ring at its own pace (the ring itself wanders slowly to a new centre every couple of
    /// minutes), a gentle bob, a call every half minute or so. It never reacts to anyone — a hit only makes it climb a
    /// little for a while (<see cref="GiantRuntime.ClimbUntil"/>). Returns false: the heartbeat carries the position.</summary>
    private bool TickSkyGiant(CombatEntity e, GiantRuntime g, CreatureSpecies sp, double dt)
    {
        if (e.ProvokeTimer > 0)
        {
            e.ProvokeTimer = System.Math.Max(0, e.ProvokeTimer - dt);
        }

        if (_uptime >= g.LaneUntil)
        {
            var rng = new System.Random(unchecked((int)(WorldGenerator.StableHash(e.Id) ^ (long)(_uptime * 10))));
            double angle = rng.NextDouble() * System.Math.PI * 2.0;
            float dist = SkyGiantLaneWander * (0.4f + (float)rng.NextDouble() * 0.6f);
            g.LaneTargetX = (float)WorldConstants.WrapX(g.LaneX + System.Math.Cos(angle) * dist, _world.Circumference);
            g.LaneTargetZ = (float)WorldConstants.WrapZ(g.LaneZ + System.Math.Sin(angle) * dist, _world.Circumference);
            g.LaneUntil = _uptime + SkyGiantLaneShiftSeconds;
        }

        // The centre eases toward its target.
        var target = Unwrapped(new Vector3f(g.LaneX, 0f, g.LaneZ), new Vector3f(g.LaneTargetX, 0f, g.LaneTargetZ));
        float tx = target.X - g.LaneX, tz = target.Z - g.LaneZ;
        float td = (float)System.Math.Sqrt(tx * tx + tz * tz);
        if (td > 0.5f)
        {
            float step = System.Math.Min(td, SkyGiantLaneDriftSpeed * (float)dt);
            g.LaneX = (float)WorldConstants.WrapX(g.LaneX + tx / td * step, _world.Circumference);
            g.LaneZ = (float)WorldConstants.WrapZ(g.LaneZ + tz / td * step, _world.Circumference);
        }

        // Round the ring: the speed along the arc is the species' own.
        float omega = sp.Speed / System.Math.Max(20f, g.LaneRadius);
        g.LaneAngle += (g.LaneClockwise ? -omega : omega) * (float)dt;
        float bob = (float)System.Math.Sin(_uptime * 0.25) * 3f;
        float climb = _uptime < g.ClimbUntil ? 12f : 0f;
        e.Position = LanePoint(g, bob + climb);
        g.Facing = LaneHeading(g);

        // The trail the hits are measured along: a sample every few blocks, as long as the body.
        if (g.Trail.Count == 0 || WrapDistSq(g.Trail[g.Trail.Count - 1], e.Position) >= SkyGiantTrailSpacing * SkyGiantTrailSpacing)
        {
            g.Trail.Add(e.Position);
            int max = (int)(sp.WormLength / SkyGiantTrailSpacing) + 1;
            if (g.Trail.Count > max)
            {
                g.Trail.RemoveAt(0);
            }
        }

        if (_uptime >= g.NextCallAt)
        {
            var rng = new System.Random(unchecked((int)(WorldGenerator.StableHash(e.Id + "call") ^ (long)(_uptime * 10))));
            g.NextCallAt = _uptime + SkyGiantCallMin + rng.NextDouble() * SkyGiantCallJitter;
            BroadcastToWorld(new WorldFx { Kind = "skycall", X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z, Strength = 1f });
        }

        return false;
    }

    /// <summary>The point of the sky giant's body nearest the eye: the head sphere and a capsule per trail span (the body
    /// trails the head along its own track, the client draws it the same way).</summary>
    private Vector3f SkyGiantAimPoint(CombatEntity giant, GiantRuntime g, CreatureSpecies sp, Vector3f eye, Vector3f shift)
    {
        float r = System.Math.Max(1.5f, sp.WormGirth * 0.5f);
        var head = giant.Position + shift;
        Vector3f best = new GiantCapsule(head, head, r).ClosestSurfacePoint(eye);
        float bestD = Dist(best, eye);
        for (int i = g.Trail.Count - 1; i > 0; i--)
        {
            var a = g.Trail[i] + shift;
            var b = g.Trail[i - 1] + shift;
            var p = new GiantCapsule(a, b, r * (0.5f + 0.5f * i / (float)g.Trail.Count)).ClosestSurfacePoint(eye);
            float d = Dist(p, eye);
            if (d < bestD)
            {
                bestD = d;
                best = p;
            }
        }

        return best;
    }

    /// <summary>Test seam (#2112): the sky giant this world hosts, and a live one's lane.</summary>
    public CreatureSpecies? SkyGiantSpeciesForTest() => Giants.SkyGiant;

    public (float LaneX, float LaneZ, float Radius, float Altitude, int Trail)? SkyGiantLaneForTest(string id)
        => _creatures.Find(c => c.Id == id)?.Giant is { Kind: CreatureBodyPlan.SkyGiant } g ? (g.LaneX, g.LaneZ, g.LaneRadius, g.Altitude, g.Trail.Count) : null;
}
