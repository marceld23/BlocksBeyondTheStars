// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3566: a body's terrain layers as images — the developer's whole-planet view for tuning the geography package
/// (valleys, the distance layers, the biome layout) at a glance. One pixel per <c>step</c> blocks over the whole torus,
/// X east, Z north at the top. Not shipped to players; the Tools CLI writes the files (<c>layers</c>).
/// </summary>
public static class TerrainLayerExport
{
    /// <summary>The layer names, in the order <see cref="Render"/> returns them.</summary>
    public static readonly string[] Layers = { "height", "water", "valley", "fresh_water", "coast", "biome" };

    private static readonly (byte R, byte G, byte B)[] BiomeColours =
    {
        (214, 196, 120), (96, 160, 72), (120, 96, 72), (150, 150, 158), (230, 230, 240), (70, 120, 150), (170, 90, 160), (200, 120, 60),
    };

    /// <summary>Renders every layer of the generator's current world mode as PNG bytes, keyed by layer name.</summary>
    public static Dictionary<string, byte[]> Render(WorldGenerator generator, PlanetType planet, int circumference, int step)
    {
        step = System.Math.Max(1, step);
        int period = WorldConstants.LatitudePeriodFor(circumference);
        int w = System.Math.Max(1, circumference / step), h = System.Math.Max(1, period / step);
        int sea = generator.SeaLevel(planet);
        var natural = new int[w * h];
        var final = new int[w * h];
        var water = new bool[w * h];
        var fresh = new int[w * h];
        var coast = new int[w * h];
        var biome = new int[w * h];
        int lo = int.MaxValue, hi = int.MinValue;
        for (int pz = 0; pz < h; pz++)
        {
            int z = (period / 2) - 1 - (pz * step); // north at the top
            for (int px = 0; px < w; px++)
            {
                int x = px * step, i = (pz * w) + px;
                natural[i] = generator.NaturalSurfaceHeight(planet, x, z);
                final[i] = generator.SurfaceHeight(planet, x, z);
                water[i] = (sea != int.MinValue && final[i] < sea) || generator.IsSurfaceWater(planet, x, z);
                fresh[i] = generator.FreshWaterDistance(planet, x, z);
                coast[i] = generator.CoastDistance(planet, x, z);
                biome[i] = generator.BiomeIndexAt(planet, x, z);
                lo = System.Math.Min(lo, final[i]);
                hi = System.Math.Max(hi, final[i]);
            }
        }

        double span = System.Math.Max(1, hi - lo);
        var images = new Dictionary<string, byte[]>(System.StringComparer.Ordinal);
        images["height"] = Encode(w, h, i =>
        {
            int east = (i % w) + 1 < w ? final[i + 1] : final[i];
            int shade = System.Math.Clamp(128 + ((final[i] - east) * 12), 40, 255);
            int g = (int)(60 + (180 * (final[i] - lo) / span));
            return ((byte)(g * shade / 255), (byte)(g * shade / 255), (byte)(g * shade / 255));
        });
        images["water"] = Encode(w, h, i => water[i] ? ((byte)40, (byte)90, (byte)200) : ((byte)170, (byte)160, (byte)140));
        images["valley"] = Encode(w, h, i =>
        {
            int d = natural[i] - final[i];
            return d == 0 ? ((byte)30, (byte)30, (byte)30) : d > 0 ? ((byte)System.Math.Min(255, 60 + (d * 12)), (byte)40, (byte)40)
                : ((byte)40, (byte)System.Math.Min(255, 60 - (d * 12)), (byte)40);
        });
        images["fresh_water"] = Encode(w, h, i => Distance(fresh[i]));
        images["coast"] = Encode(w, h, i => Distance(coast[i]));
        images["biome"] = Encode(w, h, i => BiomeColours[((biome[i] % BiomeColours.Length) + BiomeColours.Length) % BiomeColours.Length]);
        return images;
    }

    /// <summary>A distance in blocks as a colour: blue at the water, through green to dark red at 400 blocks; black = none.</summary>
    private static (byte R, byte G, byte B) Distance(int blocks)
    {
        if (blocks == RiverGeography.None)
        {
            return (0, 0, 0);
        }

        double t = System.Math.Min(1.0, blocks / 400.0);
        return ((byte)(40 + (180 * t)), (byte)(200 - (150 * t)), (byte)(220 * (1.0 - t)));
    }

    private static byte[] Encode(int w, int h, System.Func<int, (byte R, byte G, byte B)> pixel)
    {
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            var (r, g, b) = pixel(i);
            rgb[i * 3] = r;
            rgb[(i * 3) + 1] = g;
            rgb[(i * 3) + 2] = b;
        }

        return Png.EncodeRgb(w, h, rgb);
    }
}

/// <summary>A minimal PNG writer (8-bit RGB, one IDAT, no filtering) — enough for the layer export, no image library needed.</summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes <paramref name="rgb"/> (row-major, top row first, three bytes per pixel) as a PNG file.</summary>
    public static byte[] EncodeRgb(int width, int height, byte[] rgb)
    {
        using var file = new MemoryStream();
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: truecolour
        WriteChunk(file, "IHDR", ihdr);

        var raw = new byte[height * ((width * 3) + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * ((width * 3) + 1);
            raw[row] = 0; // filter: none
            System.Array.Copy(rgb, y * width * 3, raw, row + 1, width * 3);
        }

        WriteChunk(file, "IDAT", Zlib(raw));
        WriteChunk(file, "IEND", System.Array.Empty<byte>());
        return file.ToArray();
    }

    /// <summary>The zlib stream PNG needs: a two-byte header, the deflate data, the Adler-32 of the raw bytes.</summary>
    private static byte[] Zlib(byte[] raw)
    {
        using var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        uint a = 1, b = 0;
        foreach (byte v in raw)
        {
            a = (a + v) % 65521;
            b = (b + a) % 65521;
        }

        var adler = new byte[4];
        WriteBigEndian(adler, 0, (b << 16) | a);
        output.Write(adler, 0, 4);
        return output.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var header = new byte[8];
        WriteBigEndian(header, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++)
        {
            header[4 + i] = (byte)type[i];
        }

        s.Write(header, 0, 8);
        s.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFFu;
        for (int i = 4; i < 8; i++)
        {
            crc = CrcTable[(crc ^ header[i]) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte v in data)
        {
            crc = CrcTable[(crc ^ v) & 0xFF] ^ (crc >> 8);
        }

        var tail = new byte[4];
        WriteBigEndian(tail, 0, crc ^ 0xFFFFFFFFu);
        s.Write(tail, 0, 4);
    }

    private static void WriteBigEndian(byte[] into, int at, uint value)
    {
        into[at] = (byte)(value >> 24);
        into[at + 1] = (byte)(value >> 16);
        into[at + 2] = (byte)(value >> 8);
        into[at + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
