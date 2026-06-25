// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a small blocky "held item" mesh (the tool/weapon/block currently selected) from cubes in
    /// code, shared by the third-person avatar hand (<see cref="PlayerAvatar.SetHeldItem"/>) and the
    /// first-person <see cref="Viewmodel"/>. The mesh points along +Z (forward) from its holder origin.
    /// </summary>
    public static class HeldItem
    {
        public enum Kind { None, Block, Drill, Gun, Blade, Scanner, Tool, Gadget }

        /// <summary>Resolves a block key to its atlas texture + tile UV rect, so a held block shows its REAL
        /// in-world texture instead of a flat map colour. Wired by GameBootstrap once the atlas exists; null
        /// (or a null result) falls back to the tinted cube.</summary>
        public static System.Func<string, (Texture2D Tex, Rect Uv)?> BlockTileResolver;

        /// <summary>Maps the selected inventory item to a held-item kind + tint (+ the block key for
        /// <see cref="Kind.Block"/>, so the held cube can carry the block's real atlas tile).</summary>
        public static (Kind kind, Color tint, string blockKey) For(GameContent content, string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey) || content == null)
            {
                return (Kind.None, Color.white, null);
            }

            var def = content.GetItem(itemKey);
            if (def == null)
            {
                return (Kind.None, Color.white, null);
            }

            if (!string.IsNullOrEmpty(def.PlacesBlock))
            {
                return (Kind.Block, WorldMap.MapColor(def.PlacesBlock), def.PlacesBlock);
            }

            var tool = def.Tool;
            if (tool == null)
            {
                return (Kind.None, Color.white, null); // raw material — nothing meaningful to hold up
            }

            switch (tool.Kind)
            {
                case ToolKind.Drill: return (Kind.Drill, new Color(0.62f, 0.66f, 0.72f), null);
                case ToolKind.Scanner: return (Kind.Scanner, new Color(0.45f, 0.85f, 0.95f), null);
                case ToolKind.Weapon: return IsRanged(itemKey) ? (Kind.Gun, GunTint(itemKey), null) : (Kind.Blade, new Color(0.80f, 0.84f, 0.90f), null);
                case ToolKind.Gadget: return (Kind.Gadget, GadgetTint(itemKey), null);
                default: return (Kind.Tool, new Color(0.60f, 0.62f, 0.66f), null);
            }
        }

        /// <summary>The emitter glow colour for a gadget's held model (item 36).</summary>
        private static Color GadgetTint(string key)
        {
            if (key.Contains("medkit")) return new Color(0.35f, 1f, 0.55f);   // green first-aid
            if (key.Contains("stasis")) return new Color(0.4f, 0.8f, 1f);     // cyan stasis
            if (key.Contains("blaster")) return new Color(1f, 0.55f, 0.25f);  // orange blast
            return new Color(0.6f, 0.85f, 0.9f);
        }

        private static bool IsRanged(string key)
            => key.Contains("pistol") || key.Contains("blaster") || key.Contains("gauss")
               || key.Contains("laser") || key.Contains("plasma") || key.Contains("cannon") || key.Contains("gun");

        private static Color GunTint(string key)
        {
            if (key.Contains("plasma")) return new Color(0.85f, 0.5f, 1f);
            if (key.Contains("laser")) return new Color(1f, 0.5f, 0.45f);
            if (key.Contains("gauss")) return new Color(0.55f, 0.9f, 1f);
            return new Color(0.5f, 0.54f, 0.6f);
        }

        /// <summary>Builds the held-item geometry under a new holder parented to <paramref name="parent"/>.
        /// For blocks, <paramref name="blockKey"/> lets the cube carry its REAL atlas tile (textured hand
        /// block instead of a flat colour); without a resolver/tile it falls back to the tint.</summary>
        public static GameObject Build(Transform parent, Kind kind, Color tint, string blockKey = null)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            var holder = new GameObject("Held");
            holder.transform.SetParent(parent, false);

            var dark = new Color(0.20f, 0.22f, 0.26f);
            var metal = new Color(0.55f, 0.58f, 0.64f);

            switch (kind)
            {
                case Kind.Block:
                    var blockGo = Cube(holder.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.22f, 0.22f, 0.22f), tint);
                    if (blockKey != null && BlockTileResolver?.Invoke(blockKey) is { } tile)
                    {
                        // The block's real atlas tile: LitColor samples _MainTex with its ST transform in both
                        // pipelines, so scale/offset map the cube's 0..1 face UVs onto the tile.
                        var m = blockGo.GetComponent<Renderer>().sharedMaterial;
                        m.color = Color.white;
                        m.mainTexture = tile.Tex;
                        m.mainTextureScale = new Vector2(tile.Uv.width, tile.Uv.height);
                        m.mainTextureOffset = new Vector2(tile.Uv.x, tile.Uv.y);
                    }

                    break;

                case Kind.Drill:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.16f, 0.26f), metal);
                    Cube(holder.transform, new Vector3(0f, 0f, 0.24f), new Vector3(0.09f, 0.09f, 0.18f), tint);       // bit
                    Cube(holder.transform, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.07f, 0.16f, 0.08f), dark);  // grip
                    break;

                case Kind.Gun:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.10f), new Vector3(0.09f, 0.10f, 0.34f), dark);       // barrel
                    Cube(holder.transform, new Vector3(0f, 0f, 0.30f), new Vector3(0.05f, 0.05f, 0.08f), tint);      // muzzle glow
                    Cube(holder.transform, new Vector3(0f, -0.13f, -0.06f), new Vector3(0.08f, 0.18f, 0.10f), dark); // grip
                    break;

                case Kind.Blade:
                    Cube(holder.transform, new Vector3(0f, -0.04f, 0.0f), new Vector3(0.06f, 0.06f, 0.16f), dark);    // handle
                    Cube(holder.transform, new Vector3(0f, 0.02f, 0.26f), new Vector3(0.03f, 0.18f, 0.34f), tint);   // blade
                    break;

                case Kind.Scanner:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.16f, 0.12f, 0.18f), metal);     // body
                    Cube(holder.transform, new Vector3(0f, 0.10f, 0.12f), new Vector3(0.03f, 0.10f, 0.03f), dark);   // antenna
                    Cube(holder.transform, new Vector3(0f, 0.16f, 0.12f), new Vector3(0.06f, 0.06f, 0.06f), tint);   // glowing tip
                    break;

                case Kind.Gadget:
                    // A compact handheld emitter: a boxy body, a short barrel, and a glowing emitter tip in the
                    // gadget's tint (green medkit / cyan stasis / orange blaster) — item 36.
                    Cube(holder.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.13f, 0.20f), metal);     // body
                    Cube(holder.transform, new Vector3(0f, 0f, 0.18f), new Vector3(0.08f, 0.08f, 0.12f), dark);     // barrel
                    Cube(holder.transform, new Vector3(0f, 0f, 0.27f), new Vector3(0.10f, 0.10f, 0.05f), tint);     // emitter glow
                    Cube(holder.transform, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.07f, 0.16f, 0.09f), dark); // grip
                    break;

                default: // Tool
                    Cube(holder.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.12f, 0.12f, 0.26f), tint);
                    break;
            }

            return holder;
        }

        private static GameObject Cube(Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            go.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = ShaderColor.Srgb(color) };
            return go;
        }
    }
}
