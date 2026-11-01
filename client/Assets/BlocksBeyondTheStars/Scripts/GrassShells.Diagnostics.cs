// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One summary line of the meadow a few seconds into a world (#3056), for capture and bug-report logs: how
    /// many chunks carry tops, how many draw, how many instances went out last frame, and whether the material and atlas
    /// exist — the first thing to read when the carpet does not show.</summary>
    public sealed partial class GrassShells
    {
        private const int DiagnosticFrames = 300; // ~5 s at 60 fps after the first meadow frame
        private static int _diagStart = -1;
        private static bool _diagLogged;
        private static int _diagInstances;
        private static int _diagInstancesFrame;

        /// <summary>Counts the instances a carpet or flower draw sends this frame.</summary>
        private static void CountInstances(int count)
        {
            if (_diagInstancesFrame != Time.frameCount)
            {
                _diagInstancesFrame = Time.frameCount;
                _diagInstances = 0;
            }

            _diagInstances += count;
        }

        /// <summary>Called once per frame (TickFrame): logs the summary once, <see cref="DiagnosticFrames"/> after start.</summary>
        private static void DiagnosticTick()
        {
            if (_diagLogged)
            {
                return;
            }

            if (_diagStart < 0)
            {
                _diagStart = Time.frameCount;
                return;
            }

            if (Time.frameCount - _diagStart < DiagnosticFrames)
            {
                return;
            }

            _diagLogged = true;
            int withTops = 0, enabledCount = 0, inRange = 0;
            foreach (var shells in Live)
            {
                if (shells == null || shells._tops.Count == 0)
                {
                    continue;
                }

                withTops++;
                enabledCount += shells.enabled ? 1 : 0;
                inRange += DistanceSqToChunk(_camPos, shells.transform.position) < CarpetRange * CarpetRange ? 1 : 0;
            }

            int lastFrame = _diagInstancesFrame == Time.frameCount - 1 ? _diagInstances : 0;
            Debug.Log($"[GrassShells] chunks with tops={withTops} enabled={enabledCount} in range={inRange} " +
                      $"instances last frame={lastFrame} material={MeadowAssets.ShellMaterial != null} atlas={_atlas != null} " +
                      $"grass={HighDetails.Grass} flowers={HighDetails.Flowers}");
        }
    }
}
