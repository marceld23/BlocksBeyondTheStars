// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Flashback treatment for VEGA memory beats (#762): when a Kind-2 story line lands (a recovered
    /// memory), the world briefly reads as a recollection — light letterbox bars, a desaturating/cooling
    /// grade pulse (<see cref="UrpScenePost.SetFlashback"/>) and a chroma/grain burst — then restores
    /// over ~7 s. Purely cosmetic and client-local: input is never locked, nothing pauses, and the
    /// effect is suppressed (or wound down) while a menu, the space view or another cinematic owns the
    /// screen. Triggered by <see cref="VegaPanel"/>.
    /// </summary>
    public sealed class MemoryFlashback : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Duration = 7f;
        private const float RampIn = 0.8f;
        private const float RampOut = 1.6f;

        private CinematicFrame _frame;
        private float _t = -1f; // elapsed while active; < 0 = idle

        /// <summary>Starts (or extends) the flashback. No-op while a higher-priority screen state is up.</summary>
        public void Trigger()
        {
            if (Game == null || Suppressed())
            {
                return;
            }

            if (_t >= 0f)
            {
                _t = Mathf.Min(_t, RampIn); // already running — extend the hold, don't restart the ramp
                return;
            }

            _t = 0f;
            if (_frame == null)
            {
                _frame = CinematicFrame.Create("FlashbackFrame", 64); // below the prologue frame (65)
                _frame.transform.SetParent(transform, false);
            }

            UrpScenePost.Instance?.Burst(0.35f, 0.5f, Duration); // the static/glitch texture of the recall
        }

        private bool Suppressed()
            => Game.MenuOpen || Game.SpaceViewActive || Game.VegaPrologueActive || Game.CinematicCameraActive;

        private void Update()
        {
            if (_t < 0f)
            {
                return;
            }

            _t += Time.deltaTime;
            if (Game != null && Suppressed())
            {
                _t = Duration; // something took the screen — wind down immediately
            }

            float level = Mathf.Min(Mathf.Clamp01(_t / RampIn), Mathf.Clamp01((Duration - _t) / RampOut));
            UrpScenePost.Instance?.SetFlashback(level * 0.85f);
            _frame?.SetLetterbox(level * 0.45f);

            if (_t >= Duration)
            {
                _t = -1f;
                UrpScenePost.Instance?.SetFlashback(0f);
                _frame?.SetLetterbox(0f);
            }
        }

        private void OnDestroy()
        {
            UrpScenePost.Instance?.SetFlashback(0f);
            if (_frame != null)
            {
                Destroy(_frame.gameObject);
            }
        }
    }
}
