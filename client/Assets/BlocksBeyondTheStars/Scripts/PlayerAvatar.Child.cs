// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: a child's proportions (<see cref="ChildFigure"/>). Called once, right after <c>Build</c>, for a
    /// villager with the look <see cref="ChildFigure.Look"/>: the head grows in place (the neck stays, the head rises), and
    /// with it everything that hangs on it — the face, the hair. The figure's overall size is the caller's scale on the
    /// root; arms and legs keep their joints, so every gesture a grown-up plays works unchanged.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        public void SetChildProportions()
        {
            if (_head == null)
            {
                return;
            }

            _head.localScale = Vector3.one * (ChildFigure.HeadSize * ChildFigure.HeadScale);
            var at = _head.localPosition;
            _head.localPosition = new Vector3(at.x, ChildFigure.ChildHeadCentre, at.z);
        }
    }
}
