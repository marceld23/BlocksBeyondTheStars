// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>What a free-floating far shape is (#3958, #3959).</summary>
    public enum FarFloatKind : byte
    {
        /// <summary>Solid ground in the air: a sky island, an arch bar, a cap's overhang — drawn as a block slab.</summary>
        Solid = 0,

        /// <summary>A standing column of water under an island rim (#3959) — drawn as a slim water curtain.</summary>
        Waterfall = 1,
    }

    /// <summary>
    /// One free-floating shape over a far column (#3958): a band with open air between it and the column's top (the
    /// ground, the sea or a band resting on them) — a sky island, an arch bar, a cap's overhang — or a waterfall (#3959).
    /// It fills the world Y range [<see cref="Bottom"/>, <see cref="Top"/>).
    /// </summary>
    public readonly struct FarFloat
    {
        /// <summary>World Y of the lowest cell (the lower face of the slab).</summary>
        public readonly int Bottom;

        /// <summary>World Y of the upper face (the top cell's Y + 1).</summary>
        public readonly int Top;

        public readonly FarFloatKind Kind;

        /// <summary>What the shape's top reads as from afar; its <see cref="FarSample.Top"/> equals <see cref="Top"/>.</summary>
        public readonly FarSample Surface;

        /// <summary>The block the sides and the underside are made of (the planet's deep block, a bar's own rock, ice).</summary>
        public readonly ushort SideBlock;

        public FarFloat(int bottom, int top, FarFloatKind kind, FarSample surface, ushort sideBlock)
        {
            Bottom = bottom;
            Top = top;
            Kind = kind;
            Surface = surface;
            SideBlock = sideBlock;
        }

        /// <summary>The same shape with a taller top (a build standing on it, #3960, or a band merged onto it).</summary>
        public FarFloat WithTop(int top, FarSample surface) => new FarFloat(Bottom, top, Kind, surface, SideBlock);
    }
}
