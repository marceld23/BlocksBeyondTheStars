// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lift's floor picker (#3171, Lyxette 2026-11-04: "from up here I want to ride both up and down"): opens on E while
    /// the player stands on a lift platform, or on E at a lift stop where the platform waits. It lists the lift's floors with
    /// the highest at the top — the way the building stands — each with its name (the stop's label, #3172) or "Floor N", the
    /// floor the platform is at (or on its way to) marked. <b>Go</b> sends the platform there (the server checks reach and
    /// rights and says why it cannot); <b>Name</b> renames the floor's lift stop in the shared rename dialog and comes back
    /// here afterwards. The open panel follows the lift list: a new name, a new stop or the platform arriving rebuild it.
    /// Modal like <see cref="BeamPadUi"/>: pad-navigable, closes on Esc / pad B / the Close button.
    /// </summary>
    public sealed class LiftFloorUi : UiPanel
    {
        private const float W = 760f, H = 680f;

        private int _liftId;
        private string _shown = string.Empty;   // what the rows show (LiftRiding.Signature + the lift's name)
        private int _reopenLift;                // a floor was named: the picker comes back once the rename dialog is gone
        private int _reopenFrame;

        protected override string CanvasName => "LiftFloorUI";

        // pad: the stick walks the floors, A picks, B closes (#1198)
        protected override int CanvasSortingOrder => 59;

        /// <summary>Opens the picker of the lift with id <paramref name="liftId"/>.</summary>
        public void Open(int liftId)
        {
            if (Shown) Close(); // a second Open replaces the panel instead of stacking another overlay
            EnsureCanvas();
            _liftId = liftId;
            _reopenLift = 0;
            ShowCanvas();
            Build();
            PlayOpenEffect(); // once per open, never from a rebuild (#2302)
            SetMenuOwned(true);
        }

        private void Update()
        {
            if (!Shown)
            {
                ReopenAfterRename();
                return;
            }

            if (CancelPressed())
            {
                ConsumeMenuInput();
                Close();
                return;
            }

            var lift = Find();
            if (lift == null)
            {
                Close(); // the motor was mined (or the world changed) — nothing left to choose
                return;
            }

            if (Shows(lift) != _shown)
            {
                DestroyOverlay();
                Build();
            }
        }

        /// <summary>The rename dialog confirmed a floor's name: open the picker again once that dialog is closed (a frame
        /// later, so the key that confirmed it never also clicks a floor).</summary>
        private void ReopenAfterRename()
        {
            if (_reopenLift == 0 || Time.frameCount < _reopenFrame || (Game?.Context.Panel<BeaconLabelUi>()?.IsOpen ?? false))
            {
                return;
            }

            int id = _reopenLift;
            _reopenLift = 0;
            if (FindById(id) != null)
            {
                Open(id);
            }
        }

        private void Close()
        {
            DestroyOverlay();
            HideCanvas();
            SetMenuOwned(false);
        }

        private NetLift Find() => FindById(_liftId);

        private NetLift FindById(int id)
        {
            var lifts = Game?.Lifts;
            if (lifts == null) return null;
            foreach (var l in lifts)
            {
                if (l != null && l.Id == id) return l;
            }

            return null;
        }

        /// <summary>The lift's own name: the motor's label (#3172), else "Lift".</summary>
        private string LiftName(NetLift lift)
        {
            string label = lift != null ? Game?.CrystalDeviceAt(lift.X, lift.Y, lift.Z)?.Label : null;
            return string.IsNullOrEmpty(label) ? L("ui.lift.title") : label;
        }

        private string Shows(NetLift lift) => LiftName(lift) + "#" + LiftRiding.Signature(lift);

        /// <summary>Whether the local player may rename the lift's floors — the motor's owner or an ally (a world circuit's
        /// lift is the admin's to name; the server checks either way).</summary>
        private bool MayName(NetLift lift)
        {
            var motor = Game?.CrystalDeviceAt(lift.X, lift.Y, lift.Z);
            return motor == null || (Game.CanOperateCrystal(motor) && motor.OwnerId != CrystalNetRules.WorldOwnerId);
        }

        private void Build()
        {
            var lift = Find();
            _shown = Shows(lift);
            var panel = AddModal((1920f - W) * 0.5f, (1080f - H) * 0.5f, W, H);
            var head = UiKit.AddText(panel, 32f, 24f, W - 64f, 40f, LiftName(lift), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            UiKit.AddText(panel, 32f, 66f, W - 64f, 26f, L("ui.lift.choose"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            var list = UiKit.ScrollList(panel, 24f, 104f, W - 48f, H - 104f - 90f, 6f);
            var floors = LiftRiding.Floors(lift);
            if (floors.Count <= 1)
            {
                Row(list, 64f, go => UiKit.AddText(go.transform, 12f, 8f, W - 100f, 48f, L("ui.lift.no_floors"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap);
            }

            bool mayName = lift != null && MayName(lift);
            for (int i = floors.Count - 1; i >= 0; i--) // the highest floor at the top, like the building
            {
                var floor = floors[i];
                Row(list, 64f, go => FloorRow(go.transform, lift, floor, mayName));
            }

            UiKit.AddButton(panel, W - 32f - 220f, H - 72f, 220f, 48f, L("ui.crystal.close"), () =>
            {
                ConsumeMenuInput();
                Close();
            });
        }

        /// <summary>One floor: its name and where the platform is, Go (lit where the platform waits), and Name for a floor a
        /// lift stop stands on.</summary>
        private void FloorRow(Transform row, NetLift lift, LiftFloor floor, bool mayName)
        {
            string number = string.Format(L("ui.lift.floor"), floor.Number);
            string title = string.IsNullOrEmpty(floor.Name) ? number : floor.Name;
            string sub = floor.Here ? L("ui.lift.here") : floor.Heading ? L("ui.lift.heading") : string.Empty;
            if (!string.IsNullOrEmpty(floor.Name))
            {
                sub = sub.Length > 0 ? number + "  ·  " + sub : number;
            }

            UiKit.AddText(row, 12f, 4f, 380f, 30f, title, 20, floor.Here ? UiKit.Cyan : UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(row, 12f, 34f, 380f, 24f, sub, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);

            var go = UiKit.AddButton(row, 400f, 8f, 150f, 48f, floor.Here ? L("ui.lift.here_short") : L("ui.lift.go"), () =>
            {
                if (!floor.Here)
                {
                    Game?.SendCrystalDevice(lift.X, lift.Y, lift.Z, 5, floor.Level); // #3171: the server checks reach and rights
                    ClientAudio.Instance?.Cue("ui_confirm");
                }

                ConsumeMenuInput();
                Close();
            });
            if (floor.Here)
            {
                var img = go.GetComponent<Image>();
                if (img != null) img.color = UiKit.Cyan;
            }

            if (floor.HasStop && mayName)
            {
                UiKit.AddButton(row, 562f, 8f, 140f, 48f, L("ui.lift.rename"), () => RenameFloor(lift, floor));
            }
        }

        /// <summary>Names a floor in the shared rename dialog; the picker comes back once the name is confirmed. An empty name
        /// clears it (the floor is "Floor N" again).</summary>
        private void RenameFloor(NetLift lift, LiftFloor floor)
        {
            int liftId = lift.Id, x = lift.X, y = lift.Y, z = lift.Z, level = floor.Level;
            ConsumeMenuInput();
            Close();
            Game?.Context.Panel<BeaconLabelUi>()?.Open(L("ui.lift.rename_prompt"), floor.Name, name =>
            {
                Game?.SendCrystalDevice(x, y, z, 6, level, string.Empty, name ?? string.Empty); // #3171 / #3172
                _reopenLift = liftId;
                _reopenFrame = Time.frameCount + 1;
            });
        }

        private static void Row(RectTransform list, float height, System.Action<GameObject> fill)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(list, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            fill(go);
        }
    }
}
