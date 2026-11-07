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
    /// The library column beside the canvas — the outfit shelf of the character mode (#1047) and the frame library of the
    /// HUD mode (#3318) are the same column: a name field, Save, Rename, one row per saved entry with a ✕ to drop it, the
    /// status line, and under it the read-only template rows (skin templates #3321, frame templates #3320) — templates can
    /// be put on, never deleted or renamed. Clicking a row PUTS IT ON, which is why the column lives in the editor rather
    /// than on a menu card: this is where a player finds out whether they like it.
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
            public Text Status, Empty, TemplatesTitle;
            public InputField NameField;
            public string Typed = string.Empty;
            public int Selected = -1;
            public readonly List<Text> Labels = new List<Text>();
        }

        private const float ShelfW = 226f;

        /// <summary>Template rows fit between the status line (636–726) and the hint line (934).</summary>
        private const int MaxTemplateRows = 3;

        private Shelf _outfitShelf;

        /// <summary>The outfit shelf (the character mode's column).</summary>
        private void BuildOutfitColumn(Transform panel, float x)
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
            BuildShelf(panel, x, _outfitShelf);
        }

        private void BuildShelf(Transform panel, float x, Shelf shelf)
        {
            UiKit.AddText(panel, x, 64f, ShelfW, 24f, L(shelf.TitleKey), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            shelf.NameField = UiKit.AddInput(panel, x, 92f, ShelfW, 42f, shelf.Typed, v => shelf.Typed = v, L(shelf.NameKey), 24, 16);
            UiKit.AddFitButton(panel, x, 142f, ShelfW, 44f, L(shelf.SaveKey), () => SaveShelfPressed(shelf));
            UiKit.AddFitButton(panel, x, 194f, ShelfW, 40f, L(shelf.RenameKey), () => RenameShelfPressed(shelf));
            shelf.List = AddArea(panel, shelf.ListName, x, 246f, ShelfW, 380f);

            shelf.Empty = UiKit.AddText(panel, x, 252f, ShelfW, 90f, L(shelf.EmptyKey), 13, UiKit.TextCol, TextAnchor.UpperLeft); // an instruction (#3263)
            shelf.Empty.horizontalOverflow = HorizontalWrapMode.Wrap;

            shelf.Status = UiKit.AddText(panel, x, 636f, ShelfW, 90f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            shelf.Status.horizontalOverflow = HorizontalWrapMode.Wrap;

            if (shelf.TemplateNames != null)
            {
                shelf.TemplatesTitle = UiKit.AddText(panel, x, 734f, ShelfW, 22f, L(shelf.TemplatesKey), 15, UiKit.CyanDim,
                    TextAnchor.MiddleLeft, FontStyle.Bold);
                shelf.TemplateList = AddArea(panel, shelf.ListName + "Templates", x, 760f, ShelfW, MaxTemplateRows * 46f);
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

        /// <summary>(Re)fills the rows from the host. Cheap — the list is capped at a handful of rows and only rebuilt
        /// when one of them changes.</summary>
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

            float y = 0f;
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i;
                string name = string.IsNullOrEmpty(names[i]) ? "?" : names[i];
                var wear = UiKit.AddFitButton(shelf.List, 0f, y, 170f, 40f, name, () => WearShelfPressed(shelf, idx));
                var label = wear.GetComponentInChildren<Text>();
                label.color = idx == shelf.Selected ? UiKit.Cyan : UiKit.TextCol;
                shelf.Labels.Add(label);
                UiKit.AddFitButton(shelf.List, 178f, y, 48f, 40f, "✕", () => DeleteShelfPressed(shelf, idx));
                y += 46f;
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
                UiKit.AddFitButton(shelf.TemplateList, 0f, i * 46f, ShelfW, 40f, names[i], () => WearTemplatePressed(shelf, idx));
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

        private static void SetShelfStatus(Shelf shelf, string text)
        {
            if (shelf?.Status != null)
            {
                shelf.Status.text = text ?? string.Empty;
            }
        }

        /// <summary>Puts a saved entry on. The host swaps everything under the editor, so the subjects are re-read from it
        /// afterwards — otherwise the canvas would still hold the old paint and write it straight back on the next switch.</summary>
        private void WearShelfPressed(Shelf shelf, int index)
        {
            if (shelf.OnWear == null)
            {
                return;
            }

            string status = shelf.OnWear(index);
            shelf.AfterWear?.Invoke();
            SelectShelfRow(shelf, index);
            var names = shelf.Names?.Invoke();
            if (names != null && index >= 0 && index < names.Count && shelf.NameField != null)
            {
                shelf.Typed = names[index];
                shelf.NameField.text = shelf.Typed; // so Rename acts on a name the player can see and edit
            }

            SetShelfStatus(shelf, status);
        }

        /// <summary>Puts a template on — a starting point copied into the player's own look, never a saved row.</summary>
        private void WearTemplatePressed(Shelf shelf, int index)
        {
            if (shelf.OnWearTemplate == null)
            {
                return;
            }

            string status = shelf.OnWearTemplate(index);
            shelf.AfterWear?.Invoke();
            SelectShelfRow(shelf, -1);
            SetShelfStatus(shelf, status);
        }

        private void SaveShelfPressed(Shelf shelf)
        {
            if (shelf.OnSave == null)
            {
                return;
            }

            SetShelfStatus(shelf, shelf.OnSave(shelf.Typed ?? string.Empty));
            RebuildShelf(shelf);
        }

        private void RenameShelfPressed(Shelf shelf)
        {
            if (shelf.OnRename == null)
            {
                return;
            }

            SetShelfStatus(shelf, shelf.OnRename(shelf.Selected, shelf.Typed ?? string.Empty));
            RebuildShelf(shelf);
        }

        private void DeleteShelfPressed(Shelf shelf, int index)
        {
            if (shelf.OnDelete == null)
            {
                return;
            }

            SetShelfStatus(shelf, shelf.OnDelete(index));
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
