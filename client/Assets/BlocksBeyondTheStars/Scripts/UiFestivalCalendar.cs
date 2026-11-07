// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The festival day calendar window (#3316, the Advent calendar of #3315): every door in the calendar's grid
    /// (<see cref="FestivalCalendarDefinition.Layout"/>, 6 × 4 for 24) — later doors closed and dim, the doors that may be
    /// opened gently glowing, opened doors showing what they held. A click on a waiting door asks the server
    /// (<see cref="NetworkClient.SendOpenCalendarDoor"/>); the state it answers with names the door to reveal, which pops open
    /// with a small panel of its contents and a cosy sound. The calendar <b>waits</b>: the header counts the doors waiting
    /// and says until when missed doors stay — it never hurries the player.
    /// <para>
    /// Its own top-level canvas above the Tab menu (order 61, the slot picker's tier), built in one go so the modal is the
    /// last thing on it (sibling order = draw + click order, #2926/#3017). Opened from the Character tab; holds a lone
    /// player's world while it is up (the server lets the door intent through the singleplayer pause), frees the cursor,
    /// takes the pad from the menu behind it and closes with its button, Esc or pad B — and by itself when the menu behind
    /// it closes, the calendar ends or the world goes.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-45)] // before GameMenu: Esc / pad B closes the calendar first, not the menu behind it
    public sealed partial class UiFestivalCalendar : MonoBehaviour
    {
        /// <summary>The world-hold name of the calendar (<see cref="WorldHoldArbiter"/>).</summary>
        public const string HoldName = "calendar";

        private const float W = 1920f, H = 1080f;
        private const float DlgW = 1280f, DlgH = 900f;
        private const float GridX = 40f, GridY = 132f, GridW = 1200f, GridH = 610f;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.45f);

        private static UiFestivalCalendar _current;

        private GameBootstrap _game;
        private string _calendar;
        private Canvas _canvas;
        private Canvas _behind;
        private GameObject _overlay;
        private Transform _panel;
        private CanvasGroup _gridGroup;
        private Text _message;
        private CalendarDoorView[] _doors = System.Array.Empty<CalendarDoorView>();
        private GameObject _reveal;
        private int _pendingDoor;
        private float _pendingSince;
        private bool _closing;

        /// <summary>True while a calendar window is up.</summary>
        public static bool IsOpen => _current != null;

        /// <summary>Opens the window for calendar <paramref name="calendar"/> over <paramref name="behind"/> (the Tab menu's
        /// canvas — its pad navigation pauses while the window is up, and the window closes when that canvas does). Replaces
        /// an open one.</summary>
        public static UiFestivalCalendar Open(GameBootstrap game, string calendar, Canvas behind)
        {
            CloseCurrent();
            if (game == null || game.Network == null || string.IsNullOrEmpty(calendar))
            {
                return null;
            }

            var canvas = UiKit.CreateCanvas("Festival Calendar");
            canvas.sortingOrder = 61; // above the Tab menu (50) — the slot picker's tier
            var window = canvas.gameObject.AddComponent<UiFestivalCalendar>();
            window.Init(game, calendar, canvas, behind);
            return window;
        }

        /// <summary>Closes the open window, if any. True when one was open.</summary>
        public static bool CloseCurrent()
        {
            if (_current == null)
            {
                return false;
            }

            _current.Close();
            return true;
        }

        private void Init(GameBootstrap game, string calendar, Canvas canvas, Canvas behind)
        {
            _current = this;
            _game = game;
            _calendar = calendar;
            _canvas = canvas;
            _behind = behind;
            if (_behind != null)
            {
                UiNav.SetSuspended(_behind.gameObject, true); // the pad belongs to the calendar until it closes
            }

            UiNav.Enable(canvas.gameObject);
            game.SetMenuOwner(this, true);
            game.WorldHold.Hold(HoldName, Time.realtimeSinceStartup);
            game.Network.CalendarStateReceived += OnState;
            Rebuild(opening: true);
        }

        private FestivalCalendarState State
            => _game != null && _game.Network != null && _game.Network.Calendars.TryGetValue(_calendar, out var s) ? s : null;

        private string L(string key) => _game != null && _game.Localizer != null ? _game.Localizer.Get(key) : key;

        private void Update()
        {
            if (_closing)
            {
                return;
            }

            // The window goes with what it stands on: the world, the menu behind it, the calendar itself.
            var state = State;
            if (_game == null || _game.Network == null || state == null || !state.Active
                || (_behind != null && (!_behind.enabled || !_behind.gameObject.activeInHierarchy)))
            {
                Close();
                return;
            }

            if (InputMap.Down(InputAction.UiCancel) && !_game.ChatTyping && !UiKit.TextFieldFocused()
                && !_game.MenuInputHandledThisFrame)
            {
                _game.MarkMenuInputHandled(); // the menu behind stays open
                if (!CloseReveal())
                {
                    Close();
                }

                return;
            }

            if (_pendingDoor > 0 && Time.unscaledTime - _pendingSince > 6f)
            {
                _pendingDoor = 0; // no answer (an older server) — the door may be tried again
                Rebuild(opening: false);
            }
        }

        /// <summary>A state of this calendar arrived: rebuild, then reveal the door it names and show its notice.</summary>
        private void OnState(FestivalCalendarState state)
        {
            if (_closing || state == null || state.Calendar != _calendar)
            {
                return;
            }

            _pendingDoor = 0;
            if (!state.Active)
            {
                Close();
                return;
            }

            if (state.Reveal > 0)
            {
                ShowMessage(string.Empty, Gold); // the door opened — an older hint has had its turn
            }

            Rebuild(opening: false);
            if (!string.IsNullOrEmpty(state.Notice))
            {
                ShowMessage(L(state.Notice), Gold);
            }

            if (state.Reveal > 0)
            {
                ShowReveal(state, state.Reveal, celebrate: true);
            }
        }

        /// <summary>Closes the window: releases the hold, the cursor and the pad, and destroys the canvas.</summary>
        public void Close()
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            Release();
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void OnDestroy() => Release();

        /// <summary>Lets go of everything the window took — every close path ends here (idempotent).</summary>
        private void Release()
        {
            if (_current == this)
            {
                _current = null;
            }

            if (_game != null)
            {
                if (_game.Network != null)
                {
                    _game.Network.CalendarStateReceived -= OnState;
                }

                _game.WorldHold.Release(HoldName);
                _game.SetMenuOwner(this, false);
            }

            if (_behind != null)
            {
                UiNav.SetSuspended(_behind.gameObject, false);
                _behind = null;
            }

            _game = null;
        }

        /// <summary>Builds (or rebuilds) the whole modal from the latest state: header, door grid, message line, close button.
        /// <paramref name="opening"/> plays the modal's open effect — only on the first build.</summary>
        private void Rebuild(bool opening)
        {
            var state = State;
            if (state == null)
            {
                return;
            }

            int selectedDoor = SelectedDoor();
            string message = _message != null ? _message.text : string.Empty;
            Color messageColour = _message != null ? _message.color : Gold;
            if (_overlay != null)
            {
                Destroy(_overlay);
            }

            _reveal = null;
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (W - DlgW) / 2f, (H - DlgH) / 2f, DlgW, DlgH);
            _overlay = overlay;
            _panel = panel;
            if (opening)
            {
                UiKit.OpenModal(overlay);
            }

            var def = _game.Content?.FindFestivalCalendar(_calendar);
            AddHeader(state, def);
            AddGrid(state, def);
            _message = UiKit.AddText(panel, 60f, 752f, DlgW - 120f, 56f, message, 18, messageColour, TextAnchor.MiddleCenter, FontStyle.Bold);
            _message.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel, DlgW / 2f - 160f, DlgH - 74f, 320f, 54f, L("ui.action.close"), Close, "btn_exit");
            Reselect(selectedDoor);
        }

        /// <summary>The calendar's name, how many doors wait and until when missed doors stay — never a countdown.</summary>
        private void AddHeader(FestivalCalendarState state, FestivalCalendarDefinition def)
        {
            var festival = _game.Content?.FindFestival(state.Festival);
            string name = L(def != null ? def.NameKey : "calendar." + _calendar + ".name");
            string title = festival != null && !string.IsNullOrEmpty(festival.Glyph) ? festival.Glyph + " " + name : name;
            var head = UiKit.AddText(_panel, 30f, 18f, DlgW - 60f, 44f, title, 30, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabel(head, 18, 30);

            var waiting = UiKit.AddText(_panel, 40f, 64f, DlgW - 80f, 30f, WaitingLine(state), 20, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.FitLabel(waiting, 12, 20);
            string until = CatchUpLine(state);
            if (until.Length > 0)
            {
                var line = UiKit.AddText(_panel, 40f, 96f, DlgW - 80f, 26f, until, 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
                UiKit.FitLabel(line, 10, 16);
            }
        }

        /// <summary>"3 doors are waiting for you" / "The next door comes tomorrow." / "You opened every door!"</summary>
        private string WaitingLine(FestivalCalendarState state)
        {
            int waiting = FestivalCalendarView.Waiting(state);
            if (waiting == 1)
            {
                return L("ui.calendar.waiting_one");
            }

            if (waiting > 1)
            {
                return L("ui.calendar.waiting").Replace("{n}", waiting.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            int doors = FestivalCalendarView.DoorCount(state);
            return state.Available >= doors ? L("ui.calendar.all_open") : L("ui.calendar.next_tomorrow");
        }

        /// <summary>"Missed doors wait for you until 6 January." — empty when the server named no day.</summary>
        private string CatchUpLine(FestivalCalendarState state)
            => FestivalCalendarView.TryParseDay(state.CatchUpUntil, out var day)
                ? L("ui.calendar.catch_up").Replace("{date}", FestivalCalendarView.FormatDay(day, _game.Locale.Code()))
                : string.Empty;

        /// <summary>The door grid under a canvas group, so the reveal can take the pad and the clicks from it.</summary>
        private void AddGrid(FestivalCalendarState state, FestivalCalendarDefinition def)
        {
            var host = new GameObject("DoorGrid", typeof(RectTransform));
            host.transform.SetParent(_panel, false);
            UiKit.Place(host, 0f, 0f, DlgW, DlgH);
            _gridGroup = host.AddComponent<CanvasGroup>();
            var order = FestivalCalendarView.GridOrder(def?.Layout, FestivalCalendarView.DoorCount(state));
            _doors = BuildGrid(host.transform, GridX, GridY, GridW, GridH, state, order, CalendarLook.For(_game), OnDoor, _pendingDoor);
        }

        /// <summary>A door was clicked: a waiting one asks the server, an open one shows again what it held, a later one says
        /// when it comes.</summary>
        private void OnDoor(int door)
        {
            var state = State;
            if (state == null || _reveal != null || _pendingDoor > 0)
            {
                return;
            }

            switch (FestivalCalendarView.Status(state, door))
            {
                case CalendarDoorStatus.Available:
                    _pendingDoor = door;
                    _pendingSince = Time.unscaledTime;
                    ShowMessage(string.Empty, Gold);
                    _game.Network.UtcOffsetMinutes = LocalClock.UtcOffsetMinutes(); // the day boundary is the player's midnight
                    _game.Network.SendOpenCalendarDoor(_calendar, door);
                    Rebuild(opening: false); // the door shows "…" until the answer
                    break;
                case CalendarDoorStatus.Open:
                    ShowReveal(state, door, celebrate: false);
                    break;
                default:
                    ShowMessage(OpensOnLine(state, door), UiKit.CyanDim);
                    break;
            }
        }

        /// <summary>"This door opens on 14 December."</summary>
        private string OpensOnLine(FestivalCalendarState state, int door)
        {
            var day = FestivalCalendarView.DoorDay(_game.Content?.FindFestivalCalendar(_calendar), state.Season, door);
            return day.HasValue
                ? L("ui.calendar.opens_on").Replace("{date}", FestivalCalendarView.FormatDay(day.Value, _game.Locale.Code()))
                : L("ui.calendar.opens_later");
        }

        private void ShowMessage(string text, Color colour)
        {
            if (_message != null)
            {
                _message.text = text ?? string.Empty;
                _message.color = colour;
            }
        }

        /// <summary>The door the pad has selected right now (0 = none), to select it again after a rebuild.</summary>
        private int SelectedDoor()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var view in _doors)
            {
                if (view != null && view.Button != null && view.Button.gameObject == selected)
                {
                    return view.Door;
                }
            }

            return 0;
        }

        private void Reselect(int door)
        {
            if (door <= 0 || EventSystem.current == null)
            {
                return;
            }

            foreach (var view in _doors)
            {
                if (view.Door == door && view.Button != null)
                {
                    EventSystem.current.SetSelectedGameObject(view.Button.gameObject);
                    return;
                }
            }
        }
    }
}
