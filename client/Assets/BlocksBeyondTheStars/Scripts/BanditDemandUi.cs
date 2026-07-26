// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The bandit hold-up panel: a robber (on foot or as a ship) demands part of the inventory and the
    /// player chooses "hand it over" or "refuse" against a live countdown (the server enforces the
    /// deadline — silence counts as a refusal). Modeled on the dock-request modal, plus the maintenance
    /// banner's anti-reflex-click gate so a child can't dismiss a robbery by accident. On foot it takes
    /// the cursor (modal); during space flight it stays keyboard-driven ([1]/[2]) so the ship remains
    /// flyable while the ultimatum runs.
    /// </summary>
    public sealed class BanditDemandUi : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float ButtonUnlockDelay = 1.5f; // seconds before the buttons accept a click

        private Canvas _canvas;
        private GameObject _panel;
        private Text _title, _line, _items, _countdown, _hint;
        private Button _comply, _refuse;
        private Text _complyLabel, _refuseLabel;

        private BanditDemand _demand;      // the live hold-up (null = none)
        private float _deadline;           // Time.unscaledTime the server treats silence as refusal
        private float _unlockAt;           // Time.unscaledTime the buttons arm
        private bool _subscribed;
        private bool _answered;            // sent our answer — keep showing the countdown until the result lands

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!_subscribed && Game.Network != null)
            {
                Game.Network.BanditDemandReceived += OnDemand;
                Game.Network.BanditResultReceived += OnResult;
                _subscribed = true;
            }

            if (_demand is null || Game.Localizer is null)
            {
                return;
            }

            EnsureBuilt();
            Refresh();

            // Keyboard answers work everywhere; in space flight they are the ONLY input (no cursor there).
            if (!_answered && Time.unscaledTime >= _unlockAt)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                {
                    Answer(true);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                {
                    Answer(false);
                }
            }
        }

        private void LateUpdate()
        {
            // Cursor/modal arbitration (dock-request pattern): modal only on foot — in space the pilot
            // keeps flying while deciding, and SpaceView owns the input scheme.
            Game?.SetMenuOwner(this, _demand is not null && !Game.SpaceViewActive);
        }

        private void OnDemand(BanditDemand m)
        {
            _demand = m;
            _answered = false;
            _deadline = Time.unscaledTime + Mathf.Max(1, m.SecondsRemaining);
            _unlockAt = Time.unscaledTime + ButtonUnlockDelay;
            ClientAudio.Instance?.Cue("enemy_growl", 0.8f); // an ominous sting announces the hold-up
        }

        private void OnResult(BanditEncounterResult m)
        {
            if (_demand is null || m.DemandId != _demand.DemandId)
            {
                return;
            }

            string key = m.Outcome switch
            {
                "paid" => "ui.bandit.paid",
                "refused" => "ui.bandit.refused",
                "expired" => "ui.bandit.expired",
                _ => "ui.bandit.fled",
            };
            Game.ShowMessage(L(key));
            _demand = null;
            _panel?.SetActive(false);
        }

        private void Answer(bool comply)
        {
            if (_demand is null || _answered)
            {
                return;
            }

            _answered = true;
            Game.Network?.SendBanditResponse(_demand.DemandId, comply);
        }

        private void Refresh()
        {
            _panel.SetActive(true);
            _title.text = string.IsNullOrEmpty(_demand.BanditName)
                ? L("ui.bandit.title")
                : $"{L("ui.bandit.title")} — {_demand.BanditName}";

            // The demand line: an LLM-authored line wins; otherwise the localized static key.
            _line.text = "“" + (!string.IsNullOrEmpty(_demand.Text) ? _demand.Text : L(_demand.LineKey)) + "”";

            var sb = new System.Text.StringBuilder();
            foreach (var it in _demand.Demanded)
            {
                if (sb.Length > 0)
                {
                    sb.Append("   ");
                }

                sb.Append(it.Count).Append("× ").Append(L($"item.{it.Item}.name"));
            }

            _items.text = sb.ToString();

            int remaining = Mathf.Max(0, Mathf.CeilToInt(_deadline - Time.unscaledTime));
            _countdown.text = L("ui.bandit.countdown").Replace("{0}", remaining.ToString());
            _countdown.color = remaining <= 8 ? new Color(1f, 0.35f, 0.3f) : UiKit.TextCol;

            bool armed = !_answered && Time.unscaledTime >= _unlockAt;
            _comply.interactable = armed;
            _refuse.interactable = armed;
            _complyLabel.text = "[1] " + L("ui.bandit.comply");
            _refuseLabel.text = "[2] " + L("ui.bandit.refuse");
            _hint.text = _answered ? L("ui.bandit.waiting") : L("ui.bandit.hint");
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("BanditDemandUi");
            _canvas.sortingOrder = 24; // above the dock/trade panels
            var root = _canvas.transform;

            _panel = new GameObject("BanditPanel", typeof(RectTransform)).gameObject;
            _panel.transform.SetParent(root, false);
            var rt = _panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520f, 240f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            var img = _panel.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;

            var t = _panel.transform;
            _title = UiKit.AddText(t, 20f, 12f, 480f, 26f, string.Empty, 22, new Color(1f, 0.55f, 0.3f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _line = UiKit.AddText(t, 20f, 44f, 480f, 44f, string.Empty, 18, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Italic);
            _items = UiKit.AddText(t, 20f, 96f, 480f, 26f, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _countdown = UiKit.AddText(t, 20f, 126f, 480f, 24f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            _comply = UiKit.AddButton(t, 20f, 160f, 230f, 40f, string.Empty, () => Answer(true));
            _complyLabel = _comply.GetComponentInChildren<Text>();
            _refuse = UiKit.AddButton(t, 270f, 160f, 230f, 40f, string.Empty, () => Answer(false));
            _refuseLabel = _refuse.GetComponentInChildren<Text>();

            _hint = UiKit.AddText(t, 20f, 206f, 480f, 22f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleCenter);

            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.BanditDemandReceived -= OnDemand;
                Game.Network.BanditResultReceived -= OnResult;
            }
        }
    }
}
