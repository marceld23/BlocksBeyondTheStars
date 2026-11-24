// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The vertical axis of a creature: hover bands and perches of fliers, water columns of swimmers, cave floors and
/// the habitat height a creature is placed at.
/// </summary>
public sealed partial class GameServer
{
    private const float CreatureFlyAltitude = 5f; // how high above the ground fliers hover

    private const float FlierDescendRate = 4f;   // blocks/s a landing flier comes down at (#1332)
    private const float FlierClimbRate = 5f;     // blocks/s a flier climbs back to its hover band
    private const float FlierCruiseRate = 4f;    // blocks/s the hover target is eased at (#652)
    private const float HovererEaseRate = 2f;    // slower — a gas sac lags the terrain instead of tracing it
    private const float SkyGliderEaseRate = 3.5f; // #1778/#1779: a sky ray / air fish never lands, but it cruises like a flier
    private const float LandHovererHeight = 0.8f; // a floating land grazer rides this far above its feet
    private const float PerchReach = 2f;         // a perch may sit this far below the hover band's floor
    private const float TakeOffSettle = 0.3f;    // within this of the hover target → cruising again

    /// <summary>Habitat Y-snap for a one-off placement (spawn / teleport / companion re-place): the pure
    /// habitat height with no easing and no vertical state — fliers at their hover, swimmers mid-column,
    /// ground movers on the real feet cell.</summary>
    private Vector3f AdjustHabitatHeight(CreatureSpecies sp, Vector3f p)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        switch (sp.Habitat)
        {
            case CreatureHabitat.Air:
                float hover = sp.HoverAltitude > 0f ? sp.HoverAltitude : CreatureFlyAltitude;
                // #1697: over a pond the band is measured from the water, not from the bed underneath it.
                return new Vector3f(p.X, AirRestSurfaceYAt(x, z, p.Y, hover) + hover, p.Z);
            case CreatureHabitat.Water:
                return new Vector3f(p.X, WaterColumnY(x, z, 0f, surface, p.Y, clearance: SwimClearance(sp)), p.Z);
            case CreatureHabitat.Lava:
                return _generator.TryGetLavaSurface(_world.Planet, x, z, out int lavaTop, out _)
                    ? new Vector3f(p.X, lavaTop, p.Z)
                    : new Vector3f(p.X, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y), canopyCarries: false), p.Z);
            case CreatureHabitat.Amphibian:
                // #3476: the generator's lake only while it still stands — over a lake the player drained and floored,
                // its old top cell was the concrete (or the air above it), and the animal was set down in the floor.
                return TryGetStandingGeneratedWater(x, z, out int ampTop, out _)
                    ? new Vector3f(p.X, ampTop, p.Z)
                    : new Vector3f(p.X, GroundFeetYAt(x, z, (int)System.Math.Floor(p.Y), canopyCarries: false), p.Z);
            default:
                return new Vector3f(p.X, GroundFeetFor(sp, x, z, (int)System.Math.Floor(p.Y)), p.Z);
        }
    }

    /// <summary>
    /// The per-tick vertical mechanics (#1331/#1332/#1334) at an accepted X/Z: which of the five motion
    /// classes the creature moves in decides how its Y is found. Ground movers run under gravity (real
    /// jumps, real falls, a slow haul for the ones that can't jump), fliers cruise / land / perch / take off,
    /// hoverers drift on a buoyant ease, swimmers porpoise their water column. <paramref name="vertWave"/> is
    /// the creature's own vertical-life wave (sin ∈ [-1,1]); <paramref name="prof"/> supplies its amplitude.
    /// Mutates the creature's <see cref="CombatEntity.Vert"/> state.
    /// </summary>
    private Vector3f ResolveVertical(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f p, float vertWave,
        in LocomotionProfile prof, double dt, bool asleep, MoveMode intent, bool moving, int? groundHint = null)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        switch (motion)
        {
            case MotionClass.Swimmer:
                c.Vert.Airborne = false;
                return new Vector3f(p.X, WaterColumnY(x, z, vertWave, surface, p.Y,
                    holdY: sp.Habitat == CreatureHabitat.Amphibian ? p.Y : null,
                    bottom: CreatureMotion.IsBottomDweller(sp), clearance: SwimClearance(sp)), p.Z);

            case MotionClass.Hoverer:
                {
                    // Buoyant (Q5): never lands, never sinks — asleep it simply holds its band. The target eases
                    // slowly, so a gas sac visibly lags the terrain instead of contour-tracing it.
                    c.Vert.Airborne = false;
                    // #1697: the band rides on what the creature would come down onto — the surface of a
                    // fluid body when the column holds one, the ground otherwise. Measuring from the BED
                    // parked gas sacs and sleeping fliers inside a player's water moat.
                    // #2552: an air hoverer whose body is under a pond's surface rides up over it (AirRestSurfaceYAt).
                    bool airborne = sp.Habitat == CreatureHabitat.Air;
                    int rest = airborne
                        ? AirRestSurfaceYAt(x, z, p.Y, HoverOf(sp))
                        : RestSurfaceYAt(x, z, (int)System.Math.Floor(p.Y), canopyCarries: false); // #2945
                    float baseY = rest + (airborne ? HoverOf(sp) : LandHovererHeight);
                    float target = baseY + prof.VertAmp * vertWave;
                    if (airborne)
                    {
                        // #2943: a ray's wings ride over whatever lies under them — a roof beside it, a low wall.
                        target = WingClearY(sp, c.SizeScale, p.X, p.Z, target);
                    }
                    else
                    {
                        // #1854: a floating land grazer rides 0.8 above its feet cell, but its vertical-life
                        // wave can be a full block (the glider cadence) — at the trough the target sat 0.2
                        // INSIDE the floor, floor() moved the reference cell into the block, and from there the
                        // probe walked the animal down a cell at a time. The feet never go below the rest cell.
                        target = System.Math.Max(target, rest);
                    }

                    float ease = CreatureMotion.IsSkyGlider(sp) ? SkyGliderEaseRate : HovererEaseRate;
                    float snap = airborne ? AirEaseSnap(x, p.Y, z) : 24f; // #2583: out of a gas giant's gas at once
                    return new Vector3f(p.X, VerticalMotion.Ease(p.Y, target, dt, ease, snap), p.Z);
                }

            case MotionClass.Flier:
                return new Vector3f(p.X, ResolveFlier(c, sp, p, vertWave, prof, dt, asleep, intent), p.Z);

            default:
                {
                    // Walker / crawler. Lava dwellers in the melt swim it like water; ashore they walk the rock.
                    if (sp.Habitat == CreatureHabitat.Lava
                        && _generator.TryGetLavaSurface(_world.Planet, x, z, out int lavaTop, out _))
                    {
                        c.Vert.Airborne = false;
                        return new Vector3f(p.X, lavaTop, p.Z);
                    }

                    float g = VerticalMotion.Gravity(_gravityFactor);
                    int groundY = groundHint ?? GroundFeetFor(sp, x, z, (int)System.Math.Floor(p.Y)); // #1367: reuse the step's probe
                    if (!asleep && moving && !c.Vert.Airborne && c.Vert.ClimbTargetY <= 0f)
                    {
                        if (sp.LocoStyle == LocomotionStyle.Hopper && CreatureMotion.CanJump(sp)
                            && VerticalMotion.HopBeat(ref c.Vert, vertWave))
                        {
                            // A hopper's beat is a real ballistic hop now (#1331) — the stride pulse in the
                            // controller rides the same wave, so hop and stride stay in step.
                            VerticalMotion.Launch(ref c.Vert, VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeightFor(_gravityFactor, VerticalMotion.HopHeight)));
                        }
                        else if (intent == MoveMode.Flee && CreatureMotion.Glides(sp) && c.Vert.JumpCooldown <= 0f)
                        {
                            // A startled ground bird bounds (#1334): the ledge jump under glide gravity → a long, flat arc.
                            VerticalMotion.Launch(ref c.Vert, LedgeJumpImpulse(sp));
                            c.Vert.JumpCooldown = VerticalMotion.BoundCooldown;
                        }
                    }
                    else if (!moving)
                    {
                        c.Vert.LastWave = vertWave; // keep the beat detector current so a resumed hopper doesn't fire on stale sign
                    }

                    float riseRate = motion == MotionClass.Crawler ? VerticalMotion.ClimbRate : VerticalMotion.StepUpRate;
                    float gravityScale = CreatureMotion.Glides(sp) ? VerticalMotion.GlideGravityScale : 1f;
                    return new Vector3f(p.X, VerticalMotion.Ground(ref c.Vert, p.Y, groundY, g, dt, riseRate, gravityScale), p.Z);
                }
        }
    }

    private static float HoverOf(CreatureSpecies sp) => sp.HoverAltitude > 0f ? sp.HoverAltitude : CreatureFlyAltitude;

    /// <summary>
    /// A flier's vertical life (#1332): cruise at its hover band over the REAL ground (a player roof counts),
    /// swooping on its own wave; come down to a perch when the controller pauses or sleep begins and there is
    /// something to sit on within reach; sit under gravity (a removed branch drops it); climb back out when
    /// the pause ends, on any hunt/flee intent (a skittish bird flushes when the player nears), when hurt, or
    /// when roused from sleep.
    /// </summary>
    private float ResolveFlier(CombatEntity c, CreatureSpecies sp, Vector3f p, float vertWave, in LocomotionProfile prof,
        double dt, bool asleep, MoveMode intent)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        float hover = HoverOf(sp);
        // #1697: over water the cruise band rides on the SURFACE, not on the bed under it (#2552: nor in it).
        float airTarget = AirRestSurfaceYAt(x, z, p.Y, hover) + hover + prof.VertAmp * vertWave;
        bool mustFly = intent is MoveMode.Seek or MoveMode.Flee || c.PanicTimer > 0 || c.ProvokeTimer > 0;
        bool wantsDown = !mustFly && (asleep || c.Loco.Mode == MoveMode.Pause);
        ref var v = ref c.Vert;
        switch (v.Flight)
        {
            case FlightPhase.Landing:
                if (!wantsDown)
                {
                    v.Flight = FlightPhase.TakingOff;
                    goto case FlightPhase.TakingOff;
                }

                v.Airborne = false;
                float y = VerticalMotion.Ease(p.Y, v.PerchY, dt, FlierDescendRate, 24f);
                if (System.Math.Abs(y - v.PerchY) < 1e-3f)
                {
                    v.Flight = FlightPhase.Perched;
                }

                return y;

            case FlightPhase.Perched:
                if (!wantsDown)
                {
                    v.Flight = FlightPhase.TakingOff;
                    v.Airborne = false;
                    goto case FlightPhase.TakingOff;
                }

                // Sitting: plain gravity — if the perch is dug away it falls to the next floor, and once it has
                // landed there it takes off again (#1332 as decided, #1367): a bird whose branch went does not
                // simply sit on the ground below; it flushes and looks for a perch afresh.
                int feetCell = (int)System.Math.Floor(p.Y);
                int sitOn = RestSurfaceYAt(x, z, feetCell);
                if (sitOn >= feetCell && !v.Airborne && !IsSupportCell(x, feetCell - 1, z))
                {
                    // #2846: the ground it sat on was dug away and no floor lies within the probe's reach: the answer is the
                    // noise surface — the height of the vanished ground — and the bird sat on thin air. It takes wing,
                    // as over a gas sea; a floor within reach is still fallen to (#1332 as decided).
                    v.Flight = FlightPhase.TakingOff;
                    goto case FlightPhase.TakingOff;
                }

                if (LiftOutOfGasSea(x, z, p.Y, sitOn) != sitOn)
                {
                    // #2583: the perch went and only a gas giant's gas sea lies under it — it takes wing at once
                    // instead of dropping in (the probe answers the heightfield buried under the gas).
                    v.Flight = FlightPhase.TakingOff;
                    v.Airborne = false;
                    goto case FlightPhase.TakingOff;
                }

                bool wasAirborne = v.Airborne;
                float sit = VerticalMotion.Ground(ref v, p.Y, sitOn, VerticalMotion.Gravity(_gravityFactor), dt);
                if (wasAirborne && !v.Airborne)
                {
                    v.Flight = FlightPhase.TakingOff;
                }

                return sit;

            case FlightPhase.TakingOff:
                v.Airborne = false;
                float up = VerticalMotion.Ease(p.Y, airTarget, dt, FlierClimbRate, AirEaseSnap(x, p.Y, z)); // #2583: out of the gas at once
                if (System.Math.Abs(up - airTarget) <= TakeOffSettle)
                {
                    v.Flight = FlightPhase.Flying;
                }

                return up;

            default:
                v.Airborne = false;
                if (wantsDown && TryPerchSpot(sp, p, hover, out float perchY))
                {
                    v.Flight = FlightPhase.Landing;
                    v.PerchY = perchY;
                    return VerticalMotion.Ease(p.Y, perchY, dt, FlierDescendRate, 24f);
                }

                return VerticalMotion.Ease(p.Y, airTarget, dt, FlierCruiseRate, AirEaseSnap(x, p.Y, z));
        }
    }

    /// <summary>A standable feet cell under a flier within its hover band plus <see cref="PerchReach"/>, read
    /// from real blocks, where its body fits with the canopy counted as SOLID (a bird sits on a crown, never
    /// inside it). False over a pit, deep water, a block standing in a gas giant's gas sea (#2583; over open gas there is
    /// nothing to sit on at all, so a resting or sleeping flier keeps its band there) or an unloaded column — the flier
    /// keeps hovering.</summary>
    private bool TryPerchSpot(CreatureSpecies sp, Vector3f p, float hover, out float perchY)
    {
        int x = (int)System.Math.Floor(p.X), z = (int)System.Math.Floor(p.Z);
        perchY = 0f;
        if (!TryGroundFeetYAt(x, z, (int)System.Math.Floor(p.Y - hover), out int feet) || feet > p.Y
            || p.Y - feet > hover + PerchReach)
        {
            return false;
        }

        if (CreatureBodyBlocked(sp, new Vector3f(p.X, feet, p.Z), foliagePasses: false))
        {
            return false;
        }

        // #1697: a dry standable cell with water standing on it is no perch — a shallow pool over a real
        // floor passes the probe, and the bird was then set down under the surface. Nothing perches in a puddle.
        if (FluidTopAt(x, z, feet) != int.MinValue)
        {
            return false;
        }

        // #2583: nor in a gas giant's gas sea — the gas is no colliding block, so a block standing in it passes the probe
        // with gas for headroom.
        if (GasSeaTopY() != int.MinValue && GasAtFeetIfLoaded(x, feet, z))
        {
            return false;
        }

        perchY = feet;
        return true;
    }

    /// <summary>The Y inside a column's LOCAL water body (sea, upland pond, or a pool the PLAYER built —
    /// not just the global sea level, so swimmers stay in the water they spawned in): porpoising on the
    /// creature's own wave, clamped to the column (shallow water just keeps them low). A dry column rests on
    /// the ground (or holds <paramref name="holdY"/> when given — an amphibian ashore).
    ///
    /// <para>#1697: real blocks first. The generator only knows the water the world was born with, so a
    /// swimmer in a hand-dug pool snapped to the noise surface — the pre-excavation ground — instead of
    /// porpoising in the water it was standing in.</para>
    ///
    /// <para>#3476: and the other way round — a lake the player DRAINED and floored. The generator's lake is
    /// asked only while it still stands (<see cref="TryGetStandingGeneratedWater"/>), and a dry column rests on
    /// the REAL ground near <paramref name="bodyY"/>: a fish left behind porpoised through the old lake's band,
    /// whose bottom is the player's concrete, and was drawn sunk into the floor with only its back showing.
    /// Unloaded columns answer as before (the generator, else the noise surface).</para>
    ///
    /// <para>#3551: and the water the body is IN comes first. The probe started at the generator surface and only
    /// looked down, so a pond standing above the terrain the world was born with — a pool on a platform, a moat dug
    /// into a lake the player filled up — was never found: the fish was set on the ground under it, or into the fill
    /// (and the stuck-body rescue then lifted it onto the nearest floor).</para>
    ///
    /// <para>Lyxette report 2026-11-18: and the dry answer never lifts a water animal (<see cref="WaterLifeDryRestY"/>).</para>
    ///
    /// <para>#3975: <paramref name="clearance"/> lowers the band's top (never below its bottom) — a whale keeps its whole body
    /// under the surface (<see cref="SwimClearance"/>); 0 for every other swimmer, whose band is unchanged.</para></summary>
    private float WaterColumnY(int x, int z, float vertWave, int surface, float bodyY, float? holdY = null, bool bottom = false,
        float clearance = 0f)
    {
        int waterTopY = int.MinValue, seabedY = int.MinValue;
        int bedFeet = SubmergedFeetYAt(x, z, (int)System.Math.Floor(bodyY), FluidColumnScan);
        if (bedFeet == int.MinValue)
        {
            bedFeet = SubmergedFeetYAt(x, z, (int)System.Math.Round(holdY ?? surface + 1f), FluidColumnScan);
        }

        if (bedFeet != int.MinValue)
        {
            waterTopY = FluidTopAt(x, z, bedFeet);
            seabedY = bedFeet - 1;
        }

        if ((waterTopY == int.MinValue && !TryGetStandingGeneratedWater(x, z, out waterTopY, out seabedY))
            || waterTopY <= seabedY + 1) // a puddle is no column to porpoise in
        {
            return holdY ?? WaterLifeDryRestY(x, z, bodyY, bedFeet);
        }

        float lo = seabedY + 1f, hi = System.Math.Max(lo, waterTopY - 0.5f - clearance);
        if (bottom)
        {
            // #1778: a ray hugs the bed — a low band just above the seabed, lifting a little on its wave.
            float band = System.Math.Min(hi - lo, 1.5f);
            return System.Math.Min(hi, lo + 0.3f + band * 0.6f * (0.5f + 0.5f * vertWave));
        }

        return lo + (hi - lo) * (0.5f + 0.45f * vertWave);
    }

    /// <summary>#3975: how far below the surface a swimmer's band ends — its body height above its position (the body is drawn
    /// from the position up) for a plan whose body stays under water (<see cref="BodyPlanProfile.Submerged"/>: the whale), 0 for
    /// every other swimmer.</summary>
    private static float SwimClearance(CreatureSpecies sp)
        => CreatureBodyPlans.Of(sp.BodyPlan).Submerged ? CreatureBodyHeight(sp) - 1f : 0f;

    /// <summary>Where a water animal rests in a column <see cref="WaterColumnY"/> finds no water to swim in (Lyxette report
    /// 2026-11-18). In a puddle — water one cell deep at or under its body — it rests on the puddle's bed, in the water.
    /// Otherwise on the ground under it, but never ABOVE its body unless the body is inside a block (a fish in a lake the
    /// player filled up is set on the floor laid over it, #3476). The plain ground probe looked up for the nearest standable
    /// cell when nothing below the feet was one, and answered the generator surface when it found none: a fish in a pool one
    /// cell deep, flush with a floor laid on the old ground, was set at that floor's walking height beside its water — awake,
    /// stranded and in plain view — and one under an upper storey on the storey's top.</summary>
    private float WaterLifeDryRestY(int x, int z, float bodyY, int puddleFeet)
    {
        int refY = (int)System.Math.Floor(bodyY);
        if (puddleFeet != int.MinValue && puddleFeet <= refY)
        {
            return puddleFeet;
        }

        int feet = GroundFeetYAt(x, z, refY, canopyCarries: false);
        return feet <= bodyY || IsSupportCell(x, refY, z, canopyCarries: false) ? feet : bodyY;
    }

    /// <summary>The generator's water body in a column (surface top, bed) — but only while it still stands there
    /// (#3476): where the column is streamed in, its top cell must still be water (or a plant in it), or a lily pad
    /// floating on water. The generator knows only the lake the world was born with, so a lake the player drained, or
    /// filled up and floored, went on answering, and the animals that live in water were set and kept inside the
    /// player's floor. A column that is not streamed in has no real blocks to read: the generator answers as it always
    /// did. No-load reads (#1719).</summary>
    private bool TryGetStandingGeneratedWater(int x, int z, out int topY, out int bedY)
    {
        if (!_generator.TryGetWaterSurface(_world.Planet, x, z, out topY, out bedY))
        {
            return false;
        }

        if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, topY, z))))
        {
            return true;
        }

        return IsWaterOrSubmergedPlant(x, topY, z)
            || (IsFlora(_world.GetBlockIfLoaded(new Vector3i(x, topY, z)).Value) && IsWaterOrSubmergedPlant(x, topY - 1, z));
    }

    /// <summary>Finds a standable cave floor (an air pocket on solid ground, with headroom) in a column, scanning
    /// from just below the surface downward. Returns the floor's air-cell Y, or -1 if the column has no open cave.</summary>
    private int FindCaveFloorY(int x, int z, int surface)
    {
        // #1719: no-load reads. An unloaded column reads as air all the way down, so it has no solid floor and
        // yields no cave spawn — the safe answer, and no chunk is generated on the tick thread for an animal
        // that may not even spawn.
        for (int y = surface - 3; y > surface - 50; y--)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(x, y - 1, z)).IsAir   // solid floor
                && _world.GetBlockIfLoaded(new Vector3i(x, y, z)).IsAir      // feet in air
                && _world.GetBlockIfLoaded(new Vector3i(x, y + 1, z)).IsAir) // headroom
            {
                return y;
            }
        }

        return -1;
    }

    /// <summary>True if any water block sits within <paramref name="r"/> cells (horizontally, ±1 in Y) of a
    /// position — used to keep amphibians on the shoreline.</summary>
    private bool WaterWithin(Vector3f at, int r)
    {
        if (_creatureWaterId == 0)
        {
            return false;
        }

        int x = (int)System.Math.Floor(at.X), y = (int)System.Math.Floor(at.Y), z = (int)System.Math.Floor(at.Z);
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (_world.GetBlockIfLoaded(new Vector3i(x + dx, y + dy, z + dz)).Value == _creatureWaterId) // #1719: no-load read
                    {
                        return true;
                    }
                }

        return false;
    }
}
