// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How a texture is shown in the game — which decides the texture editor's 3-D preview.</summary>
    internal enum TexturePreviewKind
    {
        /// <summary>A cube with the tile on every side (most blocks; also the stand-in for non-block textures).</summary>
        Cube,

        /// <summary>A built-in form whose parts cut regions out of a PICTURE tile (bed, campfire, rug, pot, ladder).</summary>
        ShapedPicture,

        /// <summary>Crossed cards with an alpha cutout (plants, torch, lantern).</summary>
        Billboard,
    }

    /// <summary>One texture the editor can open.</summary>
    internal sealed class TextureEntry
    {
        public string Key;
        public string Label;
        public string Group;
        public BlockDefinition Block; // null for non-block textures
        public TexturePreviewKind Preview;

        /// <summary>Icon mode (#1962): the entry is an ICON of the build (<c>Resources/icons/&lt;name&gt;.png</c>), not a
        /// tile — free alpha, one frame, saved into the pack's icon folder. Null for tiles.</summary>
        public string IconName;

        public bool IsIcon => IconName != null;
    }

    /// <summary>
    /// Everything the texture editor (#1955) can open, read from the RUNNING game — the block list of the loaded
    /// content and the texture resources of the build — never from a developer's folders, so the editor works on any
    /// install. Blocks are grouped by their category; the non-block tiles (creature hides, avatar fabrics, micro
    /// fauna, the robot and the asteroid) follow in groups of their own.
    /// </summary>
    internal static class TextureCatalog
    {
        public const string GroupCreatures = "tex_creatures";
        public const string GroupAvatar = "tex_avatar";
        public const string GroupMicrofauna = "tex_microfauna";
        public const string GroupOther = "tex_other";
        public const string GroupIcons = "tex_icons";

        private static readonly string[] CategoryOrder = { "terrain", "ore", "building", "light", "door", "machine", "flora" };

        public static List<TextureEntry> Build(GameContent content, Func<string, string> localize)
        {
            var entries = new List<TextureEntry>();
            var blockKeys = new HashSet<string>(StringComparer.Ordinal);
            if (content != null)
            {
                foreach (var def in content.Blocks.Values)
                {
                    if (def.NumericId.Value == 0)
                    {
                        continue;
                    }

                    blockKeys.Add(def.Key);
                    string name = localize != null ? localize(def.NameKey) : def.Key;
                    entries.Add(new TextureEntry
                    {
                        Key = def.Key,
                        Label = string.IsNullOrEmpty(name) || name == def.NameKey ? def.Key : name,
                        Group = string.IsNullOrEmpty(def.Category) ? "building" : def.Category,
                        Block = def,
                        Preview = PreviewOf(def),
                    });
                }
            }

            // The non-block tiles: whatever the build bundles under Resources/textures that is not a block (and not
            // the extra animation frames of one).
            foreach (var asset in Resources.LoadAll<TextAsset>("textures"))
            {
                string key = asset.name;
                if (blockKeys.Contains(key) || key.EndsWith(GameTextures.AnimSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                entries.Add(new TextureEntry
                {
                    Key = key,
                    Label = key.Replace('_', ' '),
                    Group = key.StartsWith("creature_", StringComparison.Ordinal) ? GroupCreatures
                        : key.StartsWith("avatar_", StringComparison.Ordinal) ? GroupAvatar
                        : key.StartsWith("microfauna_", StringComparison.Ordinal) ? GroupMicrofauna
                        : GroupOther,
                    Preview = TexturePreviewKind.Cube,
                });
            }

            // Icon mode (#1962): the item icons of the build. They come in every size; the editor paints them on its
            // one canvas size (64×64), which is what a hotbar slot shows anyway.
            foreach (var icon in Resources.LoadAll<Texture2D>("icons"))
            {
                if (!icon.name.StartsWith("item_", StringComparison.Ordinal) || !TextureTiles.IsValidKey(icon.name))
                {
                    continue;
                }

                string itemKey = icon.name.Substring("item_".Length);
                string nameKey = content?.GetItem(itemKey)?.NameKey;
                string label = localize != null && !string.IsNullOrEmpty(nameKey) ? localize(nameKey) : null;
                entries.Add(new TextureEntry
                {
                    Key = icon.name,
                    IconName = icon.name,
                    Label = string.IsNullOrEmpty(label) || label == nameKey ? itemKey.Replace('_', ' ') : label,
                    Group = GroupIcons,
                    Preview = TexturePreviewKind.Billboard,
                });
            }

            entries.Sort((a, b) =>
            {
                int ga = Rank(a.Group), gb = Rank(b.Group);
                if (ga != gb)
                {
                    return ga.CompareTo(gb);
                }

                int g = string.CompareOrdinal(a.Group, b.Group);
                return g != 0 ? g : string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return entries;
        }

        private static int Rank(string group)
        {
            int i = Array.IndexOf(CategoryOrder, group);
            if (i >= 0)
            {
                return i;
            }

            return group.StartsWith("tex_", StringComparison.Ordinal) ? CategoryOrder.Length + 1 : CategoryOrder.Length;
        }

        private static TexturePreviewKind PreviewOf(BlockDefinition def)
        {
            if (PropShapes.DefaultPlaceShape(def.Key) != 0)
            {
                return TexturePreviewKind.ShapedPicture;
            }

            if (def.Key is "torch" or "lantern"
                || (def.Key.StartsWith("flora_", StringComparison.Ordinal) && !FloraCatalog.IsSolid(def.Key)))
            {
                return TexturePreviewKind.Billboard;
            }

            return TexturePreviewKind.Cube;
        }
    }
}
