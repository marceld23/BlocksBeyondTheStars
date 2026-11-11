// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Streaming a tall landmark (#3489). A near chunk column streams three chunks above the player and a far one its
/// surface chunk plus one — a 120-block tower would be cut off at every distance. The stream pass therefore lifts a
/// column that carries a landmark to the landmark's top IN THAT COLUMN (not the box's: the air beside a tower costs
/// nothing), but never past the player's chunk sphere: a chunk further away than the sent-set prune radius would be
/// dropped by the sweep and unloaded by the client at once, and sent again on the next pass. What stands above the
/// sphere is drawn by the far silhouette (#3497).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far up (chunks, 3-D from the player's chunk) a landmark's columns stream for this player: one chunk
    /// inside the sent-set prune radius.</summary>
    private int LandmarkLiftRadius(PlayerSession session) => _landmarks.Count == 0 ? 0 : SentSetPruneRadius(session) - 1;

    /// <summary>The highest chunk Y a landmark reaches in chunk column (<paramref name="chunkX"/>, <paramref name="chunkZ"/>),
    /// or <see cref="int.MinValue"/> when none stands in it.</summary>
    private int LandmarkTopChunk(int chunkX, int chunkZ)
    {
        if (_landmarks.Count == 0)
        {
            return int.MinValue;
        }

        int x0 = chunkX * WorldConstants.ChunkSize, z0 = chunkZ * WorldConstants.ChunkSize, last = WorldConstants.ChunkSize - 1;
        int top = int.MinValue;
        foreach (var lm in _landmarks)
        {
            int relX = WorldConstants.WrapDeltaX(x0 - lm.Origin.X, _world.Circumference), relZ = z0 - lm.Origin.Z;
            if (relX + last >= 0 && relX < lm.Structure.Width && relZ + last >= 0 && relZ < lm.Structure.Length)
            {
                top = System.Math.Max(top, lm.TopChunk(relX, relZ));
            }
        }

        return top;
    }

    /// <summary>Test/util: the top chunk a landmark lifts a column's streaming to (<see cref="int.MinValue"/> = none).</summary>
    internal int LandmarkTopChunkForTest(int chunkX, int chunkZ) => LandmarkTopChunk(chunkX, chunkZ);

    /// <summary>Test/util: the chunk-Y span column (dx, dz) streams around <paramref name="center"/> for a view radius.</summary>
    internal (int LoDy, int HiDy) StreamColumnSpanForTest(ChunkCoord center, int dx, int dz, int viewRadius)
        => StreamColumnSpan(center, dx, dz, NearFullColumnRadius, _generator.SeaLevel(_world.Planet),
            _landmarks.Count == 0 ? 0 : System.Math.Min(viewRadius + 4, 20) - 1);

    internal sealed partial class LandmarkInstance
    {
        private int[]? _columnTop;
        private readonly Dictionary<(int X, int Z), int> _topChunks = new();

        /// <summary>The highest structure row with a block in column (x, z), −1 when the column is empty.</summary>
        private int ColumnTop(int x, int z)
        {
            if (_columnTop is null)
            {
                var s = Structure;
                _columnTop = new int[s.Width * s.Length];
                for (int cx = 0; cx < s.Width; cx++)
                {
                    for (int cz = 0; cz < s.Length; cz++)
                    {
                        int y = s.Height - 1;
                        while (y >= 0 && s.Get(cx, y, cz) == 0)
                        {
                            y--;
                        }

                        _columnTop[(cx * s.Length) + cz] = y;
                    }
                }
            }

            return _columnTop[(x * Structure.Length) + z];
        }

        /// <summary>The chunk Y of the highest block in the 16 × 16 columns whose corner lies at
        /// (<paramref name="relX"/>, <paramref name="relZ"/>) from the origin — <see cref="int.MinValue"/> when they are
        /// empty. Cached per chunk column.</summary>
        public int TopChunk(int relX, int relZ)
        {
            if (_topChunks.TryGetValue((relX, relZ), out int cached))
            {
                return cached;
            }

            int top = -1;
            int x1 = System.Math.Min(Structure.Width - 1, relX + WorldConstants.ChunkSize - 1);
            int z1 = System.Math.Min(Structure.Length - 1, relZ + WorldConstants.ChunkSize - 1);
            for (int x = System.Math.Max(0, relX); x <= x1; x++)
            {
                for (int z = System.Math.Max(0, relZ); z <= z1; z++)
                {
                    top = System.Math.Max(top, ColumnTop(x, z));
                }
            }

            int chunk = top < 0 ? int.MinValue : WorldConstants.WorldToChunk(Origin.Y + top);
            _topChunks[(relX, relZ)] = chunk;
            return chunk;
        }
    }
}
