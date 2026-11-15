// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The optional header actions of an editor (#3688) — an editor shows only the ones it can do.</summary>
    [Flags]
    public enum EditorShellActions
    {
        None = 0,

        /// <summary>Undo and Redo (always as a pair).</summary>
        Undo = 1,

        /// <summary>Load / open saved work.</summary>
        Load = 2,

        /// <summary>Start new work.</summary>
        New = 4,

        /// <summary>Submit the work to the developers.</summary>
        Submit = 8,

        All = Undo | Load | New | Submit,
    }

    /// <summary>The parts of the editor header bar, left to right.</summary>
    public enum EditorHeaderSlot
    {
        Back,
        Title,
        Undo,
        Redo,
        Load,
        New,
        Submit,
        Save,

        /// <summary>The small line under Save that says what saving does here ("for you", "for the game").</summary>
        SaveSubline,
    }

    /// <summary>
    /// The geometry of the editor shell (#3688, #3695) in the menus' 1920 × 1080 reference space (top-left origin, y down —
    /// <c>UiKit.Place</c>'s frame; <see cref="HudRect"/> serves as the rectangle). A header bar on top — Back top left, the
    /// title, then on the right undo / redo, load / new, submit and the ONE primary Save with its subline — a status bar at
    /// the bottom and the work area between them. Kept Unity-free so the rules (nothing overlaps, every control is at least
    /// <see cref="MinControl"/> high, Save is the rightmost) are tested headless.
    /// <para>
    /// The editor canvases do not follow the player's UI-scale setting: their content is laid out in absolute 1920 × 1080
    /// coordinates like every menu, and a smaller reference would push the right side (Save!) off the screen. The size
    /// minimums hold at the reference instead — controls ≥ 44 px, text ≥ 16 px — which is 1:1 on the school club's Full HD
    /// screens (the maintainer's decision, 2026-11-15: PCs with a mouse at Full HD, no touch in the editors).
    /// </para>
    /// </summary>
    public sealed class EditorShellLayout
    {
        public const float CanvasW = 1920f, CanvasH = 1080f;

        /// <summary>The header bar's height; the buttons sit in its upper part, the Save subline under Save.</summary>
        public const float HeaderH = 88f;

        /// <summary>The status bar's height at the bottom.</summary>
        public const float StatusH = 52f;

        /// <summary>The outer margin of the header, the status bar and the side cards.</summary>
        public const float Margin = 16f;

        public const float ButtonY = 10f, ButtonH = 48f;

        public const float SublineY = 60f, SublineH = 22f;

        /// <summary>How far the Save subline reaches left of Save (it is right-aligned under it).</summary>
        public const float SublineReach = 120f;

        /// <summary>The smallest control (height and width) at the reference — the menus' tap size.</summary>
        public const float MinControl = 44f;

        /// <summary>The smallest font size the shell draws at the reference.</summary>
        public const int MinTextSize = 16;

        public const float BackW = 170f, UndoW = 170f, LoadW = 160f, NewW = 150f, SubmitW = 200f, SaveW = 230f;

        /// <summary>The gap between two buttons of a group, and between two groups.</summary>
        public const float Gap = 12f, GroupGap = 24f;

        /// <summary>The narrowest the title may get — with every action present it is about 500 wide.</summary>
        public const float MinTitleW = 320f;

        private readonly Dictionary<EditorHeaderSlot, HudRect> _slots = new Dictionary<EditorHeaderSlot, HudRect>();
        private readonly List<EditorHeaderSlot> _order = new List<EditorHeaderSlot>();

        private EditorShellLayout(EditorShellActions actions) => Actions = actions;

        /// <summary>The actions this layout has room for.</summary>
        public EditorShellActions Actions { get; }

        /// <summary>The slots present, left to right (the subline last).</summary>
        public IReadOnlyList<EditorHeaderSlot> Order => _order;

        /// <summary>The free area between the header and the status bar — the editor's own content goes here.</summary>
        public static HudRect WorkRect => new HudRect(0f, HeaderH, CanvasW, CanvasH - HeaderH - StatusH);

        /// <summary>The status bar along the bottom edge.</summary>
        public static HudRect StatusRect => new HudRect(0f, CanvasH - StatusH, CanvasW, StatusH);

        /// <summary>The header bar along the top edge.</summary>
        public static HudRect HeaderRect => new HudRect(0f, 0f, CanvasW, HeaderH);

        /// <summary>A side card of <paramref name="width"/> on the left or right edge of the work area, the margin to the
        /// edges, the header and the status bar kept.</summary>
        public static HudRect SideCard(bool right, float width)
        {
            var work = WorkRect;
            float w = Math.Max(MinControl, Math.Min(width, (work.W / 2f) - (Margin * 2f)));
            float x = right ? work.Right - Margin - w : work.X + Margin;
            return new HudRect(x, work.Y + Margin, w, work.H - (Margin * 2f));
        }

        /// <summary>The header for <paramref name="actions"/>: Back and the title on the left, the actions right-aligned
        /// with Save rightmost; missing actions leave no gap.</summary>
        public static EditorShellLayout Compute(EditorShellActions actions)
        {
            var layout = new EditorShellLayout(actions);
            float right = CanvasW - Margin;
            var rightSlots = new List<(EditorHeaderSlot Slot, HudRect Rect)>();

            right = PlaceGroup(rightSlots, right, (EditorHeaderSlot.Save, SaveW));
            if ((actions & EditorShellActions.Submit) != 0)
            {
                right = PlaceGroup(rightSlots, right - GroupGap + Gap, (EditorHeaderSlot.Submit, SubmitW));
            }

            var files = new List<(EditorHeaderSlot, float)>();
            if ((actions & EditorShellActions.New) != 0)
            {
                files.Add((EditorHeaderSlot.New, NewW));
            }

            if ((actions & EditorShellActions.Load) != 0)
            {
                files.Add((EditorHeaderSlot.Load, LoadW));
            }

            if (files.Count > 0)
            {
                right = PlaceGroup(rightSlots, right - GroupGap + Gap, files.ToArray());
            }

            if ((actions & EditorShellActions.Undo) != 0)
            {
                right = PlaceGroup(rightSlots, right - GroupGap + Gap, (EditorHeaderSlot.Redo, UndoW), (EditorHeaderSlot.Undo, UndoW));
            }

            float titleX = Margin + BackW + GroupGap;
            layout.Add(EditorHeaderSlot.Back, new HudRect(Margin, ButtonY, BackW, ButtonH));
            layout.Add(EditorHeaderSlot.Title, new HudRect(titleX, 0f, Math.Max(MinTitleW, right + Gap - GroupGap - titleX), HeaderH));
            for (int i = rightSlots.Count - 1; i >= 0; i--)
            {
                layout.Add(rightSlots[i].Slot, rightSlots[i].Rect);
            }

            // Right-aligned under Save; it may reach left under the next button (the buttons end above it).
            var save = layout.Rect(EditorHeaderSlot.Save);
            layout.Add(EditorHeaderSlot.SaveSubline, new HudRect(save.X - SublineReach, SublineY, save.W + SublineReach, SublineH));
            return layout;
        }

        /// <summary>True when the slot is part of this header.</summary>
        public bool Has(EditorHeaderSlot slot) => _slots.ContainsKey(slot);

        /// <summary>The slot's rectangle; throws for a slot this header does not have (see <see cref="Has"/>).</summary>
        public HudRect Rect(EditorHeaderSlot slot)
            => _slots.TryGetValue(slot, out var rect) ? rect : throw new KeyNotFoundException("The header has no " + slot + ".");

        private void Add(EditorHeaderSlot slot, HudRect rect)
        {
            _slots[slot] = rect;
            _order.Add(slot);
        }

        /// <summary>Places the buttons of one group right to left, ending at <paramref name="right"/>; returns the new right
        /// edge for the next group (the group's left edge minus a button gap).</summary>
        private static float PlaceGroup(List<(EditorHeaderSlot, HudRect)> slots, float right,
            params (EditorHeaderSlot Slot, float W)[] buttons)
        {
            foreach (var (slot, w) in buttons)
            {
                var rect = new HudRect(right - w, ButtonY, w, ButtonH);
                slots.Add((slot, rect));
                right = rect.X - Gap;
            }

            return right;
        }
    }
}
