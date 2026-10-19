// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2836: the surfaces of held models. Every cube part of a tool, a weapon, a gadget, a glove or an NPC's tool is made of
    /// a material (<see cref="HeldMaterials"/>, decided by <see cref="HeldMaterialRules"/>), and each material has a texture
    /// key (<c>held_metal</c>, <c>held_wood</c> …). The material's tile is light and mostly grey: it is multiplied over the
    /// part's own colour, so one tile serves every colour of wood or plastic. Modelled on <see cref="PropTextures"/> (#1956):
    /// <list type="bullet">
    /// <item>one shared LitColor material per (colour, glow, material) — a held model is rebuilt on every quick-bar switch, for
    /// every player in sight, and a material per part per rebuild would pile up;</item>
    /// <item>without a tile in any layer a part keeps its flat colour, exactly as before;</item>
    /// <item>a texture change (a pack, a world texture, the texture editor) updates the shared materials IN PLACE through
    /// <see cref="GameTextures.Changed"/>; the texture editor lists the twelve keys.</item>
    /// </list>
    /// The tiles repeat in metres (<see cref="HeldMaterialRules.TileMetres"/>): each part gets a unit cube whose UVs are as long
    /// as the part, cached per size bucket (<see cref="CubeMesh"/>), so a long blade shows the grain several times instead of
    /// one stretched tile.
    /// </summary>
    public static class HeldTextures
    {
        /// <summary>The colour a held material's texture starts from in the texture editor when no layer has one yet: a light
        /// grey, since the tile is multiplied over the part's colour.</summary>
        public static readonly Color EditorBase = new Color(0.86f, 0.86f, 0.86f);

        private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly HashSet<string> Loaded = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<(int Rgb, bool Glow, string Material), Material> Materials =
            new Dictionary<(int Rgb, bool Glow, string Material), Material>();
        private static readonly Dictionary<(int X, int Y, int Z), Mesh> Meshes = new Dictionary<(int X, int Y, int Z), Mesh>();
        private static Shader _shader;
        private static bool _subscribed;

        /// <summary>The texture key of every held material, in the texture editor's order.</summary>
        public static IEnumerable<string> Keys
        {
            get
            {
                foreach (string m in HeldMaterials.All)
                {
                    yield return HeldMaterials.TextureKey(m);
                }
            }
        }

        /// <summary>The material named by a texture key (<c>held_wood</c> → <c>wood</c>), or null for any other key.</summary>
        public static string MaterialOfKey(string key)
        {
            if (key == null || !key.StartsWith(HeldMaterials.TexturePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string m = key.Substring(HeldMaterials.TexturePrefix.Length);
            return HeldMaterials.IsKnown(m) ? m : null;
        }

        /// <summary>The colour the texture editor starts a held material's texture from (a flat tile), or null for a key that
        /// is no held material.</summary>
        public static Color? BaseColor(string key) => MaterialOfKey(key) != null ? EditorBase : (Color?)null;

        /// <summary>The shared material of a part in <paramref name="color"/> made of <paramref name="material"/>: the
        /// material's tile multiplied over the colour, or the flat colour while no layer has the tile. A glowing part is fully
        /// lit whatever the light (an energy coil glows in a cave).</summary>
        public static Material MaterialFor(Color color, bool glow, string material)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                GameTextures.Changed += OnTexturesChanged;
            }

            if (!HeldMaterials.IsKnown(material))
            {
                material = HeldMaterials.Metal;
            }

            var c32 = (Color32)color;
            var key = (((c32.r << 16) | (c32.g << 8) | c32.b), glow, material);
            if (Materials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            if (_shader == null)
            {
                // A project shader (always in the build): the primitives' default material is stripped from player builds.
                _shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            var mat = new Material(_shader) { name = "Held_" + material, color = ShaderColor.Srgb(color) };
            if (glow && mat.HasProperty("_Floor"))
            {
                mat.SetFloat("_Floor", 1f);
            }

            mat.mainTexture = TextureOf(material);
            Materials[key] = mat;
            return mat;
        }

        /// <summary>The unit cube for a part <paramref name="size"/> metres big, its UVs in tiles of
        /// <see cref="HeldMaterialRules.TileMetres"/> (shared per size bucket).</summary>
        public static Mesh CubeMesh(System.Numerics.Vector3 size)
        {
            var bucket = HeldMaterialRules.UvBucket(size);
            if (Meshes.TryGetValue(bucket, out var mesh) && mesh != null)
            {
                return mesh;
            }

            var cube = HeldMaterialRules.BuildCube(bucket.X, bucket.Y, bucket.Z);
            var verts = new Vector3[cube.Positions.Length];
            var normals = new Vector3[cube.Normals.Length];
            var uvs = new Vector2[cube.Uvs.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = new Vector3(cube.Positions[i].X, cube.Positions[i].Y, cube.Positions[i].Z);
                normals[i] = new Vector3(cube.Normals[i].X, cube.Normals[i].Y, cube.Normals[i].Z);
                uvs[i] = new Vector2(cube.Uvs[i].X, cube.Uvs[i].Y);
            }

            mesh = new Mesh { name = "HeldPart" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = cube.Triangles;
            mesh.RecalculateBounds();
            Meshes[bucket] = mesh;
            return mesh;
        }

        // The material's texture, read once (and again when a layer changes it); null while no layer has the tile.
        private static Texture2D TextureOf(string material)
        {
            if (Loaded.Contains(material))
            {
                return Textures.TryGetValue(material, out var tex) ? tex : null;
            }

            Loaded.Add(material);
            byte[] raw = HeldMaterialRules.LiftTile(GameTextures.TileBytes(HeldMaterials.TextureKey(material)));
            if (raw == null || raw.Length != TextureTiles.BytesPerFrame)
            {
                return null;
            }

            var texture = new Texture2D(TextureTiles.Size, TextureTiles.Size, TextureFormat.RGBA32, false)
            {
                name = HeldMaterials.TextureKey(material),
                wrapMode = TextureWrapMode.Repeat, // the UVs run in tiles
                filterMode = FilterMode.Point,     // crisp texels, like every block tile
            };
            texture.LoadRawTextureData(raw);
            texture.Apply();
            Textures[material] = texture;
            return texture;
        }

        private static void OnTexturesChanged(IReadOnlyCollection<string> keys)
        {
            foreach (string material in HeldMaterials.All)
            {
                if (!Loaded.Contains(material) || (keys != null && keys.Count > 0 && !Contains(keys, HeldMaterials.TextureKey(material))))
                {
                    continue;
                }

                if (Textures.TryGetValue(material, out var old))
                {
                    BlockCubeFaces.DestroyAsset(old);
                    Textures.Remove(material);
                }

                Loaded.Remove(material);
                var texture = TextureOf(material);
                foreach (var kv in Materials)
                {
                    if (kv.Key.Material == material && kv.Value != null)
                    {
                        kv.Value.mainTexture = texture;
                    }
                }
            }
        }

        private static bool Contains(IReadOnlyCollection<string> keys, string key)
        {
            foreach (string k in keys)
            {
                if (k == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Drops the shared materials, textures and part meshes (session teardown, with
        /// <see cref="HeldItem.ReleasePartMaterials"/>).</summary>
        public static void Release()
        {
            foreach (var material in Materials.Values)
            {
                BlockCubeFaces.DestroyAsset(material);
            }

            Materials.Clear();
            foreach (var texture in Textures.Values)
            {
                BlockCubeFaces.DestroyAsset(texture);
            }

            Textures.Clear();
            Loaded.Clear();
            foreach (var mesh in Meshes.Values)
            {
                BlockCubeFaces.DestroyAsset(mesh);
            }

            Meshes.Clear();
        }
    }
}
