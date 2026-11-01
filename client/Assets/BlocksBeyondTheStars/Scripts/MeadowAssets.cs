// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared GPU assets of the High preset's meadow (#3056): the shell-stack mesh and material of the grass carpet and
    /// the flower mesh, sprite and material. Built once on first use (the browser and the lower presets never touch them)
    /// and kept for the process, like the ground-scatter assets.
    /// </summary>
    internal static class MeadowAssets
    {
        /// <summary>Shells per carpet cell. GrassShellCommon.hlsl lifts layer k to (k + 1) / ShellLayers of the carpet height.</summary>
        public const int ShellLayers = 10;

        // The flower in object space (one unit tall; the instance matrix scales it to its height in metres).
        private const float StemTop = 0.85f;   // where the blossom sits
        private const float StemHalfWidth = 0.25f; // the stem quads (stem + leaves are painted into the sprite)
        private const float BlossomHalf = 0.2f;    // half the blossom disc's edge
        private const float BlossomTilt = 30f;     // degrees the disc tips over, so it also reads from the side

        private static Mesh _shellMesh;
        private static Mesh _flowerMesh;
        private static Material _shellMaterial;
        private static Material _flowerMaterial;
        private static Texture2D _flowerSprite;

        /// <summary>The carpet's shell stack: <see cref="ShellLayers"/> 1 × 1 quads over the cell, object y = layer 0..1.</summary>
        public static Mesh ShellMesh
        {
            get
            {
                if (_shellMesh == null)
                {
                    _shellMesh = BuildShellStack();
                }

                return _shellMesh;
            }
        }

        /// <summary>The flower: two crossed stem quads and a tilted blossom disc, rooted at 0, one unit tall.</summary>
        public static Mesh FlowerMesh
        {
            get
            {
                if (_flowerMesh == null)
                {
                    _flowerMesh = BuildFlower();
                }

                return _flowerMesh;
            }
        }

        /// <summary>The carpet's instanced material (null when the shader is missing from the build).</summary>
        public static Material ShellMaterial
        {
            get
            {
                if (_shellMaterial == null)
                {
                    var shader = Shader.Find("BlocksBeyondTheStars/GrassShell");
                    if (shader != null)
                    {
                        _shellMaterial = new Material(shader) { name = "GrassShell", enableInstancing = true };
                    }
                }

                return _shellMaterial;
            }
        }

        /// <summary>The flowers' instanced material with the painted sprite (null when the shader is missing).</summary>
        public static Material FlowerMaterial
        {
            get
            {
                if (_flowerMaterial == null)
                {
                    var shader = Shader.Find("BlocksBeyondTheStars/MeadowFlower");
                    if (shader != null)
                    {
                        _flowerMaterial = new Material(shader) { name = "MeadowFlower", enableInstancing = true };
                        _flowerMaterial.SetTexture("_FlowerTex", FlowerSpriteTexture());
                    }
                }

                return _flowerMaterial;
            }
        }

        /// <summary>The flower sprite (<see cref="FlowerSprite"/>): a linear DATA texture with mipmaps, so the masks are not
        /// sRGB-decoded and a far flower stays a smooth cut-out.</summary>
        private static Texture2D FlowerSpriteTexture()
        {
            if (_flowerSprite == null)
            {
                _flowerSprite = new Texture2D(FlowerSprite.Width, FlowerSprite.Height, TextureFormat.RGBA32, true, true)
                {
                    name = "MeadowFlowerSprite",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _flowerSprite.SetPixelData(FlowerSprite.Paint(), 0);
                _flowerSprite.Apply(true, true);
            }

            return _flowerSprite;
        }

        private static Mesh BuildShellStack()
        {
            var verts = new Vector3[ShellLayers * 4];
            var uvs = new Vector2[ShellLayers * 4];
            var tris = new int[ShellLayers * 6];
            for (int k = 0; k < ShellLayers; k++)
            {
                // The top layer comes first in the index buffer: drawn first, it hides the strands of the layers below.
                float y = (ShellLayers - k) / (float)ShellLayers;
                int v = k * 4;
                verts[v] = new Vector3(-0.5f, y, -0.5f);
                verts[v + 1] = new Vector3(-0.5f, y, 0.5f);
                verts[v + 2] = new Vector3(0.5f, y, 0.5f);
                verts[v + 3] = new Vector3(0.5f, y, -0.5f);
                uvs[v] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);
                Quad(tris, k * 6, v);
            }

            var mesh = new Mesh { name = "GrassShellStack", vertices = verts, uv = uvs, triangles = tris };
            // The shader lifts the layers to at most ~0.2 m and the wind leans them: bounds of what is really drawn.
            mesh.bounds = new Bounds(new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.5f, 1.6f));
            return mesh;
        }

        private static Mesh BuildFlower()
        {
            float h = StemHalfWidth, top = StemTop, s = BlossomHalf;
            float tilt = BlossomTilt * Mathf.Deg2Rad, sin = Mathf.Sin(tilt), cos = Mathf.Cos(tilt);
            var verts = new[]
            {
                // Stem cross (the right half of the sprite).
                new Vector3(-h, 0f, 0f), new Vector3(-h, top, 0f), new Vector3(h, top, 0f), new Vector3(h, 0f, 0f),
                new Vector3(0f, 0f, -h), new Vector3(0f, top, -h), new Vector3(0f, top, h), new Vector3(0f, 0f, h),
                // Blossom disc, tipped over around x (the left half).
                new Vector3(-s, top + (s * sin), -s * cos), new Vector3(-s, top - (s * sin), s * cos),
                new Vector3(s, top - (s * sin), s * cos), new Vector3(s, top + (s * sin), -s * cos),
            };
            var uvs = new[]
            {
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
            };
            var tris = new int[18];
            Quad(tris, 0, 0);
            Quad(tris, 6, 4);
            Quad(tris, 12, 8);
            var mesh = new Mesh { name = "MeadowFlower", vertices = verts, uv = uvs, triangles = tris };
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.4f, 1.4f, 1.4f)); // the wind leans the blossom
            return mesh;
        }

        private static void Quad(int[] tris, int at, int v)
        {
            tris[at] = v;
            tris[at + 1] = v + 1;
            tris[at + 2] = v + 2;
            tris[at + 3] = v;
            tris[at + 4] = v + 2;
            tris[at + 5] = v + 3;
        }
    }
}
