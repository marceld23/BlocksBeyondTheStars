// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How the 32×32 slots of the block texture atlas are shared out (#1952). A block's tile sits in the slot of its
    /// numeric id, so everything else needs room the ids can never grow into:
    /// <list type="bullet">
    /// <item><b>Blocks</b> 1..399 — one slot per block id (content validation caps the ids here).</item>
    /// <item><b>Official extras</b> 400..511 — tiles that are not blocks (door panels, machine parts) and the
    /// animation strips of official textures.</item>
    /// <item><b>Dynamic</b> 512..989 — animation strips of the local pack and of world textures; cleared and
    /// re-dealt whenever those layers change.</item>
    /// <item><b>Derived</b> 990..1023 — the variant and end-grain tiles, dealt from the top slot downward as before.</item>
    /// </list>
    /// Unity-free, so the headless suite pins it.
    /// </summary>
    public static class AtlasBands
    {
        public const int Cols = 32;
        public const int Rows = 32;

        /// <summary>First slot that is NOT a block slot (block ids must stay below it).</summary>
        public const int BlockEnd = 400;

        public const int ExtraStart = 400;
        public const int ExtraEnd = 512;

        public const int DynamicStart = 512;
        public const int DynamicEnd = 990;

        /// <summary>The derived tiles (variants, log end grain) live from here to the last slot.</summary>
        public const int DerivedStart = 990;
    }

    /// <summary>
    /// Deals atlas slots inside one band. A run of several slots — the frames of an animated texture — always lies
    /// in ONE atlas row, because the shader steps from frame to frame by adding a tile width to the U coordinate
    /// and a row break would land on an unrelated tile.
    /// </summary>
    public sealed class AtlasSlotAllocator
    {
        private readonly int _start;
        private readonly int _end;
        private readonly int _cols;
        private readonly bool[] _used;

        /// <param name="start">First slot of the band (inclusive).</param>
        /// <param name="end">End of the band (exclusive).</param>
        /// <param name="cols">Slots per atlas row.</param>
        public AtlasSlotAllocator(int start, int end, int cols = AtlasBands.Cols)
        {
            if (start < 0 || end <= start || cols <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(end), "An atlas band needs a positive range and row width.");
            }

            _start = start;
            _end = end;
            _cols = cols;
            _used = new bool[end - start];
        }

        /// <summary>Slots still free (not necessarily contiguous).</summary>
        public int Free
        {
            get
            {
                int n = 0;
                foreach (bool u in _used)
                {
                    if (!u)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>Reserves <paramref name="count"/> consecutive slots inside one row and returns the first, or -1
        /// when the band has no such run left. First fit, so a fixed order of requests gives fixed slots.</summary>
        public int Allocate(int count = 1)
        {
            if (count <= 0 || count > _cols)
            {
                return -1;
            }

            for (int slot = _start; slot + count <= _end; slot++)
            {
                int col = slot % _cols;
                if (col + count > _cols)
                {
                    slot += _cols - col - 1; // the run would cross the row end — continue at the next row
                    continue;
                }

                bool free = true;
                for (int i = 0; i < count; i++)
                {
                    if (_used[slot - _start + i])
                    {
                        free = false;
                        slot += i; // skip past the occupied slot
                        break;
                    }
                }

                if (!free)
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    _used[slot - _start + i] = true;
                }

                return slot;
            }

            return -1;
        }

        /// <summary>Returns a run to the band.</summary>
        public void Release(int first, int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                int index = first - _start + i;
                if (index >= 0 && index < _used.Length)
                {
                    _used[index] = false;
                }
            }
        }

        /// <summary>Frees the whole band.</summary>
        public void Clear() => Array.Clear(_used, 0, _used.Length);

        /// <summary>The slots of a run, for painting.</summary>
        public static IEnumerable<int> Run(int first, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return first + i;
            }
        }
    }
}
