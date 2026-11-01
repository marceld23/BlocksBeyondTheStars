// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Details (High)" extras (epic #3066): desktop-class effects that run only on the High preset of a desktop
    /// client — never in the browser (WebGL/WebGPU) or on mobile, which keep the plain High look. Every effect still uses
    /// only what WebGL 2 can do (ADR 0015 §3); High simply may cost more on a desktop than a browser tab can afford.
    /// <see cref="ClientSettings.Apply"/> pushes the player's switches here (#3061); the effects read the flags live and
    /// may subscribe to <see cref="Changed"/>.
    /// </summary>
    public static class HighDetails
    {
        private static readonly int ReliefId = Shader.PropertyToID("_Sc_Relief");

        /// <summary>True on a client that may run the extras at all: not WebGL, not a phone or tablet.</summary>
        public static bool DesktopClass => !ClientSettings.FarViewLightDevice;

        /// <summary>The High preset on a desktop client — the gate every extra shares.</summary>
        public static bool Active { get; private set; }

        /// <summary>Parallax occlusion mapping on rock, ore, brick and metal (#3055).</summary>
        public static bool Relief { get; private set; }

        /// <summary>The shell-grass carpet on meadows (#3056).</summary>
        public static bool Grass { get; private set; }

        /// <summary>Flowers as a ground-scatter kind (#3056).</summary>
        public static bool Flowers { get; private set; }

        /// <summary>Box-projected reflections inside ship and base rooms (#3057).</summary>
        public static bool RoomReflections { get; private set; }

        /// <summary>Scorch marks and impact craters (#3058).</summary>
        public static bool Decals { get; private set; }

        /// <summary>Volumetric sun light, including dust in rooms (#3060).</summary>
        public static bool VolumetricLight { get; private set; }

        /// <summary>Raised whenever one of the flags changes (on the main thread, from <see cref="Configure"/>).</summary>
        public static event Action Changed;

        /// <summary>Takes the player's preset and switches. Idempotent; raises <see cref="Changed"/> only on a change.</summary>
        public static void Configure(ClientSettings s)
        {
            bool active = s != null && s.Preset == QualityPreset.High && DesktopClass;
            bool relief = active && s.HighRelief;
            bool grass = active && s.HighGrass;
            bool flowers = active && s.HighFlowers;
            bool rooms = active && s.HighRoomReflections;
            bool decals = active && s.HighDecals;
            bool volumetric = active && s.HighVolumetricLight;

            Shader.SetGlobalFloat(ReliefId, relief ? 1f : 0f);

            if (active == Active && relief == Relief && grass == Grass && flowers == Flowers && rooms == RoomReflections
                && decals == Decals && volumetric == VolumetricLight)
            {
                return;
            }

            Active = active;
            Relief = relief;
            Grass = grass;
            Flowers = flowers;
            RoomReflections = rooms;
            Decals = decals;
            VolumetricLight = volumetric;
            Debug.Log($"[HighDetails] active={active} relief={relief} grass={grass} flowers={flowers} rooms={rooms} decals={decals} volumetric={volumetric}");
            Changed?.Invoke();
        }
    }
}
