// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public sealed partial class BlockTextureAtlas
    {
        // ---------------------------------------------------------------- animated tiles (#1957)
        //
        // A block whose texture has several frames keeps frame 0 in its OWN slot (icons, the held block, the far
        // terrain and every UI read that one) and gets all its frames as a row-contiguous STRIP in the dynamic
        // band. Mesh UVs stay on the own slot; the mesher adds "frames, speed, strip start" to the tint-mode
        // float of the face (AnimationCode), and the block shaders move the UV onto the strip cell of the current
        // frame. So a texture that becomes animated — a world texture arrives, the player switches a pack —
        // needs a re-mesh of the chunks (AnimationVersion tells the owner), but never new UVs.

        private readonly AtlasSlotAllocator _strips = new AtlasSlotAllocator(AtlasBands.DynamicStart, AtlasBands.DynamicEnd);
        private readonly System.Collections.Generic.Dictionary<int, (int Start, int Frames, int Fps)> _strip
            = new System.Collections.Generic.Dictionary<int, (int, int, int)>();
        private volatile int[] _animationCodes = new int[AtlasBands.BlockEnd]; // immutable snapshot: the mesher reads it off the main thread

        /// <summary>Bumps whenever a block gained, lost or moved its strip — loaded chunks must re-mesh then.</summary>
        public int AnimationVersion { get; private set; }

        /// <summary>What the mesher adds to a face's tint-mode float for block <paramref name="id"/>:
        /// <c>16·frames + 256·speedIndex + 1024·stripStart</c>, or 0 for a still tile. The tint mode itself stays in
        /// the low four bits; every term is an exact float (the sum stays far below 2²⁴).</summary>
        public float AnimationCode(ushort id)
        {
            var codes = _animationCodes;
            return id < codes.Length ? codes[id] : 0f;
        }

        private void UpdateStrip(int id, BlockDefinition def)
        {
            if (id <= 0 || id >= AtlasBands.BlockEnd || _officialOnly)
            {
                return;
            }

            var frames = GameTextures.Resolve(def.Key);
            int count = frames != null && frames.Animated ? Mathf.Min(frames.Frames.Length, BlocksBeyondTheStars.Shared.Textures.TextureTiles.MaxFrames) : 1;
            int speed = count > 1 ? System.Array.IndexOf(SpeedSteps, frames.Fps) : -1;
            bool had = _strip.TryGetValue(id, out var old);
            if (count <= 1 || speed < 0)
            {
                if (had)
                {
                    _strips.Release(old.Start, old.Frames);
                    _strip.Remove(id);
                    _animationDirty = true;
                }

                return;
            }

            int start = had && old.Frames == count ? old.Start : -1;
            if (start < 0)
            {
                if (had)
                {
                    _strips.Release(old.Start, old.Frames);
                }

                start = _strips.Allocate(count);
                if (start < 0)
                {
                    // The band is full (hundreds of animated tiles): this block stays still rather than breaking.
                    _strip.Remove(id);
                    _animationDirty |= had;
                    return;
                }
            }

            for (int f = 0; f < count; f++)
            {
                int slot = start + f;
                BlitRaw(frames.Frames[f], (slot % Cols) * Tile, (slot / Cols) * Tile);
                MarkStripSlot(slot, def.Key); // a painted animation is exempt from the calm in its strip too
                if (def.Key == "water")
                {
                    FadeTileAlpha((slot % Cols) * Tile, (slot / Cols) * Tile, 0.28f); // like its own slot
                }
            }

            if (!had || old.Start != start || old.Frames != count || old.Fps != speed)
            {
                _animationDirty = true;
            }

            _strip[id] = (start, count, speed);
        }

        private static readonly int[] SpeedSteps = { 2, 4, 8, 12 }; // TextureTiles.AllowedFps, as the shader's index
        private bool _animationDirty;

        private void PublishAnimation()
        {
            var codes = new int[AtlasBands.BlockEnd];
            foreach (var kv in _strip)
            {
                codes[kv.Key] = (16 * kv.Value.Frames) + (256 * kv.Value.Fps) + (1024 * kv.Value.Start);
            }

            _animationCodes = codes;
            if (_animationDirty)
            {
                _animationDirty = false;
                AnimationVersion++;
            }
        }
    }
}
