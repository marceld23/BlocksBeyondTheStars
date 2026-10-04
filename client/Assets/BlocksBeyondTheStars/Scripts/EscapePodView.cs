// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A drifting life pod (#2241) — the model is built by <c>SpaceView</c>; this makes it alive: a slow tumble, the
    /// little figure in the porthole waving, the orange beacon blinking, a thin SOS ring every two seconds that can be
    /// seen from far away, and a soft positional beep (<c>pod_beacon</c>). Purely cosmetic; flying close is the rescue.
    /// </summary>
    public sealed class EscapePodView : MonoBehaviour
    {
        private static readonly Color SosColor = new Color(1f, 0.55f, 0.15f);

        private Transform _body;
        private Renderer _beacon;
        private Transform _arm;
        private float _phase;
        private float _ringTimer;
        private AudioSource _beep;
        private ClientSettings _settings;
        private Vector3 _tumbleAxis;

        public void Init(Transform body, Renderer beacon, Transform arm, ClientSettings settings, string id)
        {
            _body = body;
            _beacon = beacon;
            _arm = arm;
            _settings = settings;
            int h = (id ?? string.Empty).GetHashCode();
            _phase = (h & 0xFFFF) / 65535f * 10f;
            _tumbleAxis = new Vector3(0.3f + (h & 7) * 0.05f, 1f, 0.2f).normalized;
            _ringTimer = 0.5f;

            var clip = Resources.Load<AudioClip>("audio/pod_beacon") ?? ProceduralAudio.Generate("pod_beacon");
            if (clip != null)
            {
                _beep = gameObject.AddComponent<AudioSource>();
                _beep.clip = clip;
                _beep.loop = true;
                _beep.spatialBlend = 1f;
                _beep.rolloffMode = AudioRolloffMode.Linear;
                _beep.minDistance = 4f;
                _beep.maxDistance = 70f;
                _beep.dopplerLevel = 0f;
                _beep.volume = 0.35f * (_settings?.SfxVolume ?? 0.8f);
                _beep.Play();
            }
        }

        private void Update()
        {
            float t = Time.time + _phase;
            if (_body != null)
            {
                _body.localRotation = Quaternion.AngleAxis(t * 9f, _tumbleAxis);
                _body.localPosition = new Vector3(0f, Mathf.Sin(t * 0.7f) * 0.25f, 0f);
            }

            if (_arm != null)
            {
                _arm.localRotation = Quaternion.Euler(Mathf.Sin(t * 6f) * 35f, 0f, 0f); // hello over there!
            }

            bool on = Mathf.Repeat(t, 1f) < 0.3f;
            if (_beacon != null && _beacon.enabled != on)
            {
                _beacon.enabled = on;
                if (on && _beacon.transform != null)
                {
                    FxLights.Flash(_beacon.transform.position, SosColor, 1.2f, 9f, 0.28f);
                }
            }

            _ringTimer -= Time.deltaTime;
            if (_ringTimer <= 0f)
            {
                _ringTimer = 2f;
                FxKit.Ring(transform.position, Vector3.zero, SosColor, 1f, 12f, 1.4f, thickness: 0.05f, fill: 0.01f, intensity: 1.8f);
            }

            if (_beep != null)
            {
                _beep.volume = 0.35f * (_settings?.SfxVolume ?? 0.8f);
            }
        }
    }
}
