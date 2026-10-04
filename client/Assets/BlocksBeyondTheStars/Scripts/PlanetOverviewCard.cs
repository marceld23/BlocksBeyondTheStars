// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet overview card (#2239) — "what awaits me down there?". Opens at the right edge when the ship scanner
    /// reads a planet in flight: an overall traffic light, then one row per topic — a white line icon, the topic, a
    /// short answer in green (harmless) / yellow (take care) / red (dangerous), an optional second word — and the
    /// resources when the scanner tier shows them (else a line pointing at the Deep scanner). It never takes the mouse
    /// or the keys (the pilot keeps flying) and fades by itself; the Map tab keeps the same report beside the body.
    /// </summary>
    public sealed class PlanetOverviewCard : MonoBehaviour
    {
        private const float CardWidth = 400f;
        private const float RowHeight = 28f;
        private const float ShowSeconds = 18f;

        private static PlanetOverviewCard _instance;

        private GameBootstrap _game;
        private Canvas _canvas;
        private RectTransform _root;
        private CanvasGroup _group;
        private float _age;

        public static readonly Color Green = new Color(0.5f, 1f, 0.6f);
        public static readonly Color Yellow = new Color(1f, 0.85f, 0.35f);
        public static readonly Color Red = new Color(1f, 0.45f, 0.4f);

        /// <summary>The colour of an overview level (0 green, 1 yellow, 2 red, 3 neutral).</summary>
        public static Color LevelColor(byte level) => level switch
        {
            0 => Green,
            1 => Yellow,
            2 => Red,
            _ => UiKit.TextCol,
        };

        /// <summary>The white line icon of a topic (missing icons simply leave the slot empty).</summary>
        public static string TopicIcon(string topic) => topic switch
        {
            "air" => "vital_oxygen",
            "temperature" => "vital_exposure",
            "water" => "map_mark_water",
            "structures" => "map_settlement",
            "frontier" => "map_mark_star",
            _ => "ov_" + topic,
        };

        public static void Show(GameBootstrap game, PlanetScanResult report)
        {
            if (report == null || report.Rows == null || report.Rows.Length == 0)
            {
                return;
            }

            if (_instance == null)
            {
                var go = new GameObject("PlanetOverviewCard");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PlanetOverviewCard>();
            }

            _instance.Build(game, report);
        }

        private void Build(GameBootstrap game, PlanetScanResult r)
        {
            _game = game;
            _age = 0f;
            if (_canvas == null)
            {
                _canvas = UiKit.CreateCanvas("Planet Overview", UiKit.HudRefW, UiKit.HudRefH, userScalable: true);
                _canvas.sortingOrder = 13; // just above the flight HUD, below menus
                _canvas.transform.SetParent(transform, false);
                _group = _canvas.gameObject.AddComponent<CanvasGroup>();
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }

            if (_root != null)
            {
                Destroy(_root.gameObject);
            }

            var rootGo = new GameObject("Card", typeof(RectTransform));
            rootGo.transform.SetParent(_canvas.transform, false);
            _root = rootGo.GetComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-24f, -150f);

            var loc = game != null ? game.Localizer : null;
            string L(string key, string fallback) => loc != null && loc.Has(key) ? loc.Get(key) : fallback;

            int rows = r.Rows.Length;
            bool showOres = !r.ResourcesLocked && r.Ores != null;
            int oreLines = showOres ? Mathf.Min(6, Mathf.Max(1, r.Ores.Length)) : 1;
            float height = 96f + rows * RowHeight + 34f + oreLines * 22f + 12f;
            _root.sizeDelta = new Vector2(CardWidth, height);
            UiKit.AddPanel(_root, 0f, 0f, CardWidth, height, UiKit.Panel);

            string title = string.Format(L("ui.overview.title", "{0} — what awaits you"), r.BodyName);
            UiKit.AddText(_root, 16f, 10f, CardWidth - 32f, 30f, title, 21, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            string tier = L("ui.overview.tier_" + Mathf.Clamp(r.Tier, 1, 3), "Ship scanner");
            string type = L("planet." + r.PlanetType + ".name", r.PlanetType);
            UiKit.AddText(_root, 16f, 38f, CardWidth - 32f, 22f, type + " · " + tier, 15, UiKit.CyanDim, TextAnchor.UpperLeft);

            var dangerColor = LevelColor(r.Danger);
            UiKit.AddPanel(_root, 16f, 62f, 14f, 14f, dangerColor);
            UiKit.AddText(_root, 38f, 58f, CardWidth - 54f, 24f, L("ui.overview.danger_" + Mathf.Clamp(r.Danger, (byte)0, (byte)2), "—"), 17, dangerColor, TextAnchor.UpperLeft, FontStyle.Bold);

            float y = 92f;
            foreach (var row in r.Rows)
            {
                var col = LevelColor(row.Level);
                var icon = UiKit.Icon(TopicIcon(row.Topic));
                if (icon != null)
                {
                    UiKit.AddIconSprite(_root, 16f, y + 2f, 22f, icon, col);
                }

                UiKit.AddText(_root, 46f, y, 120f, RowHeight, L("ui.overview.topic." + row.Topic, row.Topic), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                string value = L(row.ValueKey, row.ValueKey);
                if (!string.IsNullOrEmpty(row.Extra))
                {
                    value += " (" + row.Extra + ")";
                }

                if (!string.IsNullOrEmpty(row.DetailKey))
                {
                    value += " · " + L(row.DetailKey, string.Empty);
                }

                UiKit.AddText(_root, 166f, y, CardWidth - 182f, RowHeight, value, 15, col, TextAnchor.MiddleLeft);
                y += RowHeight;
            }

            y += 6f;
            UiKit.AddText(_root, 16f, y, CardWidth - 32f, 24f, L("ui.overview.resources", "Resources"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            if (!showOres)
            {
                UiKit.AddText(_root, 16f, y, CardWidth - 32f, 22f, L("ui.overview.resources_locked", "Build the Deep scanner to see the ores."), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
            else if (r.Ores.Length == 0)
            {
                UiKit.AddText(_root, 16f, y, CardWidth - 32f, 22f, L("ui.planetscan.no_ores", "No ore veins."), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
            else
            {
                for (int i = 0; i < r.Ores.Length && i < 6; i++)
                {
                    var ore = r.Ores[i];
                    string name = L("block." + ore.Block + ".name", ore.Block);
                    string abundance = L("ui.planetscan.abundance_" + ore.Abundance, string.Empty);
                    UiKit.AddText(_root, 16f, y, CardWidth - 32f, 22f, "• " + name + " — " + abundance, 14,
                        ore.Abundance >= 2 ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 22f;
                }
            }

            _canvas.enabled = true;
            _group.alpha = 1f;
        }

        private void Update()
        {
            if (_canvas == null || !_canvas.enabled)
            {
                return;
            }

            _age += Time.unscaledDeltaTime;
            bool hide = _game == null || !_game.SpaceViewActive || _game.MenuOpen;
            float fade = _age > ShowSeconds ? 1f - (_age - ShowSeconds) / 1.2f : 1f;
            _group.alpha = hide ? 0f : Mathf.Clamp01(fade);
            if (_age > ShowSeconds + 1.2f || (_game != null && !_game.SpaceViewActive && _age > 1f))
            {
                _canvas.enabled = false;
            }
        }
    }
}
