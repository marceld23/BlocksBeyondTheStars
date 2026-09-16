// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BodyPaint = BlocksBeyondTheStars.Shared.State.BodyPaint;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a small blocky "held item" mesh (the tool/weapon/block currently selected) from cubes in
    /// code, shared by the third-person avatar hand (<see cref="PlayerAvatar.SetHeldItem"/>) and the
    /// first-person <see cref="Viewmodel"/>. The mesh points along +Z (forward) from its holder origin.
    /// </summary>
    public static class HeldItem
    {
        public enum Kind { None, Block, Drill, Gun, Blade, Scanner, Tool, Gadget, Hand, Hoe, Hammer }

        /// <summary>What a working NPC carries (#1869), from the server's <c>NetNpc.Held</c> hint — not an item:
        /// the gardener's hoe, the craftsman's hammer, the guard's blade.</summary>
        public static (Kind kind, Color tint) ForNpc(string held) => held switch
        {
            "npc_hoe" => (Kind.Hoe, new Color(0.62f, 0.64f, 0.68f)),
            "npc_hammer" => (Kind.Hammer, new Color(0.50f, 0.52f, 0.56f)),
            "blade" => (Kind.Blade, new Color(0.80f, 0.84f, 0.90f)),
            // 2026-09 professions: re-tinted existing models (no new meshes).
            "npc_medkit" => (Kind.Gadget, new Color(0.35f, 1f, 0.55f)),     // the doctor's green first-aid emitter
            "npc_basket" => (Kind.Tool, new Color(0.72f, 0.52f, 0.28f)),    // the grocer's wicker basket
            "npc_book" => (Kind.Tool, new Color(0.45f, 0.30f, 0.62f)),      // the sage's tome
            "npc_leash" => (Kind.Tool, new Color(0.50f, 0.34f, 0.20f)),     // the tamer's leather leash
            "npc_pickaxe" => (Kind.Hammer, new Color(0.55f, 0.57f, 0.60f)), // the blockfarmer's pick
            "npc_camera" => (Kind.Gadget, new Color(0.95f, 0.45f, 0.85f)),  // the streamer's camera
            "npc_microphone" => (Kind.Scanner, new Color(0.45f, 0.65f, 1f)), // the reporter's microphone
            _ => (Kind.None, Color.white),
        };

        /// <summary>Resolves a block key to its atlas texture + tile UV rect, so a held block shows its REAL
        /// in-world texture instead of a flat map colour. Wired by GameBootstrap once the atlas exists; null
        /// (or a null result) falls back to the tinted cube.</summary>
        public static System.Func<string, (Texture2D Tex, Rect Uv)?> BlockTileResolver;

        /// <summary>Resolves the local player's suit arm colour so the empty-slot hand matches the
        /// avatar's glove. Wired by GameBootstrap; null falls back to the default suit blue.</summary>
        public static System.Func<Color?> HandTintResolver;

        /// <summary>Resolves the local player's arm painting (the <c>BodyPaint</c> wire payload from the
        /// appearance editor) so the empty-slot hand shows the player's own design instead of only the
        /// flat suit colour (#1427). Wired by GameBootstrap; null/empty keeps the flat tint.</summary>
        public static System.Func<string> HandPaintResolver;

        /// <summary>Default glove colour when no resolver is wired (= <c>ClientSettings.ArmColor</c> default).</summary>
        private static readonly Color DefaultGlove = new Color(0.20f, 0.45f, 0.80f);

        /// <summary>Maps the selected inventory item to a held-item kind + tint (+ the block key for
        /// <see cref="Kind.Block"/>, so the held cube can carry the block's real atlas tile).</summary>
        public static (Kind kind, Color tint, string blockKey) For(GameContent content, string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey))
            {
                // Empty hotbar slot: show the bare (gloved) hand instead of nothing (#1033).
                return (Kind.Hand, HandTintResolver?.Invoke() ?? DefaultGlove, null);
            }

            if (content == null)
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
        /// block instead of a flat colour); without a resolver/tile it falls back to the tint.
        /// <paramref name="itemKey"/> picks the item's own look for drills, guns, blades and scanners (#1931).</summary>
        public static GameObject Build(Transform parent, Kind kind, Color tint, string blockKey = null, string itemKey = null)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            var holder = new GameObject("Held");
            holder.transform.SetParent(parent, false);

            // #1931: every drill, gun, blade and scanner has its own parts (the base item keeps the model its kind had).
            var shaped = HeldItemShapes.Parts(kind.ToString(), itemKey, new HeldItemShapes.Rgb(tint.r, tint.g, tint.b));
            if (shaped != null)
            {
                foreach (var part in shaped)
                {
                    Cube(holder.transform, new Vector3(part.Position.X, part.Position.Y, part.Position.Z),
                        new Vector3(part.Size.X, part.Size.Y, part.Size.Z), new Color(part.Color.R, part.Color.G, part.Color.B));
                }

                return holder;
            }

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

                case Kind.Gadget:
                    // A compact handheld emitter: a boxy body, a short barrel, and a glowing emitter tip in the
                    // gadget's tint (green medkit / cyan stasis / orange blaster) — item 36.
                    Cube(holder.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.13f, 0.20f), metal);     // body
                    Cube(holder.transform, new Vector3(0f, 0f, 0.18f), new Vector3(0.08f, 0.08f, 0.12f), dark);     // barrel
                    Cube(holder.transform, new Vector3(0f, 0f, 0.27f), new Vector3(0.10f, 0.10f, 0.05f), tint);     // emitter glow
                    Cube(holder.transform, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.07f, 0.16f, 0.09f), dark); // grip
                    break;

                case Kind.Hand:
                    // Empty slot (#1033): a gloved fist on the suit-coloured forearm — thumb on the inner
                    // side of a right hand, cuff a darker shade for definition.
                    var cuff = new Color(tint.r * 0.7f, tint.g * 0.7f, tint.b * 0.7f);
                    var forearm = Cube(holder.transform, new Vector3(0.02f, -0.06f, -0.16f), new Vector3(0.11f, 0.11f, 0.30f), tint);
                    var cuffGo = Cube(holder.transform, new Vector3(0f, -0.02f, -0.02f), new Vector3(0.12f, 0.12f, 0.07f), cuff);
                    var fist = Cube(holder.transform, new Vector3(0f, 0f, 0.07f), new Vector3(0.13f, 0.12f, 0.14f), tint);
                    var thumb = Cube(holder.transform, new Vector3(-0.075f, 0.01f, 0.05f), new Vector3(0.05f, 0.06f, 0.08f), tint);
                    PaintHand(tint, forearm, fist, thumb); // the player's own arm painting, when there is one (#1427)
                    // The hand is suit-coloured like the avatar, so it shares the avatar's failure mode:
                    // LitColor's fixed key light + Linear colour space sink dark tints to a black silhouette
                    // without the ambient lift PlayerAvatar.Lit applies (#1427). Cuff included.
                    LiftAmbient(forearm, cuffGo, fist, thumb);
                    break;

                case Kind.Hoe:
                    // #1869: a long wooden shaft with a flat iron blade bent down at its far end.
                    var wood = new Color(0.45f, 0.30f, 0.16f);
                    Cube(holder.transform, new Vector3(0f, 0f, 0.20f), new Vector3(0.045f, 0.045f, 0.70f), wood);   // shaft
                    Cube(holder.transform, new Vector3(0f, -0.07f, 0.54f), new Vector3(0.16f, 0.14f, 0.03f), tint); // blade
                    break;

                case Kind.Hammer:
                    // #1869: a short handle with a heavy head across its end.
                    Cube(holder.transform, new Vector3(0f, 0f, 0.12f), new Vector3(0.05f, 0.05f, 0.34f), new Color(0.40f, 0.27f, 0.15f)); // handle
                    Cube(holder.transform, new Vector3(0f, 0f, 0.30f), new Vector3(0.09f, 0.18f, 0.09f), tint);                             // head
                    break;

                default: // Tool
                    Cube(holder.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.12f, 0.12f, 0.26f), tint);
                    break;
            }

            return holder;
        }

        // Cached hand-paint atlas: the hand rebuilds on every hotbar change, and baking a texture each
        // time would leak one per switch. Keyed by payload + base colour; the stale one is destroyed.
        private static Texture2D _handAtlas;
        private static string _handAtlasKey;

        /// <summary>Frees the cached hand-paint atlas (world teardown, #1464) — a static Texture2D is never
        /// swept by <c>Resources.UnloadUnusedAssets</c> while this class still references it.</summary>
        public static void ReleaseHandAtlas()
        {
            if (_handAtlas != null)
            {
                Object.Destroy(_handAtlas);
            }

            _handAtlas = null;
            _handAtlasKey = null;
        }

        /// <summary>Applies the player's arm painting to the hand parts (white tint + the atlas the avatar
        /// also bakes, mapped to the right arm's OUTER chunk — the hero face of the paint editor). Without
        /// a valid painting the parts keep their flat suit tint, matching the pre-#1427 look.</summary>
        private static void PaintHand(Color baseColor, params GameObject[] parts)
        {
            string pixels = HandPaintResolver?.Invoke();
            if (string.IsNullOrEmpty(pixels) || pixels.Length != BodyPaint.ExpectedLength(BodyPaint.Arms))
            {
                return;
            }

            string key = pixels + "#" + ColorUtility.ToHtmlStringRGB(baseColor);
            if (_handAtlasKey != key || _handAtlas == null)
            {
                if (_handAtlas != null)
                {
                    Object.Destroy(_handAtlas);
                }

                _handAtlas = BodyPaintKit.BuildAtlas(BodyPaint.Arms, pixels, baseColor);
                _handAtlasKey = key;
            }

            // Right limb = canvas row 1 → chunks 4..7 (front|outer|back|inner); outer is chunk 5. Cube
            // face UVs are 0..1, so the material ST maps every face onto that chunk (same trick as the
            // held block's atlas tile above). Transparent pixels are pre-composited onto the base colour.
            var uv = BodyPaintKit.ChunkRect(BodyPaint.Arms, 5);
            foreach (var go in parts)
            {
                var m = go.GetComponent<Renderer>().sharedMaterial;
                m.color = Color.white; // the painting's true colours, like the avatar's painted parts
                m.mainTexture = _handAtlas;
                m.mainTextureScale = new Vector2(uv.width, uv.height);
                m.mainTextureOffset = new Vector2(uv.x, uv.y);
            }
        }

        /// <summary>Raises LitColor's ambient floor + fill to the avatar's values (see
        /// <c>PlayerAvatar.Lit</c>) so suit-coloured hand parts never sink to black (#1427).</summary>
        private static void LiftAmbient(params GameObject[] parts)
        {
            foreach (var go in parts)
            {
                var m = go.GetComponent<Renderer>().sharedMaterial;
                if (m.HasProperty("_Floor")) // no-op on the Unlit/Color fallback
                {
                    m.SetFloat("_Floor", 0.62f);
                    m.SetFloat("_Fill", 0.3f);
                }
            }
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
