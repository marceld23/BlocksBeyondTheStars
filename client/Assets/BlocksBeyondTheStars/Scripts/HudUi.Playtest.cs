// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Playtests;
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The playtest card of <see cref="HudUi"/> (#3846): while the player works on a playtest task in a world, the card
    /// in the right column's context stack says what is being tested right now — the task, which of its steps, the
    /// step's whole text, how far a counting step is, and what the tester has to do about it (nothing: the game notices
    /// it; or tick it off with the Test Pilot key). With every step closed it stays and says that the report is due.
    /// What it shows is decided by <see cref="PlaytestGuide"/>; it is the player's own reminder and nothing is asked of
    /// the server for it. The card grows with its text — a task's text comes from the fetched catalogue and is never cut.
    /// </summary>
    public sealed partial class HudUi
    {
        // In HUD reference units: the full width of the right column, like the scan card.
        private const float PlaytestW = HudZones.RightW, PlaytestPad = 10f, PlaytestTextW = PlaytestW - (2f * PlaytestPad);

        private GameObject _playtestPanel;
        private TMP_Text _playtestHead, _playtestTitle, _playtestBody, _playtestHint;
        private int _playtestStamp = int.MinValue;
        private Func<string, bool?> _playtestServerReports;

        private void BuildPlaytestCard(Transform root)
        {
            _playtestPanel = Panel(root, HudZones.RightX, 460, PlaytestW, 96).gameObject;
            var panel = _playtestPanel.transform;
            _playtestHead = UiText.Add(panel, PlaytestPad, 6, PlaytestTextW, 18, string.Empty, 12, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);
            _playtestTitle = UiText.Add(panel, PlaytestPad, 24, PlaytestTextW, 20, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _playtestBody = UiText.Add(panel, PlaytestPad, 48, PlaytestTextW, 40, string.Empty, 14, UiKit.TextCol, TextAnchor.UpperLeft);
            _playtestHint = UiText.Add(panel, PlaytestPad, 92, PlaytestTextW, 18, string.Empty, 12, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiText.Fit(_playtestHead, 9f);
            UiText.Fit(_playtestTitle, 10f);
            UiText.Wrap(_playtestBody, truncate: true);
            UiText.Wrap(_playtestHint);

            // A task's title and text come from the fetched catalogue — shown verbatim.
            _playtestTitle.richText = false;
            _playtestBody.richText = false;
            _playtestServerReports = pattern => PlaytestHub.Instance != null ? PlaytestHub.Instance.ServerReports(pattern) : null;
            _playtestPanel.SetActive(false);
        }

        private void RefreshPlaytestCard()
        {
            var hub = PlaytestHub.Instance;
            var card = hub != null ? PlaytestGuide.CardFor(hub.Tracker, _playtestServerReports) : null;
            bool show = card != null;
            bool appeared = show && !_playtestPanel.activeSelf;
            if (_playtestPanel.activeSelf != show)
            {
                _playtestPanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // A pad has no Test Pilot key: its way to the page is the pause menu, and the hint says so.
            string key = InputMap.ActiveDevice == InputDeviceKind.Gamepad ? null : InputMap.Glyph(InputAction.TestPilot);
            int stamp = PlaytestCardStamp(hub.Revision, card, key);
            if (stamp != _playtestStamp || appeared)
            {
                _playtestStamp = stamp;
                WritePlaytestCard(card, key);
            }

            StackContextCard(_playtestPanel, appeared); // after the text: the stack reads the card's size
        }

        /// <summary>One stamp over everything the card shows: the text is rebuilt only when something changed. The count
        /// of a counting step is part of it — the hub's revision does not move for a count, only for a closed step.</summary>
        private static int PlaytestCardStamp(int revision, PlaytestCard card, string key)
        {
            int stamp = revision;
            stamp = (stamp * 397) ^ card.StepNumber;
            stamp = (stamp * 397) ^ card.Count;
            stamp = (stamp * 397) ^ (int)card.Hint;
            stamp = (stamp * 397) ^ card.Task.Id.GetHashCode();
            return (stamp * 397) ^ (key != null ? key.GetHashCode() : 0);
        }

        private void WritePlaytestCard(PlaytestCard card, string key)
        {
            var loc = Game.Localizer;
            string lang = Settings != null ? Settings.Language : "en";
            _playtestTitle.text = card.Task.Title.For(lang);
            if (card.AllDone)
            {
                _playtestHead.text = loc.Get("ui.testpilot.hud");
                _playtestBody.text = loc.Get("ui.testpilot.hud_all_done");
                _playtestHint.text = PlaytestKeyLine(loc, key, "ui.testpilot.hud_report_key", "ui.testpilot.hud_report_pad");
                _playtestHint.color = UiKit.Ok;
            }
            else
            {
                _playtestHead.text = string.Format(loc.Get("ui.testpilot.hud_step"), card.StepNumber, card.StepCount);
                string text = card.Step.Text.For(lang);
                if (card.Counts)
                {
                    text += "\n" + string.Format(loc.Get("ui.testpilot.step_count"), card.Count, card.Required);
                }

                _playtestBody.text = text;
                bool tick = card.Hint == PlaytestStepHint.TickOff;
                _playtestHint.text = tick
                    ? PlaytestKeyLine(loc, key, "ui.testpilot.hud_tick_key", "ui.testpilot.hud_tick_pad")
                    : loc.Get("ui.testpilot.step_auto_hint");
                _playtestHint.color = tick ? UiKit.Warn : UiKit.CyanDim;
            }

            ReflowPlaytestCard();
        }

        /// <summary>#3848: the game recognised the last open step — the celebration banner says that every step is done,
        /// names the task and the way to the report, and plays the success sound. The card keeps saying it afterwards.</summary>
        private void StartPlaytestReadyToast(string title)
        {
            EnsureResearchToast();
            var loc = Game.Localizer;
            string key = InputMap.ActiveDevice == InputDeviceKind.Gamepad ? null : InputMap.Glyph(InputAction.TestPilot);
            _researchHead.text = loc.Get("ui.testpilot.hud_all_done").ToUpperInvariant();
            _researchName.text = title;
            _researchHead.color = new Color(UiKit.Ok.r * 0.85f, UiKit.Ok.g * 0.85f, UiKit.Ok.b * 0.85f);
            _researchName.color = UiKit.Ok;
            SetToastDetail(PlaytestKeyLine(loc, key, "ui.testpilot.hud_report_key", "ui.testpilot.hud_report_pad"), _researchHead.color);
            var icon = UiKit.Icon("btn_testpilot") ?? UiKit.Icon("map_mark_star");
            _researchIcon.enabled = icon != null;
            if (icon != null)
            {
                _researchIcon.texture = icon.texture;
                _researchIcon.uvRect = new Rect(0, 0, 1, 1);
                _researchIcon.color = UiKit.Ok;
            }

            _toastHoldNow = AchievementHold + 2f; // it names a key — a moment longer to read
            _toastGlow = UiKit.Ok;
            _researchGo.SetActive(true);
            _researchAge = 0f;
            _researchFade.alpha = UiKit.ReducedMotion ? 0f : 1f;
            _researchRect.localScale = UiKit.ReducedMotion ? Vector3.one : Vector3.one * 0.75f;
            ClientAudio.Instance?.Cue("ui_success", 0.9f);
        }

        /// <summary>A hint that names the Test Pilot key by its binding — or, for a pad, the way through the pause menu.</summary>
        private static string PlaytestKeyLine(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key, string withKey, string forPad)
            => key != null ? loc.Get(withKey).Replace("{key}", key) : loc.Get(forPad);

        /// <summary>Sizes the card to the text it shows: the step's text and the hint under it take the height they need
        /// (TMP measures the wrapped text; a height of 0 asks for "as tall as it needs"). The ceiling is what the
        /// catalogue's longest text can take; past it the truncation set up in Build is the safety net.</summary>
        private void ReflowPlaytestCard()
        {
            const float bodyTop = 48f, gap = 4f, padBottom = 8f, bodyMax = 230f;
            float bodyH = Mathf.Min(_playtestBody.GetPreferredValues(_playtestBody.text, PlaytestTextW, 0f).y, bodyMax);
            UiKit.Place(_playtestBody.gameObject, PlaytestPad, bodyTop, PlaytestTextW, bodyH);
            float y = bodyTop + bodyH + gap;
            float hintH = _playtestHint.GetPreferredValues(_playtestHint.text, PlaytestTextW, 0f).y;
            UiKit.Place(_playtestHint.gameObject, PlaytestPad, y, PlaytestTextW, hintH);
            ((RectTransform)_playtestPanel.transform).sizeDelta = new Vector2(PlaytestW, y + hintH + padBottom);
        }
    }
}
