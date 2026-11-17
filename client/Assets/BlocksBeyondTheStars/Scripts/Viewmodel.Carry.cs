// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The first-person side of a block carried over the head (#3766): the held block rises to the top middle of the
    /// view (<see cref="BlockCarryPose.ViewShift"/>), so the player sees that they hold it up without it covering what
    /// they aim at.
    /// </summary>
    public sealed partial class Viewmodel
    {
        private bool _carrying;
        private float _carryView; // 0..1

        /// <summary>Starts or ends the overhead carry.</summary>
        public void SetCarrying(bool on) => _carrying = on;

        /// <summary>This frame's shift of the held item's rest position (zero when nothing is carried).</summary>
        private Vector3 CarryShift()
        {
            _carryView = BlockCarryPose.Ease(_carryView, _carrying, Time.deltaTime);
            var s = BlockCarryPose.ViewShift;
            return new Vector3(s.X, s.Y, s.Z) * _carryView;
        }
    }
}
