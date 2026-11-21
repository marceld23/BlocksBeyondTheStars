// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3611/#3615: the client's side of the giant ship wrecks, Unity-free — the words the chart, the travel screen, the scanner
/// card and the Codex share (<see cref="GiantWreckText"/>), the ranges and the approach measured from the hull's box
/// (<see cref="HullApproach"/>), the editor's word for a ship's way in, and the cost of a derelict's broken-look extras on a
/// shell 256 blocks long (<see cref="DerelictDecorPlan"/>: planned once per design, never per frame).
/// </summary>
public sealed class GiantWreckClientTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private readonly ITestOutputHelper _output;

    public GiantWreckClientTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AGiantWreck_IsADerelict_WithItsOwnKindWordAndDetail()
    {
        const string giant = GiantShipRules.IdPrefix + "sys0-0";
        Assert.True(GiantWreckText.IsGiantWreck(giant));
        Assert.True(SpaceTargeting.IsDerelictId(giant)); // every derelict rule holds for it
        Assert.True(SpaceTargeting.IsGeneratedDerelictId(giant)); // …the broken look included
        Assert.Equal("ui.map.kind_giant_wreck", GiantWreckText.DerelictKind(giant));
        Assert.Equal("ui.map.giantwreck_detail", GiantWreckText.DerelictDetail(giant));

        Assert.False(GiantWreckText.IsGiantWreck("derelict:sys0-0"));
        Assert.Equal("ui.map.kind_derelict", GiantWreckText.DerelictKind("derelict:sys0-0"));
        Assert.Equal("ui.map.derelict_detail", GiantWreckText.DerelictDetail("derelict:sys0-0"));
        Assert.False(GiantWreckText.IsGiantWreck(null));

        // A station contact is a navigation point, scannable and friendly — a giant wreck is one (kind SpaceStation).
        Assert.True(SpaceTargeting.IsNavigationKind(SpaceTargeting.SpaceStation));
        Assert.True(SpaceTargeting.IsScannableKind(SpaceTargeting.SpaceStation));
        Assert.True(SpaceTargeting.IsSystemWide(SpaceTargeting.SpaceStation));
    }

    [Fact]
    public void AnEscortCruisersWreck_IsADerelict_ButNeverCalledADerelictStation()
    {
        string wreck = CruiserWreckRules.IdOf("sys3", 1);
        Assert.True(GiantWreckText.IsCruiserWreck(wreck));
        Assert.True(GiantWreckText.IsShipWreck(wreck));
        Assert.False(GiantWreckText.IsGiantWreck(wreck));
        Assert.True(SpaceTargeting.IsDerelictId(wreck)); // every derelict rule holds for it (the lock calls it "Wreck")
        Assert.True(SpaceTargeting.IsDerelictTarget(SpaceTargeting.SpaceStation, wreck));

        // The kind word beside its name is the plain "Wreck"; the travel screen tells what the ship is.
        Assert.Equal("ui.map.kind_wreck", GiantWreckText.DerelictKind(wreck));
        Assert.Equal("giantship.escort_cruiser.desc", GiantWreckText.DerelictDetail(wreck));

        // The flight chart: its name alone, in this client's language — the others keep "name · kind".
        static string Text(string key) => "<" + key + ">";
        Assert.Equal("<station.cruiser_wreck.name>", GiantWreckText.ChartLabel(wreck, "Wreck of an escort cruiser", Text));
        Assert.Equal("Old Halden · <ui.map.kind_derelict>", GiantWreckText.ChartLabel("derelict:sys0-0", "Old Halden", Text));
        Assert.Equal("Wanderer Seven · <ui.map.kind_giant_wreck>", GiantWreckText.ChartLabel(GiantShipRules.IdPrefix + "sys0-0", "Wanderer Seven", Text));
        Assert.True(GiantWreckText.IsShipWreck(GiantShipRules.IdPrefix + "sys0-0"));
        Assert.False(GiantWreckText.IsShipWreck("derelict:sys0-0"));

        // The words exist in both mandatory languages, and none of them says "station".
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            foreach (string key in new[] { CruiserWreckRules.NameKey, GiantWreckText.DerelictKind(wreck), GiantWreckText.DerelictDetail(wreck) })
            {
                Assert.True(table.TryGetValue(key, out string? text) && text.Length > 0, $"{code}: {key} is missing");
                Assert.DoesNotContain("station", text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // The Unity side asks these helpers (PR CI never compiles it).
        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        Assert.Contains("GiantWreckText.ChartLabel(e.Id, e.Name, L)", File.ReadAllText(Path.Combine(scripts, "SpaceMap.cs")));
        Assert.Contains("GiantWreckText.IsShipWreck(body.Id) ? GiantWreckDetailH : 110f", File.ReadAllText(Path.Combine(scripts, "CraftingTechShipUI.Map.cs")));
    }

    [Fact]
    public void TheScannerCard_KnowsBothReadouts_AndReadsTheShipClassFromTheTraits()
    {
        Assert.True(GiantWreckText.IsSpaceReadout("station", "giant_wreck"));
        Assert.False(GiantWreckText.IsSpaceReadout("station", "station"));
        Assert.Equal(GiantShipRules.MapType, GiantWreckText.PlanetReadoutKind);
        Assert.Equal("ui.scan.subject.giant_wreck", GiantWreckText.SpaceSubjectKey);

        string[] traits = { "ui.scan.trait.derelict_breached", "giantship.battleship.name", "ui.scan.trait.giant_size_colossal" };
        Assert.Equal("battleship", GiantWreckText.ShipKeyFromTraits(traits));
        Assert.Null(GiantWreckText.ShipKeyFromTraits(new[] { "ui.scan.trait.derelict_breached", "giantship..name" }));
        Assert.Null(GiantWreckText.ShipKeyFromTraits(null));
    }

    [Fact]
    public void TheCodex_ListsBothLedgers_UnderOneGroup_NamedTheWayEachWasFound()
    {
        var texts = new Dictionary<string, string>
        {
            ["giantship.battleship.name"] = "Battleship",
            ["giantship.battleship.desc"] = "The old escort ship.",
            ["giantship.liner.name"] = "Cruise Liner",
            ["giantship.liner.desc"] = "A cruise liner.",
            ["ui.scan.giant_wreck_space"] = "The wreck of a giant spaceship.",
        };
        string? Text(string key) => texts.TryGetValue(key, out var t) ? t : null;

        const string planet = "giantwreck:sys2-p1:battleship";
        const string space = "space:giantwreck:derelict:giant-sys2-0";
        Assert.Equal(GiantWreckText.CodexGroup, GiantWreckText.CodexKind(planet));
        Assert.Equal(GiantWreckText.CodexGroup, GiantWreckText.CodexKind(space));
        Assert.Null(GiantWreckText.CodexKind("space:derelict"));
        Assert.Null(GiantWreckText.CodexKind("landmark:sys2-p1:eifelnyde"));
        Assert.Equal("battleship", GiantWreckText.PlanetShipKey(planet));
        Assert.Null(GiantWreckText.PlanetShipKey(space));

        // On a planet: the ship class in this client's language (not the name recorded in another one) and its Codex text.
        Assert.Equal(("Battleship", "The old escort ship."), GiantWreckText.CodexLine(planet, "Schlachtschiff", Text));

        // In space: the coined name; the class and its text once this session scanned it, else the readout's sentence.
        Assert.Equal(("Wanderer Seven", "The wreck of a giant spaceship."), GiantWreckText.CodexLine(space, "Wanderer Seven", Text));
        var classes = new Dictionary<string, string> { ["Wanderer Seven"] = "liner" };
        Assert.Equal(("Wanderer Seven", "Cruise Liner — A cruise liner."), GiantWreckText.CodexLine(space, "Wanderer Seven", Text, classes));

        // A ship this client does not know keeps the recorded name; an entry without a name shows its id.
        Assert.Equal(("Sky Whale", string.Empty), GiantWreckText.CodexLine("giantwreck:p:whale", "Sky Whale", Text));
        Assert.Equal("derelict:giant-sys2-0", GiantWreckText.CodexLine(space, string.Empty, Text).Name);

        // The server records the class in the key (after '#'), so a later session names it without having scanned it.
        string withClass = space + "#liner";
        Assert.Equal(GiantWreckText.CodexGroup, GiantWreckText.CodexKind(withClass));
        Assert.Equal(("Wanderer Seven", "Cruise Liner — A cruise liner."), GiantWreckText.CodexLine(withClass, "Wanderer Seven", Text));
        Assert.Equal("derelict:giant-sys2-0", GiantWreckText.CodexLine(withClass, string.Empty, Text).Name);
    }

    [Fact]
    public void RangesAndTheApproach_AreMeasuredFromTheHullBox_NotItsCentre()
    {
        var centre = new Vector3(0f, 100f, -300f);
        var half = new Vector3(40f, 30f, 128f); // a hull 256 long
        Assert.Equal(0f, HullApproach.BoxDistance(centre, half, centre));
        Assert.Equal(0f, HullApproach.BoxDistance(centre, half, centre + new Vector3(40f, 0f, 0f))); // on the face
        Assert.Equal(10f, HullApproach.BoxDistance(centre, half, centre + new Vector3(50f, 0f, 0f)), 3);
        Assert.Equal(5f, HullApproach.BoxDistance(centre, half, centre + new Vector3(43f, 34f, 0f)), 3); // past an edge

        // From ahead of the bow the ship stops in front of the bow — not at the centre, 128 blocks inside the hull.
        var from = centre + new Vector3(0f, 0f, 400f);
        var stop = HullApproach.StandOffPoint(centre, half, from, 24f);
        Assert.Equal(centre + new Vector3(0f, 0f, 128f + 24f), stop);
        Assert.Equal(24f, HullApproach.BoxDistance(centre, half, stop), 3);

        // Inside the box (a hangar, a breach) the ship is there already.
        var inside = centre + new Vector3(10f, 0f, 50f);
        Assert.Equal(inside, HullApproach.StandOffPoint(centre, half, inside, 24f));
    }

    [Fact]
    public void TheEditor_CallsAGiantShipsWayIn_JustEntrance()
    {
        Assert.Equal("ui.ed.giantship.entrance", StructureStampRules.MarkerLabelKey(StampTarget.GiantShip, GiantShipRules.EntranceMarker));
        Assert.Equal("ui.marker.entrance", StructureStampRules.MarkerLabelKey(StampTarget.SettlementModule, GiantShipRules.EntranceMarker));
        Assert.Equal("ui.marker.door_slide", StructureStampRules.MarkerLabelKey(StampTarget.GiantShip, GiantShipRules.DoorMarker));
        Assert.True(StructureStampRules.ShowsInPalette(StampTarget.GiantShip, "marker", GiantShipRules.EntranceMarker));
    }

    /// <summary>The battleship's shell as a pilot gets it (the cells next to outside space and what shows through its
    /// windows) — the hull a giant wreck's broken-look extras are planned on.</summary>
    private static Dictionary<Vector3i, BlockId> BattleshipShell(long seed, out int hullCells)
    {
        var content = Content.Value;
        var t = content.GetGiantShip("battleship");
        Assert.NotNull(t);
        var space = GiantShipGenerator.BuildSpace(t!, seed, content);
        var l = DerelictGenerator.GenerateShip(space, seed, content, GiantShipGenerator.IsAlien(t!)).Layout;
        var all = new List<KeyValuePair<Vector3i, ushort>>();
        for (int x = 0; x < l.Width; x++)
            for (int y = 0; y < l.Height; y++)
                for (int z = 0; z < l.Length; z++)
                {
                    ushort b = l.Get(x, y, z);
                    if (b != 0)
                    {
                        all.Add(new KeyValuePair<Vector3i, ushort>(new Vector3i(x, y, z), b));
                    }
                }

        hullCells = all.Count;
        var shell = HullShell.Build(new Vector3i(0, 0, 0), new Vector3i(l.Width - 1, l.Height - 1, l.Length - 1), all,
            c => l.Get(c.X, c.Y, c.Z), c => l.GetShape(c.X, c.Y, c.Z), StationHull.SeeThroughIds(content));
        return all.Where(kv => shell.IsVisible(kv.Key)).ToDictionary(kv => kv.Key, kv => new BlockId(kv.Value));
    }

    [Fact]
    public void TheBrokenLook_OnTheBattleshipsShell_IsPlannedQuickly_AndStaysAHandfulOfThings()
    {
        var cells = BattleshipShell(4242, out int hullCells);
        Assert.True(cells.Count > 10_000, $"the battleship's shell holds only {cells.Count} cells — is it still a giant?");

        var plan = DerelictDecorPlan.Build(cells, "derelict:giant-sys0-0"); // warm the JIT up
        var watch = new Stopwatch();
        long best = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            watch.Restart();
            plan = DerelictDecorPlan.Build(cells, "derelict:giant-sys0-0");
            watch.Stop();
            best = Math.Min(best, watch.ElapsedMilliseconds);
        }

        _output.WriteLine($"battleship: {hullCells} hull cells, shell {cells.Count} cells — decor plan {best} ms; " +
                          $"{plan.SparkCells.Count} sparks, {plan.DebrisHomes.Count} pieces, beacon {plan.BeaconCell}");

        // A handful of things to animate, whatever the hull's size: the per-frame part never walks the cells.
        Assert.InRange(plan.SparkCells.Count, 0, DerelictDecorPlan.MaxSparkCells);
        Assert.InRange(plan.DebrisHomes.Count, 0, 6);
        Assert.NotNull(plan.BeaconCell);

        // Once per design on first contact (the server sends a single changed cell as a cell, not a new design), on the main
        // thread. About 10 ms here; the sorted walk over every cell before #3615 took 200 ms — a bound with room for a slow
        // runner that still catches that again.
        Assert.True(best < 150, $"the decor plan took {best} ms on the battleship's shell of {cells.Count} cells");
    }
}
