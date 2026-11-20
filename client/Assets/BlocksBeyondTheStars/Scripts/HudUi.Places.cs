// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: the place banner — the HUD greets the player with a place's name when they walk in ("VILLAGE ·
    /// Mira Mill · Market, Workshop, Tavern"), in the place's own colour, and again when a market day opens around them. It
    /// uses the celebration slot (HudUi.Toasts.cs) after everything that celebrates something the player did, with a short
    /// hold and a soft chime: an arrival is an aside, not an award.
    /// </summary>
    public sealed partial class HudUi
    {
        /// <summary>Seconds the place banner holds (shorter than an achievement: two words to read).</summary>
        private const float PlaceHold = 2.6f;

        /// <summary>The most trades the banner's third line names.</summary>
        private const int PlaceBannerTrades = 4;

        /// <summary>The banner colour of a place without one of its own (older worlds, stations): warm paper.</summary>
        private static readonly Color PlacePaper = new Color(0.96f, 0.9f, 0.74f);

        /// <summary>A place's colour as a readable banner colour: its own hue, lifted so it never reads dark on the panel.</summary>
        internal static Color PlaceBannerColor(int rgb)
        {
            if (rgb <= 0)
            {
                return PlacePaper;
            }

            var c = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Min(s, 0.6f), Mathf.Max(v, 0.92f));
        }

        /// <summary>The banner's head line: what is going on there, else what kind of place it is.</summary>
        private string PlaceHead(NetPlace place)
        {
            var loc = Game.Localizer;
            if (place.State == PlaceStates.Market)
            {
                return loc.Get("ui.place.market_day");
            }

            if (place.State == PlaceStates.Festival)
            {
                return loc.Get("ui.place.gathering");
            }

            if (place.Kind == PlaceKinds.Station)
            {
                return PlacePurposeHead(place);
            }

            string key = place.Tier == "metropolis" ? "ui.tier_short.metropolis" : "ui.tier." + place.Tier;
            string text = loc.Get(key);
            return string.IsNullOrEmpty(text) || text == key ? loc.Get("ui.map.settlement") : text;
        }

        /// <summary>A station's head line: its purpose ("Trade station") when it has one, else plainly a station.</summary>
        private string PlacePurposeHead(NetPlace place)
        {
            var loc = Game.Localizer;
            string key = "ui.place.station." + place.Purpose;
            string text = string.IsNullOrEmpty(place.Purpose) ? string.Empty : loc.Get(key);
            return string.IsNullOrEmpty(text) || text == key ? loc.Get("ui.place.station") : text;
        }

        /// <summary>The banner's third line: what the place offers, by its building functions ("Market · Workshop ·
        /// Tavern"); empty when it offers nothing that has a name.</summary>
        private string PlaceOffers(NetPlace place)
        {
            if (place.Trades == null || place.Trades.Length == 0)
            {
                return string.Empty;
            }

            var loc = Game.Localizer;
            var names = new List<string>(PlaceBannerTrades);
            foreach (string trade in place.Trades)
            {
                string key = "ui.role_short." + trade;
                string name = loc.Get(key);
                if (!string.IsNullOrEmpty(name) && name != key && !names.Contains(name))
                {
                    names.Add(name);
                    if (names.Count == PlaceBannerTrades)
                    {
                        break;
                    }
                }
            }

            return string.Join(" · ", names);
        }

        /// <summary>The icon of a place: a station's purpose emblem, the sign of a settlement's first trade that has one,
        /// else the home mark. <paramref name="picture"/> says whether it is a painted picture (shown as it is) or a
        /// plain mark (tinted with the place's colour).</summary>
        private static Sprite PlaceIcon(NetPlace place, out bool picture)
        {
            picture = true;
            var emblem = place.Kind == PlaceKinds.Station && !string.IsNullOrEmpty(place.Purpose)
                ? UiKit.Icon("place_station_" + place.Purpose)
                : null;
            if (emblem != null)
            {
                return emblem;
            }

            if (place.Trades != null)
            {
                foreach (string trade in place.Trades)
                {
                    var sign = UiKit.Icon("place_sign_" + trade);
                    if (sign != null)
                    {
                        return sign;
                    }
                }
            }

            picture = false;
            return UiKit.Icon("map_mark_home");
        }

        /// <summary>Fills and shows the place banner for one place and plays the arrival chime.</summary>
        private void StartPlaceToast(NetPlace place)
        {
            EnsureResearchToast();
            var color = PlaceBannerColor(place.Color);
            _researchHead.text = PlaceHead(place).ToUpperInvariant();
            _researchName.text = place.Name;
            _researchHead.color = new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f);
            _researchName.color = color;
            SetToastDetail(PlaceOffers(place), _researchHead.color);
            var icon = PlaceIcon(place, out bool picture);
            _researchIcon.enabled = icon != null;
            if (icon != null)
            {
                _researchIcon.texture = icon.texture;
                _researchIcon.uvRect = new Rect(0, 0, 1, 1);
                _researchIcon.color = picture ? Color.white : color;
            }

            _toastHoldNow = PlaceHold;
            _toastGlow = color;
            _researchGo.SetActive(true);
            _researchAge = 0f;
            _researchFade.alpha = UiKit.ReducedMotion ? 0f : 1f;
            _researchRect.localScale = UiKit.ReducedMotion ? Vector3.one : Vector3.one * 0.75f;
            var audio = ClientAudio.Instance;
            if (audio != null)
            {
                bool market = place.State == PlaceStates.Market && audio.Has("place_market_bell");
                audio.Cue(market ? "place_market_bell" : audio.Has("place_arrive") ? "place_arrive" : "ui_toast", 0.6f);
            }
        }
    }
}
