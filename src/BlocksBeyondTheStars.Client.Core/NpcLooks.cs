// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Facial hair of a civilian NPC (#2123).</summary>
    public enum FacialHair : byte
    {
        None,
        Moustache,
        Goatee,
        Beard,
        FullBeard,
    }

    /// <summary>
    /// What sits on an avatar's lower face (#2123). <see cref="Breather"/> is the teal breather strip every avatar used to
    /// wear — players and the editor previews keep it; civilian NPCs show their facial hair (or a bare face) instead, and
    /// androids a small speaker grille.
    /// </summary>
    public enum LowerFace : byte
    {
        Breather,
        Bare,
        Moustache,
        Goatee,
        Beard,
        FullBeard,
        Grille,
    }

    /// <summary>
    /// Unity-free look rules for NPC faces (#2123, "can this moustache go away — at least some without a beard, some with a
    /// beard or a moustache"). Deterministic from the NPC's face seed, like the hair (the client derives the seed from the
    /// NPC id and name, or the authored character's fixed face seed), so a person keeps their face every visit. The roll is
    /// hashed with its own salt, so it does not follow the seed bits the hair picks (bald = the low three bits, the tone =
    /// bits 8 and up) — a bald head is as likely to wear a beard as anyone.
    /// </summary>
    public static class NpcLooks
    {
        /// <summary>Share (percent) of civilians without facial hair. 40 after #2123 ("at least some without a beard");
        /// 65 since #2433 (Justus: "not ALL of them — only some!") — a beard is now the exception, about one face in three,
        /// and a child who counts a moustache as a beard still sees a mostly clean-shaven crowd.</summary>
        public const int NonePercent = 65;

        /// <summary>The facial hair of the civilian with face seed <paramref name="seed"/>; androids have none (they get a
        /// speaker grille instead). About 65 % bare, the rest spread evenly over moustache, goatee, beard and full beard.</summary>
        public static FacialHair FacialHairFor(int seed, bool robot)
        {
            if (robot)
            {
                return FacialHair.None;
            }

            int roll = (int)(Mix(unchecked((uint)seed ^ 0x2F6B3A51u)) % 100u);
            if (roll < NonePercent)
            {
                return FacialHair.None;
            }

            int step = (100 - NonePercent) / 4; // about 9 each (the remainder of the integer division stays a full beard)
            roll -= NonePercent;
            return roll < step ? FacialHair.Moustache
                : roll < 2 * step ? FacialHair.Goatee
                : roll < 3 * step ? FacialHair.Beard
                : FacialHair.FullBeard;
        }

        /// <summary>The lower face the NPC avatar is built with: an android's speaker grille, else its facial hair (a bare
        /// face for none). Never the players' breather strip.</summary>
        public static LowerFace LowerFaceFor(int seed, bool robot)
        {
            if (robot)
            {
                return LowerFace.Grille;
            }

            return FacialHairFor(seed, robot: false) switch
            {
                FacialHair.Moustache => LowerFace.Moustache,
                FacialHair.Goatee => LowerFace.Goatee,
                FacialHair.Beard => LowerFace.Beard,
                FacialHair.FullBeard => LowerFace.FullBeard,
                _ => LowerFace.Bare,
            };
        }

        /// <summary>#3094: how many hair tones a person can have (<see cref="HairTone"/>).</summary>
        public const int HairToneCount = 7;

        // Civilian hair tones (linear 0..1, the client converts them like every avatar colour) — muted human colours, no
        // reds: red accents belong to the Guardians. The villagers' palette since #711; the robbers share it since #3094.
        private static readonly float[] HairTones =
        {
            0.10f, 0.09f, 0.08f, // black
            0.28f, 0.19f, 0.12f, // dark brown
            0.45f, 0.32f, 0.18f, // chestnut
            0.62f, 0.48f, 0.25f, // dark blond
            0.75f, 0.65f, 0.40f, // blond
            0.55f, 0.55f, 0.55f, // grey
            0.35f, 0.20f, 0.12f, // auburn
        };

        /// <summary>
        /// #3094: the hair of the person with face seed <paramref name="seed"/> — an index into <see cref="HairTone"/>, or −1
        /// for a bald head (about one in eight: the seed's low three bits are 0) and for every android. The villagers' rule
        /// since #711 (it lived in the client's NpcView), moved here so the robbers grow the very same hair: the tone comes
        /// from the seed's bits 8 and up, independent of the baldness bits and of the facial-hair roll.
        /// </summary>
        public static int HairToneFor(int seed, bool robot)
        {
            if (robot || (seed & 0x7) == 0)
            {
                return -1;
            }

            return (int)((uint)(seed >> 8) % (uint)HairToneCount);
        }

        /// <summary>#3094: hair tone <paramref name="index"/> (0 ≤ index &lt; <see cref="HairToneCount"/>) as a linear colour.</summary>
        public static (float R, float G, float B) HairTone(int index)
        {
            int i = Math.Max(0, Math.Min(HairToneCount - 1, index)) * 3;
            return (HairTones[i], HairTones[i + 1], HairTones[i + 2]);
        }

        /// <summary>Order-dependent string hash that is stable across sessions and machines — .NET string hash codes are
        /// randomised per process, which would re-roll every face at each login. (NpcView's hash, moved here by #3094.)</summary>
        public static int StableHash(string? s)
        {
            int h = 17;
            foreach (char c in s ?? string.Empty)
            {
                h = unchecked((h * 31) + c);
            }

            return h;
        }

        /// <summary>The face seed of a person from a numeric id and a name — what a villager without an authored face seed
        /// gets (id · 486187739 XOR the name's <see cref="StableHash"/>, #711), and (#3094) a robber from its id's hash.</summary>
        public static int FaceSeed(int id, string? name) => unchecked((id * 486187739) ^ StableHash(name));

        /// <summary>#3094: the stable non-negative hash of a planet enemy's string id — the one the client has always seeded a
        /// machine's or a robber's size and voice from.</summary>
        public static int EntityIdHash(string? id)
        {
            int h = 0;
            foreach (char c in id ?? string.Empty)
            {
                h = unchecked((h * 31) + c);
            }

            return h & 0x7fffffff;
        }

        /// <summary>#3094: a robber's face seed from its entity id and name — its hair (<see cref="HairToneFor"/>) and its face
        /// jitter come from it exactly like a villager's. Robbers never wear a beard: the mask covers the lower face anyway.</summary>
        public static int RobberFaceSeed(string? id, string? name) => FaceSeed(EntityIdHash(id), name);

        /// <summary>The MurmurHash3 finaliser: every input bit reaches every output bit.</summary>
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
