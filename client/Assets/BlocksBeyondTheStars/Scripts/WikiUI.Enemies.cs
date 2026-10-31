// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Codex's Enemies chapter with pictures (#3032, Justus: "pictures with the enemies in the enemy list"): the head,
    /// then per group its heading and one row per enemy — its portrait on the left (<see cref="EnemyPortraitRig"/> renders it
    /// from the game's own model), the entry's text (<see cref="EnemyCodexText.Entry"/>) beside it.
    /// </summary>
    public sealed partial class WikiUI
    {
        private const float PortraitW = 192f, PortraitH = 144f, PortraitGap = 18f;

        private float RenderEnemies(Transform content, float textW, float y)
        {
            var enemies = Game.Content.Enemies;
            var met = Game.EnemiesMet;
            y = AddTextBlock(content, textW, y, EnemyCodexText.Head(enemies, met, L));
            foreach (var (where, list) in EnemyCodexText.Groups(enemies))
            {
                y = AddTextBlock(content, textW, y, EnemyCodexText.GroupHeading(where, L));
                foreach (var e in list)
                {
                    y = AddEnemyRow(content, textW, y, e.Key, EnemyCodexText.Entry(e, met.Contains(e.Key), L, ItemNameOf));
                }
            }

            return y;
        }

        /// <summary>One enemy: the framed portrait, the text to its right; the row is as tall as the taller of the two.</summary>
        private float AddEnemyRow(Transform content, float textW, float y, string enemyKey, string entry)
        {
            var frame = UiKit.AddPanel(content, Pad, y, PortraitW + 8f, PortraitH + 8f, new Color(0.08f, 0.14f, 0.22f, 1f));
            var pic = new GameObject("Portrait_" + enemyKey, typeof(RectTransform));
            pic.transform.SetParent(frame.transform, false);
            UiKit.Place(pic, 4f, 4f, PortraitW, PortraitH);
            pic.AddComponent<RawImage>().texture = EnemyPortraitRig.Portrait(transform, enemyKey);

            float textX = Pad + PortraitW + 8f + PortraitGap;
            float w = textW - (textX - Pad);
            var t = UiKit.AddText(content, textX, y, w, 100f, entry, 18, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float textH = t.preferredHeight;
            ((RectTransform)t.transform).sizeDelta = new Vector2(w, textH + 10f);
            return y + Mathf.Max(PortraitH + 8f, textH) + 14f;
        }

        private string ItemNameOf(string itemKey) => L(Game.Content.GetItem(itemKey)?.NameKey ?? itemKey);
    }
}
