// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Slot actions on the SELECTED hotbar slot, straight from the on-planet HUD: press the HotbarAction
    /// key (default middle mouse) to open a radial pie of verbs around the screen centre (#935) — <b>Swap</b>
    /// (exchange the slot against any backpack item), and for a material block additionally <b>Colour</b>
    /// (dye / glow / an own saved paint design) and <b>Form</b> (built-in or own saved forms). Picking a
    /// verb opens its flat detail panel; everything applies to the WHOLE stack and lands back in the same
    /// slot (the craft intents carry the slot).
    ///
    /// Deliberately built on a plain screen-space <see cref="UiKit.CreateCanvas"/>, NOT the diegetic HUD
    /// canvas: the visor pipeline renders that one through an RT camera with barrel distortion, where
    /// pointer hit-testing lands wrong. A flat overlay canvas takes ordinary EventSystem clicks on every
    /// preset. Control freeze + cursor release go through the menu-owner arbiter (#413), like every panel.
    ///
    /// The verbs re-use the crafting menu's server paths unchanged (<c>TintCraftIntent</c>,
    /// <c>ShapeCraftIntent</c>, <c>CustomShapeCraftIntent</c>, <c>MoveItemIntent</c>) plus the item-paint
    /// sibling <c>PaintCraftIntent</c> — this class is a second front-end, not a second rule set.
    /// </summary>
    public sealed class HotbarActionUi : MonoBehaviour
    {
        public static HotbarActionUi Instance { get; private set; }
        public GameBootstrap Game;

        // Mirrors the crafting menu's Dye/Glow swatch palette so both surfaces offer the same colours.
        private static readonly int[] ColorPalette =
        {
            0xE03A3A, 0xE07A2A, 0xE0C020, 0x8FD030, 0x35C04A, 0x2FB0A0,
            0x2F8FE0, 0x2F4FE0, 0x6A3FE0, 0xB23FE0, 0xE03FA0, 0xE83060,
            0xFFFFFF, 0xC8D0D8, 0x8A94A0, 0x4A5260, 0x2A3038, 0x12161A,
        };

        // The same form list the crafting menu offers (shape index → locale key); 0 = back to a plain cube.
        private static readonly (int Shape, string Loc)[] ShapeOptions =
        {
            (0, "ui.shape.cube"), (1, "ui.shape.slab"), (2, "ui.shape.pyramid"), (3, "ui.shape.dome"),
            (4, "ui.shape.sphere"), (5, "ui.shape.ramp"), (6, "ui.shape.stairs"), (7, "ui.shape.cone"),
            (8, "ui.shape.cylinder"), (9, "ui.shape.panel"), (10, "ui.shape.post"), (11, "ui.shape.beam"),
            (12, "ui.shape.lowramp"), (13, "ui.shape.quartercube"),
            (14, "ui.shape.table"), (15, "ui.shape.chair"), (16, "ui.shape.fence"),
            (17, "ui.shape.sheet"), (18, "ui.shape.pot"),
        };

        private Canvas _canvas;
        private int _slot = -1;        // the hotbar slot the ring was opened on
        private string _item = string.Empty; // its item key at open time
        private bool _glowMode;        // colour panel: false = dye (free), true = glow (costs a crystal/unit)
        private readonly List<Texture2D> _previews = new List<Texture2D>(); // design thumbnails, destroyed on close

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Close();
        }

        /// <summary>True while the action ring / a detail panel is open (gameplay hotkeys stand down).</summary>
        public bool IsOpen => _canvas != null;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (IsOpen)
            {
                // The opening key toggles, Esc always closes. (A focused text field never lives here.)
                // Marking the frame keeps the app shell / pause menu from ALSO acting on the same Esc (#413).
                if (InputMap.Down(InputAction.HotbarAction) || Input.GetKeyDown(KeyCode.Escape))
                {
                    Game.MarkMenuInputHandled();
                    Close();
                }

                return;
            }

            if (!InputMap.Down(InputAction.HotbarAction) || Game.MenuOpen)
            {
                return;
            }

            // Exactly the states in which the HUD hides the hotbar (piloting/driving/spectating): no slots
            // on screen means no slot to act on. EVA deliberately stays allowed — the hotbar shows there.
            bool hotbarHidden = ((Game.SpaceViewActive || Game.InSpace) && !Game.InEva)
                                || !string.IsNullOrEmpty(Game.InSpeeder)
                                || Game.Spectating;
            if (hotbarHidden)
            {
                return;
            }

            Open(Game.SelectedHotbarSlot);
        }

        // --- panel lifecycle ---

        private void Open(int slot)
        {
            _slot = slot;
            _item = Game.ItemInSlot(slot);
            _glowMode = false;
            _canvas = UiKit.CreateCanvas("HotbarActionUi");
            _canvas.sortingOrder = 40; // above the HUD (10) and the flight overlay (12), below nothing that matters
            Game.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
            BuildRing();
        }

        private void Close()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                Game?.SetMenuOwner(this, false);
            }

            foreach (var tex in _previews)
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }

            _previews.Clear();
            _slot = -1;
            _item = string.Empty;
        }

        private void Rebuild(System.Action build)
        {
            foreach (Transform child in _canvas.transform)
            {
                Destroy(child.gameObject);
            }

            build();
        }

        // --- level 1: the verb ring ---

        /// <summary>The verbs as a radial pie around the screen centre (#935): four quarter-ring wedges —
        /// Swap on top, Colour left, Form right, Close at the bottom. Big targets, no scroll, one click.
        /// Wedges whose verb doesn't apply to the held item stay VISIBLE but dim and inert ("there is a
        /// verb here, just not for this item") — the served actions still mirror the server's
        /// Tintable/Shapeable gates, so the menu never offers a refused action.</summary>
        private void BuildRing()
        {
            var root = _canvas.transform;
            var dim = UiKit.AddModalDim(root, 0.55f); // lighter than a dialog — the world stays readable behind the ring
            var t = dim.transform;

            const float cx = 960f, cy = 540f; // canvas reference centre (1920×1080)

            string title = string.IsNullOrEmpty(_item)
                ? L("ui.hotbar_action.empty")
                : string.Format(L("ui.hotbar_action.title"), ItemName(_item), _slot + 1);
            var head = UiKit.AddText(t, cx - 320f, cy - 330f, 640f, 34f, title, 24, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddOutline(head);

            var blockDef = HeldBlockDef();
            bool tintable = blockDef != null && blockDef.Tintable;
            bool shapeable = blockDef != null && blockDef.Shapeable;

            // Swap is always available (an empty slot can still receive something from the backpack);
            // Close always works. Positive z-rotation is counter-clockwise: +90° = left, -90° = right.
            AddWedge(t, 0f, L("ui.hotbar_action.swap"), true, () => Rebuild(BuildSwap));
            AddWedge(t, 90f, L("ui.hotbar_action.colour"), tintable, () => Rebuild(BuildColour));
            AddWedge(t, -90f, L("ui.hotbar_action.form"), shapeable, () => Rebuild(BuildForm));
            AddWedge(t, 180f, L("ui.hotbar_action.close"), true, Close);

            if (!string.IsNullOrEmpty(_item) && !tintable && !shapeable)
            {
                UiKit.AddText(t, cx - 320f, cy + 264f, 640f, 30f, L("ui.hotbar_action.not_material"), 16, UiKit.CyanDim, TextAnchor.MiddleCenter);
            }
        }

        // Pie geometry: wedge bounding box (= ring outer diameter) and the radius the labels sit on.
        private const float WedgeDiameter = 460f;
        private const float WedgeLabelRadius = 152f;

        /// <summary>One quarter-ring wedge of the pie. The shared TOP-wedge sprite is rotated into place
        /// (uGUI raycasting follows the rotation) and alpha hit-testing keeps clicks inside the visible
        /// arc — the transparent corners of the bounding box never steal a click from a neighbour. The
        /// label counter-rotates so it reads upright at the wedge's centroid.</summary>
        private void AddWedge(Transform parent, float angleDeg, string label, bool enabled, System.Action onClick)
        {
            var go = new GameObject("Wedge", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(WedgeDiameter, WedgeDiameter);
            rt.anchoredPosition = new Vector2(960f, -540f); // canvas centre, in top-left anchor space
            rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);

            var img = go.AddComponent<Image>();
            img.sprite = WedgeSprite();
            img.color = enabled ? new Color(0.07f, 0.13f, 0.22f, 0.92f) : new Color(0.05f, 0.08f, 0.13f, 0.35f);
            img.raycastTarget = enabled;
            img.alphaHitTestMinimumThreshold = 0.5f;

            if (enabled)
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                var c = btn.colors; // same feel as UiKit.AddButton: dim at rest, bright on hover, cyan on press
                c.normalColor = new Color(0.70f, 0.74f, 0.80f, 1f);
                c.highlightedColor = Color.white;
                c.pressedColor = UiKit.Cyan;
                c.selectedColor = Color.white;
                c.fadeDuration = 0.08f;
                btn.colors = c;
                go.AddComponent<UiHover>();
                btn.onClick.AddListener(UiSound.Click);
                btn.onClick.AddListener(() => onClick());
            }

            var txt = UiKit.AddText(go.transform, 0f, 0f, 260f, 30f, label, 22,
                enabled ? UiKit.TextCol : UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddOutline(txt);
            var trt = txt.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0f, WedgeLabelRadius); // pre-rotation "up" = the wedge centroid
            trt.localRotation = Quaternion.Euler(0f, 0f, -angleDeg);  // …counter-rotated to read upright
        }

        private static Sprite _wedgeSprite;

        /// <summary>The shared quarter-annulus sprite (top wedge, centred on +Y): a soft-edged 90°-minus-gap
        /// arc between an inner and outer radius. Generated once and cached for the app's lifetime; the
        /// texture stays CPU-readable because <see cref="Image.alphaHitTestMinimumThreshold"/> samples it.</summary>
        private static Sprite WedgeSprite()
        {
            if (_wedgeSprite != null)
            {
                return _wedgeSprite;
            }

            const int n = 256;
            const float edge = 1.5f; // anti-alias band, px
            float c = (n - 1) / 2f;
            float rOut = c - 1f, rIn = rOut * 0.34f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var buf = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float radial = Mathf.Clamp01((rOut - r) / edge) * Mathf.Clamp01((r - rIn) / edge);
                    // 44° half-width leaves a ~2° visual gap to each neighbouring wedge.
                    float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 90f));
                    float angular = Mathf.Clamp01((44f - off) / edge);
                    buf[(y * n) + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Min(radial, angular)));
                }
            }

            tex.SetPixels32(buf);
            tex.Apply(false, false);
            _wedgeSprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return _wedgeSprite;
        }

        // --- level 2: swap (backpack ↔ slot) ---

        /// <summary>All 24 personal slots as a grid; clicking one swaps it with the hotbar slot the ring was
        /// opened on (one <c>MoveItemIntent</c>, validated server-side). The invoking slot is highlighted.</summary>
        private void BuildSwap()
        {
            var (_, panel) = UiKit.AddModalOverlay(_canvas.transform, 460f, 210f, 1000f, 660f);
            Header(panel, L("ui.hotbar_action.swap_title"));

            const int cols = 6;
            const float cell = 132f, pitch = 156f, x0 = 32f, y0 = 96f;
            int slotCount = 24; // the personal inventory's fixed size (quick-bar 0..8 + backpack 9..23)
            for (int k = 0; k < slotCount; k++)
            {
                int kk = k;
                float x = x0 + (k % cols) * pitch;
                float y = y0 + (k / cols) * pitch;
                string slotItem = Game.ItemInSlot(k);
                int count = Game.CountInSlot(k);
                string label = k < 9 ? (k + 1).ToString() : string.Empty;

                var b = UiKit.AddButton(panel, x, y, cell, cell, string.Empty, () =>
                {
                    if (kk != _slot)
                    {
                        Game.Network?.SendMoveItem(kk, _slot);
                    }

                    Close();
                });
                if (kk == _slot)
                {
                    var img = b.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = UiKit.Cyan; // "this is the slot you are filling"
                    }
                }

                if (!string.IsNullOrEmpty(slotItem))
                {
                    AddItemIcon(b.transform, 10f, 10f, cell - 20f, slotItem);
                    var name = UiKit.AddText(b.transform, 4f, cell - 24f, cell - 8f, 20f, ShortName(slotItem), 12, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiKit.AddOutline(name);
                    if (count > 1)
                    {
                        var cnt = UiKit.AddText(b.transform, 6f, 4f, cell - 12f, 20f, count.ToString(), 14, UiKit.TextCol, TextAnchor.UpperRight, FontStyle.Bold);
                        UiKit.AddOutline(cnt);
                    }
                }

                if (label.Length > 0)
                {
                    var num = UiKit.AddText(b.transform, 6f, 4f, 30f, 20f, label, 13, UiKit.CyanDim, TextAnchor.UpperLeft, FontStyle.Bold);
                    UiKit.AddOutline(num);
                }
            }

            // Stow: clear the hotbar slot into the first free backpack slot — the ✕ the crafting menu offers.
            if (!string.IsNullOrEmpty(_item))
            {
                UiKit.AddButton(panel, 32f, 96f + 4 * 156f + 6f, 340f, 48f, L("ui.inventory.remove_quickslot"),
                    () => { Game.Network?.SendMoveItem(_slot, -1); Close(); });
            }

            BackClose(panel, 1000f, 660f);
        }

        // --- level 2: colour (dye / glow / own designs) ---

        /// <summary>Swatch grid (dye free, glow +1 crystal per unit — the toggle shows the cost), plus the
        /// player's saved paint designs as "own texture" tiles. Everything converts the whole stack and
        /// pins the output back to the invoking slot.</summary>
        private void BuildColour()
        {
            var (_, panel) = UiKit.AddModalOverlay(_canvas.transform, 510f, 190f, 900f, 700f);
            Header(panel, string.Format(L("ui.hotbar_action.colour_title"), ItemName(_item), Game.CountInSlot(_slot)));

            // Dye/Glow mode toggle. Glow shows what it costs so the wheel never books crystals silently.
            int stack = Game.CountInSlot(_slot);
            int crystals = OwnedCount("crystal");
            var dyeBtn = UiKit.AddButton(panel, 32f, 92f, 260f, 48f, L("ui.color.dye"), () => { _glowMode = false; Rebuild(BuildColour); });
            var glowBtn = UiKit.AddButton(panel, 308f, 92f, 380f, 48f,
                string.Format(L("ui.hotbar_action.glow_cost"), stack, crystals), () => { _glowMode = true; Rebuild(BuildColour); });
            Highlight(_glowMode ? glowBtn : dyeBtn);

            const int cols = 6;
            const float sw = 96f, gap = 14f, x0 = 32f, y0 = 160f;
            for (int i = 0; i < ColorPalette.Length; i++)
            {
                int rgb = ColorPalette[i];
                float x = x0 + (i % cols) * (sw + gap);
                float y = y0 + (i / cols) * (sw + gap);
                var b = UiKit.AddButton(panel, x, y, sw, sw, string.Empty, () =>
                {
                    // Glow without the crystals would only round-trip a server refusal — say so locally.
                    if (_glowMode && crystals < stack)
                    {
                        Game.ShowMessage(L("ui.hotbar_action.need_crystals"));
                        return;
                    }

                    Game.Network?.SendTintCraft(_item, _glowMode ? 0 : rgb, _glowMode ? rgb : 0, stack, _slot);
                    Close();
                });
                var img = b.GetComponent<Image>();
                if (img != null)
                {
                    img.color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
                }
            }

            float yDesigns = y0 + 3f * (sw + gap) + 10f;
            UiKit.AddText(panel, 32f, yDesigns, 640f, 28f, L("ui.hotbar_action.own_designs"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            yDesigns += 34f;

            var designs = PaintLibrary.List();
            if (designs.Count == 0)
            {
                UiKit.AddText(panel, 32f, yDesigns, 820f, 44f, L("ui.hotbar_action.no_designs"), 15, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            const float tile = 96f;
            for (int i = 0; i < designs.Count && i < 7; i++) // one row; the library rarely holds more, and the paint tool manages it
            {
                var (name, pixels) = designs[i];
                float x = 32f + i * (tile + 14f);
                var b = UiKit.AddButton(panel, x, yDesigns, tile, tile, string.Empty, () =>
                {
                    Game.Network?.SendPaintCraft(_item, pixels, stack, _slot);
                    Close();
                });
                var preview = DesignPreview(pixels);
                if (preview != null)
                {
                    var go = new GameObject("Preview", typeof(RectTransform));
                    go.transform.SetParent(b.transform, false);
                    UiKit.Place(go, 6f, 6f, tile - 12f, tile - 12f);
                    var raw = go.AddComponent<RawImage>();
                    raw.texture = preview;
                    raw.raycastTarget = false;
                }

                var cap = UiKit.AddText(b.transform, 2f, tile - 20f, tile - 4f, 18f, name, 11, UiKit.TextCol, TextAnchor.MiddleCenter);
                UiKit.AddOutline(cap);
            }

            // Strip an applied design again (empty pixels = "remove" server-side, like clearing a block).
            if (ItemKey.Design(_item) != 0)
            {
                UiKit.AddButton(panel, 32f, yDesigns + tile + 12f, 340f, 46f, L("ui.hotbar_action.remove_design"),
                    () => { Game.Network?.SendPaintCraft(_item, string.Empty, stack, _slot); Close(); });
            }

            BackClose(panel, 900f, 700f);
        }

        // --- level 2: form (built-in + own) ---

        /// <summary>The 19 built-in forms as silhouette icons of the ACTUAL held material (the icon previews
        /// the result), plus the player's saved forms behind the shape-tool gate — the same gate the server
        /// enforces, so the panel never offers a form the craft would refuse.</summary>
        private void BuildForm()
        {
            var (_, panel) = UiKit.AddModalOverlay(_canvas.transform, 460f, 170f, 1000f, 740f);
            Header(panel, string.Format(L("ui.hotbar_action.form_title"), ItemName(_item), Game.CountInSlot(_slot)));

            int stack = Game.CountInSlot(_slot);
            int current = ItemKey.Shape(_item);
            ushort tileId = HeldBlockTile();

            const int cols = 7;
            const float cell = 116f, pitch = 132f, x0 = 32f, y0 = 96f;
            for (int i = 0; i < ShapeOptions.Length; i++)
            {
                var (shape, loc) = ShapeOptions[i];
                int target = shape;
                float x = x0 + (i % cols) * pitch;
                float y = y0 + (i / cols) * pitch;
                var b = UiKit.AddButton(panel, x, y, cell, cell, string.Empty, () =>
                {
                    Game.Network?.SendShapeCraft(_item, target, stack, _slot);
                    Close();
                });
                AddShapeIcon(b.transform, tileId, shape, cell);
                var cap = UiKit.AddText(b.transform, 2f, cell - 22f, cell - 4f, 20f, L(loc), 12, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddOutline(cap);
                if (shape == current)
                {
                    b.interactable = false;
                    var img = b.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = UiKit.Cyan; // "this is the form the stack already has"
                    }
                }
            }

            int rows = (ShapeOptions.Length + cols - 1) / cols;
            float yOwn = y0 + rows * pitch + 8f;
            UiKit.AddText(panel, 32f, yOwn, 640f, 28f, L("ui.shape.custom.section"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            yOwn += 34f;

            if (!HasShapeTool())
            {
                UiKit.AddText(panel, 32f, yOwn, 920f, 44f, L("ui.shape.custom.locked"), 15, UiKit.CyanDim, TextAnchor.UpperLeft)
                    .horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else
            {
                var forms = CustomShapeLibrary.List();
                if (forms.Count == 0)
                {
                    UiKit.AddText(panel, 32f, yOwn, 920f, 44f, L("ui.shape.custom.empty"), 15, UiKit.CyanDim, TextAnchor.UpperLeft)
                        .horizontalOverflow = HorizontalWrapMode.Wrap;
                }

                for (int i = 0; i < forms.Count && i < 7; i++) // one row, like the crafting menu's list scale
                {
                    var (name, voxels) = forms[i];
                    float x = 32f + i * pitch;
                    var b = UiKit.AddButton(panel, x, yOwn, cell, cell, string.Empty, () =>
                    {
                        Game.Network?.SendCustomShapeCraft(_item, voxels, name, stack, _slot);
                        Close();
                    });
                    var cap = UiKit.AddText(b.transform, 2f, cell - 22f, cell - 4f, 20f, name, 12, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UiKit.AddOutline(cap);
                }
            }

            BackClose(panel, 1000f, 740f);
        }

        // --- shared bits ---

        private void Header(Transform panel, string text)
        {
            var head = UiKit.AddText(panel, 32f, 28f, 900f, 40f, text, 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(head);
        }

        private void BackClose(Transform panel, float w, float h)
        {
            UiKit.AddButton(panel, 32f, h - 78f, 180f, 48f, L("ui.hotbar_action.back"), () => Rebuild(BuildRing));
            UiKit.AddButton(panel, w - 212f, h - 78f, 180f, 48f, L("ui.hotbar_action.close"), Close);
        }

        private static void Highlight(Button b)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img != null)
            {
                img.color = UiKit.Cyan;
            }
        }

        private BlocksBeyondTheStars.Shared.Definitions.BlockDefinition HeldBlockDef()
        {
            if (string.IsNullOrEmpty(_item) || Game.Content == null)
            {
                return null;
            }

            string baseKey = ItemKey.Base(_item);
            var item = Game.Content.GetItem(baseKey);
            return item != null && !string.IsNullOrEmpty(item.PlacesBlock) ? Game.Content.GetBlock(item.PlacesBlock) : null;
        }

        private ushort HeldBlockTile()
        {
            var def = HeldBlockDef();
            return def?.NumericId != null ? (ushort)def.NumericId.Value : (ushort)0;
        }

        private bool HasShapeTool()
        {
            foreach (var s in Game.Personal)
            {
                if (ItemKey.Base(s.Item) == "shape_tool")
                {
                    return true;
                }
            }

            return false;
        }

        private int OwnedCount(string baseKey)
        {
            int total = 0;
            foreach (var s in Game.Personal)
            {
                if (ItemKey.Base(s.Item) == baseKey)
                {
                    total += s.Count;
                }
            }

            return total;
        }

        /// <summary>The same icon resolution the hotbar uses (shape silhouette → atlas tile → generated
        /// icon), as a RawImage child so tinted/dyed stacks read in colour here too.</summary>
        private void AddItemIcon(Transform parent, float x, float y, float size, string item)
        {
            var go = new GameObject("ItemIcon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;

            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(ItemKey.Base(item))?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            int shape = ItemKey.Shape(item);
            Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape, Game.CustomShapes)
                : null;
            int design = ItemKey.Design(item);
            if (design != 0 && Game.PaintAtlas != null && Game.PaintAtlas.TryGetUv(design, out var designUv))
            {
                raw.texture = Game.PaintAtlas.Texture; // a painted stack reads by its texture (same rule as the hotbar)
                raw.uvRect = designUv;
            }
            else if (shapeTex != null)
            {
                raw.texture = shapeTex;
            }
            else if (blockDef != null && Game.Atlas != null)
            {
                raw.texture = Game.Atlas.Texture;
                raw.uvRect = Game.Atlas.TileUv(blockDef.NumericId.Value);
            }
            else
            {
                Texture2D itemTex = IconResolver.ItemTexture(item);
                var kind = Game.Content?.GetItem(ItemKey.Base(item))?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                raw.texture = itemTex != null ? itemTex : IconFactory.ForItem(item, kind);
            }

            raw.color = IconResolver.Tint(item, Game);
        }

        private void AddShapeIcon(Transform parent, ushort tileId, int shape, float cell)
        {
            var go = new GameObject("ShapeIcon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, 10f, 8f, cell - 20f, cell - 30f);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            Texture2D tex = shape > 0 && Game.Atlas != null
                ? ShapeIconFactory.ForBlock(Game.Atlas, tileId, shape, Game.CustomShapes)
                : null;
            if (tex != null)
            {
                raw.texture = tex;
            }
            else if (Game.Atlas != null && Game.Atlas.Texture != null)
            {
                raw.texture = Game.Atlas.Texture; // the cube (or an unbuildable silhouette) shows the plain tile
                raw.uvRect = Game.Atlas.TileUv(tileId);
            }
            else
            {
                raw.enabled = false;
            }

            raw.color = IconResolver.Tint(_item, Game);
        }

        /// <summary>A small texture of a saved design (32×32, point-filtered), owned by this panel and
        /// destroyed on close — the paint atlas only holds designs REGISTERED in this save, and a library
        /// entry may not be.</summary>
        private Texture2D DesignPreview(string pixels)
        {
            if (string.IsNullOrEmpty(pixels) || pixels.Length != PaintDesignAtlas.PixelChars)
            {
                return null;
            }

            const int n = PaintDesignAtlas.Size;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var buf = new Color32[n * n];
            var palette = FacePalette.Colors;
            for (int gy = 0; gy < n; gy++)
            {
                for (int gx = 0; gx < n; gx++)
                {
                    int index = FacePalette.ValueOf(pixels[gy * n + gx]);
                    Color32 c = index <= 0 || index >= palette.Length ? PaintDesignAtlas.Canvas : palette[index];
                    c.a = 255;
                    buf[(n - 1 - gy) * n + gx] = c; // grid row 0 = top → texture row 0 = bottom
                }
            }

            tex.SetPixels32(buf);
            tex.Apply();
            _previews.Add(tex);
            return tex;
        }

        private string L(string key) => Game.Localizer?.Get(key) ?? key;

        // Shared helper (#927): the slot's item key carries dyed/glow/shape/paint modifiers, so a raw
        // item.{key}.name lookup would render the bracketed key — the bug this panel shipped with.
        private string ItemName(string item)
            => BlocksBeyondTheStars.Shared.Localization.ItemNames.Display(Game.Localizer, item,
                _customFormName ??= idx => Game.CustomShapes?.NameOf(idx));

        private System.Func<int, string> _customFormName;

        private string ShortName(string item)
        {
            string name = ItemName(item);
            return name.Length > 12 ? name.Substring(0, 11) + "…" : name;
        }
    }
}
