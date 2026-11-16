// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Draws the bio lab's symbols (#3717): which icon and colour stand for what is decided in
    /// <see cref="BioSymbols"/> (Client.Core, tested headless) — this class only puts them on a canvas. Every icon is
    /// white ink tinted here, so one picture serves an effect (its family's colour), a side effect (amber) and a
    /// material trait (on its hexagon plate).
    /// </summary>
    public static class BioIcons
    {
        private static readonly Color PlateFill = new Color(0.16f, 0.32f, 0.46f, 1f);
        private static readonly Color PlateDim = new Color(0.12f, 0.20f, 0.28f, 1f);
        private static readonly Color BarEmpty = new Color(0.20f, 0.28f, 0.38f, 0.9f);
        private static readonly Color BadgePlate = new Color(0.02f, 0.05f, 0.09f, 0.94f);

        // The effect a preparation's key carries, kept per key: the hotbar asks ten times a second.
        private static readonly Dictionary<string, BioEffect> PreparationEffects = new Dictionary<string, BioEffect>();

        public static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>The colour of an effect: its family's (five colours — the icon tells the effects of a family apart).</summary>
        public static Color EffectColor(BioEffect effect) => effect == BioEffect.None ? Color.white : Rgb(BioSymbols.EffectRgb(effect));

        public static Color RarityColor(int rarity) => Rgb(BioSymbols.RarityRgb(rarity));

        /// <summary>The sprite of an icon name, or null — also for no name at all (an effect a newer server sent).</summary>
        public static Sprite Find(string icon) => string.IsNullOrEmpty(icon) ? null : UiKit.Icon(icon);

        /// <summary>An icon by name at a square rect, tinted; a small dot in the tint when the art is missing, so a
        /// row never loses its mark.</summary>
        public static Image Add(Transform parent, float x, float y, float size, string icon, Color tint)
        {
            var sprite = Find(icon);
            return sprite != null
                ? UiKit.AddImage(parent, x, y, size, size, sprite, tint)
                : UiKit.AddImage(parent, x + size * 0.25f, y + size * 0.25f, size * 0.5f, size * 0.5f, UiKit.DiscSprite, tint);
        }

        /// <summary>An effect's icon in its family's colour.</summary>
        public static Image AddEffect(Transform parent, float x, float y, float size, BioEffect effect)
            => Add(parent, x, y, size, BioSymbols.EffectIcon(effect), EffectColor(effect));

        /// <summary>A side effect's icon: the icon of the effect it undoes, in the warning amber.</summary>
        public static Image AddSide(Transform parent, float x, float y, float size, BioSideEffect side)
            => Add(parent, x, y, size, BioSymbols.SideIcon(side), UiKit.Warn);

        /// <summary>A material trait: its icon on the hexagon plate. A trace trait sits on a dimmer plate.</summary>
        public static void AddTrait(Transform parent, float x, float y, float size, MatTrait trait, bool trace = false)
        {
            Add(parent, x, y, size, BioSymbols.HexPlate, trace ? PlateDim : PlateFill);
            float inner = size * 0.58f;
            float inset = (size - inner) * 0.5f;
            Add(parent, x + inset, y + inset, inner, BioSymbols.TraitIcon(trait), trace ? UiKit.CyanDim : UiKit.TextCol);
        }

        /// <summary>A row of dots, <paramref name="count"/> of <paramref name="max"/> lit; returns the width it takes.</summary>
        public static float AddPips(Transform parent, float x, float y, int count, int max, float pip, Color lit)
        {
            float step = pip + 3f;
            for (int i = 0; i < max; i++)
            {
                UiKit.AddImage(parent, x + i * step, y, pip, pip, UiKit.DiscSprite, i < count ? lit : BarEmpty);
            }

            return max * step - 3f;
        }

        /// <summary>
        /// The strength bar: five segments of three steps — the fifteen levels, cut where the rarity tiers cut them — each
        /// segment in its tier's colour. The Roman numeral beside it names the tier; the bar shows the step inside it.
        /// </summary>
        public static void AddLevelBar(Transform parent, float x, float y, float w, float h, int level)
        {
            const float gap = 3f;
            float seg = (w - gap * (BioSymbols.BarSegments - 1)) / BioSymbols.BarSegments;
            for (int i = 0; i < BioSymbols.BarSegments; i++)
            {
                float sx = x + i * (seg + gap);
                UiKit.AddImage(parent, sx, y, seg, h, UiKit.SolidSprite, BarEmpty);
                int fill = BioSymbols.SegmentFill(level, i);
                if (fill > 0)
                {
                    UiKit.AddImage(parent, sx, y, seg * fill / BioSymbols.StepsPerSegment, h, UiKit.SolidSprite, RarityColor(i));
                }
            }
        }

        /// <summary>
        /// A deposit's traits as a row of chips for a list line: plate, icon and the level as a digit, strongest first,
        /// the trace trait last on its dimmer plate. Returns the width it takes; stops before <paramref name="maxWidth"/>.
        /// </summary>
        public static float AddTraitChips(Transform parent, float x, float y, float size, MaterialProfile material, float maxWidth)
        {
            float step = size + 20f;
            float cx = x;
            foreach (var trait in TraitsByLevel(material))
            {
                if (cx + step - x > maxWidth) break;
                bool trace = trait == material.Trace;
                AddTrait(parent, cx, y, size, trait, trace);
                UiKit.AddText(parent, cx + size + 2f, y, 14f, size, material.Levels[(int)trait].ToString(), 14,
                    trace ? UiKit.CyanDim : UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                cx += step;
            }

            return cx - x;
        }

        /// <summary>The traits a material shows, the strongest first; a deposit's trace trait comes last.</summary>
        public static List<MatTrait> TraitsByLevel(MaterialProfile material)
        {
            var traits = new List<MatTrait>();
            for (int i = 1; i < material.Levels.Length; i++)
            {
                if (material.Levels[i] > 0 && (MatTrait)i != material.Trace) traits.Add((MatTrait)i);
            }

            traits.Sort((a, b) => material.Levels[(int)b].CompareTo(material.Levels[(int)a]));
            if (material.Trace != MatTrait.None) traits.Add(material.Trace);
            return traits;
        }

        // ---------------- the badge on a preparation's icon ----------------

        /// <summary>The effect of a preparation's key, or <see cref="BioEffect.None"/> for any other item.</summary>
        public static BioEffect PreparationEffect(string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey) || itemKey.IndexOf(ItemKey.Separator) < 0)
            {
                return BioEffect.None; // a plain key carries no compound
            }

            if (!PreparationEffects.TryGetValue(itemKey, out var effect))
            {
                effect = BioItems.CompoundOf(itemKey)?.Effect ?? BioEffect.None;
                PreparationEffects[itemKey] = effect;
            }

            return effect;
        }

        /// <summary>
        /// Keeps the effect badge on an item icon in step with the item it shows: a preparation carries its effect's
        /// icon in the lower right corner — two preparations of one family share a tint, the badge tells them apart —
        /// and any other item carries none. Cheap to call on every refresh: it only touches the badge when the key changed.
        /// </summary>
        public static void SyncBadge(Graphic icon, string itemKey)
        {
            if (icon == null) return;
            var effect = PreparationEffect(itemKey);
            var existing = icon.transform.Find(BioBadge.ObjectName);
            if (effect == BioEffect.None)
            {
                if (existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
                return;
            }

            var badge = existing != null ? existing.GetComponent<BioBadge>() : BioBadge.Create(icon.transform, BadgePlate);
            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);
            if (badge.Effect == effect) return;
            badge.Effect = effect;
            badge.Glyph.sprite = Find(BioSymbols.EffectIcon(effect)) ?? UiKit.DiscSprite;
            badge.Glyph.color = Color.Lerp(EffectColor(effect), Color.white, 0.4f); // lighter than the tint: it sits on the dark disc
        }

        /// <summary>Forgets the cached preparation effects — with the icon caches, on leaving a session.</summary>
        public static void ClearCache() => PreparationEffects.Clear();
    }
}
