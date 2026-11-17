// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// When the music steps back and when it leans in: the ducking the scene asks for (weather, something watching,
    /// someone speaking, a musical accent) and the on-foot tension pool (<see cref="TensionMusic"/>) that takes over
    /// while a hostile stays close.
    /// </summary>
    public sealed partial class ClientMusic
    {
        /// <summary>An outing shorter than this is no homecoming, seconds.</summary>
        private const float HomeAwaySeconds = 240f;

        private readonly TensionMusic _tensionRule = new TensionMusic();
        private int _tensionFrame = -10;
        private float _awaySince = -1f;

        /// <summary>The ducking multiplier the scene asks for: a violent weather episode and a hostile creature close by
        /// (on foot) pull the music down under the ambience; aboard / in space the world ducks nothing. On top, anywhere:
        /// the music makes room while someone speaks and under a musical accent (<see cref="MusicDuck"/>).</summary>
        private float DuckFor(GameBootstrap game)
        {
            _tension = false;
            TrackHomecoming(game);
            return Mathf.Min(WorldDuck(game), MusicDuck.Level);
        }

        /// <summary>Coming back aboard after a real outing (<see cref="HomeAwaySeconds"/> or longer) plays the homecoming
        /// accent — stepping out for a minute and back in does not.</summary>
        private void TrackHomecoming(GameBootstrap game)
        {
            if (game == null)
            {
                _awaySince = -1f;
                return;
            }

            if (!game.Aboard)
            {
                if (_awaySince < 0f)
                {
                    _awaySince = Time.time;
                }

                return;
            }

            if (_awaySince >= 0f && Time.time - _awaySince >= HomeAwaySeconds)
            {
                ClientAudio.Instance?.Sting("sting_home");
            }

            _awaySince = -1f;
        }

        private float WorldDuck(GameBootstrap game)
        {
            if (game == null || game.Aboard || game.SpaceViewActive || game.InSpace || _context == GiantFight)
            {
                return 1f; // #3676: the boss track is the fight's music — never ducked or muffled under it
            }

            float duck = 1f;
            var env = game.Environment;
            if (env != null && game.ExposedToSky)
            {
                float intensity = Mathf.Clamp01(game.WeatherIntensity);
                switch (env.WeatherFamily)
                {
                    case "violent": duck = 1f - (0.45f * intensity); break;            // storm / blizzard / sandstorm
                    case "wet":
                    case "obscuring":
                    case "windy": duck = 1f - (0.2f * intensity); break;               // rain, fog, gale: a touch
                }
            }

            // A hostile close by darkens and lowers the bed — unless the tension pool already plays: that IS its music.
            if (game.NearestHostileSqr < TensionRadiusSqr && _context != MusicLibrary.Tension)
            {
                _tension = true;
                duck = Mathf.Min(duck, 0.6f);
            }

            if (game.Uneasy)
            {
                _tension = true; // 2026-09 (Valuma): something is watching — the music stays dark and low
                duck = Mathf.Min(duck, 0.7f);
            }

            return duck;
        }

        /// <summary>Whether the on-foot tension pool should play this frame: a hostile has stayed close for a moment, and
        /// the pool then holds for a while so the music does not flicker with every step in and out of range. Asked once
        /// per frame on foot; a gap (a flight, a cutscene, the finale) starts the rule afresh.</summary>
        private bool OnFootTension(GameBootstrap game)
        {
            if (Time.frameCount - _tensionFrame > 1)
            {
                _tensionRule.Reset();
            }

            _tensionFrame = Time.frameCount;
            bool near = game.NearestHostileSqr < TensionRadiusSqr;
            return _tensionRule.Step(Time.deltaTime, near, allowed: !game.Aboard);
        }
    }
}
