// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Interior damage feedback while aboard: below 50% hull the cabin sputters occasional spark
    /// bursts (rate grows with damage), below 25% a red emergency light pulses near the player and
    /// a hull alarm beeps periodically. Pure client-side dressing over the authoritative
    /// <c>ShipCombatStatus</c> hull state; the emergency threshold has hysteresis so the light
    /// doesn't flicker at the boundary.
    /// </summary>
    public sealed class ShipDamageView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float SparkBelow = 0.5f;
        private const float AlarmBelow = 0.25f;
        private const float AlarmClearAbove = 0.30f; // hysteresis

        private float _nextSpark;
        private float _nextAlarm;
        private bool _alarmOn;
        private Light _emergency;
        private Material _sparkMat;

        private void Update()
        {
            float hull = HullFraction();
            bool aboard = Game != null && Game.Aboard && !Game.SpaceViewActive;

            // Sparks: random short bursts around the player, faster the worse the hull is.
            if (aboard && hull < SparkBelow && Camera != null)
            {
                if (Time.time >= _nextSpark)
                {
                    float urgency = Mathf.InverseLerp(SparkBelow, 0f, hull);
                    _nextSpark = Time.time + Mathf.Lerp(6f, 2f, urgency);
                    SparkBurst(Camera.transform.position
                        + new Vector3(Random.Range(-3f, 3f), Random.Range(-0.5f, 1.2f), Random.Range(-3f, 3f)));
                }
            }

            // Emergency state with hysteresis.
            if (!_alarmOn && aboard && hull < AlarmBelow)
            {
                _alarmOn = true;
            }
            else if (_alarmOn && (!aboard || hull > AlarmClearAbove))
            {
                _alarmOn = false;
            }

            if (_alarmOn)
            {
                EnsureEmergencyLight();
                _emergency.enabled = true;
                _emergency.transform.position = Camera != null
                    ? Camera.transform.position + Vector3.up * 1.2f
                    : transform.position;
                _emergency.intensity = 1.2f + 0.9f * Mathf.Sin(Time.time * 5f); // pulsing red wash

                if (Time.time >= _nextAlarm)
                {
                    _nextAlarm = Time.time + 3f;
                    ClientAudio.Instance?.Cue("hull_alarm", 0.6f);
                }
            }
            else if (_emergency != null && _emergency.enabled)
            {
                _emergency.enabled = false;
            }
        }

        private float HullFraction()
        {
            var combat = Game?.ShipCombat;
            return combat != null && combat.HullMax > 0f ? Mathf.Clamp01(combat.Hull / combat.HullMax) : 1f;
        }

        private void EnsureEmergencyLight()
        {
            if (_emergency != null)
            {
                return;
            }

            var go = new GameObject("EmergencyLight");
            go.transform.SetParent(transform, false);
            _emergency = go.AddComponent<Light>();
            _emergency.type = LightType.Point;
            _emergency.color = new Color(1f, 0.12f, 0.08f);
            _emergency.range = 9f;
            _emergency.intensity = 0f;
            _emergency.shadows = LightShadows.None;
        }

        private void SparkBurst(Vector3 at)
        {
            _sparkMat ??= new Material(Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque"))
            {
                color = ShaderColor.Srgb(new Color(1f, 0.62f, 0.18f)),
            };

            for (int i = 0; i < 5; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = p.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                p.transform.position = at;
                p.transform.localScale = Vector3.one * 0.07f;
                p.GetComponent<Renderer>().sharedMaterial = _sparkMat;
                p.AddComponent<Bit>().Vel = new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(0.8f, 2.0f), Random.Range(-1.4f, 1.4f));
            }

            ClientAudio.Instance?.At("ship_hull_hit", at, pitch: 1.4f, vol: 0.25f); // a small electrical crackle stand-in
        }

        /// <summary>A short-lived spark cube: arcs under gravity, shrinks, self-destroys.</summary>
        private sealed class Bit : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.5f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.07f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
