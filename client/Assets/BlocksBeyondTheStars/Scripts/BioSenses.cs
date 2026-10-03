// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What two of the bio lab's preparations do to the player's senses (#2202). Both are read every frame from the
    /// running effects (<see cref="BioClientState"/>) — the server decides when an effect starts and ends, this only
    /// shows it.
    /// <list type="bullet">
    ///   <item><b>Night sight</b> lifts the dark. The block shaders darken a night through the global light tint
    ///   (<c>_Sc_Light</c>: sun colour × day brightness × weather) and leave a cave to its small ambient floor; the
    ///   only fill for faces without skylight is <c>_Sc_Indoor</c>, the cabin light. Night sight raises both to a
    ///   floor that grows with the effect's strength: the light tint is topped up with neutral grey to a minimum
    ///   brightness (so a day is left alone and a night keeps its hue), and the fill for sky-less faces is raised
    ///   like a faint cabin light. Two shader globals per frame — no pass, no texture, nothing the "Lite" browser
    ///   profile would feel.</item>
    ///   <item><b>Perception</b> marks the living things within its range through terrain: the range is handed to
    ///   <see cref="ThermalVision"/>, which draws them with the optic's contact blobs — without the optic's
    ///   full-screen grade, so it also behaves the same under the reduced-effects setting.</item>
    /// </list>
    ///
    /// <see cref="Sky"/> owns the two globals and writes them in its <c>Update</c>; the lift is applied on top in
    /// <c>LateUpdate</c>, so the order of the two scripts does not matter and nothing has to be handed back while
    /// the sky keeps running. Should a global NOT have been rewritten when the effect ends (the sky is off), the
    /// value kept from before the lift is put back — the effect never leaves a brighter world behind.
    /// </summary>
    public sealed class BioSenses : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The sRGB brightness the light tint is lifted to at full night sight — a late afternoon. A night
        /// sits at 0.35, noon at about 0.95 (see <c>Sky.ApplyLighting</c>).</summary>
        private const float LightFloor = 0.74f;

        /// <summary>The fill sky-less faces (caves, deep shade) get at full night sight; 1 is a lit ship cabin.</summary>
        private const float CaveFill = 0.8f;

        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int IndoorId = Shader.PropertyToID("_Sc_Indoor");

        private static BioSenses _instance;

        private float _sight;          // eased 0..1, so the dark lifts and returns instead of snapping
        private bool _holding;         // the globals carry our lift from the last frame
        private Color _lightWritten, _lightBase;
        private float _indoorWritten, _indoorBase;

        /// <summary>Adds the component to the world rig the first time one of the two effects runs (it lives on the
        /// server link's object and goes with the rig). Called every frame by <see cref="GameBootstrap"/>; a world
        /// whose player never brews such a preparation never gets one.</summary>
        public static void Ensure(GameBootstrap game)
        {
            if (_instance != null || game == null || (game.Bio.NightSight <= 0f && game.Bio.PerceptionRange <= 0f))
            {
                return;
            }

            _instance = game.gameObject.AddComponent<BioSenses>();
            _instance.Game = game;
        }

        private void LateUpdate()
        {
            bool onFoot = Game != null && !Game.SpaceViewActive; // the flight view lights itself and has no terrain
            if (ThermalVision.Instance != null)
            {
                ThermalVision.Instance.SenseRange = onFoot ? Game.Bio.PerceptionRange : 0f;
            }

            _sight = Mathf.MoveTowards(_sight, onFoot ? Mathf.Clamp01(Game.Bio.NightSight) : 0f, Time.deltaTime * 4f);
            ApplyNightSight(_sight);
        }

        private void ApplyNightSight(float strength)
        {
            if (strength <= 0f)
            {
                Release();
                return;
            }

            // What the globals hold now is their owner's value of this frame — unless nobody rewrote them since our
            // last frame, in which case it is still our own lift and the true value is the one we kept.
            var light = Shader.GetGlobalColor(LightId);
            if (!_holding || light != _lightWritten)
            {
                _lightBase = light;
            }

            float indoor = Shader.GetGlobalFloat(IndoorId);
            if (!_holding || indoor != _indoorWritten)
            {
                _indoorBase = indoor;
            }

            // Top the light tint up to the floor with neutral grey: brightness rises to the floor exactly, the hue
            // of the night stays, and a tint already brighter (any day) is left as it is. Alpha is the "set" flag.
            _lightWritten = _lightBase;
            if (_lightBase.a > 0.5f)
            {
                float floor = ShaderColor.Srgb(new Color(LightFloor, LightFloor, LightFloor)).r * strength;
                float lift = floor - ((0.299f * _lightBase.r) + (0.587f * _lightBase.g) + (0.114f * _lightBase.b));
                if (lift > 0f)
                {
                    _lightWritten = new Color(_lightBase.r + lift, _lightBase.g + lift, _lightBase.b + lift, _lightBase.a);
                }
            }

            _indoorWritten = Mathf.Max(_indoorBase, CaveFill * strength);
            Shader.SetGlobalColor(LightId, _lightWritten);
            Shader.SetGlobalFloat(IndoorId, _indoorWritten);
            _holding = true;
        }

        /// <summary>Hands the globals back: a value its owner has rewritten since is already the true one, a value
        /// that is still our lift gets the value from before the lift.</summary>
        private void Release()
        {
            if (!_holding)
            {
                return;
            }

            _holding = false;
            if (Shader.GetGlobalColor(LightId) == _lightWritten)
            {
                Shader.SetGlobalColor(LightId, _lightBase);
            }

            if (Shader.GetGlobalFloat(IndoorId) == _indoorWritten)
            {
                Shader.SetGlobalFloat(IndoorId, _indoorBase);
            }
        }

        private void OnDisable()
        {
            _sight = 0f;
            Release();
            if (ThermalVision.Instance != null)
            {
                ThermalVision.Instance.SenseRange = 0f;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
