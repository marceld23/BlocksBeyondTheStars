// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The time-of-day panel of <see cref="HudUi"/> (the day / night clock, temperature, weather and gravity) and the
    /// optional playtime readout under it.
    /// </summary>
    public sealed partial class HudUi
    {
        private TMP_Text _todText;
        private GameObject _playtimePanel; // optional session/total playtime readout (top-right, under the clock)
        private TMP_Text _playtimeText;
        private RectTransform _todMarker;

        private int _todKey = int.MinValue; // #1554 change keys of the formatted read-outs
        private string _todWeather, _todPrecip, _todFamily;
        private bool _todSpaceSky;
        private object _todLoc, _playtimeLoc;
        private long _playtimeShown = -1;

        private void BuildTimeOfDay(Transform root)
        {
            // Time of day + temperature.
            var tod = Panel(root, W - 210f, 140, 200, 56);
            _todText = UiText.Add(tod.transform, 10, 5, 184, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(tod.transform, 10, 32, 150, 12, UiKit.SolidSprite, new Color(0.05f, 0.08f, 0.16f));
            UiKit.AddImage(tod.transform, 10 + 150 * 0.25f, 32, 150 * 0.5f, 12, UiKit.SolidSprite, new Color(0.30f, 0.55f, 0.85f, 0.85f));
            _todMarker = UiKit.AddImage(tod.transform, 10, 30, 2, 16, UiKit.SolidSprite, UiKit.Cyan).rectTransform;

            // Optional playtime readout, tucked just under the clock (top-right). Hidden unless the comfort
            // setting is on; refreshed each frame in RefreshPlaytime.
            _playtimePanel = Panel(root, W - 210f, 200, 200, 40).gameObject;
            _playtimeText = UiText.Add(_playtimePanel.transform, 10, 4, 184, 30, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _playtimePanel.SetActive(false);
        }

        private void RefreshTimeOfDay(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var env = Game.Environment;
            // Day/night clock and gravity are planet-surface readings — meaningless in space, so the panel
            // stays hidden while piloting (the cabin is climate-controlled anyway). On an EVA the suit's
            // hull-temperature reading IS meaningful (#668: sun-dependent vacuum value from the server), so
            // show a temperature-only readout there instead of dropping the panel.
            if (env == null || Game.SpaceViewActive)
            {
                _todText.transform.parent.gameObject.SetActive(false);
                return;
            }

            if (Game.OnFootInSpace)
            {
                _todText.transform.parent.gameObject.SetActive(true);
                _todMarker.gameObject.SetActive(false);
                _todText.text = ColoredTemp(env.Temperature);
                return;
            }

            _todText.transform.parent.gameObject.SetActive(true);
            _todMarker.gameObject.SetActive(true);
            float t = Game.LocalTimeOfDay; // the player's local time (longitude-shifted), already 0..1
            bool day = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) > 0f;
            float nextEdge = day ? 0.75f : (t < 0.25f ? 0.25f : 1.25f);
            float frac = nextEdge - t; if (frac < 0f) frac += 1f;
            float secs = frac * Mathf.Max(1f, env.DayLengthSeconds);
            int mm = Mathf.FloorToInt(secs / 60f), ss = Mathf.FloorToInt(secs % 60f);
            // #1554: the readout only changes once a second (or with the weather / temperature / gravity), so
            // skip the five-part interpolation on the other nine refreshes.
            int todKey = (day ? 1 : 0) ^ (mm << 1) ^ (ss << 12) ^ (Mathf.RoundToInt(env.Temperature) << 18)
                         ^ (Mathf.RoundToInt(env.GravityFactor * 10f) << 26) ^ (Game.OnFootInSpace ? 1 << 30 : 0);
            if (todKey == _todKey && ReferenceEquals(env.Weather, _todWeather) && ReferenceEquals(env.Precipitation, _todPrecip)
                && ReferenceEquals(env.WeatherFamily, _todFamily) && env.SpaceSky == _todSpaceSky
                && ReferenceEquals(loc, _todLoc))
            {
                _todMarker.anchoredPosition = new Vector2(10 + 150 * t, _todMarker.anchoredPosition.y);
                return;
            }

            _todKey = todKey;
            _todWeather = env.Weather;
            _todPrecip = env.Precipitation;
            _todFamily = env.WeatherFamily;
            _todSpaceSky = env.SpaceSky;
            _todLoc = loc;
            string tempStr = env.Temperature <= -900f ? "—" : ColoredTemp(env.Temperature);
            // Show this world's gravity (e.g. "0.6 g") only when it notably differs from Earth-like, so normal
            // worlds stay uncluttered. Hidden in space (on-foot zero-g) where gravity doesn't apply.
            string gravStr = env.GravityFactor > 0.01f && Mathf.Abs(env.GravityFactor - 1f) > 0.05f && !Game.OnFootInSpace
                ? "  " + string.Format(loc.Get("ui.hud.gravity"), env.GravityFactor.ToString("0.0"))
                : string.Empty;
            _todText.text = $"{(day ? loc.Get("ui.hud.day") : loc.Get("ui.hud.night")).ToUpperInvariant()}  {mm}:{ss:00}  {tempStr}{WeatherChip(loc, env)}{gravStr}";
            _todMarker.anchoredPosition = new Vector2(10 + 150 * t, _todMarker.anchoredPosition.y);
        }

        /// <summary>
        /// The weather chip (#900): a small icon + the localized state name next to the temperature, shown
        /// only when there is actually weather to name — a clear sky says nothing, so a calm world stays as
        /// uncluttered as it was. Before this the game never named its own weather anywhere.
        /// </summary>
        private string WeatherChip(BlocksBeyondTheStars.Shared.Localization.Localizer loc,
            BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env)
        {
            string state = env.Weather;
            if (string.IsNullOrEmpty(state) || state == "clear" || env.SpaceSky)
            {
                return string.Empty;
            }

            // #2174: the shared weather look — the maps show the same glyphs and warning colours.
            string icon = WeatherLook.Glyph(state);
            string colour = WeatherLook.FamilyColorHex(env.WeatherFamily);

            string label = $"{icon} {loc.Get("weather." + state)}";
            return "  " + (colour is null ? label : $"<color={colour}>{label}</color>");
        }

        /// <summary>Temperature readout tinted by comfort (#666): icy blue below the band, hot orange above
        /// it, plain inside — matching where the suit actually starts spending energy (−5…40 °C ±5 grace).</summary>
        private static string ColoredTemp(float tempC)
        {
            string s = $"{Mathf.RoundToInt(tempC)}°C";
            if (tempC < -10f) { return $"<color=#6fd8ff>{s}</color>"; }
            if (tempC > 45f) { return $"<color=#ff7439>{s}</color>"; }
            return s;
        }

        /// <summary>Optional comfort readout: the current session's real-world playtime, plus this world's
        /// saved total. Hidden entirely unless the player enabled it in settings. Counts real wall-clock so it
        /// keeps ticking while paused in menus.</summary>
        private void RefreshPlaytime(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            if (_playtimePanel == null)
            {
                return;
            }

            bool show = Settings != null && Settings.ShowSessionTime;
            if (_playtimePanel.activeSelf != show)
            {
                _playtimePanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            long session = (long)Game.SessionSeconds;
            if (session == _playtimeShown && ReferenceEquals(loc, _playtimeLoc))
            {
                return; // #1554: changes once a second, refreshed ten times a second
            }

            _playtimeShown = session;
            _playtimeLoc = loc;
            _playtimeText.text = $"{loc.Get("ui.hud.playtime")}  {FormatDuration(session)}"
                                 + $"  ·  {loc.Get("ui.hud.playtime_total")}  {FormatDuration(Game.TotalPlaytimeSeconds)}";
        }

        /// <summary>Formats a span of seconds as <c>H:MM:SS</c> (or <c>M:SS</c> under an hour) for the HUD.</summary>
        private static string FormatDuration(long totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            long h = totalSeconds / 3600;
            long m = (totalSeconds % 3600) / 60;
            long s = totalSeconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
    }
}
