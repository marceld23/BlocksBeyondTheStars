// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The drawn body against the blocks (Lyxette's reports 2026-10-23). The server tracks an animal as one point, and its
/// block checks read that point's column — the footprint (#2753) only ever kept animals apart from each other:
/// <list type="bullet">
/// <item><b>#2943 — a sky ray's wing disc.</b> A ray of size 3 spans ten blocks; it slept beside a player's roof with its
/// centre in free air and half a wing in the concrete. The disc is sampled in the wing plane the client draws
/// (<see cref="CreatureFootprint.RayWingPlane"/>): the hover band rises until the disc is clear (a ray rides over a roof
/// beside it or a low wall under it), and a step that would put the disc into more than <see cref="WingStepLift"/> of
/// blocks is refused, so the ray turns at a wall like any blocked animal.</item>
/// <item><b>#2944 — a big land animal's head.</b> The centre stopped a quarter block short of a wall with the head up to
/// the footprint radius inside it. The line from the step's spot to the head (<see cref="CreatureFootprint.HeadReach"/>)
/// is walked column by column: a rise the legs could not take anywhere along it is a wall the head reaches first.</item>
/// <item><b>#2945 — crowns carry only fliers.</b> Leaves counted as ground for every animal, so worms and crawlers climbed a
/// giant crown that reached the ground, or were lifted onto one when their cell filled. The feet probes of every
/// non-air species read a crown as no floor (<see cref="CanopyCarries"/>); fliers still perch on one (#1332).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A disc narrower than this stays inside the columns the centre sweep already reads.</summary>
    private const float WingDiscMinRadius = 1f;

    /// <summary>The wing samples sit on this share of the radius — the drawn panels taper toward the tips.</summary>
    private const float WingRing = 0.85f;

    /// <summary>A disc wider than this also samples an inner ring, so a pillar between two rim samples is still found.</summary>
    private const float WingInnerRingFrom = 2.5f;

    /// <summary>The inner ring's share of the radius.</summary>
    private const float WingInnerRing = 0.5f;

    /// <summary>How far a ray's band may rise to clear its wings — a roof or a wall top; beyond it the obstacle is a wall.</summary>
    private const int WingLiftCells = 12;

    /// <summary>The most a single step may ask the band to rise for the wings: a low wall it glides over, not a cliff.</summary>
    private const float WingStepLift = 1.5f;

    /// <summary>The gap the band keeps between the wings and the block under them once it has risen.</summary>
    private const float WingClearance = 0.05f;

    /// <summary>A head that reaches less than this ahead stays inside the columns the centre sweep already reads.</summary>
    private const float HeadCheckMinReach = 1f;

    /// <summary>The head line ends a little short of the head's front — the middle of the head box.</summary>
    private const float HeadReachShare = 0.9f;

    /// <summary>The head line's sampling distance in blocks.</summary>
    private const float HeadLineStep = 0.5f;

    /// <summary>How far the ground along the head line may drop between two columns and still be followed.</summary>
    private const int HeadLineDrop = 3;

    private static readonly (float Cos, float Sin)[] WingBearings =
    {
        (1f, 0f), (0.7071f, 0.7071f), (0f, 1f), (-0.7071f, 0.7071f),
        (-1f, 0f), (-0.7071f, -0.7071f), (0f, -1f), (0.7071f, -0.7071f),
    };

    /// <summary>#2945: whether a tree crown carries this species' feet — fliers and the other air animals only. Every
    /// ground probe of a land, cave, water, lava or amphibian animal reads leaves as no floor (they still block its body).</summary>
    private static bool CanopyCarries(CreatureSpecies sp) => sp.Habitat == CreatureHabitat.Air;

    /// <summary>#2943: whether an animal of this species and size carries a wing disc the blocks must respect: an air ray
    /// wide enough that its wings leave the centre column.</summary>
    private static bool HasWingDisc(CreatureSpecies sp, float sizeScale)
        => sp.Habitat == CreatureHabitat.Air && sp.BodyPlan == CreatureBodyPlan.Ray
            && CreatureFootprint.Radius(sp, sizeScale) >= WingDiscMinRadius;

    /// <summary>#2943: the highest cell the wing disc touches at <paramref name="pos"/> — eight bearings on
    /// <see cref="WingRing"/> of the radius (and an inner ring on a big ray), through the height band the wings sweep.
    /// A crown passes, as it does for every air body; water passes. int.MinValue when the disc is clear.</summary>
    private int WingDiscTopHit(CreatureSpecies sp, float sizeScale, Vector3f pos)
    {
        float r = CreatureFootprint.Radius(sp, sizeScale);
        float plane = pos.Y + CreatureFootprint.RayWingPlane(sp, sizeScale);
        float half = CreatureFootprint.RayWingHalfBand(sp, sizeScale);
        int y0 = (int)System.Math.Floor(plane - half), y1 = (int)System.Math.Floor(plane + half);
        float rim = r * WingRing, inner = r > WingInnerRingFrom ? r * WingInnerRing : 0f;
        int top = int.MinValue;
        foreach (var (cos, sin) in WingBearings)
        {
            top = System.Math.Max(top, WingColumnTopHit(pos.X + (cos * rim), pos.Z + (sin * rim), y0, y1));
            if (inner > 0f)
            {
                top = System.Math.Max(top, WingColumnTopHit(pos.X + (cos * inner), pos.Z + (sin * inner), y0, y1));
            }
        }

        return top;
    }

    private int WingColumnTopHit(float fx, float fz, int y0, int y1)
    {
        int x = (int)System.Math.Floor(fx), z = (int)System.Math.Floor(fz);
        for (int y = y1; y >= y0; y--)
        {
            if (IsCollidingCellIfLoaded(x, y, z, foliagePasses: true))
            {
                return y;
            }
        }

        return int.MinValue;
    }

    /// <summary>#2943: how far a ray at <paramref name="y"/> must rise for its wing disc to be clear — 0 when it already is,
    /// <see cref="float.PositiveInfinity"/> when more than <see cref="WingLiftCells"/> would be needed (a wall, not a roof).</summary>
    private float WingLift(CreatureSpecies sp, float sizeScale, float x, float z, float y)
    {
        float under = CreatureFootprint.RayWingPlane(sp, sizeScale) - CreatureFootprint.RayWingHalfBand(sp, sizeScale);
        float at = y;
        for (int i = 0; i <= WingLiftCells; i++)
        {
            int top = WingDiscTopHit(sp, sizeScale, new Vector3f(x, at, z));
            if (top == int.MinValue)
            {
                return at - y;
            }

            at = top + 1 + WingClearance - under; // the band's floor just over the highest block it touched
            if (at - y > WingLiftCells)
            {
                break;
            }
        }

        return float.PositiveInfinity;
    }

    /// <summary>#2943: the hover target a ray's band rises to so its wings ride over what lies under them; the target itself
    /// for every other body, and when no clear height is within <see cref="WingLiftCells"/> (the step gate keeps a ray
    /// from flying into such a spot).</summary>
    private float WingClearY(CreatureSpecies sp, float sizeScale, float x, float z, float target)
    {
        if (!HasWingDisc(sp, sizeScale))
        {
            return target;
        }

        float lift = WingLift(sp, sizeScale, x, z, target);
        return float.IsPositiveInfinity(lift) ? target : target + lift;
    }

    /// <summary>#2943: whether a ray's step would put its wings into blocks — more than <see cref="WingStepLift"/> of rise
    /// at the new spot. A ray whose wings already touch (a roof built round a sleeper) may move on as long as the step does
    /// not take it deeper in, so it can always leave.</summary>
    private bool WingStepBlocked(CombatEntity c, CreatureSpecies sp, Vector3f cur, Vector3f cand)
    {
        if (!HasWingDisc(sp, c.SizeScale) || WingDiscTopHit(sp, c.SizeScale, cand) == int.MinValue)
        {
            return false;
        }

        float need = WingLift(sp, c.SizeScale, cand.X, cand.Z, cand.Y);
        if (need <= WingStepLift)
        {
            return false;
        }

        float now = WingLift(sp, c.SizeScale, cur.X, cur.Z, cur.Y);
        return now <= WingStepLift || need > now;
    }

    /// <summary>#2944: whether a land animal's head would reach a wall on this step: from the step's spot along its direction
    /// to <see cref="HeadReachShare"/> of <see cref="CreatureFootprint.HeadReach"/>, every column the line enters must be
    /// one the legs could step onto from the one before — at most <see cref="CreatureMotion.StepUpLimit"/> up, any small
    /// drop. A hillside and a stair stay open; a wall, a cliff, a fence or a low crown stops the animal with its head
    /// outside it. Walkers, crawlers and floating land grazers; fliers, swimmers and air bodies keep their own rules.</summary>
    private bool HeadStepBlocked(CombatEntity c, CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f cand)
    {
        if (!CreatureMotion.ObeysGroundRules(sp, motion))
        {
            return false;
        }

        float reach = CreatureFootprint.HeadReach(sp, c.SizeScale) * HeadReachShare;
        float dx = cand.X - cur.X, dz = cand.Z - cur.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        if (reach < HeadCheckMinReach || len < 1e-4f)
        {
            return false;
        }

        dx /= len;
        dz /= len;
        int stepUp = CreatureMotion.StepUpLimit(CreatureMotion.IsLandHoverer(sp, motion) ? MotionClass.Walker : motion); // #1862: a floating grazer is gated as a walker
        int feet = (int)System.Math.Floor(cand.Y);
        int px = (int)System.Math.Floor(cand.X), pz = (int)System.Math.Floor(cand.Z);
        int samples = (int)System.Math.Ceiling(reach / HeadLineStep);
        for (int s = 1; s <= samples; s++)
        {
            float d = System.Math.Min(reach, s * HeadLineStep);
            int hx = (int)System.Math.Floor(cand.X + (dx * d)), hz = (int)System.Math.Floor(cand.Z + (dz * d));
            if (hx == px && hz == pz)
            {
                continue;
            }

            if (IsCollidingCellIfLoaded(hx, feet + stepUp, hz, foliagePasses: false))
            {
                return true; // a rise past the step-up limit — the head meets it before the feet do
            }

            feet = HeadLineFeet(hx, hz, feet, stepUp);
            px = hx;
            pz = hz;
        }

        return false;
    }

    /// <summary>#2944: the feet height the head line follows into a column it may enter: up the steps it may take, down a
    /// small drop (<see cref="HeadLineDrop"/>), level otherwise.</summary>
    private int HeadLineFeet(int x, int z, int feet, int stepUp)
    {
        for (int k = 0; k < stepUp && IsCollidingCellIfLoaded(x, feet, z, foliagePasses: false); k++)
        {
            feet++;
        }

        for (int k = 0; k < HeadLineDrop && !IsCollidingCellIfLoaded(x, feet - 1, z, foliagePasses: false); k++)
        {
            feet--;
        }

        return feet;
    }

    /// <summary>The spawn-time body checks of this file, for every placement (<see cref="SpawnSpotClear"/>): a land animal
    /// never appears on a tree crown (#2945), a ray never with its wings in a wall — sized for the largest individual of
    /// its species, so none of them does (#2943).</summary>
    private bool BodySpawnBlocked(CreatureSpecies sp, Vector3f pos)
    {
        if (!CanopyCarries(sp) && StandsOnCanopy(pos))
        {
            return true;
        }

        return HasWingDisc(sp, MaxFaunaSizeScale) && WingDiscTopHit(sp, MaxFaunaSizeScale, pos) != int.MinValue;
    }

    /// <summary>#2945: whether the cell under a body's feet is a tree crown.</summary>
    private bool StandsOnCanopy(Vector3f pos)
    {
        var below = _world.GetBlockIfLoaded(new Vector3i(
            (int)System.Math.Floor(pos.X), (int)System.Math.Floor(pos.Y) - 1, (int)System.Math.Floor(pos.Z)));
        return !below.IsAir && TreeFoliage.IsKey(_content.BlockById(below)?.Key);
    }

    /// <summary>Test seam (#2943): the highest cell a live ray's wing disc touches, int.MinValue when it is clear or the
    /// animal is no ray.</summary>
    internal int WingDiscTopHitForTest(string id)
    {
        var c = _creatures.First(x => x.Id == id);
        var sp = _speciesById[c.SpeciesId];
        return sp.BodyPlan == CreatureBodyPlan.Ray ? WingDiscTopHit(sp, c.SizeScale, c.Position) : int.MinValue;
    }

    /// <summary>Test seam (#2945): the feet cell the creature probes would hand a species at a column.</summary>
    internal int GroundFeetForTest(string speciesId, int x, int z, int refY) => GroundFeetFor(_speciesById[speciesId], x, z, refY);
}
