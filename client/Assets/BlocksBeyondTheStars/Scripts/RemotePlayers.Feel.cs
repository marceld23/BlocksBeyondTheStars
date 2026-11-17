// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The other players' figures in the feel pass: they play the emotes and body states the server passes on
    /// (<see cref="PlayerEmote"/> — a wave, a dance, a flinch, the fall), with the emote's small sound where they
    /// stand, and they swim where they are in water. The water is read on this side from the figure's own place, so
    /// it costs nothing on the wire.
    /// </summary>
    public sealed partial class RemotePlayers
    {
        private void OnEmote(PlayerEmote m)
        {
            if (m?.PlayerId == null || !_remotes.TryGetValue(m.PlayerId, out var r) || r.Avatar == null)
            {
                return;
            }

            r.Avatar.PlayEmote(m.Emote);
            EmoteSounds.PlayAt(m.Emote, r.Avatar.transform.position);
        }

        /// <summary>A figure up to its chest in water swims.</summary>
        private void PoseSwimming(PlayerAvatar avatar)
        {
            if (avatar != null && Game != null)
            {
                avatar.SetSwimming(Game.IsWaterAt(avatar.transform.position + (Vector3.up * 1.1f)));
            }
        }
    }
}
