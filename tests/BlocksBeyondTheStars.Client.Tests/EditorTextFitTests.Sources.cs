// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The catalogue (#3523): the structure editor's sources, generator entries, previews and the stamping panel
/// (<c>StructureEditor.Sources.cs</c>, <c>.Entry.cs</c>, <c>.Previews.cs</c>, <c>.Stamping.cs</c>) and the Unity-free texts they
/// draw (<c>StampStepsDescriber</c>, <c>StructureStampRules</c>).</summary>
public sealed partial class EditorTextFitTests
{
    /// <summary>The step texts of the stamping panel (measured paragraphs, 348 wide).</summary>
    private static readonly string[] StampTextKeys =
    {
        "ui.stamp.build.entry", "ui.stamp.seat.*", "ui.stamp.tokens.air", "ui.stamp.furnish.*", "ui.stamp.turn.*", "ui.stamp.ruin.*",
        "ui.stamp.chest.underground", "ui.stamp.chest.ignored", "ui.stamp.chest.summary", "ui.stamp.life.*", "ui.stamp.residents.*",
        "ui.stamp.keep_clear", "ui.stamp.protection.*",
    };

    private static IEnumerable<FitSite> StructureSourceSites() => new[]
    {
        // The generate rows and an entry's source rows (StructureEditor.Sources.cs).
        Lines("StructureEditor source / pool row labels (140×36)", 140f, 36f, 16, 13, 13, "ui.struct.source", "ui.struct.entry_pool"),
        Lines("StructureEditor source name / pool / use as values (150×36)", 150f, 36f, 16, 13, 13, "ui.source.*", "ui.struct.pool.*", "ui.use.entry"),
        FitLine("StructureEditor source parameter labels (348)", 348f, 13, 15, "ui.source.param.*"),
        Button("StructureEditor source switches (348×34)", 348f, 34f, "ui.source.param.*", "ui.struct.as_in_world") with { Prefix = "☐ " },
        Lines("StructureEditor source choice values (290×36)", 290f, 36f, 16, 13, 13, TierValueKeys.Concat(new[] { "ui.source.opt.*" }).ToArray()),
        FitLine("StructureEditor 'as in the world' note", 348f, 13, 14, "ui.struct.as_in_world_only"),
        FitLine("StructureEditor entry / preview section titles", 348f, 13, 16, "ui.struct.entry_title", "ui.struct.preview_title"),
        Button("StructureEditor entry preview", 348f, 34f, "ui.struct.entry_preview"),
        Wrap("StructureEditor entry hint (measured)", 348f, 8, 13, "ui.struct.entry_hint"),
        Button("StructureEditor quarter turn / materials", 168f, 34f, "ui.struct.turn", "ui.struct.materials"),
        Button("StructureEditor stamping toggle", 348f, 34f, "ui.struct.stamping") with { Prefix = "▸ " },

        // The source picker (EditorLoadPicker with a title, 640 wide).
        Label("Source picker title", 600f, 18, "ui.struct.source_title"),
        Label("Source picker sections", 560f, 13, "ui.struct.source_kind.*"),
        Label("Source picker names", 311f, 15, "ui.source.settlement", "ui.source.ornasa", "ui.source.mirun", "ui.source.hunter_camp",
            "ui.source.guardian_core", "ui.source.sealed_observatory", "ui.source.station_kit", "ui.source.rail_ruin") with { Prefix = "▸  " },
        Label("Source picker details", 252f, 13, "ui.struct.source_intact", "ui.struct.source_in_station", "ui.struct.source_in_town",
            "ui.struct.source_not_pool"),
        Label("Load picker entry details", 252f, 13, "ui.struct.pool.*") with { Prefix = "Ship wreck · " },

        // The stamping panel (StructureEditor.Stamping.cs over StampStepsDescriber).
        FitLine("Stamping step titles", 348f, 13, 14, "ui.stamp.title.*"),
        Wrap("Stamping step texts (measured)", 348f, 8, 13, StampTextKeys),
        Wrap("Stamping token step (block names)", 348f, 8, 13, "ui.stamp.tokens.resolved", "ui.stamp.tokens.fixed") with { Sample = "Steel Floor" },
        Wrap("Stamping chest lines (measured)", 336f, 4, 13, "ui.stamp.chest.items", "ui.stamp.chest.table", "ui.stamp.chest.more")
            with { Prefix = "• ", Sample = "iron_plate:2-4" },
        Wrap("Stamping ruin zone lines (measured, generation 39)", 336f, 4, 13, "ui.stamp.ruin.zone_line") with { Prefix = "• ", Sample = "255, 255, 255" },

        // The status line (348×84, five lines at 13 px).
        Wrap("StructureEditor status: sources, entries, previews", 348f, 5, 13, "ui.struct.source_needs_station", "ui.struct.source_needs_town",
            "ui.ed.entry.preview", "ui.ed.entry.saved", "ui.ed.entry.locked", "ui.ed.entry.no_submit", "ui.ed.entry.wrong_pool",
            "ui.ed.turn_shown", "ui.ed.materials_shown", "ui.ed.preview_locked", "ui.ed.refuse.tokens", "ui.ed.entry.key_taken_design",
            "ui.ed.entry.key_taken_entry", "ui.ed.entry.bad_source", "ui.ed.entry.pick_source", "ui.ed.entry.stale") with { Sample = "my_town" },
        Wrap("StructureEditor status: a save warning", 348f, 2, 13, "ui.ed.warn.keep", "ui.ed.warn.cavity", "ui.ed.warn.ports",
            "ui.ed.warn.life", "ui.ed.warn.settings") with { Prefix = "⚠ ", Suffix = " (+8 more)" },
        Label("StructureEditor status: more warnings", 120f, 13, "ui.ed.warn.more"),
    };
}
