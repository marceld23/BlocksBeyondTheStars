// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Core;

namespace BlocksBeyondTheStars.Client;

/// <summary>What the own ship is doing, as far as its engine sound cares (#3248).</summary>
public enum FlightEnginePhase
{
    /// <summary>No engine at all: the ship blew up, or there is no flight.</summary>
    Off,

    /// <summary>Flying at the helm — the player steers.</summary>
    Cruise,

    /// <summary>A scripted stretch (take-off, landing descent, dock approach, transit arrival): the cinematic cues carry
    /// the moment, the engine only idles under them.</summary>
    Sequence,

    /// <summary>On a spacewalk: the parked ship idles quietly somewhere behind the suit.</summary>
    Eva,
}

/// <summary>The target levels of the own ship's engine layers for one frame (before the effects volume).</summary>
public readonly struct FlightEngineLevels
{
    public FlightEngineLevels(float idle, float thrust, float boost, float cockpit, float pitch)
    {
        Idle = idle;
        Thrust = thrust;
        Boost = boost;
        Cockpit = cockpit;
        Pitch = pitch;
    }

    /// <summary>The steady engine bed (<c>engine_idle</c>).</summary>
    public float Idle { get; }

    /// <summary>The thrust layer (<c>engine_thrust</c>) — swells with the throttle.</summary>
    public float Thrust { get; }

    /// <summary>The afterburner layer (<c>engine_boost</c>) — only during a throttle surge.</summary>
    public float Boost { get; }

    /// <summary>The cockpit electronics bed (<c>cockpit_ambience</c>) — first person only.</summary>
    public float Cockpit { get; }

    /// <summary>The engine pitch every engine layer plays at.</summary>
    public float Pitch { get; }
}

/// <summary>
/// The flight's engine mix (#3248), Unity-free so the headless tests can hold it to its promises. The own ship is three
/// engine layers — a steady idle bed, a thrust layer that swells with the throttle and an afterburner layer on top of a
/// throttle surge — plus a cockpit electronics bed in the first-person view, where the engine also reads darker (the
/// idle carries, the thrust and the afterburner are held back, the pitch sits lower). Calm by design: the idle sits at
/// the old single loop's level, full throttle adds a swell rather than a roar, and the afterburner is a short surge that
/// settles again (<see cref="FlightBoostSurge"/>). Other ships get a 3D engine loop pitched by their size, faded by
/// distance and bent by a hand-made Doppler (Unity Web has no Doppler and no filter components, so this is all
/// volume and pitch). The SpaceView applies the levels times the player's effects volume, eased with <see cref="Ease"/>.
/// </summary>
public static class FlightEngineMix
{
    /// <summary>The idle bed in cruise at rest (exterior view). At the default effects volume this is the old single
    /// engine loop's level.</summary>
    public const float IdleLevel = 0.38f;

    /// <summary>How far the idle bed dips under full drive (the thrust takes over).</summary>
    public const float IdleDuck = 0.12f;

    /// <summary>The thrust layer at full drive (exterior view).</summary>
    public const float ThrustLevel = 0.3f;

    /// <summary>The afterburner layer during a surge at full drive (exterior view).</summary>
    public const float BoostLevel = 0.26f;

    /// <summary>The cockpit electronics bed in the first-person view.</summary>
    public const float CockpitLevel = 0.32f;

    /// <summary>The engine heard from the cockpit: the thrust and the afterburner are held back by these factors.</summary>
    public const float CockpitThrustDamp = 0.45f;

    /// <summary>The afterburner heard from the cockpit.</summary>
    public const float CockpitBoostDamp = 0.5f;

    /// <summary>The idle heard from the cockpit — barely lower: the hull carries the low end.</summary>
    public const float CockpitIdleDamp = 0.9f;

    /// <summary>The cockpit's darker engine: the pitch factor in first person.</summary>
    public const float CockpitPitch = 0.93f;

    /// <summary>The idle under a scripted sequence (the cinematic cues carry those).</summary>
    public const float SequenceIdleLevel = 0.13f;

    /// <summary>The parked ship heard on a spacewalk.</summary>
    public const float EvaIdleLevel = 0.1f;

    /// <summary>How much the drive raises the engine pitch (the old loop rose by 0.2 at full throttle).</summary>
    public const float DrivePitch = 0.16f;

    /// <summary>The extra pitch of an afterburner surge.</summary>
    public const float BoostPitch = 0.05f;

    /// <summary>Easing time constants (seconds) of the layers: the thrust swells quicker than it settles, the afterburner
    /// kicks in fast and dies away slowly.</summary>
    public const float IdleSeconds = 0.6f;
    public const float ThrustRiseSeconds = 0.35f;
    public const float ThrustFallSeconds = 0.8f;
    public const float BoostRiseSeconds = 0.25f;
    public const float BoostFallSeconds = 0.9f;
    public const float CockpitSeconds = 0.5f;
    public const float PitchSeconds = 0.4f;

    /// <summary>A manoeuvring-thruster puff, outside and in the cockpit (where the hull muffles it).</summary>
    public const float RcsVolumeExterior = 0.45f;
    public const float RcsVolumeCockpit = 0.3f;

    /// <summary>The turn rate (degrees per second) that counts as a full turn input for the RCS puffs.</summary>
    public const float FullTurnRate = 120f;

    /// <summary>The stand-in "speed of sound" of the hand-made Doppler, in flight units per second. Ships cruise at
    /// 14–30 units/s, so a head-on pass bends the pitch by roughly a quarter — clearly heard, never cartoonish.</summary>
    public const float DopplerSoundSpeed = 140f;

    /// <summary>The Doppler pitch factor is held inside this band.</summary>
    public const float DopplerMin = 0.8f;
    public const float DopplerMax = 1.25f;

    /// <summary>Another ship's engine at close range, before its size and the distance fade.</summary>
    public const float RemoteEngineLevel = 0.3f;

    /// <summary>Within this distance another ship's engine plays at its full level.</summary>
    public const float RemoteEngineNear = 6f;

    /// <summary>Other ships' engines heard from the own cockpit — or through the suit on a spacewalk — are held back by
    /// this factor.</summary>
    public const float RemoteMuffledDamp = 0.7f;

    /// <summary>The target levels of the own ship's engine layers. <paramref name="throttle"/> is the forward input
    /// (0..1, its magnitude — reverse thrust is thrust too), <paramref name="speed"/> the ship's normalised speed
    /// (0..1, a strafe moves the ship without the throttle), <paramref name="boost"/> a running afterburner surge and
    /// <paramref name="firstPerson"/> the cockpit view. Out-of-range or non-finite inputs are clamped (NaN reads 0).</summary>
    public static FlightEngineLevels Target(FlightEnginePhase phase, float throttle, float speed, bool boost, bool firstPerson)
    {
        switch (phase)
        {
            case FlightEnginePhase.Off:
                return new FlightEngineLevels(0f, 0f, 0f, 0f, 1f);
            case FlightEnginePhase.Eva:
                return new FlightEngineLevels(EvaIdleLevel, 0f, 0f, 0f, 0.92f);
            case FlightEnginePhase.Sequence:
                return new FlightEngineLevels(SequenceIdleLevel, 0f, 0f, 0f, 1f);
        }

        float drive = Drive(throttle, speed);
        float idle = IdleLevel - IdleDuck * drive;
        float thrust = ThrustLevel * MathF.Pow(drive, 1.5f); // a gentle start: a nudge of the stick is a murmur, not a roar
        float burner = boost ? BoostLevel * MathF.Max(drive, 0.5f) : 0f;
        float pitch = 1f + DrivePitch * drive + (boost ? BoostPitch : 0f);
        if (!firstPerson)
        {
            return new FlightEngineLevels(idle, thrust, burner, 0f, pitch);
        }

        return new FlightEngineLevels(idle * CockpitIdleDamp, thrust * CockpitThrustDamp, burner * CockpitBoostDamp, CockpitLevel,
            pitch * CockpitPitch);
    }

    /// <summary>How hard the engine works: the throttle, or the ship's speed when it is moving without it (a strafe).</summary>
    public static float Drive(float throttle, float speed) => MathF.Max(Clamp01(MathF.Abs(Finite(throttle))), 0.8f * Clamp01(Finite(speed)));

    /// <summary>The RCS puff volume for the view.</summary>
    public static float RcsVolume(bool firstPerson) => firstPerson ? RcsVolumeCockpit : RcsVolumeExterior;

    /// <summary>Eases <paramref name="current"/> toward <paramref name="target"/>: an exponential approach with the time
    /// constant <paramref name="riseSeconds"/> going up and <paramref name="fallSeconds"/> going down, frame-rate
    /// independent (two half frames land where one whole frame does). A non-finite current value snaps to the target; a
    /// zero, negative or non-finite step changes nothing.</summary>
    public static float Ease(float current, float target, float dt, float riseSeconds, float fallSeconds)
    {
        target = Finite(target);
        if (!IsFinite(current))
        {
            return target;
        }

        if (!IsFinite(dt) || dt <= 0f)
        {
            return current;
        }

        float tau = target >= current ? riseSeconds : fallSeconds;
        if (!IsFinite(tau) || tau <= 0f)
        {
            return target;
        }

        return target + (current - target) * MathF.Exp(-dt / tau);
    }

    /// <summary>A turn of <paramref name="degrees"/> in a frame of <paramref name="dt"/> seconds as an RCS axis value
    /// (−1..1, <see cref="FullTurnRate"/> = 1). Zero for an empty frame.</summary>
    public static float TurnAxis(float degrees, float dt)
    {
        if (!IsFinite(degrees) || !IsFinite(dt) || dt <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(degrees / dt / FullTurnRate, -1f, 1f);
    }

    /// <summary>How fast a sound source closes in on the listener, in units per second (negative = moving apart):
    /// (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>) is the source minus the listener position,
    /// (<paramref name="vx"/>, <paramref name="vy"/>, <paramref name="vz"/>) the source's velocity minus the listener's.
    /// Zero when both sit on the same spot.</summary>
    public static float ClosingSpeed(float dx, float dy, float dz, float vx, float vy, float vz)
    {
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!IsFinite(len) || len < 1e-3f)
        {
            return 0f;
        }

        float along = (vx * dx + vy * dy + vz * dz) / len;
        return IsFinite(along) ? -along : 0f;
    }

    /// <summary>The Doppler pitch factor for a source closing in at <paramref name="closingSpeed"/> units per second
    /// (approaching = higher, receding = lower), held inside <see cref="DopplerMin"/>..<see cref="DopplerMax"/>.</summary>
    public static float DopplerPitch(float closingSpeed)
    {
        float v = Math.Clamp(Finite(closingSpeed), -0.9f * DopplerSoundSpeed, 0.9f * DopplerSoundSpeed);
        return Math.Clamp(DopplerSoundSpeed / (DopplerSoundSpeed - v), DopplerMin, DopplerMax);
    }

    /// <summary>The engine size of a server space entity's kind (1 = a small ship like the player's starter); 0 for a
    /// kind without an engine (stations, rocks, drops, pods, anomalies).</summary>
    public static float EngineSizeForKind(string? kind) => kind switch
    {
        SpaceTargeting.Drone => 0.6f,
        SpaceTargeting.Ufo => 0.9f,
        SpaceTargeting.HunterShuttle => 1f,
        SpaceTargeting.BanditShip => 1.1f,
        SpaceTargeting.Cruiser => 1.8f,
        _ => 0f,
    };

    /// <summary>The engine size of a ship whose drawn hull reaches <paramref name="extent"/> units from its centre (half
    /// its box diagonal): 1 for the stand-in hull other pilots fly before their design arrives (≈2 units), clamped to
    /// 0.5..3. Unknown (zero or non-finite) reads 1.</summary>
    public static float SizeFromExtent(float extent)
        => IsFinite(extent) && extent > 0f ? Math.Clamp(extent / 2f, 0.5f, 3f) : 1f;

    /// <summary>Another ship's engine pitch: a big hull rumbles lower, a drone whines higher (0.7..1.3).</summary>
    public static float RemoteEnginePitch(float size)
        => IsFinite(size) && size > 0f ? Math.Clamp(1f / MathF.Sqrt(size), 0.7f, 1.3f) : 1f;

    /// <summary>Another ship's engine level at <paramref name="distance"/> (before the effects volume): full within
    /// <see cref="RemoteEngineNear"/>, fading out quadratically to silence at <paramref name="range"/>, louder for a
    /// bigger hull and held back when <paramref name="muffled"/> (heard from the cockpit or through the suit).</summary>
    public static float RemoteEngineVolume(float distance, float range, float size, bool muffled)
    {
        if (!IsFinite(distance) || !IsFinite(range) || range <= RemoteEngineNear || distance >= range)
        {
            return 0f;
        }

        float near = Clamp01(1f - (distance - RemoteEngineNear) / (range - RemoteEngineNear));
        float loud = IsFinite(size) && size > 0f ? Math.Clamp(0.75f + 0.25f * size, 0.6f, 1.3f) : 1f;
        float level = RemoteEngineLevel * loud * near * near;
        return muffled ? level * RemoteMuffledDamp : level;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    private static float Finite(float v) => IsFinite(v) ? v : 0f;

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
