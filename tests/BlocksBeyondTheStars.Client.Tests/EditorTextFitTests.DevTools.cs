// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the developer tools on the editor shell (#3694) — the item &amp; recipe editor
/// (<c>ContentEditor*.cs</c>) and the material editor (<c>MaterialEditor*.cs</c>), both built from <c>DevEditorForm</c>.
/// The widths mirror the constants there.</summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>The keys of the check card and the status bar: the key / name checks and the warnings.</summary>
    private static readonly string[] DevCheckKeys =
    {
        "ui.ed.check.need_key", "ui.ed.check.key_space", "ui.ed.check.key_chars", "ui.ed.check.need_name", "ui.ed.check.all_set",
        "ui.content.check.key_taken", "ui.content.check.no_icon", "ui.content.check.no_inputs", "ui.content.unknown_keys",
        "ui.material.check.key_taken",
    };

    private static IEnumerable<FitSite> DeveloperToolSites() => new[]
    {
        // Card titles (UiKit.AddKicker, 16 px upper case): the narrowest card is the check card (516 − 2 × 24).
        Label("Developer editors card titles (narrowest card 468)", 468f, 16, "ui.content.item", "ui.content.recipe", "ui.content.check",
            "ui.content.tool_stats", "ui.content.effects", "ui.content.blueprint", "ui.material.texture", "ui.material.material",
            "ui.material.mechanics", "ui.material.look", "ui.material.world"),
        // DevEditorForm.LabelLeft: 18 px down to 14 on one line, then two lines at 13 in the 44 px row.
        Lines("ContentEditor item rows (ItemLabelW 210)", 210f, 44f, 18, 14, 13, "ui.content.key", "ui.content.name", "ui.content.desc",
            "ui.content.category", "ui.content.max_stack", "ui.content.places_block", "ui.content.tool_kind"),
        Lines("ContentEditor value cells (CellLabelW 170)", 170f, 44f, 18, 14, 13, "ui.content.tier", "ui.content.mining_power",
            "ui.content.mining_radius", "ui.content.damage", "ui.content.range", "ui.content.cooldown", "ui.content.energy",
            "ui.content.consume_health", "ui.content.consume_hunger", "ui.content.armor", "ui.content.oxygen", "ui.content.scan"),
        Lines("ContentEditor recipe rows (RecipeLabelW 200)", 200f, 44f, 18, 14, 13, "ui.content.station", "ui.content.output_count",
            "ui.content.market_theme", "ui.content.needs_blueprint", "ui.content.knowledge"),
        Lines("ContentEditor list titles (380 beside Add)", 380f, 44f, 18, 14, 13, "ui.content.inputs", "ui.content.unlock_cost"),
        IconButton("ContentEditor Add (200×44, btn_new)", 200f, 44f, "ui.content.add"),
        Input("ContentEditor ingredient key field (RowFieldW 300)", 300f, 18, "ui.content.item_key"),
        // The check card's lines wrap in 516 − 48 − 26 and grow with their text (measured).
        Wrap("ContentEditor check lines (442 wide, 18 px)", 442f, 4, 18, DevCheckKeys) with { Sample = "space_pickaxe_mk2" },
        // ‹ value › cycles (UiKit.AddCycle): the value gets the cycle's width less both arrows and 8 px — narrowest in the
        // material editor's 360 px columns.
        Lines("Developer editors cycle values (360 − 96)", 264f, 44f, 20, 14, 13, "ui.opt.category.*", "ui.opt.tool.*",
            "ui.opt.station.*", "ui.opt.blocktool.*", "ui.opt.worldtype.*", "ui.cat.ore", "ui.cat.terrain", "ui.cat.building",
            "ui.cat.machine", "ui.cat.light", "ui.cat.door", "ui.cat.flora", "ui.avatar.on", "ui.avatar.off"),
        Input("Developer editors default name / description (narrowest field 360)", 360f, 18, "ui.ed.default.material",
            "ui.ed.default.material_desc", "ui.ed.default.item", "ui.ed.default.item_desc"),
        // DevEditorForm.LabelAbove: 16 px down to 13 on one line over a 360 px column.
        FitLine("MaterialEditor labels above (ColW 360)", 360f, 13, 16, "ui.content.key", "ui.content.name", "ui.content.desc",
            "ui.material.category", "ui.material.tintable", "ui.material.hardness", "ui.material.tool", "ui.material.min_tier",
            "ui.material.gloss", "ui.material.metal", "ui.material.emission", "ui.material.base_color", "ui.material.world_type",
            "ui.material.frequency", "ui.material.min_depth", "ui.material.max_depth", "ui.material.source_png"),
        FitLine("MaterialEditor palette labels (base colour 220)", 220f, 13, 16, "ui.material.brush", "ui.material.base_color"),
        Lines("MaterialEditor spawn estimate (360×44)", 360f, 44f, 16, 14, 13, "ui.material.spawn_estimate") with { Sample = "0.5" },
        Button("MaterialEditor paint tools (segment 157×48)", 157f, 48f, "ui.tex.tool.brush", "ui.tex.tool.fill", "ui.tex.tool.pipette",
            "ui.tex.tool.eraser"),
        Button("MaterialEditor whole-tile fills (209×44)", 209f, 44f, "ui.material.fill", "ui.material.flat", "ui.material.clear"),
        FitLine("Developer editors open-folder link (360)", 360f, 13, 18, "ui.photos.open_folder"),
        // The shell around them: the status bar's message (1140 at 18 → 16, two lines), its key hint (680 at 16, two lines),
        // the confirmation's message (UiKit.Confirm: 688 wide, 20 px) and the header badge right of the title.
        Wrap("Developer editors status messages (1140, 16 px)", 1140f, 2, 16, DevCheckKeys) with { Sample = "space_pickaxe_mk2" },
        Wrap("Developer editors status results (1140, 16 px)", 1140f, 2, 16, "ui.content.exported", "ui.material.exported",
            "ui.ed.export_failed_hint", "ui.ed.open_folder_failed", "ui.ed.confirm_overwrite_export") with { Sample = "space_pickaxe_mk2" },
        Wrap("Developer editors key hints (680, 16 px)", 680f, 2, 16, "ui.content.keys_hint", "ui.material.keys_hint", "ui.material.hint_pad",
            "ui.material.hint_pad_panel", "ui.ed.hint_pad_form"),
        Wrap("Developer editors overwrite question (688, 20 px)", 688f, 4, 20, "ui.ed.confirm_overwrite_export") with { Sample = "space_pickaxe_mk2" },
        Label("Developer editors header badge (beside the title)", 300f, 16, "ui.editor.badge_dev"),
        // The shell's texts these editors choose: the Save subline (EditorShellLayout: SaveW 230 + SublineReach 120, 16 px) and
        // the overwrite question's confirm button (UiKit.Confirm: (760 − 72 − 24) / 2 less the icon inset, 22 → 16 px).
        FitLine("Developer editors Save subline (350)", 350f, 16, 16, "ui.editor.save_sub.content", "ui.editor.save_sub.material"),
        FitLine("Developer editors overwrite confirm button (266)", 266f, 16, 22, "ui.editor.confirm_replace_yes"),
    };
}
