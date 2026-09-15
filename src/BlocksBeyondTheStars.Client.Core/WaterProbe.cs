// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's "is this cell under water?" for the underwater wash, the audio muffle and swimming — the same
    /// <see cref="WetCell"/> rule the server's oxygen drain uses (#1902), so a head inside a submerged kelp stalk or
    /// ladder looks, sounds and breathes like water everywhere. Built once per world + content pair: the delegates
    /// are allocated here, not on every per-frame probe.
    /// </summary>
    public sealed class WaterProbe
    {
        private readonly ClientWorld _world;
        private readonly GameContent _content;
        private readonly ushort _water;
        private readonly Func<int, int, int, ushort> _blockAt;
        private readonly Func<int, int, int, bool> _nonFull;

        public WaterProbe(ClientWorld world, GameContent content)
        {
            _world = world;
            _content = content;
            _water = content.GetBlock("water")?.NumericId.Value ?? 0;
            _blockAt = (x, y, z) => _world.GetBlock(x, y, z).Value;
            _nonFull = (x, y, z) => WetCell.IsNonFullKey(_content.BlockById(_world.GetBlock(x, y, z))?.Key)
                || !ShapeCode.IsCube(_world.GetShape(x, y, z));
        }

        /// <summary>True when this probe was built for exactly this world and content (the caller rebuilds it on a
        /// world swap).</summary>
        public bool IsFor(ClientWorld world, GameContent content) => ReferenceEquals(world, _world) && ReferenceEquals(content, _content);

        /// <summary>True when the cell is water, or a plant / slim prop / building form the water surrounds.</summary>
        public bool IsWet(int x, int y, int z) => WetCell.IsWet(_blockAt, _water, _nonFull, x, y, z);
    }
}
