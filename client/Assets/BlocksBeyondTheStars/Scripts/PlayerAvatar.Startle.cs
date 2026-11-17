// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Startled (#3769 — Justus: NPCs "raise their hands and open their mouth wide" when somebody fires a weapon next to
    /// them): both hands straight up and the stock mouth wide open for a moment, then back to rest. It rides on the
    /// cheer's arms-up slot (one arm gesture at a time) with its own curve (<see cref="AvatarGestures.HandsUp"/>): no
    /// pumping, a faster rise, a longer hold. A figure with a painted face has no stock mouth to open; its hands still go up.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private bool _startled; // the arms-up gesture that plays is the startle, not the cheer
        private float _armsUpSeconds = AvatarGestures.CheerSeconds;

        /// <summary>Both hands up and the mouth wide open (~1.6 s). Ignored while lying.</summary>
        public void Startle()
        {
            if (_lying)
            {
                return;
            }

            _startled = true;
            _armsUpSeconds = AvatarGestures.HandsUpSeconds;
            _cheerTimer = _armsUpSeconds;
            _waveTimer = 0f;
        }

        /// <summary>The arms-up gesture this frame: how far it is blended in, the cheer's bob (none while startled) and how
        /// high the arms go (degrees of roll).</summary>
        private (float Blend, float Bob, float Lift) ArmsUp()
        {
            float c = 1f - Mathf.Clamp01(_cheerTimer / _armsUpSeconds);
            if (_startled)
            {
                return (AvatarGestures.HandsUp(c).Blend, 0f, AvatarGestures.HandsUpLift);
            }

            var (blend, bob) = AvatarGestures.Cheer(c);
            return (blend, bob, 150f);
        }

        /// <summary>The mouth's height factor from the startle (1 = at rest).</summary>
        private float GaspFactor()
        {
            if (!_startled || _cheerTimer <= 0f)
            {
                return 1f;
            }

            float gasp = AvatarGestures.HandsUp(1f - Mathf.Clamp01(_cheerTimer / _armsUpSeconds)).Gasp;
            return 1f + ((AvatarGestures.GaspMouth - 1f) * gasp);
        }
    }
}
