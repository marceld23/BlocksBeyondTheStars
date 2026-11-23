// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    // ---------------------------------------------------------------- Textured debris
    //
    // The debris cubes (the eight pieces a broken block splits into, a drill's chips, rubble in space) were plain
    // coloured cubes. With GameLook.TexturedDebris every cube shows a piece of the block's tile instead: the two debris
    // emitters carry the piece's rectangle in the block atlas per particle (the particle system's custom data, handed
    // to FxDebris as two extra vertex streams), and the shader reads the atlas there. Nothing else changes — still one
    // particle system and one draw call per debris kind. Where no atlas or no such block exists, every call here
    // falls through to the plain Emit.
    public sealed partial class FxKit
    {
        /// <summary>What a debris cube shows: a rectangle of the block atlas and the colour that goes with it — or, for
        /// the pieces of a thing drawn in plain colours (a plant's body), two colours — and the light of its own.</summary>
        public readonly struct DebrisLook
        {
            /// <summary>The piece's rectangle in the block atlas (UV); zero = no tile, a plain coloured cube.</summary>
            public readonly Rect Tile;

            /// <summary>Linear. White for a block in its own colours, otherwise the colour the world gives it; a plain
            /// look's main colour.</summary>
            public readonly Color Tint;

            /// <summary>0 = the tile times <see cref="Tint"/>; above 0 the share of the block shader's recolour by
            /// luminance (a wild plant 0.85, bark 0.72) with <see cref="Gain"/> (1.6 and 1.4).</summary>
            public readonly float Recolour;

            public readonly float Gain;

            /// <summary>A plain look (<see cref="InColours"/>): the second of its two colours (linear).</summary>
            public readonly Color Second;

            /// <summary>True for plain cubes in <see cref="Tint"/> and <see cref="Second"/> — the pieces of a thing that
            /// is drawn in plain colours itself (a plant's body).</summary>
            public readonly bool Plain;

            /// <summary>The piece's own light, 0..1: the emission of the block it is a piece of (a glowing mushroom, a
            /// crystal, the Fifi plant's blossom, an ore, a lamp) — what glowed keeps glowing as it breaks.</summary>
            public readonly float Glow;

            private DebrisLook(Rect tile, Color tint, float recolour, float gain, Color second, bool plain, float glow)
            {
                Tile = tile;
                Tint = tint;
                Recolour = recolour;
                Gain = gain;
                Second = second;
                Plain = plain;
                Glow = Mathf.Clamp01(glow);
            }

            public DebrisLook(Rect tile, Color tint, float recolour, float gain, float glow = 0f)
                : this(tile, tint, recolour, gain, tint, false, glow)
            {
            }

            public bool Textured => Tile.width > 0f;

            /// <summary>Plain cubes in two colours (linear): most pieces wear <paramref name="main"/>, about one in three
            /// <paramref name="second"/>, each a small step lighter or darker; <paramref name="glow"/> is their own light.</summary>
            public static DebrisLook InColours(Color main, Color second, float glow = 0f)
                => new DebrisLook(default, main, 0f, 1f, second, true, glow);

            /// <summary>One piece of a plain look: one of its two colours by <paramref name="pick"/>, a step lighter or darker.</summary>
            private DebrisLook Shade(int pick)
            {
                Color c = pick % 3 == 2 ? Second : Tint;
                float k = 0.84f + (0.08f * (pick & 3));
                return new DebrisLook(default, new Color(c.r * k, c.g * k, c.b * k, 1f), 0f, 1f, Second, true, Glow);
            }

            /// <summary>The same look on another rectangle of the tile.</summary>
            private DebrisLook On(Rect tile) => new DebrisLook(tile, Tint, Recolour, Gain, Second, Plain, Glow);

            /// <summary>The same tile in the colour a world gives the block: <paramref name="mode"/> is the block
            /// shader's tint mode (1 = a wild plant, 4 = bark), <paramref name="tint"/> the linear colour.</summary>
            public DebrisLook Tinted(int mode, Color tint)
                => mode == 1 ? new DebrisLook(Tile, tint, 0.85f, 1.6f, Glow)
                    : mode == 4 ? new DebrisLook(Tile, tint, 0.72f, 1.4f, Glow)
                    : this;

            /// <summary>The same tile times a colour (linear) — a hull in its paint.</summary>
            public DebrisLook Times(Color tint) => new DebrisLook(Tile, tint, 0f, 1f, Glow);

            /// <summary>One of the eight pieces of a block split 2 × 2 × 2: the quarter of the tile that piece carried
            /// (bit 0 = the x half, bit 1 = the y half, bit 2 = the z half of the piece's place in the block).</summary>
            public DebrisLook Piece(int octant)
            {
                if (!Textured)
                {
                    return Plain ? Shade(octant) : this;
                }

                float w = Tile.width * 0.5f, h = Tile.height * 0.5f;
                int right = (octant ^ (octant >> 2)) & 1; // across the tile: x, mirrored on the far side
                int upper = (octant >> 1) & 1;
                return On(new Rect(Tile.x + (right * w), Tile.y + (upper * h), w, h));
            }

            /// <summary>A smaller cube of the same piece: a random square inside this rectangle, <paramref name="share"/>
            /// of its side (1 = the whole rectangle).</summary>
            public DebrisLook Part(float share)
            {
                if (!Textured || share >= 0.99f)
                {
                    return this;
                }

                share = Mathf.Clamp(share, 0.1f, 1f);
                float u = Random.Range(0f, 1f - share), v = Random.Range(0f, 1f - share);
                return On(new Rect(Tile.x + (u * Tile.width), Tile.y + (v * Tile.height), Tile.width * share, Tile.height * share));
            }

            /// <summary>A chip: a small square of the tile at a random place (<paramref name="texels"/> of the tile's 64
            /// along each side — a chip a few centimetres wide then shows texels about as large as the block's own).</summary>
            public DebrisLook Chip(int texels = 8)
            {
                if (!Textured)
                {
                    return Plain ? Shade(Random.Range(0, 12)) : this;
                }

                int n = BlockTextureAtlas.Tile;
                texels = Mathf.Clamp(texels, 1, n);
                float u = Random.Range(0, n - texels + 1) / (float)n;
                float v = Random.Range(0, n - texels + 1) / (float)n;
                float share = texels / (float)n;
                return On(new Rect(Tile.x + (u * Tile.width), Tile.y + (v * Tile.height), Tile.width * share, Tile.height * share));
            }
        }

        private static readonly int DebrisAtlasId = Shader.PropertyToID("_Sc_FxAtlas");
        private static readonly int DebrisOnId = Shader.PropertyToID("_Sc_LookDebris");
        private static readonly List<Vector4> DebrisTiles = new List<Vector4>(256);
        private static readonly List<Vector4> DebrisTones = new List<Vector4>(256);

        /// <summary>Position, normal, colour, the cube face's UV — and the two custom vectors. The shader receives them
        /// packed: TEXCOORD0 = (uv, tile.xy), TEXCOORD1 = (tile.zw, recolour, gain), TEXCOORD2.x = glow.</summary>
        private static readonly List<ParticleSystemVertexStream> DebrisStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.Custom1XYZW,
            ParticleSystemVertexStream.Custom2XYZ,
        };

        private static BlockTextureAtlas _debrisAtlas;

        /// <summary>The block atlas the debris reads its tiles from (the newest one built or handed out). Published as a
        /// global texture: the debris material is shared and outlives a world.</summary>
        public static void UseDebrisAtlas(BlockTextureAtlas atlas)
        {
            _debrisAtlas = atlas;
            bool on = GameLook.TexturedDebris && atlas != null && atlas.Texture != null;
            if (on)
            {
                Shader.SetGlobalTexture(DebrisAtlasId, atlas.Texture);
            }

            Shader.SetGlobalFloat(DebrisOnId, on ? 1f : 0f);
        }

        /// <summary>Called when an atlas is destroyed: the debris must not keep reading it.</summary>
        public static void ForgetDebrisAtlas(BlockTextureAtlas atlas, BlockTextureAtlas next)
        {
            if (ReferenceEquals(_debrisAtlas, atlas))
            {
                UseDebrisAtlas(ReferenceEquals(next, atlas) ? null : next);
            }
        }

        /// <summary>A block's tile in its own colours (default = none: no atlas, no such block).</summary>
        public static DebrisLook DebrisLookOf(BlockId id)
        {
            var atlas = _debrisAtlas;
            if (!GameLook.TexturedDebris || atlas == null || atlas.Texture == null || id.Value == 0 || id.Value >= AtlasBands.BlockEnd)
            {
                return default;
            }

            return new DebrisLook(atlas.TileUv(id.Value), Color.white, 0f, 1f, ChunkMesher.EmissionFor(atlas.Content, id));
        }

        /// <summary>The tile of the block with this key (rubble that belongs to no cell: space rock, hull plates, bolts).</summary>
        public static DebrisLook DebrisLookOf(string blockKey)
        {
            var def = _debrisAtlas?.Content?.GetBlock(blockKey);
            return def?.NumericId != null ? DebrisLookOf(def.NumericId) : default;
        }

        /// <summary>Emits ONE debris cube that shows <paramref name="look"/>; without a tile the plain cube in
        /// <paramref name="color"/> (sRGB-authored), and with the textured debris off exactly what <see cref="Emit"/> does.</summary>
        public static void EmitDebris(Kind kind, Vector3 position, Vector3 velocity, float size, float life, Color color, DebrisLook look)
        {
            if (!GameLook.TexturedDebris || (kind != Kind.Debris && kind != Kind.DebrisFloat))
            {
                Emit(kind, position, velocity, size, life, color);
                return;
            }

            var ps = Ensure().Emitter(kind);
            if (ps == null)
            {
                return;
            }

            int before = ps.particleCount;
            EmitLinear(ps, kind, position, velocity, size, life, look.Textured || look.Plain ? look.Tint : Lin(color));
            int count = ps.particleCount;
            if (count <= before)
            {
                return; // the emitter is full: nothing was emitted, and the last particle is someone else's
            }

            // A new particle is appended, so it is the last one. Its two vectors are written in every case — zeros for a
            // plain cube: nothing else initialises them, and the slot may still hold a dead particle's tile.
            ps.GetCustomParticleData(DebrisTiles, ParticleSystemCustomData.Custom1);
            ps.GetCustomParticleData(DebrisTones, ParticleSystemCustomData.Custom2);
            if (DebrisTiles.Count < count || DebrisTones.Count < count)
            {
                return;
            }

            DebrisTiles[count - 1] = look.Textured ? new Vector4(look.Tile.x, look.Tile.y, look.Tile.width, look.Tile.height) : Vector4.zero;
            DebrisTones[count - 1] = look.Textured ? new Vector4(look.Recolour, look.Gain, look.Glow, 0f) : new Vector4(0f, 0f, look.Glow, 0f);
            ps.SetCustomParticleData(DebrisTiles, ParticleSystemCustomData.Custom1);
            ps.SetCustomParticleData(DebrisTones, ParticleSystemCustomData.Custom2);
        }

        /// <summary><see cref="Burst"/> for debris cubes: <paramref name="chipTexels"/> above 0 gives every cube a random
        /// chip of the tile that wide, 0 one of the eight quarters in turn — narrowed to <paramref name="part"/> of its
        /// side for cubes that were made smaller.</summary>
        public static void BurstDebris(Kind kind, Vector3 at, int count, Vector3 direction, float coneDegrees, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color color, Color? color2, DebrisLook look, int chipTexels = 0,
            float part = 1f)
        {
            if (!look.Textured && !look.Plain)
            {
                Burst(kind, at, count, direction, coneDegrees, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, color, color2);
                return;
            }

            int n = Scaled(count);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = direction.sqrMagnitude > 1e-6f
                    ? RandomInCone(direction.normalized, coneDegrees)
                    : UnityEngine.Random.onUnitSphere;
                var c = color2.HasValue ? Color.Lerp(color, color2.Value, UnityEngine.Random.value) : color;
                EmitDebris(kind, at, dir * UnityEngine.Random.Range(speedMin, speedMax), UnityEngine.Random.Range(sizeMin, sizeMax),
                    UnityEngine.Random.Range(lifeMin, lifeMax), c, chipTexels > 0 ? look.Chip(chipTexels) : look.Piece(i).Part(part));
            }
        }

        /// <summary>The debris emitters' extra wiring: two custom vectors per particle and the vertex streams that carry
        /// them to the shader. Only with the textured debris on.</summary>
        private static void ConfigureDebrisTiles(ParticleSystem ps, ParticleSystemRenderer renderer)
        {
            if (!GameLook.TexturedDebris)
            {
                return;
            }

            // The custom data module stays OFF: it would generate the two vectors itself. The values come from
            // SetCustomParticleData alone (EmitDebris); a particle nobody wrote to carries zeros, a plain cube.
            renderer.SetActiveVertexStreams(DebrisStreams);
        }
    }
}
