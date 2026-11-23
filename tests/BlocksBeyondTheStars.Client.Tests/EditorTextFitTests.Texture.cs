// Blocks Beyond the Stars â Copyright (c) 2026 Justus DÃ¼tscher & Marcel DÃ¼tscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the editors hub (<c>UiEditors.cs</c>) and the texture editor (<c>TextureEditor.Ui.cs</c>,
/// <c>TextureEditor.Panels.cs</c>).</summary>
public sealed partial class EditorTextFitTests
{
    // The hub's card grid (#3667): UiEditors.CardW 420 â the name's x 104 â 18 px right; the description 420 â 2 Ã 22; the
    // developer panel's inside the template's 1740 â 2 Ã 24.
    private const float HubNameW = 298f, HubDescW = 376f, HubDevInnerW = 1740f - 48f;

    private static IEnumerable<FitSite> HubSites() => new[]
    {
        Label("UiEditors title (template logo)", 1400f, 54, "ui.editors.title"),
        FitLine("UiEditors subtitle (template)", 1500f, 14, 20, "ui.editors.subtitle"),
        IconButton("UiEditors back (template)", 210f, 52f, "ui.menu.back"),
        Label("UiEditors section labels", HubDevInnerW, 16, "ui.editors.group.creator", "ui.editors.group.dev"),
        Lines("UiEditors card names (298Ã64)", HubNameW, 64f, 24, 14, 13, "ui.menu.ship_editor", "ui.menu.avatar_editor",
            "ui.menu.station_editor", "ui.menu.settlement_editor", "ui.menu.texture_editor", "ui.menu.form_editor", "ui.menu.tool_looks",
            "ui.menu.content_editor", "ui.menu.material_editor"),
        Wrap("UiEditors card descriptions (376, 2 lines)", HubDescW, 2, 16, "ui.editors.desc.*"),
        Label("UiEditors submit badge", HubDescW - 28f, 14, "ui.editors.badge_submit"),
        FitLine("UiEditors help card title", HubDescW - 48f, 13, 18, "ui.editors.help.title"),
        FitLine("UiEditors help card lines", HubDescW, 13, 16, "ui.editors.help.look", "ui.editors.help.fly", "ui.editors.help.place",
            "ui.editors.help.remove"),
        Wrap("UiEditors developer note (2 lines)", HubDevInnerW, 2, 15, "ui.editors.dev.note"),
    };

    // The texture editor on the editor shell (#3693, mock "Textur"): three cards â the list (360), the canvas (860) and the
    // colour / previews / "More" card (620). Kickers are drawn in upper case (UiKit.AddKicker), which this headless measure
    // cannot do, so their room counts at 80 %. The status and hint lines are the shell's (1140 / 680 px, two lines at 16).
    // Keys the shell migration adds (artifacts/locale-fragments/paint.json) are checked once they are in en.json (WhenMerged);
    // the confirmations are UiKit.Confirm's (message 688 wide at 20 â 16 px, buttons (760 â 72 â 24) / 2 less the icon inset).
    private const float KickerShare = 0.8f;

    private static IEnumerable<FitSite> TextureSites() => new[]
    {
        FitLine("TextureEditor list kicker (upper case)", 320f * KickerShare, 13, 16, "ui.tex.browser"),
        Input("TextureEditor search", 320f, 16, "ui.tex.search"),
        FitLine("TextureEditor layer line (layer part)", 812f, 13, 16, "ui.tex.layer_*") with { Suffix = " (Name)  Â·  solid â no holes allowed" },
        FitLine("TextureEditor layer line (alpha part)", 812f, 13, 16, "ui.tex.alpha_opaque", "ui.tex.alpha_cutout")
            with { Prefix = "This world has its own texture here (Name)  Â·  " },
        Label("TextureEditor slot labels on the canvas", 320f, 13, "ui.tex.part.*", "ui.tex.side.*") with { Suffix = " Â· Oben" },
        FitLine("TextureEditor frame label (650)", 650f, 13, 16, "ui.tex.frames_animated", "ui.tex.frames_still") with { Sample = "8" },
        Button("TextureEditor frame column (152Ã44)", 152f, 44f, "ui.tex.frame_add", "ui.tex.frame_del", "ui.tex.fps", "ui.tex.play",
            "ui.tex.slots", "ui.tex.fill_all"),
        Button("TextureEditor tool bar (6 segments of 812, label inset 6)", (812f - (5f * 8f)) / 6f, 48f, "ui.tex.tool.*") with { TextX = 6f },
        FitLine("TextureEditor brush size (136)", 136f, 13, 16, "ui.tex.brush_size") with { Sample = "8" },
        Button("TextureEditor symmetry (136Ã44)", 136f, 44f, "ui.tex.mirror_x", "ui.tex.mirror_y"),
        Button("TextureEditor flips (138Ã44)", 138f, 44f, "ui.tex.flip_h", "ui.tex.flip_v"),
        FitLine("TextureEditor colour kicker (upper case)", 580f * KickerShare, 13, 16, "ui.tex.color"),
        FitLine("TextureEditor preview kicker (upper case)", 200f * KickerShare, 13, 16, "ui.tex.preview"),
        FitLine("TextureEditor tiled kicker (upper case)", 360f * KickerShare, 13, 16, "ui.tex.tiled"),
        Button("TextureEditor swap (124Ã44)", 124f, 44f, "ui.tex.swap"),
        FitLine("TextureEditor tile colours (384)", 384f, 13, 16, "ui.tex.tile_colors"),
        Wrap("TextureEditor alpha hint (measured, 580 wide, 3 lines)", 580f, 3, 16, "ui.tex.alpha_bar"),
        Button("TextureEditor More (3 per row, 188Ã44)", 188f, 44f, "ui.tex.before", "ui.tex.reset", "ui.tex.remove_local", "ui.tex.export",
            "ui.tex.copy_code", "ui.tex.paste_code", "ui.tex.open_folder", "ui.tex.reload_folder", "ui.tex.publish", "ui.tex.unpublish"),
        Wrap("TextureEditor status (shell status bar, 1140 wide, 2 lines)", 1140f, 2, 16, "ui.tex.saved_local", "ui.tex.saved_local_world_wins",
            "ui.tex.save_failed", "ui.tex.removed_local", "ui.tex.none_local", "ui.tex.reset_done", "ui.tex.no_official", "ui.tex.reloaded",
            "ui.tex.publish_sent", "ui.tex.code_pasted", "ui.tex.code_copied", "ui.tex.code_bad", "ui.tex.icon_one_frame", "ui.tex.frame_limit",
            "ui.tex.exported", "ui.ed.export_failed_hint", "ui.ed.open_folder_failed") with { Sample = "stone_bricks" },
        Wrap("TextureEditor hint (shell status bar, 680 wide, 2 lines)", 680f, 2, 16, "ui.tex.hint", "ui.tex.hint_pad", "ui.tex.hint_pad_panel"),
    }.Concat(WhenMerged(
        FitLine("TextureEditor more kicker (upper case)", 580f * KickerShare, 13, 16, "ui.tex.more"),
        FitLine("TextureEditor steppers (96)", 96f, 13, 16, "ui.tex.light", "ui.tex.contrast", "ui.tex.colourfulness"),
        TextureConfirmations(),
        FitLine("TextureEditor reset confirm button (266)", 266f, 16, 22, "ui.tex.reset_yes"),
        // #3939: the soft tools' panel at the left card's foot (TextureEditor.Soft.cs) — a kicker, six buttons
        // two to a row (156×44), the "calm when saving" label (320) over a ‹ value › cycle (320 − 2 × 44 − 8), the tooltips
        // (UiTooltip: 380 less 2 × 12 padding, body at 17 px) and three status lines.
        FitLine("TextureEditor soft tools kicker (upper case)", 320f * KickerShare, 13, 16, "ui.tex.soft.title"),
        Button("TextureEditor soft tools (156×44)", 156f, 44f, "ui.tex.soft.brush", "ui.tex.soft.smooth", "ui.tex.soft.smooth_all",
            "ui.tex.soft.calm_all", "ui.tex.soft.seamless", "ui.tex.soft.luma"),
        FitLine("TextureEditor calm-when-saving label (320)", 320f, 13, 16, "ui.tex.soft.save_calm"),
        Label("TextureEditor calm-when-saving value (224)", 224f, 16, "ui.tex.soft.percent") with { Sample = "100" },
        Wrap("TextureEditor soft tools tooltips (356, 17 px)", 356f, 5, 17, "ui.tex.soft.*_tip"),
        Wrap("TextureEditor soft tools status (shell status bar, 1140 wide, 2 lines)", 1140f, 2, 16, "ui.tex.soft.seamless_done",
            "ui.tex.soft.luma_on", "ui.tex.soft.luma_not_tinted") with
        { Sample = "88.8" },
        // The shell's Save subline (EditorShellLayout: SaveW 230 + SublineReach 120, 16 px) of every paint and shape editor.
        FitLine("Paint / shape editors Save subline (350)", 350f, 16, 16, "ui.editor.save_sub.texture", "ui.editor.save_sub.avatar",
            "ui.editor.save_sub.appearance", "ui.editor.save_sub.paint", "ui.editor.save_sub.form", "ui.editor.save_sub.tool_looks",
            "ui.editor.save_sub.shape")));

    /// <summary>The texture editor's questions (UiKit.Confirm's message, 688 wide at 20 → 16 px).</summary>
    private static FitSite TextureConfirmations()
    {
        var site = Wrap("TextureEditor confirmations (UiKit.Confirm, 688)", 688f, 5, 16, "ui.tex.confirm_reset", "ui.tex.confirm_remove_local",
            "ui.tex.confirm_unpublish");
        return site with { Sample = "stone_bricks" };
    }
}
