// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's part of the look's tiers (#3946, <c>GameLook.Tiers.cs</c>): what the chunk mesher emits depends on the
    /// preset — the near drawers' data, the crown far forms, the soft edges of forms. A chunk meshed on one tier beside a
    /// chunk meshed on another can open a crown at their seam, and a chunk meshed without plant cells under a preset that
    /// hides the crossed pictures near the eye would lose its plants there. So when the preset changes what the mesher
    /// emits, every loaded chunk is meshed again, nearest first, through the ordinary dirty queue (in the browser within
    /// its per-frame build budget).
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The <see cref="GameLook.MeshTierKey"/> the loaded chunks were meshed with; -1 until the first tick.</summary>
        private int _lookMeshTierSeen = -1;

        /// <summary>From the chunk tick, before the dirty chunks are dispatched.</summary>
        private void RemeshOnLookTierChange()
        {
            int tier = GameLook.MeshTierKey;
            if (tier == _lookMeshTierSeen)
            {
                return;
            }

            bool first = _lookMeshTierSeen < 0;
            _lookMeshTierSeen = tier;
            if (first)
            {
                return; // nothing was meshed before the first tick
            }

            foreach (var coord in _chunkObjects.Keys)
            {
                _dirty.Add(coord);
            }
        }
    }
}
