// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the developer tools (material, item &amp; recipe), the "Submit to the developers" dialog, and the
/// new texts of the same release elsewhere (world list, loading screen, chat window, settings) in their real boxes.</summary>
public sealed partial class EditorTextFitTests
{
    private static IEnumerable<FitSite> DeveloperToolSites() => new[]
    {
        Label("Developer tools banner", 1400f, 15, "ui.editors.devbanner"),
        Label("MaterialEditor texture title", 500f, 18, "ui.material.texture"),
        Label("MaterialEditor palette labels", 210f, 15, "ui.material.brush", "ui.material.base_color"),
        Button("MaterialEditor fill / flat", 150f, 34f, "ui.material.fill", "ui.material.flat"),
        Button("MaterialEditor clear", 160f, 34f, "ui.material.clear"),
        Label("Material / content panel titles", 440f, 18, "ui.material.material", "ui.content.item", "ui.content.recipe"),
        Label("Material / content section headers", 440f, 16, "ui.material.mechanics", "ui.material.look", "ui.material.world",
            "ui.content.tool_stats", "ui.content.effects"),
        FitLine("Material / content field labels (180)", 180f, 13, 15, "ui.content.key", "ui.content.name", "ui.content.desc",
            "ui.content.places_block", "ui.content.market_theme", "ui.material.tool", "ui.material.category",
            "ui.material.world_type", "ui.material.tintable", "ui.content.category", "ui.content.tool_kind", "ui.content.station"),
        FitLine("Material / content stepper labels (248)", 248f, 13, 15, "ui.material.hardness", "ui.material.min_tier", "ui.material.gloss",
            "ui.material.metal", "ui.material.emission", "ui.material.frequency", "ui.material.min_depth", "ui.material.max_depth",
            "ui.content.max_stack", "ui.content.tier", "ui.content.mining_power", "ui.content.mining_radius", "ui.content.damage",
            "ui.content.range", "ui.content.cooldown", "ui.content.energy", "ui.content.consume_health", "ui.content.consume_hunger",
            "ui.content.armor", "ui.content.oxygen", "ui.content.scan", "ui.content.output_count", "ui.content.knowledge"),
        Lines("Material / content picked values (180×36)", 180f, 36f, 15, 13, 13, "ui.opt.*.*", "ui.cat.*"),
        Button("Material / content on/off", 154f, 30f, "ui.avatar.on", "ui.avatar.off"),
        Input("Material / content default name / description", 256f, 15, "ui.ed.default.material", "ui.ed.default.material_desc",
            "ui.ed.default.item", "ui.ed.default.item_desc"),
        FitLine("MaterialEditor source image label (stacked, 440)", 440f, 13, 15, "ui.material.source_png"),
        Label("MaterialEditor spawn estimate", 440f, 13, "ui.material.spawn_estimate") with { Sample = "0.5" },
        Wrap("MaterialEditor status (446×78)", 446f, 4, 13, "ui.material.exported", "ui.content.need_key", "ui.ed.export_failed_hint"),
        Button("Material / content save", 260f, 40f, "ui.material.save", "ui.content.save"),
        Button("Material / content back", 168f, 40f, "ui.menu.back"),
        Lines("Material / content hint", 1888f, 36f, 15, 13, 13, "ui.material.hint", "ui.content.hint"),
        Label("ContentEditor list titles", 314f, 16, "ui.content.inputs"),
        Label("ContentEditor unlock cost", 314f, 15, "ui.content.unlock_cost"),
        Label("ContentEditor blueprint title", 284f, 16, "ui.content.blueprint"),
        Button("ContentEditor add", 124f, 30f, "ui.content.add"),
        Input("ContentEditor item key", 280f, 15, "ui.content.item_key"),
        Wrap("ContentEditor status (446×70)", 446f, 4, 13, "ui.content.exported", "ui.content.need_key", "ui.content.unknown_keys",
            "ui.ed.export_failed_hint"),
    };

    /// <summary>Every kind's own wording of the Submit dialog — the texture keeps its approved <c>ui.tex.submit.*</c> keys.</summary>
    private static string[] SubmitKeys(string part) => new[]
    {
        "ui.tex.submit." + part, "ui.submit.ship." + part, "ui.submit.structure." + part, "ui.submit.avatar." + part, "ui.submit.hud." + part,
    };

    private static IEnumerable<FitSite> SubmitSites() => new[]
    {
        Label("Submit dialog title", 828f, 26, SubmitKeys("title")),
        Wrap("Submit dialog intro (828×44)", 828f, 2, 15, SubmitKeys("intro")),
        Label("Submit dialog name label", 828f, 15, SubmitKeys("name_label")),
        Label("Submit dialog nickname label", 828f, 15, "ui.tex.submit.nick_label"),
        Input("Submit dialog nickname field", 598f, 18, "ui.tex.submit.nick_placeholder"),
        Button("Submit dialog no name", 220f, 38f, "ui.tex.submit.no_name") with { Prefix = "☐  " },
        Wrap("Submit dialog note label (828×40)", 828f, 2, 15, "ui.tex.submit.note_label"),
        Wrap("Submit dialog consent 1 (804×40)", 804f, 2, 13, "ui.tex.submit.check_1", "ui.submit.ship.check_1", "ui.submit.structure.check_1",
            "ui.submit.avatar.check_1", "ui.submit.hud.check_1") with { Prefix = "☐  " },
        Wrap("Submit dialog consent 2 (804×62)", 804f, 3, 13, "ui.tex.submit.check_2", "ui.submit.ship.check_2", "ui.submit.structure.check_2",
            "ui.submit.avatar.check_2", "ui.submit.hud.check_2") with { Prefix = "☐  " },
        Wrap("Submit dialog consent 3 (804×40)", 804f, 2, 13, "ui.tex.submit.check_3") with { Prefix = "☐  " },
        Wrap("Submit dialog what is sent (828×84)", 828f, 4, 14, SubmitKeys("what_is_sent")),
        Wrap("Submit dialog status (828×40)", 828f, 2, 15, "ui.tex.submit.need_consent", "ui.tex.submit.sent", "ui.submit.ship.sent",
            "ui.submit.structure.sent", "ui.submit.avatar.sent", "ui.submit.hud.sent", "ui.tex.submit.queued", "ui.tex.submit.failed",
            "ui.tex.submit.not_configured", "ui.submit.not_configured", "ui.submit.empty", "ui.submit.too_many_cells", "ui.submit.too_large", "ui.feedback.sending")
            with { Sample = "88888" },
        IconButton("Submit dialog send / cancel", 400f, 52f, "ui.tex.submit.send", "ui.tex.submit.cancel"),
    };

    /// <summary>New texts of this release outside the editors, in the boxes their screens give them.</summary>
    private static IEnumerable<FitSite> OtherScreenSites() => new[]
    {
        FitLine("World list: loading (UiSaveSelect.WorldList.cs, 642 px, 17 → 13)", 642f, 13, 17, "ui.save.loading_worlds"),
        Label("Loading screen status row (UiLoading.cs, 1700 px at 17)", 1700f, 17, "ui.loading.stage.*"),
        Label("World-entry veil story line (WorldLoadingOverlay.cs footer, 1200 px at 22)", 1200f, 22, "ui.loading.lore.*"), // #3450/#3460
        Label("Loading screen data line (UiLoading.cs title, 760 px at 26)", 760f, 26, "ui.loading.data") with { Suffix = "  888/888" },
        FitLine("Chat window title (ChatUi.WindowView.cs, narrowest window)", 400f, 13, 20, "ui.chat.window_title"),
        FitLine("Chat window hint (480 px window − 2 × 14 padding)", 452f, 13, 14, "ui.chat.window_hint"),
        FitLine("Chat window new-messages pill (220 px)", 220f, 13, 15, "ui.chat.new_messages"),
        Label("Settings: dark visibility label (330 px at 20)", 330f, 20, "ui.settings.dark_visibility"),
        Wrap("Settings: dark visibility hint (495 wide at 14, measured height)", 495f, 4, 14, "ui.settings.dark_visibility_hint"),
        Label("Settings: soft distance label (330 px at 20, #3462)", 330f, 20, "ui.settings.distance_softness"),
    };
}
