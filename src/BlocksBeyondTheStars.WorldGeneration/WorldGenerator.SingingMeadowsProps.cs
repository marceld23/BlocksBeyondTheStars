// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3358: the anchor props of Soaniri's singing meadows (see <c>WorldGenerator.SingingMeadows.cs</c>) — the obsidian outcrops
/// with their resonance-crystal caps and the harp arches with their harp vines. Written like the set dressing: every chunk
/// scans the columns within <see cref="SingingPropMargin"/> of itself for anchors, each anchor builds its prop from the world
/// seed alone, and only the cells inside this chunk are written (air cells only, never carving terrain).
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>One prop write, clipped to the chunk: a plant only into air, stone (obsidian, a crystal) into air or over a
    /// singing plant — so where two props meet, a vine never ends up hanging from another vine, whatever the anchor order.</summary>
    private readonly struct SingingPropWriter
    {
        private readonly ChunkData _chunk;
        private readonly Vector3i _origin;
        private readonly SingingIds _ids;

        public SingingPropWriter(ChunkData chunk, Vector3i origin, in SingingIds ids)
        {
            _chunk = chunk;
            _origin = origin;
            _ids = ids;
        }

        public void Set(int wx, int wy, int wz, BlockId block)
        {
            int n = WorldConstants.ChunkSize;
            int lx = wx - _origin.X, ly = wy - _origin.Y, lz = wz - _origin.Z;
            if (block.IsAir || lx < 0 || lx >= n || ly < 0 || ly >= n || lz < 0 || lz >= n)
            {
                return;
            }

            var here = _chunk.Get(lx, ly, lz);
            if (here.IsAir || (!_ids.IsPlant(block) && _ids.IsPlant(here)))
            {
                _chunk.Set(lx, ly, lz, block);
            }
        }
    }

    /// <summary>Scans the anchors around this chunk and builds the outcrops and arches that reach into it.</summary>
    private void StampSingingProps(ChunkData chunk, FillContext k, in SingingIds ids)
    {
        var origin = k.Origin;
        int cs = WorldConstants.ChunkSize;
        var writer = new SingingPropWriter(chunk, origin, ids);
        for (int wx = origin.X - SingingPropMargin; wx < origin.X + cs + SingingPropMargin; wx++)
            for (int wz = origin.Z - SingingPropMargin; wz < origin.Z + cs + SingingPropMargin; wz++)
            {
                int cx = WorldConstants.WrapX(wx, _circumference), cz = Wz(wz);
                double outcropRoll = Noise.Value01(k.Seed + OutcropSalt, cx, 7, cz);
                double archRoll = Noise.Value01(k.Seed + HarpArchSalt, cx, 9, cz);
                if (outcropRoll >= SlopeOutcropChance && archRoll >= SlopeArchChance)
                {
                    continue; // the common case: no anchor here (the meadow chances are lower still)
                }

                int sy = SurfaceHeight(k.Planet, wx, wz);
                // The prop's columns stand on their own ground (within a few cells of the anchor's on the flats it seeks).
                if (sy - SingingPropMargin > origin.Y + cs - 1 || sy + SingingPropRise + SingingPropMargin < origin.Y
                    || !SingingPropGround(k, sy, wx, wz))
                {
                    continue; // nothing of it lands in this chunk, or the anchor stands in water
                }

                var zone = SingingZoneOf(k, ids, wx, wz, sy);
                int shape = (int)(Noise.Value01(k.Seed + SingingShapeSalt, cx, 11, cz) * 997);
                if (zone == SingingZone.Slope ? outcropRoll < SlopeOutcropChance : zone == SingingZone.Meadow && outcropRoll < MeadowOutcropChance)
                {
                    StampObsidianOutcrop(k.Planet, writer, ids, wx, wz, shape);
                }
                else if (zone == SingingZone.Slope ? archRoll < SlopeArchChance : zone == SingingZone.Meadow && archRoll < MeadowArchChance)
                {
                    StampHarpArch(k.Planet, writer, ids, wx, sy, wz, shape);
                }
            }
    }

    /// <summary>Dry ground under an anchor: above the sea and no pond, river or lake in its column.</summary>
    private bool SingingPropGround(FillContext k, int sy, int wx, int wz)
        => sy + 1 > k.FluidLevel && SurfacePondDepth(k.Planet, wx, wz) <= 0 && SurfaceRiverDepth(k.Planet, wx, wz) <= 0
           && SurfaceGen1WaterDepth(k.Planet, wx, wz) <= 0;

    /// <summary>An obsidian outcrop: a rough mound of radius 1–2, 3–6 tall at its heart, every column standing on its own
    /// ground; two in three carry a resonance crystal on top.</summary>
    private void StampObsidianOutcrop(PlanetType planet, SingingPropWriter writer, in SingingIds ids, int wx, int wz, int shape)
    {
        int radius = 1 + (shape % 2);
        int height = 3 + ((shape / 2) % 4);
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if ((dx * dx) + (dz * dz) > (radius * radius) + 1)
                {
                    continue;
                }

                int columnHeight = height - System.Math.Abs(dx) - System.Math.Abs(dz) - ((shape >> (dx + dz + 4)) & 1);
                if (columnHeight < 1)
                {
                    continue;
                }

                int ground = SurfaceHeight(planet, wx + dx, wz + dz);
                for (int dy = 1; dy <= columnHeight; dy++)
                {
                    writer.Set(wx + dx, ground + dy, wz + dz, ids.Obsidian);
                }

                if (dx == 0 && dz == 0 && (shape / 8) % 3 != 0)
                {
                    writer.Set(wx, ground + columnHeight + 1, wz, ids.Crystal); // the singing cap
                }
            }
    }

    /// <summary>A harp arch: two obsidian pillars two cells either side of the anchor (along X or Z), a lintel across at 4–5
    /// above the anchor's ground, harp vines hanging under the lintel between the pillars, and now and then a resonance crystal
    /// on the lintel's middle. A steep anchor (the pillars' grounds more than two apart) builds nothing.</summary>
    private void StampHarpArch(PlanetType planet, SingingPropWriter writer, in SingingIds ids, int wx, int sy, int wz, int shape)
    {
        bool alongX = (shape & 1) == 0;
        int ax = alongX ? 1 : 0, az = alongX ? 0 : 1;
        int groundA = SurfaceHeight(planet, wx - (2 * ax), wz - (2 * az));
        int groundB = SurfaceHeight(planet, wx + (2 * ax), wz + (2 * az));
        if (System.Math.Abs(groundA - groundB) > 2 || System.Math.Abs(groundA - sy) > 2 || System.Math.Abs(groundB - sy) > 2)
        {
            return;
        }

        int top = sy + 4 + ((shape / 2) % 2);
        for (int i = -2; i <= 2; i++)
        {
            int x = wx + (i * ax), z = wz + (i * az);
            if (i is -2 or 2)
            {
                int ground = i < 0 ? groundA : groundB;
                for (int y = ground + 1; y < top; y++)
                {
                    writer.Set(x, y, z, ids.Obsidian); // a pillar on its own ground
                }
            }

            writer.Set(x, top, z, ids.Obsidian); // the lintel
            if (i is > -2 and < 2 && !ids.Vine.IsAir)
            {
                writer.Set(x, top - 1, z, ids.Vine); // a harp string, hanging from the lintel
            }
        }

        if ((shape / 4) % 2 == 0)
        {
            writer.Set(wx, top + 1, wz, ids.Crystal);
        }
    }
}
