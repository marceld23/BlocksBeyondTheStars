// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3358: <b>Ornasa</b>, the ruin city of the old Sounders — a levelled moss ground with broken carved-obsidian paving, a
/// broken outer wall with gaps, an amphitheatre in the middle (a paved disc, eight broken rune pillars, a crystal on a plinth),
/// and six slots on a ring around it (<see cref="SounderSiteLayout"/>): three ruined halls (brick under obsidian, every wall
/// column broken to its own height, a crystal growing inside) and three sealed <b>sound chambers</b> — solid cubes of carved
/// obsidian with a two-high sealed doorway toward the amphitheatre and a crystal above it — then the bell-bloom trail. The
/// relic caches (one in every chamber, one in halls 0 and 4) end the emission as <c>relic_cache</c> spots.
/// </summary>
public sealed partial class SoaniriBuilder
{
    /// <summary>Ornasa's footprint radius (what it levels and keeps clear).</summary>
    public const int OrnasaRadius = 22;

    private void BuildOrnasa(int cx, int floorY, int cz)
    {
        LevelDisc(cx, cz, floorY, OrnasaRadius, _b.Moss, _b.Dirt);
        BuildOrnasaGround(cx, floorY, cz);
        BuildOrnasaAmphitheatre(cx, floorY, cz);
        for (int slot = 0; slot < SounderSiteLayout.OrnasaSlots; slot++)
        {
            var (sx, sz) = SounderSiteLayout.OrnasaSlotCentre(cx, cz, slot);
            if (slot % 2 == 0)
            {
                BuildOrnasaHall(sx, floorY, sz, cx, cz);
            }
            else
            {
                BuildOrnasaChamber(sx, floorY, sz, cx, cz);
            }
        }

        for (int k = 0; k < SounderSiteLayout.BloomTrailLength; k++)
        {
            var cell = SounderSiteLayout.BloomTrailCell(cx, floorY, cz, k);
            Clear(cell.X, cell.Y, cell.Z);
            Plant(cell.X, floorY, cell.Z, _b.Bloom);
        }

        for (int i = 0; i < SounderSiteLayout.OrnasaChambers; i++)
        {
            var inside = SounderSiteLayout.ChamberInside(cx, floorY, cz, i);
            _sink.Mark("relic_cache", inside.X, inside.Y, inside.Z);
        }

        for (int slot = 0; slot < SounderSiteLayout.OrnasaSlots; slot += 4)
        {
            var (hx, hz) = SounderSiteLayout.OrnasaSlotCentre(cx, cz, slot);
            _sink.Mark("relic_cache", hx + 1, floorY + 1, hz + 1);
        }
    }

    /// <summary>The broken paving and the broken outer wall (gaps where a wall section fell).</summary>
    private void BuildOrnasaGround(int cx, int floorY, int cz)
    {
        for (int dx = -OrnasaRadius; dx <= OrnasaRadius; dx++)
        {
            for (int dz = -OrnasaRadius; dz <= OrnasaRadius; dz++)
            {
                double d = Math.Sqrt((dx * dx) + (dz * dz));
                int x = cx + dx, z = cz + dz;
                if (d > OrnasaRadius)
                {
                    continue;
                }

                if (d >= OrnasaRadius - 1.5)
                {
                    double sector = Hash("ornasa-wall", (int)Math.Floor(Math.Atan2(dx, dz) * 6.0), 0);
                    int height = sector < 0.22 ? 0 : 1 + (int)(Hash("ornasa-wall-h", x, z) * 4.0);
                    Column(x, z, floorY + 1, floorY + height, _b.CarvedObsidian);
                    continue;
                }

                if (Hash("ornasa-paving", x, z) < 0.35)
                {
                    Set(x, floorY, z, _b.CarvedObsidian);
                }
            }
        }
    }

    /// <summary>The amphitheatre: a paved disc, eight broken rune pillars (one to three stones) and a crystal on a plinth.</summary>
    private void BuildOrnasaAmphitheatre(int cx, int floorY, int cz)
    {
        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                if ((dx * dx) + (dz * dz) <= 40)
                {
                    Set(cx + dx, floorY, cz + dz, _b.CarvedObsidian);
                }
            }
        }

        for (int i = 0; i < 8; i++)
        {
            var (px, pz) = SounderSiteLayout.Polar(cx, cz, 5.0, (i * Math.PI / 4.0) + (Math.PI / 8.0));
            int height = 1 + (int)(Hash("ornasa-rune", px, pz) * 3.0);
            Column(px, pz, floorY + 1, floorY + height, _b.Rune);
        }

        Set(cx, floorY + 1, cz, _b.CarvedObsidian);
        Set(cx, floorY + 2, cz, _b.Crystal);
    }

    /// <summary>A ruined hall (7×7): every wall column broken to its own height (brick at the foot, obsidian above, now and
    /// then a rune stone on top), a doorway toward the amphitheatre and a crystal growing in the middle.</summary>
    private void BuildOrnasaHall(int sx, int floorY, int sz, int cx, int cz)
    {
        var (tx, tz) = SounderSiteLayout.TowardCentre(sx, sz, cx, cz);
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                int x = sx + dx, z = sz + dz;
                if (Math.Abs(dx) != 3 && Math.Abs(dz) != 3)
                {
                    Set(x, floorY, z, _b.Brick); // the old floor
                    continue;
                }

                bool doorway = (dx == tx * 3 && dz == 0 && tz == 0) || (dz == tz * 3 && dx == 0 && tx == 0);
                int height = doorway ? 0 : 1 + (int)(Hash("ornasa-hall", x, z) * 5.0);
                for (int y = 1; y <= height; y++)
                {
                    bool top = y == height && height >= 3 && Hash("ornasa-hall-rune", x, z) < 0.2;
                    Set(x, floorY + y, z, top ? _b.Rune : y == 1 ? _b.Brick : _b.CarvedObsidian);
                }
            }
        }

        Set(sx, floorY + 1, sz, _b.Crystal);
    }

    /// <summary>A sealed sound chamber: a 5×5×4 cube of carved obsidian, hollow inside (3×3×3), its doorway toward the
    /// amphitheatre sealed with obsidian and a crystal set into the wall above it.</summary>
    private void BuildOrnasaChamber(int sx, int floorY, int sz, int cx, int cz)
    {
        var (tx, tz) = SounderSiteLayout.TowardCentre(sx, sz, cx, cz);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                bool inside = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1;
                for (int y = 1; y <= 4; y++)
                {
                    if (inside && y <= 3)
                    {
                        Clear(sx + dx, floorY + y, sz + dz);
                    }
                    else
                    {
                        Set(sx + dx, floorY + y, sz + dz, _b.CarvedObsidian); // the walls, the roof — and the seal
                    }
                }
            }
        }

        Set(sx + (tx * 2), floorY + 3, sz + (tz * 2), _b.Crystal);
    }
}
