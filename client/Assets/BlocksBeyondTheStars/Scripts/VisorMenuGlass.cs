// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A flat "visor glass" overlay for a menu: the same helmet styling as the diegetic HUD's
    /// <c>BlocksBeyondTheStars/Visor</c> pass — a cyan rim glow, faint animated scanlines and a top glass glint — but
    /// WITHOUT the barrel curvature, so the menu reads as "inside the helmet" while its buttons stay exactly
    /// where they're drawn (clicks aren't displaced). Drawn additively over the already-built menu as a
    /// click-through full-screen quad. Only added when the visor pipeline is actually active.
    /// </summary>
    public sealed class VisorMenuGlass : MonoBehaviour
    {
        private Material _mat;

        /// <summary>Adds the glass overlay as the top-most child of a menu canvas root. No-op when there is no
        /// visor (keeps the menu flat) or the shader was stripped from the build.</summary>
        public static void Add(Transform menuRoot)
        {
            if (UiKit.HudCamera == null)
            {
                return; // no visor pipeline → keep the menu flat, like the HUD
            }

            var shader = Shader.Find("BlocksBeyondTheStars/VisorGlass");
            if (shader == null)
            {
                Debug.LogWarning("[VisorMenuGlass] BlocksBeyondTheStars/VisorGlass shader not found; menu stays flat.");
                return;
            }

            var go = new GameObject("VisorGlass", typeof(RectTransform));
            go.transform.SetParent(menuRoot, false);
            go.transform.SetAsLastSibling(); // draw over all the menu content

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.raycastTarget = false; // clicks pass straight through to the menu underneath
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            img.material = mat;

            go.AddComponent<VisorMenuGlass>()._mat = mat;
        }

        private void Update()
        {
            if (_mat == null)
            {
                return;
            }

            _mat.SetFloat("_VisorTime", Time.unscaledTime); // animate while the game is paused behind the menu
            _mat.SetFloat("_Aspect", Screen.height > 0 ? (float)Screen.width / Screen.height : 1.78f);
        }

        private void OnDestroy()
        {
            if (_mat != null)
            {
                Destroy(_mat);
            }
        }
    }
}
