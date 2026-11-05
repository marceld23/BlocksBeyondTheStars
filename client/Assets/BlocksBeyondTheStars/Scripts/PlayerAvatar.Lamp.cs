// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3231/#3234: the suit lamp on the avatar's helmet — it glows in the LED white while the lamp is on (the own avatar in
    /// the third person, and every other player's), and it is where the beam leaves the helmet: the third-person light
    /// and the other players' lamps start here, aimed along the head (which nods with the look pitch).
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>The lit lamp's brightness (linear × the LED colour): past the bloom threshold, like a real LED.</summary>
        private const float LampGlow = 3f;

        private GameObject _gearLamp; // the lamp cube on the helmet (null when no lamp is worn)
        private Material _gearLampGlow;
        private bool _lampLit;

        /// <summary>Switches the helmet lamp's glow (no-op without a worn lamp; kept across gear rebuilds).</summary>
        public void SetLampLit(bool lit)
        {
            if (lit == _lampLit)
            {
                return;
            }

            _lampLit = lit;
            ApplyLampLook();
        }

        /// <summary>Where the beam leaves the helmet (the front of the lamp cube, or the spot it sits on), in world space.
        /// False for a figure without a head.</summary>
        public bool TryHelmetLamp(out Vector3 position)
        {
            if (_head == null)
            {
                position = default;
                return false;
            }

            position = _gearLamp != null
                ? _gearLamp.transform.TransformPoint(new Vector3(0f, 0f, 0.55f))
                : _head.TransformPoint(new Vector3(0.70f, 0.16f, 0.45f));
            return true;
        }

        /// <summary>The way the helmet faces (yaw and the nod of the look pitch) — another player's beam axis.</summary>
        public Vector3 HelmetForward => _head != null ? _head.forward : transform.forward;

        private void ApplyLampLook()
        {
            var r = _gearLamp != null ? _gearLamp.GetComponent<Renderer>() : null;
            if (r == null)
            {
                return;
            }

            if (_lampLit && _gearLampGlow == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
                _gearLampGlow = new Material(shader) { color = ShaderColor.Srgb(Headlamp.LedSrgb) * LampGlow };
                if (_gearLampGlow.HasProperty("_Floor"))
                {
                    _gearLampGlow.SetFloat("_Floor", 1f); // lit from every side — it is the light
                    _gearLampGlow.SetFloat("_Fill", 0f);
                }
            }

            r.sharedMaterial = _lampLit ? _gearLampGlow : _gearLampMat;
        }
    }
}
