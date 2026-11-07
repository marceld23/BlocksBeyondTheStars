// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Minigames.Games;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The story minigame window (#3352): the server opens it (<see cref="StoryMinigameStart"/>) when a Song Disc the player has
    /// not decoded yet sings at a Song Spindle. It runs the note-key minigame (<see cref="NoteKeyGame"/>, the disc's three words
    /// and the letters the player knows) in the native minigame host (<see cref="MinigameHostUI"/>, in story mode: no arcade
    /// knowledge lines) and reports every finished or failed run with the spelled words — the server checks them. After
    /// <see cref="SongDiscs.FailuresBeforeSkip"/> failed tries (the server's count, re-sent with each failure) a Skip button
    /// appears: VEGA's help, never a dead end.
    /// <para>Its own canvas (order 62, above the Tab menu and the calendar's tier) with a modal built first and the host on top
    /// of it; it holds a lone player's world while it is up (the result intent passes the singleplayer pause), takes the
    /// pointer and the pad, and closes with its button, the result's Close, Esc or pad B — and by itself when the world goes.</para>
    /// </summary>
    [DefaultExecutionOrder(-44)] // before GameMenu: Esc / pad B close the window first
    public sealed class StoryMinigameUI : MonoBehaviour
    {
        /// <summary>The world-hold name of the window (<see cref="WorldHoldArbiter"/>).</summary>
        public const string HoldName = "story_minigame";

        private const float W = 1920f, H = 1080f;
        private const float DlgW = 1300f, DlgH = 980f;
        private const float HostX = (W - DlgW) / 2f + 30f, HostY = (H - DlgH) / 2f + 96f, HostW = DlgW - 60f, HostH = DlgH - 190f;

        private static StoryMinigameUI _current;

        private GameBootstrap _game;
        private Canvas _canvas;
        private MinigameHostUI _host;
        private NoteKeyGame _noteKey;
        private string _gameKey = string.Empty;
        private string _context = string.Empty;
        private Button _skip;
        private Text _status;
        private bool _closing;

        /// <summary>True while a story minigame window is up.</summary>
        public static bool IsOpen => _current != null;

        /// <summary>Opens the window for <paramref name="m"/> — or, when it is already open for the same disc, only takes the
        /// server's new failure count (which may bring the Skip button).</summary>
        public static void Show(GameBootstrap game, StoryMinigameStart m)
        {
            if (game == null || game.Network == null || m == null || m.Game != SongDiscs.NoteKeyGame)
            {
                return;
            }

            if (_current != null && !_current._closing && _current._context == m.Context)
            {
                _current.UpdateFailures(m.Failures);
                return;
            }

            CloseCurrent();
            var canvas = UiKit.CreateCanvas("Story Minigame");
            canvas.sortingOrder = 62;
            var window = canvas.gameObject.AddComponent<StoryMinigameUI>();
            window.Init(game, canvas, m);
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

        private string L(string key) => _game != null && _game.Localizer != null ? _game.Localizer.Get(key) : key;

        private void Init(GameBootstrap game, Canvas canvas, StoryMinigameStart m)
        {
            _current = this;
            _game = game;
            _canvas = canvas;
            _gameKey = m.Game;
            _context = m.Context ?? string.Empty;
            UiNav.Enable(canvas.gameObject);
            game.SetMenuOwner(this, true);
            game.WorldHold.Hold(HoldName, Time.realtimeSinceStartup);
            BuildFrame();
            BuildHost(m);
            UpdateFailures(m.Failures);
        }

        /// <summary>The modal first (the host goes on top of it): the title, the hint line, Skip (hidden) and Close.</summary>
        private void BuildFrame()
        {
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (W - DlgW) / 2f, (H - DlgH) / 2f, DlgW, DlgH);
            UiKit.OpenModal(overlay);
            UiKit.AddText(panel, 0f, 18f, DlgW, 44f, L("minigame.note_key.story_title"), 28, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(panel, 40f, 60f, DlgW - 80f, 30f, L("minigame.note_key.story_hint"), 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
            _status = UiKit.AddText(panel, 40f, DlgH - 86f, DlgW - 520f, 60f, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _skip = UiKit.AddButton(panel, DlgW - 470f, DlgH - 80f, 220f, 54f, L("minigame.note_key.skip"), Skip);
            _skip.gameObject.SetActive(false);
            UiKit.AddButton(panel, DlgW - 240f, DlgH - 80f, 200f, 54f, L("ui.action.close"), Close, "btn_exit");
        }

        private void BuildHost(StoryMinigameStart m)
        {
            var go = new GameObject("StoryMinigameHost", typeof(RectTransform));
            _host = go.AddComponent<MinigameHostUI>();
            _host.Game = _game;
            _host.StoryMode = true;
            _host.OnReport = OnReport;
            _host.OnClose = Close;
            _host.Mount((RectTransform)_canvas.transform, HostX, HostY, HostW, HostH);
            _noteKey = new NoteKeyGame(m.Words ?? System.Array.Empty<string>(), m.KnownLetters, m.Instrument);
            _host.Play(_noteKey, 0, L);
        }

        /// <summary>The server's failure count: from the skip threshold on, the Skip button and VEGA's offer.</summary>
        private void UpdateFailures(int failures)
        {
            bool mayskip = failures >= SongDiscs.FailuresBeforeSkip;
            if (_skip != null)
            {
                _skip.gameObject.SetActive(mayskip);
            }

            if (_status != null)
            {
                _status.text = mayskip ? L("minigame.note_key.skip_offer") : failures > 0 ? L("minigame.note_key.try_again") : string.Empty;
            }
        }

        /// <summary>A finished run: the spelled words go to the server, which decodes the disc or counts a failed try.</summary>
        private void OnReport(string key, int score, int rating, bool completed)
        {
            if (_game?.Network == null || _noteKey?.Puzzle == null)
            {
                return;
            }

            _game.Network.SendStoryMinigameResult(_gameKey, _context, completed, _noteKey.Puzzle.Answers);
            if (completed && _status != null)
            {
                _status.text = L("minigame.note_key.decoded");
            }

            if (completed && _skip != null)
            {
                _skip.gameObject.SetActive(false);
            }
        }

        private void Skip()
        {
            _game?.Network?.SendStoryMinigameSkip(_gameKey, _context);
            Close();
        }

        private void Update()
        {
            if (_closing)
            {
                return;
            }

            if (_game == null || _game.Network == null)
            {
                Close(); // the world went
                return;
            }

            if (InputMap.Down(InputAction.UiCancel) && !_game.ChatTyping && !UiKit.TextFieldFocused() && !_game.MenuInputHandledThisFrame)
            {
                _game.MarkMenuInputHandled();
                Close();
            }
        }

        /// <summary>Closes the window: stops the game, releases the hold, the cursor and the pad, and destroys the canvas.</summary>
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

            if (_host != null)
            {
                _host.OnReport = null;
                _host.OnClose = null;
                _host.Stop();
                _host = null;
            }

            if (_game != null)
            {
                _game.WorldHold.Release(HoldName);
                _game.SetMenuOwner(this, false);
            }

            _game = null;
        }
    }
}
