using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The finale encounter overlay (implementation plan P6, stages 3–4): once the player has flown the
    /// Guardian gauntlet and landed on the Guardian Core, they <b>hack</b> it open — holding the breach key
    /// channels <see cref="CoreHackIntent"/> ticks; the server replies with <see cref="CoreHackProgress"/>,
    /// shown as a bar — and then win the <b>argument duel</b>: the core states its logic
    /// (<see cref="CoreDialogueMessage"/>) and the player answers with a number key, sending
    /// <see cref="CoreDialogueChoiceIntent"/>. A correct contradiction advances; the wrong ones are dismissed.
    /// Clearing the last node powers the core down (pacification).
    ///
    /// Drawn with IMGUI (like the other HUD overlays) and driven entirely by keyboard, so it needs no Canvas
    /// wiring and never fights the first-person cursor lock. All text is localized (bilingual DE+EN).
    /// </summary>
    public sealed class FinaleView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Singleton so the music director can read the current finale phase.</summary>
        public static FinaleView Instance { get; private set; }

        /// <summary>The argument duel is open (drives the dialogue boss track).</summary>
        public bool DuelActive => _duelActive;

        /// <summary>The player is actively channeling the breach — hack bar live, not yet complete.</summary>
        public bool Hacking => !_hacked && !_duelActive && Time.time < _hackBarVisibleUntil;

        /// <summary>Fixed name of the finale system (server <c>StarSystem.Name</c>), used to show the breach hint
        /// only while the player is actually on the Guardian Core.</summary>
        private const string GuardianSystemName = "Guardian Core";

        private const float HackTickInterval = 0.4f; // cadence of channel ticks while the breach key is held
        private const KeyCode BreachKey = KeyCode.F;

        private bool _subscribed;
        private string _systemName = string.Empty;
        private float _hackTickTimer;

        // Hack bar.
        private int _hackProgress;
        private bool _hacked;
        private float _hackBarVisibleUntil;

        // Argument duel.
        private bool _duelActive;
        private int _duelNode;
        private string _promptKey = string.Empty;
        private string[] _choiceKeys = System.Array.Empty<string>();
        private string _responseKey = string.Empty;

        // Resolution flash after the duel is won.
        private bool _won;
        private float _resolveVisibleUntil;

        private GUIStyle _box, _title, _body, _choice, _hint, _bar, _barFill;

        private bool OnGuardianWorld => string.Equals(_systemName, GuardianSystemName, System.StringComparison.Ordinal);
        private bool FinaleActive => Game?.Story != null && Game.Story.GuardianSystemRevealed && !Game.Story.GuardianDefeated;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.CoreHackProgressReceived += OnHackProgress;
                Game.Network.CoreDialogueReceived += OnDialogue;
                Game.Network.GuardianSystemRevealedReceived += OnSystemRevealed;
                Game.Network.WorldResetReceived += OnWorldReset;
                _subscribed = true;
            }

            // Breach channel: hold the key on the Guardian Core to send hack ticks (the server gates them by
            // location + state, so over-sending off-core simply does nothing).
            if (FinaleActive && OnGuardianWorld && !_hacked && !_duelActive && Input.GetKey(BreachKey))
            {
                _hackTickTimer += Time.deltaTime;
                if (_hackTickTimer >= HackTickInterval)
                {
                    _hackTickTimer = 0f;
                    Game.Network.SendCoreHackTick();
                }
            }
            else
            {
                _hackTickTimer = HackTickInterval; // primed so the first hold fires immediately
            }

            // Duel: a number key picks the matching rebuttal.
            if (_duelActive && _choiceKeys.Length > 0)
            {
                for (int i = 0; i < _choiceKeys.Length && i < 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    {
                        Game.Network.SendCoreDialogueChoice(i);
                        break;
                    }
                }
            }
        }

        private void OnHackProgress(CoreHackProgress m)
        {
            _hackProgress = m.Progress;
            _hackBarVisibleUntil = Time.time + 2.5f;
            if (m.Complete)
            {
                _hacked = true;
            }
        }

        private void OnDialogue(CoreDialogueMessage m)
        {
            _responseKey = m.ResponseKey ?? string.Empty;
            if (m.Won)
            {
                _duelActive = false;
                _won = true;
                _resolveVisibleUntil = Time.time + 9f;
                return;
            }

            _duelActive = true;
            _duelNode = m.Node;
            _promptKey = m.PromptKey ?? string.Empty;
            _choiceKeys = m.ChoiceKeys ?? System.Array.Empty<string>();
        }

        private void OnSystemRevealed(GuardianSystemRevealed m)
        {
            // The narrator line + map marker are handled elsewhere; nothing extra to do here yet.
        }

        private void OnWorldReset(WorldReset m)
        {
            _systemName = m?.SystemName ?? string.Empty;
            if (!OnGuardianWorld)
            {
                // Left the core — drop the transient breach bar (the duel, if open, stays server-authoritative).
                _hackBarVisibleUntil = 0f;
            }
        }

        private string L(string key, string fallback) => Game?.Localizer?.Get(key) ?? fallback;

        private void OnGUI()
        {
            EnsureStyles();

            if (_duelActive)
            {
                DrawDuel();
            }
            else if (_won && Time.time < _resolveVisibleUntil)
            {
                DrawCentredBanner(L("ui.finale.resolved", "The Guardian core powers down. The galaxy is at peace."));
            }
            else if (Time.time < _hackBarVisibleUntil)
            {
                DrawHackBar();
            }
            else if (FinaleActive && OnGuardianWorld && !_hacked)
            {
                DrawHint(L("ui.finale.hack_hint", "Hold [F] to breach the Guardian core"));
            }
        }

        private void DrawHackBar()
        {
            float w = Mathf.Min(520f, Screen.width * 0.5f);
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.72f;
            GUI.Box(new Rect(x - 10f, y - 30f, w + 20f, 64f), GUIContent.none, _box);
            GUI.Label(new Rect(x, y - 26f, w, 22f), $"{L("ui.finale.hacking", "Breaching the Guardian core")}  {_hackProgress}%", _body);

            GUI.Box(new Rect(x, y, w, 18f), GUIContent.none, _bar);
            GUI.Box(new Rect(x + 2f, y + 2f, (w - 4f) * Mathf.Clamp01(_hackProgress / 100f), 14f), GUIContent.none, _barFill);
        }

        private void DrawHint(string text)
        {
            var size = _hint.CalcSize(new GUIContent(text));
            float w = size.x + 28f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.78f, w, 32f);
            GUI.Box(r, GUIContent.none, _box);
            GUI.Label(r, text, _hint);
        }

        private void DrawCentredBanner(string text)
        {
            float w = Mathf.Min(720f, Screen.width * 0.7f);
            float h = 120f;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(r, GUIContent.none, _box);
            GUI.Label(new Rect(r.x + 24f, r.y + 18f, r.width - 48f, r.height - 36f), text, _title);
        }

        private void DrawDuel()
        {
            float w = Mathf.Min(860f, Screen.width * 0.74f);
            float h = Mathf.Min(440f, Screen.height * 0.66f);
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(r, GUIContent.none, _box);

            float pad = 26f;
            float cx = r.x + pad, cw = r.width - pad * 2f;
            float cy = r.y + pad;

            GUI.Label(new Rect(cx, cy, cw, 24f), L("ui.finale.duel_title", "The Guardian Core"), _title);
            cy += 34f;

            // The core's statement.
            string prompt = L(_promptKey, _promptKey);
            float promptH = _body.CalcHeight(new GUIContent(prompt), cw);
            GUI.Label(new Rect(cx, cy, cw, promptH), prompt, _body);
            cy += promptH + 10f;

            // Its reaction to the previous (wrong) pick, if any.
            if (!string.IsNullOrEmpty(_responseKey))
            {
                string resp = L(_responseKey, _responseKey);
                float respH = _body.CalcHeight(new GUIContent(resp), cw);
                GUI.Label(new Rect(cx, cy, cw, respH), resp, _body);
                cy += respH + 12f;
            }

            // Numbered rebuttals.
            for (int i = 0; i < _choiceKeys.Length; i++)
            {
                string line = $"{i + 1})  {L(_choiceKeys[i], _choiceKeys[i])}";
                float ch = _choice.CalcHeight(new GUIContent(line), cw);
                if (GUI.Button(new Rect(cx, cy, cw, ch + 8f), line, _choice))
                {
                    Game.Network.SendCoreDialogueChoice(i);
                }
                cy += ch + 14f;
            }
        }

        private void EnsureStyles()
        {
            if (_box != null)
            {
                return;
            }

            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.03f, 0.05f, 0.08f, 0.92f));
            bg.Apply();

            var barBg = new Texture2D(1, 1);
            barBg.SetPixel(0, 0, new Color(0.10f, 0.12f, 0.16f, 0.95f));
            barBg.Apply();

            var fill = new Texture2D(1, 1);
            fill.SetPixel(0, 0, new Color(1f, 0.32f, 0.26f, 1f)); // Guardian red
            fill.Apply();

            _box = new GUIStyle(GUI.skin.box);
            _box.normal.background = bg;
            _box.border = new RectOffset(8, 8, 8, 8);

            _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = true };
            _title.normal.textColor = new Color(1f, 0.5f, 0.42f);
            _title.alignment = TextAnchor.UpperLeft;

            _body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _body.normal.textColor = new Color(0.88f, 0.92f, 0.97f);

            _choice = new GUIStyle(GUI.skin.button) { fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            _choice.normal.textColor = new Color(0.92f, 0.96f, 1f);
            _choice.padding = new RectOffset(12, 12, 8, 8);

            _hint = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = new Color(1f, 0.6f, 0.5f);

            _bar = new GUIStyle(GUI.skin.box);
            _bar.normal.background = barBg;

            _barFill = new GUIStyle(GUI.skin.box);
            _barFill.normal.background = fill;
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.CoreHackProgressReceived -= OnHackProgress;
                Game.Network.CoreDialogueReceived -= OnDialogue;
                Game.Network.GuardianSystemRevealedReceived -= OnSystemRevealed;
                Game.Network.WorldResetReceived -= OnWorldReset;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
