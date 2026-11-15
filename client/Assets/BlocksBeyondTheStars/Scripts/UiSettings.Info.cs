// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The description card of the settings screen (#3666): the right-hand card shows the label and the explanation of
    /// the row under the pointer — or of the row the pad's focus is on. The explanations used to stand between the rows
    /// as small inline lines ("Warmer colours…", "Caves and nights are meant to be dark…") and broke the list up; a row
    /// without one shows its label alone, and with no row picked yet the card shows the category's own line.
    /// </summary>
    public sealed partial class UiSettings
    {
        private const float InfoPad = 26f;
        private const float InfoTextW = InfoW - 2f * InfoPad;
        private const float InfoTitleY = CardY + 90f;
        private const int InfoTitleSize = 26, InfoBodySize = 19;

        // What the card shows. Kept across a rebuild (a click on the explained row rebuilds the screen); null = the
        // category's own line (a new category, a new language).
        private string _infoTitle, _infoBody;
        private Text _infoTitleText, _infoBodyText;
        private UiSettingsRowInfo _litRow;  // the row the card explains, lit faintly
        private GameObject _lastSelected;   // the pad's focus seen last frame (TrackFocus)

        private void BuildInfoCard()
        {
            UiKit.AddCard(_root, InfoX, CardY, InfoW, CardH);
            UiKit.AddIcon(_root, InfoX + InfoPad, CardY + 22f, 36f, CategoryIcons[(int)s_category]);
            UiKit.AddKicker(_root, InfoX + InfoPad + 48f, CardY + 28f, InfoTextW - 48f, CategoryTitle(s_category));
            UiKit.AddImage(_root, InfoX + InfoPad, CardY + 72f, InfoTextW, 2f, UiKit.SolidSprite, new Color(0.12f, 0.35f, 0.5f, 0.9f));

            _infoTitleText = UiKit.AddText(_root, InfoX + InfoPad, InfoTitleY, InfoTextW, 40f, string.Empty, InfoTitleSize, UiKit.TextCol,
                TextAnchor.UpperLeft, FontStyle.Bold);
            _infoTitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _infoBodyText = UiKit.AddText(_root, InfoX + InfoPad, InfoTitleY + 50f, InfoTextW, CardY + CardH - InfoTitleY - 80f, string.Empty,
                InfoBodySize, new Color(0.74f, 0.84f, 0.94f), TextAnchor.UpperLeft);
            _infoBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _infoBodyText.lineSpacing = 1.1f;

            if (_infoTitle == null)
            {
                ShowInfo(CategoryTitle(s_category), L(s_category == Category.Language ? "ui.settings.info_language" : "ui.settings.info_default"));
            }
            else
            {
                ShowInfo(_infoTitle, _infoBody);
            }
        }

        /// <summary>Puts <paramref name="title"/> and <paramref name="body"/> on the card, the body right under the title
        /// however many lines the title takes.</summary>
        private void ShowInfo(string title, string body)
        {
            _infoTitle = title ?? string.Empty;
            _infoBody = body ?? string.Empty;
            if (_infoTitleText == null || _infoBodyText == null)
            {
                return;
            }

            _infoTitleText.text = _infoTitle;
            float titleH = Mathf.Max(32f, Mathf.Ceil(UiKit.MeasureHeight(_infoTitle, InfoTextW, InfoTitleSize, FontStyle.Bold, false)));
            _infoTitleText.rectTransform.sizeDelta = new Vector2(InfoTextW, titleH);
            _infoBodyText.text = _infoBody;
            _infoBodyText.rectTransform.anchoredPosition = new Vector2(InfoX + InfoPad, -(InfoTitleY + titleH + 14f));
        }

        /// <summary>The pointer entered a row, or the pad's focus moved onto one of its controls.</summary>
        private void OnRowEntered(UiSettingsRowInfo row)
        {
            if (row == null)
            {
                return;
            }

            if (_litRow != null && _litRow != row)
            {
                _litRow.SetLit(false);
            }

            _litRow = row;
            row.SetLit(true);
            ShowInfo(row.Label, row.Info);
        }

        /// <summary>The pad's focus (the EventSystem's selection) moving onto a row's control explains that row, as the
        /// pointer does — a pad player has no pointer to hover with. One reference compare per frame otherwise.</summary>
        private void TrackFocus()
        {
            var system = EventSystem.current;
            var selected = system != null ? system.currentSelectedGameObject : null;
            if (selected == _lastSelected)
            {
                return;
            }

            _lastSelected = selected;
            var row = selected != null ? selected.GetComponentInParent<UiSettingsRowInfo>() : null;
            if (row != null)
            {
                OnRowEntered(row);
            }
        }
    }

    /// <summary>
    /// One row of the settings screen (#3666): what the description card shows for it, and the faint light that marks the
    /// row the card explains. Sits on the row's container, so it hears the pointer entering anywhere on the row — enter
    /// events also reach the parents of the control under the pointer.
    /// </summary>
    public sealed class UiSettingsRowInfo : MonoBehaviour, IPointerEnterHandler
    {
        /// <summary>The row backing's resting colour: invisible, but still a raycast target.</summary>
        public static readonly Color Idle = new Color(0f, 0f, 0f, 0.001f);

        /// <summary>The row backing while the card explains the row.</summary>
        public static readonly Color Lit = new Color(0.40f, 0.82f, 1f, 0.08f);

        /// <summary>The row's label (the card's title).</summary>
        public string Label = string.Empty;

        /// <summary>The row's explanation; empty = none.</summary>
        public string Info = string.Empty;

        /// <summary>The row's backing image (lit while explained).</summary>
        public Image Backing;

        /// <summary>Called when the pointer enters the row.</summary>
        public System.Action<UiSettingsRowInfo> Entered;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke(this);

        /// <summary>Lights the row (or puts it back to rest).</summary>
        public void SetLit(bool lit)
        {
            if (Backing != null)
            {
                Backing.color = lit ? Lit : Idle;
            }
        }
    }
}
