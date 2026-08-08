// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Runtime tile atlas for player-painted block designs (#819) — the paint sibling of
    /// <see cref="BlockTextureAtlas"/>, built like the microfauna atlas. One 64 px slot per registered
    /// design (a 32×32 bitmap point-upscaled ×2, so the pixel-art look survives mipmapping better), 256
    /// slots — matching the server's per-save design cap. Painted faces render in chunk submesh 2 with a
    /// material that samples this texture; the design id → UV lookup crosses into the OFF-THREAD mesher,
    /// so it is published as an immutable snapshot dictionary swapped wholesale on every change
    /// (copy-on-write) — a builder thread only ever sees a complete, never-mutated map.
    /// </summary>
    public sealed class PaintDesignAtlas
    {
        /// <summary>Design bitmap side length in pixels (matches the editor grid + the wire format).</summary>
        public const int Size = 32;

        /// <summary>Hex chars per design on the wire: one palette index per pixel.</summary>
        public const int PixelChars = Size * Size;

        public const int Tile = 64;
        public const int Cols = 16;
        public const int Rows = 16;

        /// <summary>The paint "canvas" behind unpainted (palette-0) pixels: a warm paper white, so a design
        /// reads as a painted plate. The face editor composites onto skin instead; blocks get a canvas.</summary>
        public static readonly Color32 Canvas = new Color32(233, 230, 222, 255);

        public Texture2D Texture { get; private set; }

        /// <summary>A tiny flat normal map (all "straight out") for the paint material — the atlas shader
        /// expects a _NormalTex, and the block atlas' Sobel normals would map garbage onto design slots.</summary>
        public Texture2D FlatNormal { get; private set; }

        private readonly Dictionary<int, int> _slotById = new();
        private readonly Dictionary<int, string> _pixelsById = new(); // live bitmaps (repaint pre-load, main thread only)
        private Dictionary<int, Rect> _uvById = new(); // COW snapshot — replaced, never mutated (mesher threads read it)
        private int _nextSlot;

        public PaintDesignAtlas()
        {
            Texture = new Texture2D(Cols * Tile, Rows * Tile, TextureFormat.RGBA32, mipChain: true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color32[Cols * Tile * Rows * Tile];
            Texture.SetPixels32(clear);
            Texture.Apply(updateMipmaps: true);

            FlatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };
            var flat = new Color32[16];
            for (int i = 0; i < flat.Length; i++)
            {
                flat[i] = new Color32(128, 128, 255, 255);
            }

            FlatNormal.SetPixels32(flat);
            FlatNormal.Apply();
        }

        public void Destroy()
        {
            if (Texture != null)
            {
                Object.Destroy(Texture);
                Texture = null;
            }

            if (FlatNormal != null)
            {
                Object.Destroy(FlatNormal);
                FlatNormal = null;
            }
        }

        /// <summary>Registers (or updates) a design bitmap: assigns an atlas slot, blits the pixels and
        /// republishes the UV snapshot. EMPTY pixels are a moderation wipe — the slot goes blank and the id
        /// drops out of the lookup, so referencing faces fall back to the plain block texture on remesh.
        /// Ignores malformed bitmaps and (silently) designs beyond the slot capacity, mirroring the server cap.</summary>
        public void Register(int id, string pixels) => Register(id, pixels, string.Empty);

        /// <summary>As above, recording who designed it so a player copying it off a block can credit
        /// them (#846). An empty owner leaves whatever was known before.</summary>
        public void Register(int id, string pixels, string owner)
        {
            if (!string.IsNullOrEmpty(owner))
            {
                _ownerById[id] = owner;
            }

            RegisterPixels(id, pixels);
        }

        /// <summary>Display name of whoever registered a design (empty when unknown).</summary>
        public string OwnerOf(int id) => _ownerById.TryGetValue(id, out var owner) ? owner : string.Empty;

        private readonly Dictionary<int, string> _ownerById = new();

        private void RegisterPixels(int id, string pixels)
        {
            pixels ??= string.Empty;
            if (id <= 0 || (pixels.Length != 0 && pixels.Length != PixelChars))
            {
                return;
            }

            if (pixels.Length == 0)
            {
                _pixelsById.Remove(id);
                if (_slotById.TryGetValue(id, out int wipeSlot))
                {
                    BlitTile(wipeSlot, null);
                    Texture.Apply(updateMipmaps: true);
                }

                if (_uvById.ContainsKey(id))
                {
                    var shrunk = new Dictionary<int, Rect>(_uvById);
                    shrunk.Remove(id);
                    _uvById = shrunk; // publish
                }

                return;
            }

            if (!_slotById.TryGetValue(id, out int slot))
            {
                if (_nextSlot >= Cols * Rows)
                {
                    return; // atlas full — the server cap should prevent this; extra ids just stay unresolved
                }

                slot = _nextSlot++;
                _slotById[id] = slot;
            }

            _pixelsById[id] = pixels;
            BlitTile(slot, pixels);
            Texture.Apply(updateMipmaps: true);

            var grown = new Dictionary<int, Rect>(_uvById) { [id] = SlotUv(slot) };
            _uvById = grown; // publish
        }

        /// <summary>The live bitmap of a design (main thread; used to pre-load the editor when repainting).</summary>
        public bool TryGetPixels(int id, out string pixels) => _pixelsById.TryGetValue(id, out pixels);

        /// <summary>The design-id → atlas-UV lookup as a thread-safe snapshot for one mesh build. The captured
        /// dictionary is immutable (replaced wholesale on change), so worker threads read it freely.</summary>
        public System.Func<int, Rect?> Snapshot()
        {
            var map = _uvById;
            return id => map.TryGetValue(id, out var r) ? r : (Rect?)null;
        }

        /// <summary>True when at least one live (non-wiped) design is registered.</summary>
        public bool HasAny => _uvById.Count > 0;

        // Same inset as BlockTextureAtlas.TileUv: keeps bilinear/mip sampling inside the slot.
        private static Rect SlotUv(int slot)
        {
            int x = slot % Cols, y = slot / Cols;
            float w = 1f / Cols, h = 1f / Rows;
            const float inset = 0.001f;
            return new Rect(x * w + inset, y * h + inset, w - 2f * inset, h - 2f * inset);
        }

        /// <summary>Decodes one design row-major (row 0 = TOP, like the editor grid) and writes it ×2
        /// point-upscaled into the slot; null pixels blanks the slot to transparent.</summary>
        private void BlitTile(int slot, string pixels)
        {
            int ox = (slot % Cols) * Tile, oy = (slot / Cols) * Tile;
            var buf = new Color32[Tile * Tile];
            if (pixels != null)
            {
                var palette = FacePalette.Colors;
                for (int gy = 0; gy < Size; gy++)
                {
                    for (int gx = 0; gx < Size; gx++)
                    {
                        int index = HexVal(pixels[gy * Size + gx]);
                        Color32 c = index <= 0 || index >= palette.Length ? Canvas : palette[index];
                        c.a = 255; // the paint submesh is opaque — palette 0 becomes the canvas colour
                        int tx = gx * 2, ty = (Size - 1 - gy) * 2; // grid row 0 = top → texture row 0 = bottom
                        buf[ty * Tile + tx] = c;
                        buf[ty * Tile + tx + 1] = c;
                        buf[(ty + 1) * Tile + tx] = c;
                        buf[(ty + 1) * Tile + tx + 1] = c;
                    }
                }
            }

            Texture.SetPixels32(ox, oy, Tile, Tile, buf);
        }

        private static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => 0,
        };
    }
}
