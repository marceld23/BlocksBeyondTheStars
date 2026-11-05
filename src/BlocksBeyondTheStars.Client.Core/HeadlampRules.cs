// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Unity-free rules of the suit headlamp (#3229–#3234), so the headless tests pin them: which other players' lamps
    /// the shaders get (#3234), what a lit lamp counts as for the eye adaptation (#3230), and where the lamp sits and aims
    /// from the eye (#3229). The Unity side (<c>Headlamp</c>, <c>PlayerController.Gear</c>, <c>RemotePlayers.Lamps</c>)
    /// publishes the result as shader globals.
    /// </summary>
    public static class HeadlampRules
    {
        /// <summary>Slots for other players' lamps — the size of the shader arrays (<c>BBTS_REMOTE_LAMPS</c>).</summary>
        public const int MaxRemoteLamps = 4;

        /// <summary>Another player's lamp further than this from the camera lights nothing here (m): beyond its own range
        /// plus a margin, so a beam pointed at you from afar still reaches.</summary>
        public const float RemoteReach = 48f;

        /// <summary>What a lit headlamp counts as in the eye adaptation's scene brightness (0..1, #3230): the eyes adapt to
        /// the lit spot, so a dark cave earns much less of the +EV boost and everything outside the beam reads darker.</summary>
        public const float AdaptationBrightness = 0.35f;

        /// <summary>The lamp on the helmet, from the eye in the first person (m, camera space: right, up, forward) — the
        /// right side of the helmet, where the avatar's lamp sits.</summary>
        public static readonly Vector3f HelmetOffset = new Vector3f(0.13f, 0.08f, 0.05f);

        /// <summary>How far ahead of the eye the beam axis crosses the line of sight (m): the hotspot sits on the
        /// crosshair at a typical working distance.</summary>
        public const float AimDistance = 9f;

        /// <summary>
        /// The nearest lit lamps of other players within <paramref name="reach"/> of <paramref name="viewer"/>, at most
        /// <paramref name="max"/>, nearest first: their indexes in <paramref name="lamps"/> go into <paramref name="into"/>
        /// (cleared first). Positions are scene positions; NaN marks a lamp that is off.
        /// </summary>
        public static void SelectRemote(IReadOnlyList<Vector3f> lamps, Vector3f viewer, float reach, int max, List<int> into)
        {
            into.Clear();
            if (max <= 0)
            {
                return;
            }

            float reach2 = reach * reach;
            for (int i = 0; i < lamps.Count; i++)
            {
                var p = lamps[i];
                if (float.IsNaN(p.X) || p.DistanceSquared(viewer) > reach2)
                {
                    continue;
                }

                into.Add(i);
            }

            into.Sort((a, b) => lamps[a].DistanceSquared(viewer).CompareTo(lamps[b].DistanceSquared(viewer)));
            if (into.Count > max)
            {
                into.RemoveRange(max, into.Count - max);
            }
        }

        /// <summary>#3230: the scene brightness the eye adaptation follows, with the player's own lamp lit or not.</summary>
        public static float AdaptedBrightness(float sceneBrightness, bool lampLit)
            => lampLit ? Math.Max(sceneBrightness, AdaptationBrightness) : sceneBrightness;
    }
}
