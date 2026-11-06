// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Fog banks in the air (#3273) as Unity-free rules. The haze is mixed into the surface a pixel hits, so on its own it
    /// can never put a bank of mist in front of the open sky; a few large, soft, camera-facing cards at the valley fog floor
    /// do. This class holds their budget per quality preset, where a card may stand, how it fades (never "in your face",
    /// gone toward the far edge of the ring), its life cycle and the cap on the screen area all cards together may cover
    /// (overdraw on small hardware). The Unity side (<c>MistCards</c>) places, drifts and draws them; the
    /// <c>MistCard</c> shader repeats <see cref="PixelNearFade"/> per pixel.
    /// <para>Rule of the package: a few large cards, never many small particles (Valheim's thousands of fog particles are
    /// a known frame-rate sink, and its mods replace them with a few ground-mist planes).</para>
    /// </summary>
    public static class MistCardRules
    {
        /// <summary>The most cards any preset gets (High).</summary>
        public const int MaxBudget = 20;

        /// <summary>The ring around the camera the cards are spread over (horizontal metres).</summary>
        public const float RingInner = 15f, RingOuter = 70f;

        /// <summary>How far before <see cref="RingOuter"/> a card starts to fade out (metres).</summary>
        public const float FarFadeLength = 15f;

        /// <summary>Card width range (metres); the height is a share of it (<see cref="MinAspect"/>..<see cref="MaxAspect"/>) —
        /// a bank is wide and low.</summary>
        public const float MinWidth = 6f, MaxWidth = 20f;

        /// <summary>Card height as a share of its width.</summary>
        public const float MinAspect = 0.32f, MaxAspect = 0.5f;

        /// <summary>How high above the valley fog floor a card's centre hangs (metres).</summary>
        public const float MinLift = 1f, MaxLift = 4f;

        /// <summary>Ground that stands higher than this above the fog floor is a hill or a ridge: no bank there (metres).</summary>
        public const float HillTolerance = 3f;

        /// <summary>Per pixel (the shader): nothing of a card within this distance of the camera, fully there from
        /// <see cref="PixelNearStart"/> + <see cref="PixelNearLength"/> (metres).</summary>
        public const float PixelNearStart = 2f, PixelNearLength = 8f;

        /// <summary>The strongest a single card gets (alpha at its centre, before the noise).</summary>
        public const float MaxOpacity = 0.55f;

        /// <summary>Below this mist strength (0..1) there are no banks at all.</summary>
        public const float MistThreshold = 0.1f;

        /// <summary>Fade in and fade out at the start and the end of a card's life (seconds).</summary>
        public const float LifeFade = 6f;

        /// <summary>How many cards a preset affords: Potato 0 · Low 4 · Medium 10 · High 20. The browser gets none.</summary>
        /// <param name="preset">The quality preset as its index: 0 Potato, 1 Low, 2 Medium, 3 High.</param>
        /// <param name="browser">A WebGL/WebGPU (or other light-device) client.</param>
        public static int Budget(int preset, bool browser)
        {
            if (browser || preset <= 0)
            {
                return 0;
            }

            if (preset == 1)
            {
                return 4;
            }

            return preset == 2 ? 10 : MaxBudget;
        }

        /// <summary>How much of the screen all visible cards together may cover (in screens) before the nearest are let go:
        /// Low 0.6, Medium 1.0, High 1.4. Each screen covered is a full-screen layer of alpha blending.</summary>
        public static float CoverageCap(int preset)
        {
            if (preset <= 1)
            {
                return 0.6f;
            }

            return preset == 2 ? 1f : 1.4f;
        }

        /// <summary>0..1 share of the budget that shows at a mist strength (the world's mist × time of day × weather):
        /// nothing below <see cref="MistThreshold"/>, the full budget from 0.55 up.</summary>
        public static float Presence(float mist) => Clamp01((mist - MistThreshold) / 0.45f);

        /// <summary>How many of the <paramref name="budget"/> cards show at a mist strength — at least one as soon as there is
        /// any mist to show (it is faint then, see <see cref="Opacity"/>).</summary>
        public static int ActiveCount(int budget, float mist)
        {
            if (budget <= 0)
            {
                return 0;
            }

            float presence = Presence(mist);
            return presence <= 0f ? 0 : Math.Min(budget, (int)Math.Ceiling(budget * presence));
        }

        /// <summary>A card's peak alpha at a mist strength: 0 without mist, 0.15 at the threshold, up to
        /// <see cref="MaxOpacity"/> in thick dawn or fog-weather mist.</summary>
        public static float Opacity(float mist)
        {
            if (mist <= MistThreshold)
            {
                return 0f;
            }

            return 0.15f + (MaxOpacity - 0.15f) * Clamp01((mist - MistThreshold) / 0.7f);
        }

        /// <summary>The shader's per-pixel fade by the distance to the camera: 0 within <see cref="PixelNearStart"/>, 1 from
        /// <see cref="PixelNearStart"/> + <see cref="PixelNearLength"/> on — a card is never "in your face".</summary>
        public static float PixelNearFade(float distance) => Clamp01((distance - PixelNearStart) / PixelNearLength);

        /// <summary>A whole card's fade by the camera's distance to its centre: gone while the camera would stand inside it
        /// (within half its width + 1 m), fully there once its nearest edge is 6 m away.</summary>
        public static float NearFade(float distance, float halfWidth) => Clamp01((distance - halfWidth - 1f) / 5f);

        /// <summary>Fade toward the far edge of the ring by the horizontal distance to the camera: 1 inside, 0 at
        /// <see cref="RingOuter"/>.</summary>
        public static float FarFade(float horizontalDistance)
            => 1f - SmoothStep(RingOuter - FarFadeLength, RingOuter, horizontalDistance);

        /// <summary>Whether a card left its ring and should dissolve and come back elsewhere: beyond the outer edge, or the
        /// camera walked into it.</summary>
        public static bool OutOfRing(float horizontalDistance, float halfWidth)
            => horizontalDistance > RingOuter + 5f || horizontalDistance < Math.Min(RingInner * 0.5f, halfWidth + 1f);

        /// <summary>0..1 a card's life cycle: fades in over the first <see cref="LifeFade"/> seconds, out over the last ones
        /// (a short life fades in and out over half of it each).</summary>
        public static float Lifecycle(float age, float life)
        {
            if (life <= 0f || age <= 0f || age >= life)
            {
                return 0f;
            }

            float fade = Math.Min(LifeFade, life * 0.5f);
            return Clamp01(Math.Min(age, life - age) / fade);
        }

        /// <summary>Whether a spot can hold a bank: its ground lies at most <see cref="HillTolerance"/> above the valley fog
        /// floor (mist fills valleys, it does not climb hills).</summary>
        public static bool SpotHoldsMist(float groundTop, float fogFloor) => groundTop <= fogFloor + HillTolerance;

        /// <summary>The world height of a card's centre: <see cref="MinLift"/>..<see cref="MaxLift"/> above the fog floor
        /// (<paramref name="lift01"/> picks where), but never so low that the ground under it cuts more than its bottom
        /// edge.</summary>
        public static float CentreY(float fogFloor, float groundTop, float lift01, float cardHeight)
        {
            float y = fogFloor + MinLift + (MaxLift - MinLift) * Clamp01(lift01);
            return Math.Max(y, groundTop + cardHeight * 0.35f);
        }

        /// <summary>The share of the screen (0..1) a card of <paramref name="width"/> × <paramref name="height"/> metres
        /// covers at <paramref name="distance"/> metres, for a camera with tan(vertical FOV / 2) =
        /// <paramref name="tanHalfFovY"/> and width/height <paramref name="aspect"/>. More than the whole screen counts as
        /// one screen (the rest is clipped and costs nothing).</summary>
        public static float ScreenArea(float width, float height, float distance, float tanHalfFovY, float aspect)
        {
            float viewH = 2f * Math.Max(distance, 0.01f) * Math.Max(tanHalfFovY, 1e-3f);
            float viewW = viewH * Math.Max(aspect, 1e-3f);
            return Clamp01(width / viewW) * Clamp01(height / viewH);
        }

        /// <summary>The coverage cap: while the cards marked in <paramref name="keep"/> together cover more than
        /// <paramref name="cap"/> screens, the largest (= nearest) one is let go. Returns how many were let go.</summary>
        public static int ApplyCoverageCap(float[] areas, bool[] keep, int count, float cap)
        {
            if (areas == null || keep == null)
            {
                return 0;
            }

            int n = Math.Min(count, Math.Min(areas.Length, keep.Length));
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                {
                    total += areas[i];
                }
            }

            int dropped = 0;
            while (total > cap)
            {
                int largest = -1;
                for (int i = 0; i < n; i++)
                {
                    if (keep[i] && (largest < 0 || areas[i] > areas[largest]))
                    {
                        largest = i;
                    }
                }

                if (largest < 0)
                {
                    break;
                }

                keep[largest] = false;
                total -= areas[largest];
                dropped++;
            }

            return dropped;
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        internal static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
