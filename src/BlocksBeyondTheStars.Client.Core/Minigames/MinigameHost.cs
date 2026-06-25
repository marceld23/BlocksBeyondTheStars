// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>
    /// The pure, engine-free heart of the native Arcade — the C# port of <c>web/minigames/_shared/framework.js</c>.
    /// It runs the shell state machine (start / help / playing / paused / result), a paused-aware game-time clock,
    /// frame-driven timers, the HUD model and the score→reward rule, and hands a game a <see cref="MinigameApi"/>
    /// to draw and read input through. No UnityEngine here, so the whole shell + every ported game's logic is
    /// unit-tested headless; the Unity host only feeds it dt + raw input, uploads <see cref="Canvas"/> to a
    /// texture, and renders the overlays.
    ///
    /// Lifecycle mirrors the web framework exactly: on each (re)start the host re-invokes <see cref="IMinigame.Create"/>
    /// (clearing the api's loop/handlers/timers/canvas first) so a game's per-round state lives in a fresh closure.
    /// </summary>
    public sealed class MinigameHost
    {
        private readonly IMinigame _game;
        private readonly Random _rng;

        private MinigameController? _controller;
        private MinigameState _state = MinigameState.Start;
        private MinigameState _helpReturn = MinigameState.Start;

        // Clock: game-time excludes paused spans, so timers and Now() freeze on pause (the web used wall-clock
        // setTimeout; driving timers off this clock makes pause real and the whole thing deterministic to test).
        private float _now;
        private int _lastResultBest;

        // Per-round wiring the api populates (cleared on (re)start).
        internal readonly List<Action<float>> LoopFns = new();
        internal readonly Dictionary<MinigameAction, List<Action>> PressHandlers = new();
        internal readonly HashSet<MinigameAction> Held = new();
        internal Action<PointerInput>? PointerCb;
        internal Canvas2D? Canvas;
        internal readonly List<(string key, string value)> Hud = new();

        /// <summary>The current "score" HUD value (0 until the game sets it). Exposed for the host's HUD bar.</summary>
        public int Score { get; private set; }

        private readonly List<Timer> _timers = new();
        private int _nextTimerId = 1;

        private struct Timer
        {
            public int Id;
            public float Due;       // game-time the next fire is due
            public float Interval;  // 0 => one-shot
            public Action Fn;
        }

        public MinigameHost(IMinigame game, int best = 0, bool german = false, int? seed = null)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            Best = best;
            German = german;
            Api = new MinigameApi(this);
        }

        public MinigameApi Api { get; }
        public MinigameState State => _state;
        public int Best { get; private set; }
        public bool German { get; }
        public IMinigame Game => _game;

        /// <summary>The result of the most recent finished run (valid once <see cref="State"/> is Result).</summary>
        public MinigameResult Result { get; private set; }

        /// <summary>Raised exactly once per finished run, before the result overlay shows — the host UI subscribes
        /// to grant knowledge / record the highscore (mirrors the old EmbeddedBrowser <c>OnReportResult</c>).</summary>
        public event Action<MinigameResult>? OnResult;

        /// <summary>Live HUD fields for the host bar, in registration order. "score" and "time" are appended by the
        /// host UI; this is just the game's own extra fields (e.g. integrity, depth).</summary>
        public IReadOnlyList<(string key, string value)> HudFields => Hud;

        public float NowSeconds => _state == MinigameState.Playing || _state == MinigameState.Paused ? _now : 0f;

        // --- shell transitions ---

        /// <summary>Begin a fresh round (from the start/result/pause screen). Re-creates the game.</summary>
        public void StartGame()
        {
            ResetRoundState();
            _controller = _game.Create(Api);
            _now = 0f;
            _state = MinigameState.Playing;
            _controller?.Start?.Invoke();
        }

        public void Pause()
        {
            if (_state != MinigameState.Playing)
            {
                return;
            }

            _state = MinigameState.Paused;
            _controller?.Pause?.Invoke();
        }

        public void Resume()
        {
            if (_state != MinigameState.Paused)
            {
                return;
            }

            _state = MinigameState.Playing;
            _controller?.Resume?.Invoke();
        }

        /// <summary>Open the help overlay; remembers where to return (start or pause). Pauses a running game.</summary>
        public void ShowHelp()
        {
            _helpReturn = _state == MinigameState.Playing || _state == MinigameState.Paused
                ? MinigameState.Paused
                : MinigameState.Start;
            if (_state == MinigameState.Playing)
            {
                Pause();
            }

            _state = MinigameState.Help;
        }

        public void CloseHelp() => _state = _helpReturn;

        /// <summary>Leave a running/paused game back to the start screen (the pause overlay's Quit).</summary>
        public void Quit()
        {
            ResetRoundState();
            _controller = null;
            _state = MinigameState.Start;
        }

        /// <summary>Advance the simulation by <paramref name="dt"/> seconds. No-op unless actively playing. dt is
        /// clamped to 50 ms so a stalled frame can't teleport the game (matches the web loop's clamp).</summary>
        public void Tick(float dt)
        {
            if (_state != MinigameState.Playing)
            {
                return;
            }

            if (dt < 0f)
            {
                dt = 0f;
            }
            else if (dt > 0.05f)
            {
                dt = 0.05f;
            }

            _now += dt;
            FireDueTimers();

            // A timer (or a press handler dispatched earlier this frame) may have finished the game; bail if so.
            if (_state != MinigameState.Playing)
            {
                return;
            }

            for (int i = 0; i < LoopFns.Count; i++)
            {
                LoopFns[i](dt);
                if (_state != MinigameState.Playing)
                {
                    return; // a loop fn called Complete/Fail
                }
            }
        }

        // --- input feed (called by the host UI) ---

        /// <summary>Press an action. Pause/Restart/Help are handled by the shell; gameplay presses dispatch to the
        /// game's bound handlers and latch <see cref="Held"/>. Returns silently in non-play states (except the
        /// shell keys).</summary>
        public void Press(MinigameAction a)
        {
            if (a == MinigameAction.Pause)
            {
                if (_state == MinigameState.Paused)
                {
                    Resume();
                }
                else if (_state == MinigameState.Playing)
                {
                    Pause();
                }

                return;
            }

            if (a == MinigameAction.Help)
            {
                if (_state == MinigameState.Help)
                {
                    CloseHelp();
                }
                else
                {
                    ShowHelp();
                }

                return;
            }

            if (_state != MinigameState.Playing)
            {
                return;
            }

            if (a == MinigameAction.Restart)
            {
                StartGame();
                return;
            }

            if (Held.Add(a) && PressHandlers.TryGetValue(a, out var fns))
            {
                // Snapshot: a handler may restart the game and rebuild the list.
                var copy = fns.ToArray();
                foreach (var fn in copy)
                {
                    fn();
                    if (_state != MinigameState.Playing)
                    {
                        return;
                    }
                }
            }
        }

        public void Release(MinigameAction a) => Held.Remove(a);

        public void Pointer(PointerPhase phase, float x, float y)
        {
            if (_state != MinigameState.Playing)
            {
                return;
            }

            PointerCb?.Invoke(new PointerInput(phase, x, y));
        }

        // --- internals used by MinigameApi ---

        internal int Rand(int n) => n <= 0 ? 0 : _rng.Next(n);

        internal void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        internal int AddTimer(Action fn, float seconds, bool repeat)
        {
            int id = _nextTimerId++;
            _timers.Add(new Timer { Id = id, Due = _now + Math.Max(0f, seconds), Interval = repeat ? Math.Max(0.0001f, seconds) : 0f, Fn = fn });
            return id;
        }

        internal void StopTimer(int id)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                if (_timers[i].Id == id)
                {
                    _timers.RemoveAt(i);
                    return;
                }
            }
        }

        private void FireDueTimers()
        {
            // Iterate by index off a snapshot of due ids — a fired callback may add/stop timers or finish the game.
            for (int i = 0; i < _timers.Count; i++)
            {
                var t = _timers[i];
                if (t.Due > _now)
                {
                    continue;
                }

                if (t.Interval > 0f)
                {
                    t.Due += t.Interval;
                    if (t.Due <= _now)
                    {
                        t.Due = _now + t.Interval; // don't burst-catch-up after a long frame
                    }

                    _timers[i] = t;
                }
                else
                {
                    _timers.RemoveAt(i);
                    i--;
                }

                t.Fn();
                if (_state != MinigameState.Playing)
                {
                    return;
                }
            }
        }

        internal void SetHud(string key, string value)
        {
            if (string.Equals(key, "score", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int s))
            {
                Score = s;
            }

            for (int i = 0; i < Hud.Count; i++)
            {
                if (Hud[i].key == key)
                {
                    Hud[i] = (key, value);
                    return;
                }
            }

            Hud.Add((key, value));
        }

        internal void Finish(int score, int rating, bool completed)
        {
            if (_state != MinigameState.Playing && _state != MinigameState.Paused)
            {
                return;
            }

            int finalScore = score >= 0 ? score : Math.Max(0, Score);
            int r = completed ? Math.Max(1, Math.Min(3, rating)) : 0;
            bool isNewBest = completed && finalScore > Best;
            var result = new MinigameResult(finalScore, r, completed, isNewBest);

            _lastResultBest = Best;
            if (finalScore > Best)
            {
                Best = finalScore; // so a replay this session compares against the run just set
            }

            Result = result;
            _state = MinigameState.Result;
            OnResult?.Invoke(result);
        }

        /// <summary>The best score as it stood BEFORE the most recent finish — for the result UI's "was/now" line.</summary>
        public int PreviousBest => _lastResultBest;

        private void ResetRoundState()
        {
            LoopFns.Clear();
            PressHandlers.Clear();
            Held.Clear();
            PointerCb = null;
            Canvas = null;
            Hud.Clear();
            Score = 0;
            _timers.Clear();
        }
    }

    /// <summary>The handle a game draws and reads input through — the C# <c>api</c> object from the web framework.
    /// All state lives on the owning <see cref="MinigameHost"/>; this is a thin, game-facing facade.</summary>
    public sealed class MinigameApi
    {
        private readonly MinigameHost _host;

        internal MinigameApi(MinigameHost host) => _host = host;

        public bool German => _host.German;
        public int Best => _host.Best;

        /// <summary>Create (and store) the drawing surface for this round. Returns the same <see cref="Canvas2D"/>
        /// the host uploads to a texture each frame.</summary>
        public Canvas2D Canvas(int width, int height)
        {
            _host.Canvas = new Canvas2D(width, height);
            return _host.Canvas;
        }

        public Canvas2D? Surface => _host.Canvas;

        /// <summary>Elapsed playing time in seconds (frozen while paused).</summary>
        public float Now() => _host.NowSeconds;

        public void Hud(string key, object value) => _host.SetHud(key, System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);

        public void Hud(int score) => _host.SetHud("score", score.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void Bind(MinigameAction action, Action fn)
        {
            if (fn == null)
            {
                return;
            }

            if (!_host.PressHandlers.TryGetValue(action, out var list))
            {
                _host.PressHandlers[action] = list = new List<Action>();
            }

            list.Add(fn);
        }

        public bool Held(MinigameAction action) => _host.Held.Contains(action);

        public void Pointer(Action<PointerInput> cb) => _host.PointerCb = cb;

        public void Loop(Action<float> fn)
        {
            if (fn != null)
            {
                _host.LoopFns.Add(fn);
            }
        }

        /// <summary>Run <paramref name="fn"/> once after <paramref name="seconds"/> of game-time (pause-aware).</summary>
        public int After(Action fn, float seconds) => _host.AddTimer(fn, seconds, repeat: false);

        /// <summary>Run <paramref name="fn"/> every <paramref name="seconds"/> of game-time (pause-aware).</summary>
        public int Every(Action fn, float seconds) => _host.AddTimer(fn, seconds, repeat: true);

        public void StopTimer(int id) => _host.StopTimer(id);

        public int Rand(int n) => _host.Rand(n);

        public void Shuffle<T>(IList<T> list) => _host.Shuffle(list);

        public void Complete(int score, int rating = 1) => _host.Finish(score, rating, completed: true);

        public void Fail(int score = -1) => _host.Finish(score, 0, completed: false);
    }
}
