// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lights of <see cref="GuardianGiantView"/>: the meshed light blocks glow on their own (lit until the collapse
    /// lands); on top, the overlays blink — a slow pulse while calm, fast and brighter in the rage phase, steady under reduced
    /// motion or reduced flashes — and flare with the move (<see cref="GuardianGiantFrame.Flare"/>), the visor burns white-hot
    /// for the laser (<see cref="GuardianGiantFrame.Visor"/>). The chest core lights the ground around the giant red
    /// (<see cref="FxLights"/>), and the search light sweeps a beam with a bright spot where it meets the ground.
    /// </summary>
    public sealed partial class GuardianGiantView
    {
        private static readonly Color GlowRed = new Color(1f, 0.2f, 0.14f);
        private static readonly Color GlowHot = new Color(1f, 0.78f, 0.62f);
        private static readonly Color SearchColor = new Color(1f, 0.93f, 0.8f);

        private int _chestLight;
        private int _spotLight;
        private Transform _spot;
        private LineRenderer _search;

        /// <summary>The blink, the flare, the visor, the chest light and the search light for this frame.</summary>
        private void UpdateLights(in GuardianGiantFrame f, GuardianGiantTrack t, float now)
        {
            bool lit = _rig.Lit;
            bool rage = t.Status.Rage;
            float blink = Blink(now, rage);
            float body = Mathf.Max(blink, f.Flare);
            float visor = Mathf.Max(body, f.Visor);
            SetGlow(_rig.GlowBody, GlowRed, body, rage);
            SetGlow(_rig.GlowVisor, Color.Lerp(GlowRed, GlowHot, f.Visor), visor, rage);
            foreach (var part in GuardianGiantBody.Parts)
            {
                var glow = _rig.Glows[(int)part];
                if (glow != null)
                {
                    bool on = lit && (part == GuardianGiantPart.Head ? visor : body) > 0.02f;
                    if (glow.enabled != on)
                    {
                        glow.enabled = on;
                    }
                }
            }

            if (lit)
            {
                if (_chestLight == 0)
                {
                    _chestLight = FxLights.Attach(_rig.Bone(GuardianGiantPart.Torso), GuardianGiantModel.ChestLocal, GlowRed, 0.8f, 9f);
                }

                FxLights.Set(_chestLight, GlowRed, 0.6f + (1.4f * body));
            }
            else if (_chestLight != 0)
            {
                FxLights.Remove(_chestLight);
                _chestLight = 0;
            }

            UpdateSearchLight(lit ? f.SearchLight : 0f);
        }

        /// <summary>The blink level 0 … 1 at <paramref name="now"/>: a slow soft pulse, in the rage phase a fast bright one;
        /// a steady glow when the player asked for fewer flashes or less motion.</summary>
        private float Blink(float now, bool rage)
        {
            if (UiKit.ReducedMotion || FxKit.ReduceFlashes)
            {
                return rage ? 0.45f : 0.25f;
            }

            float period = rage ? 0.42f : 1.8f;
            float on = rage ? 0.2f : 0.35f;
            float phase = Mathf.Repeat(now + _blinkSeed, period);
            return phase < on ? Mathf.Sin(phase / on * Mathf.PI) * (rage ? 1f : 0.7f) : 0f;
        }

        private static void SetGlow(Material m, Color srgb, float level, bool rage)
        {
            if (m == null)
            {
                return;
            }

            var lin = FxKit.Lin(srgb);
            float k = 1.2f + ((rage ? 3f : 2f) * level);
            m.color = new Color(lin.r * k, lin.g * k, lin.b * k, 1f);
        }

        /// <summary>The search light: a widening beam from the visor along the head's gaze to the ground (or 30 blocks out),
        /// and a light where it lands. Off at <paramref name="strength"/> 0.</summary>
        private void UpdateSearchLight(float strength)
        {
            if (strength <= 0.02f)
            {
                if (_search != null && _search.enabled)
                {
                    _search.enabled = false;
                }

                if (_spotLight != 0)
                {
                    FxLights.Remove(_spotLight);
                    _spotLight = 0;
                }

                return;
            }

            EnsureSearchLight();
            var head = _rig.Bone(GuardianGiantPart.Head);
            var from = head.TransformPoint(GuardianGiantModel.VisorLocal);
            var dir = head.forward;
            var to = Physics.Raycast(from, dir, out var hit, 40f, PlayerController.WorldRayMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : from + (dir * 30f);
            _search.enabled = true;
            _search.SetPosition(0, from);
            _search.SetPosition(1, to);
            var lin = FxKit.Lin(SearchColor);
            _search.startColor = new Color(lin.r, lin.g, lin.b, 0.32f * strength);
            _search.endColor = new Color(lin.r, lin.g, lin.b, 0.04f * strength);
            _spot.position = to + (Vector3.up * 0.6f);
            if (_spotLight == 0)
            {
                _spotLight = FxLights.Attach(_spot, Vector3.zero, SearchColor, 1f, 7f);
            }

            FxLights.Set(_spotLight, SearchColor, 1.3f * strength);
        }

        private void EnsureSearchLight()
        {
            if (_search != null)
            {
                return;
            }

            var go = new GameObject("GiantSearchBeam");
            go.transform.SetParent(_parent, false);
            _search = go.AddComponent<LineRenderer>();
            _search.useWorldSpace = true;
            _search.positionCount = 2;
            _search.alignment = LineAlignment.View;
            _search.textureMode = LineTextureMode.Stretch;
            _search.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _search.receiveShadows = false;
            _search.widthCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 7f);
            _search.widthMultiplier = 1f;
            _search.sharedMaterial = FxKit.BeamMaterial("giant_search", SearchColor, 0.6f, 0.6f, 2f, 0f, 1.2f);
            _spot = new GameObject("GiantSearchSpot").transform;
            _spot.SetParent(_parent, false);
        }

        private void DisposeLights()
        {
            if (_chestLight != 0)
            {
                FxLights.Remove(_chestLight);
                _chestLight = 0;
            }

            if (_spotLight != 0)
            {
                FxLights.Remove(_spotLight);
                _spotLight = 0;
            }

            if (_search != null)
            {
                Object.Destroy(_search.gameObject);
            }

            if (_spot != null)
            {
                Object.Destroy(_spot.gameObject);
            }
        }
    }
}
