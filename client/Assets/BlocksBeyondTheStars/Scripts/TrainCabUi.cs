// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The cab's panel (#2113): the speed setting, halt / go, the autopilot switch, packing the train up and leaving
    /// it. Opened with the interact key inside the cab; every button sends one intent and the panel re-reads the train
    /// from the server's list, so what it shows is always the server's state.</summary>
    public sealed class TrainCabUi : MonoBehaviour
    {
        public static TrainCabUi Instance { get; private set; }
        public GameBootstrap Game;

        private const float W = 640f, H = 420f;

        private Canvas _canvas;
        private GameObject _overlay;
        private string _trainId = string.Empty;
        private bool _open;
        private int _openFrame;
        private float _nextRefresh;

        private void Awake() => Instance = this;

        public bool IsOpen => _open;

        public void Open(string trainId)
        {
            if (string.IsNullOrEmpty(trainId)) return;
            EnsureCanvas();
            _trainId = trainId;
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            Build();
            Game?.SetMenuOwner(this, true);
        }

        private void Update()
        {
            if (!_open) return;
            if (Time.frameCount != _openFrame && InputMap.Down(InputAction.UiCancel))
            {
                Game?.MarkMenuInputHandled();
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
            _open = false;
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Game?.SetMenuOwner(this, false);
        }

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            _canvas = UiKit.CreateCanvas("TrainCabUI");
            _canvas.sortingOrder = 59;
            UiNav.Enable(_canvas.gameObject);
            _canvas.gameObject.SetActive(false);
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

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void Rebuild()
        {
            if (_overlay != null) Destroy(_overlay);
            Build();
        }

        private void Build()
        {
            var t = Find();
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            _overlay = overlay;
            var head = UiKit.AddText(panel, 32f, 24f, W - 64f, 40f, L("ui.train.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            if (t == null)
            {
                return;
            }

            string state = t.Halted
                ? (t.HaltRemaining > 0f ? string.Format(L("ui.train.state_stop"), Mathf.CeilToInt(t.HaltRemaining)) : L("ui.train.state_halted"))
                : L("ui.train.state_running");
            UiKit.AddText(panel, 32f, 66f, W - 64f, 26f, state, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            float y = 110f;
            UiKit.AddText(panel, 32f, y, 160f, 40f, L("ui.train.speed"), 18, Color.white, TextAnchor.MiddleLeft);
            for (int s = 1; s <= 3; s++)
            {
                int speed = s;
                var b = UiKit.AddButton(panel, 200f + (s - 1) * 100f, y, 88f, 40f, (t.Speed == s ? "● " : string.Empty) + s, () => Game?.Network?.SendSetTrain(_trainId, speed: speed));
                _ = b;
            }

            y += 60f;
            UiKit.AddButton(panel, 32f, y, 260f, 44f, t.Halted ? L("ui.train.go") : L("ui.train.halt"),
                () => Game?.Network?.SendSetTrain(_trainId, halt: t.Halted ? 0 : 1));
            UiKit.AddButton(panel, 316f, y, 292f, 44f, t.Autopilot ? L("ui.train.autopilot_on") : L("ui.train.autopilot_off"),
                () => Game?.Network?.SendSetTrain(_trainId, autopilot: t.Autopilot ? 0 : 1));

            y += 70f;
            UiKit.AddButton(panel, 32f, y, 260f, 44f, L("ui.train.leave"), () => { Game?.Network?.SendExitTrain(); Close(); });
            if (t.OwnerId == Game?.LocalPlayerId)
            {
                UiKit.AddButton(panel, 316f, y, 292f, 44f, L("ui.train.stow"), () => { Game?.Network?.SendStowTrain(_trainId); Close(); });
            }
        }
    }
}
