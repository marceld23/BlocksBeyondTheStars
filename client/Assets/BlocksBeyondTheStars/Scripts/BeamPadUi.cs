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
    public sealed class BeamPadUi : UiPanel
    {
        private Text _title;
        private RectTransform _list;
        private int _sourceId;
        private readonly List<GameObject> _rows = new List<GameObject>();

        protected override string CanvasName => "BeamPadUI";

        // pad: stick walks the destinations, A beams, B closes (#1198)
        protected override int CanvasSortingOrder => 59; // above the HUD/chat + beacon overlay, below the world map (60)

        /// <summary>Opens the transporter for the pad with id <paramref name="sourceId"/> (the one the player is at).</summary>
        public void Open(int sourceId)
        {
            EnsureCanvas();
            _sourceId = sourceId;
            ShowCanvas();
            RebuildList();

            SetMenuOwned(true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        private void Update()
        {
            if (!Shown) return;

            if (CancelPressed())
            {
                ConsumeMenuInput(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Close()
        {
            HideCanvas();
            SetMenuOwned(false); // arbiter re-locks only once NO other panel is open (#413)
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
            UiKit.FitLabelSingleLine(_title, 572f, 16, 26); // a long pad name stays on the title row
            _rename.interactable = source != null && Game != null && source.OwnerId == Game.LocalPlayerId;

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
                        UiKit.AddButton(go.transform, 404f, 6f, 150f, 44f, L("ui.beam.beam_button"), () =>
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

        /// <summary>Squared distance from the player to a pad — #2613: the short way round both seams (pads arrive canonical,
        /// the player's position runs unbounded), so the nearest pad sorts first and the metres read true across X = 0.</summary>
        private float Dist(Vector3 me, NetBeam b)
            => (float)InteractReach.SeamDistanceSquared(me.x, me.y, me.z, b.X, b.Y, b.Z,
                Game != null ? Game.Circumference : BlocksBeyondTheStars.Shared.World.WorldConstants.Circumference);

        protected override void BuildChrome(Transform root)
        {
            // #3662: the in-world dialog look — a modal scrim (the pad's focus stays inside it) and an OPAQUE panel; the
            // translucent one let the world shine through the destination names. Everything hangs off the scrim, which
            // covers the whole 1920×1080 canvas, so the coordinates stay the canvas's.
            root = UiKit.AddModalDim(root, 0.45f).transform;

            const float w = 620f, h = 560f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddDialogPanel(root, x, y, w, h);

            _title = UiKit.AddText(root, x + 24, y + 20, w - 48, 32, string.Empty, 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(_title);

            UiKit.AddText(root, x + 24, y + 58, w - 48, 24, L("ui.beam.destinations"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.AddText(root, x + 24, y + 58, w - 48, 24,
                L("ui.beam.cost").Replace("{0}", ((int)BlocksBeyondTheStars.Shared.Definitions.TeleportRules.BeamEnergyCost).ToString())
                    .Replace("{1}", ((int)BlocksBeyondTheStars.Shared.Definitions.TeleportRules.BeamCooldownSeconds).ToString()), 14, UiKit.CyanDim, TextAnchor.MiddleRight); // #2994: the server's own numbers

            _list = UiKit.ScrollList(root, x + 16, y + 90, w - 32, h - 168, 6f);

            _rename = UiKit.AddButton(root, x + 24, y + h - 60, 220, 44, L("ui.beam.rename"), () =>
            {
                var source = Find(_sourceId);
                if (source == null || source.OwnerId != Game?.LocalPlayerId) return; // owner-only (server re-checks)
                int id = _sourceId;
                string current = source.Name ?? string.Empty;
                Close();
                Game?.Context.Panel<BeaconLabelUi>()?.Open(L("ui.beam.rename_prompt"), current, name => Game.Network?.SendSetBeamName(id, name));
            });

            // #3662: Close = the cross, quiet, built last — the pad lands on the first destination's Beam.
            UiKit.AddTierButton(root, x + w - 24 - 220, y + h - 60, 220, 44, L("ui.beam.close"), Close, ButtonTier.Quiet, "btn_close");
        }

        /// <summary>Rename (owner only): greyed out — and so skipped by the pad — on a pad that is not the player's, where
        /// a click used to do nothing at all.</summary>
        private Button _rename;
    }
}
