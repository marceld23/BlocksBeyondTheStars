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
        /// <summary>The look of the held item. <see cref="Gloves"/> (#2278) is worn on BOTH hands: the first-person view
        /// builds the right glove with <see cref="Build"/> and the left one with <c>left: true</c> (the mirror image), the
        /// avatar draws one on each hand. Every other kind stays one-handed. <see cref="Vehicle"/>, <see cref="Plant"/> and
        /// <see cref="Icon"/> (#2534) are the held miniatures of <see cref="HeldMiniatures"/>: the vehicle's hull, the plant's
        /// in-world look, and the extruded icon of any other item without a model. <see cref="Axe"/> and <see cref="Bow"/>
        /// (#2647) are the wilderness folk's NPC tools (<see cref="HeldItemShapes.NpcToolParts"/>).</summary>
        public enum Kind { None, Block, Drill, Gun, Blade, Scanner, Tool, Gadget, Hand, Hoe, Hammer, Gloves, Vehicle, Plant, Icon, Axe, Bow }

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
            // #2647: the wilderness folk — the gatherer keeps the basket and the miner the pick; new models for these two.
            "npc_axe" => (Kind.Axe, new Color(0.62f, 0.64f, 0.68f)),        // the woodcutter's axe (iron head)
            "npc_bow" => (Kind.Bow, new Color(0.58f, 0.38f, 0.19f)),        // the hunter's wooden bow
            _ => (Kind.None, Color.white),
        };

        /// <summary>Resolves a block key to its atlas texture + tile UV rect, so a held block shows its REAL
        /// in-world texture instead of a flat map colour. Wired by GameBootstrap once the atlas exists; null
        /// (or a null result) falls back to the tinted cube.</summary>
        public static System.Func<string, (Texture2D Tex, Rect Uv)?> BlockTileResolver;

        /// <summary>Resolves an item key to its held model from the item data (#1962,
        /// <c>ItemDefinition.HeldModel</c>); null = the model of the item's kind. Wired by GameBootstrap.</summary>
        public static System.Func<string, System.Collections.Generic.IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart>> ModelResolver;

        /// <summary>The world's content, block atlas and chunk materials, which the held vehicle and plant miniatures are
        /// meshed with (#2534). Wired by GameBootstrap; null (or a missing atlas) falls back to the gadget model / the
        /// block cube.</summary>
        public static System.Func<(GameContent Content, BlockTextureAtlas Atlas, Material Opaque, Material Transparent)> MeshKitResolver;

        /// <summary>The paint-design atlas and its chunk material, so a held painted block shows its design (#2776). Wired by
        /// GameBootstrap; null (or a null atlas) shows the block unpainted.</summary>
        public static System.Func<(PaintDesignAtlas Atlas, Material Material)> PaintKitResolver;

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

            // #2534: everything is held as a real miniature — a vehicle as its hull (it used to be the generic gadget
            // emitter), a seed, sapling or torch as its plant / prop (it used to be an opaque cube printed with the cross
            // tile), and every item without a block, a tool or a vehicle as its extruded icon (it used to be nothing).
            var placed = string.IsNullOrEmpty(def.PlacesBlock) ? null : content.GetBlock(def.PlacesBlock);
            switch (HeldItemShapes.MiniatureFor(def, placed))
            {
                case HeldItemShapes.Miniature.Vehicle:
                    return (Kind.Vehicle, GadgetTint(itemKey), null); // the tint is the gadget fallback's, without an atlas
                case HeldItemShapes.Miniature.Plant:
                    return (Kind.Plant, WorldMap.MapColor(def.PlacesBlock), def.PlacesBlock);
                case HeldItemShapes.Miniature.Icon:
                    // Only an item without any icon is left with the bare hand.
                    return IconResolver.ItemTexture(itemKey) != null
                        ? (Kind.Icon, Color.white, null)
                        : (Kind.Hand, HandTintResolver?.Invoke() ?? DefaultGlove, null);
            }

            if (!string.IsNullOrEmpty(def.PlacesBlock))
            {
                return (Kind.Block, WorldMap.MapColor(def.PlacesBlock), def.PlacesBlock);
            }

            // #2278: a pair of gloves is worn on both hands — read from the data (heldGrip), never guessed from the key, and
            // before the weapon branch, which would hold it like a blade. The tint is the suit's, for the forearms.
            if (HeldGrips.IsGloves(def))
            {
                return (Kind.Gloves, HandTintResolver?.Invoke() ?? DefaultGlove, null);
            }

            var tool = def.Tool;
            if (tool == null)
            {
                return (Kind.None, Color.white, null); // unreachable since #2534 (a tool-less item is held as its icon)
            }

            switch (tool.Kind)
            {
                case ToolKind.Drill: return (Kind.Drill, new Color(0.62f, 0.66f, 0.72f), null);
                case ToolKind.Scanner: return (Kind.Scanner, new Color(0.45f, 0.85f, 0.95f), null);
                // #2800: a gun or a blade by the weapon's range, the server's rule — the key used to decide, and the plasma
                // sword ("plasma") was held like a gun.
                case ToolKind.Weapon: return MeleeRules.IsRanged(tool) ? (Kind.Gun, GunTint(itemKey), null) : (Kind.Blade, new Color(0.80f, 0.84f, 0.90f), null);
                case ToolKind.Gadget:
                    // #2320: a gadget named a gun (the energy rope gun) is held and drawn like one — its own heldModel parts
                    // and the recoil pose — where every other gadget keeps the compact emitter.
                    return itemKey.EndsWith("_gun", System.StringComparison.Ordinal)
                        ? (Kind.Gun, GadgetTint(itemKey), null)
                        : (Kind.Gadget, GadgetTint(itemKey), null);
                default: return (Kind.Tool, new Color(0.60f, 0.62f, 0.66f), null);
            }
        }

        /// <summary>The emitter glow colour for a gadget's held model (item 36).</summary>
        private static Color GadgetTint(string key)
        {
            if (key.Contains("rope")) return new Color(0.37f, 0.95f, 1f);     // cyan energy rope (#2317)
            if (key.Contains("medkit")) return new Color(0.35f, 1f, 0.55f);   // green first-aid
            if (key.Contains("stasis")) return new Color(0.4f, 0.8f, 1f);     // cyan stasis
            if (key.Contains("blaster")) return new Color(1f, 0.55f, 0.25f);  // orange blast
            return new Color(0.6f, 0.85f, 0.9f);
        }

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
        /// <paramref name="itemKey"/> picks the item's own look for drills, guns, blades and scanners (#1931) — from the
        /// item data (#1962). <paramref name="look"/> is a PLAYER's own look for this tool (#1963) and wins over both.
        /// <paramref name="left"/> builds the LEFT hand's mirror image — only the two-handed kinds (#2278 gloves, the bare
        /// hand of the #2287 climb) have one; every other kind ignores it.</summary>
        public static GameObject Build(Transform parent, Kind kind, Color tint, string blockKey = null, string itemKey = null,
            System.Collections.Generic.IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart> look = null,
            bool left = false)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            var holder = new GameObject(left ? "HeldLeft" : "Held");
            holder.transform.SetParent(parent, false);

            // #2534: the held miniatures. Without the world's atlas a vehicle falls back to the gadget emitter and a plant
            // to the textured block cube; an item without an icon never gets here (For hands it the bare hand).
            switch (kind)
            {
                case Kind.Vehicle:
                    if (HeldMiniatures.AttachVehicle(holder.transform, itemKey))
                    {
                        return holder;
                    }

                    kind = Kind.Gadget;
                    break;
                case Kind.Plant:
                    if (HeldMiniatures.AttachPlant(holder.transform, blockKey))
                    {
                        return holder;
                    }

                    kind = Kind.Block;
                    break;
                case Kind.Icon:
                    HeldMiniatures.AttachIcon(holder.transform, itemKey);
                    return holder;
                case Kind.Block:
                    // #2776: a dyed, glowing, shaped or painted block — or a prop with its stamped form — as the world
                    // draws it; a plain block keeps the cube below.
                    if (HeldMiniatures.AttachBlock(holder.transform, blockKey, itemKey))
                    {
                        return holder;
                    }

                    break;
            }

            // #1931: every drill, gun, blade and scanner has its own parts (the base item keeps the model its kind had).
            var model = look != null && look.Count > 0 ? look : (itemKey != null ? ModelResolver?.Invoke(BlocksBeyondTheStars.Shared.State.ItemKey.Base(itemKey)) : null);
            if (kind == Kind.Gloves)
            {
                BuildGlove(holder.transform, tint, model, left);
                return holder;
            }

            // #2647: the NPC axe and bow are part models too (Client.Core pins them), never replaced by an item look.
            var shaped = HeldItemShapes.Parts(kind.ToString(), new HeldItemShapes.Rgb(tint.r, tint.g, tint.b), model)
                ?? HeldItemShapes.NpcToolParts(kind.ToString(), new HeldItemShapes.Rgb(tint.r, tint.g, tint.b));
            if (shaped != null)
            {
                foreach (var part in shaped)
                {
                    Cube(holder.transform, new Vector3(part.Position.X, part.Position.Y, part.Position.Z),
                        new Vector3(part.Size.X, part.Size.Y, part.Size.Z), new Color(part.Color.R, part.Color.G, part.Color.B), part.Glow);
                }

                return holder;
            }

            var dark = new Color(0.20f, 0.22f, 0.26f);
            var metal = new Color(0.55f, 0.58f, 0.64f);

            switch (kind)
            {
                case Kind.Block:
                    var blockGo = Cube(holder.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.22f, 0.22f, 0.22f), tint);
                    if (blockKey != null && !DressBlock(blockGo, blockKey) && BlockTileResolver?.Invoke(blockKey) is { } tile)
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
                    HandCubes(holder.transform, tint, left);
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

        // #2744: the held block wears the faces the world gives it — its own top, sides, bottom and picture front, the
        // front toward the eye. One cube mesh per block (shared by every hand that holds it) and one material per atlas.
        private static readonly System.Collections.Generic.Dictionary<ushort, Mesh> BlockMeshes = new System.Collections.Generic.Dictionary<ushort, Mesh>();
        private static readonly System.Collections.Generic.Dictionary<Texture2D, Material> BlockMaterials = new System.Collections.Generic.Dictionary<Texture2D, Material>();
        private static BlockTextureAtlas _blockMeshAtlas;

        /// <summary>
        /// Puts the block's own cube (<see cref="BlockCubeFaces"/>) on the held cube <paramref name="blockGo"/>; false when
        /// the world's content or atlas is not available (the caller falls back to the one-tile cube). The holder points
        /// along +Z, so the eye — first person and the third-person camera behind the avatar — sees the cube's −Z face:
        /// a block with a front shows it there, upright. (The Unity primitive's −Z face maps its texture turned by 180°,
        /// which is why a picture block used to stand on its head in the hand.) Lit like before: the LitColor material,
        /// white, over the atlas.
        /// </summary>
        private static bool DressBlock(GameObject blockGo, string blockKey)
        {
            var kit = MeshKitResolver?.Invoke();
            if (kit == null)
            {
                return false;
            }

            var (content, atlas, _, _) = kit.Value;
            var def = content?.GetBlock(blockKey);
            if (def == null || def.NumericId.Value == 0 || atlas == null || atlas.Texture == null)
            {
                return false;
            }

            if (!ReferenceEquals(atlas, _blockMeshAtlas))
            {
                ReleaseBlockMeshes(); // meshes of another atlas (another content snapshot)
                _blockMeshAtlas = atlas;
            }

            ushort id = def.NumericId.Value;
            if (!BlockMeshes.TryGetValue(id, out var mesh) || mesh == null)
            {
                mesh = BlockCubeFaces.BuildMesh(content, atlas, def.NumericId, BlockCubeFaces.FaceMinusZ, chunkChannels: false);
                mesh.name = "HeldBlock";
                BlockMeshes[id] = mesh;
            }

            if (!BlockMaterials.TryGetValue(atlas.Texture, out var material) || material == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
                material = new Material(shader) { color = Color.white, mainTexture = atlas.Texture };
                BlockMaterials[atlas.Texture] = material;
            }

            var renderer = blockGo.GetComponent<Renderer>();
            BlockCubeFaces.DestroyAsset(renderer.sharedMaterial); // the cube's own tinted material — the shared one replaces it
            renderer.sharedMaterial = material;
            blockGo.GetComponent<MeshFilter>().sharedMesh = mesh;
            return true;
        }

        /// <summary>Frees the held blocks' cube meshes (#2744).</summary>
        private static void ReleaseBlockMeshes()
        {
            foreach (var mesh in BlockMeshes.Values)
            {
                BlockCubeFaces.DestroyAsset(mesh);
            }

            BlockMeshes.Clear();
            _blockMeshAtlas = null;
        }

        /// <summary>The bare suit hand (#1033): a gloved fist on the suit-coloured forearm — thumb on the inner side, cuff a
        /// darker shade for definition. <paramref name="left"/> mirrors it (thumb on +x) and paints it from the left arm.</summary>
        private static void HandCubes(Transform holder, Color tint, bool left)
        {
            // The bare fist is HeldHands.Fist (#2777) at z 0.07 — the hand every held item now comes with.
            foreach (var box in HeldHands.Fist(new System.Numerics.Vector3(0f, 0f, 0.07f)))
            {
                HandBox(holder, box, tint, left);
            }
        }

        /// <summary>
        /// #2777: the first-person suit hand holding <paramref name="held"/> — the holder <see cref="Build"/> returned — in the
        /// grip its item takes (<see cref="HeldHands.GripFor"/>): the painted forearm and hand of the bare fist, built under
        /// the item where the grip closes. Nothing for the items that bring their own hands (the bare hand, the gloves).
        /// An icon whose pixels are still on their way (WebGPU) is held at its nominal card size and re-gripped when its
        /// mesh arrives (<see cref="RefreshHand"/>). Only the first-person view calls this: an avatar has its own hand.
        /// </summary>
        public static void AttachHand(GameObject held, Kind kind, string itemKey, GameContent content)
        {
            if (held == null)
            {
                return;
            }

            var def = string.IsNullOrEmpty(itemKey) || content == null ? null : content.GetItem(itemKey);
            string grip = HeldHands.GripFor(def, kind.ToString());
            if (grip == null)
            {
                return;
            }

            // TryGetComponent, not GetComponent ?? Add: in the Editor a missing component comes back as a fake-null object
            // that ?? takes for a real one, and the hand would never be built there.
            if (!held.TryGetComponent(out HeldHandGrip tag))
            {
                tag = held.AddComponent<HeldHandGrip>();
            }

            tag.Grip = grip;
            RefreshHand(held.transform);
        }

        /// <summary>(Re)builds the hand of a held holder that has one (<see cref="AttachHand"/>) around what it holds now.</summary>
        internal static void RefreshHand(Transform held)
        {
            if (held == null || !held.TryGetComponent(out HeldHandGrip tag))
            {
                return;
            }

            if (tag.Hand != null)
            {
                Object.Destroy(tag.Hand);
            }

            var parts = new System.Collections.Generic.List<HeldItemShapes.Part>();
            if (!HeldBounds(held, parts, out var min, out var max))
            {
                // Nothing measurable yet (an icon still being read back): its nominal card.
                min = new Vector3(-0.15f, -0.11f, 0.15f);
                max = new Vector3(0.15f, 0.19f, 0.15f);
            }

            var hand = new GameObject("Hand");
            hand.transform.SetParent(held, false);
            var tint = HandTintResolver?.Invoke() ?? DefaultGlove;
            var boxes = HeldHands.For(tag.Grip, parts, new System.Numerics.Vector3(min.x, min.y, min.z),
                new System.Numerics.Vector3(max.x, max.y, max.z));
            foreach (var box in boxes)
            {
                HandBox(hand.transform, box, tint, left: false);
            }

            var pivot = HeldHands.SwingPivot(boxes);
            tag.Pivot = new Vector3(pivot.X, pivot.Y, pivot.Z);
            tag.Hand = hand;
        }

        /// <summary>The bounds of what <paramref name="held"/> holds, in its own frame — its model's cubes (collected into
        /// <paramref name="parts"/> for the fist's handle) and its miniature's mesh — without its hand. False when there is
        /// nothing to measure.</summary>
        private static bool HeldBounds(Transform held, System.Collections.Generic.List<HeldItemShapes.Part> parts, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;
            for (int i = 0; i < held.childCount; i++)
            {
                var c = held.GetChild(i);
                if (c.name == "Part")
                {
                    var p = c.localPosition;
                    var s = c.localScale;
                    parts.Add(new HeldItemShapes.Part(new System.Numerics.Vector3(p.x, p.y, p.z), new System.Numerics.Vector3(s.x, s.y, s.z),
                        new HeldItemShapes.Rgb(1f, 1f, 1f)));
                    min = Vector3.Min(min, p - (s * 0.5f));
                    max = Vector3.Max(max, p + (s * 0.5f));
                    any = true;
                }
                else if (c.name == HeldMiniatures.NodeName && c.GetComponent<MeshFilter>() is { } f && f.sharedMesh != null)
                {
                    var b = f.sharedMesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                        var local = c.localPosition + (c.localRotation * Vector3.Scale(c.localScale, corner));
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }

                    any = true;
                }
            }

            return any;
        }

        /// <summary>One box of a suit hand (#2777): the forearm and the hand wear the player's arm painting when there is one
        /// (#1427, #2755), the cuff a darker shade of the suit; all of them in the shared, lifted hand materials.</summary>
        private static void HandBox(Transform parent, HeldHands.Box box, Color tint, bool left)
        {
            float mx = left ? -1f : 1f; // a left hand is the mirror image: its thumb on +X
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "HandPart";
            if (go.GetComponent<Collider>() is { } col)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(box.Position.X * mx, box.Position.Y, box.Position.Z);
            go.transform.localScale = new Vector3(box.Size.X, box.Size.Y, box.Size.Z);
            var renderer = go.GetComponent<Renderer>();
            if (box.Piece == HeldHands.Piece.Cuff)
            {
                renderer.sharedMaterial = HandMaterial(new Color(tint.r * 0.7f, tint.g * 0.7f, tint.b * 0.7f), null);
                return;
            }

            var atlas = HandPaintAtlas(tint);
            if (atlas == null)
            {
                renderer.sharedMaterial = HandMaterial(tint, null);
                return;
            }

            renderer.sharedMaterial = HandMaterial(Color.white, atlas); // the painting's true colours
            go.GetComponent<MeshFilter>().sharedMesh = HandMesh(left, hand: box.Piece == HeldHands.Piece.Hand);
        }

        /// <summary>#2278: one glove of a held pair on an avatar's hand (third person and other players). The glove model
        /// points along +Z like every held model; the avatar's hand hangs along −Y, so the glove is turned onto the hand,
        /// scaled to wrap the avatar's bigger hand box (0.2 wide) and set so its cuff closes over the wrist. No forearm —
        /// the avatar has its own arm.</summary>
        public static GameObject BuildAvatarGlove(Transform hand, string itemKey,
            System.Collections.Generic.IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart> look, bool left)
        {
            var holder = new GameObject(left ? "HeldGloveLeft" : "HeldGlove");
            holder.transform.SetParent(hand, false);
            holder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // +Z (the fist's front) → down the hanging hand
            holder.transform.localScale = Vector3.one * 1.55f;
            holder.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            var model = look != null && look.Count > 0 ? look : (itemKey != null ? ModelResolver?.Invoke(BlocksBeyondTheStars.Shared.State.ItemKey.Base(itemKey)) : null);
            BuildGlove(holder.transform, Color.white, model, left, forearm: false);
            return holder;
        }

        /// <summary>One glove of a pair (#2278): the suit forearm (painted like the bare hand's; first person only) and the
        /// glove's own parts from the item data — a right glove, mirrored for <paramref name="left"/>.</summary>
        private static void BuildGlove(Transform holder, Color suit, System.Collections.Generic.IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart> model, bool left,
            bool forearm = true)
        {
            if (forearm)
            {
                HandBox(holder, new HeldHands.Box(HeldHands.Piece.Forearm, new System.Numerics.Vector3(0.02f, -0.06f, -0.17f),
                    new System.Numerics.Vector3(0.11f, 0.11f, 0.28f)), suit, left);
            }

            var parts = HeldItemShapes.Glove(model, new HeldItemShapes.Rgb(0.45f, 0.85f, 0.95f));
            if (left)
            {
                parts = HeldItemShapes.Mirror(parts);
            }

            foreach (var part in parts)
            {
                Cube(holder, new Vector3(part.Position.X, part.Position.Y, part.Position.Z),
                    new Vector3(part.Size.X, part.Size.Y, part.Size.Z), new Color(part.Color.R, part.Color.G, part.Color.B), part.Glow);
            }
        }

        /// <summary>A first-person suit hand for the climb (#2287) under a new holder: the bare hand (painted, lifted) plus
        /// the worn climbing gear's pads and claws (<see cref="HeldItemShapes.ClimbGear"/>), mirrored for the left hand.</summary>
        public static GameObject BuildClimbHand(Transform parent, Color tint, bool left, bool gloves, bool claws)
        {
            var holder = new GameObject(left ? "ClimbHandLeft" : "ClimbHandRight");
            holder.transform.SetParent(parent, false);
            HandCubes(holder.transform, tint, left);
            var gear = HeldItemShapes.ClimbGear(gloves, claws);
            if (left)
            {
                gear = HeldItemShapes.Mirror(gear);
            }

            foreach (var part in gear)
            {
                Cube(holder.transform, new Vector3(part.Position.X, part.Position.Y, part.Position.Z),
                    new Vector3(part.Size.X, part.Size.Y, part.Size.Z), new Color(part.Color.R, part.Color.G, part.Color.B), part.Glow);
            }

            return holder;
        }

        /// <summary>The world position of a held model's tip — the front face of its foremost part (the barrel of a gun,
        /// the emitter of a gadget, the bit of a drill). Effects start here instead of at the screen centre (#2151).
        /// False when <paramref name="held"/> has no parts.</summary>
        public static bool MuzzleOf(GameObject held, out Vector3 world)
        {
            world = default;
            if (held == null || !held.activeInHierarchy)
            {
                return false;
            }

            var root = held.transform;
            Transform best = null;
            float bestZ = float.MinValue;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name == HeldMiniatures.NodeName || c.name == "Hand")
                {
                    continue; // a scaled miniature (#2534) is not a unit cube part — and has no muzzle; nor has the hand under an item (#2777)
                }

                float front = c.localPosition.z + c.localScale.z * 0.5f;
                if (front > bestZ)
                {
                    bestZ = front;
                    best = c;
                }
            }

            if (best == null)
            {
                return false;
            }

            world = root.TransformPoint(new Vector3(best.localPosition.x, best.localPosition.y, bestZ + 0.01f));
            return true;
        }

        // Cached hand-paint atlas: the hand rebuilds on every hotbar change, and baking a texture each
        // time would leak one per switch. Keyed by payload + base colour; the stale one is destroyed.
        private static Texture2D _handAtlas;
        private static string _handAtlasKey;

        // #2755: the painted forearm / fist meshes, one per arm (row 0 = left, 1 = right) and segment (lower arm, hand).
        // They depend on the arm's geometry only — the painting itself is the atlas texture — so they are built once.
        private static readonly Mesh[] HandMeshes = new Mesh[4];

        /// <summary>Frees the cached hand-paint atlas and the painted hand meshes (world teardown, #1464) — a static
        /// Texture2D / Mesh is never swept by <c>Resources.UnloadUnusedAssets</c> while this class still references it.</summary>
        public static void ReleaseHandAtlas()
        {
            ReleaseHandMaterials(paintedOnly: false); // #2777: the shared hand materials too
            BlockCubeFaces.DestroyAsset(_handAtlas);
            _handAtlas = null;
            _handAtlasKey = null;
            for (int i = 0; i < HandMeshes.Length; i++)
            {
                BlockCubeFaces.DestroyAsset(HandMeshes[i]);
                HandMeshes[i] = null;
            }
        }

        /// <summary>
        /// The player's arm painting baked for a suit colour (the atlas the avatar also bakes), or null without a valid
        /// painting — the hand parts keep their flat suit tint then, matching the pre-#1427 look.
        /// <para>
        /// #2755: the parts wear the avatar's own painted arm (<see cref="PlayerAvatar.PaintedArmMesh"/>) instead of one
        /// chunk stretched over every face of a Unity primitive, whose faces lay their UVs out turned or mirrored: the
        /// forearm the lower arm, the fist and thumb the hand, each side face its own arm chunk, read as painted. The
        /// first-person arm is the avatar's arm raised forward with the back of the hand up, so the mesh is turned from
        /// the shoulder frame (arm down −y, front +z) into the hand's (+z forward): the arm's outer side faces up — the
        /// outer chunk, the paint editor's hero face, stays on top as before — its front turns toward the body, its back
        /// outward, its inner side down; the painting's top runs back toward the shoulder, as on the avatar.
        /// </para>
        /// </summary>
        private static Texture2D HandPaintAtlas(Color baseColor)
        {
            string pixels = HandPaintResolver?.Invoke();
            if (string.IsNullOrEmpty(pixels) || pixels.Length != BodyPaint.ExpectedLength(BodyPaint.Arms))
            {
                return null;
            }

            string key = pixels + "#" + ColorUtility.ToHtmlStringRGB(baseColor);
            if (_handAtlasKey != key || _handAtlas == null)
            {
                ReleaseHandMaterials(paintedOnly: true); // they sample the atlas that goes now
                BlockCubeFaces.DestroyAsset(_handAtlas);
                _handAtlas = BodyPaintKit.BuildAtlas(BodyPaint.Arms, pixels, baseColor);
                _handAtlasKey = key;
            }

            return _handAtlas;
        }

        // #2777: one lifted material per (colour, painting) for every suit hand. The hand now comes with every held item and
        // is rebuilt on every hotbar switch; a material per part per rebuild (as the bare hand had) would pile up.
        private static readonly System.Collections.Generic.Dictionary<(int Rgb, bool Painted), Material> HandMaterials =
            new System.Collections.Generic.Dictionary<(int Rgb, bool Painted), Material>();

        /// <summary>The shared hand material in <paramref name="color"/>, or over the painting <paramref name="painting"/>
        /// (white: its true colours — the mesh's UVs already sit in the atlas). Lifted to the avatar's ambient floor + fill
        /// (see <c>PlayerAvatar.Lit</c>): the hand is suit-coloured like the avatar, and LitColor's fixed key light plus
        /// Linear colour space would sink a dark tint to a black silhouette without it (#1427).</summary>
        private static Material HandMaterial(Color color, Texture2D painting)
        {
            var c32 = (Color32)color;
            var key = (((c32.r << 16) | (c32.g << 8) | c32.b), painting != null);
            if (HandMaterials.TryGetValue(key, out var cached) && cached != null
                && (painting == null || cached.mainTexture == painting))
            {
                return cached;
            }

            if (cached != null)
            {
                BlockCubeFaces.DestroyAsset(cached);
            }

            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var material = painting != null
                ? new Material(shader) { color = Color.white, mainTexture = painting }
                : new Material(shader) { color = ShaderColor.Srgb(color) };
            if (material.HasProperty("_Floor")) // no-op on the Unlit/Color fallback
            {
                material.SetFloat("_Floor", 0.62f);
                material.SetFloat("_Fill", 0.3f);
            }

            HandMaterials[key] = material;
            return material;
        }

        private static void ReleaseHandMaterials(bool paintedOnly)
        {
            var drop = new System.Collections.Generic.List<(int Rgb, bool Painted)>();
            foreach (var kv in HandMaterials)
            {
                if (!paintedOnly || kv.Key.Painted)
                {
                    BlockCubeFaces.DestroyAsset(kv.Value);
                    drop.Add(kv.Key);
                }
            }

            foreach (var key in drop)
            {
                HandMaterials.Remove(key);
            }
        }

        /// <summary>The avatar's painted arm segment turned into the first-person hand's frame (see <see cref="HandPaintAtlas"/>):
        /// arm-down → forward (+z), the arm's outer side → up (+y), so the right arm maps (x, y, z) → (−z, x, −y) and the left
        /// arm, whose outer side is −x, (x, y, z) → (z, −x, −y). Both are rotations, so every face still reads as painted
        /// and faces outward.</summary>
        private static Mesh HandMesh(bool left, bool hand)
        {
            int slot = (left ? 0 : 2) + (hand ? 1 : 0);
            if (HandMeshes[slot] != null)
            {
                return HandMeshes[slot];
            }

            var mesh = PlayerAvatar.PaintedArmMesh(left ? 0 : 1, hand);
            var verts = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < verts.Length; i++)
            {
                verts[i] = ToHandFrame(verts[i], left);
                normals[i] = ToHandFrame(normals[i], left);
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            mesh.name = left ? "HandPaintLeft" : "HandPaintRight";
            HandMeshes[slot] = mesh;
            return mesh;
        }

        private static Vector3 ToHandFrame(Vector3 v, bool left)
            => left ? new Vector3(v.z, -v.x, -v.y) : new Vector3(-v.z, v.x, -v.y);

        // One material per (colour, glow) for ALL held parts (#1962). A held model is rebuilt whenever the hotbar
        // slot changes, for every player in sight; a material per part per rebuild was never destroyed with its
        // cube (sharedMaterial is not owned by the renderer) and piled up over a session.
        private static readonly System.Collections.Generic.Dictionary<int, Material> PartMaterials = new System.Collections.Generic.Dictionary<int, Material>();

        private static Material PartMaterial(Color color, bool glow)
        {
            var c32 = (Color32)color;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b | (glow ? 1 << 24 : 0);
            if (PartMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { color = ShaderColor.Srgb(color) };
            if (glow && material.HasProperty("_Floor"))
            {
                material.SetFloat("_Floor", 1f); // fully lit whatever the light: an energy coil glows in a cave
            }

            PartMaterials[key] = material;
            return material;
        }

        /// <summary>One glowing cube part under <paramref name="parent"/> in the shared part material (the held speeder's
        /// engine glows, #2534).</summary>
        internal static GameObject GlowPart(Transform parent, Vector3 localPos, Vector3 scale, Color color)
            => Cube(parent, localPos, scale, color, glow: true);

        /// <summary>One plain lit cube part under <paramref name="parent"/> in the shared part material (a hunter's flying
        /// arrow, #2647, is built like a held model).</summary>
        internal static GameObject SolidPart(Transform parent, Vector3 localPos, Vector3 scale, Color color)
            => Cube(parent, localPos, scale, color, glow: false);

        /// <summary>Drops the shared part materials, the held miniatures' meshes and the held blocks' cubes (session
        /// teardown).</summary>
        public static void ReleasePartMaterials()
        {
            HeldMiniatures.ReleaseAll();
            foreach (var material in PartMaterials.Values)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }

            PartMaterials.Clear();
            ReleaseBlockMeshes();
            foreach (var material in BlockMaterials.Values)
            {
                BlockCubeFaces.DestroyAsset(material);
            }

            BlockMaterials.Clear();
        }

        private static GameObject Cube(Transform parent, Vector3 localPos, Vector3 scale, Color color, bool glow)
        {
            // The parts of a model never change their material afterwards (unlike the held block, whose cube gets the
            // atlas tile), so they can share: swap the per-cube material for the cached one.
            var go = Cube(parent, localPos, scale, color);
            var renderer = go.GetComponent<Renderer>();
            Object.Destroy(renderer.sharedMaterial);
            renderer.sharedMaterial = PartMaterial(color, glow);
            return go;
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

    /// <summary>#2777: marks a first-person held holder as held by the suit hand — its grip, the hand built for it (rebuilt
    /// when an icon's mesh arrives late, <see cref="HeldItem.RefreshHand"/>) and the point on its forearm the viewmodel
    /// swings it round (<see cref="HeldHands.SwingPivot"/>).</summary>
    public sealed class HeldHandGrip : MonoBehaviour
    {
        internal string Grip;
        internal GameObject Hand;
        internal Vector3 Pivot;
    }
}
