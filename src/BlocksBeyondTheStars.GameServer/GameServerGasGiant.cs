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
/// the dense gas faster still, no armour; the world's own animals keep out of it, #2583), a <b>sky city breathes</b>
/// (<see cref="InSkyCityAir"/>: a pocket of air over every inhabited settlement on an island) — and the <b>sky giant</b>,
/// the class's own giant: a 40–80 block sailer that drifts on a slow lane between the islands, calls now and then, never
/// lands and never strikes. It lives in the giant slots like the colossus (<c>GameServerGiants</c>) and is hit along its
/// trailing body.
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

    // ---- the animals and the gas sea (#2583) ----
    //
    // Every animal of a gas giant flies (#2112), but the creature probes knew nothing of the gas: it is not solid and not one
    // of the automaton's two fluids, so the probes looked straight through it to the heightfield buried five to nine cells
    // under its surface, measured every hover band from there and parked the animals in the gas — 91 of 102 spawns burned
    // to death within a second, and the spawner kept refilling the cap. The rules below keep them out: an airborne animal's
    // band is measured from the gas sea's surface at the least (or from the rock an island holds over it), nothing spawns
    // where the gas burns, and nothing that walks steps into it.

    /// <summary>#2583: the top cell of the active world's gas sea — a gas giant's sea level, the highest cell the generator
    /// fills with gas (the first air cell is one above) — or <see cref="int.MinValue"/> on every world whose sea is not gas.
    /// Resolved once per world.</summary>
    private int GasSeaTopY()
    {
        var active = _worlds.Active;
        if (active.GasSeaTop is { } known)
        {
            return known;
        }

        var planet = _world.Planet;
        int top = planet is { IsGasWorld: true } && _generator.SeaIsGas(planet) ? _generator.SeaLevel(planet) : int.MinValue;
        active.GasSeaTop = top;
        return top;
    }

    private bool IsGasId(ushort id) => id != 0 && (id == _gasId || id == _denseGasId);

    /// <summary>#2583: the contact rule of <see cref="GasContactDpsAt"/> on real blocks, without loading anything: feet at
    /// <paramref name="feetY"/> stand in the gas (or the dense gas), or right on it. An unloaded cell reads as air.</summary>
    private bool GasAtFeetIfLoaded(int x, int feetY, int z)
    {
        ResolveGasIds();
        return IsGasId(_world.GetBlockIfLoaded(new Vector3i(x, feetY, z)).Value)
            || IsGasId(_world.GetBlockIfLoaded(new Vector3i(x, feetY - 1, z)).Value);
    }

    /// <summary>#2583: whether a body at this position would burn in the gas sea — the contact rule
    /// (<see cref="FeetInGasSeaAt"/>, no chunk is loaded or generated), asked on a gas giant only.</summary>
    private bool InGasSea(Vector3f pos)
    {
        var feet = pos.ToBlock();
        return FeetInGasSeaAt(feet.X, feet.Y, feet.Z);
    }

    /// <summary>#2583: the rest surface an AIR animal measures its band from, lifted out of the gas sea. Where the column
    /// opens onto the gas (the cell at the gas line is gas, or not streamed in) or the body is in the gas already, a rest
    /// surface at or under the gas line becomes the first cell over it: an animal over open gas rides its band above the
    /// surface, and one that sank in rises out instead of holding its depth (the roof guard of #1711 had held it there). A
    /// dry pocket under the gas line — a room dug into an island's keel — keeps its own floor; a deck or an island over the
    /// gas answers above the line anyway. Every other world gets <paramref name="rest"/> back unchanged.</summary>
    private int LiftOutOfGasSea(int x, int z, float bodyY, int rest)
    {
        int top = GasSeaTopY();
        if (top == int.MinValue || rest > top)
        {
            return rest;
        }

        ResolveGasIds();
        var line = new Vector3i(x, top, z);
        bool opensOntoGas = !_world.IsChunkLoaded(WorldConstants.WorldToChunk(line)) || IsGasId(_world.GetBlockIfLoaded(line).Value);
        return opensOntoGas || GasAtFeetIfLoaded(x, (int)System.Math.Floor(bodyY), z) ? top + 1 : rest;
    }

    /// <summary>How far over a column's highest natural top (#2583) the spawn scan starts looking for what stands on it — a
    /// sky city's houses, a deck's rail, a player's build.</summary>
    private const int GasColumnBuildHeadroom = 32;

    /// <summary>#2583: what an AIR animal spawning over a gas giant's column would come down onto — the REAL top of the
    /// column above the gas (an island of any tier, a landing deck, a sky city, a player's build, or water standing on one)
    /// or, where nothing stands over the gas, the first cell over the gas sea. The spawner used to measure from the
    /// generator's surface, which on a gas giant is the heightfield buried under the gas, and scanned only six cells
    /// around it: under an island the animal appeared far below the rock, in the gas. The scan reads real blocks without
    /// loading (one column, from <see cref="GasColumnBuildHeadroom"/> over the highest island band the generator puts
    /// there, or over the gas line where none floats, down to the gas); an island whose chunk is not streamed in yet
    /// answers with the generator's top.</summary>
    private int GasColumnAirRestY(int x, int z, int gasTop)
    {
        bool island = _generator.TryGetHighestBand(_world.Planet, x, z, out var band) && band.Top > gasTop;
        int ceiling = (island ? band.Top : gasTop) + GasColumnBuildHeadroom;
        bool open = !GasColumnSurfaceAt(x, ceiling + 1, z);
        for (int y = ceiling; y > gasTop; y--)
        {
            if (island && y == band.Top && !_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, y, z))))
            {
                return y + 1;
            }

            bool surface = GasColumnSurfaceAt(x, y, z);
            if (surface && open)
            {
                return y + 1;
            }

            open = !surface;
        }

        return gasTop + 1;
    }

    /// <summary>A cell an airborne animal would come down onto in <see cref="GasColumnAirRestY"/>: a supporting block, or
    /// water (scanning down from above, the first water cell is the water's top — the band rides on it, #2552).</summary>
    private bool GasColumnSurfaceAt(int x, int y, int z)
        => IsSupportCell(x, y, z) || (_creatureWaterId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value == _creatureWaterId);

    /// <summary>#2583: whether a step takes a walking body — a walker, a crawler, a floating land grazer; wild, a companion
    /// or one pushed by a shock glove — from dry footing into the gas sea. The gas carries nobody: over open gas the ground
    /// probe finds only the heightfield buried under it, so a wild animal could walk off a deck or an island rim into the
    /// sea and burn, and a companion (the gas spares it) would sink to the hidden floor and trot on under its owner. A deck's
    /// rail stops everybody; at a gap in it, on an island's rim or a player's bridge the drop gate held most wild steps
    /// already — this one holds for everybody, as the lava gate holds the melt. A body already in the gas may walk out. Off
    /// a gas giant it is one comparison.</summary>
    private bool StepsIntoGasSea(CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f next, int? knownNextFeet)
    {
        int top = GasSeaTopY();
        if (top == int.MinValue || !CreatureMotion.ObeysGroundRules(sp, motion))
        {
            return false;
        }

        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int nx = (int)System.Math.Floor(next.X), nz = (int)System.Math.Floor(next.Z);
        if (cx == nx && cz == nz)
        {
            return false;
        }

        int feet = (int)System.Math.Floor(cur.Y);
        int nextFeet = knownNextFeet ?? GroundFeetFor(sp, nx, nz, feet);
        return FeetInGasSea(nx, nextFeet, nz, top) && !FeetInGasSea(cx, feet, cz, top);
    }

    /// <summary>#2583: whether feet at <paramref name="feetY"/> stand in the gas sea or right on it — the contact rule of
    /// <see cref="GasContactDpsAt"/> (the feet cell or the cell under them), without loading anything: a streamed-in cell
    /// answers with its real block; a cell that is not streamed in answers with the generator's fill, which is gas at and
    /// under the gas line — the ground probe's last resort, the noise surface, lies deep in it, and its cells need not be
    /// loaded for the gate to know that. A loaded dry cell under the gas line (a room dug into an island's keel) is no gas.</summary>
    private bool FeetInGasSea(int x, int feetY, int z, int gasTop)
    {
        ResolveGasIds();
        return GasOrUnloadedSeaCell(x, feetY, z, gasTop) || GasOrUnloadedSeaCell(x, feetY - 1, z, gasTop);
    }

    /// <summary>One cell of <see cref="FeetInGasSea"/>: gas or dense gas where it is streamed in, at or under the gas line
    /// where it is not.</summary>
    private bool GasOrUnloadedSeaCell(int x, int y, int z, int gasTop)
    {
        var cell = new Vector3i(x, y, z);
        return _world.IsChunkLoaded(WorldConstants.WorldToChunk(cell)) ? IsGasId(_world.GetBlockIfLoaded(cell).Value) : y <= gasTop;
    }

    /// <summary>#2586: <see cref="FeetInGasSea"/> on a gas giant, false on every other world (no read at all there).</summary>
    private bool FeetInGasSeaAt(int x, int feetY, int z)
    {
        int top = GasSeaTopY();
        return top != int.MinValue && FeetInGasSea(x, feetY, z, top);
    }

    /// <summary>#2583: the snap distance of an AIR animal's vertical ease — the usual 24 cells, or none while its body is in
    /// a gas giant's gas (<see cref="FeetInGasSeaAt"/>: a gas cell at or under the feet, or a cell under the gas line that
    /// is not streamed in — never a loaded dry pocket): there it pops straight up to its band instead of burning for the
    /// seconds the ease takes.</summary>
    private float AirEaseSnap(int x, float bodyY, int z)
        => FeetInGasSeaAt(x, (int)System.Math.Floor(bodyY), z) ? 0f : 24f;

    // ---- the machines, the robbers and the gas sea (#2586) ----
    //
    // The planet machines, a lone robber and a base's scouts were placed like the animals were: from the generator surface,
    // the heightfield buried under the gas — the machines appeared in the gas and burned, one after another on the same
    // spot. They are placed from the real top of the column now (or not at all), a walking robot treats the gas as a wall,
    // and a scan-drone crosses open gas at its hover over the surface, as it crosses a lake.

    /// <summary>#2586: the reference Y a ground placement probes down from in a column: the generator surface + 1 — or on a
    /// gas giant, where that surface lies under the gas, the real top of the column above the gas
    /// (<see cref="GasColumnAirRestY"/>; the first cell over the gas where nothing stands on it).</summary>
    private int GroundProbeRefY(int x, int z)
    {
        int top = GasSeaTopY();
        return top != int.MinValue ? GasColumnAirRestY(x, z, top) : _generator.SurfaceHeight(_world.Planet, x, z) + 1;
    }

    /// <summary>#2586: the floor a hovering scan-drone crosses open gas on — the first cell over the gas sea, as it crosses a
    /// lake on the surface — where the column opens onto the gas (the gas line is gas, or not streamed in) and nothing
    /// solid stands between that floor and a body <paramref name="hover"/> cells over it. <see cref="int.MinValue"/>
    /// elsewhere, and on every world whose sea is not gas.</summary>
    private int GasSurfaceFloorFor(int x, int z, int hover)
    {
        int top = GasSeaTopY();
        if (top == int.MinValue)
        {
            return int.MinValue;
        }

        ResolveGasIds();
        var line = new Vector3i(x, top, z);
        if (_world.IsChunkLoaded(WorldConstants.WorldToChunk(line)) && !IsGasId(_world.GetBlockIfLoaded(line).Value))
        {
            return int.MinValue;
        }

        for (int dy = 1; dy <= hover + 2; dy++)
        {
            if (IsSupportCell(x, top + dy, z))
            {
                return int.MinValue;
            }
        }

        return top + 1;
    }

    /// <summary>#2586: the ground under a scan-drone in a column — real blocks first, then the gas sea's surface
    /// (<see cref="GasSurfaceFloorFor"/>), then the generator surface: the creatures' probe on every other world.</summary>
    private int DroneGroundAt(int x, int z, int refY)
    {
        if (TryGroundFeetYAt(x, z, refY, out int feet))
        {
            return DroneFloorOverGas(x, z, feet);
        }

        int gasFloor = GasSurfaceFloorFor(x, z, ScanDroneHover);
        return gasFloor != int.MinValue ? gasFloor : _generator.SurfaceHeight(_world.Planet, x, z) + 1;
    }

    /// <summary>#2586: a drone's floor at or under the gas line — a block standing in the gas (the gas is no colliding
    /// block, so the probe stands on it) — is the gas surface where the column opens onto the gas: four cells over a block
    /// sunk deep enough are still in the gas. Any other floor, and every floor off a gas giant, is returned unchanged.</summary>
    private int DroneFloorOverGas(int x, int z, int feet)
    {
        int top = GasSeaTopY();
        if (top == int.MinValue || feet > top)
        {
            return feet;
        }

        int gasFloor = GasSurfaceFloorFor(x, z, ScanDroneHover);
        return gasFloor != int.MinValue ? gasFloor : feet;
    }

    /// <summary>#2586: whether a walking robot's step takes its feet from dry footing into the gas sea — <see cref="FeetInGasSea"/>
    /// at the step's column and not at its own. Off a gas giant it is one comparison.</summary>
    private bool WalkerStepsIntoGasSea(int ownX, int ownZ, int feet, int nextX, int nextZ, int nextFeet)
    {
        int top = GasSeaTopY();
        return top != int.MinValue && FeetInGasSea(nextX, nextFeet, nextZ, top) && !FeetInGasSea(ownX, feet, ownZ, top);
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
