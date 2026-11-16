// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The effect badge of a preparation's icon (see <see cref="BioIcons.SyncBadge"/>): a dark disc in the
    /// icon's lower right quarter with the effect's icon on it.</summary>
    public sealed class BioBadge : MonoBehaviour
    {
        public const string ObjectName = "bio_badge";

        public BioEffect Effect;
        public Image Glyph;

        public static BioBadge Create(Transform icon, Color plate)
        {
            var go = new GameObject(ObjectName, typeof(RectTransform));
            go.transform.SetParent(icon, false);
            Stretch(go.GetComponent<RectTransform>(), 0.46f, 0f, 1.04f, 0.58f);
            var disc = go.AddComponent<Image>();
            disc.sprite = UiKit.DiscSprite;
            disc.color = plate;
            disc.raycastTarget = false;

            var glyphGo = new GameObject("glyph", typeof(RectTransform));
            glyphGo.transform.SetParent(go.transform, false);
            Stretch(glyphGo.GetComponent<RectTransform>(), 0.16f, 0.16f, 0.84f, 0.84f);
            var glyph = glyphGo.AddComponent<Image>();
            glyph.raycastTarget = false;

            var badge = go.AddComponent<BioBadge>();
            badge.Glyph = glyph;
            return badge;
        }

        private static void Stretch(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
