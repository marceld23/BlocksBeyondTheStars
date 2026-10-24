// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client
{
    public static partial class ChunkMesher
    {
        /// <summary>Whether a block stops the coloured block light — the flood's own test (air, glass, water, plants,
        /// foliage and slim props let it through). #2951: an edit that flips this inside a lamp's reach changes the light
        /// beyond the edited chunk, so the client re-meshes the chunks in that reach.</summary>
        internal static bool StopsBlockLight(GameContent content, BlockId b)
            => !b.IsAir && !IsTransparent(content, b) && !IsFloraBlock(content, b) && !IsFoliageBlock(content, b)
               && !IsSlimPropBlock(content, b);
    }
}
