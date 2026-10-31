// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The season's look (#3042): the server names the planet's season and how far into it the year is
    /// (<c>WorldEnvironment.Season</c>/<c>SeasonProgress</c>); <see cref="SeasonLook"/> turns that into targets, which ease in
    /// here over about half a minute (a capture snaps them) and go to the shaders every frame as <c>_Sc_Season</c> — the
    /// plants' tint (<c>FloraAccent.hlsl</c>) and the grass tufts (<c>ScatterLit</c>) turn without a single remesh. The grade
    /// takes the season's warmth too (<see cref="SeasonGrade"/>), and a cold winter adds a frost on the tops
    /// (<c>TickWeatherTraces</c>). In space, in a cabin or with no season everything eases back to zero.
    /// </summary>
    public sealed partial class Sky
    {
        private static readonly int SeasonId = Shader.PropertyToID("_Sc_Season");

        /// <summary>The look shown now: x autumn, y winter, z bloom (eased toward <see cref="SeasonLook.Target"/>).</summary>
        private Vector4 _seasonLook;

        private void TickSeasonLook(float dt)
        {
            var env = Game?.Environment;
            bool on = env != null && !env.SpaceSky && !Game.SpaceViewActive && !string.IsNullOrEmpty(env.Season);
            var (autumn, winter, bloom) = on ? SeasonLook.Target(env.Season, env.SeasonProgress) : (0f, 0f, 0f);
            var target = new Vector4(autumn, winter, bloom, 0f);
            _seasonLook = Game != null && Game.CaptureEnvActive ? target : Vector4.MoveTowards(_seasonLook, target, dt / 30f);
            Shader.SetGlobalVector(SeasonId, _seasonLook);
        }

        /// <summary>The grade with the season folded in: a share of the warm or cool tint and a saturation factor.</summary>
        private (Color Tint, float Sat) SeasonGrade(Color tint, float sat)
        {
            var env = Game?.Environment;
            if (env == null || env.SpaceSky || string.IsNullOrEmpty(env.Season))
            {
                return (tint, sat);
            }

            var (warmth, saturation) = SeasonLook.Grade(env.Season);
            tint = Color.Lerp(tint, warmth >= 0f ? FestivalMood.WarmTint : FestivalMood.CoolTint, Mathf.Clamp01(Mathf.Abs(warmth)) * 0.5f);
            return (tint, sat * saturation);
        }
    }
}
