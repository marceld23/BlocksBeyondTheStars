// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

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
        /// <summary>Share (percent) of civilians without facial hair.</summary>
        public const int NonePercent = 40;

        /// <summary>The facial hair of the civilian with face seed <paramref name="seed"/>; androids have none (they get a
        /// speaker grille instead). About 40 % bare, the rest spread evenly over moustache, goatee, beard and full beard.</summary>
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

            int step = (100 - NonePercent) / 4; // 15 each
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
