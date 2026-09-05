// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// SDF text for the HUD (TextMeshPro, which ships inside the uGUI 2.0 package). The font asset is built at
    /// RUNTIME from the bundled Rajdhani TTF (dynamic atlas, glyphs rasterised on demand), with the Noto fonts
    /// as dynamic fallbacks for the non-Latin locales — no baked font asset, in keeping with "everything in
    /// code" (ADR 0002). Three looks share the one atlas: plain, a soft dark underlay (readable over bright
    /// terrain — replaces the vertex-copying <see cref="UiOutline"/>) and a cyan hologram glow.
    /// Legacy <c>UnityEngine.UI.Text</c> stays in use for the menus; only the diegetic HUD moved here.
    /// </summary>
    public static class UiText
    {
        public enum Look { Plain, Outline, Glow }

        private static TMP_FontAsset _font;
        private static Material _outlineMat, _glowMat;

        /// <summary>The runtime SDF font asset (Rajdhani + Noto fallbacks). Created on first use.</summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }

                var ttf = UiKit.Font;
                _font = Create(ttf, 1024);
                if (_font == null)
                {
                    // No font face (font data stripped?) — TMP already logged why. Fall back to TMP's default so
                    // the HUD still has text; it just isn't Rajdhani.
                    Debug.LogWarning("[UiText] runtime SDF font creation failed — using the TMP default font asset.");
                    _font = TMP_Settings.defaultFontAsset;
                    return _font;
                }

                _font.name = "Rajdhani SDF (runtime)";
                // A runtime-created font asset has NO fallback list yet (null, not empty) — create it.
                _font.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
                // Fallbacks: Noto Sans (Cyrillic/Greek/extended Latin) first, then the CJK faces. Dynamic atlases
                // cost one 1024² Alpha8 texture each (1 MB) and only fill when a locale actually needs them.
                foreach (var name in new[] { "fonts/NotoSans-Medium", "fonts/NotoSansJP-Medium", "fonts/NotoSansKR-Medium", "fonts/NotoSansSC-Medium" })
                {
                    var fb = Resources.Load<Font>(name);
                    if (fb == null)
                    {
                        continue;
                    }

                    var fbAsset = Create(fb, 1024);
                    if (fbAsset == null)
                    {
                        continue;
                    }

                    fbAsset.name = fb.name + " SDF (runtime fallback)";
                    _font.fallbackFontAssetTable.Add(fbAsset);
                }

                return _font;
            }
        }

        private static TMP_FontAsset Create(Font ttf, int atlas)
            => TMP_FontAsset.CreateFontAsset(ttf, 72, 8, GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);

        /// <summary>Soft dark underlay — the readability look for text drawn straight over the world.</summary>
        public static Material OutlineMaterial
        {
            get
            {
                if (_outlineMat == null)
                {
                    _outlineMat = new Material(Font.material) { name = "Rajdhani SDF Underlay", hideFlags = HideFlags.HideAndDontSave };
                    _outlineMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    _outlineMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0.02f, 0.06f, 0.85f));
                    _outlineMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
                    _outlineMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
                    _outlineMat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
                    _outlineMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.45f);
                }

                return _outlineMat;
            }
        }

        /// <summary>Cyan hologram glow around the glyphs (titles, headline numbers).</summary>
        public static Material GlowMaterial
        {
            get
            {
                if (_glowMat == null)
                {
                    _glowMat = new Material(Font.material) { name = "Rajdhani SDF Glow", hideFlags = HideFlags.HideAndDontSave };
                    _glowMat.EnableKeyword(ShaderUtilities.Keyword_Glow);
                    _glowMat.SetColor(ShaderUtilities.ID_GlowColor, new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.55f));
                    _glowMat.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
                    _glowMat.SetFloat(ShaderUtilities.ID_GlowInner, 0.06f);
                    _glowMat.SetFloat(ShaderUtilities.ID_GlowOuter, 0.30f);
                    _glowMat.SetFloat(ShaderUtilities.ID_GlowPower, 0.75f);
                    // A whisper of underlay too, so glowing titles stay legible over a bright sky.
                    _glowMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                    _glowMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0.02f, 0.06f, 0.6f));
                    _glowMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.25f);
                    _glowMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.25f);
                    _glowMat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
                    _glowMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
                }

                return _glowMat;
            }
        }

        /// <summary>Maps the legacy anchor enum the call sites use onto TMP's alignment.</summary>
        public static TextAlignmentOptions Align(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Left,
        };

        /// <summary>
        /// Adds an SDF label at a top-left anchored rect — the TMP twin of <see cref="UiKit.AddText"/> (same
        /// argument order, same defaults: single line, overflow, no raycast). Bold titles get a touch of
        /// tracking; <see cref="Look.Outline"/> / <see cref="Look.Glow"/> pick the shared effect materials.
        /// </summary>
        public static TextMeshProUGUI Add(Transform parent, float x, float y, float w, float h, string text, float size,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, Look look = Look.Plain)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, w, h);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.text = text ?? string.Empty;
            t.fontSize = size;
            t.color = color;
            t.alignment = Align(anchor);
            t.fontStyle = style == FontStyle.Bold || style == FontStyle.BoldAndItalic ? FontStyles.Bold : FontStyles.Normal;
            if (style == FontStyle.Italic || style == FontStyle.BoldAndItalic)
            {
                t.fontStyle |= FontStyles.Italic;
            }

            t.characterSpacing = style == FontStyle.Bold ? 1.5f : 0.5f;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            Style(t, look);
            return t;
        }

        /// <summary>Switches a label between the plain / underlay / glow materials.</summary>
        public static void Style(TMP_Text t, Look look)
        {
            if (t == null)
            {
                return;
            }

            switch (look)
            {
                case Look.Outline:
                    t.fontSharedMaterial = OutlineMaterial;
                    t.extraPadding = true;
                    break;
                case Look.Glow:
                    t.fontSharedMaterial = GlowMaterial;
                    t.extraPadding = true;
                    break;
                default:
                    t.fontSharedMaterial = Font.material;
                    break;
            }
        }

        /// <summary>Multi-line mode: wrap inside the rect and clip lines that don't fit (the legacy
        /// <c>Wrap + Truncate</c> pairing the HUD panels used).</summary>
        public static void Wrap(TMP_Text t, bool truncate = false)
        {
            if (t == null)
            {
                return;
            }

            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = truncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
        }
    }
}
