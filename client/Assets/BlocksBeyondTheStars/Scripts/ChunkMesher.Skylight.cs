// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    public static partial class ChunkMesher
    {
        /// <summary>
        /// The skylight of the air cells a build looks into (TEXCOORD1.x: 1 = sees the sky, 0 = sealed off). Faces are
        /// sky-lit only where their air cell is open overhead or near an opening, so caves, building/ship interiors and
        /// overhang undersides go dark while open ground and cliff faces stay sunlit; the sun and sky terms are scaled by it
        /// in the block shaders, while the headlamp and the emissive blocks light regardless — caves need a lamp.
        /// <para>The rules live in <see cref="SkylightRules"/> / <see cref="SkylightField"/> (Client.Core, tested headless):
        /// a cave mouth feathers over the 5×5 columns around it; a tree crown or a one-block roof keeps the shade floor
        /// (#1608); under three or more light-stopping blocks only the nearest opening at the cell's height lets light in,
        /// fading to nothing eight blocks into a tunnel (#3265); the floor of a narrow, deep shaft is dim, not open sky.</para>
        /// One field per meshing thread (builds run on thread-pool workers; WebGL has the one main thread).
        /// </summary>
        [System.ThreadStatic] private static SkylightField _skylightScratch;

        /// <summary>Starts the skylight of one build around the chunk at <paramref name="origin"/>.</summary>
        private static SkylightField BeginSkylight(System.Func<int, int, int, BlockId> worldBlock, BlockTraits traits, Vector3i origin)
        {
            var field = _skylightScratch ??= new SkylightField(WorldConstants.ChunkSize);
            field.Begin(worldBlock, traits.StopsSkyFn, origin.X, origin.Y, origin.Z);
            return field;
        }

        private sealed partial class BlockTraits
        {
            private System.Func<BlockId, bool> _stopsSkyFn;

            /// <summary>#3265: whether a block stops the daylight coming down — <see cref="StopsBlockLight"/>'s rule from the
            /// trait table (air, glass, water, plants, leaves, slim props and cutout cubes let it through). One delegate per
            /// content, shared by every build.</summary>
            public System.Func<BlockId, bool> StopsSkyFn
                => _stopsSkyFn ??= id => !id.IsAir && (FlagsOf(id) & TraitExposesOpaqueFace) == 0;
        }
    }
}
