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
    /// The transporter panel for beam blocks: opens when the player presses E on a beam pad they may use. Lists the
    /// destination pads on this world (the player's own + allied), each with its name, coordinates and distance, and
    /// a Beam button that asks the server to teleport. The owner also gets a Rename button (which reuses the beacon
    /// label overlay). Modal like <see cref="BeaconLabelUi"/> — frees the cursor and pauses on-foot control.
    /// </summary>
    public sealed class BeamPadUi : MonoBehaviour
    {
        public static BeamPadUi Instance { get; private set; }
        public GameBootstrap Game;

        private Canvas _canvas;
        private Text _title;
        private RectTransform _list;
        private int _sourceId;
        private bool _open, _built;
        private int _openFrame = -1;
        private readonly List<GameObject> _rows = new List<GameObject>();

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        public bool IsOpen => _open;

        /// <summary>Opens the transporter for the pad with id <paramref name="sourceId"/> (the one the player is at).</summary>
        public void Open(int sourceId)
        {
            EnsureBuilt();
            _sourceId = sourceId;
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);
            RebuildList();

            if (Game != null) Game.MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (!_open) return;

            if (Time.frameCount != _openFrame && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }
        }

        private void Close()
        {
            _open = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (Game != null) Game.MenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private NetBeam Find(int id)
        {
            if (Game?.Beams == null) return null;
            foreach (var b in Game.Beams)
            {
                if (b.Id == id) return b;
            }

            return null;
        }

        private void RebuildList()
        {
            foreach (var go in _rows) Destroy(go);
            _rows.Clear();

            var source = Find(_sourceId);
            string srcName = source == null || string.IsNullOrEmpty(source.Name) ? L("ui.beam.default") : source.Name;
            _title.text = L("ui.beam.title") + " — " + srcName;

            // Collect usable destinations (own + allied), excluding the source, nearest first.
            var dests = new List<NetBeam>();
            if (Game?.Beams != null)
            {
                foreach (var b in Game.Beams)
                {
                    if (b.Id != _sourceId && Game.CanUseBeam(b)) dests.Add(b);
                }
            }

            Vector3 me = Game != null ? Game.PlayerPosition : Vector3.zero;
            dests.Sort((a, b) => Dist(me, a).CompareTo(Dist(me, b)));

            if (dests.Count == 0)
            {
                AddRowGo(64f, go => UiKit.AddText(go.transform, 12f, 8f, 520f, 48f, L("ui.beam.none"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft));
            }
            else
            {
                foreach (var d in dests)
                {
                    var dest = d; // capture
                    string name = string.IsNullOrEmpty(dest.Name) ? L("ui.beam.default") : dest.Name;
                    int cx = Mathf.FloorToInt(dest.X), cz = Mathf.FloorToInt(dest.Z);
                    int meters = Mathf.RoundToInt(Mathf.Sqrt(Dist(me, dest)));
                    string distLine = L("ui.beam.distance").Replace("{0}", meters.ToString());

                    AddRowGo(56f, go =>
                    {
                        UiKit.AddText(go.transform, 12f, 4f, 380f, 28f, name, 19, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                        UiKit.AddText(go.transform, 12f, 30f, 380f, 22f, $"X {cx}  ·  Z {cz}  ·  {distLine}", 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                        UiKit.AddButton(go.transform, 404f, 8f, 150f, 40f, L("ui.beam.beam_button"), () =>
                        {
                            Game.Network?.SendBeamTeleport(_sourceId, dest.Id);
                            Close();
                        });
                    });
                }
            }
        }

        /// <summary>Adds a fixed-height row to the scrollable destination list and lets the caller fill it in.</summary>
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

        private static float Dist(Vector3 me, NetBeam b) => (new Vector3(b.X, b.Y, b.Z) - me).sqrMagnitude;

        private void EnsureBuilt()
        {
            if (_built) return;

            _canvas = UiKit.CreateCanvas("BeamPadUI");
            _canvas.sortingOrder = 59; // above the HUD/chat + beacon overlay, below the world map (60)
            var root = _canvas.transform;

            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            const float w = 620f, h = 560f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            _title = UiKit.AddText(root, x + 24, y + 20, w - 48, 32, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;

            UiKit.AddText(root, x + 24, y + 58, w - 48, 24, L("ui.beam.destinations"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(root, x + 24, y + 58, w - 48, 24,
                L("ui.beam.cost").Replace("{0}", "6").Replace("{1}", "6"), 14, UiKit.CyanDim, TextAnchor.MiddleRight);

            _list = UiKit.ScrollList(root, x + 16, y + 90, w - 32, h - 168, 6f);

            UiKit.AddButton(root, x + 24, y + h - 60, 220, 44, L("ui.beam.rename"), () =>
            {
                var source = Find(_sourceId);
                if (source == null || source.OwnerId != Game?.LocalPlayerId) return; // owner-only (server re-checks)
                int id = _sourceId;
                string current = source.Name ?? string.Empty;
                Close();
                BeaconLabelUi.Instance?.Open(L("ui.beam.rename_prompt"), current, name => Game.Network?.SendSetBeamName(id, name));
            });

            UiKit.AddButton(root, x + w - 24 - 220, y + h - 60, 220, 44, L("ui.beam.close"), Close);

            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
