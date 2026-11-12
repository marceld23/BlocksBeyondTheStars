// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3358: <b>Mirun</b>, the village of the last Sounders — a levelled moss plaza with a ring path of old brick, the bell post
/// in the middle (a carved-obsidian column with four chimes on its arms and a resonance crystal on top), six round-ish huts
/// of stone and reed planks on a ring, their doors toward the plaza and a chime on every roof, three paper lanterns, bell
/// blooms and an echo flower by the post, and a fringe of chime reeds around it all (the reed marsh's plant).
/// </summary>
public sealed partial class SoaniriBuilder
{
    /// <summary>Mirun's footprint radius (what it levels and keeps clear).</summary>
    public const int MirunRadius = 15;

    /// <summary>How many huts stand on Mirun's ring.</summary>
    public const int MirunHuts = 6;

    private const double MirunHutRing = 9.5;

    /// <summary>The centre of hut <paramref name="i"/> on the ring around the plaza.</summary>
    public static (int X, int Z) MirunHutCentre(int cx, int cz, int i)
    {
        double a = (i * Math.PI * 2.0 / MirunHuts) + 0.26;
        return (cx + (int)Math.Round(Math.Sin(a) * MirunHutRing), cz + (int)Math.Round(Math.Cos(a) * MirunHutRing));
    }

    private void BuildMirun(int cx, int floorY, int cz)
    {
        LevelDisc(cx, cz, floorY, MirunRadius, _b.Moss, _b.Dirt);
        BuildMirunPlaza(cx, floorY, cz);
        for (int i = 0; i < MirunHuts; i++)
        {
            var (hx, hz) = MirunHutCentre(cx, cz, i);
            BuildMirunHut(hx, floorY, hz, cx, cz);
        }

        for (int i = 0; i < 3; i++)
        {
            double a = (i * Math.PI * 2.0 / 3.0) + 0.8;
            int lx = cx + (int)Math.Round(Math.Sin(a) * 6.0), lz = cz + (int)Math.Round(Math.Cos(a) * 6.0);
            Column(lx, lz, floorY + 1, floorY + 2, _b.CarvedObsidian);
            Set(lx, floorY + 3, lz, _b.Lantern);
        }

        BuildMirunReeds(cx, floorY, cz);
    }

    /// <summary>The ring path, the bell post with its chimes and crystal, and the little garden of the post.</summary>
    private void BuildMirunPlaza(int cx, int floorY, int cz)
    {
        for (int dx = -5; dx <= 5; dx++)
        {
            for (int dz = -5; dz <= 5; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                if (d2 >= 9 && d2 <= 18)
                {
                    Set(cx + dx, floorY, cz + dz, _b.Brick); // the old ring path
                }
            }
        }

        Column(cx, cz, floorY + 1, floorY + 5, _b.CarvedObsidian);
        foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            Set(cx + ox, floorY + 5, cz + oz, _b.CarvedObsidian); // the arms
            Set(cx + ox, floorY + 4, cz + oz, _b.Chime);          // a bell under each arm
        }

        Set(cx, floorY + 6, cz, _b.Crystal);
        Plant(cx + 2, floorY, cz - 2, _b.Bloom);
        Plant(cx - 2, floorY, cz - 2, _b.Bloom);
        Plant(cx - 2, floorY, cz + 1, _b.Bloom);
        Plant(cx + 2, floorY, cz + 1, _b.Echo);
    }

    /// <summary>A 5×5 hut: a course of brick under two of reed planks, a two-high door toward the plaza, a stepped plank roof
    /// with a chime on top and a plank floor inside.</summary>
    private void BuildMirunHut(int hx, int floorY, int hz, int cx, int cz)
    {
        int towardX = cx - hx, towardZ = cz - hz;
        (int doorX, int doorZ) = Math.Abs(towardX) >= Math.Abs(towardZ) ? (Math.Sign(towardX) * 2, 0) : (0, Math.Sign(towardZ) * 2);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                int x = hx + dx, z = hz + dz;
                bool wall = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
                bool door = dx == doorX && dz == doorZ;
                if (wall)
                {
                    if (!door)
                    {
                        Set(x, floorY + 1, z, _b.Brick);
                        Set(x, floorY + 2, z, _b.LightPlanks);
                    }

                    Set(x, floorY + 3, z, _b.LightPlanks);
                }
                else
                {
                    Set(x, floorY, z, _b.LightPlanks);
                    Set(x, floorY + 5, z, _b.Planks);
                }

                Set(x, floorY + 4, z, _b.Planks);
            }
        }

        Set(hx, floorY + 6, hz, _b.Chime);
    }

    /// <summary>The reed fringe: about a third of the floor's outer ring grows chime reeds (never inside a hut wall).</summary>
    private void BuildMirunReeds(int cx, int floorY, int cz)
    {
        for (int dx = -MirunRadius; dx <= MirunRadius; dx++)
        {
            for (int dz = -MirunRadius; dz <= MirunRadius; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                int x = cx + dx, z = cz + dz;
                if (d2 < 13 * 13 || d2 > MirunRadius * MirunRadius || Hash("mirun-reed", x, z) >= 0.35
                    || !_sink.Get(x, floorY + 1, z).IsAir)
                {
                    continue;
                }

                Plant(x, floorY, z, _b.Reed);
            }
        }
    }
}
