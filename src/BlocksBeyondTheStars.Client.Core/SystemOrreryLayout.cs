// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>One body on the travel screen's small system chart (#2745): where its dot goes and how its orbit ring is drawn.
/// Chart units from the star (the chart's centre), +<see cref="Y"/> = the system's +z — the flight chart's orientation.</summary>
public readonly struct OrreryMarker
{
    public OrreryMarker(int index, string id, float x, float y, float dotRadius, float ringRadius, string hostId)
    {
        Index = index;
        Id = id;
        X = x;
        Y = y;
        DotRadius = dotRadius;
        RingRadius = ringRadius;
        HostId = hostId;
    }

    /// <summary>Index of the body in the list the layout was built from.</summary>
    public int Index { get; }

    public string Id { get; }
    public float X { get; }
    public float Y { get; }
    public float DotRadius { get; }

    /// <summary>The radius of the body's orbit ring around the star, through its dot; 0 = no ring (a moon, a station, a
    /// belt member, or an orbit too small to read).</summary>
    public float RingRadius { get; }

    /// <summary>The body this one is drawn beside (a moon's planet, a station hanging over one); empty for a body drawn
    /// at its own position.</summary>
    public string HostId { get; }

    public bool IsSatellite => HostId.Length > 0;
}

/// <summary>An asteroid belt on the small system chart: three or more star-orbiting asteroid bodies sharing one annulus,
/// drawn as one band (chart radii) instead of a ring each — the flight chart's rule.</summary>
public readonly struct OrreryBelt
{
    public OrreryBelt(float inner, float outer)
    {
        Inner = inner;
        Outer = outer;
    }

    public float Inner { get; }
    public float Outer { get; }
}

/// <summary>The result of <see cref="SystemOrreryLayout.Layout"/>.</summary>
public sealed class OrreryLayout
{
    public OrreryLayout(float scale, IReadOnlyList<OrreryMarker> markers, IReadOnlyList<OrreryBelt> belts)
    {
        Scale = scale;
        Markers = markers;
        Belts = belts;
    }

    /// <summary>Flight-scene units → chart units.</summary>
    public float Scale { get; }

    /// <summary>One marker per (non-null) body, in the order of the input list.</summary>
    public IReadOnlyList<OrreryMarker> Markers { get; }

    public IReadOnlyList<OrreryBelt> Belts { get; }
}

/// <summary>
/// The travel screen's small system chart (#2745), laid out from the star map's bodies like the flight system chart
/// (<c>SpaceMap</c>, #623): every body at its real system position × <see cref="SystemBodyLayout.FlightViewScale"/>, the
/// chart centred on the star and fitted with <see cref="SystemChartLayout.FitScale"/>, an orbit ring through every
/// star-orbiting body (<see cref="SystemChartLayout.OrbitRadius"/>/<see cref="SystemChartLayout.ShowRing"/>), a belt as
/// one band. It used to invent its orbits from the list index and set the dots circling at made-up speeds.
/// <para>What the flight chart solves with its scene layout, this chart solves itself: a moon's real offset (90 system
/// units) and a station's (500) are a few pixels here, inside their planet's dot. So a moon — and a station or wreck
/// whose dot would touch a planet's — is drawn on a small ring just outside that planet's dot, in the direction it really
/// lies, spread so no two touch. The fit holds that ring back, so every dot stays on the chart.</para>
/// <para>Pure and deterministic (no string hashing): the same bodies always give the same chart.</para>
/// </summary>
public static class SystemOrreryLayout
{
    /// <summary>Clear space between a satellite's dot and its host's, and between two satellites, in chart units.</summary>
    public const float SatelliteGap = 3f;

    /// <summary>Asteroid bodies whose orbit radii lie this close (flight-scene units) share a belt — the flight chart's
    /// <c>SpaceMap.BeltGroupGap</c>.</summary>
    public const float BeltGroupGap = 160f * SystemBodyLayout.FlightViewScale;

    /// <summary>How many asteroid bodies in one annulus make a belt.</summary>
    public const int BeltMinMembers = 3;

    private const float Tau = (float)(Math.PI * 2.0);

    /// <summary>Whether the body circles the star itself (and gets an orbit ring): planets and the landable asteroid
    /// bodies without a parent — the flight chart's rule. A moon carries its planet's id.</summary>
    public static bool OrbitsStar(NetBody? body)
        => body != null && string.IsNullOrEmpty(body.ParentId) && (body.Kind == "Planet" || body.Kind == "AsteroidField");

    /// <summary>Lays a system out on a round chart of <paramref name="chartHalf"/> (chart units from the star to the rim).
    /// <paramref name="dotRadius"/> is the drawn radius of a body's dot (chart units, independent of the fit);
    /// <paramref name="markerPad"/> is extra room every dot needs around it (a halo).</summary>
    public static OrreryLayout Layout(IReadOnlyList<NetBody?> bodies, float chartHalf, float markerPad, Func<NetBody, float> dotRadius)
    {
        int n = bodies.Count;
        var x = new float[n];
        var z = new float[n];
        var dot = new float[n];
        var primary = new bool[n];
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var b = bodies[i];
            if (b == null)
            {
                continue;
            }

            x[i] = b.SystemX * SystemBodyLayout.FlightViewScale;
            z[i] = b.SystemZ * SystemBodyLayout.FlightViewScale;
            dot[i] = Math.Max(0f, dotRadius(b));
            primary[i] = OrbitsStar(b);
            if (!string.IsNullOrEmpty(b.Id) && !byId.ContainsKey(b.Id))
            {
                byId[b.Id] = i;
            }
        }

        // A body whose parent is a star-orbiting body is that body's satellite (a moon, a derelict hanging over it).
        var host = new int[n];
        for (int i = 0; i < n; i++)
        {
            host[i] = -1;
            var b = bodies[i];
            if (b != null && !primary[i] && !string.IsNullOrEmpty(b.ParentId) && byId.TryGetValue(b.ParentId, out int h) && primary[h])
            {
                host[i] = h;
            }
        }

        // The fit and the free bodies (stations, wrecks, debris fields) depend on each other: a free body whose dot would
        // touch a planet's joins that planet's satellites, which holds back more room, which shrinks the chart, which can
        // bring the next one into touch. Joining only ever adds, so this settles within one pass per free body.
        float scale = 1f;
        var ringOf = new float[n];
        for (int pass = 0; pass <= n; pass++)
        {
            float pad = SatelliteRings(host, dot, ringOf);
            for (int i = 0; i < n; i++)
            {
                if (bodies[i] != null && host[i] < 0)
                {
                    pad = Math.Max(pad, dot[i]);
                }
            }

            pad += markerPad;
            scale = SystemChartLayout.FitScale(chartHalf, OwnPositions(x, bodies, host), OwnPositions(z, bodies, host), 0f, 0f, pad);

            bool joined = false;
            for (int i = 0; i < n; i++)
            {
                if (bodies[i] == null || primary[i] || host[i] >= 0)
                {
                    continue;
                }

                int nearest = NearestPrimary(i, x, z, primary, bodies);
                if (nearest >= 0)
                {
                    float dx = (x[i] - x[nearest]) * scale, dz = (z[i] - z[nearest]) * scale;
                    if (Math.Sqrt((dx * dx) + (dz * dz)) < dot[i] + dot[nearest] + SatelliteGap)
                    {
                        host[i] = nearest;
                        joined = true;
                    }
                }
            }

            if (!joined)
            {
                break;
            }
        }

        // Place: bodies of their own at their position, satellites on their host's ring, spread.
        var px = new float[n];
        var py = new float[n];
        for (int i = 0; i < n; i++)
        {
            px[i] = x[i] * scale;
            py[i] = z[i] * scale;
        }

        PlaceSatellites(host, x, z, dot, ringOf, bodies, px, py);

        var belts = new List<OrreryBelt>();
        var beltMember = Belts(bodies, primary, x, z, scale, belts);

        var markers = new List<OrreryMarker>(n);
        for (int i = 0; i < n; i++)
        {
            var b = bodies[i];
            if (b == null)
            {
                continue;
            }

            float ring = 0f;
            if (primary[i] && !beltMember[i])
            {
                float r = SystemChartLayout.OrbitRadius(px[i], py[i]);
                ring = SystemChartLayout.ShowRing(r) ? r : 0f;
            }

            markers.Add(new OrreryMarker(i, b.Id ?? string.Empty, px[i], py[i], dot[i], ring,
                host[i] >= 0 ? bodies[host[i]]!.Id ?? string.Empty : string.Empty));
        }

        return new OrreryLayout(scale, markers, belts);
    }

    /// <summary>Fills <paramref name="ringOf"/> with the ring radius (chart units, from the host's centre) of every host with
    /// satellites: just outside the host's dot, and wide enough that neighbouring satellites, spread evenly, never touch.
    /// Returns the room the widest ring plus its dots needs beyond a host's position — 0 without satellites.</summary>
    private static float SatelliteRings(int[] host, float[] dot, float[] ringOf)
    {
        int n = host.Length;
        Array.Clear(ringOf, 0, n);
        var count = new int[n];
        var widest = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (host[i] >= 0)
            {
                count[host[i]]++;
                widest[host[i]] = Math.Max(widest[host[i]], dot[i]);
            }
        }

        float reach = 0f;
        for (int h = 0; h < n; h++)
        {
            if (count[h] == 0)
            {
                continue;
            }

            float chord = (2f * widest[h]) + SatelliteGap;
            float ring = dot[h] + widest[h] + SatelliteGap;
            if (count[h] > 1)
            {
                ring = Math.Max(ring, chord / (2f * (float)Math.Sin(Math.PI / count[h])));
            }

            ringOf[h] = ring;
            reach = Math.Max(reach, ring + widest[h]);
        }

        return reach;
    }

    /// <summary>The coordinates of the bodies drawn at their own position (not beside a host) — what the fit measures.</summary>
    private static float[] OwnPositions(float[] values, IReadOnlyList<NetBody?> bodies, int[] host)
    {
        var list = new List<float>(values.Length);
        for (int i = 0; i < values.Length; i++)
        {
            if (bodies[i] != null && host[i] < 0)
            {
                list.Add(values[i]);
            }
        }

        return list.ToArray();
    }

    private static int NearestPrimary(int i, float[] x, float[] z, bool[] primary, IReadOnlyList<NetBody?> bodies)
    {
        int best = -1;
        float bestSq = float.MaxValue;
        for (int j = 0; j < x.Length; j++)
        {
            if (!primary[j] || bodies[j] == null)
            {
                continue;
            }

            float dx = x[i] - x[j], dz = z[i] - z[j];
            float sq = (dx * dx) + (dz * dz);
            if (sq < bestSq || (sq == bestSq && best >= 0 && string.CompareOrdinal(bodies[j]!.Id, bodies[best]!.Id) < 0))
            {
                bestSq = sq;
                best = j;
            }
        }

        return best;
    }

    /// <summary>Puts every satellite on its host's ring, at the angle it really lies at from the host, then spreads the
    /// angles so neighbours keep a dot and a gap apart (evenly, when the real angles crowd too much).</summary>
    private static void PlaceSatellites(int[] host, float[] x, float[] z, float[] dot, float[] ringOf, IReadOnlyList<NetBody?> bodies,
        float[] px, float[] py)
    {
        int n = host.Length;
        for (int h = 0; h < n; h++)
        {
            if (ringOf[h] <= 0f)
            {
                continue;
            }

            var members = new List<(float Angle, string Id, int Index)>();
            float widest = 0f;
            for (int i = 0; i < n; i++)
            {
                if (host[i] != h)
                {
                    continue;
                }

                float dx = x[i] - x[h], dz = z[i] - z[h];
                // No offset at all (a degenerate save): point away from the star, the way a moon is usually read.
                float angle = (dx * dx) + (dz * dz) > 1e-6f ? (float)Math.Atan2(dz, dx)
                    : (x[h] * x[h]) + (z[h] * z[h]) > 1e-6f ? (float)Math.Atan2(z[h], x[h]) : 0f;
                members.Add((Wrap(angle), bodies[i]!.Id ?? string.Empty, i));
                widest = Math.Max(widest, dot[i]);
            }

            members.Sort((a, b) =>
            {
                int c = a.Angle.CompareTo(b.Angle);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });

            // The angle two neighbours need between them (chord = a dot plus the gap); the ring is wide enough that
            // members.Count of these fit around it.
            float ring = ringOf[h];
            float step = 2f * (float)Math.Asin(Math.Min(1.0, ((2f * widest) + SatelliteGap) / (2f * ring)));
            var angles = new float[members.Count];
            for (int k = 0; k < members.Count; k++)
            {
                angles[k] = k == 0 ? members[0].Angle : Math.Max(members[k].Angle, angles[k - 1] + step);
            }

            if (members.Count > 1 && angles[members.Count - 1] - angles[0] > Tau - step + 1e-4f)
            {
                for (int k = 0; k < members.Count; k++)
                {
                    angles[k] = members[0].Angle + (k * Tau / members.Count);
                }
            }

            for (int k = 0; k < members.Count; k++)
            {
                int i = members[k].Index;
                px[i] = px[h] + (ring * (float)Math.Cos(angles[k]));
                py[i] = py[h] + (ring * (float)Math.Sin(angles[k]));
            }
        }
    }

    /// <summary>Groups the star-orbiting asteroid bodies by orbit radius; <see cref="BeltMinMembers"/> or more within
    /// <see cref="BeltGroupGap"/> of each other are one belt. Returns which bodies are belt members.</summary>
    private static bool[] Belts(IReadOnlyList<NetBody?> bodies, bool[] primary, float[] x, float[] z, float scale, List<OrreryBelt> belts)
    {
        int n = bodies.Count;
        var member = new bool[n];
        var fields = new List<(float R, string Id, int Index)>();
        for (int i = 0; i < n; i++)
        {
            if (primary[i] && bodies[i]!.Kind == "AsteroidField")
            {
                fields.Add(((float)Math.Sqrt((x[i] * x[i]) + (z[i] * z[i])), bodies[i]!.Id ?? string.Empty, i));
            }
        }

        fields.Sort((a, b) =>
        {
            int c = a.R.CompareTo(b.R);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        int start = 0;
        for (int k = 1; k <= fields.Count; k++)
        {
            if (k < fields.Count && fields[k].R - fields[k - 1].R <= BeltGroupGap)
            {
                continue;
            }

            if (k - start >= BeltMinMembers)
            {
                belts.Add(new OrreryBelt(fields[start].R * scale, fields[k - 1].R * scale));
                for (int m = start; m < k; m++)
                {
                    member[fields[m].Index] = true;
                }
            }

            start = k;
        }

        return member;
    }

    private static float Wrap(float angle)
    {
        while (angle < 0f)
        {
            angle += Tau;
        }

        while (angle >= Tau)
        {
            angle -= Tau;
        }

        return angle;
    }
}
