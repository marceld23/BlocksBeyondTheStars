// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>One body of the cockpit's holographic system map (#4055): what it circles, how far from it, the direction it
/// lies in at second 0 and how fast it travels. Chart units and axes are <see cref="SystemOrreryLayout"/>'s.</summary>
public readonly struct HoloBody
{
    public HoloBody(int index, string id, int host, float radius, float angle, float speed, float dotRadius, float ringRadius, bool active)
    {
        Index = index;
        Id = id;
        Host = host;
        Radius = radius;
        Angle = angle;
        Speed = speed;
        DotRadius = dotRadius;
        RingRadius = ringRadius;
        Active = active;
    }

    /// <summary>Index of the body in the list the map was built from.</summary>
    public int Index { get; }

    public string Id { get; }

    /// <summary>The body this one circles — its position in <see cref="HoloSystemMap.Bodies"/> (a moon's planet, a station
    /// hanging over one); -1 = the star at the chart's centre.</summary>
    public int Host { get; }

    /// <summary>Its distance from what it circles.</summary>
    public float Radius { get; }

    /// <summary>The direction it lies in from what it circles at second 0, in radians (0 = the chart's +x, growing toward
    /// +y) — where the travel chart draws it.</summary>
    public float Angle { get; }

    /// <summary>Radians per second, growing <see cref="Angle"/> when positive; 0 = it keeps its place.</summary>
    public float Speed { get; }

    public float DotRadius { get; }

    /// <summary>The radius of its orbit ring around the star, through its dot at every second; 0 = no ring.</summary>
    public float RingRadius { get; }

    /// <summary>The body the player is at: drawn bigger (already in <see cref="DotRadius"/>) and brighter.</summary>
    public bool Active { get; }
}

/// <summary>
/// The cockpit's holographic system map (#4055): the system the ship is in, laid out by <see cref="SystemOrreryLayout"/>
/// — the travel screen's chart, so at second 0 every body stands where that chart shows it — and then set travelling.
/// The hologram used to take the first five entries of the body list, whatever they were, put them at distances made up
/// from the list index and angles hashed from their ids, and turn them all together.
/// <para>
/// <b>Travelling.</b> A body that circles the star moves along its ring, a moon around its planet, in the direction and
/// at a pace taken from its orbital period (<see cref="NetBody.OrbitPeriodDays"/>, negative = the other way). A real
/// orbit takes one to seven hours, and a moon's is a hundred times shorter than an outer planet's, so the time lapse
/// compresses the range (<see cref="OrbitSeconds"/>): every planet visibly moves and no moon whirls. The satellites of
/// one body turn together, and so do the members of a belt — they share a ring or a band, and at their own paces their
/// dots would run through each other. A body without a period (a station, a wreck) keeps its place, beside its planet
/// when it hangs over one. In flight space the bodies stand still (the period only drives the sky), so after a while the
/// hologram no longer matches the flight chart; that is the price of seeing the planets travel.
/// </para>
/// <para>Nothing leaves the chart: a ring keeps its radius, and the layout's fit already holds back the room a planet's
/// satellites need around it. Pure and deterministic like the layout.</para>
/// </summary>
public sealed class HoloSystemMap
{
    /// <summary>Chart units from the star to the rim. The layout's own measures (the gap between a moon and its planet,
    /// the smallest ring worth drawing) are made for a chart of about this size.</summary>
    public const float ChartHalf = 100f;

    /// <summary>The star's radius, and the dot radii by kind: a planet, a giant planet, a moon or an asteroid body, and
    /// everything else (a station, a wreck, a debris field).</summary>
    public const float StarRadius = 10f, PlanetDot = 6f, GiantDot = 9f, MoonDot = 4f, SmallDot = 3f;

    /// <summary>How much bigger the body the player is at is drawn.</summary>
    public const float ActiveScale = 1.4f;

    /// <summary>Clear chart units every dot keeps to the rim.</summary>
    public const float MarkerPad = 2f;

    /// <summary>The time lapse: an orbit of one day takes this many seconds, and the time grows with the square root of
    /// the period.</summary>
    public const float SecondsPerRootDay = 12f;

    /// <summary>The fastest and the slowest orbit around the star, and around a planet, in seconds.</summary>
    public const float MinStarOrbitSeconds = 24f, MaxStarOrbitSeconds = 90f, MinMoonOrbitSeconds = 6f, MaxMoonOrbitSeconds = 20f;

    private const float Tau = (float)(Math.PI * 2.0);

    private readonly HoloBody[] _bodies;

    private HoloSystemMap(HoloBody[] bodies, IReadOnlyList<OrreryBelt> belts)
    {
        _bodies = bodies;
        Belts = belts;
    }

    /// <summary>One entry per (non-null) body, in the order of the list the map was built from.</summary>
    public IReadOnlyList<HoloBody> Bodies => _bodies;

    /// <summary>The asteroid belts, each one band around the star.</summary>
    public IReadOnlyList<OrreryBelt> Belts { get; }

    /// <summary>The system the ship is in — the one holding the star map's active location — or null while the map does
    /// not know it. No other system stands in: the hologram then shows the star alone.</summary>
    public static NetStarSystem? ActiveSystem(StarMapData? map) => FlightStar.SystemOf(map, map?.ActiveLocationId);

    /// <summary>The radius of a body's dot: by kind, and bigger for the body the player is at.</summary>
    public static float DotRadius(NetBody body, string? activeBodyId)
    {
        float radius = body.Kind switch
        {
            "Planet" => body.PlanetType == "gas_giant" || BodySize.LooksGiant(body) ? GiantDot : PlanetDot,
            "Moon" or "AsteroidField" => MoonDot,
            _ => SmallDot,
        };
        return IsActive(body, activeBodyId) ? radius * ActiveScale : radius;
    }

    /// <summary>How many seconds one orbit takes in the hologram for an orbital period of <paramref name="periodDays"/>
    /// (its sign does not matter here): <see cref="SecondsPerRootDay"/> × √days, held between the fastest and the slowest
    /// orbit of its kind — around the star, or around a planet. A longer period never gives a shorter orbit. 0 for a body
    /// without a period.</summary>
    public static float OrbitSeconds(float periodDays, bool aroundStar)
    {
        if (periodDays == 0f || float.IsNaN(periodDays) || float.IsInfinity(periodDays))
        {
            return 0f;
        }

        float seconds = SecondsPerRootDay * MathF.Sqrt(Math.Abs(periodDays));
        return aroundStar
            ? Math.Clamp(seconds, MinStarOrbitSeconds, MaxStarOrbitSeconds)
            : Math.Clamp(seconds, MinMoonOrbitSeconds, MaxMoonOrbitSeconds);
    }

    /// <summary>Builds the map of a system's <paramref name="bodies"/>; <paramref name="activeBodyId"/> is the body the
    /// player is at.</summary>
    public static HoloSystemMap Build(IReadOnlyList<NetBody?>? bodies, string? activeBodyId)
    {
        bodies ??= Array.Empty<NetBody?>();
        var layout = SystemOrreryLayout.Layout(bodies, ChartHalf, MarkerPad, b => DotRadius(b, activeBodyId));
        var markers = layout.Markers;
        int n = markers.Count;

        // A satellite names its host by id; like the layout, the first body carrying an id owns it.
        var at = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int k = 0; k < n; k++)
        {
            if (markers[k].Id.Length > 0 && !at.ContainsKey(markers[k].Id))
            {
                at[markers[k].Id] = k;
            }
        }

        var host = new int[n];
        for (int k = 0; k < n; k++)
        {
            host[k] = markers[k].IsSatellite && at.TryGetValue(markers[k].HostId, out int h) && h != k ? h : -1;
        }

        var speed = Speeds(bodies, markers, host, layout.Belts.Count);
        var list = new HoloBody[n];
        for (int k = 0; k < n; k++)
        {
            var m = markers[k];
            float dx = m.X, dy = m.Y;
            if (host[k] >= 0)
            {
                dx -= markers[host[k]].X;
                dy -= markers[host[k]].Y;
            }

            float radius = MathF.Sqrt((dx * dx) + (dy * dy));
            float angle = radius > 1e-6f ? MathF.Atan2(dy, dx) : 0f;
            list[k] = new HoloBody(m.Index, m.Id, host[k], radius, angle, speed[k], m.DotRadius, m.RingRadius,
                IsActive(bodies[m.Index]!, activeBodyId));
        }

        return new HoloSystemMap(list, layout.Belts);
    }

    /// <summary>Where a body is after <paramref name="seconds"/>, in chart units from the star.</summary>
    public void PositionAt(int body, float seconds, out float x, out float y)
    {
        var b = _bodies[body];
        float angle = b.Angle + (b.Speed * seconds);
        x = b.Radius * MathF.Cos(angle);
        y = b.Radius * MathF.Sin(angle);
        if (b.Host >= 0)
        {
            // A host circles the star itself, so this goes one step deep.
            var h = _bodies[b.Host];
            float hostAngle = h.Angle + (h.Speed * seconds);
            x += h.Radius * MathF.Cos(hostAngle);
            y += h.Radius * MathF.Sin(hostAngle);
        }
    }

    /// <summary>Each marker's speed in radians per second. A planet takes its own period; the satellites of one body
    /// share the longest period among them, and so do the members of a belt, so each group turns as one.</summary>
    private static float[] Speeds(IReadOnlyList<NetBody?> bodies, IReadOnlyList<OrreryMarker> markers, int[] host, int belts)
    {
        int n = markers.Count;
        var familyPeriod = new float[n];
        var beltPeriod = new float[belts];
        for (int k = 0; k < n; k++)
        {
            float period = bodies[markers[k].Index]!.OrbitPeriodDays;
            if (host[k] >= 0)
            {
                familyPeriod[host[k]] = Longer(familyPeriod[host[k]], period);
            }
            else if (markers[k].Belt >= 0)
            {
                beltPeriod[markers[k].Belt] = Longer(beltPeriod[markers[k].Belt], period);
            }
        }

        var speed = new float[n];
        for (int k = 0; k < n; k++)
        {
            if (host[k] >= 0)
            {
                speed[k] = SpeedOf(familyPeriod[host[k]], aroundStar: false);
            }
            else if (markers[k].Belt >= 0)
            {
                speed[k] = SpeedOf(beltPeriod[markers[k].Belt], aroundStar: true);
            }
            else if (SystemOrreryLayout.OrbitsStar(bodies[markers[k].Index]))
            {
                speed[k] = SpeedOf(bodies[markers[k].Index]!.OrbitPeriodDays, aroundStar: true);
            }
        }

        return speed;
    }

    /// <summary>The period of the two that takes longer (the first on a tie); a body without one never wins.</summary>
    private static float Longer(float current, float period)
        => !float.IsNaN(period) && !float.IsInfinity(period) && Math.Abs(period) > Math.Abs(current) ? period : current;

    private static float SpeedOf(float periodDays, bool aroundStar)
    {
        float seconds = OrbitSeconds(periodDays, aroundStar);
        return seconds <= 0f ? 0f : (periodDays < 0f ? -Tau : Tau) / seconds;
    }

    private static bool IsActive(NetBody body, string? activeBodyId)
        => !string.IsNullOrEmpty(activeBodyId) && body.Id == activeBodyId;
}
