// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Sol's Mars (#4018): nothing anybody built stands there — its structure whitelist keeps only the net fragments, on every
/// save.
/// </summary>
public sealed class MarsTests : IDisposable
{
    private const string MarsType = "sol_mars";
    private const string MarsBody = "sol-mars";
    private const int MarsCircumference = 8512;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_mars_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly ITestOutputHelper _output;

    public MarsTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private static PlanetType Mars => Content.GetPlanet(MarsType)!;

    [Fact]
    public void Mars_KeepsOnlyTheNetFragments_OfEveryStampedKind_AndNoRuinProps()
    {
        var mars = Mars;
        Assert.True(mars.RestrictStructures);
        Assert.Equal(new[] { "net_fragments" }, mars.AllowedStructures);

        // Every stamped kind but the net fragments is off on Mars: no G.D.S. ruin, rune monument, fallen town, rail ruin, camp,
        // factory, wreck, giant wreck, vault, landmark, guide tower or fallen giant (the tablets and the Sounder sites follow
        // their own planet type and stand nowhere else).
        var rules = Content.StructureTypes;
        var allowed = StructureTypeRules.Kinds.Where(k => StructureTypeRules.AllowedOn(rules.Get(k), mars, restricted: true)).ToList();
        Assert.Equal(new[] { StructureTypeRules.NetFragment }, allowed);

        // The chunk generation's rune shrines need "monuments", its lone rune stones "rune_stones" — neither is listed.
        Assert.DoesNotContain("monuments", mars.AllowedStructures);
        Assert.DoesNotContain(StructureTypeRules.RuneStonesWhitelist, mars.AllowedStructures);
    }

    /// <summary>A world's first landing on Mars (Sol revealed on its first boot) stamps none of the rolled kinds but the net
    /// fragments.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Mars_FirstVisit_StampsNothingButTheNetFragments()
    {
        var config = new ServerConfig
        {
            WorldName = "mars",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true },
        };
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "mars"));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, new RecordingTransport(), repo);
        server.Start();
        var admin = server.AddLocalPlayer("Ada");
        admin.State.Role = PlayerRole.WorldAdmin;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" });

        var clock = Stopwatch.StartNew();
        Assert.True(server.VisitBodyForTest(MarsBody));
        _output.WriteLine($"first boot of {MarsBody} took {clock.ElapsedMilliseconds} ms");
        Assert.Equal(MarsCircumference, server.World.Circumference);

        var kinds = server.PlacementRecordsForTest.Where(r => r.Placed && r.LocationId == MarsBody).Select(r => r.Kind).Distinct().ToList();
        _output.WriteLine("placed kinds on Mars: " + string.Join(", ", kinds));
        _output.WriteLine("stamp report: " + string.Join(", ", server.StampReportForTest.Select(r => $"{r.Kind} {r.Placed}/{r.Requested}")));
        Assert.All(kinds, k => Assert.Equal(StructureTypeRules.NetFragment, k));
        Assert.Empty(server.LandmarksForTest);
        server.Stop();
    }
}
