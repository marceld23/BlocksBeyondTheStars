// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: the <b>Choir Tower</b> — the finale stage of the Sounders pack. A paved base in a <b>barren ring</b> (the chime moss
/// around it turned to grey scree, its plants gone: where the old hunger shows, and where the Soundraks like to stand), a
/// round tower of carved obsidian <see cref="SounderSiteLayout.ChoirTowerHeight"/> blocks high with two bands of resonance
/// crystal, a doorway in the south and a spiral of steps around a central column inside, and on top a wide platform with a
/// low rim and the <b>eight pipes</b> in a ring: pipe <c>i</c> is tone <c>i</c>, clockwise from north, the low tones the tall
/// pipes, each crowned with a resonance crystal. The pipes are the melody site <see cref="ChoirTowerSiteKey"/> (the sound
/// pack's <see cref="RegisterMelodySite(string, System.Collections.Generic.IReadOnlyList{Vector3i})"/>, re-registered every
/// load) and a struck crystal feeds the running puzzle or sings its tone (<see cref="UseResonanceCrystal"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The radius of the paved base around the tower.</summary>
    private const int ChoirBaseRadius = 9;

    /// <summary>The barren ring reaches this far from the tower's axis.</summary>
    private const int ChoirBarrenRadius = 24;

    /// <summary>The top platform's radius (the rim stands on its outermost ring).</summary>
    private const int ChoirPlatformRadius = 7;

    /// <summary>The eight neighbours of the central column, clockwise from north — the spiral's step cells.</summary>
    private static readonly (int X, int Z)[] ChoirSpiral = { (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1) };

    private void WriteChoirTower(in SounderBlocks b, int cx, int floorY, int cz)
    {
        WriteBarrenRing(b, cx, cz);
        LevelSounderDisc(cx, cz, floorY, ChoirBaseRadius, b.CarvedObsidian, b.Stone);
        WriteChoirTowerBody(b, cx, floorY, cz);
        int platformY = ChoirPlatformY(floorY);
        WriteChoirPlatform(b, cx, platformY, cz);
        for (int i = 0; i < MelodySiteGeometry.Pillars; i++)
        {
            var pipe = SounderSiteLayout.ChoirPipeBase(cx, platformY, cz, i);
            int stem = SounderSiteLayout.ChoirPipeStem(i);
            SounderColumn(pipe.X, pipe.Z, pipe.Y, pipe.Y + stem - 1, b.CarvedObsidian);
            SounderSet(pipe.X, pipe.Y + stem, pipe.Z, b.Crystal);
        }
    }

    /// <summary>The floor of the top platform for a tower standing on <paramref name="floorY"/>.</summary>
    internal static int ChoirPlatformY(int floorY) => floorY + SounderSiteLayout.ChoirTowerHeight + 1;

    /// <summary>The barren ring: outside the base, the natural chime moss turns to scree and its plants are gone.</summary>
    private void WriteBarrenRing(in SounderBlocks b, int cx, int cz)
    {
        var planet = _world.Planet;
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
                int ground = _generator.SurfaceHeight(planet, x, z);
                if (_world.GetBlock(SounderCell(x, ground, z)) == b.Moss)
                {
                    SounderSet(x, ground, z, b.Scree);
                }

                var above = SounderCell(x, ground + 1, z);
                if (FloraCatalog.IsPlantKey(_content.BlockById(_world.GetBlock(above))?.Key))
                {
                    _world.SetBlock(above, BlockId.Air);
                }
            }
        }
    }

    /// <summary>The round tower: walls 3.2–4.5 from the axis, two crystal bands at the cardinal points, a two-high doorway in
    /// the south, and inside the central column with one step per level spiralling clockwise around it.</summary>
    private void WriteChoirTowerBody(in SounderBlocks b, int cx, int floorY, int cz)
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
                        SounderSet(cx + dx, floorY + y, cz + dz, band ? b.Crystal : b.CarvedObsidian);
                    }
                }
            }

            SounderSet(cx, floorY + y, cz, b.CarvedObsidian); // the central column
            var (sx, sz) = ChoirSpiral[(y - 1) % ChoirSpiral.Length];
            SounderSet(cx + sx, floorY + y, cz + sz, b.CarvedObsidian); // this level's step
        }
    }

    /// <summary>The top platform: a disc of carved obsidian with the stairwell open around the column and a one-high rim.</summary>
    private void WriteChoirPlatform(in SounderBlocks b, int cx, int platformY, int cz)
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

                SounderSet(cx + dx, platformY, cz + dz, b.CarvedObsidian);
                if (d2 > rimFrom + 6)
                {
                    SounderSet(cx + dx, platformY + 1, cz + dz, b.CarvedObsidian); // the rim keeps everyone up here
                }
            }
        }
    }

    /// <summary>The pipes' base cells (the melody pillars), clockwise from north, for a tower on <paramref name="floorY"/>.</summary>
    internal static Vector3i[] ChoirPipes(int cx, int floorY, int cz)
    {
        int platformY = ChoirPlatformY(floorY);
        var pipes = new Vector3i[MelodySiteGeometry.Pillars];
        for (int i = 0; i < pipes.Length; i++)
        {
            pipes[i] = SounderSiteLayout.ChoirPipeBase(cx, platformY, cz, i);
        }

        return pipes;
    }

    /// <summary>Every load: the eight pipes as the melody site <see cref="ChoirTowerSiteKey"/>, and the pack site on the top
    /// platform (where the finale plays).</summary>
    private void RegisterChoirTowerSite(int cx, int floorY, int cz)
    {
        RegisterMelodySite(ChoirTowerSiteKey, ChoirPipes(cx, floorY, cz));
        RegisterPackSite(SoundersIds.PackId, ChoirTowerSiteKey, _world.LocationId, new Vector3i(cx, ChoirPlatformY(floorY) + 1, cz));
    }

    /// <summary>
    /// The use key on a resonance crystal (#3358): a Choir Tower pipe (or any crystal that is a melody pillar) strikes its
    /// tone into the running puzzle or starts one; with no puzzle it sings its pillar's tone, and any other crystal sings its
    /// own tone (a hash of its cell) — so every crystal on Soaniri is an instrument. The singing plants hear it too.
    /// </summary>
    private void UseResonanceCrystal(PlayerSession session, Vector3i cell)
    {
        if (TryStrikeMelodyPillar(session, cell))
        {
            return;
        }

        MelodySiteAt(cell, out int pillar);
        int tone = pillar >= 0 ? pillar : CrystalTone(WorldConstants.CanonicalBlock(cell, _world.Circumference));
        PlayNoteSequence(new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), new[] { tone }, SoundersIds.InstrumentVoice,
            0.6f, withFifth: false, source: "resonance_crystal");
        HearSingingPlants(session, tone, SoundersIds.InstrumentVoice);
    }

    /// <summary>The tone a lone resonance crystal sings (0–7): a stable hash of its canonical cell.</summary>
    internal static int CrystalTone(Vector3i cell)
    {
        unchecked
        {
            uint h = ((uint)cell.X * 73856093u) ^ ((uint)cell.Y * 19349663u) ^ ((uint)cell.Z * 83492791u);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (int)(h % (uint)SounderAlphabet.ToneCount);
        }
    }
}
