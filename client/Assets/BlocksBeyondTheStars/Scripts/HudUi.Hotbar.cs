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
    /// The hotbar of <see cref="HudUi"/>: the quick-bar cells with their block / item icons, the selection tick and
    /// the slot-action badge (#935).
    /// </summary>
    public sealed partial class HudUi
    {
        private const int Slots = 9;

        private UiKit.QuickSlot[] _hotbar;
        private GameObject _hotbarRoot; // backplate + cells + rings, toggled together when flying
        private TMP_Text _slotActionBadge;  // HotbarAction key glyph over the SELECTED cell — the slot-action ring's on-hotbar tell (#935)
        private float _hotbarX0, _hotbarPitch; // cell 0's left edge + cell spacing, for sliding the badge to the selection

        private int _lastSelSlot = -1; // hotbar selection tick state
        private System.Func<int, string> _customFormName; // cached resolver for ItemNames.Display (per-frame path)
        private System.Func<uint, string> _speciesName;   // …and the species lookup of a sample's or seedling's name (#2201)
        private float _hotbarY; // top of the quick-bar cells (#2290: the gear strip lines up with them)

        /// <summary>A quick scale tick (1 → 1.1 → 1 over 0.12 s) on the newly selected hotbar slot.</summary>
        private sealed class SlotTick : MonoBehaviour
        {
            private const float Life = 0.12f;
            private float _t;

            public void Restart()
            {
                _t = 0f;
                enabled = !UiKit.ReducedMotion;
                if (!enabled)
                {
                    transform.localScale = Vector3.one;
                }
            }

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_t / Life);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.1f);
                if (k >= 1f)
                {
                    transform.localScale = Vector3.one;
                    enabled = false;
                }
            }
        }

        private void BuildHotbar(Transform root)
        {
            // Hotbar: a centred row of large icon cells on a backplate, raised a touch off the bottom edge so the
            // held tool reads clearly. Shares its cell style with the flight ship-systems bar (UiKit.QuickSlot).
            // All cells + backplate + selection rings live under one stretched container so flying hides them in one.
            _hotbarRoot = new GameObject("Hotbar", typeof(RectTransform));
            _hotbarRoot.transform.SetParent(root, false);
            var hbRt = _hotbarRoot.GetComponent<RectTransform>();
            hbRt.anchorMin = Vector2.zero; hbRt.anchorMax = Vector2.one; hbRt.offsetMin = hbRt.offsetMax = Vector2.zero;
            var hbParent = _hotbarRoot.transform;
            _hotbar = new UiKit.QuickSlot[Slots];
            const float sw = 72f, pitch = 80f;
            float total = (Slots - 1) * pitch + sw, x0 = (W - total) / 2f, hy = H - sw - 40f;
            UiKit.QuickBackplate(hbParent, x0 - 12f, hy - 10f, total + 24f, sw + 20f);
            for (int i = 0; i < Slots; i++)
            {
                _hotbar[i] = UiKit.MakeQuickSlot(hbParent, x0 + i * pitch, hy, sw);
            }

            // Slot-action badge (#935): the HotbarAction key's short name floating just above the SELECTED
            // cell — the middle-click ring's only at-the-object tell. Slid to the selection and tinted per
            // frame in RefreshHotbar; lives under the hotbar root so the flying/driving states hide it too.
            _hotbarX0 = x0;
            _hotbarPitch = pitch;
            _slotActionBadge = UiText.Add(hbParent, x0, hy - 34f, sw, 20f, string.Empty, 12, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiText.Style(_slotActionBadge, UiText.Look.Outline);

            // Pickup feed anchors (#745): rows sit flush with the backplate's right edge, stacking upward
            // from just above it.
            _pickupRightX = x0 + total + 12f;
            _pickupAnchorY = hy - 14f;
            _hotbarY = hy;
        }

        /// <summary>Small-integer strings for the hotbar (slot numbers, stack counts) without a ToString per refresh.</summary>
        private static readonly string[] _smallInts = new string[1000];

        private static string SmallInt(int n)
        {
            if (n < 0 || n >= _smallInts.Length)
            {
                return n.ToString();
            }

            return _smallInts[n] ??= n.ToString();
        }

        // --- hotbar ---

        private void RefreshHotbar(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            // No on-foot hotbar while flying the ship — you're piloting, not holding hand tools. BUT on an EVA
            // the hotbar IS shown: you float in space and build/mine on structures from your inventory, so you
            // need to see + pick the held block/tool (B?).
            // Observer mode has no hotbar either (issue #487): an observer carries nothing and may not build,
            // so showing slots would advertise actions the server drops.
            bool hide = ((Game.SpaceViewActive || Game.InSpace) && !Game.InEva)
                        || !string.IsNullOrEmpty(Game.InSpeeder)
                        || Game.Spectating;
            if (_hotbarRoot != null && _hotbarRoot.activeSelf == hide)
            {
                _hotbarRoot.SetActive(!hide);
            }

            if (hide)
            {
                return;
            }

            // A quick pop on the newly selected slot's icon (skipped for the initial selection); the persistent
            // scale-up + bright ring (UiKit.StyleQuickSlot) is the steady "this is selected" cue.
            int selNow = Game.SelectedHotbarSlot;
            if (selNow != _lastSelSlot && _lastSelSlot >= 0 && selNow >= 0 && selNow < Slots && _hotbar[selNow].Icon != null)
            {
                var ico = _hotbar[selNow].Icon.gameObject;
                (ico.GetComponent<SlotTick>() ?? ico.AddComponent<SlotTick>()).Restart();
                UiKit.FlashQuickSlot(_hotbar[selNow]); // the holo ring flares and settles
            }

            _lastSelSlot = selNow;

            bool selQualifies = false; // Colour/Form apply to the selected slot's item (badge tint, #935)
            for (int i = 0; i < Slots; i++)
            {
                var s = _hotbar[i];
                bool sel = i == Game.SelectedHotbarSlot;
                UiKit.StyleQuickSlot(s, sel);
                s.Num.text = SmallInt(i + 1);

                string item = Game.ItemInSlot(i);
                if (string.IsNullOrEmpty(item))
                {
                    s.Icon.enabled = false;
                    s.Name.text = string.Empty;
                    s.Count.text = string.Empty;
                    continue;
                }

                // Stack size top-right (#744): only for real stacks, so tools (max stack 1) stay clean.
                int count = Game.CountInSlot(i);
                s.Count.text = count > 1 ? SmallInt(count) : string.Empty;

                var blockDef = Game.Content?.GetBlock(item);
                if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
                {
                    blockDef = Game.Content?.GetBlock(pb); // a seed etc. shows the tile of the block it places
                }

                if (sel)
                {
                    // Mirrors HotbarActionUi.HeldBlockDef exactly (base key → PlacesBlock): a dyed/shaped/
                    // painted stack carries its modifiers in the composite key, which GetItem won't resolve.
                    string selBase = BlocksBeyondTheStars.Shared.State.ItemKey.Base(item);
                    var selItemDef = Game.Content?.GetItem(selBase);
                    var selDef = selItemDef != null && !string.IsNullOrEmpty(selItemDef.PlacesBlock)
                        ? Game.Content?.GetBlock(selItemDef.PlacesBlock)
                        : null;
                    selQualifies = selDef != null && (selDef.Tintable || selDef.Shapeable);
                }

                // A shaped block (sphere/pyramid/…) shows a form-specific icon instead of a plain cube tile (#125).
                int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(item);
                Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                    ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape, Game.CustomShapes)
                    : null;

                // A painted item (own texture, p-field in the key) shows its design — the texture IS the
                // point of that stack, so it beats the form silhouette. Unresolved/wiped ids fall through.
                int design = BlocksBeyondTheStars.Shared.State.ItemKey.Design(item);
                Texture2D itemTex;
                if (design != 0 && Game.PaintAtlas != null && Game.PaintAtlas.TryGetUv(design, out var designUv))
                {
                    s.Icon.texture = Game.PaintAtlas.Texture;
                    s.Icon.uvRect = designUv;
                }
                else if (shapeTex != null)
                {
                    s.Icon.texture = shapeTex;
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }
                else if (blockDef != null && Game.Atlas != null)
                {
                    BlockCubeIcon.Apply(s.Icon, Game.Atlas, blockDef.NumericId.Value); // a block whose faces differ is a cube (#2744)
                }
                else if ((itemTex = IconResolver.ItemTexture(item)) != null)
                {
                    s.Icon.texture = itemTex; // a generated content-styled icon
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }
                else
                {
                    var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                    s.Icon.texture = IconFactory.ForItem(item, kind);
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }

                s.Icon.color = IconResolver.Tint(item, Game); // toxic consumables read green
                s.Icon.enabled = true;
                // The held slot shows its full name (brighter); the rest stay short so the row reads at a
                // glance. Resolved through the shared helper — a dyed/shaped/painted stack carries its
                // modifier in the key, and a raw item.{key}.name lookup renders as the bracketed key (#927).
                string name = BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(loc, item,
                    _customFormName ??= idx => Game.CustomShapes?.NameOf(idx),
                    _speciesName ??= seed => Game.Bio.SpeciesName(seed)); // #2201: a seedling names its species
                s.Name.text = sel ? name : (name.Length > 10 ? name.Substring(0, 9) + "…" : name);
                s.Name.color = sel ? UiKit.Cyan : UiKit.TextCol;
            }

            // Slot-action badge (#935): shown while the selected slot holds anything to act on (the ring
            // also opens on an empty slot, but advertising that is noise). Bright while Colour/Form apply
            // to the held material, dim while only Swap does. Touch has no key to press, so no badge.
            if (_slotActionBadge != null)
            {
                bool show = selNow >= 0 && selNow < Slots && !string.IsNullOrEmpty(Game.ItemInSlot(selNow))
                            && InputMap.ActiveDevice != InputDeviceKind.Touch;
                if (_slotActionBadge.gameObject.activeSelf != show)
                {
                    _slotActionBadge.gameObject.SetActive(show);
                }

                if (show)
                {
                    var badgeRt = _slotActionBadge.rectTransform;
                    badgeRt.anchoredPosition = new Vector2(_hotbarX0 + selNow * _hotbarPitch, badgeRt.anchoredPosition.y);
                    _slotActionBadge.text = GlyphText(loc, InputAction.HotbarAction);
                    _slotActionBadge.color = selQualifies ? UiKit.Cyan : UiKit.CyanDim;
                }
            }
        }
    }
}
