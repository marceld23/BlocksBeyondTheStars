// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2792: a server test uses the shared <see cref="RecordingTransport"/> instead of a private copy of it. 79 test files
/// carried one (46 of the 203 added in the 30 days before this guard copied it again); the identical ones are gone.
/// The files below still hold a copy that grew extras (text helpers, a disconnect list, a send hook …) — a ratchet: the
/// list may only shrink, and a file that drops its copy must leave the list too.
/// </summary>
public sealed class RecordingTransportGuardTests
{
    private static readonly string[] DriftedCopies =
    {
        "BaseAirBulkheadTests.cs", "BaseAirLeakTests.cs", "BaseAirReachTests.cs", "BioLabFixTests.cs",
        "BioTankFixTests.cs", "CargoTransferTests.cs", "CrystalNetTests.cs", "CutsceneLedgerTests.cs",
        "DerelictStationTests.cs", "DropLootTests.cs", "FarTerrainTileTests.cs", "PadSpawnTests.cs",
        "PerWorldSpeciesTests.cs", "PlayerStationReportsTests.cs", "ReconnectAndGhostTests.cs", "ShipHatchCellsTests.cs",
        "ShipInteriorReturnTests.cs", "ShipLossRecoveryTests.cs", "SideStoryPilotTests.cs", "StationBodyIdentityTests.cs",
        "StationDoorTests.cs", "StationGravityTests.cs", "StoryGapClosureTests.cs", "StoryJournalTests.cs",
        "StoryPackRuntimeTests.cs", "StorySwitchTests.cs", "StoryUiFixesTests.cs", "TutorialBuildingCoursesTests.cs",
        "TutorialCardsTests.cs", "TutorialCoursesOnlyTests.cs", "TutorialCrystalCoursesTests.cs", "TutorialDeepDivesTests.cs",
        "TutorialFirstStartQuestionTests.cs", "TutorialMoreCoursesTests.cs", "TutorialPackTests.cs", "VegaChapterLeadsTests.cs",
        "VegaChapterScenesTests.cs", "VegaFinaleSetPiecesTests.cs", "VegaStoryMissionsTests.cs", "VegaStoryPackTests.cs",
        "WorldPauseTests.cs", "WorldStoriesRulesTests.cs",
    };

    private static readonly Regex NestedCopy = new(@"^\s*(?:public |private |internal )?sealed class RecordingTransport\b",
        RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    [Fact]
    public void ServerTests_UseTheSharedRecordingTransport_NotAPrivateCopy()
    {
        string dir = Path.Combine(TestPaths.RepoRoot(), "tests", "BlocksBeyondTheStars.Tests");
        var withCopy = Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetFileName(f) != "RecordingTransport.cs" && NestedCopy.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetFileName(f))
            .ToHashSet(StringComparer.Ordinal);

        var newCopies = withCopy.Where(f => !DriftedCopies.Contains(f, StringComparer.Ordinal)).OrderBy(f => f).ToList();
        Assert.True(newCopies.Count == 0,
            "Use the shared RecordingTransport (tests/BlocksBeyondTheStars.Tests/RecordingTransport.cs) instead of a private " +
            "copy; if a test needs more, extend the shared one:\n" + string.Join('\n', newCopies));

        var cleared = DriftedCopies.Where(f => !withCopy.Contains(f)).OrderBy(f => f).ToList();
        Assert.True(cleared.Count == 0,
            "These files no longer carry a private RecordingTransport — take them off DriftedCopies:\n" +
            string.Join('\n', cleared));
    }
}
