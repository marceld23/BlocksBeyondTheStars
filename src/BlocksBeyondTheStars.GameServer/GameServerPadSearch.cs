// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3068: the landing-pad search of a new body, made faster without moving a single pad. A new world's first boot spent
/// 15–19 s on "landing pads": the routed river grid (built lazily by the first water test, ~245 000 surface heights on a
/// start planet) and about 14 600 candidate columns per pad. The river grid is now sampled on several threads
/// (<see cref="BuildWorkers"/>, <c>WorldGenerator.SetBuildWorkers</c>), and one pad search remembers each column's
/// answers, because neighbouring candidates share most of their footprint samples. Searching the pads themselves side by
/// side was measured and dropped: on a laptop an all-core burst lowers the clock for the single-threaded work after it,
/// and the pads gained little against a thread-local generator override in every terrain query.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Extra threads for a one-off world build (#3068): a new body's river grid. None when the server runs
    /// without background threads (<see cref="ServerConfig.ChunkGenWorkers"/> 0 — the in-browser singleplayer, or a host
    /// that asked for it); otherwise every core but the caller's, at most <see cref="ServerConfig.ChunkGenWorkersCeiling"/>.
    /// The work is a burst at a world's first load, never a running cost.</summary>
    private int BuildWorkers => _config.ChunkGenWorkers == 0
        ? 0
        : System.Math.Clamp(System.Environment.ProcessorCount - 1, 1, ServerConfig.ChunkGenWorkersCeiling);

    /// <summary>
    /// The per-column answers of one pad search (#3068): water, a band overhead and the surface height, one slot per
    /// column of the search window. The ring search asks 13 + 5 + 9 footprint columns per candidate on a three-block grid,
    /// and its neighbours ask most of them again — the generator's own column memo is capped at 262 144 entries and clears
    /// itself whole when full, which a search of up to 140 000 columns per pad, twelve pads in a row, kept hitting mid-search.
    /// The window holds every column the search can reach (the budget plus the footprint radius), so each is asked of
    /// the generator once; a column outside it is simply not remembered. Every answer is the generator's own, so the
    /// pads come out exactly as before. Six bytes per column: ~0.9 MB for a land search, ~2.3 MB for an ocean one, gone
    /// with the search.
    /// </summary>
    internal sealed class PadColumnMemo
    {
        private const byte Unknown = 0;
        private const byte No = 1;
        private const byte Yes = 2;
        private const int NoHeight = int.MinValue;

        private readonly int _baseX;
        private readonly int _baseZ;
        private readonly int _reach;
        private readonly int _size;
        private readonly int _circumference;
        private readonly System.Func<int, int, bool> _wet;
        private readonly System.Func<int, int, bool> _band;
        private readonly System.Func<int, int, int> _height;
        private readonly byte[] _wetCells;
        private readonly byte[] _bandCells;
        private readonly int[] _heights;

        /// <param name="wet">The water answer for one column (the generation-8 dry test's column check).</param>
        /// <param name="band">Whether a band (an arch bar, a crown, a sky island) hangs over the column.</param>
        /// <param name="height">The column's surface height.</param>
        public PadColumnMemo(int baseX, int baseZ, int reach, int circumference,
            System.Func<int, int, bool> wet, System.Func<int, int, bool> band, System.Func<int, int, int> height)
        {
            _baseX = baseX;
            _baseZ = baseZ;
            _reach = reach;
            _size = 2 * reach + 1;
            _circumference = circumference;
            _wet = wet;
            _band = band;
            _height = height;
            _wetCells = new byte[_size * _size];
            _bandCells = new byte[_size * _size];
            _heights = new int[_size * _size];
            System.Array.Fill(_heights, NoHeight);
        }

        /// <summary>The column's slot, or -1 outside the window. X is wrapped to its shortest distance from the planned
        /// column, so a search across the longitude seam keeps one slot per real column.</summary>
        private int Slot(int x, int z)
        {
            int dx = WorldConstants.WrapDeltaX(x - _baseX, _circumference);
            int dz = z - _baseZ;
            if (dx < -_reach || dx > _reach || dz < -_reach || dz > _reach)
            {
                return -1;
            }

            return (dz + _reach) * _size + dx + _reach;
        }

        public bool IsWet(int x, int z) => Remembered(_wetCells, x, z, _wet);

        public bool HasBand(int x, int z) => Remembered(_bandCells, x, z, _band);

        public int Height(int x, int z)
        {
            int slot = Slot(x, z);
            if (slot < 0)
            {
                return _height(x, z);
            }

            int known = _heights[slot];
            if (known == NoHeight)
            {
                known = _height(x, z);
                _heights[slot] = known;
            }

            return known;
        }

        private bool Remembered(byte[] cells, int x, int z, System.Func<int, int, bool> ask)
        {
            int slot = Slot(x, z);
            if (slot < 0)
            {
                return ask(x, z);
            }

            byte known = cells[slot];
            if (known != Unknown)
            {
                return known == Yes;
            }

            bool answer = ask(x, z);
            cells[slot] = answer ? Yes : No;
            return answer;
        }
    }
}
