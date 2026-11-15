// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How much weight a menu action carries (#3662). A screen has exactly ONE <see cref="Primary"/> action — the filled
    /// button that says "this is the next step" — and everything else steps back: <see cref="Secondary"/> is the outlined
    /// button every screen used for everything until then, <see cref="Quiet"/> a dimmer outline for side actions (Back,
    /// Settings, Credits), <see cref="Danger"/> the red, set-apart button of a destructive action (delete).
    /// </summary>
    public enum ButtonTier
    {
        Primary,
        Secondary,
        Quiet,
        Danger,
    }

    /// <summary>
    /// The menu template (#3662): one header, one set of button tiers and one set of icon meanings for every menu screen.
    /// Before it, every screen placed its own title and Back button — Back sat in five different places and carried the
    /// power icon of Quit, titles came in four styles and three alignments, and every action looked the same.
    /// <para>
    /// Geometry is in the menus' 1920×1080 reference space (<see cref="CreateCanvas"/>'s default): the content runs from
    /// <see cref="MenuLeft"/> to <see cref="MenuRight"/>; the header holds Back (top left, the arrow icon — Esc and pad B
    /// do the same through <c>AppShell</c>), then the title and the subtitle on the content's left edge; the content starts
    /// at <see cref="MenuContentTop"/>; a footer row (when a screen has one) sits at <see cref="MenuFooterY"/>.
    /// </para>
    /// <para>
    /// Icons with a fixed meaning: <c>btn_back</c> (arrow) = back, <c>btn_exit</c> (power) = quit the game ONLY,
    /// <c>btn_delete</c> (bin) = delete, <c>btn_new</c> (plus) = create something new, <c>btn_close</c> (cross) = close an
    /// in-game screen, <c>btn_pause</c> = the pause menu.
    /// </para>
    /// </summary>
    public static partial class UiKit
    {
        /// <summary>Left edge of a menu screen's content (the main menu's button column shares it).</summary>
        public const float MenuLeft = ShellColumnX;

        /// <summary>Right edge of a menu screen's content.</summary>
        public const float MenuRight = 1830f;

        /// <summary>Width between <see cref="MenuLeft"/> and <see cref="MenuRight"/>.</summary>
        public const float MenuWidth = MenuRight - MenuLeft;

        /// <summary>The header's Back button: top left.</summary>
        public const float MenuBackY = 36f, MenuBackW = 210f, MenuBackH = 52f;

        /// <summary>The screen title's row and font size.</summary>
        public const float MenuTitleY = 100f, MenuTitleH = 80f;

        /// <summary>The screen title's font size.</summary>
        public const int MenuTitleSize = 54;

        /// <summary>The subtitle line under the title.</summary>
        public const float MenuSubtitleY = 182f;

        /// <summary>Where a menu screen's content starts.</summary>
        public const float MenuContentTop = 236f;

        /// <summary>A menu screen's footer row (Back-to-defaults links, the screen's primary action when it has no better
        /// place).</summary>
        public const float MenuFooterY = 990f, MenuFooterH = 56f;

        /// <summary>The lowest y a content panel reaches when the screen has a footer row.</summary>
        public const float MenuContentBottom = 966f;

        /// <summary>Text on a filled primary button: dark on the bright fill.</summary>
        public static readonly Color PrimaryText = new Color(0.01f, 0.07f, 0.13f);

        /// <summary>The red of a destructive action.</summary>
        public static readonly Color DangerCol = new Color(1f, 0.45f, 0.50f);

        /// <summary>A text link (tertiary action).</summary>
        public static readonly Color LinkCol = new Color(0.45f, 0.86f, 1f);

        /// <summary>An opaque content card: menu panels carry text, so the animated backdrop must not show through them
        /// (the translucent <see cref="Panel"/> let the menu ship make buttons look disabled).</summary>
        public static readonly Color CardFill = new Color(0.03f, 0.08f, 0.17f, 0.96f);

        private static Sprite _primarySprite, _secondarySprite, _quietSprite, _dangerSprite;

        /// <summary>The outlined secondary button on an OPAQUE fill: the stock <see cref="ButtonSprite"/> bakes alpha 0.82,
        /// so the moving menu ship behind it made menu buttons look disabled.</summary>
        public static Sprite SecondarySprite => _secondarySprite != null ? _secondarySprite
            : _secondarySprite = RoundedSprite(14, 2, new Color(0.05f, 0.12f, 0.24f, 0.97f), Cyan);

        /// <summary>The filled primary button: a bright cyan fill with a lighter edge.</summary>
        public static Sprite PrimarySprite => _primarySprite != null ? _primarySprite
            : _primarySprite = RoundedSprite(14, 2, new Color(0.27f, 0.74f, 0.95f, 1f), new Color(0.72f, 0.94f, 1f, 1f));

        /// <summary>The quiet button: a darker fill and a dimmed edge.</summary>
        public static Sprite QuietSprite => _quietSprite != null ? _quietSprite
            : _quietSprite = RoundedSprite(14, 2, new Color(0.03f, 0.08f, 0.16f, 0.92f), CyanDim);

        /// <summary>The destructive button: a dark red fill with a red edge.</summary>
        public static Sprite DangerSprite => _dangerSprite != null ? _dangerSprite
            : _dangerSprite = RoundedSprite(14, 2, new Color(0.22f, 0.04f, 0.07f, 0.92f), new Color(1f, 0.45f, 0.50f, 0.9f));

        /// <summary>
        /// The template header: Back top left (arrow icon), the title and the subtitle on the content's left edge. Esc and
        /// pad B already back out of every menu phase in <c>AppShell</c>; the button is the same way for the mouse.
        /// Returns the Back button.
        /// </summary>
        public static Button AddScreenHeader(Transform root, string title, string subtitle, System.Action onBack, string backLabel)
        {
            var back = AddTierButton(root, MenuLeft, MenuBackY, MenuBackW, MenuBackH, backLabel, onBack, ButtonTier.Quiet, "btn_back");
            AddLogo(root, MenuLeft, MenuTitleY, 1400f, MenuTitleH, title, MenuTitleSize);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = AddText(root, MenuLeft + 4f, MenuSubtitleY, 1500f, 30f, subtitle, 20, CyanDim, TextAnchor.MiddleLeft);
                FitLabelSingleLine(sub, 1500f, 14, 20);
            }

            return back;
        }

        /// <summary>A button in the given <paramref name="tier"/> (see <see cref="ButtonTier"/>), with an icon.</summary>
        public static Button AddTierButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick,
            ButtonTier tier, string icon = null)
        {
            var button = AddButton(parent, x, y, w, h, label, onClick, icon);
            StyleTier(button, tier);
            return button;
        }

        /// <summary>Restyles a button built by <see cref="AddButton"/> to a tier — also used to move the "selected" look
        /// between the segments of a <see cref="Segmented"/> bar.</summary>
        public static void StyleTier(Button button, ButtonTier tier)
        {
            if (button == null || !(button.targetGraphic is Image frame))
            {
                return;
            }

            frame.sprite = tier switch
            {
                ButtonTier.Primary => PrimarySprite,
                ButtonTier.Quiet => QuietSprite,
                ButtonTier.Danger => DangerSprite,
                _ => SecondarySprite,
            };

            var colors = button.colors;
            // The stock dim-until-hover tint would grey the bright primary fill; filled buttons stay lit and only brighten.
            colors.normalColor = tier == ButtonTier.Primary ? new Color(0.92f, 0.96f, 1f, 1f) : new Color(0.70f, 0.74f, 0.80f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = tier == ButtonTier.Primary ? new Color(0.75f, 0.9f, 1f, 1f) : Cyan;
            button.colors = colors;

            Color ink = tier switch
            {
                ButtonTier.Primary => PrimaryText,
                ButtonTier.Danger => DangerCol,
                ButtonTier.Quiet => new Color(0.80f, 0.88f, 0.96f),
                _ => TextCol,
            };
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                text.color = ink;
            }

            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                if (image != frame && image.gameObject.name != HitAreaName)
                {
                    // The line icons are cyan art: dark on the cyan fill, red on a danger button, as drawn elsewhere.
                    image.color = tier == ButtonTier.Primary ? PrimaryText : tier == ButtonTier.Danger ? DangerCol : Color.white;
                }
            }
        }

        /// <summary>True for a button drawn in the <see cref="ButtonTier.Danger"/> tier — the pad's first focus avoids it
        /// (a delete confirmation opens on Cancel).</summary>
        public static bool IsDangerButton(Selectable control)
            => _dangerSprite != null && control != null && control.targetGraphic is Image frame && frame.sprite == _dangerSprite;

        /// <summary>The smallest touch target in the menus' reference pixels (a finger on a tablet, a child's click).</summary>
        public const float MinTapSize = 44f;

        private const string HitAreaName = "HitArea";

        /// <summary>
        /// Grows a small control's touch target to at least <paramref name="minW"/>×<paramref name="minH"/> without
        /// changing its look: an invisible raycast child, centred on the control. Clicks on it reach the control (uGUI
        /// bubbles them up to the button). The caller keeps the bigger area clear of neighbouring controls.
        /// </summary>
        public static void AddHitArea(Selectable control, float minW = MinTapSize, float minH = MinTapSize)
        {
            if (control == null || !(control.transform is RectTransform rt))
            {
                return;
            }

            var size = rt.rect.size;
            if (size.x <= 0f && size.y <= 0f)
            {
                size = rt.sizeDelta;
            }

            float w = Mathf.Max(size.x, minW), h = Mathf.Max(size.y, minH);
            if (w <= size.x && h <= size.y)
            {
                return;
            }

            var go = new GameObject(HitAreaName, typeof(RectTransform));
            var area = (RectTransform)go.transform;
            area.SetParent(rt, false);
            area.SetAsFirstSibling(); // behind the control's own text and icon
            area.anchorMin = area.anchorMax = area.pivot = new Vector2(0.5f, 0.5f);
            area.anchoredPosition = Vector2.zero;
            area.sizeDelta = new Vector2(w, h);
            var hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f); // a hit area, not a look (as the cycle's value area)
            hit.raycastTarget = true;
        }

        /// <summary>
        /// Makes the area of a control's label a second mouse / touch target of <paramref name="control"/> — a click there
        /// is a click on it (the settings rows' state word does the same for their switch). Invisible and no focus stop: the
        /// pad keeps one stop, the control. Add it before the label's texts so they draw over it.
        /// </summary>
        public static Button AddLabelHit(Transform parent, float x, float y, float w, float h, Button control)
        {
            var go = new GameObject("LabelHit", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() =>
            {
                if (control != null && control.IsInteractable())
                {
                    control.onClick.Invoke();
                }
            });
            return button;
        }

        /// <summary>
        /// A tertiary action as a text link (cyan, underlined) — "Forgot password?", "Read more", "Reset this category".
        /// The underline is drawn as a 1 px bar the width of the text, since the legacy <see cref="Text"/> has no
        /// underline style. A link lower than <see cref="MinTapSize"/> keeps its text where it was asked for but gets a
        /// taller hit area around it (the box grows evenly up and down; the text stays vertically centred).
        /// </summary>
        public static Button AddLinkButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick,
            int size = 18, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (h < MinTapSize)
            {
                y -= (MinTapSize - h) / 2f;
                h = MinTapSize;
            }

            var go = new GameObject("Link", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var hit = go.AddComponent<Image>();
            hit.sprite = SolidSprite;
            hit.color = new Color(0f, 0f, 0f, 0.001f); // a hit area, not a visible backing
            var button = go.AddComponent<Button>();
            button.targetGraphic = hit;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            button.colors = colors;
            go.AddComponent<UiHover>();
            button.onClick.AddListener(UiSound.Click);
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = AddText(go.transform, 0f, 0f, w, h, label, size, LinkCol, anchor, FontStyle.Bold);
            FitLabelSingleLine(text, w, 11, size);
            float textW = Mathf.Min(w, MeasureWidth(label, text.fontSize));
            float underlineX = anchor == TextAnchor.MiddleRight ? w - textW : anchor == TextAnchor.MiddleCenter ? (w - textW) / 2f : 0f;
            var underline = AddImage(go.transform, underlineX, h / 2f + text.fontSize * 0.55f, textW, 1.5f, SolidSprite,
                new Color(LinkCol.r, LinkCol.g, LinkCol.b, 0.75f));
            underline.raycastTarget = false;
            return button;
        }

        /// <summary>A small section label above a block of content: cyan, bold, upper case.</summary>
        public static Text AddKicker(Transform parent, float x, float y, float w, string text, Color? color = null)
        {
            var label = AddText(parent, x, y, w, 24f, (text ?? string.Empty).ToUpperInvariant(), 16, color ?? Cyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            FitLabelSingleLine(label, w, 11, 16);
            return label;
        }

        /// <summary>An opaque content card (holo frame) — menu panels carry text, the backdrop stays behind them.</summary>
        public static Transform AddCard(Transform parent, float x, float y, float w, float h)
            => UiHolo.AddPanel(parent, x, y, w, h, CardFill, 18f, 1.5f, 1f).transform;

        /// <summary>
        /// A row of tabs (#3662): the selected segment is drawn as a primary button, the others quiet. Selecting calls
        /// <paramref name="onSelect"/> with the index; <see cref="Segmented.Select"/> moves the look without a callback.
        /// A screen whose shoulder buttons are free lets a pad step the tabs with LB / RB
        /// (<see cref="Segmented.StepWithShoulders"/>); the console keeps them for its main tabs.
        /// </summary>
        public static Segmented AddSegmented(Transform parent, float x, float y, float w, float h, IReadOnlyList<string> labels,
            int selected, System.Action<int> onSelect, IReadOnlyList<string> icons = null, float gap = 8f)
        {
            var bar = new Segmented();
            int n = labels.Count;
            float segW = (w - gap * (n - 1)) / n;
            bar.Buttons = new Button[n];
            for (int i = 0; i < n; i++)
            {
                int index = i;
                string icon = icons != null && i < icons.Count ? icons[i] : null;
                bar.Buttons[i] = AddButton(parent, x + i * (segW + gap), y, segW, h, labels[i], () =>
                {
                    bar.Select(index);
                    onSelect?.Invoke(index);
                }, icon);
            }

            bar.Select(selected);
            return bar;
        }

        /// <summary>A tab bar built by <see cref="AddSegmented"/>.</summary>
        public sealed class Segmented
        {
            /// <summary>One button per tab, in order.</summary>
            public Button[] Buttons;

            /// <summary>The selected tab.</summary>
            public int Selected { get; private set; }

            /// <summary>Marks tab <paramref name="index"/> as selected (look only, no callback).</summary>
            public void Select(int index)
            {
                Selected = index;
                for (int i = 0; i < Buttons.Length; i++)
                {
                    StyleTier(Buttons[i], i == index ? ButtonTier.Primary : ButtonTier.Quiet);
                }
            }

            /// <summary>LB / RB step through the tabs while a pad is in hand — the console convention (#1409) — and the
            /// screen's hint strip names them (<c>ui.pad.tabs</c>, as on the console). The bar must already sit under the
            /// screen's <see cref="UiNavFocus"/> root.</summary>
            public void StepWithShoulders()
            {
                if (Buttons == null || Buttons.Length < 2 || Buttons[0] == null)
                {
                    return;
                }

                var tabs = Buttons[0].gameObject.AddComponent<SegmentedShoulderTabs>();
                tabs.Bar = this;
                Buttons[0].GetComponentInParent<UiNavFocus>()?.AddHint("ui.pad.tabs", PadButton.Lb, PadButton.Rb);
            }
        }

        /// <summary>
        /// A cycle control <c>‹ value ›</c> (#3666): the value centred between two arrow buttons, so it can no longer be
        /// mistaken for a text field. <paramref name="step"/> gets -1 or +1 and returns the new value text. The value area
        /// itself steps forward on click, as the old single cycle buttons did. For a pad (and the arrow keys) the value is
        /// the control's ONE focus stop: Left / Right step it (<see cref="StepOnMove"/>), Up / Down leave the row; the
        /// arrows stay mouse and touch targets.
        /// </summary>
        public static Text AddCycle(Transform parent, float x, float y, float w, float h, string value, System.Func<int, string> step)
        {
            var frame = AddImage(parent, x, y, w, h, QuietSprite, Color.white, Image.Type.Sliced);
            frame.raycastTarget = false;
            Text valueText = null;
            var mid = AddButton(parent, x + h, y, w - 2f * h, h, value, () => valueText.text = step(+1));
            StyleTier(mid, ButtonTier.Quiet);
            ((Image)mid.targetGraphic).color = new Color(1f, 1f, 1f, 0f); // the frame above is the look; this is the hit area
            valueText = mid.GetComponentInChildren<Text>();
            valueText.alignment = TextAnchor.MiddleCenter;
            Place(valueText.gameObject, 4f, 0f, w - 2f * h - 8f, h);
            // The frame is what a focus shows on (the pad's frame, the press tint) — the value area itself is invisible.
            // It keeps its resting look: only a press tints it.
            mid.targetGraphic = frame;
            var tint = mid.colors;
            tint.normalColor = Color.white;
            mid.colors = tint;
            StepOnMove.Attach(mid, dir => valueText.text = step(dir));
            AddArrow(parent, x, y, h, "‹", () => valueText.text = step(-1));
            AddArrow(parent, x + w - h, y, h, "›", () => valueText.text = step(+1));
            return valueText;
        }

        /// <summary>
        /// The value of a − / + stepper as the stepper's ONE pad focus stop (the cycle's rule, <see cref="AddCycle"/>): an
        /// invisible button over the value — add the value's text after it — that shows the quiet frame only while focused
        /// or pressed, does nothing on A, and steps with Left (-1) / Right (+1) through <paramref name="step"/>
        /// (<see cref="StepOnMove"/>). Take the − and + buttons out of the pad's way with <see cref="NoPadStop"/>.
        /// </summary>
        public static Button AddStepperValue(Transform parent, float x, float y, float w, float h, System.Action<int> step)
        {
            var go = new GameObject("StepperValue", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var look = go.AddComponent<Image>();
            look.sprite = QuietSprite;
            look.type = Image.Type.Sliced;
            var stop = go.AddComponent<Button>();
            stop.targetGraphic = look;
            var tint = stop.colors;
            tint.normalColor = new Color(1f, 1f, 1f, 0f);
            tint.highlightedColor = new Color(1f, 1f, 1f, 0f); // no hover look: a click on the value does nothing
            tint.selectedColor = Color.white;
            tint.pressedColor = Cyan;
            stop.colors = tint;
            StepOnMove.Attach(stop, step);
            return stop;
        }

        /// <summary>Takes a control out of the pad's navigation — it stays a mouse and touch target (a stepper's − / +, a
        /// cycle's arrows).</summary>
        public static void NoPadStop(Selectable control)
        {
            if (control != null)
            {
                control.navigation = new Navigation { mode = Navigation.Mode.None };
            }
        }

        /// <summary>A cycle's ‹ / › button: the glyph centred and large, quiet, and no focus stop of its own — the value
        /// between the two takes Left / Right (<see cref="StepOnMove"/>).</summary>
        private static void AddArrow(Transform parent, float x, float y, float size, string glyph, System.Action onClick)
        {
            var arrow = AddButton(parent, x, y, size, size, glyph, onClick);
            StyleTier(arrow, ButtonTier.Quiet);
            NoPadStop(arrow);
            var text = arrow.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Cyan;
            Place(text.gameObject, 0f, -2f, size, size);
            text.fontSize = 28;
            text.resizeTextForBestFit = false;
        }

        /// <summary>
        /// An on/off switch (#3664): a pill with a knob, cyan when on. Returns the button; <paramref name="onChange"/> gets
        /// the new state. The pill is 60×32; its touch target reaches <see cref="MinTapSize"/> high (6 px above and below
        /// it — the caller leaves that clear).
        /// </summary>
        public static Button AddSwitch(Transform parent, float x, float y, bool on, System.Action<bool> onChange)
        {
            const float w = 60f, h = 32f, knob = 24f;
            var go = new GameObject("Switch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var track = go.AddComponent<Image>();
            track.sprite = PillSprite;
            track.type = Image.Type.Sliced;
            var dot = AddImage(go.transform, 4f, (h - knob) / 2f, knob, knob, DiscSprite, Color.white);
            bool[] state = { on };
            void Paint()
            {
                track.color = state[0] ? new Color(0.27f, 0.74f, 0.95f, 1f) : new Color(0.20f, 0.30f, 0.40f, 1f);
                dot.rectTransform.anchoredPosition = new Vector2(state[0] ? w - knob - 4f : 4f, -(h - knob) / 2f);
                dot.color = state[0] ? Color.white : new Color(0.55f, 0.64f, 0.72f);
            }

            var button = go.AddComponent<Button>();
            button.targetGraphic = track;
            go.AddComponent<UiHover>();
            button.onClick.AddListener(UiSound.Click);
            button.onClick.AddListener(() =>
            {
                state[0] = !state[0];
                Paint();
                onChange?.Invoke(state[0]);
            });
            Paint();
            AddHitArea(button, w, MinTapSize);
            return button;
        }

        /// <summary>
        /// "5 min ago" / "vor 5 Min." for a UTC moment (#3663): just now, minutes, hours, days — the keys
        /// <c>ui.time.ago_*</c> carry the number as <c>{0}</c>.
        /// </summary>
        public static string TimeAgo(System.Func<string, string> localize, System.DateTime utc)
        {
            var span = System.DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1.5)
            {
                return localize("ui.time.ago_now");
            }

            if (span.TotalHours < 1.5)
            {
                return localize("ui.time.ago_minutes").Replace("{0}", ((int)System.Math.Round(span.TotalMinutes)).ToString());
            }

            if (span.TotalDays < 1.5)
            {
                return localize("ui.time.ago_hours").Replace("{0}", ((int)System.Math.Round(span.TotalHours)).ToString());
            }

            return localize("ui.time.ago_days").Replace("{0}", ((int)System.Math.Round(span.TotalDays)).ToString());
        }

        /// <summary>
        /// Shortens <paramref name="text"/> at a word boundary until it fits <paramref name="width"/>×<paramref name="height"/>
        /// at <paramref name="size"/>, ending in "…" (#3663: the festival card used to stop mid-sentence without a sign that
        /// more follows). Returns the text unchanged when it fits. A text without spaces (Chinese, Japanese) is cut between
        /// characters. Cheap even for a whole release post: the text is first cut to a character budget the box can hold at
        /// most, then the cut point is found by binary search — a handful of measurements instead of one per word.
        /// </summary>
        public static string Ellipsize(string text, float width, float height, int size, FontStyle style = FontStyle.Normal)
        {
            if (string.IsNullOrEmpty(text) || MeasureHeight(text, width, size, style, false) <= height)
            {
                return text ?? string.Empty;
            }

            var cuts = EllipsizeCuts(text, width, height, size);
            int lo = 0, hi = cuts.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (MeasureHeight(EllipsizeAt(text, cuts[mid]), width, size, style, false) <= height)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return EllipsizeAt(text, cuts[best]);
        }

        /// <summary>The candidate cut lengths for <see cref="Ellipsize"/>, ascending: every word boundary inside the
        /// character budget (an upper bound of what the box can show), or every character when spaces are rare there —
        /// Chinese and Japanese have none between words, only now and then around a Latin name.</summary>
        private static List<int> EllipsizeCuts(string text, float width, float height, int size)
        {
            int budget = Mathf.Clamp((int)(width * height / (size * size * 0.4f)), 8, text.Length);
            var cuts = new List<int>();
            for (int i = 1; i < budget; i++)
            {
                if (text[i] == ' ')
                {
                    cuts.Add(i);
                }
            }

            if (cuts.Count < Mathf.Max(2, budget / 40))
            {
                cuts.Clear();
                for (int i = 1; i <= budget; i++)
                {
                    cuts.Add(i);
                }
            }

            return cuts;
        }

        private static string EllipsizeAt(string text, int length)
            => text.Substring(0, length).TrimEnd(',', ';', ':', '-', '–', ' ', '.', '、', '，', '。') + " …";

        private static Sprite _pillSprite;

        /// <summary>A fully rounded white pill (tint via <see cref="Image.color"/>) — the switch track.</summary>
        public static Sprite PillSprite => _pillSprite != null ? _pillSprite
            : _pillSprite = RoundedSprite(15, 0, Color.white, Color.white);

        /// <summary>
        /// The rounded-rect sprite every panel and button is cut from: <paramref name="fill"/> inside, a
        /// <paramref name="border"/>-pixel <paramref name="edge"/>, transparent outside the corner radius; nine-sliced.
        /// </summary>
        private static Sprite RoundedSprite(int radius, int border, Color fill, Color edge)
        {
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance into a rounded corner (0 along the straight edges).
                    float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                    float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * size + x] = dist > radius ? new Color(0f, 0f, 0f, 0f) : dist > radius - border ? edge : fill;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            int b = radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
    }
}
