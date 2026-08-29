// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>A flier's vertical sub-state (#1332): airborne cruising, coming down onto a perch, sitting
/// on it, or climbing back to its hover band.</summary>
public enum FlightPhase : byte { Flying, Landing, Perched, TakingOff }

/// <summary>
/// Per-actor vertical state (#1331) — carried on the entity next to <see cref="LocomotionState"/>, stepped by
/// <see cref="VerticalMotion"/>. A default (all-zero) value is "grounded, flying" and valid from the first tick.
/// Server-only, never persisted.
/// </summary>
public struct VerticalState
{
    public float VertVel;       // blocks/s, +up — only meaningful while Airborne
    public bool Airborne;       // mid-jump / mid-fall (ground classes) — gravity is integrating Y
    public float AirTime;       // seconds airborne so far (the never-stuck timeout)
    public float JumpCooldown;  // seconds until the next voluntary hop/bound may launch
    public float ClimbTargetY;  // > 0: a crawler/giant is easing up in place to this feet Y before stepping over
    public float LastWave;      // previous tick's vertical-life wave (a hopper launches on the upward zero crossing)
    public FlightPhase Flight;  // fliers only
    public float PerchY;        // the feet Y a landing flier is descending to
    public bool InWater;        // amphibians: which class was in effect last tick (one cell of hysteresis)
}

/// <summary>
/// Pure, deterministic vertical mechanics for creatures (#1331): gravity, ballistic jumps and hops, the slow
/// climb-over of animals that cannot jump, and the "never stuck" timeout. Horizontal motion stays with
/// <see cref="LocomotionController"/>; the server decides <i>when</i> to launch (a ledge ahead, a hop beat, a
/// startled ground bird) and feeds the real ground height in. Constants mirror the player: base gravity 20,
/// a jump that clears one block, both scaled by the world's gravity factor (Q9).
/// </summary>
public static class VerticalMotion
{
    /// <summary>The player's gravity constant (PlayerController.Gravity) — creatures fall like the player does.</summary>
    public const float BaseGravity = 20f;

    /// <summary>The player's jump clears ~1.225 blocks; creatures get the same reach so they can take the same
    /// ledges the player can (Q1a).</summary>
    public const float JumpHeight = 1.25f;

    /// <summary>A hopper's beat hop — a visible bound, well short of a ledge jump.</summary>
    public const float HopHeight = 0.6f;

    /// <summary>Ground birds (#1334) fall under this fraction of gravity while airborne — long, flat bounds.</summary>
    public const float GlideGravityScale = 0.4f;

    /// <summary>Rate a walker eases up a sub-block rise / a giant steps up one block (blocks/s).</summary>
    public const float StepUpRate = 6f;

    /// <summary>Rate a crawler drags itself up a one-block rise (blocks/s) — slower than a walker's step.</summary>
    public const float ClimbRate = 2.5f;

    /// <summary>An airborne creature that has not landed by now is snapped to its ground probe — the
    /// never-stuck guarantee for removed floors, unloaded chunks and numeric edge cases.</summary>
    public const float AirborneTimeout = 2.0f;

    /// <summary>Seconds between voluntary bounds (startled ground birds, flee hops).</summary>
    public const float BoundCooldown = 1.2f;

    /// <summary>Effective gravity for a world: the base constant scaled by its gravity factor (asteroids ~0.4,
    /// heavy worlds up to 1.6), clamped so a degenerate factor can't stall or slam anything.</summary>
    public static float Gravity(float gravityFactor)
        => BaseGravity * System.Math.Clamp(gravityFactor, 0.3f, 3f);

    /// <summary>The launch speed that peaks <paramref name="height"/> blocks under gravity <paramref name="g"/>.</summary>
    public static float ImpulseFor(float g, float height)
        => (float)System.Math.Sqrt(2.0 * System.Math.Max(0.01f, g) * System.Math.Max(0f, height));

    /// <summary>The jump height on a world — like the player's: at least the base height, and proportionally
    /// higher on lighter worlds (<c>base × max(1, 1/f)</c>), never lower on heavy ones so a 1-block ledge
    /// stays clearable everywhere.</summary>
    public static float JumpHeightFor(float gravityFactor, float baseHeight = JumpHeight)
        => baseHeight * System.Math.Max(1f, 1f / System.Math.Clamp(gravityFactor, 0.3f, 3f));

    /// <summary>Starts a jump/hop/bound: the creature leaves the ground with <paramref name="impulse"/> up.</summary>
    public static void Launch(ref VerticalState s, float impulse)
    {
        s.Airborne = true;
        s.VertVel = impulse;
        s.AirTime = 0f;
        s.ClimbTargetY = 0f;
    }

    /// <summary>
    /// One tick of a ground-bound creature's Y. <paramref name="groundY"/> is the real feet cell under it
    /// (<paramref name="curY"/> above it → it falls; below it by a fraction → it steps up at
    /// <paramref name="riseRate"/>). A pending <see cref="VerticalState.ClimbTargetY"/> rises in place instead
    /// (a crawler or giant hauling itself up a ledge before it steps over). <paramref name="gravityScale"/> is
    /// 1 for everyone but a gliding ground bird. Returns the new Y.
    /// </summary>
    public static float Ground(ref VerticalState s, float curY, float groundY, float g, double dt,
        float riseRate = StepUpRate, float gravityScale = 1f)
    {
        float ft = (float)dt;
        if (ft <= 0f)
        {
            return curY;
        }

        if (s.JumpCooldown > 0f)
        {
            s.JumpCooldown = System.Math.Max(0f, s.JumpCooldown - ft);
        }

        if (s.Airborne)
        {
            s.AirTime += ft;
            if (s.AirTime > AirborneTimeout)
            {
                Land(ref s);
                return groundY; // never stuck in the air — snap to the probe
            }

            // Exact ballistic step (y += v·dt − ½·g·dt², then v −= g·dt) — a plain Euler step would shave the
            // peak by ~v·dt/2 and a 1.25-block jump could fall a hair short of a 1-block ledge at 15 Hz.
            float ga = g * gravityScale;
            float y = curY + s.VertVel * ft - 0.5f * ga * ft * ft;
            s.VertVel -= ga * ft;
            if (s.VertVel <= 0f && y <= groundY)
            {
                Land(ref s);
                return groundY;
            }

            return y;
        }

        if (s.ClimbTargetY > 0f)
        {
            // Hauling up in place (no gravity while the animal is pulling itself over the lip).
            float ny = System.Math.Min(s.ClimbTargetY, curY + riseRate * ft);
            if (ny >= s.ClimbTargetY - 1e-4f)
            {
                s.ClimbTargetY = 0f;
            }

            return ny;
        }

        if (curY > groundY + 0.05f)
        {
            // The floor is lower than the feet (a dug pit, a removed block, a step down) — start falling.
            s.Airborne = true;
            s.VertVel = 0f;
            s.AirTime = 0f;
            return curY;
        }

        if (curY < groundY)
        {
            return System.Math.Min(groundY, curY + riseRate * ft); // a sub-block rise / a gentle step up
        }

        return groundY;
    }

    /// <summary>Whether a climb-over is in progress or the animal is still below the ledge it wants.</summary>
    public static bool IsBelow(float curY, float targetFeetY) => curY < targetFeetY - 0.05f;

    /// <summary>Begins a crawler's/giant's climb-over toward <paramref name="feetY"/> (a rise it may take but
    /// cannot jump). No-op while airborne.</summary>
    public static void BeginClimb(ref VerticalState s, float feetY)
    {
        if (!s.Airborne)
        {
            s.ClimbTargetY = feetY;
        }
    }

    /// <summary>A hopper launches on the upward zero-crossing of its vertical-life wave (the same beat that
    /// pulses its stride in <see cref="LocomotionController"/>), so hop and stride stay in step. Returns true
    /// exactly once per beat, only while grounded.</summary>
    public static bool HopBeat(ref VerticalState s, float wave)
    {
        bool cross = !s.Airborne && s.LastWave <= 0f && wave > 0f && s.ClimbTargetY <= 0f;
        s.LastWave = wave;
        return cross;
    }

    /// <summary>Eases a Y toward a target at a capped rate — the flier's descent/ascent and the buoyant hover.
    /// Snaps outright beyond <paramref name="snapBeyond"/> (spawn, teleport, shove).</summary>
    public static float Ease(float cur, float target, double dt, float rate, float snapBeyond)
    {
        float d = target - cur;
        if (double.IsPositiveInfinity(dt) || System.Math.Abs(d) > snapBeyond)
        {
            return target;
        }

        float maxStep = (float)(rate * dt);
        return System.Math.Abs(d) <= maxStep ? target : cur + System.Math.Sign(d) * maxStep;
    }

    private static void Land(ref VerticalState s)
    {
        s.Airborne = false;
        s.VertVel = 0f;
        s.AirTime = 0f;
    }
}
