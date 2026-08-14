// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Per-crate stash filter (#1032): press E at a storage crate to pick which items belong in it — a
    /// dedicated ore crate, a food crate, and a bulk H-stash sorts itself. The panel offers the crate's
    /// current whitelist plus everything stashable the player carries; clicking toggles an item, Save sends
    /// one <c>SetContainerFilterIntent</c>, "allow everything" clears the filter. Nothing selected means no
    /// filter (today's behaviour). The server re-validates every key — this panel is a convenience, not the
    /// rule. Modal like <see cref="BeaconLabelUi"/>: control freeze + cursor release via the arbiter (#413).
    /// </summary>
    public sealed class ContainerFilterUi : MonoBehaviour
    {
        public static ContainerFilterUi Instance { get; private set; }
        public GameBootstrap Game;

        // The 6×4 grid shows at most 24 entries (server cap is 32): current whitelist first, then the
        // player's carried stashables — with a full backpack of distinct materials the tail may not fit,
        // which is acceptable for a sorting UI (walk over with the items you want to dedicate the crate to).
        private const int Cols = 6;
        private const int MaxShown = 24;

        private Canvas _canvas;
        private string _containerId = string.Empty;
        private readonly List<string> _candidates = new List<string>();
        private readonly HashSet<string> _selected = new HashSet<string>();

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Close();
        }

        /// <summary>True while the panel is open (gameplay hotkeys stand down).</summary>
        public bool IsOpen => _canvas != null;

        /// <summary>Opens the filter panel for a crate: its current whitelist pre-selected, the player's
        /// stashable items offered on top.</summary>
        public void Open(string containerId, string[] currentFilter)
        {
            if (IsOpen || Game == null)
            {
                return;
            }

            _containerId = containerId;
            _selected.Clear();
            _candidates.Clear();

            foreach (var key in currentFilter ?? System.Array.Empty<string>())
            {
                string baseKey = ItemKey.Base(key);
                if (baseKey.Length > 0 && !_candidates.Contains(baseKey))
                {
                    _candidates.Add(baseKey);
                    _selected.Add(baseKey);
                }
            }

            // Everything stashable in the backpack (same category rule the server stashes by).
            foreach (var s in Game.Personal)
            {
                string baseKey = ItemKey.Base(s.Item);
                if (baseKey.Length > 0 && !_candidates.Contains(baseKey)
                    && Game.Content?.GetItem(baseKey)?.Category is Shared.Definitions.ItemCategory.Material or Shared.Definitions.ItemCategory.Component)
                {
                    _candidates.Add(baseKey);
                }
            }

            _canvas = UiKit.CreateCanvas("ContainerFilterUi");
            _canvas.sortingOrder = 58; // above the HUD/chat, below the world map (60) — same shelf as BeaconLabelUi
            UiNav.Enable(_canvas.gameObject); // pad: stick walks the grid, A toggles (#940)
            Game.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
            Build();
        }

        private void Update()
        {
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                Game?.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Close()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                Game?.SetMenuOwner(this, false);
            }

            _containerId = string.Empty;
            _candidates.Clear();
            _selected.Clear();
        }

        private void Build()
        {
            var (_, panel) = UiKit.AddModalOverlay(_canvas.transform, 460f, 100f, 1000f, 880f);

            var head = UiKit.AddText(panel, 32f, 28f, 900f, 40f, L("ui.container_filter.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
            UiKit.AddText(panel, 32f, 76f, 936f, 48f, L("ui.container_filter.hint"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);

            const float cell = 132f, pitch = 156f, x0 = 32f, y0 = 140f;
            int shown = Mathf.Min(_candidates.Count, MaxShown);
            for (int k = 0; k < shown; k++)
            {
                string item = _candidates[k];
                float x = x0 + (k % Cols) * pitch;
                float y = y0 + (k / Cols) * pitch;

                var b = UiKit.AddButton(panel, x, y, cell, cell, string.Empty, () =>
                {
                    if (!_selected.Add(item))
                    {
                        _selected.Remove(item);
                    }

                    Rebuild();
                });
                if (_selected.Contains(item))
                {
                    var img = b.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = UiKit.Cyan; // "this belongs in the crate"
                    }
                }

                AddItemIcon(b.transform, 10f, 10f, cell - 20f, item);
                var name = UiKit.AddText(b.transform, 4f, cell - 24f, cell - 8f, 20f, ShortName(item), 12, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddOutline(name);
            }

            if (_candidates.Count == 0)
            {
                UiKit.AddText(panel, 32f, y0 + 40f, 936f, 60f, L("ui.container_filter.empty"), 20, UiKit.TextCol, TextAnchor.MiddleCenter);
            }

            UiKit.AddButton(panel, 32f, 802f, 300f, 48f, L("ui.container_filter.save"), () =>
            {
                Game.Network?.SendSetContainerFilter(_containerId, _selected.ToArray());
                Close();
            });
            UiKit.AddButton(panel, 350f, 802f, 300f, 48f, L("ui.container_filter.allow_all"), () =>
            {
                Game.Network?.SendSetContainerFilter(_containerId, System.Array.Empty<string>());
                Close();
            });
            UiKit.AddButton(panel, 788f, 802f, 180f, 48f, L("ui.container_filter.cancel"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });
        }

        private void Rebuild()
        {
            foreach (Transform child in _canvas.transform)
            {
                Destroy(child.gameObject);
            }

            Build();
        }

        /// <summary>The same icon resolution the hotbar uses (atlas tile for block items, generated icon
        /// otherwise). Filter entries are base keys, so no dyed/shaped/painted branches are needed here.</summary>
        private void AddItemIcon(Transform parent, float x, float y, float size, string item)
        {
            var go = new GameObject("ItemIcon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            if (blockDef != null && Game.Atlas != null)
            {
                raw.texture = Game.Atlas.Texture;
                raw.uvRect = Game.Atlas.TileUv(blockDef.NumericId.Value);
            }
            else
            {
                Texture2D itemTex = IconResolver.ItemTexture(item);
                var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                raw.texture = itemTex != null ? itemTex : IconFactory.ForItem(item, kind);
            }

            raw.color = IconResolver.Tint(item, Game);
        }

        private string ShortName(string item)
        {
            string name = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, item, null);
            return name.Length > 12 ? name.Substring(0, 11) + "…" : name;
        }

        private string L(string key) => Game.Localizer?.Get(key) ?? key;
    }
}
