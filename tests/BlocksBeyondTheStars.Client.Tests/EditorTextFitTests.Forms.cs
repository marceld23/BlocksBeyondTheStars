// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the layer-canvas editors on the editor shell (#3693) — the form editor, "My tools" and the
/// in-game shape designer (<c>LayerCanvasEditor*.cs</c>, <c>FormEditor*.cs</c>, <c>ToolLookEditor*.cs</c>,
/// <c>ShapeEditor*.cs</c>): the mock's three cards (left 360, centre 860, right 620 wide, 24 px inside) and the shell's
/// title, status bar and hint.</summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>A kicker is drawn in upper case: its words may use this share of the box in mixed case.</summary>
    private const float FormKickerShare = 0.8f;

    // Card insides: left 360 − 2 × 24, right 620 − 2 × 24, the centre's rows 860 − 2 × 24.
    private const float FormLeftW = 312f, FormRightW = 572f, FormCentreRowW = 812f;

    /// <summary>The shell's title room with the most header buttons any of the three shows (Undo / Redo and New).</summary>
    private static float FormTitleW => EditorShellLayout.Compute(EditorShellActions.Undo | EditorShellActions.New).Rect(EditorHeaderSlot.Title).W;

    private static IEnumerable<FitSite> FormSites() => FormShellSites().Concat(FormCardSites()).Concat(ToolLookSites()).Concat(ShapeDesignerSites());

    /// <summary>The shell's parts the three editors fill: the title, the status bar (1140 wide, two lines at 16 px) and the
    /// hint (680 wide, two lines at 16 px).</summary>
    private static IEnumerable<FitSite> FormShellSites() => new[]
    {
        FitLine("Form / tool-look / shape header title", FormTitleW, 20, 30, "ui.menu.form_editor", "ui.menu.tool_looks", "ui.shape.custom.title"),
        Wrap("Form / tool-look / shape status (shell status bar, 1140 wide, 2 lines)", 1140f, 2, 16, "ui.form.saved", "ui.form.need_name",
            "ui.form.deleted", "ui.form.load_failed", "ui.form.footprint_limit", "ui.form.problem_empty", "ui.form.problem_solid",
            "ui.form.problem_empty_cell", "ui.form.problem_detailed", "ui.shape.custom.exported", "ui.shape.custom.imported",
            "ui.shape.custom.import_failed", "ui.shape.custom.saved", "ui.shape.custom.multi_in_menu", "ui.toollook.saved", "ui.toollook.limit",
            "ui.toollook.standard_again", "ui.toollook.problem_empty", "ui.toollook.problem_parts", "ui.tex.code_copied", "ui.toollook.pasted",
            "ui.toollook.no_tools") with { Sample = "Mein Tisch" },
        Wrap("Form / tool-look / shape hint (shell status bar, 680 wide, 2 lines)", 680f, 2, 16, "ui.form.hint", "ui.form.hint_pad",
            "ui.form.hint_pad_panel", "ui.toollook.hint", "ui.toollook.hint_pad", "ui.shape.custom.hint"),
    }.Concat(WhenMerged(
        Wrap("Form / tool-look / shape first tip and paste (status bar)", 1140f, 2, 16, "ui.form.tip_remove", "ui.form.pasted"),
        Wrap("ToolLookEditor standard-look question (UiKit.Confirm message, 688 wide)", 688f, 5, 16, "ui.toollook.confirm_standard"),
        Button("Layer editors Place | Remove switch (the narrowest: 380 / 2 − 4)", 186f, 44f, "ui.ed.tool_place", "ui.ed.tool_remove")));

    /// <summary>The form editor's cards (<c>FormEditor.Ui.cs</c>) and the centre card's layer line all three share.</summary>
    private static IEnumerable<FitSite> FormCardSites() => new[]
    {
        FitLine("Layer editors left kickers (upper case)", FormLeftW * FormKickerShare, 13, 16, "ui.shape.custom.library", "ui.form.material",
            "ui.toollook.my_tools"),
        Input("FormEditor material search", FormLeftW, 16, "ui.tex.search"),
        Button("FormEditor library rows (246×44)", 246f, 44f, "ui.form.blocks") with { Prefix = "Mein Tisch  · ", Sample = "8" },
        Button("FormEditor library row of a nameless copy (246×44)", 246f, 44f, "ui.form.unnamed") with { Suffix = " 2" },
        FitLine("Layer editors layer line (680)", 680f, 13, 18, "ui.shape.custom.layer") with { Sample = "88" },
        Button("Layer editors helpers (the narrowest: 188×44)", 188f, 44f, "ui.shape.custom.copy_below", "ui.shape.custom.clear_layer",
            "ui.shape.custom.mirror_x", "ui.shape.custom.clear_all"),
        Button("FormEditor helpers (197×44)", 197f, 44f, "ui.form.fill_layer", "ui.shape.custom.mirror_z"),
        Button("FormEditor grid toggle (200×44)", 200f, 44f, "ui.shape.custom.grid_toggle"),
        Label("FormEditor budget (812)", FormCentreRowW, 16, "ui.form.budget_per_block", "ui.shape.custom.budget") with { Sample = "88" },
        Wrap("FormEditor problem (812, 2 lines)", FormCentreRowW, 2, 15, "ui.form.problem_empty", "ui.form.problem_solid",
            "ui.form.problem_empty_cell", "ui.form.problem_detailed"),
        FitLine("FormEditor preview kicker (upper case, 300)", 300f * FormKickerShare, 13, 16, "ui.shape.custom.preview"),
        FitLine("FormEditor material / dye kickers (upper case, 252)", 252f * FormKickerShare, 13, 16, "ui.form.material", "ui.form.dye"),
        FitLine("FormEditor footprint / name kickers (upper case)", FormRightW * FormKickerShare, 13, 16, "ui.form.footprint_title",
            "ui.shape.custom.name"),
        FitLine("FormEditor footprint", FormRightW, 13, 16, "ui.form.footprint") with { Sample = "8" },
        FitLine("FormEditor footprint rows (300)", 300f, 13, 16, "ui.form.width", "ui.form.height", "ui.form.length"),
        Wrap("FormEditor footprint note (572, 5 lines)", FormRightW, 5, 16, "ui.form.footprint_note"),
        Input("Layer editors name field", FormRightW, 18, "ui.shape.custom.name_hint"),
        Button("FormEditor more row (185×44)", 185f, 44f, "ui.form.duplicate", "ui.shape.custom.export", "ui.shape.custom.import"),
    };

    /// <summary>"My tools" (<c>ToolLookEditor.Ui.cs</c>): the right column of the centre card is 384 wide.</summary>
    private static IEnumerable<FitSite> ToolLookSites() => new[]
    {
        Wrap("ToolLookEditor note (measured, 312 wide)", FormLeftW, 8, 16, "ui.toollook.note"),
        FitLine("ToolLookEditor hand here (320)", 320f, 13, 16, "ui.toollook.hand_here"),
        FitLine("ToolLookEditor colours kicker (upper case, 384)", 384f * FormKickerShare, 13, 16, "ui.toollook.colours"),
        Button("ToolLookEditor glow switch (384×44)", 384f, 44f, "ui.toollook.glow_toggle"),
        FitLine("ToolLookEditor glow state (384)", 384f, 13, 16, "ui.toollook.glow_on", "ui.toollook.glow_off"),
        Label("ToolLookEditor budget (384)", 384f, 16, "ui.toollook.budget") with { Sample = "88" },
        FitLine("ToolLookEditor preview kicker (upper case, 572)", FormRightW * FormKickerShare, 13, 16, "ui.shape.custom.preview"),
        Button("ToolLookEditor back to the standard look (572×44)", FormRightW, 44f, "ui.toollook.use_standard"),
        Button("ToolLookEditor code buttons (282×44)", 282f, 44f, "ui.tex.copy_code", "ui.tex.paste_code"),
        Wrap("ToolLookEditor rules (measured, 572 wide, 8 lines)", FormRightW, 8, 16, "ui.toollook.rules"),
    };

    /// <summary>The in-game shape designer (<c>ShapeEditor.Ui.cs</c>): its helpers are a third of the centre row (265).</summary>
    private static IEnumerable<FitSite> ShapeDesignerSites() => new[]
    {
        Button("ShapeEditor library buttons (312×44)", FormLeftW, 44f, "ui.shape.custom.export", "ui.shape.custom.import"),
        Button("ShapeEditor helpers (265×44)", 265f, 44f, "ui.shape.custom.grid_toggle", "ui.shape.custom.mirror_z"),
        FitLine("ShapeEditor budget (265)", 265f, 13, 16, "ui.shape.custom.budget") with { Sample = "88" },
        Button("ShapeEditor craft (572×44)", FormRightW, 44f, "ui.shape.custom.craft"),
        // #3941 (ShapeEditor.Slots.cs): the colour-slot palette's kicker over the right card's width.
        FitLine("ShapeEditor slot palette kicker (upper case, 572)", FormRightW * FormKickerShare, 13, 16, "ui.shape.slot.title"),
    };
}
