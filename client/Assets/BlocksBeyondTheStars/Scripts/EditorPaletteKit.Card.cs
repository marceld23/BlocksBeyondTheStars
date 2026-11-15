// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a click into a build editor's 3D view does (#3691): place the selected palette entry, or remove what the
    /// click hits. Removing used to need the middle mouse button, which a laptop touchpad does not have.</summary>
    internal enum EditorBuildTool
    {
        Place = 0,
        Remove = 1,
    }

    /// <summary>What <see cref="EditorPaletteKit.AddPaletteCard"/> builds the palette card from.</summary>
    internal sealed class PaletteCardSpec
    {
        /// <summary>The card's kicker ("Blocks & parts").</summary>
        public string Title;

        public IReadOnlyList<EditorPaletteKit.Entry> Palette;

        /// <summary>The entry selected at first (an index into <see cref="Palette"/>).</summary>
        public int Selected;

        /// <summary>The search text to start with.</summary>
        public string Search = string.Empty;

        /// <summary>Called on every selection (also the first one) with the entry index.</summary>
        public Action<int> OnSelected;

        /// <summary>Optional: called on every keystroke of the search field (the list already re-filters itself).</summary>
        public Action<string> OnSearchChanged;

        /// <summary>Optional: called when the player switches the tool (not when <see cref="PaletteCard.SetTool"/> is called
        /// with the tool already on).</summary>
        public Action<EditorBuildTool> OnToolChanged;
    }

    /// <summary>
    /// The left card of a build editor on the <see cref="EditorShell"/> (#3691), as the approved mock draws it: the kicker,
    /// the search field, the <b>Place | Remove</b> switch and the palette as tiles (<see cref="PaletteRowStyle.Tiles"/>: at
    /// least 52 px, names at 16 px or more). Built by <see cref="EditorPaletteKit.AddPaletteCard"/> on a card of the shell
    /// (<see cref="EditorShell.AddSideCard"/>); the editor keeps the placing logic and asks <see cref="Tool"/> what a click
    /// does.
    /// </summary>
    internal sealed class PaletteCard
    {
        /// <summary>The card's inner margin and the rows of its fixed head.</summary>
        internal const float Pad = 16f, SearchY = 46f, ToolY = 100f, ListY = 160f;

        /// <summary>The height of the search field and of the tool switch (at least a tap target).</summary>
        internal const float SearchH = 44f, ToolH = 48f;

        public Transform Root { get; internal set; }

        public InputField Search { get; internal set; }

        /// <summary>The Place | Remove switch (the selected segment is drawn as the template's selected tab).</summary>
        public UiKit.Segmented Tools { get; internal set; }

        public RectTransform ListParent { get; internal set; }

        public PaletteListUi List { get; internal set; }

        internal Action<EditorBuildTool> OnToolChanged;

        /// <summary>The tool a click into the 3D view uses.</summary>
        public EditorBuildTool Tool => Tools != null && Tools.Selected == (int)EditorBuildTool.Remove ? EditorBuildTool.Remove : EditorBuildTool.Place;

        /// <summary>The Place segment — the pad's first focus in a build editor (<see cref="EditorShell.PreferFocus"/>).</summary>
        public Button PlaceButton => Tools.Buttons[(int)EditorBuildTool.Place];

        public Button RemoveButton => Tools.Buttons[(int)EditorBuildTool.Remove];

        /// <summary>Switches the tool (a key, the pad) as a click on the switch would — callback included.</summary>
        public void SetTool(EditorBuildTool tool)
        {
            if (Tool == tool)
            {
                return;
            }

            Tools.Select((int)tool);
            OnToolChanged?.Invoke(tool);
        }
    }

    internal static partial class EditorPaletteKit
    {
        /// <summary>
        /// Fills <paramref name="card"/> (<paramref name="w"/>×<paramref name="h"/>, e.g. <see cref="EditorShell.AddSideCard"/>)
        /// with the build editor's palette (#3691): the kicker, the search field (it re-filters the tiles by itself), the
        /// Place | Remove switch and the scrolling tile list. The structure editor can take the same card.
        /// </summary>
        internal static PaletteCard AddPaletteCard(AppShell shell, Transform card, float w, float h, PaletteCardSpec spec)
        {
            string L(string key) => shell != null ? shell.L(key) : key;
            const float pad = PaletteCard.Pad;
            var result = new PaletteCard { Root = card, OnToolChanged = spec.OnToolChanged };
            UiKit.AddKicker(card, pad, 14f, w - 2f * pad, spec.Title);
            result.Search = UiKit.AddInput(card, pad, PaletteCard.SearchY, w - 2f * pad, PaletteCard.SearchH, spec.Search, v =>
            {
                result.List?.Rebuild(v);
                spec.OnSearchChanged?.Invoke(v);
            }, L("ui.pal.search"));
            result.Tools = UiKit.AddSegmented(card, pad, PaletteCard.ToolY, w - 2f * pad, PaletteCard.ToolH,
                new[] { L("ui.ed.tool_place"), L("ui.ed.tool_remove") }, (int)EditorBuildTool.Place,
                i => result.OnToolChanged?.Invoke((EditorBuildTool)i));
            foreach (var segment in result.Tools.Buttons)
            {
                var label = segment.GetComponentInChildren<Text>(true);
                UiKit.FitLabelSingleLine(label, ToolLabelW(w), PaletteListUi.TileTextMin, 20);
            }

            float listW = w - pad;
            result.ListParent = UiKit.ScrollList(card, pad / 2f, PaletteCard.ListY, listW, h - PaletteCard.ListY - pad / 2f);
            result.List = new PaletteListUi(shell, result.ListParent, spec.Palette, spec.Selected, PaletteRowStyle.Tiles, TileRowWidth(w))
            {
                OnSelected = spec.OnSelected,
            };
            result.List.Rebuild(spec.Search);
            return result;
        }

        /// <summary>The width the card's scroll list gives a tile — the list's width minus its padding (4 left, 12 right).</summary>
        internal static float TileRowWidth(float cardW) => cardW - PaletteCard.Pad - 16f;

        /// <summary>The room of a tool segment's label: half the switch minus the gap and the button's text insets.</summary>
        internal static float ToolLabelW(float cardW) => ((cardW - 2f * PaletteCard.Pad - 8f) / 2f) - 28f;

        /// <summary>True on the frame Delete or X was pressed to remove the block under the pointer — never while a text
        /// field has the keyboard, and never with Ctrl held (that is a shortcut).</summary>
        internal static bool RemoveKeyDown()
        {
            if (UiKit.TextFieldFocused() || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return false;
            }

            return Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X);
        }
    }
}
