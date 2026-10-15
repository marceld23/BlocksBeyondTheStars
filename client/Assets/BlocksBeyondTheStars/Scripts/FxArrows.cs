// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2647: a hunter's arrow in flight (the server's <c>WorldFx</c> kind <c>"arrow"</c>). A small arrow of cube parts — a
    /// light wooden shaft, an iron tip, a pair of red fletchings — leaves the bow at the release, flies its short arc
    /// (<see cref="ArrowFlight"/>, pointing along it) to the animal and is gone: no damage, no shake, no corpse. The
    /// animal's sparkle comes from the creature-defeated path, which waits for the arrow (<see cref="ArrowLandings"/>).
    /// </summary>
    public static class FxArrows
    {
        private static readonly Color Shaft = new Color(0.80f, 0.64f, 0.42f);
        private static readonly Color Tip = new Color(0.58f, 0.60f, 0.64f);
        private static readonly Color Fletching = new Color(0.88f, 0.30f, 0.24f);
        private static readonly Color Trail = new Color(1f, 0.96f, 0.85f);

        /// <summary>Flies an arrow from <paramref name="from"/> to <paramref name="to"/> (scene space), leaving after
        /// <paramref name="delay"/> seconds (the hunter's release). Returns the flight time in seconds.</summary>
        public static float Fly(Vector3 from, Vector3 to, float delay)
        {
            var a = new System.Numerics.Vector3(from.x, from.y, from.z);
            var b = new System.Numerics.Vector3(to.x, to.y, to.z);
            float flight = ArrowFlight.Seconds(a, b);
            delay = Mathf.Max(0f, delay);

            var root = new GameObject("Arrow");
            root.transform.SetParent(FxKit.Ensure().transform, false);
            HeldItem.SolidPart(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.022f, 0.022f, 0.62f), Shaft);
            HeldItem.SolidPart(root.transform, new Vector3(0f, 0f, 0.33f), new Vector3(0.045f, 0.045f, 0.07f), Tip);
            HeldItem.SolidPart(root.transform, new Vector3(0f, 0f, -0.25f), new Vector3(0.006f, 0.07f, 0.11f), Fletching);
            HeldItem.SolidPart(root.transform, new Vector3(0f, 0f, -0.25f), new Vector3(0.07f, 0.006f, 0.11f), Fletching);
            root.SetActive(false); // on the string until the release

            float total = delay + flight;
            bool trail = FxKit.Rich;
            FxKit.Animate(total, t =>
            {
                float elapsed = t * total;
                if (elapsed < delay)
                {
                    return;
                }

                float c = Mathf.Clamp01((elapsed - delay) / flight);
                var p = ArrowFlight.Position(a, b, c);
                var d = ArrowFlight.Direction(a, b, c);
                var pos = new Vector3(p.X, p.Y, p.Z);
                root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(d.X, d.Y, d.Z)));
                if (!root.activeSelf)
                {
                    root.SetActive(true);
                }

                if (trail)
                {
                    FxKit.Emit(FxKit.Kind.Motes, pos, Vector3.zero, 0.035f, 0.22f, Trail);
                }
            }, () => Object.Destroy(root), root);
            return flight;
        }
    }
}
