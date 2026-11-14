// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3580: the browser build releases long decoded clips after a move to another place. A browser decodes every clip on
    /// its first play and keeps the decoded buffer outside the WebAssembly heap (Unity 6.6 does so for every clip on
    /// Chromium, whatever its load type), and this router survives travel — so the decoded set grew with every planet. The
    /// rules (which moves count, which clips go) are <see cref="AudioReleaseRules"/>. Native builds keep everything: memory
    /// is not their constraint, and a released clip comes back with a late first play.
    /// </summary>
    public sealed partial class ClientAudio
    {
        /// <summary>The clips loaded from <c>Resources/audio</c> — the only ones whose data can be dropped and decoded again
        /// (a code-created clip has nothing to reload from).</summary>
        private readonly HashSet<AudioClip> _bundled = new HashSet<AudioClip>();

        private string _placeKey;          // the last place the player arrived at (never the own ship's interior)
        private bool _releasePending;
        private float _releaseReadyFor;    // seconds the new world has been ready

        private static bool ReleasesDecodedClips => Application.platform == RuntimePlatform.WebGLPlayer;

        private void OnPlaceReset(WorldReset m)
        {
            string next = AudioReleaseRules.PlaceKey(m.PlanetType, m.PlanetName, m.SystemName);
            if (next == null)
            {
                return; // the ship's interior: entered and left all the time, the place outside stays the same
            }

            bool moved = AudioReleaseRules.IsMove(_placeKey, next);
            _placeKey = next;
            if (moved && ReleasesDecodedClips)
            {
                _releasePending = true;
                _releaseReadyFor = 0f;
            }
        }

        /// <summary>Releases once the new world has been ready for <see cref="AudioReleaseRules.SettleSeconds"/>: its beds and
        /// loops hold their clips by then, and an arrival cue has played out.</summary>
        private void TickAudioRelease(float dt)
        {
            if (!_releasePending)
            {
                return;
            }

            if (Game == null || !Game.WorldReady)
            {
                _releaseReadyFor = 0f;
                return;
            }

            _releaseReadyFor += dt;
            if (_releaseReadyFor >= AudioReleaseRules.SettleSeconds)
            {
                _releasePending = false;
                ReleaseIdleClips(AudioReleaseRules.MinDecodedBytes, "a move to another place");
            }
        }

        /// <summary>Diagnostics and the browser check (<c>unityInstance.SendMessage("Game", "ReleaseIdleAudioNow", "")</c>):
        /// releases now by the normal rule; <c>"all"</c> drops the size floor, so every decoded clip no source holds goes;
        /// <c>"decode"</c> releases nothing but asks every bundled clip to decode, so a check can measure what a release frees.</summary>
        public void ReleaseIdleAudioNow(string mode)
        {
            if (mode == "decode")
            {
                int asked = 0;
                foreach (var clip in _bundled)
                {
                    if (clip != null && clip.loadState == AudioDataLoadState.Unloaded && clip.LoadAudioData())
                    {
                        asked++;
                    }
                }

                Debug.Log($"[Audio] Asked {asked} bundled clips to decode (check).");
                return;
            }

            ReleaseIdleClips(mode == "all" ? 0L : AudioReleaseRules.MinDecodedBytes, "a request");
        }

        private void ReleaseIdleClips(long minBytes, string reason)
        {
            // A clip any source holds — playing, paused, a loop at volume 0, a bed between fades — stays.
            var held = new HashSet<AudioClip>();
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
            {
                if (src.clip != null)
                {
                    held.Add(src.clip);
                }
            }

            int count = 0;
            long bytes = 0;
            foreach (var clip in _bundled)
            {
                if (clip == null)
                {
                    continue;
                }

                long size = AudioReleaseRules.DecodedBytes(clip.samples, clip.channels);
                if (AudioReleaseRules.ShouldRelease(clip.loadState == AudioDataLoadState.Loaded, held.Contains(clip), size, minBytes)
                    && clip.UnloadAudioData())
                {
                    count++;
                    bytes += size;
                }
            }

            _bed?.DropIdleCopies(); // the browser's muffled copies of the old place's beds
            _night?.DropIdleCopies();
            Debug.Log($"[Audio] Released {count} decoded clips (~{bytes / 1048576f:F1} MiB) after {reason}.");
        }
    }
}
