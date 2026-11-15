// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3646: a CIVILIZATION template opened in the structure editor and saved again keeps everything — its civilization, pool and
/// prop pool, its sea-floor seat and protection word, its AIR POCKETS (never turned into a block or a plain cavity) and its prop
/// markers with their settings. The open → save path runs through the rules the Unity editor calls
/// (<see cref="CivilizationEditorRules"/>, <see cref="CivilizationFields"/>; StructureEditor.Landmark.cs, .GiantShip.cs), and a
/// source guard pins that the editor's files carry the fields and use those rules.
/// </summary>
public sealed class UserTemplateFilesCivilizationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>One look of a plain cell (a block, a cavity or an air pocket) at its place.</summary>
    private static List<string> PlainCells(StructureTemplate t)
    {
        var list = new List<string>();
        TemplateBody.ForEachPlain(t, p => list.Add($"{p.X},{p.Y},{p.Z} {(p.Air ? "air" : p.Cavity ? "cavity" : "block")} {(p.Cavity ? string.Empty : p.Id)} "
                                                 + $"{p.Tint} {p.Glow} {p.Shape} {p.Keep}"));
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>The cells with settings (markers, ports, devices) as the file lists them.</summary>
    private static List<string> SettingCells(StructureTemplate t)
    {
        var list = t.Cells.Where(c => !TemplateBody.IsPlain(c))
            .Select(c => $"{c.X},{c.Y},{c.Z} {c.Kind} {c.Id} {c.Config} {c.Port} {c.Mode} {c.Label} {c.Keep}").ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    /// <summary>
    /// The editor's open → save of a template: <c>TemplateDesignCells</c> (the compact body, then the listed cells, each cell's
    /// design kind and id, a marker in a cavity in its own place with the carve kept aside) and <c>LoadLandmark</c> (the fields
    /// and the seat kept aside) into the design of one cell per place — the first wins, like <c>ApplyCells</c> —, then
    /// <c>BuildLandmarkTemplate</c> (each cell's template id, the seat, the carves and the fields written back) and the compact
    /// body of <c>ExportLandmark</c>.
    /// </summary>
    private static StructureTemplate OpenAndSave(StructureTemplate source)
    {
        var carves = MarkerCarves.Under(source);
        var design = new Dictionary<(int, int, int), TemplateCell>();
        void Place(TemplateCell c) => design.TryAdd((c.X, c.Y, c.Z), c);
        TemplateBody.ForEachPlain(new StructureTemplate { Palette = source.Palette, Rows = source.Rows }, p =>
        {
            if (!carves.ContainsKey((p.X, p.Y, p.Z)))
            {
                Place(new TemplateCell
                {
                    X = p.X,
                    Y = p.Y,
                    Z = p.Z,
                    Kind = CivilizationEditorRules.DesignKind(p),
                    Id = CivilizationEditorRules.DesignId(p),
                    Tint = p.Tint,
                    Glow = p.Glow,
                    Shape = p.Shape,
                    Keep = p.Keep,
                });
            }
        });
        foreach (var c in source.Cells.Where(c => !(c.Kind is LandmarkRules.CavityKind or CivilizationRules.AirKind && carves.ContainsKey((c.X, c.Y, c.Z)))))
        {
            Place(new TemplateCell
            {
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                Kind = c.Kind,
                Id = CivilizationEditorRules.DesignId(c.Kind, c.Id),
                Tint = c.Tint,
                Glow = c.Glow,
                Shape = c.Shape,
                Port = c.Port,
                Mode = c.Mode,
                Config = c.Config,
                Label = c.Label,
                Keep = c.Keep,
            });
        }

        var fields = CivilizationFields.Of(source);
        string seat = CivilizationEditorRules.SeatOfLoaded(source);
        var saved = new StructureTemplate
        {
            Key = source.Key,
            Name = source.Name,
            Kind = "landmark",
            Landmark = true,
            Ruin = source.Ruin,
            RuinProfile = source.RuinProfile,
            RuinStrength = source.RuinStrength,
            GroundRow = source.GroundRow,
            NameKey = source.NameKey,
            Weight = source.Weight,
            MinTerrainGeneration = source.MinTerrainGeneration,
            RuinZones = source.RuinZones,
            Requires = new List<string>(source.Requires),
            Seat = CivilizationEditorRules.SeatToSave(seat, fields.IsSet),
            Width = source.Width,
            Height = source.Height,
            Length = source.Length,
        };
        foreach (var c in design.Values)
        {
            c.Id = CivilizationEditorRules.TemplateId(c.Kind, c.Id);
            saved.Cells.Add(c);
        }

        MarkerCarves.WriteBack(saved, carves);
        fields.ApplyTo(saved);
        TemplateBody.Compact(saved);
        return saved;
    }

    /// <summary>A small sunken civilization template: a block, a cavity, a two-cell air pocket, a prop and a chest marker.</summary>
    private static StructureTemplate Sunken()
    {
        var t = new StructureTemplate
        {
            Key = "gt_test_bell",
            Name = "Test bell",
            Kind = CivilizationRules.TemplateKind,
            Civilization = "grosstechniker",
            CivPool = CivilizationRules.PoolSmall,
            Seat = LandmarkRules.SeatSeabed,
            Protection = LandmarkRules.ProtectionOpen,
            Ruin = LandmarkRules.RuinAlways,
            Width = 4,
            Height = 3,
            Length = 1,
            GroundRow = 1,
            MinTerrainGeneration = 43,
        };
        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Kind = "block", Id = "dark_sandstone", Keep = true });
        t.Cells.Add(new TemplateCell { X = 1, Y = 0, Kind = LandmarkRules.CavityKind });
        t.Cells.Add(new TemplateCell { X = 2, Y = 1, Kind = CivilizationRules.AirKind });
        t.Cells.Add(new TemplateCell { X = 3, Y = 1, Kind = CivilizationRules.AirKind });
        t.Cells.Add(new TemplateCell { X = 2, Y = 2, Kind = "marker", Id = CivilizationRules.PropMarker, Config = "pool=remains" });
        t.Cells.Add(new TemplateCell { X = 3, Y = 2, Kind = "marker", Id = LandmarkRules.ChestMarker, Config = "table=civ_grosstechniker_common" });
        TemplateBody.Compact(t);
        return t;
    }

    [Fact]
    public void AnAirPocket_LoadsAsTheAirBrush_AndSavesAsAirAgain()
    {
        var t = Sunken();
        var air = new List<TemplateBody.Plain>();
        TemplateBody.ForEachPlain(t, p =>
        {
            if (p.Air)
            {
                air.Add(p);
            }
        });

        Assert.Equal(2, air.Count);
        Assert.All(air, p => Assert.Equal(CivilizationRules.AirKind, CivilizationEditorRules.DesignKind(p)));
        Assert.All(air, p => Assert.Equal(CivilizationEditorRules.AirPocketId, CivilizationEditorRules.DesignId(p)));
        Assert.Equal(string.Empty, CivilizationEditorRules.TemplateId(CivilizationRules.AirKind, CivilizationEditorRules.AirPocketId));
        Assert.Equal(string.Empty, CivilizationEditorRules.TemplateId(LandmarkRules.CavityKind, LandmarkRules.CavityKind));
        Assert.Equal("stone", CivilizationEditorRules.TemplateId("block", "stone"));
        Assert.NotEqual("air", CivilizationEditorRules.AirPocketId); // "air" is the air block's key — the editor would draw its tile
        Assert.Null(Content.GetBlock(CivilizationEditorRules.AirPocketId));
    }

    [Fact]
    public void ASunkenTemplate_OpenedAndSaved_KeepsItsFieldsAirPocketsAndProps()
    {
        var source = Sunken();
        var saved = OpenAndSave(source);

        Assert.Equal(("grosstechniker", CivilizationRules.PoolSmall, string.Empty), (saved.Civilization, saved.CivPool, saved.PropPool));
        Assert.Equal(LandmarkRules.SeatSeabed, saved.Seat);
        Assert.Equal(LandmarkRules.ProtectionOpen, saved.Protection);
        Assert.Equal(CivilizationRules.TemplateKind, saved.Kind);
        Assert.False(saved.Landmark);
        Assert.Contains(saved.Palette, p => p.Kind == CivilizationRules.AirKind && p.Id.Length == 0);
        Assert.Equal(PlainCells(source), PlainCells(saved));
        Assert.Equal(SettingCells(source), SettingCells(saved));
        Assert.Empty(Content.CivilizationTemplateProblems(saved));
    }

    [Fact]
    public void EveryShippedCivilizationTemplate_SurvivesOpenAndSave()
    {
        var authored = Content.CivilizationTemplates.Where(t => !t.IsSource).ToList();
        Assert.NotEmpty(authored);
        Assert.Contains(authored, t => t.Palette.Any(p => p.Kind == CivilizationRules.AirKind)); // the giant dwelling's diving bell
        Assert.Contains(authored, t => t.Cells.Any(c => c.Kind == "marker" && c.Id == CivilizationRules.PropMarker));
        foreach (var source in authored)
        {
            var saved = OpenAndSave(source);
            Assert.True((source.Civilization, source.CivPool, source.PropPool, source.Seat, source.Protection)
                        == (saved.Civilization, saved.CivPool, saved.PropPool, saved.Seat, saved.Protection), source.Key);
            Assert.True(PlainCells(source).SequenceEqual(PlainCells(saved)), $"{source.Key}: a plain cell changed");
            Assert.True(SettingCells(source).SequenceEqual(SettingCells(saved)), $"{source.Key}: a marker changed");
            Assert.Empty(Content.CivilizationTemplateProblems(saved));
        }
    }

    [Fact]
    public void AMarkerInACavity_KeepsItsPlace_AndTheCarveUnderIt()
    {
        // A chest in the giant dwelling's carved rooms, the old mine's tunnels, the carrier's hangar: the design used to give the
        // place to the cavity and skip the marker, so a save lost it.
        var templates = new[]
        {
            Content.CivilizationTemplateByKey("gt_giant_dwelling"),
            Content.Landmarks.FirstOrDefault(t => t.Key == "old_mine"),
            Content.GiantShips.FirstOrDefault(t => t.Key == "carrier"),
        };
        foreach (var source in templates)
        {
            Assert.NotNull(source);
            Assert.NotEmpty(MarkerCarves.Under(source!));
            var saved = OpenAndSave(source!);
            Assert.True(PlainCells(source!).SequenceEqual(PlainCells(saved)), $"{source!.Key}: a plain cell changed");
            Assert.True(SettingCells(source!).SequenceEqual(SettingCells(saved)), $"{source!.Key}: a marker changed");
        }

        Assert.Empty(MarkerCarves.Under(Sunken())); // the sample's markers stand in the open
        var bell = Sunken();
        bell.Cells.Add(new TemplateCell { X = 2, Y = 1, Kind = "marker", Id = LandmarkRules.ChestMarker });
        var carves = MarkerCarves.Under(bell);
        Assert.Equal(CivilizationRules.AirKind, CivilizationEditorRules.DesignKind(Assert.Single(carves).Value)); // a chest in the bell
        var rebuilt = new StructureTemplate();
        rebuilt.Cells.Add(new TemplateCell { X = 2, Y = 1, Kind = "marker", Id = LandmarkRules.ChestMarker });
        MarkerCarves.WriteBack(rebuilt, carves);
        Assert.Contains(rebuilt.Cells, c => c.Kind == CivilizationRules.AirKind && (c.X, c.Y, c.Z) == (2, 1, 0));
        var gone = new StructureTemplate();
        MarkerCarves.WriteBack(gone, carves); // the marker was removed: the place is what the design holds now
        Assert.Empty(gone.Cells);
    }

    [Fact]
    public void TheSeaFloorSeat_IsACivilizationTemplatesOnly()
    {
        var landmark = new StructureTemplate { Seat = LandmarkRules.SeatSeabed };
        Assert.Equal(string.Empty, CivilizationEditorRules.SeatOfLoaded(landmark)); // a landmark may not name it
        Assert.Equal(LandmarkRules.SeatSpan, CivilizationEditorRules.SeatOfLoaded(new StructureTemplate { Seat = "SPAN" }));
        Assert.Equal(LandmarkRules.SeatSeabed, CivilizationEditorRules.SeatToSave(LandmarkRules.SeatSeabed, civilization: true));
        Assert.Equal(string.Empty, CivilizationEditorRules.SeatToSave(LandmarkRules.SeatSeabed, civilization: false));
        Assert.Equal(LandmarkRules.SeatSpan, CivilizationEditorRules.SeatToSave(LandmarkRules.SeatSpan, civilization: false));
    }

    [Fact]
    public void ALandmark_IsLeftAlone_AndAPropKeepsItsPropPool()
    {
        var landmark = new StructureTemplate { Kind = "landmark", Landmark = true, Protection = "protected" };
        CivilizationFields.Of(landmark).ApplyTo(landmark);
        Assert.Equal(("landmark", true, string.Empty, "protected"), (landmark.Kind, landmark.Landmark, landmark.Civilization, landmark.Protection));

        var prop = Content.CivilizationTemplates.First(t => t.CivPool == CivilizationRules.PoolProp);
        var fields = CivilizationFields.Of(prop);
        Assert.True(fields.IsSet && fields.IsProp);
        Assert.Equal(prop.PropPool, OpenAndSave(prop).PropPool);
    }

    [Fact]
    public void ASaveIsChecked_AsTheContentLoaderChecksIt()
    {
        var bad = Sunken();
        bad.Cells.Add(new TemplateCell { X = 0, Y = 2, Kind = "marker", Id = "vendor" }); // a lost civilization has nobody home
        Assert.NotEmpty(CivilizationEditorRules.SaveProblems(bad, Content));
        Assert.Empty(CivilizationEditorRules.SaveProblems(Sunken(), Content));
        Assert.Contains(CivilizationEditorRules.SaveProblems(new StructureTemplate { Seat = LandmarkRules.SeatSeabed }, Content),
            p => p.Contains("seabed", StringComparison.Ordinal)); // no civilization: the landmark checks refuse the sea floor
    }

    // ---------------------------------------------------------------- the Unity editor's files (source guard)

    [Fact]
    public void TheEditorsFiles_CarryTheCivilizationFields_AndKeepAirPockets()
    {
        string editor = ClientTestPaths.ScriptPartsText("StructureEditor");
        // The landmark / civilization file the editor writes and reads (JsonUtility): the three fields of a civilization template.
        Assert.Contains("public string civilization = string.Empty, civPool = string.Empty, propPool = string.Empty;", editor);
        Assert.Contains("civilization = t.Civilization ?? string.Empty, civPool = t.CivPool ?? string.Empty, propPool = t.PropPool ?? string.Empty,", editor);
        Assert.Contains("Civilization = j.civilization ?? string.Empty, CivPool = j.civPool ?? string.Empty, PropPool = j.propPool ?? string.Empty,", editor);
        // Open: the fields, the seat and each cell's kind through the rules — the landmark AND the giant ship load share the cells.
        Assert.Contains("_civ = CivilizationFields.Of(source);", editor);
        Assert.Contains("_lmSeat = CivilizationEditorRules.SeatOfLoaded(source);", editor);
        Assert.Equal(1, CountOf(editor, "kind = CivilizationEditorRules.DesignKind(p), id = CivilizationEditorRules.DesignId(p),"));
        Assert.Equal(2, CountOf(editor, "var cells = TemplateDesignCells(source, out var carves);"));
        Assert.Equal(2, CountOf(editor, "_carves = carves;"));
        Assert.Contains("var under = MarkerCarves.Under(source);", editor);
        Assert.DoesNotContain("kind = p.Cavity ? LandmarkRules.CavityKind : \"block\"", editor); // the old load read an air pocket as a cavity
        // Save: the fields back, the seat, an empty id for a cavity and an air pocket; never into the landmark folder.
        Assert.Contains("_civ.ApplyTo(t);", editor);
        Assert.Equal(2, CountOf(editor, "MarkerCarves.WriteBack(t, _carves);")); // the landmark's and the giant ship's save
        Assert.Contains("Seat = CivilizationEditorRules.SeatToSave(_lmSeat, CivilizationMode),", editor);
        Assert.True(CountOf(editor, "CivilizationEditorRules.TemplateId(") >= 3, "landmark, giant ship and preview cells");
        Assert.Contains("WriteCivilizationBundle(key, json);", editor);
        // The palette: the air brush and the prop marker; the settings tool edits a prop's pool.
        Assert.Contains("Tool(CivilizationEditorRules.AirPocketId, StructureStampRules.KindAir, AirColor),", editor);
        Assert.Contains("M(CivilizationRules.PropMarker, PropColor),", editor);
        Assert.Contains("|| marker == CivilizationRules.PropMarker;", ClientTestPaths.ScriptPartsText("EditorMarkerPanel"));
    }

    private static int CountOf(string text, string what)
    {
        int n = 0;
        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
