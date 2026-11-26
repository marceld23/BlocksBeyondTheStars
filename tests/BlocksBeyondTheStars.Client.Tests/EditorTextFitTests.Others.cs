// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue: the "Submit to the developers" dialog and the new texts of the same release elsewhere (world list,
/// loading screen, chat window, settings) in their real boxes; the developer tools are in <c>EditorTextFitTests.DevTools.cs</c>.</summary>
public sealed partial class EditorTextFitTests
{
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
        // #3666: the settings rows' labels get 390 px (UiSettings.Rows.cs; a longer translation shrinks towards 14), and the
        // explanations moved out of the list into the description card (UiSettings.Info.cs: 388 px wide at 19).
        Label("Settings: dark visibility label (390 px at 20)", 390f, 20, "ui.settings.dark_visibility"),
        Wrap("Settings: description card body (388 wide at 19)", 388f, 12, 19, "ui.settings.dark_visibility_hint", "ui.settings.cosiness_hint",
            "ui.settings.festivals_hint", "ui.settings.fov_hint", "ui.settings.pad_triggers_hint", "ui.settings.pad_vibration_hint",
            "ui.settings.reduce_flashes_hint", "ui.settings.reduced_effects_hint", "ui.settings.story_text_wait_hint",
            "ui.settings.show_all_objectives_hint", "ui.settings.third_person_view_hint"),
        Label("Settings: soft distance label (390 px at 20, #3462)", 390f, 20, "ui.settings.distance_softness"),
        Label("Settings: third-person view label (390 px at 20, #4027)", 390f, 20, "ui.settings.third_person_view"),
        // #3615: the giant wrecks — the travel screen's paragraph (CraftingTechShipUI.Map.cs, 600 × 150 at 18: seven lines) and
        // the planet map's legend slot (WorldMap.cs, 140 px slot − 36 for the icon, at 15).
        // #3902: an escort cruiser's wreck shows its ship text in the same box (GiantWreckText.DerelictDetail).
        Wrap("Travel screen: giant wreck detail (600 × 150 at 18)", 600f, 7, 18, "ui.map.giantwreck_detail", "giantship.escort_cruiser.desc"),
        // #4039: the New-world form's mode box narrowed to the first three cards (MyWorldsLayout.ModeBoxW = 707, hints 150 high
        // at 15: eight lines; the Sandbox hint shares them with the flight line) and "World options…" under the fourth card.
        Wrap("New world: Hard Survival hint (707 × 150 at 15)", 707f, 8, 15, "ui.save.hard_hint"),
        Wrap("New world: Sandbox hint (707 at 15, six lines + the flight line)", 707f, 6, 15, "ui.save.sandbox_hint"),
        Wrap("New world: flight line under the Sandbox hint (707 at 15)", 707f, 2, 15, "ui.save.creative_flight") with { Sample = "Leertaste" },
        FitLine("New world: flight line in the Creative box (683 px, 14 → 13)", 683f, 13, 14, "ui.save.creative_flight") with { Sample = "Leertaste" },
        IconButton("New world: World options (229 × 46)", 229f, 46f, "ui.worldopt.open"),
        Label("Planet map legend: giant wreck (104 px at 15)", 104f, 15, "ui.map.kind_giant_wreck"),
        Label("Planet map legend: lost civilization (104 px at 15, #3642)", 104f, 15, "ui.map.kind_civilization"),
        Label("Planet map legend: guardian giant (104 px at 15, #3676)", 104f, 15, "ui.map.kind_guardian_giant"),
        Label("Planet map legend: guide tower (104 px at 15, #3893)", 104f, 15, "ui.map.kind_sps_tower"),
    };
}
