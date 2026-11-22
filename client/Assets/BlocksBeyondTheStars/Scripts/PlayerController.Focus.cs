// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    public sealed partial class PlayerController
    {
        /// <summary>How far away the thing in the middle of the view is — the first block the look ray meets
        /// (a plant or a leaf counts, water does not), or <paramref name="max"/> when there is nothing within it (the
        /// sky, the far distance, no world). The depth of field focuses there (<c>UrpScenePost</c>).</summary>
        public float FocusDistance(float max)
        {
            if (Camera == null)
            {
                return max;
            }

            var eye = Camera.transform;
            return TerrainHit(eye.position, eye.forward, max, out _, out float distance) ? distance : max;
        }
    }
}
