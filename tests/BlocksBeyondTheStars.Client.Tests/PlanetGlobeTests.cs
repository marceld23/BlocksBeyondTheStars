// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The planet globe (#3127): the own sphere mesh without the pole wedges — counts, shape, seam, pole vertices at their
/// segment centres, outward winding and the mapping convention <see cref="PlanetSpin"/> turns it by — and its polar
/// caps (#3128) in the world's ground colour.
/// </summary>
public sealed class PlanetGlobeTests
{
    private static readonly GlobeMeshData Globe = GlobeMeshData.Build(GlobeMeshData.DefaultLonSegments, GlobeMeshData.DefaultLatSegments);

    [Fact]
    public void Globe_Has48x24Segments_WithOnePoleVertexPerSegment()
    {
        const int lon = 48, lat = 24;
        Assert.Equal(lon, Globe.LonSegments);
        Assert.Equal(lat, Globe.LatSegments);
        Assert.Equal(2 * lon + (lat - 1) * (lon + 1), Globe.VertexCount); // 1223 — well inside 16-bit indices
        Assert.Equal(Globe.VertexCount * 3, Globe.Normals.Length);
        Assert.Equal(Globe.VertexCount * 2, Globe.Uvs.Length);
        Assert.Equal(2 * lon * (lat - 1) * 3, Globe.Triangles.Length);

        var used = new bool[Globe.VertexCount];
        foreach (int i in Globe.Triangles)
        {
            Assert.InRange(i, 0, Globe.VertexCount - 1);
            used[i] = true;
        }

        Assert.DoesNotContain(false, used); // no stray vertex
    }

    [Fact]
    public void EveryVertex_SitsOnTheHalfUnitSphere_WithItsNormalPointingOut()
    {
        for (int i = 0; i < Globe.VertexCount; i++)
        {
            var (x, y, z) = Position(i);
            Assert.Equal(0.5, Math.Sqrt(x * x + y * y + z * z), 5);
            Assert.Equal(x * 2.0, Globe.Normals[i * 3], 5);
            Assert.Equal(y * 2.0, Globe.Normals[i * 3 + 1], 5);
            Assert.Equal(z * 2.0, Globe.Normals[i * 3 + 2], 5);

            // v is linear in latitude, like the map's rows: v = 0 the south pole, 1 the north pole.
            var (_, v) = Uv(i);
            Assert.Equal(Math.Sin((v - 0.5) * Math.PI) * 0.5, y, 5);
        }
    }

    [Fact]
    public void SeamColumn_IsDuplicated_AtUZeroAndOne()
    {
        for (int ring = 1; ring < Globe.LatSegments; ring++)
        {
            int first = Globe.RingVertex(ring, 0), last = Globe.RingVertex(ring, Globe.LonSegments);
            Assert.Equal(Position(first), Position(last));
            Assert.Equal(Uv(first).V, Uv(last).V);
            Assert.Equal(0f, Uv(first).U);
            Assert.Equal(1f, Uv(last).U);
        }
    }

    [Fact]
    public void PoleVertices_SitAtTheirSegmentCentre_SoNoPoleTriangleShearsTheMap()
    {
        for (int s = 0; s < Globe.LonSegments; s++)
        {
            double centre = (s + 0.5) / Globe.LonSegments;
            Assert.Equal((0.0, -0.5, 0.0), Position(Globe.SouthPole(s)));
            Assert.Equal((0.0, 0.5, 0.0), Position(Globe.NorthPole(s)));
            Assert.Equal(centre, Uv(Globe.SouthPole(s)).U, 6);
            Assert.Equal(centre, Uv(Globe.NorthPole(s)).U, 6);
            Assert.Equal(0f, Uv(Globe.SouthPole(s)).V);
            Assert.Equal(1f, Uv(Globe.NorthPole(s)).V);
        }

        // Every triangle touching a pole: the pole's u is the midpoint of the two ring vertices beside it.
        int poleTriangles = 0;
        for (int t = 0; t < Globe.Triangles.Length; t += 3)
        {
            var corners = new[] { Globe.Triangles[t], Globe.Triangles[t + 1], Globe.Triangles[t + 2] };
            var pole = Array.FindIndex(corners, IsPole);
            if (pole < 0)
            {
                continue;
            }

            poleTriangles++;
            var ring = new List<float>();
            foreach (int c in corners)
            {
                if (!IsPole(c))
                {
                    ring.Add(Uv(c).U);
                }
            }

            Assert.Equal(2, ring.Count);
            Assert.Equal(1f / Globe.LonSegments, Math.Abs(ring[1] - ring[0]), 5);
            Assert.Equal((ring[0] + ring[1]) / 2f, Uv(corners[pole]).U, 5);
        }

        Assert.Equal(2 * Globe.LonSegments, poleTriangles);
    }

    [Fact]
    public void EveryTriangle_FacesOutward_UnderUnitysClockwiseFront()
    {
        // Unity draws a triangle's front on the side Cross(b − a, c − a) points to (a clockwise triangle on screen);
        // the SkyBodyPhase and PlanetLimb shaders cull back faces, so every front must face away from the centre.
        for (int t = 0; t < Globe.Triangles.Length; t += 3)
        {
            var a = Position(Globe.Triangles[t]);
            var b = Position(Globe.Triangles[t + 1]);
            var c = Position(Globe.Triangles[t + 2]);
            var (ex, ey, ez) = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            var (fx, fy, fz) = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
            var (nx, ny, nz) = (ey * fz - ez * fy, ez * fx - ex * fz, ex * fy - ey * fx);
            double area = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            Assert.True(area > 1e-6, $"triangle {t / 3} is degenerate");
            double outward = nx * (a.X + b.X + c.X) + ny * (a.Y + b.Y + c.Y) + nz * (a.Z + b.Z + c.Z);
            Assert.True(outward > 0, $"triangle {t / 3} faces inward");
        }
    }

    [Fact]
    public void Mapping_UZeroAtPlusX_AzimuthGrowingWithU_AsPlanetSpinTakesIt()
    {
        Assert.Equal(0.0, GlobeMeshData.SeamAzimuthDeg);
        Assert.Equal(1.0, GlobeMeshData.UvDirection);
        for (int ring = 1; ring < Globe.LatSegments; ring++)
        {
            for (int column = 0; column <= Globe.LonSegments; column++)
            {
                int i = Globe.RingVertex(ring, column);
                var (x, _, z) = Position(i);
                double azimuth = (Math.Atan2(z, x) * 180.0 / Math.PI + 360.0) % 360.0;
                double expected = PlanetSpin.AzimuthOf(Uv(i).U, 0.0, GlobeMeshData.SeamAzimuthDeg, GlobeMeshData.UvDirection);
                double gap = Math.Abs(azimuth - expected) % 360.0;
                Assert.True(Math.Min(gap, 360.0 - gap) < 1e-3, $"column {column}: azimuth {azimuth} vs {expected}");
            }
        }

        // Turned by PlanetSpin's yaw, the noon meridian of the mesh points at the star (here at azimuth 130°).
        const double tod = 0.3, sunAz = 130.0;
        double yaw = PlanetSpin.YawDegrees(tod, sunAz, GlobeMeshData.SeamAzimuthDeg, GlobeMeshData.UvDirection);
        Assert.Equal(sunAz, PlanetSpin.AzimuthOf(PlanetSpin.NoonLongitude(tod), yaw, GlobeMeshData.SeamAzimuthDeg, GlobeMeshData.UvDirection), 6);
    }

    [Fact]
    public void CapFade_IsZeroBelow62_OneFrom72_SmoothAndSymmetricBetween()
    {
        Assert.Equal(0f, PlanetGlobeCaps.Fade(0f));
        Assert.Equal(0f, PlanetGlobeCaps.Fade(61.9f));
        Assert.Equal(0f, PlanetGlobeCaps.Fade(-62f));
        Assert.Equal(1f, PlanetGlobeCaps.Fade(72f));
        Assert.Equal(1f, PlanetGlobeCaps.Fade(-90f));
        Assert.Equal(0.5, PlanetGlobeCaps.Fade(67f), 4);
        float last = 0f;
        for (float lat = 62f; lat <= 72f; lat += 0.25f)
        {
            float f = PlanetGlobeCaps.Fade(lat);
            Assert.True(f >= last, $"fade falls at {lat}°");
            Assert.Equal(f, PlanetGlobeCaps.Fade(-lat));
            last = f;
        }

        // Row latitudes: row 0 the south edge, symmetric about the equator.
        Assert.Equal(-88.125, PlanetGlobeCaps.RowLatitude(0, 48), 3);
        Assert.Equal(88.125, PlanetGlobeCaps.RowLatitude(47, 48), 3);
        Assert.Equal(-PlanetGlobeCaps.RowLatitude(10, 48), PlanetGlobeCaps.RowLatitude(37, 48), 4);
    }

    [Fact]
    public void Caps_PaintThePolarRowsInTheGroundColour_AndLeaveTheRestAndTheSourceAlone()
    {
        const int w = 96, h = 48, ground = 0x336699;
        var source = new byte[w * h * 4];
        new Random(3128).NextBytes(source);
        var original = (byte[])source.Clone();

        var capped = PlanetGlobeCaps.Apply(source, w, h, ground);

        Assert.Equal(original, source); // the landing map shares the raw map — it is never written
        Assert.NotSame(source, capped);
        for (int y = 0; y < h; y++)
        {
            float lat = Math.Abs(PlanetGlobeCaps.RowLatitude(y, h));
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                Assert.Equal(source[o + 3], capped[o + 3]); // alpha kept
                if (lat < PlanetGlobeCaps.BlendStartDeg)
                {
                    Assert.Equal(source[o..(o + 4)], capped[o..(o + 4)]);
                }
                else if (lat >= PlanetGlobeCaps.FullCapDeg)
                {
                    Assert.Equal(new byte[] { 0x33, 0x66, 0x99 }, capped[o..(o + 3)]);
                }
                else
                {
                    AssertBlended(source[o], capped[o], 0x33, PlanetGlobeCaps.Fade(lat));
                    AssertBlended(source[o + 1], capped[o + 1], 0x66, PlanetGlobeCaps.Fade(lat));
                    AssertBlended(source[o + 2], capped[o + 2], 0x99, PlanetGlobeCaps.Fade(lat));
                }
            }
        }
    }

    [Fact]
    public void Caps_BlendGradually_TowardThePoles()
    {
        // A white map under a black cap: the brightness falls row by row toward each pole, never jumps from map to cap.
        const int w = 8, h = 48;
        var white = new byte[w * h * 4];
        Array.Fill(white, (byte)255);
        var capped = PlanetGlobeCaps.Apply(white, w, h, 0x000000);
        int blendRows = 0;
        for (int y = 1; y < h / 2; y++)
        {
            int south = capped[y * w * 4], southward = capped[(y - 1) * w * 4];
            int north = capped[(h - 1 - y) * w * 4], northward = capped[(h - y) * w * 4];
            Assert.True(southward <= south && northward <= north, $"row {y}");
            Assert.Equal(south, north); // both caps alike
            Assert.True(south - southward < 160, $"row {y}: a hard edge of {south - southward}");
            blendRows += south is > 0 and < 255 ? 1 : 0;
        }

        Assert.True(blendRows >= 2, $"only {blendRows} blended rows per cap");
        Assert.Throws<ArgumentException>(() => PlanetGlobeCaps.Apply(new byte[10], w, h, 0));
    }

    [Fact]
    public void PolarAverage_IsTheColourOfTheCapRows_NotOfTheWholeMap()
    {
        // A beige gas sea round the poles and a blue band at the equator: the cap takes the beige, never the blue.
        const int w = 8, h = 48;
        var map = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            bool polar = PlanetGlobeCaps.Fade(PlanetGlobeCaps.RowLatitude(y, h)) > 0f;
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                (map[o], map[o + 1], map[o + 2], map[o + 3]) = polar ? ((byte)0xD8, (byte)0xC0, (byte)0x90, (byte)255) : ((byte)0x20, (byte)0x40, (byte)0xC0, (byte)255);
            }
        }

        Assert.Equal(0xD8C090, PlanetGlobeCaps.PolarAverage(map, w, h, 0x808080));
        Assert.Equal(0x123456, PlanetGlobeCaps.PolarAverage(new byte[3], w, h, 0x123456)); // a broken map keeps the fallback
    }

    private static void AssertBlended(byte from, byte got, byte to, float fade)
    {
        int expected = (int)(from + (to - from) * fade + 0.5f);
        Assert.InRange(got, Math.Min(from, to), Math.Max(from, to));
        Assert.InRange((int)got, expected - 1, expected + 1);
    }

    private static bool IsPole(int vertex) => vertex < Globe.LonSegments || vertex >= Globe.NorthPole(0);

    private static (double X, double Y, double Z) Position(int i)
        => (Globe.Positions[i * 3], Globe.Positions[i * 3 + 1], Globe.Positions[i * 3 + 2]);

    private static (float U, float V) Uv(int i) => (Globe.Uvs[i * 2], Globe.Uvs[i * 2 + 1]);
}
