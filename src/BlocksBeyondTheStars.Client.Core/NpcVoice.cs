// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#2592: the six wordless speech voices an NPC can have — three human (female, male, neutral) and three robot
    /// (bright, deep, neutral) banks of three clips each (<c>npc_voice_{human,robot}_{f,m,n}_{1..3}</c>).</summary>
    public enum NpcVoiceBank : byte
    {
        HumanFemale,
        HumanMale,
        HumanNeutral,
        RobotBright,
        RobotDeep,
        RobotNeutral,
    }

    /// <summary>#2592: the short non-verbal reaction sounds an NPC plays on top of its speech (an optional layer, see
    /// <see cref="NpcVoice.ReactionsEnabled"/>): a quiet idle murmur, a trade confirmation, an alarm.</summary>
    public enum NpcReaction : byte
    {
        Idle,
        Trade,
        Alert,
    }

    /// <summary>
    /// #2592: an NPC's wordless speech voice — like VEGA's (<see cref="Cutscenes.CutsceneVoice"/>): whenever an NPC shows a
    /// speech bubble, a short babble of one to three vocoder clips plays at the NPC, never the same clip twice in a row. No
    /// words, so nothing to translate. The Unity-free half; <c>NpcView</c> plays it through the voice budget
    /// (<see cref="NpcVoiceGate"/>).
    /// <list type="bullet">
    /// <item><b>Voice per NPC</b> (<see cref="VoiceFor"/>), deterministic from the face seed the avatar is built with, so it
    /// matches the look: a human with facial hair speaks male (sometimes neutral), never female; other humans female, male
    /// or neutral; androids one of the robot banks; the G.D.S. guardians the deep robot voice.</item>
    /// <item><b>Pitch per NPC</b> (<see cref="Pitch"/>): stable 0.92–1.08 (a human's also follows the body size), plus a
    /// ±2 % jitter per clip (<see cref="ClipPitch"/>).</item>
    /// <item><b>Babble length</b> (<see cref="BabblePlan"/>): one clip per ~40 characters of the bubble, 1–3 clips.</item>
    /// <item><b>Reactions</b> (<see cref="ReactionClip"/>): the <c>npc_human_*</c> / robot idle, trade and alert clips,
    /// behind <see cref="ReactionsEnabled"/>; the robot bank is the single prefix <see cref="RobotReactionPrefix"/>.</item>
    /// </list>
    /// </summary>
    public static class NpcVoice
    {
        /// <summary>Clips per speech bank.</summary>
        public const int ClipsPerBank = 3;

        /// <summary>About one clip per this many characters of a bubble.</summary>
        public const int CharsPerClip = 40;

        /// <summary>A babble never chains more clips than this.</summary>
        public const int MaxClips = 3;

        /// <summary>The nominal length of one speech clip (the real clip length wins where it is known).</summary>
        public const float ClipSeconds = 1.6f;

        /// <summary>The speech clips' name prefix (<c>Resources/audio/npc_voice_*</c>).</summary>
        public const string SpeechPrefix = "npc_voice_";

        /// <summary>The human reaction clips' prefix (<c>npc_human_{idle,trade,alert}</c>).</summary>
        public const string HumanReactionPrefix = "npc_human_";

        /// <summary>The robot reaction clips' prefix — ONE switch (#2589 E15, decided by the maintainer's sound review
        /// 2026-10-14: the existing <c>npc_alien_*</c> clips).</summary>
        public const string RobotReactionPrefix = "npc_alien_";

        /// <summary>The reaction layer (trade confirmation, alarm, idle murmur) on top of the speech babble — ONE switch
        /// (kept on by the maintainer's sound review 2026-10-14). A readonly field rather than a const so switching it off
        /// leaves no unreachable-code warning behind.</summary>
        public static readonly bool ReactionsEnabled = true;

        /// <summary>The lowest stable per-NPC pitch.</summary>
        public const float PitchMin = 0.92f;

        /// <summary>The highest stable per-NPC pitch.</summary>
        public const float PitchMax = 1.08f;

        /// <summary>The per-clip pitch jitter (±) around the NPC's own pitch.</summary>
        public const float ClipJitter = 0.02f;

        /// <summary>The speech babble's share of the master × SFX volume (positional, so it also fades with distance).</summary>
        public const float SpeechVolume = 0.75f;

        /// <summary>A sleepy NPC (greeted in bed) speaks lower …</summary>
        public const float SleepyPitch = 0.88f;

        /// <summary>… and quieter.</summary>
        public const float SleepyVolume = 0.5f;

        private static readonly string[][] BankClips = BuildBankClips();

        /// <summary>
        /// The speech bank of the NPC with face seed <paramref name="seed"/> (the seed <c>NpcView</c> builds the avatar
        /// with). G.D.S. guardians (<paramref name="isGuardian"/>) speak with the deep robot voice; other androids with a
        /// seeded robot bank (bright/deep/neutral, about a third each); a human with facial hair
        /// (<see cref="NpcLooks.FacialHairFor"/>) male (~80 %) or neutral, never female; every other human female (~45 %),
        /// male (~35 %) or neutral (~20 %). The roll is hashed with its own salt, so it does not follow the seed bits the
        /// hair, the tone or the beard pick.
        /// </summary>
        public static NpcVoiceBank VoiceFor(int seed, bool isRobot, bool isGuardian = false)
        {
            if (isGuardian)
            {
                return NpcVoiceBank.RobotDeep;
            }

            int roll = (int)(Mix(unchecked((uint)seed ^ 0x6A09E667u)) % 100u);
            if (isRobot)
            {
                return roll < 34 ? NpcVoiceBank.RobotBright : roll < 67 ? NpcVoiceBank.RobotDeep : NpcVoiceBank.RobotNeutral;
            }

            if (NpcLooks.FacialHairFor(seed, robot: false) != FacialHair.None)
            {
                return roll < 80 ? NpcVoiceBank.HumanMale : NpcVoiceBank.HumanNeutral;
            }

            return roll < 45 ? NpcVoiceBank.HumanFemale : roll < 80 ? NpcVoiceBank.HumanMale : NpcVoiceBank.HumanNeutral;
        }

        /// <summary>Whether <paramref name="bank"/> is one of the robot voices.</summary>
        public static bool IsRobot(NpcVoiceBank bank) => bank >= NpcVoiceBank.RobotBright;

        /// <summary>The bank's part of the clip name: <c>human_f</c>, <c>robot_m</c>, ….</summary>
        public static string BankKey(NpcVoiceBank bank) => bank switch
        {
            NpcVoiceBank.HumanFemale => "human_f",
            NpcVoiceBank.HumanMale => "human_m",
            NpcVoiceBank.HumanNeutral => "human_n",
            NpcVoiceBank.RobotBright => "robot_f",
            NpcVoiceBank.RobotDeep => "robot_m",
            _ => "robot_n",
        };

        /// <summary>The <see cref="ClipsPerBank"/> speech clip ids of a bank (<c>npc_voice_human_f_1</c> …), shared arrays.</summary>
        public static IReadOnlyList<string> Clips(NpcVoiceBank bank)
        {
            int i = (int)bank;
            return i >= 0 && i < BankClips.Length ? BankClips[i] : BankClips[0];
        }

        /// <summary>How many clips a bubble of <paramref name="textLength"/> characters babbles: one per
        /// <see cref="CharsPerClip"/> started, at least one, at most <see cref="MaxClips"/>.</summary>
        public static int ClipCount(int textLength)
        {
            if (textLength <= 0)
            {
                return 1;
            }

            int count = (textLength + CharsPerClip - 1) / CharsPerClip;
            return count < 1 ? 1 : count > MaxClips ? MaxClips : count;
        }

        /// <summary>
        /// The clip indices (0..<see cref="ClipsPerBank"/>−1) a bubble of <paramref name="textLength"/> characters babbles,
        /// in order: <see cref="ClipCount"/> random clips, never the same one twice in a row — also not the
        /// <paramref name="previous"/> clip the NPC ended its last babble with (−1 = none).
        /// </summary>
        public static int[] BabblePlan(int textLength, Random rng, int previous = -1)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var plan = new int[ClipCount(textLength)];
            int last = previous;
            for (int i = 0; i < plan.Length; i++)
            {
                last = Cutscenes.CutsceneVoice.PickClip(ClipsPerBank, last, (float)rng.NextDouble());
                plan[i] = last;
            }

            return plan;
        }

        /// <summary>
        /// The NPC's own stable pitch: <see cref="PitchMin"/>…<see cref="PitchMax"/> from the face seed (its own salt); a
        /// human's also follows the body size (<paramref name="size"/>, 1 = normal; a bigger body speaks lower, by
        /// 1/√size, clamped to 0.75–1.35). A robot's ignores the size.
        /// </summary>
        public static float Pitch(int seed, bool isRobot, float size)
        {
            float roll = (Mix(unchecked((uint)seed ^ 0xBB67AE85u)) & 0xFFFFu) / 65535f;
            float pitch = PitchMin + (roll * (PitchMax - PitchMin));
            if (isRobot || size <= 0f || float.IsNaN(size))
            {
                return pitch;
            }

            float s = size < 0.5f ? 0.5f : size > 2f ? 2f : size;
            pitch /= (float)Math.Sqrt(s);
            return pitch < 0.75f ? 0.75f : pitch > 1.35f ? 1.35f : pitch;
        }

        /// <summary>One clip's pitch: the NPC's <paramref name="basePitch"/> ±<see cref="ClipJitter"/> for
        /// <paramref name="roll"/> in 0..1, so a babble never sounds looped.</summary>
        public static float ClipPitch(float basePitch, float roll)
        {
            float r = roll < 0f ? 0f : roll > 1f ? 1f : roll;
            return basePitch * (1f - ClipJitter + (2f * ClipJitter * r));
        }

        /// <summary>The clip id of a reaction: <c>npc_human_&lt;kind&gt;</c>, or <see cref="RobotReactionPrefix"/> +
        /// kind for an android.</summary>
        public static string ReactionClip(bool isRobot, NpcReaction kind)
            => (isRobot ? RobotReactionPrefix : HumanReactionPrefix) + kind switch
            {
                NpcReaction.Trade => "trade",
                NpcReaction.Alert => "alert",
                _ => "idle",
            };

        /// <summary>A reaction's share of the master × SFX volume: the idle murmur quiet, the alarm loudest.</summary>
        public static float ReactionVolume(NpcReaction kind) => kind switch
        {
            NpcReaction.Alert => 0.85f,
            NpcReaction.Trade => 0.7f,
            _ => 0.35f,
        };

        /// <summary>
        /// The seconds until an NPC's next idle murmur: 30–60 s, from its face seed and the murmur's number
        /// (<paramref name="count"/>), so neighbours never murmur in step. The voice budget's global gap comes on top.
        /// </summary>
        public static float IdleGapSeconds(int seed, int count)
        {
            float roll = (Mix(unchecked((uint)seed ^ 0x3C6EF372u ^ ((uint)count * 0x9E3779B9u))) & 0xFFFFu) / 65535f;
            return 30f + (roll * 30f);
        }

        private static string[][] BuildBankClips()
        {
            var banks = new string[6][];
            for (int b = 0; b < banks.Length; b++)
            {
                banks[b] = new string[ClipsPerBank];
                for (int i = 0; i < ClipsPerBank; i++)
                {
                    banks[b][i] = SpeechPrefix + BankKey((NpcVoiceBank)b) + "_" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return banks;
        }

        /// <summary>The MurmurHash3 finaliser (as <see cref="NpcLooks"/>): every input bit reaches every output bit.</summary>
        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
