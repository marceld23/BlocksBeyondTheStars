// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pickup feed of <see cref="HudUi"/> (#745): the "+n name" rows above the hotbar's right end, and the block
    /// icons that fly from a mined block into the hotbar.
    /// </summary>
    public sealed partial class HudUi
    {
        // Pickup feed (#745): a short right-aligned column just above the hotbar's right end, one row per
        // collected item ("icon  +n name"). Repeat pickups of the same item merge and count up instead of
        // stacking rows; each row fades out after a couple of seconds. Rows live under the hotbar root so
        // flying/driving hides the feed together with the bar.
        private sealed class PickupRow
        {
            public string Item;
            public int Count;
            public float Ttl;
            public GameObject Go;
            public CanvasGroup Fade;
            public TMP_Text Label;
        }

        private readonly System.Collections.Generic.List<PickupRow> _pickupRows = new System.Collections.Generic.List<PickupRow>();
        private const int PickupMaxRows = 4;
        private const float PickupRowH = 26f, PickupRowW = 380f, PickupLife = 2.5f, PickupFadeTime = 0.5f;
        private float _pickupRightX, _pickupAnchorY; // right edge + top of the hotbar backplate

        private Canvas _flyCanvas; // own overlay canvas so the visor distortion can't bend the fly-ins

        /// <summary>Eases a pickup icon from its spawn point into the hotbar zone, shrinking, then dies.</summary>
        private sealed class FlyIcon : MonoBehaviour
        {
            public Vector2 From, To;

            private const float Life = 0.45f;
            private float _t;
            private RectTransform _rt;

            private void Awake() => _rt = (RectTransform)transform;

            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / Life);
                _rt.anchoredPosition = Vector2.Lerp(From, To, k * k); // ease-in: accelerates toward the bar
                transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.45f, k);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A small block-tile icon flying from the mined block toward the hotbar — the
        /// "it went into your inventory" read (mining-loop juice). No-op if the block is unknown.</summary>
        public void FlyPickup(Vector3 worldPos, BlocksBeyondTheStars.Shared.Primitives.BlockId block)
        {
            var cam = Camera.main;
            var def = Game?.Content?.BlockById(block);
            if (cam == null || def == null || Game.Atlas == null || GameBootstrap.HudHidden)
            {
                return; // #2461: no icon flies across a cutscene (its canvas sits above every frame) — #2496: nor to a hidden hotbar
            }

            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f)
            {
                return; // behind the camera
            }

            if (_flyCanvas == null)
            {
                var go = new GameObject("PickupFlyCanvas");
                _flyCanvas = go.AddComponent<Canvas>();
                _flyCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _flyCanvas.sortingOrder = 240;
            }

            var iconGo = new GameObject("fly_icon");
            iconGo.transform.SetParent(_flyCanvas.transform, false);
            var raw = iconGo.AddComponent<RawImage>();
            BlockCubeIcon.Apply(raw, Game.Atlas, def.NumericId.Value); // the tile, or the cube of a block whose faces differ (#2744)
            raw.raycastTarget = false;
            var rt = raw.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(34f, 34f);

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var fly = iconGo.AddComponent<FlyIcon>();
            fly.From = new Vector2(sp.x, sp.y) - center;
            fly.To = new Vector2(0f, -Screen.height * 0.5f + 70f); // the hotbar zone (bottom-centre)
        }

        // --- pickup feed (#745) ---

        /// <summary>Drains the queued inventory gains into the feed rows and ages/fades them. While the
        /// hotbar is hidden (piloting/driving/observer) gains are dropped, not saved up — announcing a
        /// pile of stale pickups after landing would be noise, not feedback.</summary>
        private void UpdatePickupFeed(float dt)
        {
            if (_hotbarRoot == null || !_hotbarRoot.activeSelf)
            {
                Game.PickupGains.Clear();
                if (_pickupRows.Count > 0)
                {
                    foreach (var row in _pickupRows)
                    {
                        Destroy(row.Go);
                    }

                    _pickupRows.Clear();
                }

                return;
            }

            bool changed = false;
            while (Game.PickupGains.Count > 0)
            {
                var (item, gained) = Game.PickupGains.Dequeue();
                var row = _pickupRows.Find(r => r.Item == item);
                if (row == null)
                {
                    if (_pickupRows.Count >= PickupMaxRows)
                    {
                        Destroy(_pickupRows[0].Go); // full: the oldest row yields
                        _pickupRows.RemoveAt(0);
                    }

                    row = MakePickupRow(item);
                    _pickupRows.Add(row);
                }

                row.Count += gained;
                row.Ttl = PickupLife;
                // Same #927 rule as the hotbar caption: mined dyed/shaped/painted drops arrive with a
                // composite key, which must never reach the locale table raw.
                row.Label.text = $"+{row.Count} " + BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(
                    Game.Localizer, item, _customFormName ??= idx => Game.CustomShapes?.NameOf(idx),
                    _speciesName ??= seed => Game.Bio.SpeciesName(seed));
                // #2964: a pickup that only topped up a backpack stack leaves the hotbar unchanged — say where it went.
                if (InventoryDiff.OnlyInBackpack(Game.Personal, item, Slots) && Game.Localizer != null)
                {
                    row.Label.text += "  ·  " + Game.Localizer.Get("hud.pickup.backpack");
                }

                changed = true;
            }

            for (int i = _pickupRows.Count - 1; i >= 0; i--)
            {
                var row = _pickupRows[i];
                row.Ttl -= dt;
                if (row.Ttl <= 0f)
                {
                    Destroy(row.Go);
                    _pickupRows.RemoveAt(i);
                    changed = true;
                    continue;
                }

                // Steady, then a short fade-out at the end; reduced motion holds full alpha until removal.
                row.Fade.alpha = UiKit.ReducedMotion ? 1f : Mathf.Clamp01(row.Ttl / PickupFadeTime);
            }

            if (changed)
            {
                // Stack upward from the backplate: newest row sits closest to the hotbar.
                for (int i = 0; i < _pickupRows.Count; i++)
                {
                    float y = _pickupAnchorY - (_pickupRows.Count - i) * PickupRowH;
                    UiKit.Place(_pickupRows[i].Go, _pickupRightX - PickupRowW, y, PickupRowW, PickupRowH);
                }
            }
        }

        /// <summary>One feed row: right-aligned "+n name" text with the item's icon at the far right.</summary>
        private PickupRow MakePickupRow(string item)
        {
            var go = new GameObject("pickup_row", typeof(RectTransform));
            go.transform.SetParent(_hotbarRoot.transform, false);
            var fade = go.AddComponent<CanvasGroup>();
            fade.blocksRaycasts = false;
            fade.interactable = false;

            var iconGo = new GameObject("icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            UiKit.Place(iconGo, PickupRowW - 24f, 3f, 20f, 20f);
            var raw = iconGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetItemIcon(raw, item);

            var label = UiText.Add(go.transform, 0f, 3f, PickupRowW - 30f, 20f, string.Empty, 15,
                UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Bold);
            UiText.Style(label, UiText.Look.Outline);

            return new PickupRow { Item = item, Go = go, Fade = fade, Label = label };
        }

        /// <summary>Resolves an item key to its icon the same way the hotbar does (atlas tile for blocks
        /// and seeds, generated PNG or procedural fallback for pure items) — minus the shaped-block special
        /// case, since drops in the feed are plain base items.</summary>
        private void SetItemIcon(RawImage img, string item)
        {
            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            Texture2D itemTex;
            if (blockDef != null && Game.Atlas != null)
            {
                BlockCubeIcon.Apply(img, Game.Atlas, blockDef.NumericId.Value); // a block whose faces differ is a cube (#2744)
            }
            else if ((itemTex = IconResolver.ItemTexture(item)) != null)
            {
                img.texture = itemTex;
                img.uvRect = new Rect(0, 0, 1, 1);
            }
            else
            {
                var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                img.texture = IconFactory.ForItem(item, kind);
                img.uvRect = new Rect(0, 0, 1, 1);
            }

            img.color = IconResolver.Tint(item, Game);
        }
    }
}
