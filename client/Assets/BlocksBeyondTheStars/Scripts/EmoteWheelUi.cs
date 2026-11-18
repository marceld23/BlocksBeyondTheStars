// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The emote wheel: nine things the player's figure can do — wave, nod, cheer, point, shake the head, dance,
    /// clap, sit down, bow — on a ring like the hotbar's (<see cref="UiWheel"/>). On the keyboard the key is HELD:
    /// the wheel shows while it is down, the pointer picks a wedge, letting go plays it. A short TAP of the key
    /// instead leaves the wheel open for a click, so the key need not sit under the left hand (letting go in the middle
    /// plays nothing). The pad and the tablet open it from the action list; there it stays open until a wedge is
    /// chosen or it is closed. The emote goes to the server, which tells the players nearby; the own figure plays it
    /// at once. A fixed list — nothing can be typed, so nothing needs moderating.
    /// </summary>
    public sealed class EmoteWheelUi : UiPanel
    {
        private static readonly (byte Emote, string Key)[] Emotes =
        {
            (PlayerEmotes.Wave, "ui.emote.wave"),
            (PlayerEmotes.Cheer, "ui.emote.cheer"),
            (PlayerEmotes.Clap, "ui.emote.clap"),
            (PlayerEmotes.Dance, "ui.emote.dance"),
            (PlayerEmotes.Sit, "ui.emote.sit"),
            (PlayerEmotes.Bow, "ui.emote.bow"),
            (PlayerEmotes.HeadShake, "ui.emote.head_shake"),
            (PlayerEmotes.Nod, "ui.emote.nod"),
            (PlayerEmotes.Point, "ui.emote.point"),
        };

        /// <summary>A key let go this soon, with no wedge under the pointer, was a tap: the wheel stays open, seconds.</summary>
        private const float TapSeconds = 0.3f;

        /// <summary>The shortest time between two emotes, seconds. The server drops one that comes sooner than 0.8 s after
        /// the last (its <c>EmoteCooldownSeconds</c>); a pick inside the pace is ignored here as well, so the own figure
        /// never plays an emote the others did not get. A little over the server's number: the two clocks do not see the
        /// gap between two messages alike (the network's jitter, the server's tick).</summary>
        private const float PaceSeconds = 0.9f;

        private float _playedAt = float.NegativeInfinity; // when the last emote went out (the world clock: it stands still with the server's)
        private bool _holdMode;
        private int _hover = -1;
        private float _openedAt;
        private GameObject _closeButton;

        protected override string CanvasName => "EmoteWheelUi";

        protected override int CanvasSortingOrder => 40; // like the hotbar's ring: above the HUD, below the menus

        /// <summary>True while the wheel is up (gameplay hotkeys stand down).</summary>
        public override bool IsOpen => IsBuilt;

        /// <summary>On foot, alive, with no menu or scene in the way.</summary>
        public bool CanOpen
            => Game != null && !Game.MenuOpen && !GameBootstrap.CutsceneActive && !Game.SpaceViewActive && !Game.InSpace
               && string.IsNullOrEmpty(Game.InSpeeder) && !Game.Spectating && Game.Health > 0f;

        protected override void OnDestroy()
        {
            Close();
            base.OnDestroy();
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!IsOpen)
            {
                if (InputMap.Down(InputAction.EmoteWheel) && CanOpen)
                {
                    // A held key is the keyboard's way; a press that is not held came from the action list (pad, touch).
                    Open(hold: InputMap.Held(InputAction.EmoteWheel));
                }

                return;
            }

            if (CancelPressed(skipOpenFrame: false))
            {
                ConsumeMenuInput();
                Close();
            }
            else if (_holdMode && !InputMap.Held(InputAction.EmoteWheel))
            {
                Released();
            }
            else if (!_holdMode && InputMap.Down(InputAction.EmoteWheel))
            {
                ConsumeMenuInput();
                Close();
            }
        }

        /// <summary>The held key was let go: over a wedge it plays that emote; a quick tap with nothing picked turns the
        /// wheel into the stay-open kind (click a wedge, or the middle to close); otherwise it just closes.</summary>
        private void Released()
        {
            int pick = _hover;
            if (pick < 0 && Time.unscaledTime - _openedAt < TapSeconds)
            {
                _holdMode = false;
                if (_closeButton != null)
                {
                    _closeButton.SetActive(true);
                }

                return;
            }

            Close();
            if (pick >= 0)
            {
                Play(pick);
            }
        }

        private void Open(bool hold)
        {
            _holdMode = hold;
            _hover = -1;
            _openedAt = Time.unscaledTime;
            CreateCanvasNow();
            SetMenuOwned(true); // frees the cursor and holds the controls while the wheel is up
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);

            var dim = UiKit.AddModalDim(Canvas.transform, 0.45f); // light: the world stays readable behind the ring
            var t = dim.transform;
            var head = UiKit.AddText(t, 640f, 540f - (UiWheel.Diameter / 2f) - 54f, 640f, 34f, L("ui.emote.title"), 24, UiKit.Cyan,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddOutline(head);

            var wedges = new List<Button>(Emotes.Length);
            for (int i = 0; i < Emotes.Length; i++)
            {
                int index = i;
                wedges.Add(UiWheel.AddWedge(t, i, Emotes.Length, L(Emotes[i].Key),
                    () => { Close(); Play(index); },
                    over => _hover = over ? index : (_hover == index ? -1 : _hover)));
            }

            UiWheel.WireRing(wedges);
            // The pad, the tablet and a tapped key have no key to let go of: a way out in the middle (hidden while held).
            _closeButton = UiKit.AddButton(t, 960f - 70f, 540f - 24f, 140f, 48f, L("ui.hotbar_action.close"), Close).gameObject;
            _closeButton.SetActive(!hold);

            UiKit.TransitionIn(dim.gameObject, 0f);
        }

        private void Close()
        {
            if (IsBuilt)
            {
                DestroyCanvas();
                SetMenuOwned(false);
            }

            _hover = -1;
        }

        private void Play(int index)
        {
            if (Game.WorldTime - _playedAt < PaceSeconds)
            {
                return; // the server would drop it: the own figure must not show what the others do not get
            }

            _playedAt = Game.WorldTime;
            byte emote = Emotes[index].Emote;
            var avatar = FindAnyObjectByType<PlayerController>()?.Avatar;
            if (emote == PlayerEmotes.Sit && avatar != null && avatar.EmoteSitting)
            {
                emote = PlayerEmotes.Stand; // "sit" only ever sits down (a switch could arrive inverted): getting up is said outright
            }

            Game.Network?.SendEmote(emote);
            if (avatar != null)
            {
                avatar.PlayEmote(emote); // the own figure at once — seen in third person
            }

            EmoteSounds.Play(emote);
            Game.ShowMessage(L(Emotes[index].Key)); // first person shows no body: the status line says what the figure does
        }
    }

    /// <summary>The small sound each emote makes. The clips are optional: an emote without one plays silently.</summary>
    internal static class EmoteSounds
    {
        public static string Cue(byte emote) => emote switch
        {
            PlayerEmotes.Wave => "emote_wave",
            PlayerEmotes.Nod => "emote_nod",
            PlayerEmotes.Cheer => "emote_cheer",
            PlayerEmotes.Point => "emote_point",
            PlayerEmotes.HeadShake => "emote_head_shake",
            PlayerEmotes.Dance => "emote_dance",
            PlayerEmotes.Clap => "emote_clap",
            PlayerEmotes.Sit => "emote_sit",
            PlayerEmotes.Stand => "emote_sit", // getting up sounds like sitting down, as it did while Sit was a switch
            PlayerEmotes.Bow => "emote_bow",
            _ => null,
        };

        /// <summary>The own emote: flat, at the listener.</summary>
        public static void Play(byte emote)
        {
            string cue = Cue(emote);
            if (cue != null)
            {
                ClientAudio.Instance?.Cue(cue, 0.7f);
            }
        }

        /// <summary>Someone else's emote: where their figure stands.</summary>
        public static void PlayAt(byte emote, Vector3 at)
        {
            string cue = Cue(emote);
            if (cue != null)
            {
                ClientAudio.Instance?.At(cue, at, 1f, 0.8f);
            }
        }
    }
}
