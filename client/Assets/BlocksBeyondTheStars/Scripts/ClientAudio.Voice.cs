// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The figure's voice in the audio layer (#3767). The figure's own mouth and breath sounds (<see cref="PlayerVoice.IsVoiceClip"/>:
    /// the breaths, the gasp, the strain, the pain grunt, the fall) are played in the voice the player chose — the pitch as
    /// the playback pitch, the age as a timbre baked into the clip (<see cref="SampleKit.Variant"/>, cached per clip and
    /// timbre, so the browser hears it too). <see cref="PlayOwn"/> applies it to every such clip; the cues that used to go
    /// through <see cref="Cue"/> — which cannot pitch — come through <see cref="Voice"/>.
    /// </summary>
    public sealed partial class ClientAudio
    {
        /// <summary>The own figure's voice (<see cref="PlayerVoice.Pack"/>); 0 = the middle.</summary>
        public int OwnVoice => Settings != null ? Settings.VoicePacked : 0;

        /// <summary>One of the own figure's voice sounds, 2D, in the chosen voice — its next take when it has several.</summary>
        public void Voice(string id, float vol = 1f, float pitch = 1f) => PlayOwn(Take(id), vol, pitch);

        /// <summary>Another figure's voice sound where it stands (a hit, a fall), in THAT player's voice.</summary>
        public void VoiceAt(string id, Vector3 pos, int packedVoice, float vol)
        {
            if (!_clips.TryGetValue(Take(id), out var clip) || clip == null)
            {
                return;
            }

            PlayAt(VoiceTimbre(clip, packedVoice), pos, PlayerVoice.Factor(packedVoice), vol, id, AudioCategory.RemotePlayer);
        }

        /// <summary>A sample of a voice for the character editor: one breath, 2D, in <paramref name="packedVoice"/> — whatever
        /// the saved voice is, so the sliders can be heard before Save.</summary>
        public void VoiceSample(int packedVoice)
        {
            if (!_clips.TryGetValue("breath_exhale", out var clip) || clip == null)
            {
                return;
            }

            var src = OwnSource();
            src.clip = VoiceTimbre(clip, packedVoice);
            src.pitch = Mathf.Clamp(PlayerVoice.Factor(packedVoice), 0.5f, 2f);
            src.volume = Mathf.Clamp01(0.9f * SfxVol());
            src.Play();
        }

        /// <summary>The clip with the age's timbre baked in (the clip itself for the middle).</summary>
        private static AudioClip VoiceTimbre(AudioClip clip, int packedVoice, int tail = 0)
        {
            var (op, amount) = PlayerVoice.Timbre(PlayerVoice.Unpack(packedVoice).Age);
            return op == VoiceOp.None && tail == 0 ? clip : SampleKit.Variant(clip, null, op, amount, tail);
        }
    }
}
