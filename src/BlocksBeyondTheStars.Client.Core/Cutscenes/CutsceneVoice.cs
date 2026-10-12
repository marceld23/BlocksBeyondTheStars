// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// #2495: a narrator's non-verbal vocoder voice — VEGA's chatter (#761), the short ElevenLabs clips chained while her text
    /// types. The Unity-free half of the client's <c>NarratorVoice</c>, which VEGA's panel and the cutscene director's speaker
    /// box share: which clips a speaker has (<see cref="VoiceOf"/>), when a speaker box chatters (<see cref="ShouldChatter"/>),
    /// which clip comes next (<see cref="PickClip"/>, never the same twice in a row), the pitch jitter and the volume. No words,
    /// so nothing to translate or re-record when a text changes.
    /// </summary>
    public static class CutsceneVoice
    {
        /// <summary>VEGA's clips when a pack names none (her panel always speaks with these).</summary>
        public static readonly IReadOnlyList<string> VegaDefault = new[] { "vega_chatter_1", "vega_chatter_2", "vega_chatter_3" };

        /// <summary>The lowest pitch of a clip (a touch of jitter so the babble never sounds looped).</summary>
        public const float PitchMin = 0.94f;

        /// <summary>The highest pitch of a clip.</summary>
        public const float PitchMax = 1.08f;

        /// <summary>The voice's share of the master × SFX volume.</summary>
        public const float VolumeScale = 0.5f;

        /// <summary>
        /// The clips a speaker box chatters with: the <c>voice</c> of the speaker <paramref name="speakerId"/> names (empty =
        /// the pack's narrator) — the narrator or one of the pack's <c>speakers</c> — else, for VEGA (<c>vega</c>), her
        /// <see cref="VegaDefault"/>. Any other speaker without a voice is silent (the Guardian core: its line-start hum only).
        /// </summary>
        public static IReadOnlyList<string> VoiceOf(StoryDefinition? pack, string? speakerId)
        {
            string narratorId = string.IsNullOrEmpty(pack?.Narrator?.Id) ? StoryNarrator.DefaultId : pack!.Narrator.Id;
            string id = string.IsNullOrEmpty(speakerId) ? narratorId : speakerId!;
            var clips = pack?.FindSpeaker(id)?.Voice;
            int named = 0;
            if (clips is not null)
            {
                foreach (string clip in clips)
                {
                    if (!string.IsNullOrEmpty(clip))
                    {
                        named++;
                    }
                }
            }

            if (clips is not null && named > 0)
            {
                // The pack's own list as it is (no copy per frame — the director asks every frame a speaker box shows); only a
                // list with an empty name in it is filtered.
                return named == clips.Count ? (IReadOnlyList<string>)clips : clips.Where(c => !string.IsNullOrEmpty(c)).ToArray();
            }

            return id == StoryNarrator.DefaultId ? VegaDefault : Array.Empty<string>();
        }

        /// <summary>
        /// Whether a speaker box chatters now: a <c>speaker</c> caption of a speaker with a voice, not closed by the reader
        /// (a closed caption fades out in silence). A page-gated box ("Story text: wait for me") chatters while its text
        /// types — the continue control completing the typing stops it; a timed box shows its page whole, so it chatters for
        /// as long as the page would take to type (<see cref="SpeakingSeconds"/>, measured from the page's start,
        /// <paramref name="pageAge"/>). Subtitles, reading frames, title cards, credits and banners never chatter — the
        /// prologue's narration ("You wake…") stays silent.
        /// </summary>
        public static bool ShouldChatter(CaptionView view, float pageAge, bool hasVoice)
            => ShouldChatter(view.Caption?.Style, view.Caption?.PageGated ?? false, view.Closing, view.VisibleChars,
                view.Text?.Length ?? 0, pageAge, hasVoice);

        /// <summary><see cref="ShouldChatter(CaptionView, float, bool)"/> on the caption's facts.</summary>
        public static bool ShouldChatter(string? style, bool pageGated, bool closing, int visibleChars, int textLength, float pageAge,
            bool hasVoice)
        {
            if (!hasVoice || closing || textLength <= 0 || style != CutsceneCaptionStyles.Speaker)
            {
                return false;
            }

            return pageGated ? visibleChars < textLength : pageAge >= 0f && pageAge < SpeakingSeconds(textLength);
        }

        /// <summary>How long a page of <paramref name="chars"/> characters takes to type in a speaker box
        /// (<see cref="CutscenePlan.TypewriterCharsPerSecond"/>).</summary>
        public static float SpeakingSeconds(int chars) => chars <= 0 ? 0f : chars / CutscenePlan.TypewriterCharsPerSecond;

        /// <summary>
        /// The next clip of <paramref name="count"/>: a random one (<paramref name="roll"/> in 0..1) that is never
        /// <paramref name="previous"/> (the clip just played; −1 = none) when there is a choice. −1 when there are no clips.
        /// </summary>
        public static int PickClip(int count, int previous, float roll)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (count == 1)
            {
                return 0;
            }

            bool avoid = previous >= 0 && previous < count;
            int choices = avoid ? count - 1 : count;
            int pick = Math.Min(choices - 1, (int)(Clamp01(roll) * choices));
            if (avoid && pick >= previous)
            {
                pick++;
            }

            return pick;
        }

        /// <summary>A clip's pitch for <paramref name="roll"/> in 0..1 (<see cref="PitchMin"/>…<see cref="PitchMax"/>).</summary>
        public static float Pitch(float roll) => PitchMin + (Clamp01(roll) * (PitchMax - PitchMin));

        /// <summary>The voice's volume: master × SFX (clamped to 1) × <see cref="VolumeScale"/> — the panel's level since #761.
        /// No separate mute; the master bus applies once more on the listener (kept for parity).</summary>
        public static float Volume(float master, float sfx) => Clamp01(master * sfx) * VolumeScale;

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
