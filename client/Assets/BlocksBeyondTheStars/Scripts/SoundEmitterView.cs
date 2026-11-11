// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3494: the world's sound emitters — a place that plays a loop which gets louder as the player comes closer (a
    /// statue that sings across a valley). The server names them (<see cref="GameBootstrap.SoundEmitters"/>, sent on world
    /// entry, so a late joiner hears them too); <see cref="SoundEmitterMix"/> picks the loudest
    /// <see cref="SoundEmitterMix.MaxVoices"/> four times a second, one per sound, and each plays on a pooled positional
    /// voice that fades in and out. The mix fades by distance itself — Unity only pans. In space, in a cabin, on a station
    /// and under a cutscene's duck the voices fall silent.
    /// </summary>
    public sealed class SoundEmitterView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float PickSeconds = 0.25f;
        private const float FadePerSecond = 0.8f;

        private readonly List<SoundEmitterMix.Emitter> _emitters = new List<SoundEmitterMix.Emitter>();
        private readonly List<SoundEmitterMix.Voice> _picks = new List<SoundEmitterMix.Voice>();
        private readonly AudioSource[] _voices = new AudioSource[SoundEmitterMix.MaxVoices];
        private readonly float[] _targets = new float[SoundEmitterMix.MaxVoices];
        private float _pickTimer;

        private void Awake()
        {
            for (int v = 0; v < _voices.Length; v++)
            {
                var go = new GameObject("SoundEmitter" + v);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.volume = 0f;
                src.spatialBlend = 0.85f;
                src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 4000f; // the mix fades by distance itself; Unity only pans
                src.maxDistance = 4001f;
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

            _pickTimer -= Time.deltaTime;
            if (_pickTimer <= 0f)
            {
                _pickTimer = PickSeconds;
                Repick(audio);
            }

            float level = audio.BedLevel;
            for (int v = 0; v < _voices.Length; v++)
            {
                var src = _voices[v];
                src.volume = Mathf.MoveTowards(src.volume, _targets[v] * level, Time.deltaTime * FadePerSecond);
                if (src.isPlaying && src.volume <= 0f && _targets[v] <= 0f)
                {
                    src.Stop();
                }
            }
        }

        /// <summary>One pick: which emitters play, and on which voice. A voice that already plays a picked sound keeps it,
        /// so a loop never restarts while the player walks about.</summary>
        private void Repick(ClientAudio audio)
        {
            for (int v = 0; v < _targets.Length; v++)
            {
                _targets[v] = 0f;
            }

            var sources = Game.SoundEmitters;
            if (sources == null || sources.Length == 0 || !Audible())
            {
                return;
            }

            _emitters.Clear();
            foreach (var e in sources)
            {
                var scene = Game.ScenePos(e.X, e.Y, e.Z); // the copy nearest the player across the world's seam
                _emitters.Add(new SoundEmitterMix.Emitter(scene.x, scene.y, scene.z, e.Sound, e.Radius, e.Volume));
            }

            var listener = Camera != null ? Camera.transform.position : Game.PlayerPosition;
            SoundEmitterMix.Pick(_emitters, listener.x, listener.y, listener.z, _picks);
            var taken = new bool[_voices.Length];
            foreach (var pick in _picks)
            {
                var emitter = _emitters[pick.Emitter];
                var clip = audio.Clip(emitter.Sound);
                if (clip == null)
                {
                    continue; // a sound that is not bundled stays silent
                }

                int voice = VoiceFor(clip, taken);
                if (voice < 0)
                {
                    continue;
                }

                taken[voice] = true;
                var src = _voices[voice];
                src.transform.position = new Vector3(emitter.X, emitter.Y, emitter.Z);
                if (src.clip != clip)
                {
                    src.clip = clip;
                    src.volume = 0f;
                    src.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
                }

                if (!src.isPlaying)
                {
                    src.Play();
                }

                _targets[voice] = pick.Level;
            }
        }

        /// <summary>The voice that already holds the clip, else a silent free one, else none.</summary>
        private int VoiceFor(AudioClip clip, bool[] taken)
        {
            for (int v = 0; v < _voices.Length; v++)
            {
                if (!taken[v] && _voices[v].clip == clip)
                {
                    return v;
                }
            }

            for (int v = 0; v < _voices.Length; v++)
            {
                if (!taken[v] && (_voices[v].clip == null || _voices[v].volume <= 0.01f))
                {
                    return v;
                }
            }

            return -1;
        }

        private bool Audible()
            => Game.Environment != null && Game.World != null && !Game.InSpace && !Game.SpaceViewActive
               && !Game.InEva && !Game.OnFootInSpace && string.IsNullOrEmpty(Game.StationName);
    }
}
