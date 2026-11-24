// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Buffers.Binary;
using System.IO.Compression;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The <c>.bbmap</c> file format of a <see cref="PlanetMap"/> (#3978), written by <c>tools/planet_maps</c> and read by the
/// game. Little-endian throughout:
/// <code>
/// offset  size  field
///      0     4  magic "BBMP"
///      4     2  format version (<see cref="FormatVersion"/>)
///      6     2  layers: bit 0 elevation, bit 1 biome (both required in version 1)
///      8     4  width  (cells per row, the whole circumference)
///     12     4  height (rows, the whole latitude period; row 0 = the south edge)
///     16     8  content hash: FNV-1a 64 over the uncompressed payload
///     24     …  payload, raw deflate (RFC 1951, no zlib header):
///               width·height int16 elevations (metres), row-major from the south edge,
///               then width·height bytes of biome class (0..7, <see cref="PlanetMapBiome"/>)
/// </code>
/// A map is frozen once a release ships it: a changed map is a new key (or a new version for new worlds only), because
/// the ground of every world made from it would move.
/// </summary>
public static class PlanetMapCodec
{
    /// <summary>The format version this code writes and reads.</summary>
    public const ushort FormatVersion = 1;

    /// <summary>The folder under <c>data/</c> the maps live in, and their file extension.</summary>
    public const string DataFolder = "maps";

    /// <inheritdoc cref="DataFolder"/>
    public const string Extension = ".bbmap";

    /// <summary>The largest side a map may have (cells) — bounds what an untrusted file can make the game allocate.</summary>
    public const int MaxSide = 8192;

    private const int HeaderSize = 24;
    private const ushort LayerElevation = 1;
    private const ushort LayerBiome = 2;
    private static readonly byte[] Magic = { (byte)'B', (byte)'B', (byte)'M', (byte)'P' };

    /// <summary>The bytes of a <c>.bbmap</c> file holding <paramref name="map"/>.</summary>
    public static byte[] Encode(PlanetMap map)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        byte[] payload = Payload(map);
        var header = new byte[HeaderSize];
        Array.Copy(Magic, header, Magic.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), LayerElevation | LayerBiome);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), map.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), map.Height);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(16), Fnv1a(payload));

        using var output = new MemoryStream();
        output.Write(header, 0, header.Length);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(payload, 0, payload.Length);
        }

        return output.ToArray();
    }

    /// <summary>Reads a <c>.bbmap</c> file. Throws <see cref="InvalidDataException"/> on anything that is not a complete,
    /// intact map of this format version: a wrong magic, an unknown version or layer set, an implausible size, a short
    /// or long payload, a hash that does not match, or a biome class out of range.</summary>
    public static PlanetMap Decode(byte[] data)
    {
        if (data is null || data.Length < HeaderSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a planet map (.bbmap): the magic is missing.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        ushort layers = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16));
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Planet map format version {version} is not supported (this game reads {FormatVersion}).");
        }

        if (layers != (LayerElevation | LayerBiome))
        {
            throw new InvalidDataException($"A version-{FormatVersion} planet map carries the elevation and biome layers (flags 3), got {layers}.");
        }

        if (width < 2 || height < 2 || width > MaxSide || height > MaxSide)
        {
            throw new InvalidDataException($"Implausible planet map size {width} × {height}.");
        }

        int cells = width * height;
        byte[] payload = Inflate(data, cells * 3);
        if (Fnv1a(payload) != hash)
        {
            throw new InvalidDataException("The planet map's content hash does not match its cells (a damaged file).");
        }

        var elevation = new short[cells];
        var biome = new byte[cells];
        for (int i = 0; i < cells; i++)
        {
            elevation[i] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(i * 2));
        }

        Array.Copy(payload, cells * 2, biome, 0, cells);
        foreach (byte b in biome)
        {
            if (b >= PlanetMapBiome.Count)
            {
                throw new InvalidDataException($"A planet map biome class must be below {PlanetMapBiome.Count}, found {b}.");
            }
        }

        return new PlanetMap(width, height, elevation, biome, hash);
    }

    /// <summary>The content hash of a map's cells — the FNV-1a 64 of the payload <see cref="Encode"/> would write.</summary>
    public static ulong HashCells(short[] elevation, byte[] biome)
    {
        ulong h = FnvOffset;
        foreach (short e in elevation)
        {
            h = (h ^ (byte)(e & 0xFF)) * FnvPrime;
            h = (h ^ (byte)((e >> 8) & 0xFF)) * FnvPrime;
        }

        foreach (byte b in biome)
        {
            h = (h ^ b) * FnvPrime;
        }

        return h;
    }

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static ulong Fnv1a(byte[] bytes)
    {
        ulong h = FnvOffset;
        foreach (byte b in bytes)
        {
            h = (h ^ b) * FnvPrime;
        }

        return h;
    }

    private static byte[] Payload(PlanetMap map)
    {
        int cells = map.Width * map.Height;
        var payload = new byte[cells * 3];
        for (int row = 0, i = 0; row < map.Height; row++)
        {
            for (int col = 0; col < map.Width; col++, i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 2), map.ElevationAt(col, row));
                payload[cells * 2 + i] = map.BiomeAt(col, row);
            }
        }

        return payload;
    }

    /// <summary>Inflates the payload behind the header to exactly <paramref name="expected"/> bytes — one byte more is
    /// read to tell a long payload, so a small "bomb" never makes the game allocate more than the map.</summary>
    private static byte[] Inflate(byte[] data, int expected)
    {
        var payload = new byte[expected];
        try
        {
            using var inflate = new DeflateStream(new MemoryStream(data, HeaderSize, data.Length - HeaderSize), CompressionMode.Decompress);
            int read = 0;
            while (read < expected)
            {
                int n = inflate.Read(payload, read, expected - read);
                if (n <= 0)
                {
                    throw new InvalidDataException($"The planet map's payload ends after {read} of {expected} bytes.");
                }

                read += n;
            }

            if (inflate.ReadByte() >= 0)
            {
                throw new InvalidDataException("The planet map's payload is longer than its size says.");
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("The planet map's payload is not valid deflate data.", ex);
        }

        return payload;
    }
}
