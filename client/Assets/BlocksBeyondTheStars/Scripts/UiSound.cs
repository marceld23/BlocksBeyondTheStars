// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Lightweight UI click/hover sounds (procedural), available everywhere — including the shell
    /// menus where there is no <see cref="ClientAudio"/>. Owns one persistent 2D audio source.
    /// <see cref="UiKit"/> buttons play <see cref="Click"/> on press and <see cref="Hover"/> on
    /// pointer-enter (via <see cref="UiHover"/>). Volume tracks master×SFX (set by AppShell).
    /// </summary>
    public static class UiSound
    {
        private static AudioSource _src;
        private static AudioClip _click, _hover;

        /// <summary>Overall UI-sound volume (master × SFX), refreshed by the shell.</summary>
        public static float Volume = 0.5f;

        private static void Ensure()
        {
            if (_src != null)
            {
                return;
            }

            var go = new GameObject("UiSound");
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
            _click = ProceduralAudio.Generate("ui_click");
            _hover = ProceduralAudio.Generate("ui_hover");
        }

        public static void Click()
        {
            Ensure();
            if (_click != null && Volume > 0.001f)
            {
                _src.PlayOneShot(_click, Volume);
            }
        }

        public static void Hover()
        {
            Ensure();
            if (_hover != null && Volume > 0.001f)
            {
                _src.PlayOneShot(_hover, Volume * 0.6f);
            }
        }
    }

    /// <summary>Plays a UI hover blip when the pointer enters the element (added to UiKit buttons).</summary>
    public sealed class UiHover : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData) => UiSound.Hover();
    }
}
