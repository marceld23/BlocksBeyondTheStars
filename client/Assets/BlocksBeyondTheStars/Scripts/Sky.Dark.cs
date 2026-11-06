// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The dark (#3265: caves, dug tunnels and nights were far too bright): the globals that keep it dark without ever
    /// turning it pitch black — the flat dark floor every lit surface keeps (<c>_Sc_DarkFloor</c>, raised by the "Visibility
    /// in the dark" setting), the models' share of the world light (<c>_Sc_ModelLight</c>: creatures, avatars, doors and
    /// held items no longer glow in a cave), and the gentle interior fill of a room in the player's own base. The values are
    /// <see cref="DarkLightRules"/> (Client.Core, tested headless).
    /// </summary>
    public sealed partial class Sky
    {
        private static readonly int DarkFloorId = Shader.PropertyToID("_Sc_DarkFloor");
        private static readonly int ModelLightId = Shader.PropertyToID("_Sc_ModelLight");

        /// <summary>"Visibility in the dark" (0..1), from the settings: raises the dark floor and the models' light in the dark.</summary>
        public float DarkVisibility;

        private float _baseFill;          // the eased home-base fill (0 … DarkLightRules.HomeBaseFill)
        private float _modelLight = 1f;   // the world cameras' model light (previews and the menu always get 1)

        /// <summary>The defaults before any world is up (the menu, previews): the shaders read an unset dark floor as its
        /// default too, so this only makes the globals explicit from the first frame.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitDarkGlobals()
        {
            Shader.SetGlobalFloat(DarkFloorId, DarkLightRules.DarkFloor(0f));
            Shader.SetGlobalFloat(ModelLightId, 1f);
        }

        /// <summary>The interior fill the shaders get: the cabin's or the station's, or — #3265 — a gentle fill in a room of
        /// the player's own base, so a dug home without lamps is dim-cosy, not cave-dark. Caves and tunnels outside the
        /// base get none.</summary>
        private float InteriorFill(bool litInterior, bool home)
        {
            float target = !litInterior && home ? DarkLightRules.HomeBaseFill : 0f;
            _baseFill = Mathf.MoveTowards(_baseFill, target, Time.deltaTime * 0.5f);
            return Mathf.Max(_indoor, _baseFill);
        }

        /// <summary>Publishes the dark floor and the models' light for this frame (<paramref name="linearLight"/> = the
        /// world light as the shaders get it).</summary>
        private void PublishDarkGlobals(Color linearLight, float interiorFill)
        {
            Shader.SetGlobalFloat(DarkFloorId, DarkLightRules.DarkFloor(DarkVisibility));
            float luma = 0.299f * linearLight.r + 0.587f * linearLight.g + 0.114f * linearLight.b;
            float exposure = Probe != null ? Probe.CameraExposure : 1f;
            _modelLight = DarkLightRules.ModelLight(luma, exposure, interiorFill, DarkVisibility);
            Shader.SetGlobalFloat(ModelLightId, _modelLight);
        }

        /// <summary>Space view, menu: the plain studio light for the models and the default dark floor.</summary>
        private void ResetDarkGlobals()
        {
            _modelLight = 1f;
            _baseFill = 0f;
            Shader.SetGlobalFloat(ModelLightId, 1f);
            Shader.SetGlobalFloat(DarkFloorId, DarkLightRules.DarkFloor(DarkVisibility));
        }

        /// <summary>#3265: a dark cave is no cosy place — the cosy grade's lift and softening fade with the camera's exposure
        /// (to 30 % underground), unless a lamp, torch or campfire lights the spot (a lit camp in a cave stays cosy).</summary>
        private float DarkPlaceFactor(bool lampLit)
            => lampLit || Probe == null ? 1f : Mathf.Lerp(0.3f, 1f, Probe.CameraExposure);
    }
}
