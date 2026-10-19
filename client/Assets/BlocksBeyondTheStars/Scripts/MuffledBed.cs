// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One looping 2D ambience layer of <see cref="ClientAudio"/> that can be heard through a roof (#2805, #2825): the
    /// weather/biome bed and the night layer. Muffled, the layer drops to <see cref="RoofLevel"/> of its open-air level and
    /// loses its highs — rain on the hull instead of silence. On the desktop a low-pass filter on the layer's OWN child
    /// object sweeps down (a filter on the ClientAudio object would muffle every cue on it), and it takes over the
    /// underwater muffle the bus filter gives the sources on that object. Unity Web ignores audio filter components (#878),
    /// so there a muffled copy of the clip is baked once (<see cref="SampleKit.MuffledLoop"/>) and cross-faded in on a twin
    /// source; a clip that cannot be read back (not Decompress-On-Load in the browser) plays dry at a lower level instead.
    /// A layer whose level is silent stops its sources, so a quiet layer costs no decoding.
    /// </summary>
    internal sealed class MuffledBed
    {
        /// <summary>Muffled, the layer plays at this share of its open-air level (#2805: "about a third").</summary>
        public const float RoofLevel = 0.33f;

        /// <summary>The low-pass cutoff of the muffled layer (Hz): the rain's patter and the wind's body stay, the hiss goes.</summary>
        public const float RoofCutoff = 1000f;

        private const float UnderwaterCutoff = 680f;  // the same cutoff as the bus filter (ClientAudio)
        private const float OpenCutoff = 22000f;
        private const float CutoffSweepPerSecond = 45000f;
        private const float RoofFadeSeconds = 0.8f;   // stepping in or out of a doorway
        private const float DryOnlyRoofLevel = 0.2f;  // web, a clip that cannot be baked: only quieter
        private const int MaxBaked = 8;

        private static bool BakeMuffle => Application.platform == RuntimePlatform.WebGLPlayer;

        private readonly AudioSource _dry;
        private readonly AudioSource _muffled;        // web only: the baked copy, cross-faded in
        private readonly AudioLowPassFilter _filter;  // desktop only
        private readonly Dictionary<AudioClip, AudioClip> _baked = new Dictionary<AudioClip, AudioClip>(); // null = cannot be baked
        private float _level;   // the faded open-air level
        private float _roof;    // 0 open air … 1 muffled, eased
        private float _cutoff = OpenCutoff;

        public MuffledBed(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            _dry = LoopSource(go);
            if (BakeMuffle)
            {
                _muffled = LoopSource(go);
            }
            else
            {
                _filter = go.AddComponent<AudioLowPassFilter>();
                _filter.cutoffFrequency = OpenCutoff;
                _filter.lowpassResonanceQ = 1f;
            }
        }

        /// <summary>The layer's loop (null = none).</summary>
        public AudioClip Clip => _dry.clip;

        /// <summary>The faded open-air level (0 = silent) — the roof scales it down on top.</summary>
        public float Level => _level;

        /// <summary>Switches the loop; the level restarts from silence and fades up in <see cref="Tick"/>.</summary>
        public void SetClip(AudioClip clip)
        {
            _dry.Stop();
            _dry.clip = clip;
            _dry.volume = 0f;
            _level = 0f;
            if (_muffled != null)
            {
                _muffled.Stop();
                _muffled.clip = null;
                _muffled.volume = 0f;
            }
        }

        /// <summary>One frame: fades the level toward <paramref name="target"/> at <paramref name="fadePerSecond"/>, the roof
        /// toward <paramref name="muffled"/>, and (desktop) the cutoff toward the underwater one while
        /// <paramref name="submerged"/>.</summary>
        public void Tick(float target, float fadePerSecond, bool muffled, bool submerged, float dt)
        {
            if (_dry.clip == null)
            {
                return;
            }

            _level = Mathf.MoveTowards(_level, target, dt * fadePerSecond);
            _roof = Mathf.MoveTowards(_roof, muffled ? 1f : 0f, dt / RoofFadeSeconds);
            if (_level <= 0f && target <= 0f)
            {
                Silence();
                return;
            }

            if (!_dry.isPlaying)
            {
                _dry.Play();
            }

            if (_filter != null)
            {
                // Desktop: one source, its own filter. The cutoff glides on a log scale (an even-sounding sweep); under
                // water the underwater cutoff wins, as it did while this source sat under the bus filter.
                float wanted = submerged ? UnderwaterCutoff : OpenCutoff * Mathf.Pow(RoofCutoff / OpenCutoff, _roof);
                _cutoff = Mathf.MoveTowards(_cutoff, wanted, dt * CutoffSweepPerSecond);
                _filter.cutoffFrequency = _cutoff;
                _dry.volume = _level * Mathf.Lerp(1f, RoofLevel, _roof);
                return;
            }

            // Web: cross-fade to the baked copy; while it is not (or cannot be) baked, only quieter.
            var copy = _roof > 0f ? MuffledCopy(_dry.clip) : null;
            if (copy == null)
            {
                if (_muffled.isPlaying)
                {
                    _muffled.Stop();
                }

                _dry.volume = _level * Mathf.Lerp(1f, DryOnlyRoofLevel, _roof);
                return;
            }

            if (_muffled.clip != copy)
            {
                _muffled.clip = copy;
            }

            if (!_muffled.isPlaying)
            {
                _muffled.Play();
            }

            _dry.volume = _level * (1f - _roof);
            _muffled.volume = _level * RoofLevel * _roof;
        }

        /// <summary>Frees the baked copies (code-created clips are never collected) — the owner's OnDestroy.</summary>
        public void Dispose()
        {
            foreach (var copy in _baked.Values)
            {
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }

            _baked.Clear();
        }

        private void Silence()
        {
            if (_dry.isPlaying)
            {
                _dry.Stop();
            }

            _dry.volume = 0f;
            if (_muffled != null && _muffled.isPlaying)
            {
                _muffled.Stop();
            }
        }

        /// <summary>The baked muffled copy of a loop, or null while it is not ready or when it cannot be made (remembered).
        /// Only a loaded Decompress-On-Load clip can be read back; an unloaded one is asked to load and tried again on a
        /// later frame.</summary>
        private AudioClip MuffledCopy(AudioClip clip)
        {
            if (_baked.TryGetValue(clip, out var done))
            {
                return done;
            }

            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.loadState == AudioDataLoadState.Failed)
            {
                _baked[clip] = null;
                return null;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }

                return null;
            }

            if (_baked.Count >= MaxBaked)
            {
                // A long session through many weathers: start over. None of them is playing — a new loop starts with the
                // twin source emptied (SetClip), and this clip has no copy yet.
                Dispose();
            }

            var copy = SampleKit.MuffledLoop(clip, RoofCutoff);
            _baked[clip] = copy;
            return copy;
        }

        private static AudioSource LoopSource(GameObject go)
        {
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }
    }
}
