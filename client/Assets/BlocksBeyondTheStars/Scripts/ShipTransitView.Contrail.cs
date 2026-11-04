// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3177: the 20 seconds between "a trader ship is coming in to land — look up!" and its touchdown. The server sends a landing
    /// FX with <c>ApproachSeconds</c> set when the trader decides to land; this draws a bright glint high in the sky gliding in
    /// towards the pad and leaving a white contrail, so there is something to look up at. It ends above the pad, where the
    /// touchdown flight (<see cref="ShipTransitView"/>'s cinematic) takes over.
    /// </summary>
    public sealed partial class ShipTransitView
    {
        /// <summary>How far out (blocks, horizontally) the glint starts.</summary>
        private const float ContrailReach = 520f;

        /// <summary>How high above the pad the glint starts.</summary>
        private const float ContrailStartHeight = 210f;

        /// <summary>How high above the pad the contrail ends — a little above the touchdown flight's start.</summary>
        private const float ContrailEndHeight = 70f;

        /// <summary>Seconds between two contrail puffs.</summary>
        private const float ContrailPuffEvery = 0.08f;

        private void StartContrail(BlocksBeyondTheStars.Networking.Messages.ShipTransitFx fx)
        {
            StartCoroutine(Contrail(fx));
        }

        private IEnumerator Contrail(BlocksBeyondTheStars.Networking.Messages.ShipTransitFx fx)
        {
            var end = new Vector3(Game.SceneX(fx.X), fx.Y + ContrailEndHeight, Game.SceneZ(fx.Z));
            float bearing = ContrailBearing(fx.PlayerId);
            var start = end + new Vector3(Mathf.Cos(bearing) * ContrailReach, ContrailStartHeight - ContrailEndHeight, Mathf.Sin(bearing) * ContrailReach);
            float duration = Mathf.Max(4f, fx.ApproachSeconds - 1.5f);
            float t = 0f, puff = 0f;
            var glint = new Color(1f, 0.95f, 0.82f);
            var vapour = new Color(0.97f, 0.98f, 1f, 0.55f);
            while (t < duration)
            {
                if (Game == null || Game.SpaceViewActive)
                {
                    yield break;
                }

                t += Time.deltaTime;
                float u = 1f - Mathf.Clamp01(t / duration);
                var at = Vector3.Lerp(start, end, 1f - u * u); // eases in: fast far out, slowing down over the pad
                FxKit.Emit(FxKit.Kind.Glow, at, Vector3.zero, 2.4f, 0.12f, glint);
                puff += Time.deltaTime;
                if (puff >= ContrailPuffEvery)
                {
                    puff = 0f;
                    FxKit.Emit(FxKit.Kind.Smoke, at, Random.insideUnitSphere * 0.4f, Random.Range(3f, 4.5f), Random.Range(6f, 8f), vapour);
                }

                yield return null;
            }
        }

        /// <summary>A direction the ship comes in from, stable per ship (FNV-1a over its id), in radians.</summary>
        private static float ContrailBearing(string id)
        {
            uint h = 2166136261;
            foreach (char c in id ?? string.Empty)
            {
                h = (h ^ c) * 16777619;
            }

            return h % 3600 / 3600f * Mathf.PI * 2f;
        }
    }
}
