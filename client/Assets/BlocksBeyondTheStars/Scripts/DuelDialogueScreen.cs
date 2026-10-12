// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Guardian core's duel dialogue on screen (#2478, E28, synthesis §6.5 <c>fin.duel</c>): a full letterbox, the core's
    /// box under the top bar — its name in its colour, "Argument n of 4", its reaction to the last answer in italics and its
    /// statement — and the answers as the dialogue's OWN large buttons over the lower bar (mouse, pad, touch), never the
    /// hotbar digits. The frame's corner button is "Step back". Pure presentation on its own <see cref="CinematicFrame"/>
    /// (sorting order like the director's scenes); <see cref="CutsceneDirector"/> decides when it shows and what it says, and
    /// the server keeps the authority over the duel. Pad: <see cref="UiNav"/> walks the answers, A picks.
    /// </summary>
    public sealed class DuelDialogueScreen
    {
        private const float RefW = 1920f;
        private const float PanelW = 1240f, PanelY = 150f, PanelH = 330f;
        // The answers stack upwards from just above the corner button's row (its plate starts at y ≈ 1012), so the last one
        // never sits under it however many a node offers.
        private const float ChoiceW = 1240f, ChoiceH = 72f, ChoiceGap = 10f, ChoicesBottom = 1000f;

        /// <summary>The most answers a node offers (the pack's nodes carry three).</summary>
        public const int MaxChoices = 4;

        private readonly CinematicFrame _frame;
        private readonly Image _panel;
        private readonly Text _name;
        private readonly Text _progress;
        private readonly Text _words;
        private readonly Button[] _choices = new Button[MaxChoices];
        private readonly Text[] _choiceLabels = new Text[MaxChoices];
        private int _picked = -1;
        private float _scale = -1f;

        private DuelDialogueScreen(CinematicFrame frame)
        {
            _frame = frame;
            var root = frame.Root;

            _panel = UiKit.AddPanel(root, (RefW - PanelW) * 0.5f, PanelY, PanelW, PanelH, new Color(0.10f, 0.03f, 0.04f, 0.9f));
            _name = UiKit.AddText(_panel.transform, 32f, 14f, PanelW - 400f, 40f, string.Empty, 28, new Color(1f, 0.42f, 0.36f),
                TextAnchor.MiddleLeft, FontStyle.Bold);
            _progress = UiKit.AddText(_panel.transform, PanelW - 352f, 14f, 320f, 40f, string.Empty, 20, new Color(1f, 0.78f, 0.74f, 0.8f),
                TextAnchor.MiddleRight);
            _words = UiKit.AddText(_panel.transform, 32f, 62f, PanelW - 64f, PanelH - 76f, string.Empty, 27, UiKit.TextCol, TextAnchor.UpperLeft);
            _words.horizontalOverflow = HorizontalWrapMode.Wrap;
            _words.verticalOverflow = VerticalWrapMode.Overflow;
            _words.supportRichText = true; // the core's reaction is set in italics

            for (int i = 0; i < MaxChoices; i++)
            {
                int index = i;
                _choices[i] = UiKit.AddButton(root, (RefW - ChoiceW) * 0.5f, ChoicesBottom - ChoiceH, ChoiceW, ChoiceH,
                    string.Empty, () => _picked = index);
                _choiceLabels[i] = _choices[i].GetComponentInChildren<Text>();
                if (_choiceLabels[i] != null)
                {
                    _choiceLabels[i].alignment = TextAnchor.MiddleLeft;
                    UiKit.FitLabel(_choiceLabels[i], 14, 24); // two lines for the longest German answer, smaller before cut
                }

                _choices[i].gameObject.SetActive(false);
            }

            // The pad walks the answers with the stick and picks with A; B steps back (the director reads it as UiCancel).
            UiNav.Enable(root.gameObject);
        }

        /// <summary>Builds the dialogue on its own canvas at <paramref name="sortingOrder"/>, with the corner button.</summary>
        public static DuelDialogueScreen Create(string name, int sortingOrder)
        {
            var frame = CinematicFrame.Create(name, sortingOrder);
            frame.EnableSkipButton(); // "Step back" — the label is the director's
            return new DuelDialogueScreen(frame);
        }

        public void SetLetterbox(float amount01) => _frame.SetLetterbox(amount01);

        public void SetFade(float alpha) => _frame.SetFade(alpha);

        public void SetHint(string text, float alpha) => _frame.SetHint(text, alpha);

        /// <summary>True once per click/tap on the corner button since the last call.</summary>
        public bool ConsumeSkipClick() => _frame.ConsumeSkipClick();

        /// <summary>The answer clicked or tapped since the last call (its index), or -1.</summary>
        public int ConsumePick()
        {
            int picked = _picked;
            _picked = -1;
            return picked;
        }

        /// <summary>Shows a node: who speaks, "Argument n of 4", the core's words and the answers.</summary>
        public void Show(CutsceneSpeaker speaker, string progress, string words, IReadOnlyList<string> choices)
        {
            ApplyScale();
            _name.text = speaker?.Name ?? string.Empty;
            if (speaker != null)
            {
                _name.color = new Color(speaker.Color.r, speaker.Color.g, speaker.Color.b, 1f);
            }

            _progress.text = progress ?? string.Empty;
            _words.text = words ?? string.Empty;
            int count = choices == null ? 0 : Mathf.Min(MaxChoices, choices.Count);
            float top = ChoicesBottom - count * ChoiceH - Mathf.Max(0, count - 1) * ChoiceGap;
            for (int i = 0; i < MaxChoices; i++)
            {
                bool used = i < count;
                _choices[i].gameObject.SetActive(used);
                if (!used)
                {
                    continue;
                }

                ((RectTransform)_choices[i].transform).anchoredPosition = new Vector2((RefW - ChoiceW) * 0.5f, -(top + i * (ChoiceH + ChoiceGap)));
                if (_choiceLabels[i] != null)
                {
                    _choiceLabels[i].text = choices[i];
                }
            }

            _picked = -1;
        }

        /// <summary>Whether the answers take a pick (they wait for the core after one).</summary>
        public void SetChoicesOpen(bool open)
        {
            for (int i = 0; i < MaxChoices; i++)
            {
                if (_choices[i].interactable != open)
                {
                    _choices[i].interactable = open;
                }
            }
        }

        /// <summary>Tears the dialogue down (the frame's canvas goes with it).</summary>
        public void Destroy()
        {
            if (_frame != null)
            {
                Object.Destroy(_frame.gameObject);
            }
        }

        /// <summary>The texts follow the player's UI scale (#2461); the boxes keep their place on the 1920×1080 frame.</summary>
        private void ApplyScale()
        {
            float s = UiKit.UserScale;
            if (Mathf.Approximately(s, _scale))
            {
                return;
            }

            _scale = s;
            _name.fontSize = Mathf.RoundToInt(28 * s);
            _progress.fontSize = Mathf.RoundToInt(20 * s);
            _words.fontSize = Mathf.RoundToInt(27 * s);
            for (int i = 0; i < MaxChoices; i++)
            {
                if (_choiceLabels[i] != null)
                {
                    _choiceLabels[i].resizeTextMaxSize = Mathf.RoundToInt(24 * s);
                    _choiceLabels[i].resizeTextMinSize = Mathf.Min(_choiceLabels[i].resizeTextMaxSize, Mathf.RoundToInt(14 * s));
                }
            }
        }
    }
}
