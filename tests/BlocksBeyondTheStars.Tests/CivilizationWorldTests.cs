// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3642: a lost civilization's site in the living world, with test-made civilizations (<see cref="CivilizationTestWorlds"/>):
/// the map entry and the discovery once a player comes near (a relic's knowledge once, the Codex entry <c>civ:&lt;key&gt;</c>),
/// VEGA's lines — each once, each only where its trigger holds, one a pass, held while the hints are muted — the scan that reads
/// a site and the scan of a tagged block, the settlers' rumours (only off the home body, from terrain generation 43), and the
/// breath a diver takes in a sunken site's air pocket.
/// </summary>
public sealed class CivilizationWorldTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_civ_world_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        _repos.ForEach(r => r.Dispose());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private SvGameServer Start(GameContent content, string name, string planet, out RecordingTransport transport,
        int generation = WorldDescription.CivilizationsGeneration)
    {
        transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(CivilizationTestWorlds.Config(name, planet, generation), content, transport, repo);
        server.Start();
        return server;
    }

    private static PlayerSession Walker(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Stands the player <paramref name="gap"/> blocks west of the site's footprint, at its ground level.</summary>
    private static void StandBeside(SvGameServer server, PlayerSession p, SvGameServer.LandmarkInstance site, int gap)
        => p.State.Position = new Vector3f(
            WorldConstants.WrapX(site.Origin.X - gap, server.World.Circumference) + 0.5f, site.GroundY + 1, site.Origin.Z + (site.Structure.Length / 2) + 0.5f);

    /// <summary>One tick of a second; the VEGA lines it sent to <paramref name="p"/>.</summary>
    private static List<string> Tick(SvGameServer server, RecordingTransport transport, PlayerSession p)
    {
        int before = transport.Sent.Count;
        server.TickForTest(1.1);
        return transport.Sent.Skip(before).Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
            .Select(l => l.LineKey).Where(k => k.StartsWith("vega.civ.", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void ASite_GoesOnTheMap_TheCivilizationIsDiscoveredOnce_AndVegaSpeaksEachLineOnce_WhereItsTriggerHolds()
    {
        var content = CivilizationTestWorlds.Content();
        var server = Start(content, "civ_land", "meadowlands", out var transport);
        var site = Assert.Single(server.CivilizationSitesForTest);
        Assert.Equal(CivilizationTestWorlds.Land, site.Civilization);
        Assert.Null(site.WaterTop);
        var p = Walker(server, "Ada");
        string discovery = CivilizationRules.DiscoveryKey(CivilizationTestWorlds.Land);
        var lines = CivilizationTestWorlds.LandLines.Select(l => l.Key).ToArray();

        // The client's landmark list carries its silhouette as a civilization's.
        Assert.Contains(server.LandmarkListForTest(p).Landmarks, l => l.Kind == SvGameServer.CivilizationMapType && l.Name == "Labyrinth Ruin");

        StandBeside(server, p, site, 160); // far away: nothing
        Assert.Empty(Tick(server, transport, p));
        Assert.DoesNotContain(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.CivilizationMapType);
        Assert.DoesNotContain(discovery, p.State.Scanned);

        // In sight: on the map, the civilization discovered (a relic's knowledge, the Codex delta named after the people) and
        // VEGA's first line — only that one: nothing else holds out here.
        int knowledge = p.State.KnowledgePoints, before = transport.Sent.Count;
        StandBeside(server, p, site, 70);
        Assert.Equal(new[] { lines[0] }, Tick(server, transport, p));
        var entry = Assert.Single(server.PlanetPoisForTest("Ada"), poi => poi.Type == SvGameServer.CivilizationMapType);
        Assert.Equal(("Labyrinth Ruin", site.Rect.Cx), (entry.Name, (int)entry.X));
        Assert.Contains(discovery, p.State.Scanned);
        Assert.Equal(8, p.State.KnowledgePoints - knowledge);
        Assert.Equal("Labyrinth Builders", p.State.ScannedNames[discovery]);
        var delta = Assert.Single(transport.Sent.Skip(before).Select(s => s.Msg).OfType<DiscoveryLog>(), d => d.Entries.Contains(discovery));
        Assert.Equal("Labyrinth Builders", delta.Names.Single());
        Assert.Contains(SvGameServer.CivilizationVegaMilestone(CivilizationTestWorlds.Land, CivilizationTestWorlds.LandLines[0]), p.State.Milestones);
        Assert.Empty(Tick(server, transport, p)); // said once; nothing else holds here
        Assert.Equal(8, p.State.KnowledgePoints - knowledge); // discovered once

        // Inside the box (the middle of the hall: far from the iron, the glass and the statue): the inside line.
        var s = site.Structure;
        p.State.Position = CivilizationTestWorlds.At(server, site, s.Width / 2, 1, s.Length / 2);
        Assert.Equal(new[] { lines[1] }, Tick(server, transport, p));
        Assert.Empty(Tick(server, transport, p));

        // At the iron wall ("iron_*", a prefix), at the glass, at the statue's marker: each its own line, once.
        var iron = CivilizationTestWorlds.CellOf(content, site, "iron_wall");
        p.State.Position = CivilizationTestWorlds.At(server, site, iron.X, 1, iron.Z);
        Assert.Equal(new[] { lines[2] }, Tick(server, transport, p));
        Assert.Empty(Tick(server, transport, p));

        var glass = CivilizationTestWorlds.CellOf(content, site, "glass");
        p.State.Position = CivilizationTestWorlds.At(server, site, glass.X, 1, glass.Z);
        Assert.Equal(new[] { lines[3] }, Tick(server, transport, p));

        var statue = Assert.Single(s.Markers, m => m.Type == CivilizationRules.PropMarker).LocalPos;
        p.State.Position = CivilizationTestWorlds.At(server, site, statue.X + 3, 1, statue.Z);
        Assert.Equal(new[] { lines[4] }, Tick(server, transport, p));
        Assert.Empty(Tick(server, transport, p)); // no marker of the "statue_large" pool: its line never comes

        // Every line once — walking the whole round again says nothing.
        foreach (var at in new[] { new Vector3i(s.Width / 2, 1, s.Length / 2), iron, glass, statue })
        {
            p.State.Position = CivilizationTestWorlds.At(server, site, at.X, 1, at.Z);
            Assert.Empty(Tick(server, transport, p));
        }

        Assert.DoesNotContain(SvGameServer.CivilizationVegaMilestone(CivilizationTestWorlds.Land, CivilizationTestWorlds.LandLines[5]), p.State.Milestones);
        Assert.Equal(8, p.State.KnowledgePoints - knowledge);
    }

    [Fact]
    public void MutedHints_HoldTheLines_TheDiscoveryStillCounts_AndAboardNothingIsSaid()
    {
        var content = CivilizationTestWorlds.Content();
        var server = Start(content, "civ_mute", "meadowlands", out var transport);
        var site = Assert.Single(server.CivilizationSitesForTest);
        var bea = Walker(server, "Bea");
        bea.Vega.VegaHintsMuted = true;
        bea.State.Position = CivilizationTestWorlds.At(server, site, site.Structure.Width / 2, 1, site.Structure.Length / 2);
        Assert.Empty(Tick(server, transport, bea));
        Assert.DoesNotContain(bea.State.Milestones, m => m.StartsWith("vega:civ:", StringComparison.Ordinal));
        Assert.Contains(CivilizationRules.DiscoveryKey(CivilizationTestWorlds.Land), bea.State.Scanned); // a find, not a hint

        // Aboard (the ship parked here) VEGA keeps them too; on foot with the hints on they come, the first one first.
        bea.Vega.VegaHintsMuted = false;
        bea.State.AboardShip = true;
        Assert.Empty(Tick(server, transport, bea));
        bea.State.AboardShip = false;
        Assert.Equal(new[] { CivilizationTestWorlds.LandLines[0].Key }, Tick(server, transport, bea));
        Assert.Equal(new[] { CivilizationTestWorlds.LandLines[1].Key }, Tick(server, transport, bea));
    }

    [Fact]
    public void AScan_ReadsTheSite_AsTheCivilizationsDiscovery_AndATaggedBlockElsewhereDiscoversNothing()
    {
        var content = CivilizationTestWorlds.Content(tagGlass: true);
        var server = Start(content, "civ_scan", "meadowlands", out _);
        var site = Assert.Single(server.CivilizationSitesForTest);
        string discovery = CivilizationRules.DiscoveryKey(CivilizationTestWorlds.Land);

        // Any block of the site reads as the site: the people's subject, the site's name, a relic's knowledge once.
        var cid = Walker(server, "Cid");
        var floor = new Vector3i(site.Structure.Width / 2, 0, site.Structure.Length / 2);
        cid.State.Position = CivilizationTestWorlds.At(server, site, floor.X, 1, floor.Z);
        var cell = GiantWreckTestWorlds.At(server, site, floor);
        int knowledge = cid.State.KnowledgePoints;
        var first = server.ScanSubject("Cid", "block", "stone", cell: cell);
        Assert.Equal((SvGameServer.CivilizationMapType, "civ_" + CivilizationTestWorlds.Land, "Labyrinth Ruin", SvGameServer.CivilizationScanInfo),
            (first.Kind, first.SubjectKey, first.Subject, first.InfoKey));
        Assert.Equal("ui.scan.threat.inert", first.ThreatKey);
        Assert.True(first.FirstTime);
        Assert.Equal(8, cid.State.KnowledgePoints - knowledge);
        Assert.Contains(discovery, cid.State.Scanned);
        Assert.Equal("Labyrinth Builders", cid.State.ScannedNames[discovery]); // the ledger is named after the people
        Assert.False(server.ScanSubject("Cid", "block", "stone", cell: cell).FirstTime);
        server.TickForTest(1.1); // standing in the site: no second discovery
        Assert.Equal(8, cid.State.KnowledgePoints - knowledge);

        // A block next to the site is just a block.
        Assert.Equal("block", server.ScanSubject("Cid", "block", "stone", cell: new Vector3i(cell.X, cell.Y + 4, cell.Z)).Kind);

        // Far from every site, a block of the people (the glass is tagged here) is only a block: one a player crafted or carried off
        // discovers nothing (#3642 review) — not even where it really stands.
        var dan = Walker(server, "Dan");
        dan.State.Position = new Vector3f(WorldConstants.WrapX(site.Origin.X - 300, server.World.Circumference) + 0.5f, 200f, site.Origin.Z + 0.5f);
        var beside = new Vector3i((int)Math.Floor(dan.State.Position.X) + 1, 201, (int)Math.Floor(dan.State.Position.Z));
        server.World.SetBlock(beside, new BlockId(0));
        Assert.Equal("block", server.ScanSubject("Dan", "block", "glass", cell: beside).Kind);
        Assert.DoesNotContain(discovery, dan.State.Scanned); // the client named glass, the cell holds air: no discovery

        server.World.SetBlock(beside, content.GetBlock("glass")!.NumericId);
        knowledge = dan.State.KnowledgePoints;
        var tagged = server.ScanSubject("Dan", "block", "glass", cell: beside);
        Assert.Equal(("block", "glass"), (tagged.Kind, tagged.SubjectKey)); // the readout stays the block's (its yield, its ledger)
        Assert.DoesNotContain(discovery, dan.State.Scanned);
        Assert.Equal(tagged.KnowledgeGained, dan.State.KnowledgePoints - knowledge);
    }

    [Fact]
    public void Settlers_TellTheRumours_OfACivilizationAtHomeElsewhere_EachOnce_AndNeverAtItsHome()
    {
        var content = CivilizationTestWorlds.Content();
        var server = Start(content, "civ_rumour", "meadowlands", out _);
        string here = server.World.LocationId;
        var homes = server.Metadata.CivilizationHomes;
        Assert.Contains(here, homes[CivilizationTestWorlds.Land]);
        Assert.NotEmpty(homes[CivilizationTestWorlds.Far]);
        Assert.DoesNotContain(here, homes[CivilizationTestWorlds.Far]);

        var eve = Walker(server, "Eve");
        var far = new[]
        {
            "My grandmother told of a sea world with ruins on its floor. Dark stone, strange pictures — and a red light shining in the deep.",
            "Divers say there are machines with white eyes lying on the bottom of some sea out there. Nobody knows who built them.",
        };
        var told = new[] { server.CivilizationRumourForTest("Eve"), server.CivilizationRumourForTest("Eve") };
        Assert.Equal(far.OrderBy(t => t, StringComparer.Ordinal), told.OrderBy(t => t, StringComparer.Ordinal)); // each once, the land people's never
        Assert.Equal(string.Empty, server.CivilizationRumourForTest("Eve"));
        Assert.Contains(SvGameServer.CivilizationRumourPrefix + "npc.rumour.civ.grosstechniker.1", eve.State.Milestones);

        // A world of an older terrain generation has no homes, so no rumours.
        var older = Start(content, "civ_rumour_old", "meadowlands", out _, generation: WorldDescription.GiantWrecksGeneration);
        Assert.Empty(older.Metadata.CivilizationHomes);
        Walker(older, "Eve");
        Assert.Equal(string.Empty, older.CivilizationRumourForTest("Eve"));
    }

    [Fact]
    public void ADiver_BreathesInASunkenSitesAirPocket_AndVegaSaysSo()
    {
        var content = CivilizationTestWorlds.SeaContent("meadowlands");
        var server = Start(content, "civ_sea", "meadowlands", out var transport);
        Assert.True(server.AtmosphereBreathable);
        var site = Assert.Single(server.CivilizationSitesForTest);
        Assert.Equal(CivilizationTestWorlds.Sea, site.Civilization);
        Assert.NotNull(site.WaterTop);
        var p = Walker(server, "Finn");

        // Feet and head in the pocket (local rows 1 and 2 of the middle column): the world holds air there under the sea.
        var s = site.Structure;
        int mx = s.Width / 2, mz = s.Length / 2;
        Assert.True(site.Layout.IsAirPocket(mx, 2, mz));
        p.State.Position = CivilizationTestWorlds.At(server, site, mx, 1, mz);
        var head = WorldConstants.CanonicalBlock(site.Cell(new Vector3i(mx, 2, mz)), server.World.Circumference);
        Assert.True(server.World.GetBlock(head).IsAir);
        var overRoof = WorldConstants.CanonicalBlock(site.Cell(new Vector3i(mx, s.Height + 1, mz)), server.World.Circumference);
        Assert.Equal(content.GetBlock("water")!.NumericId, server.World.GetBlock(overRoof));

        p.State.Oxygen = 40f;
        Assert.Equal(new[] { "vega.civ.grosstechniker.air" }, Tick(server, transport, p));
        Assert.True(p.State.Oxygen > 40f, $"a head in the air pocket must breathe (oxygen {p.State.Oxygen})");
        Assert.Empty(Tick(server, transport, p)); // said once

        // Out in the sea over the roof the tank drains again.
        p.State.Position = new Vector3f(p.State.Position.X, site.Origin.Y + s.Height, p.State.Position.Z);
        float full = p.State.Oxygen;
        server.TickForTest(1.1);
        Assert.True(p.State.Oxygen < full, $"a head in the water must drain the tank (oxygen {p.State.Oxygen}, was {full})");
    }
}
