// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Makes a menu navigable by gamepad. uGUI's <c>StandaloneInputModule</c> already turns the pad into
    /// directional navigation + Submit(A)/Cancel(B) via the project's InputManager axes, and buttons built by
    /// <see cref="UiKit.AddButton"/> default to <c>Navigation.Automatic</c> — so the ONE missing piece is that
    /// a mouse-built menu has nothing selected, leaving the stick with no cursor to move. This component fixes
    /// exactly that: while a gamepad is the active device and this menu has no valid selection, it selects the
    /// first interactable control in its own subtree. It is completely inert on keyboard/mouse (so it never
    /// steals the pointer), and self-healing (re-focuses if the selected control is hidden or destroyed).
    ///
    /// Attach with <see cref="UiNav.Enable"/> on a menu's root object — for a screen built on its own
    /// <see cref="UiKit.CreateCanvas"/> root that is the CANVAS object, not the owning MonoBehaviour's
    /// GameObject: <c>CreateCanvas</c> returns a scene-root GameObject that is never reparented, so a
    /// component sitting on the owner sees none of the screen's controls (#1198).
    ///
    /// Three details keep it from doing harm on screens that are built but not currently shown:
    /// <list type="bullet">
    /// <item>It respects <see cref="Canvas.enabled"/>. Screens hide themselves two different ways —
    /// <c>gameObject.SetActive(false)</c> (which disables this component too) and <c>canvas.enabled = false</c>
    /// (which does NOT: the GameObject stays active). Without the canvas check a hidden screen keeps pulling
    /// the selection away from whatever is actually on screen (#1198).</item>
    /// <item>It prefers not to auto-select a text field: a form's first control is usually a field, and
    /// landing there means the player's first stick flick edits text instead of moving. Since the on-screen
    /// keyboard (#1211) a field is no longer a TRAP — <see cref="PadTextEntryBridge"/> keeps it deactivated
    /// while a pad is in hand — so a screen made of nothing but fields now focuses its first one rather than
    /// being left with no selection at all.</item>
    /// <item>It remembers WHERE the selection was and restores that position when the controls are rebuilt.
    /// Data-driven panes (the crafting list rebuilds all three panes on every pick) would otherwise snap the
    /// focus back to control #1 on every input.</item>
    /// </list>
    ///
    /// On top of claiming the selection it does the three things a mouse pointer gives a menu for free and a
    /// pad does not — all only while a pad is the active device, so keyboard/mouse looks and sounds unchanged:
    /// <list type="bullet">
    /// <item><b>The selection is visible.</b> A cyan frame (<see cref="Outline"/>) sits on whatever is
    /// selected, and moving it plays the hover blip (#1410). The colour block on <see cref="UiKit.AddButton"/>
    /// only brightens a tint, which on a wall of inventory tiles reads as noise — and <see cref="UiHover"/> is
    /// pointer-enter only, so the stick used to move the cursor in silence.</item>
    /// <item><b>The selection stays on screen.</b> When it moves inside a <see cref="ScrollRect"/>, the pane
    /// scrolls the minimum amount that brings it fully into the viewport (#1407). uGUI does not do this by
    /// itself; the three 742 px crafting panes with content several times that height simply let the
    /// highlight walk out of sight.</item>
    /// <item><b>The controls are named.</b> A hint strip along the bottom edge says "(A) choose · (B) back",
    /// plus whatever the screen adds through <see cref="UiNav.AddHint"/> (#1408). Built through
    /// <see cref="InputMap.PadGlyph"/>, so it follows the player's glyph set and pad rebinds.</item>
    /// </list>
    ///
    /// Full coverage across every panel is tracked as follow-up (issue #195) and wants on-device validation —
    /// CI can't test a pad.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiNavFocus : MonoBehaviour
    {
        /// <summary>While true this menu neither claims nor restores the selection. Screens that hand the
        /// sticks to a 3D viewport (the ship editor's fly-cam, the face editor's paint canvas) suspend their
        /// panel navigation while the viewport has focus, so the pad drives exactly one thing at a time.</summary>
        public bool Suspended;

        /// <summary>Whether this screen shows the bottom hint strip. The two editors carry a hint line of
        /// their own in the same place (their viewport verbs change with Start), so they switch this off.</summary>
        public bool PadHints = true;

        private const float HintMargin = 10f;   // px from the bottom edge of the canvas
        private const float ScrollMargin = 8f;  // px of breathing room when a row is scrolled into view

        private Canvas _canvas;
        private int _lastIndex = -1;        // position of the last valid selection among the interactables
        private GameObject _lastSelected;   // what that position was read from, so Update can skip re-walking

        private readonly List<(PadButton[] Buttons, string VerbKey)> _extraHints = new();
        private GameObject _hintGo;
        private Text _hintText;
        private Outline _selectionFrame;

        private void Awake() => _canvas = GetComponentInParent<Canvas>();

        private void OnDisable()
        {
            ClearSelectionFrame();
            if (_hintGo != null)
            {
                _hintGo.SetActive(false);
            }
        }

        /// <summary>Whether this menu may claim the selection at all: not suspended, and not hidden behind a
        /// disabled <see cref="Canvas"/> (see the class summary — a canvas hidden that way keeps its
        /// GameObject active, so this component would otherwise keep running).</summary>
        public bool WantsFocus
        {
            get
            {
                if (_canvas == null)
                {
                    _canvas = GetComponentInParent<Canvas>(); // a canvas added after Awake still counts
                }

                return !Suspended && (_canvas == null || _canvas.enabled);
            }
        }

        /// <summary>The control this menu would focus right now, or null if it has none to offer. Split out of
        /// <see cref="Update"/> so the rule can be checked without a pad, an EventSystem or a running
        /// player loop — none of which CI has.</summary>
        public Selectable FocusTarget() => PreferredSelectable(Interactables());

        /// <summary>Records where <paramref name="current"/> sits among this menu's controls, so the same
        /// position can be restored after a rebuild. Ignores controls that belong to another menu.</summary>
        public void NoteSelection(GameObject current)
        {
            if (current == null)
            {
                return;
            }

            var all = Interactables();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject == current)
                {
                    _lastIndex = i;
                    return;
                }
            }
        }

        /// <summary>Adds a screen-specific line to the hint strip: the named buttons (joined with "/") and
        /// the verb behind <paramref name="verbKey"/> — e.g. LB/RB "switch tab" on the in-game menu.</summary>
        public void AddHint(string verbKey, params PadButton[] buttons)
        {
            _extraHints.Add((buttons, verbKey));
            if (_hintText != null)
            {
                _hintText.text = string.Empty; // force a recompose on the next refresh
            }
        }

        private void Update()
        {
            if (InputMap.ActiveDevice != InputDeviceKind.Gamepad || !WantsFocus)
            {
                // Keyboard/mouse in hand, or this screen is hidden — leave the selection alone, and take
                // the pad-only chrome down with us so a mouse user never sees a frame or a hint strip.
                ClearSelectionFrame();
                _lastSelected = null;
                if (_hintGo != null && _hintGo.activeSelf)
                {
                    _hintGo.SetActive(false);
                }

                return;
            }

            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            var current = es.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy)
            {
                var sel = current.GetComponent<Selectable>();
                if (sel != null && sel.IsInteractable())
                {
                    // Only walk the subtree when the selection actually MOVED. GetComponentsInChildren
                    // allocates, and a pad resting on one control must not cost an array per frame — the
                    // crafting pane alone holds hundreds of Selectables (WebGL / Raspberry-Pi budgets).
                    if (current != _lastSelected)
                    {
                        bool moved = _lastSelected != null; // null = first claim / after a rebuild: no blip
                        _lastSelected = current;
                        NoteSelection(current);
                        if (current.transform.IsChildOf(transform))
                        {
                            OnSelectionMoved(sel, moved);
                        }
                    }

                    RefreshHint(current);
                    return; // a valid control is focused — nothing to do.
                }
            }

            _lastSelected = null;
            ClearSelectionFrame();

            var restore = FocusTarget();
            if (restore != null)
            {
                es.SetSelectedGameObject(restore.gameObject);
            }

            RefreshHint(null);
        }

        // ---- selection chrome (#1407, #1410) --------------------------------------------------------------

        private void OnSelectionMoved(Selectable sel, bool playSound)
        {
            ClearSelectionFrame();
            var graphic = sel.targetGraphic != null ? sel.targetGraphic : sel.GetComponent<Graphic>();
            if (graphic != null)
            {
                _selectionFrame = graphic.gameObject.AddComponent<Outline>();
                _selectionFrame.effectColor = UiKit.Cyan;
                _selectionFrame.effectDistance = new Vector2(3f, -3f);
                _selectionFrame.useGraphicAlpha = true;
            }

            if (playSound)
            {
                UiSound.Hover();
            }

            ScrollIntoView(sel.transform as RectTransform);
        }

        private void ClearSelectionFrame()
        {
            if (_selectionFrame != null)
            {
                Destroy(_selectionFrame);
                _selectionFrame = null;
            }
        }

        /// <summary>Scrolls the nearest vertical <see cref="ScrollRect"/> just far enough that
        /// <paramref name="target"/> is fully inside its viewport. No-op when the target is not scrollable
        /// content (a tab button, the inline scrollbar itself) or the page already fits.</summary>
        private static void ScrollIntoView(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            var scroll = target.GetComponentInParent<ScrollRect>();
            if (scroll == null || !scroll.vertical || scroll.content == null || !target.IsChildOf(scroll.content))
            {
                return;
            }

            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float range = scroll.content.rect.height - viewport.rect.height;
            if (range <= 0.5f)
            {
                return; // the whole page fits — nothing can be out of view
            }

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                float y = viewport.InverseTransformPoint(corners[i]).y;
                top = Mathf.Max(top, y);
                bottom = Mathf.Min(bottom, y);
            }

            var view = viewport.rect;
            float delta = ScrollDelta(top, bottom, view.yMax, view.yMin, ScrollMargin);
            if (Mathf.Abs(delta) < 0.5f)
            {
                return;
            }

            // Normalized position is pivot-agnostic (1 = top, 0 = bottom), so the same maths serves the
            // hand-built crafting panes and UiKit.ScrollList alike. Scrolling DOWN lowers it.
            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - delta / range);
        }

        /// <summary>How far a pane must scroll so a row spanning <paramref name="selTop"/>..
        /// <paramref name="selBottom"/> sits inside the viewport <paramref name="viewTop"/>..
        /// <paramref name="viewBottom"/> (all in one vertical space, y up): positive = scroll DOWN by that
        /// many pixels, negative = scroll up, 0 = already visible. A row taller than the viewport aligns
        /// its top. Pure so CI can pin it without a canvas.</summary>
        public static float ScrollDelta(float selTop, float selBottom, float viewTop, float viewBottom, float margin)
        {
            if (selTop > viewTop)
            {
                return -(selTop - viewTop + margin); // above the visible band: scroll up
            }

            if (selBottom < viewBottom)
            {
                return viewBottom - selBottom + margin; // below it: scroll down
            }

            return 0f;
        }

        // ---- hint strip (#1408) ----------------------------------------------------------------------------

        private void RefreshHint(GameObject current)
        {
            if (!PadHints)
            {
                return;
            }

            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
                if (_canvas == null)
                {
                    return; // nothing full-screen to hang a strip on
                }
            }

            if (_hintGo == null)
            {
                BuildHint();
            }

            bool onField = current != null && current.GetComponent<InputField>() != null;
            string text = ComposeHint(onField, _extraHints);
            if (_hintText.text != text)
            {
                _hintText.text = text;
            }

            if (!_hintGo.activeSelf)
            {
                _hintGo.SetActive(true);
            }

            // Screens rebuild by clearing the canvas' children and re-adding; keep the strip on top of
            // whatever was added after it.
            if (_hintGo.transform.GetSiblingIndex() != _canvas.transform.childCount - 1)
            {
                _hintGo.transform.SetAsLastSibling();
            }
        }

        private void BuildHint()
        {
            _hintGo = new GameObject("PadHints", typeof(RectTransform));
            var rt = (RectTransform)_hintGo.transform;
            rt.SetParent(_canvas.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, HintMargin);
            rt.sizeDelta = new Vector2(1600f, 30f);

            _hintText = _hintGo.AddComponent<Text>();
            _hintText.font = UiKit.Font;
            _hintText.fontSize = 17;
            _hintText.fontStyle = FontStyle.Bold;
            _hintText.color = UiKit.TextCol;
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hintText.raycastTarget = false;
            UiKit.AddOutline(_hintText);
        }

        /// <summary>The strip's text: A (Submit is hard-wired to the bottom face button in the InputManager,
        /// so its glyph is fixed) as "choose" — or "type" while a text field is selected — then the bound
        /// Cancel button as "back", then the screen's extra lines. Verbs come from <c>ui.pad.*</c> via
        /// <see cref="UiKit.L"/>; the glyphs follow the player's glyph set (#1219). Static so CI can pin
        /// the wording without a pad.</summary>
        public static string ComposeHint(bool onTextField, IReadOnlyList<(PadButton[] Buttons, string VerbKey)> extras)
        {
            var sb = new StringBuilder(96);
            Append(sb, InputMap.PadGlyph(KeyCode.JoystickButton0), onTextField ? "ui.pad.type" : "ui.pad.choose");
            Append(sb, InputMap.PadGlyph(GamepadInputSource.ButtonFor(InputAction.UiCancel)), "ui.pad.back");
            if (extras != null)
            {
                for (int i = 0; i < extras.Count; i++)
                {
                    var (buttons, verbKey) = extras[i];
                    string glyph = string.Empty;
                    for (int b = 0; b < buttons.Length; b++)
                    {
                        string g = InputMap.PadGlyph(GamepadInputSource.CodeOf(buttons[b]));
                        if (g != null)
                        {
                            glyph = glyph.Length == 0 ? g : glyph + "/" + g;
                        }
                    }

                    Append(sb, glyph, verbKey);
                }
            }

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string glyph, string verbKey)
        {
            if (string.IsNullOrEmpty(glyph))
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append("   ·   ");
            }

            sb.Append(glyph).Append(' ').Append(UiKit.L(verbKey));
        }

        // ---- focus rule ------------------------------------------------------------------------------------

        /// <summary>The control to focus: the one sitting where the selection was before the panel was
        /// rebuilt (clamped — a filtered list can get shorter), else the first control that is not a text
        /// field, and only failing that a field (see the class summary).</summary>
        private Selectable PreferredSelectable(Selectable[] all)
        {
            if (all.Length == 0)
            {
                return null;
            }

            if (_lastIndex >= 0)
            {
                var remembered = all[Mathf.Clamp(_lastIndex, 0, all.Length - 1)];
                if (!IsTextField(remembered))
                {
                    return remembered;
                }
            }

            for (int i = 0; i < all.Length; i++)
            {
                if (!IsTextField(all[i]))
                {
                    return all[i];
                }
            }

            return all[0]; // nothing but fields: focus one anyway — with the on-screen keyboard it is usable
        }

        /// <summary>This menu's currently interactable controls, in hierarchy order. Controls that opted out
        /// of navigation (the scrollbars, #1411) are not offered either — the stick could never reach them.</summary>
        private Selectable[] Interactables()
        {
            var found = GetComponentsInChildren<Selectable>(includeInactive: false);
            int n = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].IsInteractable() && found[i].gameObject.activeInHierarchy
                    && found[i].navigation.mode != Navigation.Mode.None)
                {
                    found[n++] = found[i];
                }
            }

            if (n == found.Length)
            {
                return found;
            }

            var kept = new Selectable[n];
            System.Array.Copy(found, kept, n);
            return kept;
        }

        /// <summary>True for controls that swallow the navigation axes once focused (see the class summary).</summary>
        private static bool IsTextField(Selectable s) => s is InputField;
    }

    /// <summary>Helpers for wiring gamepad menu navigation onto a menu root.</summary>
    public static class UiNav
    {
        /// <summary>Ensures <paramref name="menuRoot"/> auto-focuses its first control for a gamepad. Idempotent.
        /// <paramref name="padHints"/> false suppresses the bottom hint strip for screens that draw their own
        /// (the ship / face editors).</summary>
        public static void Enable(GameObject menuRoot, bool padHints = true)
        {
            if (menuRoot == null)
            {
                return;
            }

            var nav = menuRoot.GetComponent<UiNavFocus>();
            if (nav == null)
            {
                nav = menuRoot.AddComponent<UiNavFocus>();
            }

            nav.PadHints = padHints;
        }

        /// <summary>Adds a screen-specific line to <paramref name="menuRoot"/>'s hint strip — see
        /// <see cref="UiNavFocus.AddHint"/>. No-op if the root carries no <see cref="UiNavFocus"/>.</summary>
        public static void AddHint(GameObject menuRoot, string verbKey, params PadButton[] buttons)
        {
            var nav = menuRoot != null ? menuRoot.GetComponent<UiNavFocus>() : null;
            nav?.AddHint(verbKey, buttons);
        }

        /// <summary>Hands the pad to (or takes it back from) a screen's 3D viewport — see
        /// <see cref="UiNavFocus.Suspended"/>. Clears the selection on suspend, so the panel's highlight does
        /// not sit there looking focused while the sticks drive the world. No-op if the root carries no
        /// <see cref="UiNavFocus"/>.</summary>
        public static void SetSuspended(GameObject menuRoot, bool suspended)
        {
            var nav = menuRoot != null ? menuRoot.GetComponent<UiNavFocus>() : null;
            if (nav == null || nav.Suspended == suspended)
            {
                return;
            }

            nav.Suspended = suspended;
            if (suspended && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
