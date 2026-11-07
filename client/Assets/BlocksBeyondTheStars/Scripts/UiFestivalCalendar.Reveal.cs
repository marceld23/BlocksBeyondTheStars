// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The calendar's reveal (#3316): the door just opened pops, a small panel over the grid shows what it held — each item
    /// with its icon and count, a cosmetic with the line that says where to try it — and the cosy "door opens" sound plays.
    /// An already open door shows the same panel quietly when it is clicked again.
    /// </summary>
    public sealed partial class UiFestivalCalendar
    {
        /// <summary>The bundled "calendar door opens" clip (tools/ai-assets, #3316); the plain success chime without it.</summary>
        public const string DoorSound = "calendar_door_open";

        private const float RevealW = 720f, RevealH = 420f, RevealRowH = 64f;

        /// <summary>Shows what <paramref name="door"/> held; <paramref name="celebrate"/> pops the door and plays the sound.</summary>
        private void ShowReveal(FestivalCalendarState state, int door, bool celebrate)
        {
            CloseReveal();
            if (_panel == null)
            {
                return;
            }

            if (_gridGroup != null)
            {
                _gridGroup.interactable = false; // the pad and the clicks belong to the panel until "Great!"
            }

            var look = CalendarLook.For(_game);
            var panel = UiKit.AddDialogPanel(_panel, (DlgW - RevealW) / 2f, 210f, RevealW, RevealH);
            _reveal = panel.gameObject;
            string head = L("ui.calendar.door").Replace("{n}", door.ToString(System.Globalization.CultureInfo.InvariantCulture));
            UiKit.AddText(panel, 24f, 18f, RevealW - 48f, 44f, head, 30, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            AddRevealRows(panel, FestivalCalendarView.ContentOf(state, door), look);

            var ok = UiKit.AddButton(panel, RevealW / 2f - 140f, RevealH - 74f, 280f, 54f, L("ui.calendar.reveal_ok"), () => CloseReveal());
            ok.GetComponent<Image>().color = new Color(0.85f, 0.7f, 0.35f);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(ok.gameObject); // the pad's A says "Great!" at once
            }

            if (celebrate)
            {
                Pop(panel as RectTransform, 0.6f, 0.32f);
                PopDoor(door);
                var audio = ClientAudio.Instance;
                if (audio != null)
                {
                    audio.Cue(audio.Has(DoorSound) ? DoorSound : "ui_success", 0.9f);
                }
            }
        }

        /// <summary>One row per item (icon, "3 × Iron ore"), then the cosmetic's unlock line.</summary>
        private void AddRevealRows(Transform panel, CalendarDoorContent content, CalendarLook look)
        {
            float y = 82f;
            int items = content?.Items?.Length ?? 0;
            for (int i = 0; i < items && i < 3; i++)
            {
                string item = content.Items[i];
                int count = content.Counts != null && i < content.Counts.Length ? content.Counts[i] : 1;
                AddItemIcon(panel, 120f, y + 6f, 52f, item, 1, look);
                string line = count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " × " + look.ItemName(item);
                var text = UiKit.AddText(panel, 190f, y, RevealW - 230f, RevealRowH, line, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.FitLabel(text, 14, 24);
                y += RevealRowH;
            }

            if (content != null && !string.IsNullOrEmpty(content.Cosmetic))
            {
                UiKit.AddText(panel, 112f, y + 4f, 64f, 64f, CosmeticSymbol, 44, Gold, TextAnchor.MiddleCenter);
                var unlock = UiKit.AddText(panel, 190f, y, RevealW - 230f, 92f, UnlockLine(content.Cosmetic, look), 20, Gold,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                unlock.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.FitLabel(unlock, 12, 20);
            }
            else if (items == 0)
            {
                UiKit.AddText(panel, 24f, y, RevealW - 48f, RevealRowH, "✓", 40, Gold, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>"Unlocked: Christmas lights for your HUD frame — try it in the character editor!" — where the template waits.</summary>
        public static string UnlockLine(string cosmetic, CalendarLook look)
        {
            look ??= new CalendarLook();
            cosmetic ??= string.Empty;
            string key = cosmetic.StartsWith(CosmeticKeys.HudFramePrefix, System.StringComparison.Ordinal) ? "ui.calendar.unlocked_hudframe"
                : cosmetic.StartsWith(CosmeticKeys.SkinPrefix, System.StringComparison.Ordinal) ? "ui.calendar.unlocked_skin"
                : "ui.calendar.unlocked";
            return look.L(key).Replace("{name}", look.CosmeticName(cosmetic));
        }

        /// <summary>Closes the reveal panel and hands the grid back. True when one was open.</summary>
        private bool CloseReveal()
        {
            if (_reveal == null)
            {
                return false;
            }

            Destroy(_reveal);
            _reveal = null;
            if (_gridGroup != null)
            {
                _gridGroup.interactable = true;
            }

            return true;
        }

        /// <summary>The opened door pops on the grid behind the panel.</summary>
        private void PopDoor(int door)
        {
            foreach (var view in _doors)
            {
                if (view != null && view.Door == door && view.Button != null)
                {
                    Pop(view.Button.transform as RectTransform, 1.25f, 0.4f);
                    return;
                }
            }
        }

        /// <summary>Scales <paramref name="rect"/> from <paramref name="from"/> to its size with a soft overshoot — around its
        /// centre. Nothing under reduced motion.</summary>
        private static void Pop(RectTransform rect, float from, float seconds)
        {
            if (rect == null || UiKit.ReducedMotion)
            {
                return;
            }

            var size = rect.sizeDelta;
            rect.pivot = new Vector2(0.5f, 0.5f); // UiKit.Place pins the top-left corner; pop around the middle instead
            rect.anchoredPosition += new Vector2(size.x * 0.5f, -size.y * 0.5f);
            rect.gameObject.AddComponent<UiPop>().Init(from, seconds);
        }

        private sealed class UiPop : MonoBehaviour
        {
            private float _from, _seconds, _t;

            public void Init(float from, float seconds)
            {
                _from = from;
                _seconds = Mathf.Max(0.05f, seconds);
                transform.localScale = Vector3.one * from;
            }

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_t / _seconds);
                float back = 1f + 2.70158f * Mathf.Pow(k - 1f, 3f) + 1.70158f * Mathf.Pow(k - 1f, 2f); // ease-out-back
                transform.localScale = Vector3.one * Mathf.LerpUnclamped(_from, 1f, back);
                if (k >= 1f)
                {
                    transform.localScale = Vector3.one;
                    Destroy(this);
                }
            }
        }
    }
}
