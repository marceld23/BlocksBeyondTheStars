// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The cab's panel (#2113): the speed setting (#3444: a top speed; the state line shows the speed right now),
    /// halt / go, the autopilot switch, whether villagers ride along (#3446), packing the train up and leaving it. Opened
    /// with the interact key inside the cab;
    /// every button sends one intent and the panel re-reads the train from the server's list, so what it shows is always
    /// the server's state.</summary>
    public sealed class TrainCabUi : UiPanel
    {
        private const float W = 640f, H = 480f;

        private string _trainId = string.Empty;
        private float _nextRefresh;

        protected override string CanvasName => "TrainCabUI";

        protected override int CanvasSortingOrder => 59;

        /// <summary>
        /// #2303: the cab panel's canvas is a top-level object of its own, so it outlived the world rig and every world
        /// join left one more hidden canvas behind — and the old static instance pointed at a destroyed panel. The same
        /// treatment as <c>FeedbackUi</c> (#1789): give back the menu if it was open; the base drops the registration and
        /// the canvas.
        /// </summary>
        protected override void OnDestroy()
        {
            if (Shown)
            {
                Shown = false;
                SetMenuOwned(false);
            }

            base.OnDestroy();
        }

        public void Open(string trainId)
        {
            if (string.IsNullOrEmpty(trainId)) return;
            if (Shown) Close(); // #2303: a second Open replaces the panel instead of stacking another overlay over it
            EnsureCanvas();
            _trainId = trainId;
            ShowCanvas();
            Build();
            // #2302: the open effect — only here, never in the half-second Rebuild(); that refresh waits its half second
            // first, so it cannot swap the fading overlay for a new one on the next frame (the panel was just built).
            PlayOpenEffect();
            _nextRefresh = Time.time + 0.5f;
            SetMenuOwned(true);
        }

        private void Update()
        {
            if (!Shown) return;
            if (CancelPressed())
            {
                ConsumeMenuInput();
                Close();
                return;
            }

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + 0.5f;
                if (Find() == null)
                {
                    Close(); // stowed under us
                    return;
                }

                Rebuild();
            }
        }

        private void Close()
        {
            DestroyOverlay();
            HideCanvas();
            SetMenuOwned(false);
        }

        private NetTrain Find()
        {
            if (Game?.Trains == null) return null;
            foreach (var t in Game.Trains)
            {
                if (t != null && t.Id == _trainId) return t;
            }

            return null;
        }

        private void Rebuild()
        {
            DestroyOverlay();
            Build();
        }

        private void Build()
        {
            var t = Find();
            var panel = AddModal((1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            var head = UiKit.AddText(panel, 32f, 24f, W - 64f, 40f, L("ui.train.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            if (t == null)
            {
                return;
            }

            UiKit.AddText(panel, 32f, 66f, W - 64f, 26f, StateText(t), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // #2125: the intercity line's public train (no owner) runs its own timetable — no controls, only the way out.
            if (string.IsNullOrEmpty(t.OwnerId))
            {
                head.text = L("ui.train.public_title");
                var note = UiKit.AddText(panel, 32f, 110f, W - 64f, 150f, L("ui.train.public"), 18, Color.white, TextAnchor.UpperLeft);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(panel, 32f, 300f, 260f, 44f, L("ui.train.leave"), () => { Game?.Network?.SendExitTrain(); Close(); });
                return;
            }

            // #3444: the three settings are top speeds (8 / 16 / 24 blocks/s): the train eases in, cruises by the distance to
            // its next stop and brakes into it, never faster than the chosen one.
            float y = 104f;
            UiKit.AddText(panel, 32f, y, W - 64f, 26f, L("ui.train.top_speed"), 18, Color.white, TextAnchor.MiddleLeft);
            y += 32f;
            for (int s = 1; s <= 3; s++)
            {
                int speed = s;
                string label = (t.Speed == s ? "● " : string.Empty) + Mathf.RoundToInt(RailMotion.TopSpeed(s));
                UiKit.AddButton(panel, 32f + (s - 1) * 196f, y, 180f, 40f, label, () => Game?.Network?.SendSetTrain(_trainId, speed: speed));
            }

            y += 60f;
            UiKit.AddButton(panel, 32f, y, 260f, 44f, t.Halted ? L("ui.train.go") : L("ui.train.halt"),
                () => Game?.Network?.SendSetTrain(_trainId, halt: t.Halted ? 0 : 1));
            UiKit.AddButton(panel, 316f, y, 292f, 44f, t.Autopilot ? L("ui.train.autopilot_on") : L("ui.train.autopilot_off"),
                () => Game?.Network?.SendSetTrain(_trainId, autopilot: t.Autopilot ? 0 : 1));

            // #3446: villagers ride along between the towns on the line (and come to visit by train) — the owner's choice.
            y += 64f;
            UiKit.AddButton(panel, 32f, y, W - 64f, 44f, t.TakesPassengers ? L("ui.train.passengers_on") : L("ui.train.passengers_off"),
                () => Game?.Network?.SendSetTrain(_trainId, passengers: t.TakesPassengers ? 0 : 1));

            y += 70f;
            UiKit.AddButton(panel, 32f, y, 260f, 44f, L("ui.train.leave"), () => { Game?.Network?.SendExitTrain(); Close(); });
            if (t.OwnerId == Game?.LocalPlayerId)
            {
                UiKit.AddButton(panel, 316f, y, 292f, 44f, L("ui.train.stow"), () => { Game?.Network?.SendStowTrain(_trainId); Close(); });
            }
        }

        /// <summary>The state line: halted (at a stop with the seconds left), or running — with the speed it runs at right
        /// now (#3444, the server's velocity; an older server's train at its setting's old fixed speed).</summary>
        private string StateText(NetTrain t)
        {
            if (t.Halted)
            {
                return t.HaltRemaining > 0f ? string.Format(L("ui.train.state_stop"), Mathf.CeilToInt(t.HaltRemaining)) : L("ui.train.state_halted");
            }

            int now = Mathf.RoundToInt(TrainDrawArc.PredictSpeed(t.Velocity, t.Halted, t.Autopilot, t.Speed));
            return now > 0 ? string.Format(L("ui.train.state_running_at"), now) : L("ui.train.state_running");
        }
    }
}
