// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The escort cruiser's look (#3900) in numbers: how the flight view recognises the boss among the cruisers, how big
    /// its model, its brackets, its plumes and its explosion are, and where the parts sit that carry the player idea's
    /// picture — a row of red glowing windows along the bridge and, above them, a long barrel pointing forward. The Unity
    /// builder (<c>SpaceView.BuildEscortCruiserModel</c>) places its cubes by these numbers, so the muzzle the mega laser
    /// starts at is the barrel's real front end. All lengths are in the model's own units, before its scale; the model
    /// faces +Z.
    /// </summary>
    public static class EscortCruiserLook
    {
        /// <summary>The wire kind the boss shares with the plain cruiser.</summary>
        public const string Kind = "Cruiser";

        /// <summary>A cruiser at least this big is drawn as the boss even when no boss status named it (a status lost
        /// on the way, a server that sends none): the plain cruiser has scale 1, the boss
        /// <see cref="EscortCruiserRules.ModelScale"/>.</summary>
        public const float BossScaleFrom = 2f;

        /// <summary>The hull: width, height, length (centred on the model's origin).</summary>
        public const float HullWidth = 2.4f, HullHeight = 1.3f, HullLength = 7.6f;

        /// <summary>The bridge block the windows sit in: its centre and size.</summary>
        public const float BridgeY = 1.0f, BridgeZ = 1.6f, BridgeWidth = 1.5f, BridgeHeight = 0.75f, BridgeLength = 2.8f;

        /// <summary>The windows on each side of the bridge: how many, their height on the hull and their size.</summary>
        public const int WindowsPerSide = 5;

        /// <summary>See <see cref="WindowsPerSide"/>.</summary>
        public const float WindowY = 1.05f, WindowHeight = 0.26f, WindowLength = 0.34f;

        /// <summary>The barrel above the windows: its centre line's height, where it starts (on its mount) and its length.</summary>
        public const float BarrelY = 1.62f, BarrelStartZ = 1.4f, BarrelLength = 5.1f, BarrelThickness = 0.34f;

        /// <summary>The muzzle — the barrel's front end — in the model's own frame: where the charge gathers and the
        /// aiming line and the beam start.</summary>
        public const float MuzzleX = 0f, MuzzleY = BarrelY, MuzzleZ = BarrelStartZ + BarrelLength;

        /// <summary>Whether a space entity is drawn as the escort cruiser: a cruiser a live boss status names, or one
        /// that is simply as big as the boss.</summary>
        public static bool IsEscortCruiser(string? kind, float scale, bool namedByBossStatus)
            => string.Equals(kind, Kind, StringComparison.Ordinal) && (namedByBossStatus || scale >= BossScaleFrom);

        /// <summary>The model's scale for the entity's wire scale. A boss the status names but whose scale says nothing
        /// (1, or a broken value) gets the rule's size; a real value is kept within sane bounds.</summary>
        public static float ModelScale(float entityScale)
            => entityScale >= 1.5f && entityScale <= 6f ? entityScale : EscortCruiserRules.ModelScale;

        /// <summary>The boss's rough radius in the flight frame (lock brackets, effect box) — the plain cruiser's is 4.5.</summary>
        public static float TargetRadius(float modelScale) => 5.5f * Math.Max(1f, modelScale);

        /// <summary>The explosion's size factor — the plain cruiser's is 2.2. It grows slower than the model, so the
        /// flash of the boss's end stays a picture and not a white screen.</summary>
        public static float ExplosionScale(float modelScale) => 2.2f * (1f + (0.6f * (Math.Max(1f, modelScale) - 1f)));

        /// <summary>The size of one engine plume — the plain cruiser's is 1.3 — hotter (longer, wider) once enraged.</summary>
        public static float PlumeSize(float modelScale, bool enraged) => 1.3f * Math.Max(1f, modelScale) * (enraged ? 1.45f : 1f);

        /// <summary>The Z of window <paramref name="index"/> (0 … <see cref="WindowsPerSide"/> − 1) on a side of the
        /// bridge: an even row from its rear to its front, a margin left at both ends.</summary>
        public static float WindowZ(int index)
        {
            int i = Math.Max(0, Math.Min(WindowsPerSide - 1, index));
            float first = BridgeZ - (BridgeLength * 0.5f) + (WindowLength * 0.5f) + 0.18f;
            float last = BridgeZ + (BridgeLength * 0.5f) - (WindowLength * 0.5f) - 0.18f;
            return WindowsPerSide <= 1 ? BridgeZ : first + ((last - first) * i / (WindowsPerSide - 1));
        }

        /// <summary>How brightly the windows (and the engines' cores) glow at <paramref name="time"/>, 0 … 1: a calm,
        /// steady red in the first phase; brighter and pulsing once enraged. With reduced motion the enraged glow is the
        /// brighter level without the pulse.</summary>
        public static float Glow(bool enraged, float time, bool reducedMotion)
        {
            if (!enraged)
            {
                return 0.35f;
            }

            return reducedMotion ? 0.85f : 0.7f + (0.3f * (0.5f + (0.5f * (float)Math.Sin(time * 7.0))));
        }
    }

    /// <summary>
    /// The mega laser's warning (#3900) in numbers: the aiming line from the barrel to the targeted ship while the shot
    /// charges. It pulses, and faster the closer the shot comes — the countdown a pilot reads without a number — and it
    /// is drawn for every pilot, strongest for the one it points at.
    /// </summary>
    public static class MegaLaserAim
    {
        /// <summary>The line's pulse rate when the charge starts and just before it fires (pulses per second).</summary>
        public const float PulseStartHz = 1.5f, PulseEndHz = 7f;

        /// <summary>The line fades in over this share of the charge instead of popping up.</summary>
        public const float FadeInShare = 0.06f;

        /// <summary>Whether a heavy shot's style is the mega laser.</summary>
        public static bool IsMegaLaser(string? style)
            => string.Equals(style, SpaceCombatRules.HeavyStyleMegaLaser, StringComparison.Ordinal);

        /// <summary>How many pulses the line has made at <paramref name="t"/> (0 … 1 of a charge of
        /// <paramref name="seconds"/>): the rate rises evenly from <see cref="PulseStartHz"/> to <see cref="PulseEndHz"/>.</summary>
        public static float Pulses(float t, float seconds)
        {
            float u = Clamp01(t);
            return Math.Max(0f, seconds) * ((PulseStartHz * u) + ((PulseEndHz - PulseStartHz) * u * u * 0.5f));
        }

        /// <summary>The line's opacity at <paramref name="t"/> (0 … 1 of the charge): for the pilot it points at
        /// (<paramref name="atMe"/>) clearly stronger than for a bystander at every moment, pulsing between about half
        /// and full — steady under reduced motion.</summary>
        public static float Alpha(float t, float seconds, bool atMe, bool reducedMotion)
        {
            float u = Clamp01(t);
            float peak = atMe ? 0.95f : 0.45f;
            float wave = reducedMotion ? 1f : 0.55f + (0.45f * (0.5f + (0.5f * (float)Math.Cos(Pulses(u, seconds) * 2.0 * Math.PI))));
            float fadeIn = FadeInShare > 0f ? Clamp01(u / FadeInShare) : 1f;
            return peak * wave * fadeIn;
        }

        /// <summary>The line's width in flight units: thin, a little thicker for the pilot it points at, and swelling
        /// toward the shot.</summary>
        public static float Width(float t, bool atMe) => (atMe ? 0.22f : 0.13f) * (1f + (0.8f * Clamp01(t)));

        /// <summary>The whole seconds left to show beside the warning (3, 2, 1) for <paramref name="remaining"/> seconds
        /// of charge; 0 once it fired.</summary>
        public static int Countdown(float remaining)
            => remaining <= 0f || float.IsNaN(remaining) ? 0 : (int)Math.Ceiling(Math.Min(remaining, 99f));

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }
}
