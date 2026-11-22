// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Plant far pictures: beyond the bodies' range a plant is still its crossed picture, and with the painted tile's
    /// outline the plant would change shape where the range ends. So every
    /// species with a body gets its far picture BAKED from that body, once per world: individual 0 (a standing strand
    /// the running piece, 2) rasterised from two sides (<see cref="PlantFarBake"/>) into a 64 × 64 cell of a small far
    /// atlas, framed like the crossed picture frames a typical plant of the species, so the picture's outline lies where
    /// the body's does. The block shader looks a crossed picture's tile slot up in the atlas' last cell (PlantFar.hlsl)
    /// and samples the far atlas instead of the block atlas; a slot without an entry keeps its tile.
    /// <para>
    /// Left out on purpose: the solid plants (their far picture is the collider form, opaque — a cut-out picture would
    /// show its holes) and the water lily (seen from the side a flat pad is a line). The school club's authored plants
    /// are baked like every other species: near the camera they are bodies already, so the far picture must follow the
    /// body there too or the plant would change its outline at the range's end. Baked lazily, about a millisecond a
    /// frame, and uploaded in a few batches; the world's colour is applied by the shader as before.
    /// </para>
    /// <para>
    /// Tiers (<see cref="GameLook.PlantFarPictures"/>): a desktop client bakes on Low, Medium and High — on Low, where no
    /// plant has a body, the far picture is the only one. Potato and the browser bake nothing and keep the crossed picture
    /// of the tile; a change to Potato drops a finished atlas. Ticked by <see cref="PlantFarPictureDriver"/>, not by the
    /// chunks' bodies, since on Low there are none.
    /// </para>
    /// </summary>
    public sealed partial class PlantBodies
    {
        private const int FarCols = 16, FarRows = 8;
        private const int FarCells = (FarCols * FarRows) - 1; // the last cell holds the slot map
        private const int FarSide = PlantFarBake.Size;
        private const int FarUploadEvery = 24;
        private const double FarBudgetMs = 1.0;

        private static readonly int FarId = Shader.PropertyToID("_Sc_LookPlantFar");
        private static readonly int FarInfoId = Shader.PropertyToID("_Sc_LookPlantFarInfo");
        private static readonly HashSet<string> FarSolid = new HashSet<string>(FloraCatalog.SolidKeys());
        private static readonly Queue<ushort> FarQueue = new Queue<ushort>();
        private static readonly System.Diagnostics.Stopwatch FarClock = new System.Diagnostics.Stopwatch();
        private static readonly List<Vector3> FarVerts = new List<Vector3>();
        private static readonly List<Vector3> FarNormals = new List<Vector3>();
        private static readonly List<Color> FarTones = new List<Color>();
        private static readonly List<Vector4> FarParts = new List<Vector4>();
        private static readonly List<int> FarTris = new List<int>();
        private static Texture2D _farAtlas;
        private static byte[] _farPixels;
        private static int _farWorld = -1, _farAtlasVersion = -1, _farNext, _farUnsent;
        private static int _farFrame = -1;
        private static bool _farOn;
        private static bool _farOffPublished;
        private static PlantFarPictureDriver _farDriver;

        /// <summary>The object that ticks the far pictures once a frame; made with the first world (main thread).</summary>
        private static void EnsureFarDriver()
        {
            if (_farDriver != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("PlantFarPictures") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _farDriver = go.AddComponent<PlantFarPictureDriver>();
        }

        /// <summary>Once a frame from <see cref="PlantFarPictureDriver"/>: bakes on the presets that have far pictures and
        /// drops them on a change to one that has none (<see cref="GameLook.PlantFarPictures"/>).</summary>
        internal static void TickFarPictureDriver()
        {
            if (_farFrame == Time.frameCount)
            {
                return;
            }

            _farFrame = Time.frameCount;
            if (!GameLook.PlantFarPictures)
            {
                if (_farOn || !_farOffPublished)
                {
                    StopFarPictures(); // also once at the start, so the shader never reads an unset lookup
                }

                return;
            }

            if (!_farOn)
            {
                _farOn = true;
                _farOffPublished = false;
                _farWorld = -1; // (re)start: the tick below queues every species of the world
            }

            TickFarPictures();
        }

        /// <summary>Potato (or the browser): no far pictures — the shader's lookup goes off, so every crossed picture shows
        /// its tile again, and the atlas and the queue are dropped.</summary>
        private static void StopFarPictures()
        {
            _farOn = false;
            _farOffPublished = true;
            FarQueue.Clear();
            _farUnsent = 0;
            _farPixels = null;
            Shader.SetGlobalVector(FarInfoId, new Vector4(FarCols, FarRows, 0f, 6f));
            if (_farAtlas != null)
            {
                Destroy(_farAtlas);
                _farAtlas = null;
            }
        }

        /// <summary>Once a frame: starts over for a new world or atlas, bakes what the frame's budget allows and uploads
        /// the atlas in batches.</summary>
        private static void TickFarPictures()
        {
            if (_content == null || _atlas == null)
            {
                return;
            }

            if (_farWorld != _worldVersion || _farAtlasVersion != _atlasVersion)
            {
                StartFarPictures();
            }

            if (FarQueue.Count == 0 && _farUnsent == 0)
            {
                return;
            }

            FarClock.Restart();
            while (FarQueue.Count > 0 && (FarClock.Elapsed.TotalMilliseconds < FarBudgetMs || _farUnsent == 0))
            {
                if (BakeFarPicture(FarQueue.Dequeue()))
                {
                    _farUnsent++;
                }

                if (_farUnsent >= FarUploadEvery)
                {
                    break;
                }
            }

            if (_farUnsent >= FarUploadEvery || (FarQueue.Count == 0 && _farUnsent > 0))
            {
                UploadFarPictures();
            }
        }

        /// <summary>A new world or atlas: the old pictures go (the shader's lookup is off until the first new upload) and
        /// every species with a body is queued.</summary>
        private static void StartFarPictures()
        {
            _farWorld = _worldVersion;
            _farAtlasVersion = _atlasVersion;
            _farNext = 0;
            _farUnsent = 0;
            FarQueue.Clear();
            Shader.SetGlobalVector(FarInfoId, new Vector4(FarCols, FarRows, 0f, 6f));
            _farPixels = new byte[FarCols * FarSide * FarRows * FarSide * 4];
            if (_farAtlas != null)
            {
                Destroy(_farAtlas); // made unreadable by its last upload; the next upload builds a new one
                _farAtlas = null;
            }

            foreach (var def in _content.Blocks.Values)
            {
                string key = def.Key;
                int family = key != null ? PlantForms.FamilyOf(key) : -1;
                if (family < 0 || family == PlantForms.Lily || FarSolid.Contains(key)
                    || def.NumericId.Value == 0 || def.NumericId.Value >= BlockTextureAtlas.Cols * BlockTextureAtlas.Rows)
                {
                    continue;
                }

                FarQueue.Enqueue(def.NumericId.Value);
            }
        }

        /// <summary>Bakes one species into the next free cell and points its tile slot (and the slots of its variant tiles)
        /// at it. False when nothing was baked (no body, no room left).</summary>
        private static bool BakeFarPicture(ushort block)
        {
            var def = _content.BlockById(new BlockId(block));
            if (def == null || _farNext >= FarCells)
            {
                return false;
            }

            string key = def.Key;
            int family = PlantForms.FamilyOf(key);
            bool fixedForm = FloraCatalog.KeepsOwnColour(key);
            bool tall = FloraCatalog.IsTall(key);
            bool straight = family == PlantForms.Strand && PlantForms.StandsStraight(key);
            FarVerts.Clear();
            FarNormals.Clear();
            FarTones.Clear();
            FarParts.Clear();
            FarTris.Clear();
            PlantForms.Geometry(family, key, tall, def.Accent, fixedForm ? 0L : _worldSeed, fixedForm ? "fixed" : _location,
                straight ? 2 : 0, FarVerts, FarNormals, FarTones, FarParts, FarTris, out float radius);
            if (FarTris.Count == 0)
            {
                return false;
            }

            var result = PlantFarBake.Bake(ToNumerics(FarVerts), FarWeights(), FarTris, FarFrame(radius, tall, straight),
                FarColours(block, family, def.Accent || fixedForm), QualitySettings.activeColorSpace == ColorSpace.Linear);
            int cell = _farNext++;
            CopyFarCell(cell, result.Rgba);
            MapFarSlot(block, cell);
            if (_atlas.TryGetVariants(block, out var variants))
            {
                foreach (ushort slot in variants)
                {
                    MapFarSlot(slot, cell);
                }
            }

            return true;
        }

        /// <summary>The picture's frame in the body's own units: the crossed picture of a typical plant of the species is
        /// 0.84 m wide and as high as its species' height factor (1.85 for a tall one), and the body standing in it is
        /// scaled by its size roll, which the cell's room caps for a wide body (<see cref="Build"/>: 0.56 less the typical
        /// offset of 0.07, over the body's radius) — so the picture's units are the card's divided by that size.</summary>
        private static PlantFarBake.Frame FarFrame(float radius, bool tall, bool straight)
        {
            float size = straight ? 1f : Mathf.Max(0.5f, Mathf.Min(1f, 0.49f / Mathf.Max(radius, 0.1f)));
            return new PlantFarBake.Frame(0.42f / size, (tall ? 1.85f : 1f) / size);
        }

        /// <summary>The body's colour slots over the species' own tile colours (<see cref="PaletteFor"/>): the main colour,
        /// the second a lighter main (without the body's shift toward yellow, which the block shader would take for a
        /// blossom), the accent the tile's own blossom colour where the species has one, and the stems a darker main.</summary>
        private static PlantFarBake.Colours FarColours(ushort block, int family, bool accent)
        {
            var palette = PaletteFor(block, family == PlantForms.Fruit);
            var main = new System.Numerics.Vector3(palette.Main.r, palette.Main.g, palette.Main.b);
            var second = main * 1.25f;
            var tip = accent ? new System.Numerics.Vector3(palette.Accent.r, palette.Accent.g, palette.Accent.b) : second;
            return new PlantFarBake.Colours(main, second, tip, main * 0.6f);
        }

        private static List<System.Numerics.Vector3> ToNumerics(List<Vector3> source)
        {
            var list = new List<System.Numerics.Vector3>(source.Count);
            foreach (var v in source)
            {
                list.Add(new System.Numerics.Vector3(v.x, v.y, v.z));
            }

            return list;
        }

        // Per vertex: brightness, second, accent (the vertex colour) and the dark weight (the part data's y).
        private static List<System.Numerics.Vector4> FarWeights()
        {
            var list = new List<System.Numerics.Vector4>(FarTones.Count);
            for (int i = 0; i < FarTones.Count; i++)
            {
                list.Add(new System.Numerics.Vector4(FarTones[i].r, FarTones[i].g, FarTones[i].b, i < FarParts.Count ? FarParts[i].y : 0f));
            }

            return list;
        }

        private static void CopyFarCell(int cell, byte[] rgba)
        {
            int ox = (cell % FarCols) * FarSide, oy = (cell / FarCols) * FarSide, width = FarCols * FarSide;
            for (int y = 0; y < FarSide; y++)
            {
                System.Buffer.BlockCopy(rgba, y * FarSide * 4, _farPixels, (((oy + y) * width) + ox) * 4, FarSide * 4);
            }
        }

        /// <summary>The slot map in the last cell: texel (slot % 32, slot / 32) carries the cell + 1 in its alpha (0 = the
        /// tile stays) — alpha, because an sRGB texture leaves it untouched.</summary>
        private static void MapFarSlot(int slot, int cell)
        {
            int width = FarCols * FarSide;
            int x = ((FarCols - 1) * FarSide) + (slot % BlockTextureAtlas.Cols);
            int y = ((FarRows - 1) * FarSide) + (slot / BlockTextureAtlas.Cols);
            if (slot / BlockTextureAtlas.Cols < FarSide)
            {
                _farPixels[(((y * width) + x) * 4) + 3] = (byte)(cell + 1);
            }
        }

        private static void UploadFarPictures()
        {
            _farUnsent = 0;
            if (_farAtlas == null)
            {
                _farAtlas = new Texture2D(FarCols * FarSide, FarRows * FarSide, TextureFormat.RGBA32, mipChain: true, linear: false)
                {
                    name = "PlantFarPictures",
                    filterMode = FilterMode.Point, // like the block atlas: crisp texels; the slot map is read texel-exact
                    wrapMode = TextureWrapMode.Clamp,
                    ignoreMipmapLimit = true, // the slot map must stay whole on the presets that halve textures
                };
            }

            // The last batch frees the CPU copies (2 MB here, as much again in Unity's copy): a new world starts afresh.
            bool last = FarQueue.Count == 0;
            _farAtlas.SetPixelData(_farPixels, 0);
            _farAtlas.Apply(updateMipmaps: true, makeNoLongerReadable: last);
            if (last)
            {
                _farPixels = null;
            }

            Shader.SetGlobalTexture(FarId, _farAtlas);
            // x, y = the grid, z = on, w = the mip with one texel per cell (64 px → mip 6).
            Shader.SetGlobalVector(FarInfoId, new Vector4(FarCols, FarRows, 1f, 6f));
        }
    }
}
