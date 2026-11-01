// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The colour of the flight view's star (#3075): the star of the system the flight is in, read from the star map
    /// (<see cref="FlightStar"/>) — the environment's sun colour belongs to the world last landed on, and a hyperjump in
    /// flight brings no new environment, so every system used to show the departure system's star.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>#3077: how far the star's core is washed toward white — 0.85 read white-hot whatever the star.</summary>
        private const float SunCoreWhite = 0.6f;

        private Material _sunCoreMat;
        private StarMapData _sunMap; // the star map the star was last coloured from

        /// <summary>The star of the system the flight is in; the environment's sun, then the stylised default, as fallbacks.</summary>
        private Color FlightStarColor()
        {
            _sunMap = Game?.StarMap;
            int rgb = FlightStar.ColorOf(_sunMap, Game?.Environment != null ? Game.Environment.SunColor : 0);
            return rgb != 0 ? Rgb(rgb) : new Color(1f, 0.96f, 0.88f);
        }

        private static Color SunCoreColor(Color star) => Color.Lerp(star, Color.white, SunCoreWhite);

        /// <summary>A star map that arrives after the scene was built (normally it comes in the same network batch as the
        /// flight state) re-tints the star — corona, core and the lens flare that reads <see cref="_sunColor"/>.</summary>
        private void RetintSunOnNewMap()
        {
            if (_sunMat == null || ReferenceEquals(_sunMap, Game?.StarMap))
            {
                return;
            }

            _sunColor = FlightStarColor();
            _sunMat.SetColor("_Color", ShaderColor.Srgb(_sunColor));
            if (_sunCoreMat != null)
            {
                _sunCoreMat.SetColor("_Color", ShaderColor.Srgb(SunCoreColor(_sunColor)));
            }
        }
    }
}
