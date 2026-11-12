// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The generator goldens of #3511, second half: the small procedural structures of the wilds, the rail, the buried
/// G.D.S. ruin, the wrecks and the derelicts. See <c>StructureGoldens.Generators.cs</c> for the rules.</summary>
public sealed partial class StructureGeneratorGoldenTests
{
    [Fact]
    public void CampsLodgesOutpostsLabsAndFactories_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        for (int i = 0; i < Seeds.Length; i++)
        {
            cases.Add(($"banditcamp:{N(Seeds[i])}:{Surfaces[i]}", Hash(BanditCampGenerator.Generate(Seeds[i], Surfaces[i], Content))));
            cases.Add(($"outpost:{N(Seeds[i])}:{Surfaces[i]}", StructureGoldens.Outpost(Content, PioneerOutpostGenerator.Generate(Seeds[i], Surfaces[i], Content))));
            cases.Add(($"spslab:{N(Seeds[i])}", Hash(SpsLabGenerator.Generate(Seeds[i], i == 0 ? "stone" : "sand", Content))));
        }

        // Every wall kind of the outpost (the rolls are drawn anyway, so a forced kind shifts nothing else), and its boat.
        foreach (string wall in PioneerOutpostGenerator.WallKinds)
        {
            var o = PioneerOutpostGenerator.Generate(Seeds[1], "grass", Content, wall, hidden: true, chests: PioneerOutpostGenerator.MaxChests);
            cases.Add(($"outpost:wall:{wall}", StructureGoldens.Outpost(Content, o)));
        }

        foreach (bool alongX in new[] { true, false })
        {
            var boat = PioneerOutpostGenerator.Boat(alongX, Content).Select(c => (c.Local, c.Block, 0, 0, c.Shape));
            cases.Add(($"outpost:boat:{(alongX ? "x" : "z")}", StructureGoldens.Cells(Content, boat)));
        }

        // A lodge per yard kind (the professions differ by their yard), plain and alien, with and without the planet's ores.
        string[] yards = { LodgeRules.YardPlot, LodgeRules.YardQuarry, LodgeRules.YardWoodpile, LodgeRules.YardDryingFrame };
        foreach (string yard in yards)
        {
            for (int i = 0; i < 2; i++)
            {
                string[] ores = i == 0 ? new[] { "iron_ore", "copper_ore" } : Array.Empty<string>();
                var s = LodgeGenerator.Generate(Seeds[i + 1], yard, Surfaces[i], ores, alien: i == 1, Content);
                cases.Add(($"lodge:{yard}:{N(Seeds[i + 1])}:{Surfaces[i]}:{(i == 1 ? "alien" : "plain")}", Hash(s)));
            }
        }

        foreach (int machines in new[] { 1, 3, 6 })
        {
            cases.Add(($"factory:{machines}:{N(Seeds[1])}", Hash(FactoryGenerator.Generate(Seeds[1], machines, Content))));
        }

        cases.Add(($"factory:2:{N(Seeds[2])}", Hash(FactoryGenerator.Generate(Seeds[2], 2, Content))));
        StructureGoldens.AssertPinned("camps, lodges, outposts, labs and factories", WildsPins, cases, LinuxPins);
    }

    [Fact]
    public void RailStationsAndRailRuins_EveryHeading_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        for (int heading = 0; heading < 4; heading++)
        {
            cases.Add(($"railstation:h{heading}", Hash(RailStationGenerator.Generate(heading, Content))));
            for (int i = 0; i < 2; i++)
            {
                var s = RailRuinGenerator.Generate(Seeds[i + 1], heading, Surfaces[i], Content);
                cases.Add(($"railruin:h{heading}:{N(Seeds[i + 1])}:{Surfaces[i]}", Hash(s)));
            }
        }

        StructureGoldens.AssertPinned("rail stations and rail ruins", RailPins, cases, LinuxPins);
    }

    [Fact]
    public void AncientRuins_TheShippedKit_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        var kit = Content.KitByKey("ruin_gds_1")!;
        Assert.True(AncientRuinGenerator.IsGdsRuinKit(kit));
        var spec = CityLayoutSpec.FromKit(kit);
        var crossings = AncientRuinGenerator.EntranceCrossings(spec);
        var pool = ModulePool();
        for (int i = 0; i < 2; i++)
        {
            var (ci, cj) = crossings[(i * 7) % crossings.Count];
            var composition = new List<string>();
            var ruin = AncientRuinGenerator.Compose(Seeds[i + 1], Content, spec, kit, pool, composition, ci, cj);
            ulong hash = StructureGoldens.AncientRuin(Content, ruin);
            cases.Add(($"gdsruin:{N(Seeds[i + 1])}:x{ci}:{cj}", hash));

            // The record replays the picks: the same ruin again (the server's every later load).
            var replay = AncientRuinGenerator.Compose(Seeds[i + 1], Content, spec, kit, pool, composition, ci, cj);
            Assert.Equal(hash, StructureGoldens.AncientRuin(Content, replay));
        }

        // An authored structure enclosed as built (an author's underground template or kit): a small procedural hamlet here.
        var hamlet = SettlementGenerator.Generate("hamlet", false, Seeds[1], "grass", Content);
        var box = AncientRuinGenerator.Enclose(Seeds[1], Content, hamlet, "test_kit", new List<string> { "a", string.Empty });
        cases.Add(("enclose:hamlet", StructureGoldens.AncientRuin(Content, box)));
        StructureGoldens.AssertPinned("ancient ruins", AncientRuinPins, cases, LinuxPins);
    }

    [Fact]
    public void WrecksOfEveryShipDesign_AndDerelicts_MatchThePins()
    {
        var cases = new List<(string, ulong)>();
        foreach (var design in Content.Ships.Values.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            foreach (long seed in Seeds.Take(2))
            {
                cases.Add(($"wreck:{design.Key}:{N(seed)}", StructureGoldens.Wreck(Content, WreckGenerator.Generate(design, seed, Content))));
            }
        }

        foreach (long seed in Seeds)
        {
            cases.Add(($"derelict:beta:{N(seed)}", StructureGoldens.Derelict(Content, DerelictGenerator.Generate(seed, Content))));
        }

        foreach (var (seed, proceduralOnly) in new[] { (7919L, true), (Seeds[1], false), (5L * 7919, false), (Seeds[2], false) })
        {
            var l = DerelictGenerator.GenerateLegacy(seed, Content, proceduralOnly);
            cases.Add(($"derelict:legacy:{N(seed)}:{(proceduralOnly ? "procedural" : "any")}", StructureGoldens.Derelict(Content, l)));
        }

        StructureGoldens.AssertPinned("wrecks and derelicts", WreckPins, cases, LinuxPins);
    }
}
