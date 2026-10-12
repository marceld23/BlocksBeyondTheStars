// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The director's dialogue mode for the Guardian core's argument duel (#2478, E28, synthesis §6.5 <c>fin.duel</c>; it
    /// replaces the old IMGUI box and its number keys). Every <see cref="CoreDialogueMessage"/> updates
    /// <see cref="CoreDuelState"/>; on the core world the dialogue takes the screen like a set piece: a full letterbox, the
    /// camera close on the core column in its chamber (the server sends where), the core's box and the answers as their own
    /// buttons (<see cref="DuelDialogueScreen"/>). The HUD, the touch layer and the controls step aside under the
    /// <see cref="DuelGateReason"/>; a singleplayer world holds while the core waits (never in multiplayer, where the camera
    /// also returns after 30 s — the dialogue stays). Elsewhere the dialogue never shows (the server sends a banner).
    /// <list type="bullet">
    /// <item>The breach scene plays first; the dialogue opens when the player's moment is safe (not behind the loading veil,
    /// a menu or another scene, not falling).</item>
    /// <item>Esc / pad B / the corner button steps back: the duel waits, and the player faces the core again near it with
    /// the use control (<see cref="FinaleView"/> shows how) or when the core speaks again.</item>
    /// <item>Danger (multiplayer: damage, a machine closing in) closes it; it comes back by itself once it is safe.</item>
    /// <item>After a pick the buttons wait for the core's answer — the server decides what counts.</item>
    /// </list>
    /// </summary>
    public sealed partial class CutsceneDirector
    {
        /// <summary>The dialogue's reason in <see cref="GameBootstrap.Cutscenes"/> while it is on screen.</summary>
        public const string DuelGateReason = "duel";

        /// <summary>The id of the pack speaker that voices the core (the <c>guardian_core</c> finale's speaker).</summary>
        private const string CoreSpeakerId = "core";

        /// <summary>How often a dialogue without its camera looks again for the close framing (chunks may still stream).</summary>
        private const float DuelCameraRetrySeconds = 1f;

        private const int DuelCameraTries = 5;

        private readonly CoreDuelState _duel = new CoreDuelState();
        private readonly object _duelCursorOwner = new object();
        private DuelDialogueScreen _duelScreen;
        private bool _duelSteppedBack;
        private bool _duelDangerClosed;
        private bool _duelHolding;
        private bool _duelCamera;
        private bool _duelCapped;
        private int _duelCameraTries;
        private float _duelCameraRetryAt;
        private float _duelCameraSeconds;
        private int _duelShownVersion = -1;
        private int _duelPickedVersion = -1;
        private float _duelPickedAt = float.NegativeInfinity;
        private float _duelOpenedAt;
        private float _duelLetterbox;

        /// <summary>True while the duel dialogue is on screen.</summary>
        public static bool DuelShown => Instance != null && Instance._duelScreen != null;

        /// <summary>Whether a player who stepped back from the duel may face the core again now (near the core, on its world,
        /// nothing else on screen) — <see cref="FinaleView"/> shows the hint and calls <see cref="ResumeDuel"/>.</summary>
        public bool DuelResumable
            => _duelSteppedBack && _duelScreen == null && _play == null && Game != null && !Game.Spectating
               && _duel.Open && CoreDuelRules.OnCoreWorld(Game.StarMap?.ActiveLocationId, Game.SpaceViewActive)
               && CoreDuelRules.NearCore(_duel, Game.PlayerPosition.x, Game.PlayerPosition.y, Game.PlayerPosition.z, Game.Circumference);

        /// <summary>The player faces the core again after stepping back.</summary>
        public void ResumeDuel() => _duelSteppedBack = false;

        private void OnCoreDialogue(CoreDialogueMessage m)
        {
            _duel.Apply(m);
            if (m != null && !m.Won)
            {
                _duelSteppedBack = false; // the core speaks again (a new node, or a re-presented one) — face it
            }
        }

        /// <summary>The dialogue's frame tick (only while no scene plays). Returns true while it owns the screen, so no scene
        /// starts meanwhile.</summary>
        private bool TickDuel()
        {
            bool want = CoreDuelRules.ShowsDialogue(_duel, Game.StarMap?.ActiveLocationId, Game.SpaceViewActive, Game.Spectating, _duelSteppedBack);
            if (want && BreachWaiting())
            {
                want = false; // the core wakes first (the breach scene), then it speaks
            }

            if (want && _duelScreen == null && !CoreDuelRules.MayOpen(_inputs))
            {
                want = false; // the veil, a menu, a fall … — and in multiplayer danger: it opens once the moment allows
            }

            if (want && _duelDangerClosed)
            {
                _duelDangerClosed = false; // safe again (checked just above)
            }

            if (!want)
            {
                CloseDuel();
                return false;
            }

            if (_duelScreen == null)
            {
                OpenDuel();
            }

            // Multiplayer danger: damage or a machine closing in ends the dialogue for now; it returns once it is safe. (In
            // singleplayer the dialogue holds the world — nothing reaches the player.)
            if (CoreDuelRules.StepsAsideForDanger(_inputs, Time.time - _duelOpenedAt))
            {
                _duelDangerClosed = true;
                CloseDuel();
                return false;
            }

            if (CinematicFrame.SkipPressed() || _duelScreen.ConsumeSkipClick())
            {
                Game.MarkMenuInputHandled(); // this Esc steps back — it must not open the pause menu as well
                _duelSteppedBack = true;
                CloseDuel();
                return false;
            }

            bool open = CoreDuelRules.ChoicesOpen(_duel, _duelPickedVersion, _duelPickedAt, Time.time);
            int pick = _duelScreen.ConsumePick();
            if (open && pick >= 0 && pick < _duel.ChoiceKeys.Count)
            {
                Game.Network?.SendCoreDialogueChoice(pick); // the server decides what the answer does
                _duelPickedVersion = _duel.Version;
                _duelPickedAt = Time.time;
                open = false;
            }

            PresentDuel(open);
            return true;
        }

        /// <summary>A queued breach scene (the pack's <see cref="StoryFinaleStages.Breach"/> binding) that may play now — the
        /// dialogue waits a frame for it ("now or never": it plays or drops at once).</summary>
        private bool BreachWaiting()
        {
            foreach (var item in _queue.Items)
            {
                if (item.Payload is Owed owed && owed.Pack?.Finale?.Cutscenes != null
                    && owed.Pack.Finale.Cutscenes.TryGetValue(StoryFinaleStages.Breach, out var breach)
                    && breach == item.DefinitionId)
                {
                    return true;
                }
            }

            return false;
        }

        private void OpenDuel()
        {
            _duelScreen = DuelDialogueScreen.Create("DuelFrame", FrameSortingOrder);
            _duelOpenedAt = Time.time;
            _duelLetterbox = 0f;
            _duelShownVersion = -1;
            _duelCamera = false;
            _duelCapped = false;
            _duelCameraTries = 0;
            _duelCameraRetryAt = 0f;
            _duelCameraSeconds = 0f;
            GameBootstrap.Cutscenes.Begin(DuelGateReason); // HUD, touch layer, chat and the controls step aside
            Game.SetCursorOwner(_duelCursorOwner, true);   // the answers are clickable
            if (CutsceneRules.DuelHoldsWorld(true, _inputs.Multiplayer))
            {
                Game.WorldHold.Hold(WorldHoldArbiter.Cutscene, Time.realtimeSinceStartup);
                _duelHolding = true;
            }

            StageDuelCamera();
            Debug.Log($"[Cutscene] Duel dialogue opened at node {_duel.Node} (camera={_duelCamera}, hold={_duelHolding}).");
        }

        private void CloseDuel()
        {
            if (_duelScreen == null)
            {
                return;
            }

            if (_duelCamera)
            {
                _rig.Release();
                _duelCamera = false;
            }

            GameBootstrap.Cutscenes.End(DuelGateReason);
            Game?.SetCursorOwner(_duelCursorOwner, false);
            if (_duelHolding)
            {
                _duelHolding = false;
                Game?.WorldHold.Release(WorldHoldArbiter.Cutscene);
            }

            _duelScreen.Destroy();
            _duelScreen = null;
            Debug.Log("[Cutscene] Duel dialogue closed.");
        }

        /// <summary>The camera close on the core column in its chamber (the close framing of <see cref="CutsceneStaging"/>);
        /// anything else keeps the player's own view — the player usually stands right in front of the core anyway.</summary>
        private void StageDuelCamera()
        {
            _duelCameraTries++;
            _duelCameraRetryAt = Time.time + DuelCameraRetrySeconds;
            var world = Game.World;
            if (Camera == null || world == null || !_duel.HasAnchor || Game.SpaceViewActive)
            {
                return;
            }

            CinematicStageScan.ClearSampler path = (x, y, z) => world.TryGetBlock(x, y, z, out var b) && b.IsAir;
            var game = Game;
            var terrain = path;
            CinematicStageScan.ClearSampler spot = (x, y, z) => terrain(x, y, z) && game.LandedShipBlockAt(x, y, z, out _, out _).IsAir;
            var staged = CutsceneStaging.Stage(CutsceneCameras.Static, path, spot, _duel.AnchorX, _duel.AnchorY, _duel.AnchorZ, 1f,
                Game.PlayerYaw, skyOpen: false, chartAvailable: false, closeUp: true, syntheticAllowed: false);
            if (!staged.CloseUp)
            {
                return;
            }

            _rig.Apply(staged, CutsceneStaging.ObjectLine(spot, _duel.AnchorX, _duel.AnchorY, _duel.AnchorZ), spot);
            _duelCamera = _rig.Holding;
        }

        private void PresentDuel(bool choicesOpen)
        {
            float dt = Time.deltaTime;
            _duelLetterbox = Mathf.MoveTowards(_duelLetterbox, 1f, dt / 0.6f);
            _duelScreen.SetLetterbox(CinematicTimeline.EaseInOut(_duelLetterbox));
            _duelScreen.SetFade(0.12f);

            if (_duelCamera)
            {
                _duelCameraSeconds += dt;
                if (SafeMomentGate.CameraCapReached(_inputs.Multiplayer, _duelCameraSeconds))
                {
                    _rig.Release(); // multiplayer: the camera goes back after 30 s — the dialogue stays
                    _duelCamera = false;
                    _duelCapped = true;
                }
            }
            else if (!_duelCapped && _duelCameraTries < DuelCameraTries && Time.time >= _duelCameraRetryAt)
            {
                StageDuelCamera();
            }

            if (_duel.Version != _duelShownVersion)
            {
                _duelShownVersion = _duel.Version;
                var speaker = CoreSpeaker();
                string words = CoreDuelRules.SpeakerText(_duel, L, reaction => "<i>" + reaction + "</i>");
                string progress = CoreDuelRules.ProgressText(_duel, L("ui.finale.duel_progress"));
                var choices = new List<string>();
                foreach (string key in _duel.ChoiceKeys)
                {
                    choices.Add(L(key));
                }

                _duelScreen.Show(speaker, progress, words, choices);
                if (!string.IsNullOrEmpty(speaker.LineStartCue))
                {
                    ClientAudio.Instance?.Cue(speaker.LineStartCue);
                }
            }

            _duelScreen.SetChoicesOpen(choicesOpen);
            _duelScreen.SetHint(DuelLeaveLabel(), _duelLetterbox >= 1f ? 0.75f : 0f);
        }

        /// <summary>The core as its box shows it: the world story's <c>core</c> speaker (name, colour, its hum), else the duel's
        /// title in Guardian red.</summary>
        private CutsceneSpeaker CoreSpeaker()
        {
            StoryDefinition pack = null;
            string storyId = Game?.Story?.StoryId;
            if (!string.IsNullOrEmpty(storyId))
            {
                Game.Content?.TryGetStory(storyId, out pack);
            }

            if (pack?.FindSpeaker(CoreSpeakerId) != null)
            {
                return SpeakerFor(pack, CoreSpeakerId);
            }

            return new CutsceneSpeaker { Name = L("ui.finale.duel_title"), Icon = null, Color = new Color(1f, 0.35f, 0.29f) };
        }

        /// <summary>"Step back" for the control in hand (like the scenes' skip label).</summary>
        private string DuelLeaveLabel() => InputMap.ActiveDevice switch
        {
            InputDeviceKind.Touch => L("ui.finale.duel_leave_touch"),
            InputDeviceKind.Gamepad => SafeFormat(L("ui.finale.duel_leave_pad"), InputMap.Glyph(InputAction.UiCancel)),
            _ => SafeFormat(L("ui.finale.duel_leave_key"), "Esc"), // UiCancel's keyboard key is locked to Escape
        };
    }
}
