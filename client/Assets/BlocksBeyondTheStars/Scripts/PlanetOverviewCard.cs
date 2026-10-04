// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship scanner's card at the right edge (#2239, #2247). A planet or moon reads "what awaits me down there?": an
    /// overall traffic light, then one row per topic — a white line icon, the topic, a short answer in green
    /// (harmless) / yellow (take care) / red (dangerous), an optional second word — and the resources when the scanner
    /// tier shows them (else a line pointing at the Deep scanner). Any other target (asteroid, station, wreck, life
    /// pod, raider, Guardian machine, anomaly, wormhole) gets its readout in the same card: the sentence, the traits,
    /// the threat and the knowledge it paid. It never takes the mouse or the keys (the pilot keeps flying) and fades by
    /// itself; the Map tab keeps a planet's report beside the body.
    /// <para>All ship readouts live here, never in the hand scanner's HUD panel at the bottom left — that one sat under
    /// VEGA's objective chip in flight and kept showing the last surface scan (#2247).</para>
    /// </summary>
    public sealed class PlanetOverviewCard : MonoBehaviour
    {
        private const float CardWidth = 400f;
        private const float RowHeight = 28f;
        private const float ShowSeconds = 18f;
        private const float NoticeSeconds = 6f;

        private static PlanetOverviewCard _instance;

        private GameBootstrap _game;
        private Canvas _canvas;
        private RectTransform _root;
        private CanvasGroup _group;
        private float _age;
        private float _showFor = ShowSeconds;

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

        /// <summary>A planet's or moon's overview.</summary>
        public static void Show(GameBootstrap game, PlanetScanResult report)
        {
            if (report == null || report.Rows == null || report.Rows.Length == 0)
            {
                return;
            }

            Instance().BuildPlanet(game, report);
        }

        /// <summary>#2247: the readout of a space object the ship scanner read — or the reason it could not.</summary>
        public static void ShowReadout(GameBootstrap game, ScanResult scan)
        {
            if (scan == null || game == null || game.Localizer == null)
            {
                return;
            }

            Instance().BuildReadout(game, scan);
        }

        private static PlanetOverviewCard Instance()
        {
            if (_instance == null)
            {
                var go = new GameObject("PlanetOverviewCard");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<PlanetOverviewCard>();
            }

            return _instance;
        }

        /// <summary>A fresh, empty card root on the (lazily built) canvas.</summary>
        private RectTransform NewCard(GameBootstrap game, float showFor)
        {
            _game = game;
            _age = 0f;
            _showFor = showFor;
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
            return _root;
        }

        private void Reveal()
        {
            _canvas.enabled = true;
            _group.alpha = 1f;
        }

        private static string L(GameBootstrap game, string key, string fallback)
        {
            var loc = game != null ? game.Localizer : null;
            return loc != null && loc.Has(key) ? loc.Get(key) : fallback;
        }

        private void BuildPlanet(GameBootstrap game, PlanetScanResult r)
        {
            var root = NewCard(game, ShowSeconds);
            int rows = r.Rows.Length;
            bool showOres = !r.ResourcesLocked && r.Ores != null;
            int oreLines = showOres ? Mathf.Min(6, Mathf.Max(1, r.Ores.Length)) : 1;
            float height = 96f + rows * RowHeight + 34f + oreLines * 22f + 12f;
            root.sizeDelta = new Vector2(CardWidth, height);
            UiKit.AddPanel(root, 0f, 0f, CardWidth, height, UiKit.Panel);

            string title = string.Format(L(game, "ui.overview.title", "{0} — what awaits you"), r.BodyName);
            UiKit.AddText(root, 16f, 10f, CardWidth - 32f, 30f, title, 21, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            string tier = L(game, "ui.overview.tier_" + Mathf.Clamp(r.Tier, 1, 3), "Ship scanner");
            string type = L(game, "planet." + r.PlanetType + ".name", r.PlanetType);
            UiKit.AddText(root, 16f, 38f, CardWidth - 32f, 22f, type + " · " + tier, 15, UiKit.CyanDim, TextAnchor.UpperLeft);

            var dangerColor = LevelColor(r.Danger);
            UiKit.AddPanel(root, 16f, 62f, 14f, 14f, dangerColor);
            UiKit.AddText(root, 38f, 58f, CardWidth - 54f, 24f, L(game, "ui.overview.danger_" + Mathf.Clamp(r.Danger, (byte)0, (byte)2), "—"), 17, dangerColor, TextAnchor.UpperLeft, FontStyle.Bold);

            float y = 92f;
            foreach (var row in r.Rows)
            {
                var col = LevelColor(row.Level);
                var icon = UiKit.Icon(TopicIcon(row.Topic));
                if (icon != null)
                {
                    UiKit.AddIconSprite(root, 16f, y + 2f, 22f, icon, col);
                }

                UiKit.AddText(root, 46f, y, 120f, RowHeight, L(game, "ui.overview.topic." + row.Topic, row.Topic), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                string value = L(game, row.ValueKey, row.ValueKey);
                if (!string.IsNullOrEmpty(row.Extra))
                {
                    value += " (" + row.Extra + ")";
                }

                if (!string.IsNullOrEmpty(row.DetailKey))
                {
                    value += " · " + L(game, row.DetailKey, string.Empty);
                }

                UiKit.AddText(root, 166f, y, CardWidth - 182f, RowHeight, value, 15, col, TextAnchor.MiddleLeft);
                y += RowHeight;
            }

            y += 6f;
            UiKit.AddText(root, 16f, y, CardWidth - 32f, 24f, L(game, "ui.overview.resources", "Resources"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            if (!showOres)
            {
                UiKit.AddText(root, 16f, y, CardWidth - 32f, 22f, L(game, "ui.overview.resources_locked", "Build the Deep scanner to see the ores."), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
            else if (r.Ores.Length == 0)
            {
                UiKit.AddText(root, 16f, y, CardWidth - 32f, 22f, L(game, "ui.planetscan.no_ores", "No ore veins."), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }
            else
            {
                for (int i = 0; i < r.Ores.Length && i < 6; i++)
                {
                    var ore = r.Ores[i];
                    string name = L(game, "block." + ore.Block + ".name", ore.Block);
                    string abundance = L(game, "ui.planetscan.abundance_" + ore.Abundance, string.Empty);
                    UiKit.AddText(root, 16f, y, CardWidth - 32f, 22f, "• " + name + " — " + abundance, 14,
                        ore.Abundance >= 2 ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 22f;
                }
            }

            Reveal();
        }

        /// <summary>#2247: a space object's readout. A refusal (out of range, recharging, nothing to read) carries no
        /// kind — it shows as a short notice that leaves sooner.</summary>
        private void BuildReadout(GameBootstrap game, ScanResult scan)
        {
            var loc = game.Localizer;
            bool refused = string.IsNullOrEmpty(scan.Kind);
            var root = NewCard(game, refused ? NoticeSeconds : ShowSeconds);
            const float pad = 16f;
            const float textW = CardWidth - 2f * pad;

            string title = refused
                ? loc.Get("ui.scan.title")
                : ScanReadoutText.Title(game.Content, loc, scan);
            string info = refused
                ? (!string.IsNullOrEmpty(scan.InfoKey) ? loc.Get(scan.InfoKey) : scan.Info)
                : ScanReadoutText.Info(game.Content, loc, scan);
            string threat = !string.IsNullOrEmpty(scan.ThreatKey) ? loc.Get(scan.ThreatKey) : string.Empty;

            // The panel goes in first so it draws behind the text.
            var panel = UiKit.AddPanel(root, 0f, 0f, CardWidth, 100f, UiKit.Panel);
            UiKit.AddText(root, pad, 10f, textW, 30f, title, 21, refused ? UiKit.CyanDim : UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            float y = 42f;
            if (!refused)
            {
                UiKit.AddText(root, pad, y, textW, 22f, L(game, "ui.overview.tier_1", "Ship scanner"), 15, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 26f;
            }

            var body = UiKit.AddText(root, pad, y, textW, 24f, info ?? string.Empty, 16, refused ? Yellow : UiKit.TextCol, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            float bodyH = Mathf.Max(22f, body.preferredHeight);
            body.rectTransform.sizeDelta = new Vector2(textW, bodyH);
            y += bodyH + 8f;

            if (!refused && threat.Length > 0 && threat != "—")
            {
                bool hostile = scan.ThreatKey == "ui.scan.threat.hostile";
                UiKit.AddText(root, pad, y, textW, 22f, loc.Get("ui.scan.threat") + ": " + threat, 16, hostile ? Red : UiKit.TextCol, TextAnchor.UpperLeft);
                y += 26f;
            }

            if (!refused)
            {
                bool first = scan.FirstTime && scan.KnowledgeGained > 0;
                string know = first
                    ? $"{loc.Get("ui.scan.first_time")}  +{scan.KnowledgeGained}  ({loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal})"
                    : $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}";
                UiKit.AddText(root, pad, y, textW, 24f, know, 16, first ? Green : UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 28f;
            }

            float height = y + 8f;
            root.sizeDelta = new Vector2(CardWidth, height);
            panel.rectTransform.sizeDelta = new Vector2(CardWidth, height);
            Reveal();
        }

        private void Update()
        {
            if (_canvas == null || !_canvas.enabled)
            {
                return;
            }

            _age += Time.unscaledDeltaTime;
            bool hide = _game == null || !_game.SpaceViewActive || _game.MenuOpen;
            float fade = _age > _showFor ? 1f - (_age - _showFor) / 1.2f : 1f;
            _group.alpha = hide ? 0f : Mathf.Clamp01(fade);
            if (_age > _showFor + 1.2f || (_game != null && !_game.SpaceViewActive && _age > 1f))
            {
                _canvas.enabled = false;
            }
        }
    }
}
