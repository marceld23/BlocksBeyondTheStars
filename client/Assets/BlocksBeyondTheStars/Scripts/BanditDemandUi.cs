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
        private string _laidOutLine;       // the demand line the current row layout was measured for

        // Base row layout (1920×1080 reference units). The speech line row grows with its measured
        // wrapped height; every row below it and the panel shift down by the same delta.
        private const float PanelW = 520f, PanelBaseH = 240f, RowW = 480f, LineBaseH = 44f;
        private const float ItemsY = 96f, CountdownY = 126f, ButtonsY = 158f, ButtonH = 44f, HintY = 208f;

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
            // keeps flying while deciding, and SpaceView owns the input scheme. #3662: except on a tablet or a pad — there the keys
            // 1 / 2 do not exist and the touch layer's full-screen look pad (order 100) lay over these buttons (24), so the
            // ultimatum ran out by itself. As a menu owner the touch layer steps aside for the two taps.
            if (Game == null)
            {
                return;
            }

            // A pad likewise: in flight the 1 / 2 keys were its only answer, so a pad pilot could not answer at all — as a
            // menu owner the cursor (pad focus) reaches the two buttons, and the ship holds while the pilot decides.
            bool modal = !Game.SpaceViewActive || InputMap.ActiveDevice == InputDeviceKind.Touch
                || InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            Game.SetMenuOwner(this, _demand is not null && modal);
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
                "destroyed" => "ui.bandit.destroyed", // #2357: the raider's ship broke apart (it explodes, by decision)
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
            if (_line.text != _laidOutLine)
            {
                ReflowForLine();
            }

            var sb = new System.Text.StringBuilder();
            foreach (var it in _demand.Demanded)
            {
                if (sb.Length > 0)
                {
                    sb.Append("   ");
                }

                sb.Append(it.Count).Append("× ").Append(L($"item.{it.Item}.name"));
            }

            SetFitted(_items, sb.ToString(), 20);

            int remaining = Mathf.Max(0, Mathf.CeilToInt(_deadline - Time.unscaledTime));
            SetFitted(_countdown, L("ui.bandit.countdown").Replace("{0}", remaining.ToString()), 18);
            _countdown.color = remaining <= 8 ? new Color(1f, 0.35f, 0.3f) : UiKit.TextCol;

            bool armed = !_answered && Time.unscaledTime >= _unlockAt;
            _comply.interactable = armed;
            _refuse.interactable = armed;
            _complyLabel.text = NpcDialogUi.KeyPrefix(1) + L("ui.bandit.comply"); // the number keys only while a keyboard is in hand
            _refuseLabel.text = NpcDialogUi.KeyPrefix(2) + L("ui.bandit.refuse");
            SetFitted(_hint, _answered ? L("ui.bandit.waiting") : L("ui.bandit.hint"), 14);
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        /// <summary>Sets a one-line row and keeps it inside the panel: the German countdown ("Antworte innerhalb von 30 s —
        /// Schweigen gilt als Ablehnung.") and a long list of demanded goods ran past the 480-wide row. Measured only when
        /// the text changes (the countdown: once a second).</summary>
        private static void SetFitted(Text row, string text, int maxSize)
        {
            if (row.text == text)
            {
                return;
            }

            row.text = text;
            UiKit.FitLabelSingleLine(row, RowW, 12, maxSize);
        }

        /// <summary>
        /// Sizes the speech-line row to its wrapped height and shifts every row below it (and the panel
        /// bottom) by the same amount, so long hail lines — the localized ones as much as LLM-authored
        /// ones — stay inside the panel instead of spilling past its edges. Measured with an explicit
        /// scaleFactor of 1 so the height comes back in the same reference units the rows are placed in
        /// (the UiWhatsNew pattern).
        /// </summary>
        private void ReflowForLine()
        {
            float lineH = Mathf.Max(LineBaseH, UiKit.MeasureHeight(_line.text, RowW, 18, FontStyle.Italic, richText: false) + 4f);
            float delta = lineH - LineBaseH;

            _line.rectTransform.sizeDelta = new Vector2(RowW, lineH);
            _items.rectTransform.anchoredPosition = new Vector2(20f, -(ItemsY + delta));
            _countdown.rectTransform.anchoredPosition = new Vector2(20f, -(CountdownY + delta));
            ((RectTransform)_comply.transform).anchoredPosition = new Vector2(20f, -(ButtonsY + delta));
            ((RectTransform)_refuse.transform).anchoredPosition = new Vector2(270f, -(ButtonsY + delta));
            _hint.rectTransform.anchoredPosition = new Vector2(20f, -(HintY + delta));
            ((RectTransform)_panel.transform).sizeDelta = new Vector2(PanelW, PanelBaseH + delta);
            _laidOutLine = _line.text;
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("BanditDemandUi");
            _canvas.sortingOrder = 24; // above the dock/trade panels
            UiNav.Enable(_canvas.gameObject); // pad: stick walks Comply / Refuse, A picks (#1043) — 1/2 keys stay
            var root = _canvas.transform;

            // Opaque like every text-carrying panel (#3662); no scrim and clear of VEGA's comms zone (bottom left).
            _panel = UiKit.AddCenteredPanel(root, "BanditPanel", PanelW, PanelBaseH, 60f);
            _panel.GetComponent<Image>().sprite = UiKit.DialogSprite;

            var t = _panel.transform;
            _title = UiKit.AddText(t, 20f, 12f, RowW, 26f, string.Empty, 22, new Color(1f, 0.55f, 0.3f), TextAnchor.MiddleLeft, FontStyle.Bold);
            _line = UiKit.AddText(t, 20f, 44f, RowW, LineBaseH, string.Empty, 18, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Italic);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap; // hail lines wrap; ReflowForLine grows the row
            _items = UiKit.AddText(t, 20f, ItemsY, RowW, 26f, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _countdown = UiKit.AddText(t, 20f, CountdownY, RowW, 24f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            // 44 high: a finger's target (#3662). Comply first, so a pad lands on the answer that ends the hold-up peacefully.
            _comply = UiKit.AddButton(t, 20f, ButtonsY, 230f, ButtonH, string.Empty, () => Answer(true));
            _complyLabel = _comply.GetComponentInChildren<Text>();
            _refuse = UiKit.AddButton(t, 270f, ButtonsY, 230f, ButtonH, string.Empty, () => Answer(false));
            _refuseLabel = _refuse.GetComponentInChildren<Text>();

            _hint = UiKit.AddText(t, 20f, HintY, RowW, 22f, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleCenter);

            _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.BanditDemandReceived -= OnDemand;
                Game.Network.BanditResultReceived -= OnResult;
            }

            // A scene-root canvas — it goes with the rig (#2936).
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
