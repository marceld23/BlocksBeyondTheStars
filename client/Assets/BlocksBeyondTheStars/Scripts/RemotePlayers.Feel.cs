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
    /// stand, they move the mouth while their player speaks, and they swim where they are in water. The water is read
    /// on this side from the figure's own place, so it costs nothing on the wire.
    /// </summary>
    public sealed partial class RemotePlayers
    {
        /// <summary>How long a defeated player's figure stays where it fell, seconds. The server puts the player back at
        /// the ship in the tick of the fall; a figure that followed its presence there at once got up again on the way
        /// (the avatar stands a downed figure up once it is somewhere else), so the fall showed for a quarter of a second.</summary>
        private const float FallHoldSeconds = 1f;

        /// <summary>The live instance (one per world rig) — the voice chat moves a speaker's mouth through it. Null
        /// outside a world.</summary>
        public static RemotePlayers Current { get; private set; }

        private void Awake() => Current = this;

        private void OnEmote(PlayerEmote m)
        {
            if (m?.PlayerId == null || !_remotes.TryGetValue(m.PlayerId, out var r) || r.Avatar == null)
            {
                return;
            }

            r.Avatar.PlayEmote(m.Emote);
            var at = r.Avatar.transform.position;
            if (m.Emote == PlayerEmotes.Down && !r.Hidden && !r.TimedOut)
            {
                r.FellUntil = Time.time + FallHoldSeconds; // a figure that is drawn; one behind a stealth field stays unseen
            }

            if (m.Emote is PlayerEmotes.Hurt or PlayerEmotes.Down)
            {
                // #3767: the pain grunt and the fall, where the figure stands, in THAT player's voice. A quiet flinch
                // (damage that ticks: thin air, cold, fire) is seen and not heard — a grunt with each never stopped.
                if (m.Emote == PlayerEmotes.Down || !m.Quiet)
                {
                    ClientAudio.Instance?.VoiceAt(m.Emote == PlayerEmotes.Hurt ? "hurt_player" : "player_death", at, m.Voice, 0.7f);
                }

                return;
            }

            EmoteSounds.PlayAt(m.Emote, at);
        }

        /// <summary>True while a figure is held where it fell (<see cref="FallHoldSeconds"/>): the presence neither moves
        /// nor hides it then. Asked every frame; the call that finds the time over ends the hold — the figure shows as
        /// the presence has it by now, and the frame's placing puts it where the presence is, in one step (the
        /// interpolation arrived there long ago, so nothing slides across the map). There it stands: it is somewhere else.</summary>
        private static bool HeldWhereItFell(Remote r)
        {
            if (r.FellUntil <= 0f)
            {
                return false;
            }

            if (Time.time < r.FellUntil)
            {
                return true;
            }

            r.FellUntil = 0f;
            r.Avatar.SetVisible(!r.Hidden && !r.TimedOut);
            return false;
        }

        /// <summary>The player speaks — a voice-chat frame of theirs was taken for playback: the figure's mouth moves for
        /// <paramref name="seconds"/>. Called with every frame, so it bridges the gap to the next one.</summary>
        public void Talk(string playerId, float seconds = 0.25f)
        {
            if (playerId != null && _remotes.TryGetValue(playerId, out var r) && r.Avatar != null)
            {
                r.Avatar.Talk(seconds);
            }
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
