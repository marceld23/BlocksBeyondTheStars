// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The code-built model toolkit of the flight view and its relatives (the menu backdrop, the ship paint preview,
    /// the ship-station decor, the planet enemies and the cutscene vista): collider-free cubes, the unlit, lit, textured
    /// and sun-lit-phase materials, and the bundled 64×64 block tiles. These used to be private to
    /// <see cref="SpaceView"/> and were copied word for word into each of those views (#2935); the
    /// <see cref="SpaceView"/> partials and the backdrop import them with <c>using static</c>, so the call sites read as
    /// before. Every call returns a NEW Material / Texture2D — the caller owns it. Near-clones that differ on purpose
    /// (StationDecorView's unnamed cube, WorldEntities' parent-first cube, PlayerAvatar's tile loader, the creature
    /// builder) keep their own versions.
    /// </summary>
    internal static class ModelKit
    {
        private static Shader _unlitShader;
        private static readonly int PhaseSunDirId = Shader.PropertyToID("_PhaseSunDir");

        /// <summary>A named primitive cube under <paramref name="parent"/> (collider stripped) with the given local
        /// position, scale and shared material.</summary>
        internal static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Destroys the collider a primitive comes with (the models are render-only).</summary>
        internal static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }
        }

        /// <summary>Flat unlit colour material.</summary>
        internal static Material Unlit(Color c)
        {
            _unlitShader ??= Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque"); // #1514: no string lookup per part
            return new Material(_unlitShader) { color = ShaderColor.Srgb(c) };
        }

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        internal static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Lit material textured with a bundled block texture (white-ish tint so the texture reads).</summary>
        internal static Material LitTex(string texKey, Color tint, float tile = 2f) => Lit(tint, LoadTex(texKey), new Vector2(tile, tile));

        /// <summary>Sun-lit-phase material for a celestial body (the <c>SkyBodyPhase</c> shader), lit from the
        /// world-space direction to the star <paramref name="sunDir"/> so a terminator / crescent emerges. Falls back
        /// to the fixed-light Lit shader when the phase shader is unavailable. Same colour/texture/tiling handling as
        /// <see cref="Lit"/>. The orbit view, the menu backdrop and the cutscene director's space vista (#2477) light
        /// their bodies with it.</summary>
        internal static Material LitPhase(Color c, Vector3 sunDir, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/SkyBodyPhase");
            if (shader == null)
            {
                return Lit(c, tex, tiling);
            }

            var m = new Material(shader) { color = ShaderColor.Srgb(c) };
            m.SetVector(PhaseSunDirId, sunDir.sqrMagnitude > 1e-6f ? (Vector4)sunDir.normalized : new Vector4(0f, 0f, 1f, 0f));
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes raw 64x64 RGBA32,
        /// via LoadRawTextureData from the core module — no ImageConversion dependency).</summary>
        internal static Texture2D LoadTex(string key)
        {
            // The winning layer of the texture source (#1952): world texture, local pack, or the bundled tile.
            byte[] raw = GameTextures.TileBytes(key);
            if (raw == null || raw.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(raw);
            tex.Apply();
            return tex;
        }
    }
}
