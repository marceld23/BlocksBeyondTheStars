using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Dense block storage for a single chunk: a flat array of block ids indexed via
/// <see cref="WorldConstants.LocalIndex"/>. This is the in-memory representation used
/// by both client and server; persistence stores only the deltas against the
/// procedurally generated baseline.
/// </summary>
public sealed class ChunkData
{
    public ChunkCoord Coord { get; }

    private readonly ushort[] _blocks;

    public ChunkData(ChunkCoord coord)
    {
        Coord = coord;
        _blocks = new ushort[WorldConstants.BlocksPerChunk];
    }

    private ChunkData(ChunkCoord coord, ushort[] blocks)
    {
        Coord = coord;
        _blocks = blocks;
    }

    /// <summary>Wraps an existing raw block array (used when loading from storage).</summary>
    public static ChunkData FromRaw(ChunkCoord coord, ushort[] blocks)
    {
        if (blocks.Length != WorldConstants.BlocksPerChunk)
        {
            throw new ArgumentException(
                $"Expected {WorldConstants.BlocksPerChunk} blocks, got {blocks.Length}.", nameof(blocks));
        }

        return new ChunkData(coord, blocks);
    }

    public BlockId Get(int x, int y, int z) => new(_blocks[WorldConstants.LocalIndex(x, y, z)]);

    public void Set(int x, int y, int z, BlockId block) => _blocks[WorldConstants.LocalIndex(x, y, z)] = block.Value;

    /// <summary>Read-only view of the raw backing array for serialization.</summary>
    public ReadOnlySpan<ushort> RawBlocks => _blocks;

    public ushort[] ToArray() => (ushort[])_blocks.Clone();
}
