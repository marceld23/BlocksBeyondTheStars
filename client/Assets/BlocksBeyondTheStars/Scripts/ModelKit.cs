// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The code-built model toolkit of the flight view and its relatives (the menu backdrop, the ship paint preview,
    /// the ship-station decor, the planet enemies and the cutscene vista): collider-free cubes, the unlit, lit, textured
    /// and sun-lit-phase materials, and the bundled 64×64 block tiles. These used to be private to
    /// <see cref="SpaceView"/> and were copied word for word into each of those views (#2935); the
    /// <see cref="SpaceView"/> partials and the backdrop import them with <c>using static</c>, so the call sites read as
    /// before. Every call returns a NEW Material / Texture2D — the caller owns it. A model that comes and goes (a space
    /// entity, another pilot's placeholder hull) is built between <see cref="BeginOwned"/> and <see cref="EndOwned"/>, which
    /// ties what the kit made for it to the model, so they are freed with its last part (#2938). Near-clones that differ on
    /// purpose (StationDecorView's unnamed cube, WorldEntities' parent-first cube, PlayerAvatar's tile loader, the creature
    /// builder) keep their own versions.
    /// </summary>
    internal static class ModelKit
    {
        private static Shader _unlitShader;
        private static readonly int PhaseSunDirId = Shader.PropertyToID("_PhaseSunDir");

        // #2938: what the kit made since the innermost BeginOwned (null outside an owned build).
        private static List<Object> _owning;

        /// <summary>The token <see cref="BeginOwned"/> hands out and <see cref="EndOwned"/> takes back.</summary>
        internal readonly struct OwnedScope
        {
            internal readonly List<Object> Outer;

            internal OwnedScope(List<Object> outer) => Outer = outer;
        }

        /// <summary>#2938: from here until <see cref="EndOwned"/>, every Material and block tile the kit makes is recorded
        /// for the model being built. Scopes nest; pair every call with EndOwned in a <c>finally</c>. Never cache a kit
        /// material made inside the scope in a field (a <c>??=</c> cache): it dies with this model.</summary>
        internal static OwnedScope BeginOwned()
        {
            var scope = new OwnedScope(_owning);
            _owning = new List<Object>();
            return scope;
        }

        /// <summary>#2938: closes the scope <paramref name="scope"/> opened and ties what the kit made in it to
        /// <paramref name="model"/> (<see cref="ModelAssetLease"/>): the materials and tiles are destroyed once the last
        /// part that wears them is gone. Without a model (the build failed) nothing is tied.</summary>
        internal static void EndOwned(OwnedScope scope, GameObject model)
        {
            var made = _owning;
            _owning = scope.Outer;
            if (model != null && made != null && made.Count > 0)
            {
                ModelAssetLease.Tie(model, made);
            }
        }

        /// <summary>Records <paramref name="asset"/> for the model being built, if any (#2938).</summary>
        private static T Own<T>(T asset) where T : Object
        {
            _owning?.Add(asset);
            return asset;
        }

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
            return Own(new Material(_unlitShader) { color = ShaderColor.Srgb(c) });
        }

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        internal static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = Own(new Material(shader) { color = ShaderColor.Srgb(c) });
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

            var m = Own(new Material(shader) { color = ShaderColor.Srgb(c) });
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

            var tex = Own(new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            });
            tex.LoadRawTextureData(raw);
            tex.Apply();
            return tex;
        }
    }

    /// <summary>
    /// #2938: one part's hold on the Materials and block tiles <see cref="ModelKit"/> made for its model. Unity never
    /// garbage-collects a code-made Material or Texture2D and a despawn only destroys GameObjects, so every space entity
    /// that came and went used to leave its materials and 64×64 tiles behind until the return-to-menu sweep. Every renderer
    /// of the model that wears one of them gets a lease; the leases of one model share the set, and the LAST one to go
    /// destroys it — not the model root, because a part can outlive it (<c>SpaceFx.FlingParts</c> sends the cubes of a
    /// destroyed hostile tumbling on) and must keep its look until it is gone too. Like <see cref="OwnedProceduralMesh"/>,
    /// it relies on OnDestroy, which Unity calls only for a GameObject that was active once — the models are built under
    /// the live scene root.
    /// </summary>
    public sealed class ModelAssetLease : MonoBehaviour
    {
        private sealed class AssetSet
        {
            internal readonly List<Object> Assets;
            internal int Holders;

            internal AssetSet(List<Object> assets) => Assets = assets;
        }

        private AssetSet _set;

        /// <summary>Hands a lease on <paramref name="assets"/> to every renderer under <paramref name="model"/> that wears
        /// one of them; when none does, the model root holds the set.</summary>
        internal static void Tie(GameObject model, List<Object> assets)
        {
            var set = new AssetSet(assets);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (WearsAny(r, assets))
                {
                    Hold(r.gameObject, set);
                }
            }

            if (set.Holders == 0)
            {
                Hold(model, set);
            }
        }

        private static bool WearsAny(Renderer r, List<Object> assets)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m != null && assets.Contains(m))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Hold(GameObject go, AssetSet set)
        {
            go.AddComponent<ModelAssetLease>()._set = set;
            set.Holders++;
        }

        private void OnDestroy()
        {
            var set = _set;
            _set = null;
            if (set == null || --set.Holders > 0)
            {
                return;
            }

            foreach (var asset in set.Assets)
            {
                if (asset != null)
                {
                    Destroy(asset);
                }
            }
        }
    }
}
