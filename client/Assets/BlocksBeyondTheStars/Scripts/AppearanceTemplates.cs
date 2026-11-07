// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.HudFrames;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The templates the character editor offers (#3318 HUD frames, #3321 skins): the free ones (<c>unlock: ""</c>)
    /// everywhere, a calendar one only in a world where a calendar door unlocked it (<c>hudframe:&lt;key&gt;</c> /
    /// <c>skin:&lt;key&gt;</c> in <c>NetworkClient.CosmeticUnlockKeys</c>) — the main menu passes no unlock check and so
    /// shows only the free ones. A template is a starting point: putting it on copies it into the player's own look.
    /// </summary>
    public static class AppearanceTemplates
    {
        /// <summary>The HUD frame templates on offer, in the file's order; broken entries are left out.</summary>
        public static List<HudFrameDefinition> OfferedFrames(GameContent content, Func<string, bool> unlocked)
        {
            var list = new List<HudFrameDefinition>();
            if (content == null)
            {
                return list;
            }

            foreach (var frame in content.HudFrames)
            {
                if (Offered(frame.Unlock, CosmeticKeys.HudFramePrefix + frame.Key, unlocked) && HudFrameDesign.TryFromTemplate(frame, out _))
                {
                    list.Add(frame);
                }
            }

            return list;
        }

        /// <summary>The skin templates on offer, in the file's order.</summary>
        public static List<SkinDefinition> OfferedSkins(GameContent content, Func<string, bool> unlocked)
        {
            var list = new List<SkinDefinition>();
            if (content == null)
            {
                return list;
            }

            foreach (var skin in content.Skins)
            {
                if (Offered(skin.Unlock, CosmeticKeys.SkinPrefix + skin.Key, unlocked))
                {
                    list.Add(skin);
                }
            }

            return list;
        }

        private static bool Offered(string unlock, string cosmeticKey, Func<string, bool> unlocked)
            => string.IsNullOrEmpty(unlock) || (unlocked != null && unlocked(cosmeticKey));

        /// <summary>Puts a skin template onto <paramref name="look"/>: each colour it names (an empty entry keeps the
        /// player's own) and the four body paintings; the face stays the player's.</summary>
        public static void ApplySkin(SkinDefinition skin, AvatarOutfit look)
        {
            if (skin == null || look == null)
            {
                return;
            }

            var colors = skin.Colors ?? Array.Empty<string>();
            for (int slot = 0; slot < 4 && slot < colors.Length; slot++)
            {
                if (TryColor(colors[slot], out var color))
                {
                    SetColor(look, slot, color);
                }
            }

            var paint = skin.Paint ?? Array.Empty<string>();
            for (int part = 0; part < BodyPaintKit.PartCount; part++)
            {
                look.SetBodyPaint(part, part < paint.Length ? paint[part] ?? string.Empty : string.Empty);
            }
        }

        /// <summary>Colour slot 0 skin, 1 torso, 2 arms, 3 legs of an outfit.</summary>
        private static void SetColor(AvatarOutfit look, int slot, Color color)
        {
            switch (slot)
            {
                case 0: look.SkinColor = color; break;
                case 1: look.TorsoColor = color; break;
                case 2: look.ArmColor = color; break;
                default: look.LegColor = color; break;
            }
        }

        /// <summary>A <c>#rrggbb</c> colour; false for an empty entry ("keep the player's own") or anything unreadable.</summary>
        public static bool TryColor(string hex, out Color color)
        {
            color = Color.white;
            if (!HudFrameTwinkle.TryParseRgb(hex, out int rgb))
            {
                return false;
            }

            color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
            return true;
        }

        /// <summary>The localized names of <paramref name="items"/>.</summary>
        public static List<string> Names<T>(List<T> items, Func<T, string> nameKey, Func<string, string> localize)
        {
            var names = new List<string>(items.Count);
            foreach (var item in items)
            {
                string key = nameKey(item);
                names.Add(localize != null ? localize(key) : key);
            }

            return names;
        }
    }
}
