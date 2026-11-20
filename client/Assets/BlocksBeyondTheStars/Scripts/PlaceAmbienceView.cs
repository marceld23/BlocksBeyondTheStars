// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: the sound layer of the place the player stands in — a quiet bed of far voices, steps and a door
    /// under the weather, busier on a market day, a deck's hum aboard a station. <see cref="PlaceAmbience"/> says which loop
    /// and how loud (by the place's size and the time of day); this view plays it on two voices that cross-fade, at the
    /// ambience level like every bed, softer under a roof and silent in a vacuum. The single sources of a place — the
    /// tavern, the workshop, the well — are positional emitters (<see cref="SoundEmitterView"/>), not this bed.
    /// </summary>
    public sealed class PlaceAmbienceView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The bed's share of the ambience level: it sits under the weather and the biome, never over them.</summary>
        private const float Share = 0.42f;

        /// <summary>What is left of the bed under a roof (inside a house the village is outside the window).</summary>
        private const float RoofedShare = 0.5f;

        private const float FadePerSecond = 0.35f;

        private readonly AudioSource[] _voices = new AudioSource[2];
        private readonly string[] _loops = { string.Empty, string.Empty };
        private int _live;

        private void Awake()
        {
            for (int v = 0; v < _voices.Length; v++)
            {
                var go = new GameObject("PlaceAmbience" + v);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.volume = 0f;
                src.spatialBlend = 0f; // a bed, not a place in the room
                _voices[v] = src;
            }
        }

        private void Update()
        {
            var audio = ClientAudio.Instance;
            if (Game == null || audio == null)
            {
                return;
            }

            float time = Game.LocalTimeOfDay;
            var place = Audible(audio) ? Game.CurrentPlace : null;
            string loop = PlaceAmbience.LoopFor(place, time);
            float level = loop.Length == 0 ? 0f : PlaceAmbience.LevelFor(place, time) * Share * audio.BedLevel;
            if (level > 0f && !Game.ExposedToSky && string.IsNullOrEmpty(Game.StationName))
            {
                level *= RoofedShare;
            }

            if (loop.Length > 0 && _loops[_live] != loop)
            {
                Switch(audio, loop);
            }

            if (loop.Length > 0 && _loops[_live] == loop)
            {
                _voices[_live].pitch = PlaceAmbience.PitchFor(place); // a station's hum by its size and purpose; else 1
            }

            for (int v = 0; v < _voices.Length; v++)
            {
                var src = _voices[v];
                float target = v == _live && _loops[v] == loop ? level : 0f;
                src.volume = Mathf.MoveTowards(src.volume, target, Time.deltaTime * FadePerSecond);
                if (src.isPlaying && src.volume <= 0f && target <= 0f)
                {
                    src.Stop();
                }
                else if (!src.isPlaying && target > 0f && src.clip != null)
                {
                    src.Play();
                }
            }
        }

        /// <summary>Hands the bed to the other voice with the new loop; the old one fades out under it.</summary>
        private void Switch(ClientAudio audio, string loop)
        {
            var clip = audio.Clip(loop);
            if (clip == null)
            {
                return; // a loop that is not bundled stays silent (and the old one fades with its place)
            }

            _live = 1 - _live;
            _loops[_live] = loop;
            var src = _voices[_live];
            src.Stop();
            src.clip = clip;
            src.volume = 0f;
            src.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
        }

        private bool Audible(ClientAudio audio)
            => Game.Environment != null && Game.World != null && !Game.InSpace && !Game.SpaceViewActive
               && !Game.InEva && !audio.InVacuum;
    }
}
