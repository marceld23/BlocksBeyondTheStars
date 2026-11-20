// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3859: tall trees stream whole. A far chunk column streams only the band around its terrain surface (one chunk above
/// it), a near column three chunks above the player's own — a giant tree (up to 64 blocks) or an ordinary tree over a
/// surface high in its chunk stood out of that span, and the client, which draws a chunk it was never sent as air, cut its
/// crown off flat until the player came close. Every column now streams up to the top of its tall flora
/// (<see cref="WorldGeneration.WorldGenerator.TallFloraTopInSquare"/>), capped by the player's chunk sphere like the
/// landmark lift (a chunk beyond it would be pruned from the sent-set and sent again on the next pass).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far (in chunks) a tall tree's column may lift its streaming for this player: just inside the sent-set's
    /// prune sphere.</summary>
    private int FloraLiftRadius(PlayerSession session) => SentSetPruneRadius(session) - 1;

    /// <summary>The span of column (<paramref name="dx"/>, <paramref name="dz"/>) raised to the top of its tall flora,
    /// as far as the sphere of radius <paramref name="floraLift"/> around the player's chunk reaches.</summary>
    private (int LoDy, int HiDy) WithFloraTop(ChunkCoord center, int dx, int dz, (int LoDy, int HiDy) span, int floraLift)
    {
        int reach = (floraLift * floraLift) - (dx * dx) - (dz * dz);
        if (reach <= 0)
        {
            return span;
        }

        int top = FloraTopChunk(center.X + dx, center.Z + dz);
        if (top == int.MinValue)
        {
            return span;
        }

        return (span.LoDy, System.Math.Max(span.HiDy, System.Math.Min(top - center.Y, (int)System.Math.Sqrt(reach))));
    }

    /// <summary>The highest chunk Y the tall flora of chunk column (<paramref name="chunkX"/>, <paramref name="chunkZ"/>)
    /// reaches, or <see cref="int.MinValue"/> — cached per canonical column on the world (a pure function of the generator).</summary>
    private int FloraTopChunk(int chunkX, int chunkZ)
    {
        var tops = _worlds.Active.FloraTopChunks;
        var key = (WorldConstants.CanonicalChunkX(chunkX, _world.Circumference), WorldConstants.CanonicalChunkZ(chunkZ, _world.Circumference));
        if (tops.TryGetValue(key, out int top))
        {
            return top;
        }

        int topY = _generator.TallFloraTopInSquare(_world.Planet, key.Item1 * WorldConstants.ChunkSize,
            key.Item2 * WorldConstants.ChunkSize, WorldConstants.ChunkSize);
        top = topY == int.MinValue ? int.MinValue : WorldConstants.WorldToChunk(topY);
        if (tops.Count >= FarColumnBandCacheCap)
        {
            tops.Clear(); // keep the table bounded, like the far bands
        }

        tops[key] = top;
        return top;
    }

    /// <summary>Test/util: the tall-flora top chunk of a column (<see cref="int.MinValue"/> = none).</summary>
    internal int FloraTopChunkForTest(int chunkX, int chunkZ) => FloraTopChunk(chunkX, chunkZ);
}
