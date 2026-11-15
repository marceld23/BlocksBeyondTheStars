// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    public static partial class UiKit
    {
        /// <summary>The game's name — the fallback title when the logo cannot be loaded.</summary>
        public const string GameTitle = "BLOCKS BEYOND THE STARS";

        /// <summary>
        /// The game's official logo (#3706, <see cref="GameLogo"/>: rocket icon + wordmark) fitted into the box (<paramref name="x"/>, <paramref name="y"/>,
        /// <paramref name="w"/>, <paramref name="h"/>) with its aspect kept: vertically centred, its letters flush with the box's
        /// left edge (the menu's one column axis) or centred. Without the logo texture it falls back to the extruded text
        /// title (<see cref="AddLogo"/>) in the same box. Not a raycast target.
        /// </summary>
        public static RawImage AddGameLogo(Transform parent, float x, float y, float w, float h, bool centered = false)
        {
            var tex = GameLogo.Texture;
            if (tex == null)
            {
                AddLogo(parent, x, y, w, h, GameTitle, Mathf.Clamp(Mathf.RoundToInt(h * 0.46f), 18, 72));
                return null;
            }

            float aspect = tex.width / (float)tex.height;
            float lw = Mathf.Min(w, h * aspect);
            float lh = lw / aspect;
            float margin = lw * GameLogo.MarginFraction; // transparent room left of the mark (none today)
            float lx = centered ? x + (w - lw) * 0.5f : x - margin;
            float ly = y + (h - lh) * 0.5f;

            var go = new GameObject("GameLogo", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, lx, ly, lw, lh);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }
    }
}
