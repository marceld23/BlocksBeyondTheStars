// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shaping-tool host (#845) — the form twin of <see cref="PaintToolUi"/>. Right-clicking with the
    /// held <c>shape_tool</c> opens the <see cref="ShapeEditor"/>: aimed at nothing it starts on an empty
    /// grid, aimed at a placed block carrying a player-designed form it opens PRE-LOADED with that form,
    /// which is how a form is copied off someone else's build (#846) — the same gesture that copies a paint
    /// design off a painted block. Player control freezes + the cursor frees through the same menu-owner
    /// arbiter the paint editor uses.
    /// </summary>
    public sealed class ShapeToolUi : MonoBehaviour
    {
        public static ShapeToolUi Instance { get; private set; }
        public GameBootstrap Game;

        private ShapeEditor _editor;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_editor != null)
            {
                Destroy(_editor.gameObject);
            }
        }

        /// <summary>True while the form editor is open (gameplay hotkeys should stand down).</summary>
        public bool IsOpen => _editor != null;

        /// <summary>Opens the editor on an empty grid — the "design something new" entry point.</summary>
        public void OpenNew() => Open(string.Empty, string.Empty);

        /// <summary>Opens the editor pre-loaded with the form of a placed cell, if it carries one. Returns
        /// false when that block is an ordinary one, so the caller can fall back to the empty grid.</summary>
        public bool OpenForCell(Vector3Int cell)
        {
            if (Game?.World == null)
            {
                return false;
            }

            int shapeIndex = ShapeCode.ShapeOf(Game.World.GetShape(cell.x, cell.y, cell.z));
            if (!ShapeCode.IsCustomShape(shapeIndex) || !Game.CustomShapes.TryGetVoxels(shapeIndex, out string voxels))
            {
                return false;
            }

            // Credit whoever designed it — a copied form should not quietly become yours.
            string owner = Game.CustomShapes.OwnerOf(shapeIndex);
            string name = Game.CustomShapes.NameOf(shapeIndex);
            if (!string.IsNullOrEmpty(owner))
            {
                name = string.Format(Game.Localizer?.Get("ui.shape.custom.copied_from") ?? "{0} (by {1})", name, owner);
            }

            Open(voxels, name);
            return true;
        }

        private void Open(string voxels, string name)
        {
            if (_editor != null || Game == null)
            {
                return;
            }

            var go = new GameObject("ShapeEditor");
            _editor = go.AddComponent<ShapeEditor>();
            _editor.Game = Game;
            _editor.InitialVoxels = voxels;
            _editor.InitialName = name;
            _editor.Localizer = key => Game.Localizer?.Get(key) ?? key;
            // Opened from the TOOL the player is holding the tool, not a material — so "apply" here means
            // "keep it": the form lands in the library and is crafted later in the menu, where a material is
            // selected. (The crafting tab's own editor host does craft directly; see CraftingTechShipUI.)
            _editor.ApplyLabelKey = "ui.shape.custom.keep";
            _editor.OnApply = SaveAndTell;
            _editor.OnSaveDesign = CustomShapeLibrary.Save;
            _editor.LibraryProvider = CustomShapeLibrary.List;
            _editor.OnClosed = () => { _editor = null; Game?.SetMenuOwner(this, false); };
            Game.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
        }

        /// <summary>Keeps the drawn form in the local library and points the player at where it becomes real
        /// material — the crafting menu, which is the only place a material is actually selected.</summary>
        private void SaveAndTell(string voxels, string name)
        {
            CustomShapeLibrary.Save(voxels, name);
            Game?.ShowMessage(Game.Localizer?.Get("ui.shape.custom.saved") ?? "Form saved — craft it under \"My forms\".");
        }

        /// <summary>Right-clicking a stencil that carries a form (#846): the form goes into the receiver's
        /// library, which is the whole point of handing one over. A blank stencil opens the editor instead,
        /// so the item is never a dead end in the hand.</summary>
        public void UseStencil(string itemKey)
        {
            int shapeIndex = ItemKey.Shape(itemKey);
            if (!ShapeCode.IsCustomShape(shapeIndex) || Game?.CustomShapes == null
                || !Game.CustomShapes.TryGetVoxels(shapeIndex, out string voxels))
            {
                OpenNew();
                return;
            }

            string owner = Game.CustomShapes.OwnerOf(shapeIndex);
            string name = Game.CustomShapes.NameOf(shapeIndex);
            if (!string.IsNullOrEmpty(owner))
            {
                name = string.Format(Game.Localizer?.Get("ui.shape.custom.copied_from") ?? "{0} (by {1})", name, owner);
            }

            CustomShapeLibrary.Save(voxels, name);
            Game.ShowMessage(Game.Localizer?.Get("ui.shape.custom.imported") ?? "Form added to your library.");
        }
    }
}
