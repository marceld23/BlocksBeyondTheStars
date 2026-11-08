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
    ///   <item><b>Night sight</b> (the preparation, or a lit Ressaw lamp worn, #3339) makes the world around the player look
    ///   like day. This component only eases the effect's strength in and out and hands it over as <see cref="NightSight"/>;
    ///   since #3414 <see cref="Sky"/> draws the whole look from it — the light, the models' light and the ambient, the sky,
    ///   haze and mist colours, the fill of sky-less faces (<see cref="NightSightRules"/>) — and the starfield, the clouds
    ///   and the eye adaptation follow. Before, this component lifted only the light and the cabin fill globals, so the
    ///   player saw a lit bubble in a night-black world. Shader values only — nothing the "Lite" browser profile would
    ///   feel.</item>
    ///   <item><b>Perception</b> marks the living things within its range through terrain: the range is handed to
    ///   <see cref="ThermalVision"/>, which draws them with the optic's contact blobs — without the optic's
    ///   full-screen grade, so it also behaves the same under the reduced-effects setting.</item>
    /// </list>
    /// Only the wearer sees night sight: the effect travels in the player's own state only, so other players keep seeing
    /// just the lamp's beam.
    /// </summary>
    public sealed class BioSenses : MonoBehaviour
    {
        public GameBootstrap Game;

        private static BioSenses _instance;

        private float _sight; // eased 0..1, so the dark lifts and returns instead of snapping

        /// <summary>#3414: the eased night-sight strength (0..1): 0 without the effect, in the flight view (which lights
        /// itself) and once the component is gone. <see cref="Sky"/> reads it every frame, like the lightning flash; the
        /// starfield, the clouds and the eye adaptation read it too. Written in <c>LateUpdate</c>: a reader may get the value
        /// of the frame before, which the quarter-second ease makes invisible.</summary>
        public static float NightSight { get; private set; }

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
            if (Game?.Views.Thermal != null)
            {
                Game.Views.Thermal.SenseRange = onFoot ? Game.Bio.PerceptionRange : 0f;
            }

            _sight = Mathf.MoveTowards(_sight, onFoot ? Mathf.Clamp01(Game.Bio.NightSight) : 0f, Time.deltaTime * 4f);
            NightSight = _sight;
        }

        private void OnDisable()
        {
            _sight = 0f;
            NightSight = 0f; // the Sky draws a plain night again from the next frame — nothing to hand back
            if (Game?.Views.Thermal != null)
            {
                Game.Views.Thermal.SenseRange = 0f;
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
