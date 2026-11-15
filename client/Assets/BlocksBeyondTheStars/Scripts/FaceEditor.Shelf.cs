// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The library card beside the canvas — the outfit shelf of the character mode (#1047) and the frame library of the
    /// HUD mode (#3318) are the same card: a name field, "Save outfit" and Rename, one row per saved entry with "Wear" and a
    /// red ✕ to drop it, and under it the read-only template rows (skin templates #3321, frame templates #3320) —
    /// templates can be put on, never deleted or renamed. Wearing PUTS IT ON, which is why the card lives in the editor
    /// rather than on a menu card: this is where a player finds out whether they like it.
    /// <para>
    /// #3693: deleting an entry asks first (it is gone for good); putting an outfit on over unsaved paint asks too (the
    /// look is swapped under the editor, which cannot be undone); the column's messages go to the shell's status bar.
    /// </para>
    /// </summary>
    public sealed partial class FaceEditor
    {
        // Outfit shelf hooks beyond the outfits themselves (#3321): the host's skin templates. Null = none.
        public Func<List<string>> SkinTemplateNames;   // localized names, in the host's order
        public Func<int, string> OnWearSkinTemplate;   // index → status

        /// <summary>One library column and its state.</summary>
        private sealed class Shelf
        {
            public string ListName, TitleKey, NameKey, SaveKey, RenameKey, EmptyKey, TemplatesKey;
            public Func<List<string>> Names;
            public Func<string, string> OnSave;
            public Func<int, string> OnWear, OnDelete;
            public Func<int, string, string> OnRename;
            public Func<List<string>> TemplateNames;
            public Func<int, string> OnWearTemplate;
            public Action AfterWear;            // re-read the editor's subjects after the host swapped the look

            public RectTransform List, TemplateList;
            public Text Empty, TemplatesTitle;
            public InputField NameField;
            public string Typed = string.Empty;
            public int Selected = -1;
            public readonly List<Text> Labels = new List<Text>();
        }

        // Inside the outfits card (440 wide): one column 392 wide; eight rows of 52 (MaxOutfits / MaxHudFrameDesigns).
        private const float ShelfColX = 24f, ShelfW = 392f, ShelfRowH = 44f, ShelfRowPitch = 52f, ShelfListY = 160f;
        private const float ShelfNameW = 196f, ShelfWearW = 140f;

        /// <summary>Template rows fit under the eight entry rows, inside the card.</summary>
        private const int MaxTemplateRows = 3;

        /// <summary>Status lines of the hosts that mean "look here" (amber), not news — the rest is neutral information.</summary>
        private static readonly string[] ShelfWarningKeys =
        {
            "ui.avatar.need_name", "ui.avatar.outfit_limit", "ui.avatar.outfit_select_first", "ui.avatar.outfit_name_taken",
            "ui.hudframe.need_name", "ui.hudframe.limit", "ui.hudframe.select_first", "ui.hudframe.name_taken", "ui.hudframe.empty",
        };

        private Shelf _outfitShelf;

        /// <summary>The outfit shelf (the character mode's column).</summary>
        private void BuildOutfitColumn(Transform card)
        {
            _outfitShelf = new Shelf
            {
                ListName = "OutfitList",
                TitleKey = "ui.avatar.outfits",
                NameKey = "ui.avatar.outfit_name",
                SaveKey = "ui.avatar.outfit_save",
                RenameKey = "ui.avatar.outfit_rename",
                EmptyKey = "ui.avatar.outfit_none",
                TemplatesKey = "ui.avatar.skin_templates",
                Names = OutfitNames,
                OnSave = OnSaveOutfit,
                OnWear = OnWearOutfit,
                OnDelete = OnDeleteOutfit,
                OnRename = OnRenameOutfit,
                TemplateNames = SkinTemplateNames,
                OnWearTemplate = OnWearSkinTemplate,
                AfterWear = ReloadSubjects,
            };
            BuildShelf(card, _outfitShelf);
        }

        private void BuildShelf(Transform card, Shelf shelf)
        {
            const float x = ShelfColX, w = ShelfW, half = (ShelfW - 8f) / 2f;
            UiKit.AddKicker(card, x, 20f, w, L(shelf.TitleKey));
            shelf.NameField = UiKit.AddInput(card, x, 52f, w, 44f, shelf.Typed, v => shelf.Typed = v, L(shelf.NameKey), 24, 18);
            UiKit.AddTierButton(card, x, 104f, half, 44f, L(shelf.SaveKey), () => SaveShelfPressed(shelf), ButtonTier.Secondary);
            UiKit.AddTierButton(card, x + half + 8f, 104f, half, 44f, L(shelf.RenameKey), () => RenameShelfPressed(shelf), ButtonTier.Quiet);
            shelf.List = AddArea(card, shelf.ListName, x, ShelfListY, w, 8f * ShelfRowPitch);

            // An instruction (#3263): normal colour, as many lines as the language needs.
            shelf.Empty = UiKit.AddText(card, x, ShelfListY + 6f, w, 120f, L(shelf.EmptyKey), 16, UiKit.TextCol, TextAnchor.UpperLeft);
            shelf.Empty.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (shelf.TemplateNames != null)
            {
                float ty = ShelfListY + (8f * ShelfRowPitch) + 16f;
                shelf.TemplatesTitle = UiKit.AddKicker(card, x, ty, w, L(shelf.TemplatesKey));
                shelf.TemplateList = AddArea(card, shelf.ListName + "Templates", x, ty + 30f, w, MaxTemplateRows * ShelfRowPitch);
            }

            RebuildShelf(shelf);
        }

        /// <summary>An empty, top-left placed rectangle to build rows into.</summary>
        private static RectTransform AddArea(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiKit.Place(go, x, y, w, h);
        }

        private static void ClearChildren(RectTransform area)
        {
            for (int i = area.childCount - 1; i >= 0; i--)
            {
                Destroy(area.GetChild(i).gameObject);
            }
        }

        /// <summary>(Re)fills the rows from the host: the name, "Wear", a red ✕. Cheap — the list is capped at eight rows
        /// and only rebuilt when one of them changes.</summary>
        private void RebuildShelf(Shelf shelf)
        {
            if (shelf?.List == null)
            {
                return;
            }

            ClearChildren(shelf.List);
            shelf.Labels.Clear();
            var names = shelf.Names?.Invoke() ?? new List<string>();
            if (shelf.Selected >= names.Count)
            {
                shelf.Selected = -1;
            }

            if (shelf.Empty != null)
            {
                shelf.Empty.gameObject.SetActive(names.Count == 0);
            }

            for (int i = 0; i < names.Count && i < 8; i++)
            {
                int idx = i;
                float y = i * ShelfRowPitch;
                string name = string.IsNullOrEmpty(names[i]) ? "?" : names[i];
                var label = UiKit.AddText(shelf.List, 4f, y, ShelfNameW - 8f, ShelfRowH, name, 18, idx == shelf.Selected ? UiKit.Cyan : UiKit.TextCol,
                    TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(label, ShelfNameW - 8f, UiKit.EditorMinFontSize, 18);
                shelf.Labels.Add(label);
                UiKit.AddTierButton(shelf.List, ShelfNameW, y, ShelfWearW, ShelfRowH, L("ui.avatar.wear"), () => WearShelfPressed(shelf, idx),
                    ButtonTier.Quiet);
                UiKit.AddTierButton(shelf.List, ShelfW - ShelfRowH, y, ShelfRowH, ShelfRowH, "✕", () => DeleteShelfPressed(shelf, idx),
                    ButtonTier.Danger);
            }

            RebuildShelfTemplates(shelf);
        }

        /// <summary>The template rows: put on with a click, never deleted. The heading goes when there is none.</summary>
        private void RebuildShelfTemplates(Shelf shelf)
        {
            if (shelf.TemplateList == null)
            {
                return;
            }

            ClearChildren(shelf.TemplateList);
            var names = shelf.TemplateNames?.Invoke() ?? new List<string>();
            if (shelf.TemplatesTitle != null)
            {
                shelf.TemplatesTitle.gameObject.SetActive(names.Count > 0);
            }

            for (int i = 0; i < names.Count && i < MaxTemplateRows; i++)
            {
                int idx = i;
                UiKit.AddTierButton(shelf.TemplateList, 0f, i * ShelfRowPitch, ShelfW, ShelfRowH, names[i], () => WearTemplatePressed(shelf, idx),
                    ButtonTier.Quiet);
            }
        }

        private void SelectShelfRow(Shelf shelf, int index)
        {
            shelf.Selected = index;
            for (int i = 0; i < shelf.Labels.Count; i++)
            {
                if (shelf.Labels[i] != null)
                {
                    shelf.Labels[i].color = i == index ? UiKit.Cyan : UiKit.TextCol;
                }
            }
        }

        /// <summary>A host's line in the status bar: amber when it asks the player to do something, else neutral.</summary>
        private void SetShelfStatus(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                ShowStatus(text, ShelfSeverity(text));
            }
        }

        /// <summary>The hosts return their lines already localized; a line that starts like one of the warning texts (up to
        /// its first placeholder) is a warning.</summary>
        private EditorStatusSeverity ShelfSeverity(string text)
        {
            foreach (string key in ShelfWarningKeys)
            {
                string pattern = L(key);
                int brace = pattern.IndexOf('{');
                string head = brace >= 0 ? pattern.Substring(0, brace) : pattern;
                if (head.Length >= 4 && text.StartsWith(head, StringComparison.Ordinal))
                {
                    return EditorStatusSeverity.Warning;
                }
            }

            return EditorStatusSeverity.Info;
        }

        /// <summary>Puts a saved entry on. An outfit swaps the whole look under the editor (not undoable), so over unsaved
        /// paint it asks first; a frame lands on the canvas as one undoable step and needs no question.</summary>
        private void WearShelfPressed(Shelf shelf, int index)
        {
            if (shelf.OnWear == null)
            {
                return;
            }

            if (shelf.AfterWear != null)
            {
                _shell.ConfirmDiscard(() => WearShelfEntry(shelf, index));
            }
            else
            {
                WearShelfEntry(shelf, index);
            }
        }

        /// <summary>The host swaps everything under the editor, so the subjects are re-read from it afterwards — otherwise the
        /// canvas would still hold the old paint and write it straight back on the next switch.</summary>
        private void WearShelfEntry(Shelf shelf, int index)
        {
            EndStroke();
            string status = shelf.OnWear(index);
            shelf.AfterWear?.Invoke();
            SelectShelfRow(shelf, index);
            var names = shelf.Names?.Invoke();
            if (names != null && index >= 0 && index < names.Count && shelf.NameField != null)
            {
                shelf.Typed = names[index];
                shelf.NameField.text = shelf.Typed; // so Rename acts on a name the player can see and edit
            }

            SetShelfStatus(status);
        }

        /// <summary>Puts a template on — a starting point copied into the player's own look, never a saved row.</summary>
        private void WearTemplatePressed(Shelf shelf, int index)
        {
            if (shelf.OnWearTemplate == null)
            {
                return;
            }

            void Wear()
            {
                EndStroke();
                string status = shelf.OnWearTemplate(index);
                shelf.AfterWear?.Invoke();
                SelectShelfRow(shelf, -1);
                SetShelfStatus(status);
            }

            if (shelf.AfterWear != null)
            {
                _shell.ConfirmDiscard(Wear);
            }
            else
            {
                Wear();
            }
        }

        /// <summary>Keeps the look under the typed name. The canvas is committed first, so the outfit holds what is on
        /// screen.</summary>
        private void SaveShelfPressed(Shelf shelf)
        {
            if (shelf.OnSave == null)
            {
                return;
            }

            EndStroke();
            CommitActive();
            SetShelfStatus(shelf.OnSave(shelf.Typed ?? string.Empty));
            RebuildShelf(shelf);
        }

        private void RenameShelfPressed(Shelf shelf)
        {
            if (shelf.OnRename == null)
            {
                return;
            }

            SetShelfStatus(shelf.OnRename(shelf.Selected, shelf.Typed ?? string.Empty));
            RebuildShelf(shelf);
        }

        /// <summary>The red ✕: a saved entry is gone for good, so it asks first (#3690).</summary>
        private void DeleteShelfPressed(Shelf shelf, int index)
        {
            if (shelf.OnDelete == null)
            {
                return;
            }

            var names = shelf.Names?.Invoke();
            string name = names != null && index >= 0 && index < names.Count ? names[index] : string.Empty;
            _shell.ConfirmDelete(name, () => DeleteShelfEntry(shelf, index));
        }

        private void DeleteShelfEntry(Shelf shelf, int index)
        {
            SetShelfStatus(shelf.OnDelete(index));
            if (shelf.Selected == index)
            {
                shelf.Selected = -1;
            }
            else if (shelf.Selected > index)
            {
                shelf.Selected--;
            }

            RebuildShelf(shelf);
        }
    }
}
