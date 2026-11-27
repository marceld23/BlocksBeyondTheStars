// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the Ship Designer on the editor shell (#3691, <c>ShipEditor.Ui.cs</c>) — the palette card
/// (<c>EditorPaletteKit.AddPaletteCard</c> on a 400 px side card), the ship card (380 px, rows 340 wide, 16 px and up) and
/// the ship's lines in the shell's status bar.</summary>
public sealed partial class EditorTextFitTests
{
    // The palette card: 400 wide, its scroll list 384 (a tile row 368), a tile's name from x 56 to 8 px before the edge; the
    // tool switch's two segments (400 − 2 × 16 − 8) / 2 = 180 wide, minus the button's 28 px of text insets.
    private const float PaletteCardInnerW = 368f, TileNameW = 304f, ToolLabelW = 152f;

    // The ship card: a 364 px scroll list, rows of 340; a stepper's label column 176; the selected part's name from x 50.
    private const float ShipRowW = 340f, ShipStepperLabelW = 176f, ShipPartNameW = 294f;

    // The shell (EditorShellLayout): Save 230 + the subline's 120 px reach; the status text 1140, the key line 680.
    private const float SaveSublineW = 350f, StatusTextW = 1140f, HintW = 680f;

    private static IEnumerable<FitSite> ShipSites() => new[]
    {
        // The palette card.
        FitLine("Palette card title (368)", PaletteCardInnerW, 16, 16, "ui.ship.palette"),
        Input("Palette card search (368)", PaletteCardInnerW, 18, "ui.pal.search"),
        FitLine("Palette card Place | Remove (152)", ToolLabelW, 16, 20, "ui.ed.tool_place", "ui.ed.tool_remove"),
        Label("Palette card section headers (352)", PaletteCardInnerW - 16f, 16, "ui.cat.*", "ui.pal.parts"),
        Lines("Palette card tiles (304×68, 16 px, three lines in a 72 px tile)", TileNameW, 68f, 18, 16, 16, "block.*.name", "ui.part.*")
            with { MaxLines = 3 },

        // The ship card.
        FitLine("ShipEditor card title (348)", 348f, 16, 16, "ui.ship.title"),
        Wrap("ShipEditor field labels (measured rows, 340)", ShipRowW, 3, 16, "ui.struct.name", "ui.ship.desc", "ui.struct.shape",
            "ui.struct.key", "ui.ship.blueprint", "ui.ship.dye_hex", "ui.ship.glow_hex"),
        Input("ShipEditor default name / description in their fields", ShipRowW, 18, "ui.ed.default.ship", "ui.ed.default.ship_desc"),
        FitLine("ShipEditor section headers (340)", ShipRowW, 16, 16, "ui.ship.stats", "ui.ship.interior", "ui.ship.selected_part",
            "ui.ship.cost"),
        Lines("ShipEditor stepper labels (176×44)", ShipStepperLabelW, 44f, 18, 16, 16, "ui.ship.hull", "ui.ship.shield", "ui.ship.speed",
            "ui.ship.handling", "ui.ship.cargo", "ui.ship.int_w", "ui.ship.int_l", "ui.ship.int_h", "ui.ship.turn_r"),
        Wrap("ShipEditor interior hint (measured row, 340)", ShipRowW, 8, 16, "ui.ship.interior_hint"),
        Lines("ShipEditor selected part (294×68)", ShipPartNameW, 68f, 18, 16, 16, "block.*.name", "ui.part.*") with { MaxLines = 3 },
        Lines("ShipEditor form value (164×48)", 164f, 48f, 18, 16, 16, FormKeys.Append("ui.ed.form_auto_short").ToArray()),
        Button("ShipEditor form pick (122×44)", 122f, 44f, "ui.ed.form_pick"),
        Wrap("ShipEditor placed (measured row, 340)", ShipRowW, 2, 16, "ui.ed.placed") with { Sample = "88888" },
        Button("ShipEditor advanced toggle (340×44)", ShipRowW, 44f, "ui.ship.advanced") with { Prefix = "▸ " },
        Button("ShipEditor cost add (160×44)", 160f, 44f, "ui.ship.cost_add"),
        Input("ShipEditor cost item (190)", 190f, 16, "ui.ship.cost_item"),
        // #3695: the card's button labels stay at 16 px or more (one line in the button's text box: its width − 18 − 10).
        FitLine("ShipEditor form pick label at 16 px (94)", 94f, 16, 22, "ui.ed.form_pick"),
        FitLine("ShipEditor cost add label at 16 px (132)", 132f, 16, 22, "ui.ship.cost_add"),
        FitLine("ShipEditor advanced label at 16 px (312)", 312f, 16, 22, "ui.ship.advanced") with { Prefix = "▸ " },

        // The shell: the Save subline, the status bar (its line breaks become spaces — a translation that still has the old
        // three-paragraph saved line keeps within two lines on screen), the key line.
        FitLine("ShipEditor Save subline (350)", SaveSublineW, 16, 16, "ui.editor.save_sub.ship"),
        Wrap("ShipEditor status (shell status bar, 1140)", StatusTextW, 3, 16, "ui.ed.saved_ship", "ui.ed.need_key",
            "ui.ed.export_failed_hint", "ui.ed.load_failed_hint", "ui.ed.loaded", "ui.ed.not_found", "ui.ed.skipped",
            "ui.ed.user_ship_renamed", "ui.submit.empty", "ui.submit.unchanged_ship") with { Sample = "my_ship" },
        Lines("ShipEditor key line (shell status bar, 680×44)", HintW, 44f, 16, 16, 16, "ui.ship.hint", "ui.ship.hint_remove",
            "ui.ship.hint_pad", "ui.ship.hint_pad_panel"),
    };
}
