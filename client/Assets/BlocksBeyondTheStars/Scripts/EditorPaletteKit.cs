// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shared palette model + row list for the standalone build editors (<see cref="ShipEditor"/>,
    /// <see cref="StructureEditor"/>). Entries carry a localized label, a section group and — for
    /// blocks — the block's real atlas tile as icon plus the tile's average colour (which also renders
    /// the placed 3D cell, so the build preview matches the in-game material). The editors keep only
    /// their special entries (markers / ship parts) and the placement logic.
    /// </summary>
    internal static class EditorPaletteKit
    {
        internal struct Entry
        {
            public string Id;      // palette id: block key, or a marker/part id
            public string Label;   // localized display label
            public string Kind;    // "block" | "marker" | "element" | "station"
            public string Group;   // section slug: "markers", "parts" or a block category (ui.cat.*)
            public Color Color;    // 3D cell colour (avg atlas tile for blocks; hand-picked otherwise)
            public Sprite Icon;    // atlas tile sprite for blocks; null -> plain colour swatch
        }

        /// <summary>Display order of the block category sections; unknown categories sort after.</summary>
        private static readonly string[] CategoryOrder = { "building", "light", "door", "machine", "terrain", "ore", "flora" };

        /// <summary>Every placeable block as a palette entry — localized label, category group, atlas
        /// tile icon + average colour — sorted by category, then by label in the active language.</summary>
        internal static List<Entry> BlockEntries(AppShell shell, BlockTextureAtlas atlas)
        {
            var list = new List<Entry>();
            var content = shell != null ? shell.Content : null;
            if (content == null)
            {
                return list;
            }

            foreach (var def in content.Blocks.Values)
            {
                if (def.Key == "air")
                {
                    continue;
                }

                list.Add(new Entry
                {
                    Id = def.Key,
                    Label = shell.L(def.NameKey),
                    Kind = "block",
                    Group = string.IsNullOrEmpty(def.Category) ? "building" : def.Category,
                    Color = TileAverage(atlas, def.NumericId.Value, def.Key),
                    Icon = TileSprite(atlas, def.NumericId.Value),
                });
            }

            list.Sort((a, b) =>
            {
                int rank = CategoryRank(a.Group).CompareTo(CategoryRank(b.Group));
                if (rank != 0)
                {
                    return rank;
                }

                int group = string.Compare(a.Group, b.Group, StringComparison.Ordinal);
                return group != 0 ? group : string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static int CategoryRank(string group)
        {
            int i = Array.IndexOf(CategoryOrder, group);
            return i >= 0 ? i : CategoryOrder.Length;
        }

        /// <summary>Localized section title for a group slug ("markers", "parts" or a block category).</summary>
        internal static string GroupTitle(AppShell shell, string group)
        {
            string key = group == "markers" ? "ui.pal.markers"
                : group == "parts" ? "ui.pal.parts"
                : "ui.cat." + group;
            return shell?.Localizer != null && shell.Localizer.Has(key) ? shell.L(key) : group;
        }

        /// <summary>Cuts the block's tile out of the editor-local atlas as a UI sprite; null without an atlas.</summary>
        internal static Sprite TileSprite(BlockTextureAtlas atlas, ushort id)
        {
            var tex = atlas?.Texture;
            if (tex == null)
            {
                return null;
            }

            var uv = atlas.TileUv(id);
            var px = new Rect(uv.x * tex.width, uv.y * tex.height, uv.width * tex.width, uv.height * tex.height);
            return Sprite.Create(tex, px, new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Average opaque colour of the block's atlas tile — the closest single-colour stand-in
        /// for the real material (cutout pixels are skipped so flora reads green, not black). Falls back
        /// to a stable key-hash hue when no atlas is available.</summary>
        internal static Color TileAverage(BlockTextureAtlas atlas, ushort id, string key)
        {
            var tex = atlas?.Texture;
            if (tex == null)
            {
                return HashSwatch(key);
            }

            var uv = atlas.TileUv(id);
            int x = Mathf.RoundToInt(uv.x * tex.width), y = Mathf.RoundToInt(uv.y * tex.height);
            int w = Mathf.RoundToInt(uv.width * tex.width), h = Mathf.RoundToInt(uv.height * tex.height);
            var pixels = tex.GetPixels(x, y, w, h);
            float r = 0f, g = 0f, b = 0f;
            int n = 0;
            for (int i = 0; i < pixels.Length; i += 3) // every 3rd pixel is plenty for an average
            {
                var p = pixels[i];
                if (p.a < 0.5f)
                {
                    continue;
                }

                r += p.r; g += p.g; b += p.b; n++;
            }

            return n == 0 ? HashSwatch(key) : new Color(r / n, g / n, b / n);
        }

        /// <summary>Legacy stable pseudo-colour for a key (pre-atlas palette look), kept as fallback.</summary>
        private static Color HashSwatch(string key)
        {
            int h = 0;
            foreach (char c in key) h = h * 31 + c;
            float hue = ((h & 0x7FFFFFFF) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.32f, 0.78f);
        }
    }

    /// <summary>
    /// The palette row list (uGUI): the entries rendered as selectable rows under localized section
    /// headers, filtered by a search string; exactly one entry is selected at a time. Selection is by
    /// entry index, so it survives filtering.
    /// </summary>
    internal sealed class PaletteListUi
    {
        private readonly AppShell _shell;
        private readonly Transform _parent;
        private readonly IReadOnlyList<EditorPaletteKit.Entry> _entries;
        private readonly List<Image> _rowImages = new();
        private readonly List<int> _rowToEntry = new();

        public int Selected { get; private set; }

        /// <summary>Notified with the entry index on every selection (also the initial one).</summary>
        public Action<int> OnSelected;

        public PaletteListUi(AppShell shell, Transform listParent, IReadOnlyList<EditorPaletteKit.Entry> entries, int initialSelected = 0)
        {
            _shell = shell;
            _parent = listParent;
            _entries = entries;
            Selected = initialSelected;
        }

        /// <summary>Rebuilds the rows from the search filter (matched by label or id); a section header
        /// precedes the first visible row of each group.</summary>
        public void Rebuild(string search)
        {
            for (int i = _parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_parent.GetChild(i).gameObject);
            }

            _rowImages.Clear();
            _rowToEntry.Clear();

            string q = (search ?? string.Empty).Trim().ToLowerInvariant();
            string lastGroup = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                bool match = q.Length == 0
                    || (e.Label != null && e.Label.ToLowerInvariant().Contains(q))
                    || (e.Id != null && e.Id.ToLowerInvariant().Contains(q));
                if (!match)
                {
                    continue;
                }

                if (e.Group != lastGroup)
                {
                    AddHeader(EditorPaletteKit.GroupTitle(_shell, e.Group));
                    lastGroup = e.Group;
                }

                AddRow(i);
            }

            Select(Selected);
        }

        public void Select(int index)
        {
            Selected = index;
            OnSelected?.Invoke(index);
            for (int i = 0; i < _rowImages.Count; i++)
            {
                _rowImages[i].color = _rowToEntry[i] == index
                    ? new Color(0.45f, 0.82f, 1f, 1f)
                    : new Color(0.62f, 0.68f, 0.76f, 1f);
            }
        }

        private void AddHeader(string title)
        {
            var row = new GameObject("Header", typeof(RectTransform));
            row.transform.SetParent(_parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 26f;
            UiKit.AddText(row.transform, 6f, 4f, 250f, 22f, title, 13, UiKit.CyanDim, TextAnchor.LowerLeft, FontStyle.Bold);
        }

        private void AddRow(int entryIndex)
        {
            var e = _entries[entryIndex];
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(_parent, false);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 36f;

            var img = row.AddComponent<Image>();
            img.sprite = UiKit.ButtonSprite;
            img.type = Image.Type.Sliced;

            var btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            int idx = entryIndex;
            btn.onClick.AddListener(() => Select(idx));

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(row.transform, false);
            UiKit.Place(iconGo, 8f, 6f, 24f, 24f);
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.raycastTarget = false;
            if (e.Icon != null)
            {
                iconImg.sprite = e.Icon; // the block's real atlas tile
            }
            else
            {
                iconImg.sprite = UiKit.SolidSprite;
                iconImg.color = e.Color;
            }

            UiKit.AddText(row.transform, 40f, 0f, 230f, 36f, e.Label, 15, UiKit.TextCol);
            _rowImages.Add(img);
            _rowToEntry.Add(entryIndex);
        }
    }
}
