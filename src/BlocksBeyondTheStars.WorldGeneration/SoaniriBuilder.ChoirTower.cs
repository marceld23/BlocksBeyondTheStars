// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3358: the <b>Choir Tower</b> — the finale stage of the Sounders pack. A paved base in a <b>barren ring</b> (the chime moss
/// around it turned to grey scree, its plants gone), a round tower of carved obsidian
/// <see cref="SounderSiteLayout.ChoirTowerHeight"/> blocks high with two bands of resonance crystal, a doorway in the south and
/// a spiral of steps around a central column inside, and on top a wide platform with a low rim and the <b>eight pipes</b> in
/// a ring: pipe <c>i</c> is tone <c>i</c>, clockwise from north, the low tones the tall pipes, each crowned with a resonance
/// crystal.
/// </summary>
public sealed partial class SoaniriBuilder
{
    /// <summary>The radius of the paved base around the tower.</summary>
    public const int ChoirBaseRadius = 9;

    /// <summary>The barren ring reaches this far from the tower's axis (the Choir Tower's footprint radius).</summary>
    public const int ChoirBarrenRadius = 24;

    /// <summary>The top platform's radius (the rim stands on its outermost ring).</summary>
    public const int ChoirPlatformRadius = 7;

    /// <summary>The eight neighbours of the central column, clockwise from north — the spiral's step cells.</summary>
    private static readonly (int X, int Z)[] ChoirSpiral = { (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1) };

    /// <summary>The floor of the top platform for a tower standing on <paramref name="floorY"/>.</summary>
    public static int ChoirPlatformY(int floorY) => floorY + SounderSiteLayout.ChoirTowerHeight + 1;

    /// <summary>The pipes' base cells (the melody pillars), clockwise from north, for a tower on <paramref name="floorY"/>.</summary>
    public static Vector3i[] ChoirPipes(int cx, int floorY, int cz)
    {
        int platformY = ChoirPlatformY(floorY);
        var pipes = new Vector3i[MelodySiteGeometry.Pillars];
        for (int i = 0; i < pipes.Length; i++)
        {
            pipes[i] = SounderSiteLayout.ChoirPipeBase(cx, platformY, cz, i);
        }

        return pipes;
    }

    private void BuildChoirTower(int cx, int floorY, int cz)
    {
        BuildBarrenRing(cx, cz);
        LevelDisc(cx, cz, floorY, ChoirBaseRadius, _b.CarvedObsidian, _b.Stone);
        BuildChoirTowerBody(cx, floorY, cz);
        int platformY = ChoirPlatformY(floorY);
        BuildChoirPlatform(cx, platformY, cz);
        for (int i = 0; i < MelodySiteGeometry.Pillars; i++)
        {
            var pipe = SounderSiteLayout.ChoirPipeBase(cx, platformY, cz, i);
            int stem = SounderSiteLayout.ChoirPipeStem(i);
            Column(pipe.X, pipe.Z, pipe.Y, pipe.Y + stem - 1, _b.CarvedObsidian);
            Set(pipe.X, pipe.Y + stem, pipe.Z, _b.Crystal);
        }
    }

    /// <summary>The barren ring: outside the base, the natural chime moss turns to scree and its plants are gone.</summary>
    private void BuildBarrenRing(int cx, int cz)
    {
        int r2Max = ChoirBarrenRadius * ChoirBarrenRadius;
        for (int dx = -ChoirBarrenRadius; dx <= ChoirBarrenRadius; dx++)
        {
            for (int dz = -ChoirBarrenRadius; dz <= ChoirBarrenRadius; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                if (d2 > r2Max || d2 <= ChoirBaseRadius * ChoirBaseRadius)
                {
                    continue;
                }

                int x = cx + dx, z = cz + dz;
                int ground = _ground(x, z);
                if (_sink.Get(x, ground, z) == _b.Moss)
                {
                    Set(x, ground, z, _b.Scree);
                }

                if (FloraCatalog.IsPlantKey(_content.BlockById(_sink.Get(x, ground + 1, z))?.Key))
                {
                    _sink.Put(x, ground + 1, z, BlockId.Air);
                }
            }
        }
    }

    /// <summary>The round tower: walls 3.2–4.5 from the axis, two crystal bands at the cardinal points, a two-high doorway in
    /// the south, and inside the central column with one step per level spiralling clockwise around it.</summary>
    private void BuildChoirTowerBody(int cx, int floorY, int cz)
    {
        int height = SounderSiteLayout.ChoirTowerHeight;
        for (int y = 1; y <= height; y++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                for (int dz = -4; dz <= 4; dz++)
                {
                    int d2 = (dx * dx) + (dz * dz);
                    if (d2 < 10 || d2 > 20)
                    {
                        continue;
                    }

                    bool doorway = dx == 0 && dz < 0 && y <= 2;
                    bool band = (y == 8 || y == 16) && ((dx == 0) != (dz == 0)) && d2 == 16;
                    if (!doorway)
                    {
                        Set(cx + dx, floorY + y, cz + dz, band ? _b.Crystal : _b.CarvedObsidian);
                    }
                }
            }

            Set(cx, floorY + y, cz, _b.CarvedObsidian); // the central column
            var (sx, sz) = ChoirSpiral[(y - 1) % ChoirSpiral.Length];
            Set(cx + sx, floorY + y, cz + sz, _b.CarvedObsidian); // this level's step
        }
    }

    /// <summary>The top platform: a disc of carved obsidian with the stairwell open around the column and a one-high rim.</summary>
    private void BuildChoirPlatform(int cx, int platformY, int cz)
    {
        int r2Max = (ChoirPlatformRadius * ChoirPlatformRadius) + 1;
        int rimFrom = (ChoirPlatformRadius - 1) * (ChoirPlatformRadius - 1);
        for (int dx = -ChoirPlatformRadius; dx <= ChoirPlatformRadius; dx++)
        {
            for (int dz = -ChoirPlatformRadius; dz <= ChoirPlatformRadius; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                if (d2 > r2Max || (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1))
                {
                    continue; // outside, or the stairwell
                }

                Set(cx + dx, platformY, cz + dz, _b.CarvedObsidian);
                if (d2 > rimFrom + 6)
                {
                    Set(cx + dx, platformY + 1, cz + dz, _b.CarvedObsidian); // the rim keeps everyone up here
                }
            }
        }
    }
}
