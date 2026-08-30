// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The structure editor can load a procedural station / settlement of any tier as a starting point (#1401):
/// every tier's output must fit the editor's 128³ build room, and every marker the generators emit must be one
/// the editor's marker palette offers — otherwise the Generate button would silently drop cells.
/// </summary>
public sealed class StructureGeneratorEditorRoomTests
{
    private const int Room = 128;

    // Mirrors StructureEditor.StationMarkers / SettlementMarkers (the marker ids, not the colours).
    private static readonly HashSet<string> StationPalette = new(StringComparer.Ordinal)
    {
        "hangar", "vendor", "mission_board", "heal_tank", "quarters", "console", "npc", "greenhouse", "spawn",
        "door_slide", "door_hinge", "door_energy",
    };

    private static readonly HashSet<string> SettlementPalette = new(StringComparer.Ordinal)
    {
        "vendor", "mission_board", "npc", "door_slide", "door_hinge", "door_energy", "loot", "greenhouse", "chest", "data_terminal",
    };

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("huge")]
    [InlineData("colossal")]
    public void ProceduralStation_FitsTheEditorRoom_AndUsesPaletteMarkers(string tier)
    {
        for (long seed = 1; seed <= 3; seed++)
        {
            var s = StationGenerator.Generate(tier, seed * 7919, Content);
            Assert.True(s.Width <= Room && s.Height <= Room && s.Length <= Room,
                $"{tier} seed {seed}: {s.Width}×{s.Height}×{s.Length} exceeds the {Room}³ editor room");
            foreach (var m in s.Markers)
            {
                Assert.True(StationPalette.Contains(m.Type), $"{tier}: station marker '{m.Type}' is not in the editor palette");
            }
        }
    }

    [Theory]
    [InlineData("hamlet")]
    [InlineData("village")]
    [InlineData("town")]
    [InlineData("city")]
    public void ProceduralSettlement_FitsTheEditorRoom_AndUsesPaletteMarkers(string tier)
    {
        foreach (var surface in new[] { "grass", "sand", "stone" })
        {
            for (long seed = 1; seed <= 3; seed++)
            {
                var s = SettlementGenerator.Generate(tier, ruined: false, seed * 104729, surface, Content);
                Assert.True(s.Width <= Room && s.Height <= Room && s.Length <= Room,
                    $"{tier}/{surface} seed {seed}: {s.Width}×{s.Height}×{s.Length} exceeds the {Room}³ editor room");
                foreach (var m in s.Markers)
                {
                    Assert.True(SettlementPalette.Contains(m.Type), $"{tier}: settlement marker '{m.Type}' is not in the editor palette");
                }
            }
        }
    }

    [Fact]
    public void SizeHintTables_CoverEveryEditorTier()
    {
        // The editor's tier steppers read these tables for the "procedural: …" hint (#1402); an unknown tier
        // silently falls back to the medium / village row, so pin the ones the steppers offer.
        Assert.Equal(24, StationGenerator.Layout("colossal").Modules);
        Assert.Equal(16, StationGenerator.Layout("huge").Modules);
        Assert.Equal((4, 4, 4), SettlementGenerator.Layout("city"));
        Assert.Equal((1, 2, 1), SettlementGenerator.Layout("hamlet"));
        Assert.Equal(8, SettlementGenerator.Plot);
    }
}
