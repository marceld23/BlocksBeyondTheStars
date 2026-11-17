// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The interface's own sounds, available everywhere — including the shell menus where there is no
    /// <see cref="ClientAudio"/>. Owns one persistent 2D audio source. Each sound is the bundled recording
    /// (<c>Resources/audio/ui_*.mp3</c>) when there is one, else the code-made stand-in from
    /// <see cref="ProceduralAudio"/>, so the interface is never mute without assets.
    /// <see cref="UiKit"/> buttons play <see cref="Click"/> on press and <see cref="Hover"/> on pointer-enter (via
    /// <see cref="UiHover"/>); <see cref="UiPanel"/> plays <see cref="Open"/> and <see cref="Close"/>.
    /// Volume tracks SFX × the interface slider (set by AppShell; the master is the listener's).
    /// </summary>
    public static class UiSound
    {
        private static AudioSource _src;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        /// <summary>Overall UI-sound volume (SFX × interface), refreshed by the shell.</summary>
        public static float Volume = 0.5f;

        private static void Ensure()
        {
            if (_src != null)
            {
                return;
            }

            var go = new GameObject("UiSound");
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(go); // play mode only — it throws in edit mode, where EditMode tests click buttons
            }

            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;
        }

        private static AudioClip Clip(string id)
        {
            if (!Clips.TryGetValue(id, out var clip) || clip == null)
            {
                clip = Resources.Load<AudioClip>("audio/" + id);
                if (clip == null)
                {
                    clip = ProceduralAudio.Generate(id);
                }

                Clips[id] = clip; // null is remembered too: a sound without a recording or a stand-in stays silent
            }

            return clip;
        }

        /// <summary>Plays the interface sound <paramref name="id"/> at <paramref name="gain"/> of the UI volume.</summary>
        public static void Play(string id, float gain = 1f)
        {
            if (Volume <= 0.001f)
            {
                return;
            }

            Ensure();
            var clip = Clip(id);
            if (clip != null)
            {
                _src.PlayOneShot(clip, Mathf.Clamp01(Volume * gain));
            }
        }

        public static void Click() => Play("ui_click");

        /// <summary>The pointer enters an element. The most frequent sound of the interface, so it sits well under the
        /// click and never rattles: sweeping the pointer across a row of buttons ticks at most every
        /// <see cref="HoverGapSeconds"/>.</summary>
        public static void Hover()
        {
            float now = Time.unscaledTime;
            if (now - _lastHover < HoverGapSeconds && now >= _lastHover)
            {
                return;
            }

            _lastHover = now;
            Play("ui_hover", HoverGain);
        }

        /// <summary>The hover tick's share of the UI volume (the click plays at 1).</summary>
        private const float HoverGain = 0.3f;

        /// <summary>The least time between two hover ticks, seconds.</summary>
        private const float HoverGapSeconds = 0.07f;

        private static float _lastHover = -1f;

        /// <summary>A window opens.</summary>
        public static void Open() => Play("ui_open", 0.8f);

        /// <summary>A window closes.</summary>
        public static void Close() => Play("ui_close", 0.8f);

        /// <summary>A tab or page changes.</summary>
        public static void Tab() => Play("ui_tab");
    }

    /// <summary>Plays a UI hover blip when the pointer enters the element (added to UiKit buttons).</summary>
    public sealed class UiHover : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData) => UiSound.Hover();
    }
}
