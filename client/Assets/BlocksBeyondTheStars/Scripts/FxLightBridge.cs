// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Makes a Unity point <see cref="Light"/> actually light the world (#2151). URP additional lights are off by design
    /// (ADR 0003 — the voxels light themselves), so every effect light the views created (the beam pad's glow, the
    /// ship's red emergency lamp, a landing ship's engine glow, data cubes, net fragments, glowing creatures) rendered
    /// nothing at all. This bridge mirrors the light's colour, intensity, range and enabled state into a shader-side
    /// <see cref="FxLights"/> slot every frame, so the same object lights blocks, models and creatures around it.
    /// Add it next to the Light with <see cref="Mirror"/>.
    /// </summary>
    public sealed class FxLightBridge : MonoBehaviour
    {
        private Light _light;
        private int _id;
        private bool _flicker;

        /// <summary>Mirrors <paramref name="light"/> into the FX lights (once per light; returns the bridge).</summary>
        public static FxLightBridge Mirror(Light light, bool flicker = false)
        {
            if (light == null)
            {
                return null;
            }

            var bridge = light.GetComponent<FxLightBridge>() ?? light.gameObject.AddComponent<FxLightBridge>();
            bridge._light = light;
            bridge._flicker = flicker;
            return bridge;
        }

        private void LateUpdate()
        {
            bool on = _light != null && _light.enabled && _light.intensity > 0.01f && gameObject.activeInHierarchy;
            if (!on)
            {
                Drop();
                return;
            }

            // Unity light intensities in these views run 0.5..4 at ranges of 3..12 blocks — map to the FX-light scale.
            float intensity = Mathf.Clamp(_light.intensity * 0.6f, 0f, 4f);
            if (_id == 0)
            {
                _id = FxLights.Attach(transform, Vector3.zero, _light.color, intensity, Mathf.Max(2f, _light.range), _flicker);
            }
            else
            {
                FxLights.Set(_id, _light.color, intensity);
            }
        }

        private void OnDisable() => Drop();

        private void OnDestroy() => Drop();

        private void Drop()
        {
            if (_id != 0)
            {
                FxLights.Remove(_id);
                _id = 0;
            }
        }
    }
}
