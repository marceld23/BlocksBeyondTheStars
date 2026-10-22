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
    /// The gear strip of <see cref="HudUi"/> (#2290): one button per worn ACTIVE piece beside the quick-bar, on its own
    /// plain overlay canvas.
    /// </summary>
    public sealed partial class HudUi
    {
        // The gear strip (#2290): one small button per worn ACTIVE piece beside the quick-bar — see RefreshGearStrip. It
        // lives on its own plain overlay canvas: the HUD canvas may render through the visor camera, where a tap lands
        // wrong, and on a tablet the touch layer's full-screen look pad (sort 100) would swallow every tap below it.
        /// <summary>The worn ACTIVE gear, in the strip's order — the Suit tab's "Can be used actively" list reads it too.</summary>
        internal static readonly string[] ActiveGear = { "suit_lamp", "jetpack", "glider", "stealth_suit" };

        private sealed class GearCell
        {
            public string Key;
            public GameObject Go;
            public Image Frame;
            public RawImage Icon;
            public TMP_Text Label;
            public Image EnergyFill; // the jetpack's and the cloak's suit energy; null on the lamp and the glider
        }

        private const float GearCellSize = 48f, GearPitch = 58f;
        private static readonly Color GearOffTint = new Color(0.62f, 0.72f, 0.82f, 0.55f);
        private Canvas _gearCanvas;
        private readonly System.Collections.Generic.List<GearCell> _gearCells = new();
        private object _gearEquipment;        // the Game.Equipment array the worn set below was read from
        private int _gearWorn, _gearBuiltFor = -1; // bit i = ActiveGear[i] worn; the set the cells were built for
        private bool _gearTouchLayout;
        private object _gearLoc;

        private void CreateGearCanvas()
        {
            // The gear strip (#2290): its own plain canvas in the HUD's reference space (see the fields); cells on demand.
            _gearCanvas = UiKit.CreateCanvas("HudGearStrip", W, H, userScalable: true);
            _gearCanvas.sortingOrder = 11;
            _gearCanvas.enabled = false;
        }

        // --- gear strip (#2290) ---

        /// <summary>The gear strip (#2290): one small button per worn ACTIVE piece — lamp, jetpack, glider, stealth suit — in
        /// line with the quick-bar past its right end (above its left end while the touch controls are up: their ► / … /
        /// DOWN / USE buttons own the right). Lit while the piece is on, dimmed while off; its control under it (a "hold"
        /// mark on the two that ride the jump); the suit energy left on the two that spend it. A tap toggles the lamp or
        /// the cloak, or says how the jetpack and the glider are used — on a tablet; on the desktop the cursor is locked.
        /// Hidden with the quick-bar, while dead / respawning / loading, and while nothing active is worn. The cells are
        /// rebuilt only when the worn set, the layout or the language changes; their states follow at the refresh rate.</summary>
        private void RefreshGearStrip(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            if (_gearCanvas == null)
            {
                return;
            }

            // The worn set follows the equipment snapshot — a new array per inventory update.
            if (!ReferenceEquals(Game.Equipment, _gearEquipment))
            {
                _gearEquipment = Game.Equipment;
                _gearWorn = 0;
                for (int i = 0; i < ActiveGear.Length; i++)
                {
                    if (Game.Wears(ActiveGear[i]))
                    {
                        _gearWorn |= 1 << i;
                    }
                }
            }

            bool show = _gearWorn != 0 && _hotbarRoot != null && _hotbarRoot.activeSelf && Game.Health > 0f
                        && !Game.AwaitingRespawnConfirm && (_veil == null || !_veil.VeilActive);
            if (_gearCanvas.enabled != show)
            {
                _gearCanvas.enabled = show;
            }

            if (!show)
            {
                return;
            }

            var touchUi = TouchControlsUi.Active;
            bool touch = touchUi != null && touchUi.Visible;
            if (_gearWorn != _gearBuiltFor || touch != _gearTouchLayout || !ReferenceEquals(loc, _gearLoc))
            {
                BuildGearStrip(loc, touch);
            }

            if (_playerRig == null)
            {
                _playerRig = FindAnyObjectByType<PlayerController>(); // the rig WorldRig creates
            }

            float energy = Game.SuitEnergy / Mathf.Max(1f, Game.SuitEnergyMax); // #2297: the worn battery's full mark
            foreach (var cell in _gearCells)
            {
                bool on = GearOn(cell.Key);
                cell.Frame.color = on ? UiKit.SlotSelected : UiKit.SlotIdle;
                cell.Icon.color = on ? Color.white : GearOffTint;
                cell.Label.color = on ? UiKit.Cyan : UiKit.CyanDim;
                string label = GearLabel(loc, cell.Key, touch);
                if (cell.Label.text != label)
                {
                    cell.Label.text = label;
                }

                if (cell.EnergyFill != null)
                {
                    UiHolo.SetBar(cell.EnergyFill, energy, GearCellSize - 12f);
                }
            }
        }

        /// <summary>Lays the cells out for the worn set — see <see cref="RefreshGearStrip"/> for where and why.</summary>
        private void BuildGearStrip(BlocksBeyondTheStars.Shared.Localization.Localizer loc, bool touch)
        {
            foreach (var cell in _gearCells)
            {
                Destroy(cell.Go);
            }

            _gearCells.Clear();
            _gearBuiltFor = _gearWorn;
            _gearTouchLayout = touch;
            _gearLoc = loc;

            // Above the touch layer (100) while it is up, so a tap reaches the strip and not the look pad under it; just
            // over the HUD otherwise, below every dialog.
            _gearCanvas.sortingOrder = touch ? 101 : 11;

            // Desktop / pad: past the quick-bar backplate's right end, level with its cells. Touch: above the bar's left
            // end, right of the ◄ button — the bar's right is the thumb zone there.
            float x = touch ? _hotbarX0 + 112f : _pickupRightX + 12f;
            float y = touch ? _hotbarY - 82f : _hotbarY + 4f;
            for (int i = 0; i < ActiveGear.Length; i++)
            {
                if ((_gearWorn & (1 << i)) != 0)
                {
                    _gearCells.Add(MakeGearCell(loc, x, y, ActiveGear[i]));
                    x += GearPitch;
                }
            }
        }

        private GearCell MakeGearCell(BlocksBeyondTheStars.Shared.Localization.Localizer loc, float x, float y, string key)
        {
            var go = new GameObject("GearCell_" + key, typeof(RectTransform));
            go.transform.SetParent(_gearCanvas.transform, false);
            UiKit.Place(go, x, y, GearCellSize, GearCellSize + 16f);

            var frame = UiHolo.AddPanel(go.transform, 0f, 0f, GearCellSize, GearCellSize, UiKit.SlotIdle, 8f, 1f, 0.45f);
            frame.raycastTarget = true; // the tap target
            var button = frame.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; // never a pad stop: no menu owns this canvas
            button.onClick.AddListener(() => OnGearTapped(key));

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(frame.transform, false);
            UiKit.Place(iconGo, 6f, 4f, GearCellSize - 12f, GearCellSize - 12f);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            var tex = IconResolver.ItemTexture(key);
            icon.texture = tex != null ? tex : IconFactory.ForItem(key, BlocksBeyondTheStars.Shared.Definitions.ToolKind.None);

            Image energy = null;
            if (key == "jetpack" || key == "stealth_suit")
            {
                energy = UiHolo.AddBar(frame.transform, 6f, GearCellSize - 7f, GearCellSize - 12f, 3f,
                    new Color(0.03f, 0.07f, 0.13f, 0.9f), Energy).Fill;
            }

            if (key == "jetpack" || key == "glider")
            {
                // The "hold" mark: these two are no switch — they work while the jump is held.
                UiText.Add(frame.transform, 3f, 1f, GearCellSize - 6f, 12f, loc.Get("ui.hud.gear_hold"), 9, UiKit.TextCol,
                    TextAnchor.UpperLeft, FontStyle.Bold, UiText.Look.Outline);
            }

            var label = UiText.Add(go.transform, -5f, GearCellSize + 1f, GearCellSize + 10f, 14f, string.Empty, 11, UiKit.CyanDim,
                TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Outline);
            label.enableAutoSizing = true; // "Leertaste" / "(Cross)" must fit under a 48 px cell
            label.fontSizeMin = 7f;
            label.fontSizeMax = 11f;
            return new GearCell { Key = key, Go = go, Frame = frame, Icon = icon, Label = label, EnergyFill = energy };
        }

        /// <summary>Whether a strip piece is on: the lamp lit, the jetpack firing, the glider open (the controller's
        /// state), the cloak up (the server's answer).</summary>
        private bool GearOn(string key)
        {
            switch (key)
            {
                case "suit_lamp":
                    return _playerRig != null && _playerRig.LampOn;
                case "jetpack":
                    return _playerRig != null && _playerRig.JetpackActive;
                case "glider":
                    return _playerRig != null && _playerRig.Gliding;
                default:
                    return Game.Stealthed;
            }
        }

        /// <summary>The control under a strip cell: the lamp's and the cloak's key (nothing on a tablet — the cell itself is
        /// the button there), the jump for the jetpack and the glider.</summary>
        private static string GearLabel(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key, bool touch)
        {
            switch (key)
            {
                case "suit_lamp":
                    return touch ? string.Empty : ActionGlyph(loc, InputAction.ToggleLamp);
                case "stealth_suit":
                    return touch ? string.Empty : ActionGlyph(loc, InputAction.ToggleStealth);
                default:
                    return JumpGlyph(loc);
            }
        }

        /// <summary>A tap on a strip cell (#2290): the lamp and the cloak toggle like their keys; the jetpack and the glider
        /// have nothing to switch, so the tap says how they are used.</summary>
        private void OnGearTapped(string key)
        {
            var loc = Game?.Localizer;
            if (loc == null)
            {
                return;
            }

            if (_playerRig == null)
            {
                _playerRig = FindAnyObjectByType<PlayerController>();
            }

            switch (key)
            {
                case "suit_lamp":
                    if (_playerRig != null)
                    {
                        _playerRig.ToggleLamp();
                    }

                    break;
                case "stealth_suit":
                    if (_playerRig != null)
                    {
                        _playerRig.ToggleStealth();
                    }

                    break;
                default:
                    Game.ShowMessage(loc.Get(key == "jetpack" ? "ui.hud.gear_jetpack_hint" : "ui.hud.gear_glider_hint")
                        .Replace("{key}", JumpGlyph(loc)));
                    break;
            }
        }
    }
}
