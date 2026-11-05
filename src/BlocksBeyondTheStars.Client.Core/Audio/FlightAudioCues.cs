// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client;

/// <summary>
/// The manoeuvring thrusters' puffs (#3248): a short hiss when a strafe, a yaw or a pitch STARTS or flips direction — never
/// a hiss per frame while the stick is held. Each axis has a press and a release threshold (hysteresis), and an axis only
/// counts as released once it stayed quiet for <see cref="ReleaseHold"/> (a jittery mouse reads as one long turn, not as a
/// string of starts). Puffs are at least <see cref="MinGap"/> apart and the same axis puffs at most every
/// <see cref="AxisGap"/>; the two recorded takes alternate.
/// </summary>
public sealed class RcsPuffTrigger
{
    /// <summary>The number of axes: strafe, yaw, pitch.</summary>
    public const int Axes = 3;

    /// <summary>An axis value at or above this (magnitude) is a thruster firing.</summary>
    public const float Press = 0.35f;

    /// <summary>An axis value at or below this (magnitude) is the thruster easing off.</summary>
    public const float Release = 0.15f;

    /// <summary>How long an axis must stay at or below <see cref="Release"/> before it counts as released.</summary>
    public const float ReleaseHold = 0.15f;

    /// <summary>The shortest time between two puffs, seconds.</summary>
    public const float MinGap = 0.18f;

    /// <summary>The shortest time between two puffs of the same axis, seconds.</summary>
    public const float AxisGap = 0.35f;

    private readonly int[] _sign = new int[Axes];
    private readonly float[] _quietFor = new float[Axes];
    private readonly float[] _axisAt = new float[Axes];
    private float _lastAt;
    private float _lastNow;
    private int _take;

    public RcsPuffTrigger() => Reset();

    /// <summary>Forgets every axis state (a new flight, the controls taken away).</summary>
    public void Reset()
    {
        for (int i = 0; i < Axes; i++)
        {
            _sign[i] = 0;
            _quietFor[i] = 0f;
            _axisAt[i] = float.NegativeInfinity;
        }

        _lastAt = float.NegativeInfinity;
        _lastNow = float.NaN;
    }

    /// <summary>Feeds one frame at time <paramref name="now"/> (seconds) with the three axis values (−1..1; non-finite
    /// reads 0). True when a puff should play now; <paramref name="take"/> is then the take to play (0 or 1, alternating).</summary>
    public bool Step(float now, float strafe, float yaw, float pitch, out int take)
    {
        take = _take;
        float dt = float.IsNaN(_lastNow) || !IsFinite(now) ? 0f : Math.Max(0f, now - _lastNow);
        if (IsFinite(now))
        {
            _lastNow = now;
        }

        int fired = -1;
        fired = Axis(0, strafe, dt, fired);
        fired = Axis(1, yaw, dt, fired);
        fired = Axis(2, pitch, dt, fired);
        if (fired < 0 || !IsFinite(now) || now - _lastAt < MinGap || now - _axisAt[fired] < AxisGap)
        {
            return false;
        }

        _lastAt = now;
        _axisAt[fired] = now;
        _take = 1 - _take;
        return true;
    }

    /// <summary>Updates one axis; returns <paramref name="axis"/> when it just started or flipped (the first such axis
    /// of the frame wins), else <paramref name="fired"/>.</summary>
    private int Axis(int axis, float value, float dt, int fired)
    {
        if (!IsFinite(value))
        {
            value = 0f;
        }

        float mag = Math.Abs(value);
        int sign = value > 0f ? 1 : -1;
        if (mag >= Press)
        {
            _quietFor[axis] = 0f;
            if (_sign[axis] != sign)
            {
                _sign[axis] = sign;
                return fired < 0 ? axis : fired;
            }

            return fired;
        }

        if (mag <= Release)
        {
            _quietFor[axis] += dt;
            if (_quietFor[axis] >= ReleaseHold)
            {
                _sign[axis] = 0;
            }
        }
        else
        {
            _quietFor[axis] = 0f; // between the thresholds: the state holds
        }

        return fired;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}

/// <summary>
/// The afterburner surge (#3248): a quick push of the throttle from low to high kicks the afterburner layer in for
/// <see cref="SurgeSeconds"/> — as long as the throttle stays up — then it settles back into the thrust layer. A held
/// throttle is never a constant roar, and a tapped key re-kicks it at most every <see cref="Cooldown"/>.
/// </summary>
public sealed class FlightBoostSurge
{
    /// <summary>The throttle at or above which a quick push surges.</summary>
    public const float High = 0.85f;

    /// <summary>The throttle below which the push must have started.</summary>
    public const float Low = 0.5f;

    /// <summary>How quickly the push from below <see cref="Low"/> to <see cref="High"/> must happen, seconds.</summary>
    public const float PushWindow = 0.35f;

    /// <summary>How long a surge lasts while the throttle stays at or above <see cref="Hold"/>.</summary>
    public const float SurgeSeconds = 1.6f;

    /// <summary>A surge ends early when the throttle drops below this.</summary>
    public const float Hold = 0.6f;

    /// <summary>The shortest time between two surges.</summary>
    public const float Cooldown = 3f;

    private float _left;
    private float _sinceLow;
    private float _sinceSurge;

    public FlightBoostSurge() => Reset();

    /// <summary>Whether a surge is running.</summary>
    public bool Active => _left > 0f;

    /// <summary>Forgets any surge (a new flight).</summary>
    public void Reset()
    {
        _left = 0f;
        _sinceLow = float.PositiveInfinity;
        _sinceSurge = float.PositiveInfinity;
    }

    /// <summary>Advances by <paramref name="dt"/> seconds with the throttle <paramref name="throttle"/> (0..1, its
    /// magnitude); true while a surge runs.</summary>
    public bool Step(float dt, float throttle)
    {
        dt = float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f ? 0f : dt;
        float t = float.IsNaN(throttle) || float.IsInfinity(throttle) ? 0f : Math.Min(1f, Math.Abs(throttle));
        _sinceSurge += dt;
        _sinceLow += dt;
        if (_left > 0f)
        {
            _left = t >= Hold ? Math.Max(0f, _left - dt) : 0f;
        }

        if (t >= High && _sinceLow <= PushWindow && _sinceSurge >= Cooldown)
        {
            _left = SurgeSeconds;
            _sinceSurge = 0f;
        }

        if (t < Low)
        {
            _sinceLow = 0f;
        }

        return _left > 0f;
    }
}

/// <summary>
/// The shield recharge cue (#3248): once the own shield has dropped (a hit), the first rise again — the server recharges
/// it only out of combat — plays <c>shield_recharge</c> once. Then it waits for the next drop; two cues are at least
/// <see cref="MinGap"/> apart.
/// </summary>
public sealed class ShieldRechargeCue
{
    /// <summary>A drop by more than this arms the cue.</summary>
    public const float DropEpsilon = 0.05f;

    /// <summary>A rise by more than this is the shield recharging.</summary>
    public const float RiseEpsilon = 0.01f;

    /// <summary>The shortest time between two cues, seconds.</summary>
    public const float MinGap = 5f;

    private float _last = float.NaN;
    private bool _armed;
    private float _playedAt = float.NegativeInfinity;

    /// <summary>Starts over from <paramref name="shield"/> (a new flight); nothing is armed.</summary>
    public void Reset(float shield)
    {
        _last = shield;
        _armed = false;
    }

    /// <summary>Feeds a new shield value at time <paramref name="now"/>; true when the recharge cue should play.</summary>
    public bool Step(float now, float shield, float shieldMax)
    {
        if (float.IsNaN(shield) || float.IsInfinity(shield) || !(shieldMax > 0f))
        {
            return false;
        }

        float last = _last;
        _last = shield;
        if (float.IsNaN(last) || last < 0f)
        {
            return false;
        }

        if (shield < last - DropEpsilon)
        {
            _armed = true;
            return false;
        }

        if (shield <= last + RiseEpsilon || !_armed)
        {
            return false;
        }

        _armed = false;
        if (now - _playedAt < MinGap)
        {
            return false;
        }

        _playedAt = now;
        return true;
    }
}

/// <summary>
/// Which sound a ship weapon's shot makes (#3248, #3238): the mining beam (or any shot at a rock) the mining zap, the
/// cannons — the plasma bolt and the heavy laser-cannon beam — the recorded <c>ship_weapon</c> shot, the twin pulse laser
/// (and anything unknown) the laser zap. Driven by the module's data-driven fx style, so a new module sounds right
/// without code.
/// </summary>
public static class FlightWeaponCue
{
    public const string Mine = "ship_mine";
    public const string Laser = "ship_laser";
    public const string Cannon = "ship_weapon";

    /// <summary>The cue id for a shot of the fx <paramref name="style"/>; <paramref name="mining"/> = the shot hits a rock.</summary>
    public static string For(string? style, bool mining)
    {
        if (mining || style == FxStyles.DrillBeam)
        {
            return Mine;
        }

        return style == FxStyles.PlasmaBolt || style == FxStyles.HeavyBeam ? Cannon : Laser;
    }

    /// <summary>The pitch of the shot: the heavy beam booms lower than the plasma bolt.</summary>
    public static float Pitch(string? style) => style == FxStyles.HeavyBeam ? 0.85f : 1f;
}
