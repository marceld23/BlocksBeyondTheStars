// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One door of the calendar grid as built (#3316) — what the reveal pops and the EditMode tests read.</summary>
    public sealed class CalendarDoorView
    {
        public int Door;
        public CalendarDoorStatus Status;
        public Button Button;

        /// <summary>The big number on a closed door ("…" while its answer is on the way); empty on an open door.</summary>
        public string Number = string.Empty;

        /// <summary>The short names of an open door's contents ("3 × Iron ore · Christmas lights"); empty on a closed door.</summary>
        public string Summary = string.Empty;
    }

    /// <summary>How the calendar names and draws things — the game's localizer, item names and icons, the cosmetic names.
    /// A plain set of functions, so the door grid builds in an EditMode test without a running game.</summary>
    public sealed class CalendarLook
    {
        public System.Func<string, string> L = key => key;
        public System.Func<string, string> ItemName = key => key;
        public System.Func<string, Sprite> ItemIcon = _ => null;
        public System.Func<string, Color> ItemTint = _ => Color.white;
        public System.Func<string, string> CosmeticName = key => key;

        /// <summary>The look of a running game.</summary>
        public static CalendarLook For(GameBootstrap game)
        {
            var look = new CalendarLook();
            if (game == null || game.Localizer == null)
            {
                return look;
            }

            var loc = game.Localizer;
            look.L = loc.Get;
            look.ItemName = item => BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(loc, item);
            look.ItemIcon = item => IconResolver.Resolve(item, game);
            look.ItemTint = item => IconResolver.Tint(item, game);
            look.CosmeticName = key => CosmeticNameOf(key, game.Content, loc.Get, loc.Has);
            return look;
        }

        /// <summary>A cosmetic's own name (<c>hudframe:christmas_lights</c> → "Christmas lights"), else its kind ("HUD frame").</summary>
        public static string CosmeticNameOf(string key, BlocksBeyondTheStars.Shared.Content.GameContent content,
            System.Func<string, string> l, System.Func<string, bool> has)
        {
            key ??= string.Empty;
            string nameKey = null, kindKey = "ui.calendar.cosmetic.other";
            if (key.StartsWith(CosmeticKeys.HudFramePrefix, System.StringComparison.Ordinal))
            {
                nameKey = content?.FindHudFrame(key.Substring(CosmeticKeys.HudFramePrefix.Length))?.NameKey;
                kindKey = "ui.calendar.cosmetic.hudframe";
            }
            else if (key.StartsWith(CosmeticKeys.SkinPrefix, System.StringComparison.Ordinal))
            {
                nameKey = content?.FindSkin(key.Substring(CosmeticKeys.SkinPrefix.Length))?.NameKey;
                kindKey = "ui.calendar.cosmetic.skin";
            }

            return !string.IsNullOrEmpty(nameKey) && has(nameKey) ? l(nameKey) : l(kindKey);
        }
    }

    public sealed partial class UiFestivalCalendar
    {
        private const float DoorGap = 12f;
        private const string CosmeticSymbol = "✦";

        private static readonly Color DoorNumber = new Color(1f, 0.95f, 0.82f);
        private static readonly Color DoorNumberDim = new Color(0.55f, 0.6f, 0.68f);
        private static readonly Color OpenFill = new Color(0.07f, 0.11f, 0.19f);

        // The closed doors' festive colours — deep green, blue, plum, teal and copper; never the enemies' red.
        private static readonly Color[] DoorColours =
        {
            new Color(0.16f, 0.42f, 0.26f),
            new Color(0.16f, 0.27f, 0.50f),
            new Color(0.40f, 0.20f, 0.42f),
            new Color(0.12f, 0.40f, 0.44f),
            new Color(0.50f, 0.30f, 0.15f),
        };

        /// <summary>Builds the doors of <paramref name="state"/> in <paramref name="order"/> (row by row) into the rect — each a
        /// button calling <paramref name="onDoor"/> with its number. Public for the EditMode tests.</summary>
        public static CalendarDoorView[] BuildGrid(Transform parent, float x, float y, float w, float h, FestivalCalendarState state,
            int[] order, CalendarLook look, System.Action<int> onDoor, int pendingDoor = 0)
        {
            order ??= System.Array.Empty<int>();
            look ??= new CalendarLook();
            int cols = FestivalCalendarView.Columns(order.Length);
            int rows = Mathf.Max(1, FestivalCalendarView.Rows(order.Length));
            float cw = (w - DoorGap * (cols - 1)) / cols;
            float ch = (h - DoorGap * (rows - 1)) / rows;
            var views = new CalendarDoorView[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                float dx = x + (i % cols) * (cw + DoorGap);
                float dy = y + (i / cols) * (ch + DoorGap);
                views[i] = BuildDoor(parent, dx, dy, cw, ch, state, order[i], look, onDoor, order[i] == pendingDoor);
            }

            return views;
        }

        private static CalendarDoorView BuildDoor(Transform parent, float x, float y, float w, float h, FestivalCalendarState state,
            int door, CalendarLook look, System.Action<int> onDoor, bool pending)
        {
            var status = FestivalCalendarView.Status(state, door);
            var view = new CalendarDoorView { Door = door, Status = status };
            if (status == CalendarDoorStatus.Available)
            {
                // A soft gold rim behind the door: the doors that may be opened glow (the button covers all but the edge).
                var rim = UiKit.AddImage(parent, x - 3f, y - 3f, w + 6f, h + 6f, UiKit.ButtonSprite, Gold, Image.Type.Sliced);
                rim.raycastTarget = false;
                rim.gameObject.AddComponent<DoorGlow>().Init(Gold, 0.35f, 0.9f);
            }

            view.Button = UiKit.AddButton(parent, x, y, w, h, string.Empty, () => onDoor?.Invoke(door));
            view.Button.gameObject.name = "Door" + door;
            var img = view.Button.GetComponent<Image>();
            if (status == CalendarDoorStatus.Open)
            {
                img.color = OpenFill;
                view.Summary = AddOpenFace(view.Button.transform, w, h, door, state, look);
                return view;
            }

            var colour = DoorColours[(door * 7) % DoorColours.Length];
            img.color = status == CalendarDoorStatus.Later ? new Color(colour.r * 0.45f, colour.g * 0.45f, colour.b * 0.45f) : colour;
            view.Number = pending ? "…" : door.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AddClosedFace(view.Button.transform, w, h, view.Number, status);
            if (status == CalendarDoorStatus.Available && !pending)
            {
                img.gameObject.AddComponent<DoorGlow>().Init(colour, 0f, 0.22f); // the gentle pulse
            }

            return view;
        }

        /// <summary>A closed door: its big number, a little star on the waiting ones.</summary>
        private static void AddClosedFace(Transform door, float w, float h, string number, CalendarDoorStatus status)
        {
            bool later = status == CalendarDoorStatus.Later;
            var label = UiKit.AddText(door, 0f, 0f, w, h, number, Mathf.RoundToInt(Mathf.Min(52f, h * 0.42f)),
                later ? DoorNumberDim : DoorNumber, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddOutline(label);
            if (!later)
            {
                UiKit.AddText(door, w - 34f, 6f, 28f, 28f, "★", 20, Gold, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>An open door: its small number, the icons of what it held (items, a cosmetic's symbol) and their short
        /// names. Returns the names line.</summary>
        private static string AddOpenFace(Transform door, float w, float h, int number, FestivalCalendarState state, CalendarLook look)
        {
            UiKit.AddText(door, 8f, 4f, 48f, 24f, number.ToString(System.Globalization.CultureInfo.InvariantCulture), 16, Gold,
                TextAnchor.UpperLeft, FontStyle.Bold);
            var content = FestivalCalendarView.ContentOf(state, number);
            var names = new List<string>(3);
            int symbols = content == null ? 0 : Mathf.Min(2, content.Items?.Length ?? 0) + (string.IsNullOrEmpty(content.Cosmetic) ? 0 : 1);
            float size = Mathf.Min(48f, h * 0.36f);
            float x = (w - symbols * size - (symbols - 1) * 10f) / 2f;
            float y = Mathf.Max(26f, h * 0.22f);
            for (int i = 0; content != null && i < Mathf.Min(2, content.Items?.Length ?? 0); i++)
            {
                string item = content.Items[i];
                int count = content.Counts != null && i < content.Counts.Length ? content.Counts[i] : 1;
                AddItemIcon(door, x, y, size, item, count, look);
                names.Add(look.ItemName(item));
                x += size + 10f;
            }

            if (content != null && !string.IsNullOrEmpty(content.Cosmetic))
            {
                UiKit.AddText(door, x, y, size, size, CosmeticSymbol, Mathf.RoundToInt(size * 0.75f), Gold, TextAnchor.MiddleCenter);
                names.Add(look.CosmeticName(content.Cosmetic));
            }

            string summary = symbols == 0 ? "✓" : string.Join(" · ", names);
            var line = UiKit.AddText(door, 6f, h - 46f, w - 12f, 42f, summary, 14, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.FitLabel(line, 9, 14);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            return summary;
        }

        /// <summary>An item's icon (its generated icon or block tile, else the components symbol) with its count.</summary>
        private static void AddItemIcon(Transform parent, float x, float y, float size, string item, int count, CalendarLook look)
        {
            var sprite = look.ItemIcon(item);
            if (sprite != null)
            {
                UiKit.AddIconSprite(parent, x, y, size, sprite, look.ItemTint(item));
            }
            else
            {
                UiKit.AddIcon(parent, x, y, size, "cat_components");
            }

            if (count > 1)
            {
                var n = UiKit.AddText(parent, x + size * 0.35f, y + size * 0.6f, size * 0.75f, size * 0.45f,
                    "×" + count.ToString(System.Globalization.CultureInfo.InvariantCulture), 14, Color.white, TextAnchor.LowerRight, FontStyle.Bold);
                UiKit.AddOutline(n);
            }
        }

        /// <summary>A gentle pulse of an image's colour toward gold — the waiting doors' glow. Still under reduced motion.</summary>
        private sealed class DoorGlow : MonoBehaviour
        {
            private Image _img;
            private Color _base;
            private float _min, _max;

            public void Init(Color baseColour, float min, float max)
            {
                _img = GetComponent<Image>();
                _base = baseColour;
                _min = min;
                _max = max;
                Apply(UiKit.ReducedMotion ? (min + max) * 0.5f : min);
            }

            private void Update()
            {
                if (!UiKit.ReducedMotion)
                {
                    Apply(Mathf.Lerp(_min, _max, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f)));
                }
            }

            private void Apply(float k)
            {
                if (_img != null)
                {
                    var c = Color.Lerp(_base, Gold, k);
                    c.a = Mathf.Lerp(0.55f, 1f, k);
                    _img.color = _base == Gold ? new Color(Gold.r, Gold.g, Gold.b, c.a) : new Color(c.r, c.g, c.b, 1f);
                }
            }
        }
    }
}
