using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The native Arcade host (Stream D Phase 2): runs a pure <see cref="MinigameHost"/> and gives it a uGUI body
    /// — a point-filtered <see cref="RawImage"/> fed from the game's <see cref="Canvas2D"/> each frame, a HUD bar,
    /// and the start / help / pause / result overlays — plus the keyboard + pointer bridge. This is the C#
    /// replacement for the embedded-browser minigames: it needs no UWB/CEF, so it builds on every platform.
    ///
    /// Mounted into the Arcade play region by <see cref="ArcadeUI"/>; on a finished run it reports
    /// <c>(key, score, rating, completed)</c> through <see cref="OnReport"/> (the same path the browser used).
    /// </summary>
    public sealed class MinigameHostUI : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Raised on a finished run: (key, score, rating, completed). ArcadeUI records the highscore +
        /// grants knowledge, mirroring the old EmbeddedBrowser result handler.</summary>
        public System.Action<string, int, int, bool> OnReport;

        private RectTransform _region;     // play-region rect in 1920×1080 space
        private float _rx, _ry, _rw, _rh;

        private MinigameHost _host;
        private string _key = string.Empty;
        private Texture2D _tex;
        private RawImage _surface;
        private RectTransform _surfaceRt;
        private Text _hud;
        private RectTransform _overlayRoot;
        private MinigameState _shownState = (MinigameState)(-1);
        private bool _built;

        private const float HudH = 44f;

        // Action → physical keys. Self-contained (legacy Input) so this branch doesn't depend on Stream C's
        // InputMap; Pause is P (Escape stays the menu-close key), the rest mirror the web KEYMAP.
        private static readonly (MinigameAction action, KeyCode[] keys)[] KeyMap =
        {
            (MinigameAction.Left, new[] { KeyCode.LeftArrow, KeyCode.A }),
            (MinigameAction.Right, new[] { KeyCode.RightArrow, KeyCode.D }),
            (MinigameAction.Up, new[] { KeyCode.UpArrow, KeyCode.W }),
            (MinigameAction.Down, new[] { KeyCode.DownArrow, KeyCode.S }),
            (MinigameAction.Confirm, new[] { KeyCode.Return, KeyCode.KeypadEnter }),
            (MinigameAction.Primary, new[] { KeyCode.Space }),
            (MinigameAction.Secondary, new[] { KeyCode.LeftShift, KeyCode.RightShift }),
            (MinigameAction.Pause, new[] { KeyCode.P }),
            (MinigameAction.Restart, new[] { KeyCode.R }),
            (MinigameAction.Help, new[] { KeyCode.H }),
        };

        private readonly bool[] _keyState = new bool[System.Enum.GetValues(typeof(MinigameAction)).Length];
        private bool _ptrDown;
        private bool _ptrInside;

        public bool IsPlaying => _host != null;

        /// <summary>Builds the host UI under this component's own GameObject (a full-canvas child of
        /// <paramref name="parent"/>), so toggling it active shows/hides the whole game at once. The region is in
        /// the parent canvas' 1920×1080 top-left space.</summary>
        public void Mount(RectTransform parent, float x, float y, float w, float h)
        {
            _rx = x;
            _ry = y;
            _rw = w;
            _rh = h;
            var self = (RectTransform)transform;
            self.SetParent(parent, false);
            UiKit.Place(gameObject, 0f, 0f, parent.rect.width, parent.rect.height); // cover the canvas → region coords map straight through
            _region = self;
            EnsureBuilt();
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            // HUD bar across the top of the region.
            var hudGo = new GameObject("MgHud", typeof(RectTransform));
            hudGo.transform.SetParent(_region, false);
            UiKit.Place(hudGo, _rx, _ry, _rw, HudH);
            _hud = hudGo.AddComponent<Text>();
            _hud.font = UiKit.Font;
            _hud.fontSize = 20;
            _hud.color = UiKit.TextCol;
            _hud.alignment = TextAnchor.MiddleCenter;
            _hud.raycastTarget = false;

            // Surface RawImage (placed/resized per game in LayoutSurface). Point-filtered for crisp pixels; the
            // texture's row 0 is the bottom, the Canvas2D's row 0 is the top, so we flip V on display.
            var surfGo = new GameObject("MgSurface", typeof(RectTransform));
            surfGo.transform.SetParent(_region, false);
            _surfaceRt = UiKit.Place(surfGo, _rx, _ry + HudH, _rw, _rh - HudH);
            _surface = surfGo.AddComponent<RawImage>();
            _surface.uvRect = new Rect(0f, 1f, 1f, -1f);
            _surface.raycastTarget = false;

            _overlayRoot = (RectTransform)new GameObject("MgOverlay", typeof(RectTransform)).transform;
            _overlayRoot.SetParent(_region, false);
            UiKit.Place(_overlayRoot.gameObject, _rx, _ry, _rw, _rh);

            _built = true;
            gameObject.SetActive(false);
        }

        /// <summary>Start playing <paramref name="game"/> (shows its start screen).</summary>
        public void Play(IMinigame game, int best, bool german)
        {
            EnsureBuilt();
            gameObject.SetActive(true);
            _key = game.Key;
            _host = new MinigameHost(game, best, german);
            _host.OnResult += HandleResult;

            int cw = 640, ch = 440; // a default until the game creates its canvas on StartGame
            EnsureTexture(cw, ch);
            _shownState = (MinigameState)(-1);
            ResetKeyState();
            RefreshOverlay(force: true);
        }

        /// <summary>Stops + hides the host (leaving the Arcade or closing the menu).</summary>
        public void Stop()
        {
            if (_host != null)
            {
                _host.OnResult -= HandleResult;
                _host = null;
            }

            if (_built)
            {
                gameObject.SetActive(false);
                ClearOverlay();
            }
        }

        private void HandleResult(MinigameResult r) => OnReport?.Invoke(_key, r.Score, r.Rating, r.Completed);

        private void Update()
        {
            if (_host == null)
            {
                return;
            }

            PumpInput();
            _host.Tick(Time.unscaledDeltaTime);

            // The game (re)creates its canvas on StartGame — match the texture + layout to it.
            var surf = _host.Api.Surface;
            if (surf != null)
            {
                EnsureTexture(surf.Width, surf.Height);
                _tex.LoadRawTextureData(surf.Rgba);
                _tex.Apply(false);
            }

            UpdateHud();
            RefreshOverlay(force: false);
        }

        // ── input ────────────────────────────────────────────────────────────────────────────────

        private void PumpInput()
        {
            for (int i = 0; i < KeyMap.Length; i++)
            {
                var (action, keys) = KeyMap[i];
                bool down = false;
                foreach (var k in keys)
                {
                    if (Input.GetKey(k))
                    {
                        down = true;
                        break;
                    }
                }

                int ai = (int)action;
                if (down && !_keyState[ai])
                {
                    _host.Press(action);
                }
                else if (!down && _keyState[ai])
                {
                    _host.Release(action);
                }

                _keyState[ai] = down;
            }

            PumpPointer();
        }

        private void PumpPointer()
        {
            var surf = _host.Api.Surface;
            if (surf == null || _surfaceRt == null)
            {
                return;
            }

            bool inside = RectTransformUtility.RectangleContainsScreenPoint(_surfaceRt, Input.mousePosition, null);
            Vector2 canvasPt = default;
            if (inside && RectTransformUtility.ScreenPointToLocalPointInRectangle(_surfaceRt, Input.mousePosition, null, out var lp))
            {
                float w = _surfaceRt.rect.width, h = _surfaceRt.rect.height;
                float u = Mathf.Clamp01(lp.x / w);
                float fromTop = Mathf.Clamp01(-lp.y / h);
                canvasPt = new Vector2(u * surf.Width, fromTop * surf.Height);
            }

            if (Input.GetMouseButtonDown(0) && inside)
            {
                _ptrDown = true;
                _host.Pointer(PointerPhase.Down, canvasPt.x, canvasPt.y);
            }
            else if (Input.GetMouseButtonUp(0) && _ptrDown)
            {
                _ptrDown = false;
                _host.Pointer(PointerPhase.Up, canvasPt.x, canvasPt.y);
            }
            else if (inside)
            {
                _host.Pointer(PointerPhase.Move, canvasPt.x, canvasPt.y);
            }

            _ptrInside = inside;
        }

        private void ResetKeyState()
        {
            for (int i = 0; i < _keyState.Length; i++)
            {
                _keyState[i] = false;
            }

            _ptrDown = false;
        }

        // ── presentation ─────────────────────────────────────────────────────────────────────────

        private void EnsureTexture(int w, int h)
        {
            if (_tex != null && _tex.width == w && _tex.height == h)
            {
                LayoutSurface(w, h);
                return;
            }

            if (_tex != null)
            {
                Destroy(_tex);
            }

            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            _surface.texture = _tex;
            LayoutSurface(w, h);
        }

        /// <summary>Aspect-fit the canvas into the region below the HUD bar, centred.</summary>
        private void LayoutSurface(int cw, int ch)
        {
            float availW = _rw, availH = _rh - HudH;
            float scale = Mathf.Min(availW / cw, availH / ch);
            float dw = cw * scale, dh = ch * scale;
            float x = _rx + (availW - dw) * 0.5f;
            float y = _ry + HudH + (availH - dh) * 0.5f;
            UiKit.Place(_surfaceRt.gameObject, x, y, dw, dh);
        }

        private void UpdateHud()
        {
            if (_host.State != MinigameState.Playing && _host.State != MinigameState.Paused)
            {
                _hud.text = string.Empty;
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(De("Punkte", "Score")).Append(' ').Append(_host.Score);
            foreach (var f in _host.HudFields)
            {
                if (f.key == "score")
                {
                    continue;
                }

                sb.Append("    ").Append(f.key).Append(' ').Append(f.value);
            }

            sb.Append("    ").Append(De("Zeit", "Time")).Append(' ').Append(FmtTime(_host.NowSeconds));
            if (_host.Best > 0)
            {
                sb.Append("    ").Append(De("Beste", "Best")).Append(' ').Append(_host.Best);
            }

            _hud.text = sb.ToString();
        }

        private static string FmtTime(float s)
        {
            int t = Mathf.Max(0, Mathf.FloorToInt(s));
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        private void RefreshOverlay(bool force)
        {
            if (!force && _host.State == _shownState)
            {
                return;
            }

            _shownState = _host.State;
            ClearOverlay();
            switch (_shownState)
            {
                case MinigameState.Start:
                    BuildStart();
                    break;
                case MinigameState.Help:
                    BuildHelp();
                    break;
                case MinigameState.Paused:
                    BuildPaused();
                    break;
                case MinigameState.Result:
                    BuildResult();
                    break;
                // Playing → no overlay.
            }
        }

        private void ClearOverlay()
        {
            for (int i = _overlayRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_overlayRoot.GetChild(i).gameObject);
            }
        }

        private Image Panel()
        {
            // A dim card centred in the region.
            float pw = Mathf.Min(720f, _rw - 80f), ph = Mathf.Min(560f, _rh - 80f);
            float px = (_rw - pw) * 0.5f, py = (_rh - ph) * 0.5f;
            UiKit.AddImage(_overlayRoot, 0, 0, _rw, _rh, UiKit.SolidSprite, new Color(0.02f, 0.05f, 0.10f, 0.82f));
            return UiKit.AddPanel(_overlayRoot, px, py, pw, ph, new Color(0.05f, 0.10f, 0.18f, 0.98f));
        }

        private bool German => Game != null && Game.German;
        private string De(string de, string en) => German ? de : en;

        private void BuildStart()
        {
            var g = _host.Game;
            var panel = Panel().transform;
            float pw = ((RectTransform)panel).rect.width;
            UiKit.AddText(panel, 0, 40, pw, 44, g.Title.Get(German), 30, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(panel, 0, 92, pw, 26, De("Schwierigkeit", "Difficulty") + ": " + g.Difficulty + "/5", 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, 40, 140, pw - 80, 140, g.Desc.Get(German), 18, UiKit.TextCol, TextAnchor.UpperCenter);
            UiKit.AddText(panel, 40, 300, pw - 80, 40, De("Schlage deinen Rekord für Wissen (+5/+10/+15)", "Beat your best for knowledge (+5/+10/+15)"), 14, UiKit.Ok, TextAnchor.MiddleCenter);
            float bw = 200f, by = ((RectTransform)panel).rect.height - 90f;
            UiKit.AddButton(panel, pw / 2 - bw - 12, by, bw, 56, De("Starten", "Start"), () => _host.StartGame());
            UiKit.AddButton(panel, pw / 2 + 12, by, bw, 56, De("Hilfe", "Help"), () => _host.ShowHelp());
        }

        private void BuildHelp()
        {
            var g = _host.Game;
            var panel = Panel().transform;
            float pw = ((RectTransform)panel).rect.width;
            UiKit.AddText(panel, 0, 36, pw, 40, De("Hilfe", "Help"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var sb = new System.Text.StringBuilder();
            sb.Append(g.Desc.Get(German)).Append("\n\n");
            foreach (var line in g.Help)
            {
                sb.Append("• ").Append(line.Get(German)).Append('\n');
            }

            UiKit.AddText(panel, 44, 100, pw - 88, 300, sb.ToString(), 18, UiKit.TextCol, TextAnchor.UpperLeft);
            UiKit.AddButton(panel, pw / 2 - 100, ((RectTransform)panel).rect.height - 84f, 200, 54, De("Zurück", "Back"), () => _host.CloseHelp());
        }

        private void BuildPaused()
        {
            var panel = Panel().transform;
            float pw = ((RectTransform)panel).rect.width;
            float ph = ((RectTransform)panel).rect.height;
            UiKit.AddText(panel, 0, ph / 2 - 120, pw, 44, De("Pausiert", "Paused"), 28, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            float bw = 220f, bx = pw / 2 - bw / 2;
            UiKit.AddButton(panel, bx, ph / 2 - 50, bw, 50, De("Fortsetzen", "Resume"), () => _host.Resume());
            UiKit.AddButton(panel, bx, ph / 2 + 8, bw, 50, De("Neu starten", "Restart"), () => _host.StartGame());
            UiKit.AddButton(panel, bx, ph / 2 + 66, bw, 50, De("Verlassen", "Quit"), () => _host.Quit());
        }

        private void BuildResult()
        {
            var r = _host.Result;
            var panel = Panel().transform;
            float pw = ((RectTransform)panel).rect.width;
            float ph = ((RectTransform)panel).rect.height;
            UiKit.AddText(panel, 0, 44, pw, 44, r.Completed ? De("Abgeschlossen", "Complete") : De("Fehlgeschlagen", "Failed"),
                30, r.Completed ? UiKit.Ok : UiKit.Warn, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(panel, 0, 110, pw, 30, De("Bewertung", "Rating") + ": " + r.Rating + "/3", 20, UiKit.Cyan, TextAnchor.MiddleCenter);
            UiKit.AddText(panel, 0, 156, pw, 30, De("Punkte", "Score") + ": " + r.Score, 22, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            string reward = r.IsNewBest
                ? "★ " + De("Neuer Rekord!", "New best!") + "   +" + r.Knowledge + " " + De("Wissen", "knowledge")
                : (r.Completed ? De("Kein neuer Rekord — schlag ihn für Wissen", "No new best — beat it for knowledge") : string.Empty);
            UiKit.AddText(panel, 40, 200, pw - 80, 30, reward, 16, r.IsNewBest ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleCenter);
            float bw = 200f, by = ph - 90f;
            UiKit.AddButton(panel, pw / 2 - bw - 12, by, bw, 56, De("Erneut", "Play again"), () => _host.StartGame());
            UiKit.AddButton(panel, pw / 2 + 12, by, bw, 56, De("Schließen", "Close"), () => _host.Quit()); // back to this game's start screen
        }

        private void OnDestroy()
        {
            if (_tex != null)
            {
                Destroy(_tex);
            }
        }
    }
}
