// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3646: the fields of a CIVILIZATION template (docs/developer/CIVILIZATIONS.md) the structure editor's landmark form has no
    /// rows for — its civilization, its pool, a prop's prop pool and the file's protection word (a civilization site is never
    /// protected, so the form shows no picker). A civilization template opened in the editor carries them to the file it saves,
    /// unchanged.
    /// </summary>
    public sealed class CivilizationFields
    {
        public string Civilization { get; set; } = string.Empty;

        /// <summary>The civilization pool (<see cref="CivilizationRules.TemplatePools"/>: giant, city, small, prop).</summary>
        public string CivPool { get; set; } = string.Empty;

        /// <summary>A prop's own pool name (<c>statue</c>, <c>remains</c> …) that a <c>prop pool=…</c> marker asks for.</summary>
        public string PropPool { get; set; } = string.Empty;

        /// <summary>The loaded file's protection word, written back as it was (the server never protects a civilization site).</summary>
        public string Protection { get; set; } = string.Empty;

        /// <summary>Whether the design is a civilization template (it names a civilization).</summary>
        public bool IsSet => Civilization.Length > 0;

        /// <summary>Whether it is a prop (<see cref="CivilizationRules.PoolProp"/>): a small piece a prop marker places, never a
        /// site of its own.</summary>
        public bool IsProp => CivilizationEditorRules.IsPropPool(CivPool);

        /// <summary>The fields of a loaded template — all empty for a template that names no civilization.</summary>
        public static CivilizationFields Of(StructureTemplate? t)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Civilization))
            {
                return new CivilizationFields();
            }

            return new CivilizationFields
            {
                Civilization = t.Civilization.Trim(),
                CivPool = t.CivPool ?? string.Empty,
                PropPool = t.PropPool ?? string.Empty,
                Protection = t.Protection ?? string.Empty,
            };
        }

        /// <summary>Writes the fields onto a template the editor built from the design: the civilization, the pool, the prop pool,
        /// kind <see cref="CivilizationRules.TemplateKind"/> instead of a landmark (no landmark pool may offer it) and the protection
        /// word the loaded file had. A template of a design without a civilization is left alone.</summary>
        public void ApplyTo(StructureTemplate t)
        {
            if (t is null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            if (!IsSet)
            {
                return;
            }

            t.Civilization = Civilization;
            t.CivPool = CivPool;
            t.PropPool = PropPool;
            t.Kind = CivilizationRules.TemplateKind;
            t.Landmark = false;
            t.Protection = Protection;
        }
    }

    /// <summary>
    /// #3646: how the structure editor reads and writes a civilization template (Unity-free, so the headless suite pins it): its
    /// AIR POCKETS (<see cref="CivilizationRules.AirKind"/>) load as cells of the palette's air brush and save as air again —
    /// never as a block, never as a plain cavity —, its sea-floor seat stays, its ruin preview is the civilization's own build, and
    /// a save is checked as the content loader will check it. The file goes to the export bundle
    /// <c>civilization_exports/&lt;key&gt;/civilization.json</c> for <c>tools/merge_structure.py</c>, never to the landmark
    /// folder (the landmark pool would offer it as a landmark).
    /// </summary>
    public static class CivilizationEditorRules
    {
        /// <summary>The design id of an air-pocket cell (the palette's air brush, kind <see cref="CivilizationRules.AirKind"/>).
        /// Never a block key: <c>air</c> names the air block, whose tile the editor would draw.</summary>
        public const string AirPocketId = "air_pocket";

        /// <summary>The export folder (under the app's root) that holds one bundle per saved civilization template.</summary>
        public const string ExportsFolder = "civilization_exports";

        /// <summary>The file of a civilization template's export bundle.</summary>
        public const string BundleFile = "civilization.json";

        /// <summary>Whether <paramref name="pool"/> is the prop pool (any letter case).</summary>
        public static bool IsPropPool(string? pool) => string.Equals(pool, CivilizationRules.PoolProp, StringComparison.OrdinalIgnoreCase);

        /// <summary>The design kind of an empty template cell — an air pocket (always a cavity too) or a cavity — or null for a
        /// cell that holds a block.</summary>
        public static string? EmptyKind(bool cavity, bool air) => air ? CivilizationRules.AirKind : cavity ? LandmarkRules.CavityKind : null;

        /// <summary>The design kind of a plain template cell: an air pocket, a cavity or a block.</summary>
        public static string DesignKind(TemplateBody.Plain p) => EmptyKind(p.Cavity, p.Air) ?? "block";

        /// <summary>The design id of a template cell of <paramref name="kind"/>: the palette tool's id for an air pocket and a
        /// cavity, else the template's own id.</summary>
        public static string DesignId(string? kind, string? id) => kind switch
        {
            CivilizationRules.AirKind => AirPocketId,
            LandmarkRules.CavityKind => LandmarkRules.CavityKind,
            _ => id ?? string.Empty,
        };

        /// <summary>The design id of a plain template cell.</summary>
        public static string DesignId(TemplateBody.Plain p) => DesignId(DesignKind(p), p.Id);

        /// <summary>The template id of a design cell: empty for an air pocket and a cavity (they place no block), else its id.</summary>
        public static string TemplateId(string? kind, string? id)
            => kind is CivilizationRules.AirKind or LandmarkRules.CavityKind ? string.Empty : id ?? string.Empty;

        /// <summary>The seat a loaded template's form keeps: a span, the sea floor (only a civilization template may name it), else
        /// empty (the guaranteed search).</summary>
        public static string SeatOfLoaded(StructureTemplate t)
        {
            if (LandmarkRules.IsSpanSeat(t))
            {
                return LandmarkRules.SeatSpan;
            }

            return LandmarkRules.IsSeabedSeat(t) && !string.IsNullOrWhiteSpace(t.Civilization) ? LandmarkRules.SeatSeabed : string.Empty;
        }

        /// <summary>The seat a save writes: a span, the sea floor for a <paramref name="civilization"/> template (a landmark may not
        /// name it), else empty.</summary>
        public static string SeatToSave(string? seat, bool civilization)
        {
            if (string.Equals(seat, LandmarkRules.SeatSpan, StringComparison.OrdinalIgnoreCase))
            {
                return LandmarkRules.SeatSpan;
            }

            return civilization && string.Equals(seat, LandmarkRules.SeatSeabed, StringComparison.OrdinalIgnoreCase) ? LandmarkRules.SeatSeabed : string.Empty;
        }

        /// <summary>The ruin preview with <paramref name="seed"/>: the build the server runs — a civilization template with its
        /// civilization's materials, ruin look (rubble, overgrowth; under water as its civilization lives) and props
        /// (<see cref="CivilizationLayouts"/>), any other landmark the landmark's own; always run as a ruin.</summary>
        public static LandmarkLayout RuinPreview(StructureTemplate t, GameContent content, long seed, string surface)
        {
            if (t is null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (content.GetCivilization(t.Civilization) is not { } civ)
            {
                return LandmarkGenerator.Build(t, seed, 0, content, surface, ruin: true);
            }

            var materials = CivilizationLayouts.MaterialsOf(civ, surface, content);
            return LandmarkGenerator.Build(t, seed, 0, content, surface, ruin: true, LandmarkGenerator.RuinRevision, materials,
                CivilizationLayouts.RuinOptionsOf(civ, airless: false, civ.Scope.Underwater), new CivilizationPropDress(civ, seed, content, materials));
        }

        /// <summary>What refuses a save, one sentence each — empty when it may be saved: a civilization template is checked as the
        /// content loader checks it (<see cref="GameContent.CivilizationTemplateProblems"/>: the landmark checks, its civilization
        /// and pool, nobody home in a lost civilization, known blocks and loot tables), any other landmark by the landmark checks.</summary>
        public static List<string> SaveProblems(StructureTemplate t, GameContent? content)
        {
            if (t is null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            return content != null && !string.IsNullOrWhiteSpace(t.Civilization) ? content.CivilizationTemplateProblems(t) : LandmarkRules.Problems(t);
        }
    }
}
