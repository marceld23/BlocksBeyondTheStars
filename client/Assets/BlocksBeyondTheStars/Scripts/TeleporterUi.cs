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
    /// The suit teleporter's destination picker (#1056): opens on right-click / the place action with the
    /// held <c>suit_teleporter</c>. Row one is always "back to ship" (the recall the device has always done);
    /// below it one row per <b>allied</b> player standing on this body — the roster from the alliance tab
    /// crossed with the star map's online-player locations — with the distance when their avatar is in view.
    /// Modal like <see cref="BeamPadUi"/> (cursor freed, on-foot control paused), stick-navigable for pads
    /// (<see cref="UiNav"/>), Esc / pad B closes. The server re-validates every choice: device, energy,
    /// cooldown, the alliance, same body, target not aboard their ship.
    /// </summary>
    public sealed class TeleporterUi : MonoBehaviour
    {
        public static TeleporterUi Instance { get; private set; }
        public GameBootstrap Game;
        /// <summary>Remote avatars, for the distance line (set by WorldRig; null-safe).</summary>
        public RemotePlayers Remotes;

        private Canvas _canvas;
        private RectTransform _list;
        private bool _open, _built, _subscribed;
        private int _openFrame = -1;
        private readonly List<GameObject> _rows = new List<GameObject>();

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        public bool IsOpen => _open;

        /// <summary>Opens the picker. Asks the server for a fresh roster + star map so the ally list is current
        /// (both replies rebuild the rows while the panel is open).</summary>
        public void Open()
        {
            EnsureBuilt();
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            RebuildList();

            Game?.Network?.SendRequestAllianceList();
            Game?.Network?.SendRequestStarMap();
            Game?.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.AllianceListReceived += _ => { if (_open) RebuildList(); };
                Game.Network.StarMapReceived += _ => { if (_open) RebuildList(); };
                _subscribed = true;
            }

            if (!_open) return;

            // Esc always closes; pad B backs out (the list is stick-navigable, so the pad needs an exit).
            if (Time.frameCount != _openFrame && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)))
            {
                Game?.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Close()
        {
            _open = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            Game?.SetMenuOwner(this, false); // arbiter re-locks only once NO other panel is open (#413)
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

            AddRowGo(30f, go => UiKit.AddText(go.transform, 12f, 4f, 520f, 24f, L("ui.tp.allies_here"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft));

            var allies = AlliesHere();
            if (allies.Count == 0)
            {
                AddRowGo(64f, go => UiKit.AddText(go.transform, 12f, 8f, 540f, 48f, L("ui.tp.no_allies"), 17, UiKit.CyanDim, TextAnchor.MiddleLeft));
                return;
            }

            foreach (var name in allies)
            {
                string id = name; // capture (player id == display name)
                float dist = DistanceTo(name);
                string sub = dist >= 0f
                    ? L("ui.beam.distance").Replace("{0}", Mathf.RoundToInt(dist).ToString())
                    : L("ui.tp.out_of_sight");
                AddRowGo(56f, go =>
                {
                    UiKit.AddText(go.transform, 12f, 4f, 380f, 28f, name, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.AddText(go.transform, 12f, 30f, 380f, 22f, sub, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    UiKit.AddButton(go.transform, 404f, 8f, 150f, 40f, L("ui.tp.beam_button"), () =>
                    {
                        Game.Network?.SendTeleportToPlayer(id);
                        Close();
                    });
                });
            }
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

        private void EnsureBuilt()
        {
            if (_built) return;

            _canvas = UiKit.CreateCanvas("TeleporterUI");
            _canvas.sortingOrder = 59; // same layer as the beam-pad transporter: above HUD/chat, below the world map
            var root = _canvas.transform;

            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            const float w = 620f, h = 560f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            var title = UiKit.AddText(root, x + 24, y + 20, w - 48, 32, L("ui.tp.title"), 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;

            UiKit.AddText(root, x + 24, y + 58, w - 48, 24, L("ui.tp.destinations"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(root, x + 24, y + 58, w - 48, 24,
                L("ui.beam.cost").Replace("{0}", "10").Replace("{1}", "30"), 14, UiKit.CyanDim, TextAnchor.MiddleRight);

            _list = UiKit.ScrollList(root, x + 16, y + 90, w - 32, h - 168, 6f);

            UiKit.AddButton(root, x + w - 24 - 220, y + h - 60, 220, 44, L("ui.beam.close"), Close);

            UiNav.Enable(_canvas.gameObject); // pad: stick walks the rows, A beams, B closes
            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
