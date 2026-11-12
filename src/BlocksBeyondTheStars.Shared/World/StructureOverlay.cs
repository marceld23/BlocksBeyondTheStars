// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// A STRUCTURE OVERLAY (terrain generation 39, <see cref="WorldDescription.SchoolClubLandmarksGeneration"/>): the final cells
/// of one placed structure — a landmark after its ruin pass, with its plinth, its carved cavities, its seal stones, its chest
/// crates and their hidden stones — kept as ONE blob instead of a block edit per cell. The server applies the cells of a
/// chunk whenever that chunk is generated, before the players' block edits, so what players change stays an ordinary edit
/// on top. A giant landmark has half a million to four million cells (every column is cleared from its ground row to its
/// top, so most of them are air); as edit rows that was the bulk of a save.
/// <para>Cells are grouped by (canonical) chunk and, inside a chunk, into RUNS: consecutive local indices
/// (<see cref="WorldConstants.LocalIndex"/>, X fastest) that hold the same value — the block, its dye and glow (0xRRGGBB,
/// 0 = none) and its packed shape (0 = a plain cube). Air is a value like any other — it is what carves. A hollow hall is a
/// few runs per row of a chunk, so the decoded overlay stays a fraction of a byte per cell in memory.</para>
/// <para>The blob (<see cref="Encode"/>): one format byte (<see cref="FormatVersion"/>), then a raw deflate stream of
/// unsigned LEB128 varints — the chunk count; per chunk its zig-zagged X, Y, Z and its run count, each run's gap after the
/// previous run's end and its length minus one, the runs' block ids, one flag byte per run (1 tint, 2 glow, 4 shape) and
/// the flagged values. Pure managed code, netstandard2.1-clean: the browser's in-memory save stores the same bytes.</para>
/// </summary>
public sealed class StructureOverlay
{
    /// <summary>The blob format this build writes and reads.</summary>
    public const byte FormatVersion = 1;

    /// <summary>The most bytes a blob may inflate to — far above any landmark and a guard against a damaged row making the
    /// server allocate without bound.</summary>
    public const int MaxInflatedBytes = 64 * 1024 * 1024;

    private const byte TintFlag = 1;
    private const byte GlowFlag = 2;
    private const byte ShapeFlag = 4;

    private readonly StructureOverlayChunk[] _chunks;

    public StructureOverlay(IReadOnlyList<StructureOverlayChunk> chunks)
    {
        _chunks = chunks.ToArray();
        CellCount = _chunks.Sum(c => c.Count);
        RunCount = _chunks.Sum(c => c.RunCount);
    }

    /// <summary>The chunks the overlay touches, in ascending (X, Y, Z) order when built or decoded.</summary>
    public IReadOnlyList<StructureOverlayChunk> Chunks => _chunks;

    /// <summary>All cells of all chunks.</summary>
    public int CellCount { get; }

    /// <summary>All runs of all chunks.</summary>
    public int RunCount { get; }

    /// <summary>Every cell as its world position (canonical, like the chunk coordinates) with its values, chunk by chunk.</summary>
    public IEnumerable<(Vector3i Cell, ushort Block, int Tint, int Glow, int Shape)> Cells()
    {
        foreach (var chunk in _chunks)
        {
            var origin = WorldConstants.ChunkOrigin(chunk.Coord);
            for (int r = 0; r < chunk.RunCount; r++)
            {
                ushort block = chunk.RunBlock(r);
                var (tint, glow, shape) = chunk.RunExtras(r);
                int end = chunk.RunStart(r) + chunk.RunLength(r);
                for (int local = chunk.RunStart(r); local < end; local++)
                {
                    yield return (new Vector3i(origin.X + LocalX(local), origin.Y + LocalY(local), origin.Z + LocalZ(local)),
                        block, tint, glow, shape);
                }
            }
        }
    }

    /// <summary>The local X of a <see cref="WorldConstants.LocalIndex"/>.</summary>
    public static int LocalX(int local) => local % WorldConstants.ChunkSize;

    /// <summary>The local Y of a <see cref="WorldConstants.LocalIndex"/>.</summary>
    public static int LocalY(int local) => local / (WorldConstants.ChunkSize * WorldConstants.ChunkSize);

    /// <summary>The local Z of a <see cref="WorldConstants.LocalIndex"/>.</summary>
    public static int LocalZ(int local) => (local / WorldConstants.ChunkSize) % WorldConstants.ChunkSize;

    // ---------------------------------------------------------------- encoding

    /// <summary>The blob: the format byte, then the deflated run stream.</summary>
    public byte[] Encode()
    {
        using var raw = new MemoryStream();
        WriteVarint(raw, (uint)_chunks.Length);
        foreach (var chunk in _chunks)
        {
            WriteChunk(raw, chunk);
        }

        using var packed = new MemoryStream();
        packed.WriteByte(FormatVersion);
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return packed.ToArray();
    }

    private static void WriteChunk(MemoryStream raw, StructureOverlayChunk chunk)
    {
        WriteVarint(raw, ZigZag(chunk.Coord.X));
        WriteVarint(raw, ZigZag(chunk.Coord.Y));
        WriteVarint(raw, ZigZag(chunk.Coord.Z));
        int n = chunk.RunCount;
        WriteVarint(raw, (uint)n);
        int end = 0;
        for (int r = 0; r < n; r++)
        {
            WriteVarint(raw, (uint)(chunk.RunStart(r) - end));
            WriteVarint(raw, (uint)(chunk.RunLength(r) - 1));
            end = chunk.RunStart(r) + chunk.RunLength(r);
        }

        for (int r = 0; r < n; r++)
        {
            WriteVarint(raw, chunk.RunBlock(r));
        }

        for (int r = 0; r < n; r++)
        {
            var (tint, glow, shape) = chunk.RunExtras(r);
            raw.WriteByte((byte)((tint != 0 ? TintFlag : 0) | (glow != 0 ? GlowFlag : 0) | (shape != 0 ? ShapeFlag : 0)));
        }

        for (int r = 0; r < n; r++)
        {
            var (tint, glow, shape) = chunk.RunExtras(r);
            WriteIfSet(raw, tint);
            WriteIfSet(raw, glow);
            WriteIfSet(raw, shape);
        }
    }

    private static void WriteIfSet(MemoryStream raw, int value)
    {
        if (value != 0)
        {
            WriteVarint(raw, unchecked((uint)value));
        }
    }

    private static uint ZigZag(int v) => unchecked((uint)((v << 1) ^ (v >> 31)));

    private static int UnZigZag(uint v) => unchecked((int)(v >> 1) ^ -(int)(v & 1));

    private static void WriteVarint(MemoryStream s, uint v)
    {
        while (v >= 0x80)
        {
            s.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }

        s.WriteByte((byte)v);
    }

    // ---------------------------------------------------------------- decoding

    /// <summary>Reads a blob <see cref="Encode"/> wrote. Fails — never throws — on anything else: an unknown format byte,
    /// a stream that is not deflate, inflates past <see cref="MaxInflatedBytes"/>, ends early or carries bytes after the
    /// last chunk, a run past the chunk's end or overlapping the one before, an unknown flag.</summary>
    public static bool TryDecode(byte[]? data, out StructureOverlay overlay)
    {
        overlay = new StructureOverlay(Array.Empty<StructureOverlayChunk>());
        if (data is null || data.Length < 2 || data[0] != FormatVersion || !TryInflate(data, out byte[] raw, out int length))
        {
            return false;
        }

        var reader = new Reader(raw, length);
        if (!reader.TryVarint(out uint count) || count > (uint)(length / 4) + 1)
        {
            return false;
        }

        var chunks = new StructureOverlayChunk[count];
        for (int c = 0; c < chunks.Length; c++)
        {
            if (!TryReadChunk(ref reader, out var chunk))
            {
                return false;
            }

            chunks[c] = chunk;
        }

        if (!reader.AtEnd)
        {
            return false;
        }

        overlay = new StructureOverlay(chunks);
        return true;
    }

    private static bool TryInflate(byte[] data, out byte[] raw, out int length)
    {
        raw = Array.Empty<byte>();
        length = 0;
        try
        {
            using var source = new MemoryStream(data, 1, data.Length - 1, writable: false);
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (target.Length + read > MaxInflatedBytes)
                {
                    return false;
                }

                target.Write(buffer, 0, read);
            }

            raw = target.GetBuffer();
            length = (int)target.Length;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReadChunk(ref Reader reader, out StructureOverlayChunk chunk)
    {
        chunk = null!;
        if (!reader.TryVarint(out uint zx) || !reader.TryVarint(out uint zy) || !reader.TryVarint(out uint zz)
            || !reader.TryVarint(out uint count) || count > WorldConstants.BlocksPerChunk)
        {
            return false;
        }

        int n = (int)count;
        var starts = new ushort[n];
        var lengths = new ushort[n];
        var blocks = new ushort[n];
        int end = 0;
        for (int r = 0; r < n; r++)
        {
            if (!reader.TryVarint(out uint gap) || !reader.TryVarint(out uint less)
                || gap >= WorldConstants.BlocksPerChunk || less >= WorldConstants.BlocksPerChunk
                || end + (int)gap + (int)less + 1 > WorldConstants.BlocksPerChunk)
            {
                return false;
            }

            starts[r] = (ushort)(end + (int)gap);
            lengths[r] = (ushort)(less + 1);
            end = starts[r] + lengths[r];
        }

        for (int r = 0; r < n; r++)
        {
            if (!reader.TryVarint(out uint block) || block > ushort.MaxValue)
            {
                return false;
            }

            blocks[r] = (ushort)block;
        }

        if (!TryReadExtras(ref reader, n, out var tints, out var glows, out var shapes))
        {
            return false;
        }

        chunk = new StructureOverlayChunk(new ChunkCoord(UnZigZag(zx), UnZigZag(zy), UnZigZag(zz)), starts, lengths, blocks, tints, glows, shapes);
        return true;
    }

    private static bool TryReadExtras(ref Reader reader, int n, out int[]? tints, out int[]? glows, out int[]? shapes)
    {
        tints = glows = shapes = null;
        var flags = new byte[n];
        for (int r = 0; r < n; r++)
        {
            if (!reader.TryByte(out flags[r]) || (flags[r] & ~(TintFlag | GlowFlag | ShapeFlag)) != 0)
            {
                return false;
            }
        }

        for (int r = 0; r < n; r++)
        {
            if (!TryReadExtra(ref reader, flags[r], TintFlag, n, r, ref tints)
                || !TryReadExtra(ref reader, flags[r], GlowFlag, n, r, ref glows)
                || !TryReadExtra(ref reader, flags[r], ShapeFlag, n, r, ref shapes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadExtra(ref Reader reader, byte flags, byte flag, int n, int r, ref int[]? values)
    {
        if ((flags & flag) == 0)
        {
            return true;
        }

        if (!reader.TryVarint(out uint value))
        {
            return false;
        }

        (values ??= new int[n])[r] = unchecked((int)value);
        return true;
    }

    /// <summary>A forward reader over the inflated bytes.</summary>
    private struct Reader
    {
        private readonly byte[] _data;
        private readonly int _length;
        private int _at;

        public Reader(byte[] data, int length)
        {
            _data = data;
            _length = length;
            _at = 0;
        }

        public readonly bool AtEnd => _at == _length;

        public bool TryByte(out byte value)
        {
            if (_at >= _length)
            {
                value = 0;
                return false;
            }

            value = _data[_at++];
            return true;
        }

        public bool TryVarint(out uint value)
        {
            value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (!TryByte(out byte b))
                {
                    return false;
                }

                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return true;
                }
            }

            return false; // longer than a 32-bit varint
        }
    }

    // ---------------------------------------------------------------- the block-id palette

    /// <summary>The blob with every block id translated through <paramref name="remap"/> (old id → new id; the
    /// block-id palette migration of a content update that shifted the ids). Returns the SAME array when no id changed —
    /// and when the blob cannot be read, which the load path then reports — so a caller can skip a no-op write.</summary>
    public static byte[] RemapBlocks(byte[] data, IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (remap.Count == 0 || !TryDecode(data, out var overlay))
        {
            return data;
        }

        bool changed = false;
        var chunks = new StructureOverlayChunk[overlay._chunks.Length];
        for (int c = 0; c < chunks.Length; c++)
        {
            chunks[c] = overlay._chunks[c].Remapped(remap, out bool chunkChanged);
            changed |= chunkChanged;
        }

        return changed ? new StructureOverlay(chunks).Encode() : data;
    }
}

/// <summary>The cells of a <see cref="StructureOverlay"/> in one (canonical) chunk: runs of consecutive local indices that
/// hold one value, in ascending order, none overlapping.</summary>
public sealed class StructureOverlayChunk
{
    private readonly ushort[] _starts;
    private readonly ushort[] _lengths;
    private readonly ushort[] _blocks;
    private readonly int[]? _tints;
    private readonly int[]? _glows;
    private readonly int[]? _shapes;

    /// <summary>A chunk's runs: parallel arrays — the first local index, the length (1 … 4096) and the block of each run,
    /// ascending and apart; a value array (dye, glow, shape per run) is null when every one of its values is 0.</summary>
    public StructureOverlayChunk(ChunkCoord coord, ushort[] starts, ushort[] lengths, ushort[] blocks, int[]? tints, int[]? glows, int[]? shapes)
    {
        Coord = coord;
        _starts = starts;
        _lengths = lengths;
        _blocks = blocks;
        _tints = tints;
        _glows = glows;
        _shapes = shapes;
        int cells = 0;
        foreach (ushort length in lengths)
        {
            cells += length;
        }

        Count = cells;
    }

    public ChunkCoord Coord { get; }

    /// <summary>The cells of all runs.</summary>
    public int Count { get; }

    public int RunCount => _starts.Length;

    /// <summary>The first <see cref="WorldConstants.LocalIndex"/> of run <paramref name="r"/>.</summary>
    public int RunStart(int r) => _starts[r];

    public int RunLength(int r) => _lengths[r];

    public ushort RunBlock(int r) => _blocks[r];

    /// <summary>The dye, the glow and the packed shape of run <paramref name="r"/> (0 = none).</summary>
    public (int Tint, int Glow, int Shape) RunExtras(int r)
        => (_tints is null ? 0 : _tints[r], _glows is null ? 0 : _glows[r], _shapes is null ? 0 : _shapes[r]);

    /// <summary>The run that holds local index <paramref name="local"/>, −1 when none does.</summary>
    public int FindRun(int local)
    {
        int lo = 0, hi = _starts.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_starts[mid] > local)
            {
                hi = mid - 1;
            }
            else if (_starts[mid] + _lengths[mid] <= local)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }

    /// <summary>This chunk with its block ids translated (itself when none changed).</summary>
    internal StructureOverlayChunk Remapped(IReadOnlyDictionary<ushort, ushort> remap, out bool changed)
    {
        ushort[]? blocks = null;
        for (int r = 0; r < _blocks.Length; r++)
        {
            if (remap.TryGetValue(_blocks[r], out ushort to) && to != _blocks[r])
            {
                (blocks ??= (ushort[])_blocks.Clone())[r] = to;
            }
        }

        changed = blocks is not null;
        return blocks is null ? this : new StructureOverlayChunk(Coord, _starts, _lengths, blocks, _tints, _glows, _shapes);
    }
}
