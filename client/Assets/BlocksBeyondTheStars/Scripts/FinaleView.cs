// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The finale encounter overlay (implementation plan P6, stages 3–4): once the player has flown the
    /// Guardian gauntlet and landed on the Guardian Core, they <b>hack</b> it open — holding the breach key
    /// channels <see cref="CoreHackIntent"/> ticks; the server replies with <see cref="CoreHackProgress"/>,
    /// shown as a bar — and then win the <b>argument duel</b>: the core states its logic
    /// (<see cref="CoreDialogueMessage"/>) and the player answers, sending <see cref="CoreDialogueChoiceIntent"/>.
    /// A correct contradiction advances; the wrong ones are dismissed. Clearing the last node powers the core down.
    /// <para>
    /// #2478 (E28): the duel itself is the cutscene director's dialogue mode now (<see cref="CutsceneDirector"/>,
    /// <see cref="DuelDialogueScreen"/>) — shown only on the core world, the answers on its own buttons. The IMGUI duel box and
    /// its number keys are gone: the digits were the hotbar's, and players anywhere picked answers by accident. This view keeps
    /// the duel's state for the music, the hack bar, the gauntlet banner, the breach hint and — when the player stepped back
    /// from the dialogue — the hint to face the core again (the use control).
    /// </para>
    /// Drawn with IMGUI (like the other HUD overlays), so it needs no Canvas wiring. All text is localized (bilingual DE+EN).
    /// </summary>
    public sealed class FinaleView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The argument duel is open (drives the dialogue boss track).</summary>
        public bool DuelActive => _duelActive;

        /// <summary>The player is actively channeling the breach — hack bar live, not yet complete.</summary>
        public bool Hacking => !_hacked && !_duelActive && Time.time < _hackBarVisibleUntil;

        /// <summary>Fixed name of the finale system (server <c>StarSystem.Name</c>), used to show the breach hint
        /// only while the player is actually on the Guardian Core.</summary>
        private const string GuardianSystemName = "Guardian Core";

        private const float HackTickInterval = 0.4f; // cadence of channel ticks while the breach control is held

        private bool _subscribed;
        private string _systemName = string.Empty;
        private float _hackTickTimer;

        // Hack bar.
        private int _hackProgress;
        private bool _hacked;
        private float _hackBarVisibleUntil;

        // Argument duel (its state; the dialogue is the director's, #2478).
        private bool _duelActive;

        // Resolution flash after the duel is won.
        private bool _won;
        private float _resolveVisibleUntil;

        private GUIStyle _box, _title, _body, _hint, _bar, _barFill;

        /// <summary>On the finale body, on foot. #1831: judged by the location id first — <c>JoinAccepted</c> names the
        /// system too, but only <c>WorldReset</c> ever reached <see cref="_systemName"/>, so a save joined while
        /// standing in the core chamber had no breach hint and the fire hold MINED the core instead of channelling
        /// the hack. The system name stays as the fallback for a star map that has not arrived yet.</summary>
        private bool OnGuardianWorld
            => !(Game?.SpaceViewActive ?? false)
               && ((Game?.StarMap?.ActiveLocationId is { } here && here.StartsWith("guardian_finale", System.StringComparison.Ordinal))
                   || string.Equals(_systemName, GuardianSystemName, System.StringComparison.Ordinal));

        /// <summary>#2478: the player stands on the Guardian core world (the duel's dialogue and its music belong there).</summary>
        public bool OnCoreWorld => OnGuardianWorld;

        /// <summary>#2478: the player stepped back from the duel dialogue and may face the core again here.</summary>
        private bool DuelResumeHint
            => _duelActive && FinaleActive && Game?.Views.Director != null && Game.Views.Director.DuelResumable;

        private bool FinaleActive => Game?.Story != null && Game.Story.GuardianSystemRevealed && !Game.Story.GuardianDefeated;

        /// <summary>True while holding the breach control would do something: finale live, on the Guardian
        /// world, core not yet hacked, no duel up. Gates the touch ATTACK/hold button (#1042).</summary>
        public bool BreachAvailable => FinaleActive && OnGuardianWorld && !_hacked && !_duelActive;

        private ImguiOverlay _overlay; // #1553: on-demand OnGUI — see ImguiOverlay

        private void Awake()
        {
            _overlay = ImguiOverlay.Attach(gameObject, DrawOverlay, repaintOnly: true); // labels only — the duel's buttons are the director's (#2478)
        }

        private void LateUpdate()
        {
            // IMGUI draws above every canvas, so the overlay steps aside under any cutscene (#2461) — the banners come back
            // the moment it ends. #2478: the gauntlet banner and the breach hint are things to show too — since the overlay
            // became on-demand (#1553) they only drew while the hack bar happened to be up.
            _overlay.Sync(WantsOverlay() && !GameBootstrap.CutsceneActive);
        }

        /// <summary>Whether anything of the overlay has something to show: the won banner, the hack bar, the resume hint, the
        /// breach hint on the core world, the gauntlet banner in the finale's flight.</summary>
        private bool WantsOverlay()
            => (_won && Time.time < _resolveVisibleUntil && !(Game?.Views.Director?.HoldsScreen ?? false))
               || Time.time < _hackBarVisibleUntil
               || DuelResumeHint
               || (FinaleActive && OnGuardianWorld && !_hacked)
               || InGauntlet(out _);

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

            // Breach channel: hold the control on the Guardian Core to send hack ticks (the server gates them by
            // location + state, so over-sending off-core simply does nothing). The control is the PrimaryFire
            // action — its default key IS the historical F, and going through InputMap gives the pad binding
            // and the touch ATTACK hold button the same channel (#1042/#1043).
            if (BreachAvailable && InputMap.Held(InputAction.PrimaryFire))
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

            // #2478: the duel's answers are the dialogue's own buttons (never the hotbar digits, E28). A player who stepped
            // back faces the core again with the use control near it.
            if (DuelResumeHint && !GameBootstrap.CutsceneActive && !Game.MenuOpen && InputMap.Down(InputAction.Interact))
            {
                Game.Views.Director.ResumeDuel();
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
            if (m == null)
            {
                return;
            }

            if (m.Won)
            {
                _duelActive = false;
                _won = true;
                _resolveVisibleUntil = Time.time + 9f;
                return;
            }

            _duelActive = true; // the dialogue itself is the director's (#2478)
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

        private void DrawOverlay()
        {
            EnsureStyles();

            if (_won && Time.time < _resolveVisibleUntil && !(Game?.Views.Resolution?.Playing ?? false) && !(Game?.Views.Director?.HoldsScreen ?? false))
            {
                // IMGUI draws above every canvas, so the banner must yield while the resolution
                // cinematic (#1124) or the director's stand-down card (#2478) is on screen — it says the same thing, bigger.
                DrawCentredBanner(L("ui.finale.resolved", "The Guardian core powers down. The galaxy is at peace."));
            }
            else if (Time.time < _hackBarVisibleUntil)
            {
                DrawHackBar();
            }
            else if (DuelResumeHint)
            {
                // #2478: the player stepped back from the duel dialogue — the use control faces the core again.
                DrawHint(HudUi.ExpandKeyTokens(Game?.Localizer, L("ui.finale.duel_resume", "Press {key:Interact} to answer the Guardian core")));
            }
            else if (InGauntlet(out int hostiles))
            {
                DrawGauntletBanner(hostiles);
            }
            else if (FinaleActive && OnGuardianWorld && !_hacked)
            {
                // All 14 locales carry the literal "[F]" token; swap in the control for the device in hand
                // (bound key / pad glyph / the touch ATTACK button) so the hint never names a dead key.
                string control = InputMap.ActiveDevice == InputDeviceKind.Touch
                    ? L("ui.touch.attack", "ATTACK")
                    : InputMap.Glyph(InputAction.PrimaryFire);
                DrawHint(L("ui.finale.hack_hint", "Hold [F] to breach the Guardian core").Replace("[F]", "[" + control + "]"));
            }
        }

        /// <summary>True while engaging the elite gauntlet: in the finale system's flight space with hostiles
        /// present. Reports how many machines remain.</summary>
        private bool InGauntlet(out int hostiles)
        {
            hostiles = 0;
            if (!FinaleActive || Game == null || !(Game.SpaceViewActive || Game.InSpace))
            {
                return false;
            }

            string here = Game.StarMap?.ActiveLocationId;
            if (string.IsNullOrEmpty(here) || !here.StartsWith("guardian_finale"))
            {
                return false;
            }

            var ents = Game.Space?.Entities;
            if (ents == null)
            {
                return false;
            }

            foreach (var e in ents)
            {
                if (e.Kind == "Drone" || e.Kind == "Ufo" || e.Kind == "Cruiser")
                {
                    hostiles++;
                }
            }

            return hostiles > 0;
        }

        private void DrawGauntletBanner(int hostiles)
        {
            string text = $"{L("ui.finale.gauntlet", "Guardian gauntlet")} — {hostiles} {L("ui.finale.hostiles", "elite machines")}";
            var size = _hint.CalcSize(new GUIContent(text));
            float w = size.x + 28f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.06f, w, 32f);
            GUI.Box(r, GUIContent.none, _box);
            GUI.Label(r, text, _hint);
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
        }
    }
}
