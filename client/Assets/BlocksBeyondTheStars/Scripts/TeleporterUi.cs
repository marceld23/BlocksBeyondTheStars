// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The teleporter's destination picker (#1056, #2994, #2995): opens on right-click / the place action with a held
    /// <c>suit_teleporter</c> or <c>quantum_teleporter</c>. Row one is always "back to ship" (the recall the device has
    /// always done); below it one row per <b>allied</b> player standing on this body — the roster from the alliance tab
    /// crossed with the star map's online-player locations — and then the fixed places of this body: own and allied bases,
    /// beam pads and radio beacons, and the map markers (own and shared), nearest first (<c>TeleporterUi.Places.cs</c>).
    /// The Quantum Teleporter adds typed coordinates and the map waypoint. Modal like <see cref="BeamPadUi"/> (cursor freed,
    /// on-foot control paused), stick-navigable for pads (<see cref="UiNav"/>), Esc / pad B closes. The server re-validates
    /// every choice: device, energy, cooldown, the alliance, same body, target not aboard their ship.
    /// </summary>
    public sealed partial class TeleporterUi : UiPanel
    {
        /// <summary>Remote avatars, for the distance line (set by WorldRig; null-safe).</summary>
        public RemotePlayers Remotes;

        private RectTransform _list;
        private Text _title;
        private bool _subscribed;
        private bool _quantum;
        private bool _rebuildPending; // a roster update that arrived while a coordinate was being typed
        private readonly List<GameObject> _rows = new List<GameObject>();

        protected override string CanvasName => "TeleporterUI";

        // pad: stick walks the rows, A beams, B closes
        protected override int CanvasSortingOrder => 59; // same layer as the beam-pad transporter: above HUD/chat, below the world map

        /// <summary>Opens the picker. Asks the server for a fresh roster + star map so the ally list is current
        /// (both replies rebuild the rows while the panel is open). <paramref name="quantum"/>: the held device is the
        /// Quantum Teleporter — the coordinate and waypoint rows are offered too (#2995).</summary>
        public void Open(bool quantum = false)
        {
            _quantum = quantum;
            EnsureCanvas();
            _title.text = L(quantum ? "ui.tp.title_quantum" : "ui.tp.title");
            ShowCanvas();
            RebuildList();

            Game?.Network?.SendRequestAllianceList();
            Game?.Network?.SendRequestStarMap();
            SetMenuOwned(true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.AllianceListReceived += _ => RebuildWhenFree();
                Game.Network.StarMapReceived += _ => RebuildWhenFree();
                _subscribed = true;
            }

            if (!Shown) return;

            if (_rebuildPending && !TypingCoordinates)
            {
                _rebuildPending = false;
                RebuildList();
            }

            // Esc always closes; pad B backs out (the list is stick-navigable, so the pad needs an exit).
            if (CancelPressed())
            {
                ConsumeMenuInput(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        /// <summary>Rebuilds the rows on a roster update — later when the player is typing a coordinate (a rebuild would take the
        /// field's focus away mid-number).</summary>
        private void RebuildWhenFree()
        {
            if (!Shown)
            {
                return;
            }

            if (TypingCoordinates)
            {
                _rebuildPending = true;
                return;
            }

            RebuildList();
        }

        private void Close()
        {
            HideCanvas();
            SetMenuOwned(false); // arbiter re-locks only once NO other panel is open (#413)
        }

        /// <summary>Allied players currently on the same body as the local player, by name (== player id).</summary>
        private List<string> AlliesHere()
        {
            var result = new List<string>();
            var allies = Game?.Alliances?.Allies;
            var players = Game?.StarMap?.Players;
            string here = Game?.StarMap?.ActiveLocationId;
            string me = Game?.LocalPlayerId;
            if (allies == null || players == null || string.IsNullOrEmpty(here)) return result;

            var allied = new HashSet<string>();
            foreach (var a in allies)
            {
                if (a.Online && !string.IsNullOrEmpty(a.PartnerId)) allied.Add(a.PartnerId);
            }

            var seen = new HashSet<string>();
            foreach (var p in players)
            {
                if (string.IsNullOrEmpty(p.Name) || p.Name == me || p.LocationId != here) continue;
                if (allied.Contains(p.Name) && seen.Add(p.Name)) result.Add(p.Name);
            }

            result.Sort(System.StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Scene distance (m) to a remote avatar, or -1 when it isn't streamed in (too far to see).</summary>
        private float DistanceTo(string name)
        {
            var remotes = Remotes;
            if (remotes == null || Game == null) return -1f;
            foreach (var c in remotes.Contacts())
            {
                if (c.Name == name) return (c.Scene - Game.PlayerPosition).magnitude;
            }

            return -1f;
        }

        private void RebuildList()
        {
            foreach (var go in _rows) Destroy(go);
            _rows.Clear();

            // Row 1: back to the ship — the recall the device has always done.
            AddRowGo(56f, go =>
            {
                UiKit.AddText(go.transform, 12f, 4f, 380f, 28f, L("ui.tp.to_ship"), 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(go.transform, 12f, 30f, 380f, 22f, L("ui.tp.to_ship_hint"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddButton(go.transform, 404f, 8f, 150f, 40f, L("ui.tp.beam_button"), () =>
                {
                    Game.Network?.SendTeleportToShip();
                    Close();
                });
            });

            if (_quantum)
            {
                AddQuantumRows(); // #2995: typed coordinates and the map waypoint, right under the ship
            }

            AddAllyRows();
            AddPlaceRows(); // #2994: bases, beam pads, beacons, markers of this body
        }

        private void AddAllyRows()
        {
            AddHeader(L("ui.tp.allies_here"));
            var allies = AlliesHere();
            if (allies.Count == 0)
            {
                AddRowGo(40f, go => UiKit.AddText(go.transform, 12f, 4f, 540f, 32f, L("ui.tp.no_allies"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft));
                return;
            }

            foreach (var name in allies)
            {
                string id = name; // capture (player id == display name)
                float dist = DistanceTo(name);
                string sub = dist >= 0f
                    ? L("ui.beam.distance").Replace("{0}", Mathf.RoundToInt(dist).ToString())
                    : L("ui.tp.out_of_sight");
                AddBeamRow(name, sub, () => Game.Network?.SendTeleportToPlayer(id));
            }
        }

        private void AddHeader(string text)
            => AddRowGo(30f, go => UiKit.AddText(go.transform, 12f, 4f, 520f, 24f, text, 16, UiKit.CyanDim, TextAnchor.MiddleLeft));

        /// <summary>One destination row: a bold name, a sub line (distance, owner, coordinates) and the Beam button.</summary>
        private void AddBeamRow(string title, string sub, System.Action beam)
        {
            AddRowGo(56f, go =>
            {
                UiKit.AddText(go.transform, 12f, 4f, 380f, 28f, title, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(go.transform, 12f, 30f, 380f, 22f, sub, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.AddButton(go.transform, 404f, 8f, 150f, 40f, L("ui.tp.beam_button"), () =>
                {
                    beam();
                    Close();
                });
            });
        }

        /// <summary>Adds a fixed-height row to the scrollable list and lets the caller fill it in.</summary>
        private void AddRowGo(float height, System.Action<GameObject> fill)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_list, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            fill(go);
            _rows.Add(go);
        }

        protected override void BuildChrome(Transform root)
        {
            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            const float w = 620f, h = 760f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            _title = UiKit.AddText(root, x + 24, y + 20, w - 48, 32, L("ui.tp.title"), 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;

            UiKit.AddText(root, x + 24, y + 58, w - 48, 24, L("ui.tp.destinations"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            // #2994: the cost label reads the server's own numbers (it was hard-coded beside them).
            UiKit.AddText(root, x + 24, y + 58, w - 48, 24,
                L("ui.beam.cost").Replace("{0}", ((int)TeleportRules.SuitEnergyCost).ToString())
                    .Replace("{1}", ((int)TeleportRules.SuitCooldownSeconds).ToString()), 14, UiKit.CyanDim, TextAnchor.MiddleRight);

            _list = UiKit.ScrollList(root, x + 16, y + 90, w - 32, h - 168, 6f);

            UiKit.AddButton(root, x + w - 24 - 220, y + h - 60, 220, 44, L("ui.beam.close"), Close);
        }
    }
}
