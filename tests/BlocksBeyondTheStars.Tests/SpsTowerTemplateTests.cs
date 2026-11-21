// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3893: the SPS guide towers as content — the structure kind's rule (terrain generation 47, about every fifth to sixth
/// inhabited world), the three shipped ruins (<c>data/sps_towers/*.json</c>, built by <c>tools/landmarks/sps_tower_*.py</c>) as
/// a pool of their own, and the tower generator: whatever the seed, the turn and the ruin pass do, the relay console, every
/// emergency lamp (burning) and — with a guard post — the capsule tank stand where the layout's markers say, the kept way up
/// is whole, and the tallest ruin stays under the atmosphere line wherever a tower can be seated.
/// </summary>
public sealed class SpsTowerTemplateTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] Shipped = { "sps_tower_broken", "sps_tower_leaning", "sps_tower_stump" };

    private static ushort Id(string block) => Content.GetBlock(block)!.NumericId.Value;

    [Fact]
    public void TheKind_ComesWithGeneration47_AndTheFileEqualsTheDefaults()
    {
        Assert.Equal(47, WorldDescription.SkyLightsGeneration);
        Assert.Equal(WorldDescription.SkyLightsGeneration, WorldDescription.CurrentTerrainGeneration);

        var rule = StructureTypeRules.Defaults.Get(StructureTypeRules.SpsTower);
        Assert.Equal("sps_tower", rule.Kind);
        Assert.True(rule.Enabled);
        Assert.Equal(WorldDescription.SkyLightsGeneration, rule.MinGeneration);
        Assert.Equal(0.18, rule.Chance);
        Assert.Equal(SpsTowerRules.StructureKind, rule.Whitelist);
        Assert.Equal(
            new[] { StructureTypeRules.ConditionAtmosphere, StructureTypeRules.ConditionNotAsteroid, StructureTypeRules.ConditionNotGas, StructureTypeRules.ConditionNotVoid },
            rule.Conditions.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(60, rule.KeepClearFrom[StructureTypeRules.Settlement]); // never nearer than 60 blocks to a settlement
        Assert.True(rule.KeepClearFrom[StructureTypeRules.Pad] >= 20); // and never on a landing pad

        // data/structure_types.json states the very same rule (StructureTypeRulesTests holds every kind together; this pins ours).
        var loaded = Content.StructureTypes.Get(StructureTypeRules.SpsTower);
        Assert.Equal((rule.Enabled, rule.MinGeneration, rule.Chance, rule.Whitelist, rule.KeepClear),
            (loaded.Enabled, loaded.MinGeneration, loaded.Chance, loaded.Whitelist, loaded.KeepClear));
        Assert.Equal(rule.Conditions.OrderBy(c => c, StringComparer.Ordinal), loaded.Conditions.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(rule.KeepClearFrom.OrderBy(kv => kv.Key, StringComparer.Ordinal), loaded.KeepClearFrom.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        Assert.Contains(StructureTypeRules.SpsTower, StructureTypeRules.Kinds);
    }

    [Fact]
    public void TheThreeRuins_LoadAsAPoolOfTheirOwn_AlwaysRuins_AlwaysProtected()
    {
        Assert.Equal(Shipped, Content.SpsTowers.Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var t in Content.SpsTowers)
        {
            Assert.True(t.SpsTower);
            Assert.False(t.Landmark);
            Assert.False(t.GiantShip);
            Assert.Equal(SpsTowerRules.TemplateKind, t.Kind);
            Assert.Empty(Content.SpsTowerProblems(t));
            Assert.True(LandmarkRules.IsRuin(t));
            Assert.Equal(LandmarkRules.ProfileLattice, LandmarkRules.ProfileOf(t));
            Assert.Equal(LandmarkRules.ProtectionProtected, LandmarkRules.ProtectionOf(t));
            Assert.Equal(WorldDescription.SkyLightsGeneration, t.MinTerrainGeneration);
            Assert.Equal(SpsTowerRules.NameKey(t.Key), t.NameKey);
            Assert.NotNull(SpsTowerRules.GuardPostBox(t)); // every shipped ruin can carry the guard post
            Assert.True(Math.Max(t.Width, Math.Max(t.Height, t.Length)) <= LandmarkRules.MaxSize);

            // One console, a way up with a chest on the highest platform left, chests in the relay room, lamps on the mast.
            var markers = t.Cells.Where(c => c.Kind == "marker").ToList();
            Assert.Single(markers, c => c.Id == SpsTowerRules.ConsoleMarker);
            Assert.Single(markers, c => c.Id == SpsTowerRules.TankMarker);
            Assert.Equal(2, markers.Count(c => c.Id == SpsTowerRules.LampMarker && SpsTowerRules.IsTankLamp(c.Config)));
            Assert.True(markers.Count(c => c.Id == SpsTowerRules.LampMarker && !SpsTowerRules.IsTankLamp(c.Config)) >= 6);
            var chests = markers.Where(c => c.Id == LandmarkRules.ChestMarker).ToList();
            Assert.True(chests.Count >= 4, $"{t.Key} has {chests.Count} chests");
            Assert.Contains(chests, c => c.Y >= 20); // the one up on the platform
            Assert.All(markers, c => Assert.Contains(c.Id, SpsTowerRules.Markers));
        }

        // No other pool offers one, and no pack list forgets them.
        Assert.DoesNotContain(Content.Landmarks, t => t.SpsTower || Shipped.Contains(t.Key));
        Assert.DoesNotContain(Content.GiantShips, t => t.SpsTower);
        Assert.Null(Content.LandmarkByKey("sps_tower_stump"));
        Assert.Equal(3, Content.SpsTowersFor(null, "meadowlands", generation: WorldDescription.SkyLightsGeneration).Count);
        Assert.Empty(Content.SpsTowersFor(null, "meadowlands", generation: WorldDescription.SkyLightsGeneration - 1));
    }

    [Fact]
    public void TheNames_AreLocaleKeysInBothLanguages_WithTheirCodexText()
    {
        foreach (string locale in new[] { "en", "de" })
        {
            string json = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", locale + ".json"));
            foreach (string key in Shipped)
            {
                Assert.Contains("\"" + SpsTowerRules.NameKey(key) + "\"", json, StringComparison.Ordinal);
                Assert.Contains("\"" + SpsTowerRules.DescriptionKey(key) + "\"", json, StringComparison.Ordinal);
            }

            foreach (string key in new[] { "ui.map.kind_sps_tower", SvGameServer.SpsTowerScanInfo, "ui.admin.help_spstower", "ui.worldopt.sps_towers" })
            {
                Assert.Contains("\"" + key + "\"", json, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ABrokenTemplate_IsRefused_AndNeverBreaksTheLoad()
    {
        var good = Content.GetSpsTower("sps_tower_stump")!;
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Protection = "open"), Content.Blocks.ContainsKey), p => p.Contains("protected", StringComparison.Ordinal));
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Ruin = "never"), Content.Blocks.ContainsKey), p => p.Contains("ruin", StringComparison.Ordinal));
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Landmark = true), Content.Blocks.ContainsKey), p => p.Contains("landmark", StringComparison.Ordinal));
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Cells.RemoveAll(c => c.Id == SpsTowerRules.ConsoleMarker)), Content.Blocks.ContainsKey),
            p => p.Contains(SpsTowerRules.ConsoleMarker, StringComparison.Ordinal));
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Cells.RemoveAll(c => c.Id == SpsTowerRules.TankMarker)), Content.Blocks.ContainsKey),
            p => p.Contains(SpsTowerRules.TankMarker, StringComparison.Ordinal));
        Assert.Contains(SpsTowerRules.Problems(Copy(good, t => t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 1, Kind = "marker", Id = "vendor" })), Content.Blocks.ContainsKey),
            p => p.Contains("vendor", StringComparison.Ordinal));

        var warnings = new List<string>();
        var catalog = new StructureCatalog();
        catalog.SetSpsTowers(new[] { Copy(good, t => (t.Key, t.Protection) = ("open_tower", "open")), Copy(good, _ => { }) }, Content.SpsTowerProblems,
            warnings.Add);
        Assert.Equal(new[] { good.Key }, catalog.SpsTowers.Select(t => t.Key));
        Assert.Single(warnings, w => w.Contains("Skipping guide tower 'open_tower'", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRuinPass_LeavesTheConsole_TheBurningLamps_TheTankAndTheWayUp()
    {
        ushort console = Id(SpsTowerRules.ConsoleBlock), lamp = Id(SpsTowerRules.LampBlock), tank = Id(SpsTowerRules.TankBlock), ladder = Id("ladder");
        var wood = new[] { "wood_planks", "wood_planks_light", "wood_planks_mahogany", "wood_log" }.Select(Id).ToHashSet();
        foreach (var t in Content.SpsTowers)
        {
            int towerLamps = t.Cells.Count(c => c.Kind == "marker" && c.Id == SpsTowerRules.LampMarker && !SpsTowerRules.IsTankLamp(c.Config));
            int ladderCells = 0;
            TemplateBody.ForEachPlain(t, p => ladderCells += p.Id == "ladder" ? 1 : 0);
            foreach (long seed in new long[] { 1, 77, -4242, 20261121 })
            {
                foreach (bool post in new[] { true, false })
                {
                    int turns = (int)(((seed % 4) + 4) % 4);
                    var layout = SpsTowerGenerator.Build(t, seed, turns, Content, "grass", atmosphere: true, guardPost: post);
                    var s = layout.Structure;
                    string at = $"{t.Key} seed {seed} turns {turns} post {post}";
                    Assert.True(layout.Ruined, at);
                    var parts = SpsTowerGenerator.PartsOf(layout);

                    // The relay console: one, where its marker came to rest, on a floor.
                    Assert.True(parts.Console is not null, at);
                    var c = parts.Console!.Value;
                    Assert.True(s.Get(c.X, c.Y, c.Z) == console, at + ": no console block at its marker");
                    Assert.True(s.Get(c.X, c.Y - 1, c.Z) != 0, at + ": the console stands on nothing");
                    Assert.Equal(1, Count(s, console));

                    // Every emergency lamp is a burning red lamp (the pass switched every authored light off).
                    Assert.Equal(towerLamps, parts.TowerLamps.Count);
                    Assert.Equal(parts.TowerLamps.Count, parts.TowerLamps.Distinct().Count());
                    Assert.All(parts.TowerLamps, l => Assert.True(s.Get(l.X, l.Y, l.Z) == lamp, at + $": lamp cell {l.X},{l.Y},{l.Z} holds {s.Get(l.X, l.Y, l.Z)}"));
                    Assert.Equal(0, Count(s, Id("strip_light_warm"))); // the room's own lights are dead (authored switched off)

                    // The guard post: the tank two cells high between its two lamps — or nothing of the hut at all.
                    Assert.Equal(post, parts.HasGuardPost);
                    if (post)
                    {
                        var k = parts.Tank!.Value;
                        Assert.True(s.Get(k.X, k.Y, k.Z) == tank && s.Get(k.X, k.Y + 1, k.Z) == tank, at + ": the tank is not two cells high");
                        Assert.Equal(2, Count(s, tank));
                        Assert.Equal(2, parts.TankLamps.Count);
                        Assert.All(parts.TankLamps, l => Assert.True(s.Get(l.X, l.Y, l.Z) == lamp, at));
                        Assert.Equal(parts.TowerLamps.Count + 2, Count(s, lamp));
                    }
                    else
                    {
                        Assert.Null(parts.Tank);
                        Assert.Empty(parts.TankLamps);
                        Assert.Equal(0, Count(s, tank));
                        Assert.Equal(parts.TowerLamps.Count, Count(s, lamp));
                        Assert.Equal(0, AllCells(s).Count(p => wood.Contains(s.Get(p.X, p.Y, p.Z)))); // no plank of the hut is left
                    }

                    // The kept way up: every ladder cell of the template still stands, and so do the chests.
                    Assert.Equal(ladderCells, Count(s, ladder));
                    int chests = t.Cells.Count(m => m.Kind == "marker" && m.Id == LandmarkRules.ChestMarker);
                    Assert.Equal(post ? chests : chests - 1, s.Markers.Count(m => m.Type == LandmarkRules.ChestMarker));
                    Assert.DoesNotContain(s.Markers, m => m.Type == SpsTowerRules.GuardPostMarker);

                    // The same seed derives the very same ruin (a later load reads the markers back from it).
                    var again = SpsTowerGenerator.Build(t, seed, turns, Content, "grass", atmosphere: true, guardPost: post);
                    Assert.Equal(AllCells(s).Select(p => s.Get(p.X, p.Y, p.Z)), AllCells(again.Structure).Select(p => again.Structure.Get(p.X, p.Y, p.Z)));
                }
            }
        }
    }

    [Fact]
    public void AboutFourInTenTowers_HaveAGuardPost_DecidedByTheSeed()
    {
        var t = Content.GetSpsTower("sps_tower_leaning")!;
        int with = Enumerable.Range(1, 2000).Count(seed => SpsTowerGenerator.RollsGuardPost(t, seed));
        Assert.InRange(with, 700, 900); // 0.4 of 2000
        Assert.Equal(SpsTowerGenerator.RollsGuardPost(t, 1234), SpsTowerGenerator.RollsGuardPost(t, 1234));
    }

    [Fact]
    public void TheTowersTop_StaysBelowTheAtmosphereLine_OnEveryPlanetTypeThatCanBeInhabited()
    {
        // The line is an ABSOLUTE world row (PlanetType.AtmosphereHeight), so the seat decides: a seat whose top would come
        // within the margin of the line is refused (SpsTowerFitsUnderSky, asked by the seat search for every candidate spot).
        Assert.True(SvGameServer.SpsTowerFitsUnderSky(groundY: 70, rise: 100, line: 190));
        Assert.False(SvGameServer.SpsTowerFitsUnderSky(groundY: 83, rise: 100, line: 190)); // 183 + the margin of 8 passes the line
        Assert.True(SvGameServer.SpsTowerFitsUnderSky(groundY: 400, rise: 100, line: 0)); // a body without a line

        int tallest = Content.SpsTowers.Max(SpsTowerRules.HeightAboveGround);
        int lowest = Content.SpsTowers.Min(SpsTowerRules.HeightAboveGround);
        Assert.InRange(tallest, 90, 120); // a guide tower is huge …
        Assert.True(lowest <= 60, "one ruin must be low enough for the worlds with a low sky");

        var inhabitable = Content.Planets.Values.Where(p => !p.Void && !p.IsGasWorld && !p.IsAirless && p.AtmosphereHeight > 0
                                                            && !p.Key.StartsWith("asteroid", StringComparison.Ordinal)).ToList();
        Assert.True(inhabitable.Count > 20);
        foreach (var planet in inhabitable)
        {
            // On ground as high as the type's rolling terrain reaches (base + amplitude) at least one ruin fits under the sky …
            int high = planet.BaseHeight + (int)Math.Ceiling((double)planet.Amplitude);
            Assert.True(SvGameServer.SpsTowerFitsUnderSky(high + 1, lowest, planet.AtmosphereHeight),
                $"{planet.Key}: no guide tower fits under its atmosphere line {planet.AtmosphereHeight} on ground {high}");

            // … and where its sky is as high as most skies (190 and up) so does the tallest one.
            if (planet.AtmosphereHeight >= 190)
            {
                Assert.True(SvGameServer.SpsTowerFitsUnderSky(high + 1, tallest, planet.AtmosphereHeight),
                    $"{planet.Key}: the tallest guide tower ({tallest}) passes its atmosphere line {planet.AtmosphereHeight} on ground {high}");
            }
        }
    }

    // ---------------------------------------------------------------- previews (on demand)

    /// <summary>
    /// Writes the three ruins AS THE SERVER DERIVES THEM — after the ruin pass, with the console, the lamps and the tank —
    /// for the seeds in <c>BBTS_SPS_TOWER_PREVIEW_SEEDS</c> (comma list, default <c>1</c>) as template files into the folder
    /// <c>BBTS_SPS_TOWER_PREVIEW</c>, for <c>tools/landmarks/preview.py</c>; each line says how much of the authored ruin
    /// fell. Does nothing without the folder — a builder's tool, not a check.
    /// </summary>
    [Fact]
    public void RuinPreviews_AreWrittenWhenAsked()
    {
        string? folder = Environment.GetEnvironmentVariable("BBTS_SPS_TOWER_PREVIEW");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var seeds = (Environment.GetEnvironmentVariable("BBTS_SPS_TOWER_PREVIEW_SEEDS") ?? "1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Directory.CreateDirectory(folder);
        var json = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var lines = new List<string>();
        foreach (var t in Content.SpsTowers)
        {
            int authored = 0;
            TemplateBody.ForEachPlain(t, p => authored += p.Cavity ? 0 : 1);
            foreach (long seed in seeds)
            {
                var layout = SpsTowerGenerator.Build(t, seed, 0, Content, "grass", atmosphere: true, guardPost: true);
                var s = layout.Structure;
                var ruin = new StructureTemplate
                {
                    Key = $"{t.Key}_ruin{seed}",
                    Name = $"{t.Name} (as derived, seed {seed})",
                    Landmark = true,
                    Width = s.Width,
                    Height = s.Height,
                    Length = s.Length,
                    GroundRow = layout.GroundRow,
                };
                foreach (var p in AllCells(s))
                {
                    ushort id = s.Get(p.X, p.Y, p.Z);
                    if (id != 0)
                    {
                        var (tint, glow) = s.GetModifier(p.X, p.Y, p.Z);
                        ruin.Cells.Add(new TemplateCell
                        {
                            X = p.X,
                            Y = p.Y,
                            Z = p.Z,
                            Id = Content.BlockById(new Shared.Primitives.BlockId(id))!.Key,
                            Tint = tint,
                            Glow = glow,
                            Shape = s.GetShape(p.X, p.Y, p.Z),
                        });
                    }
                }

                int left = ruin.Cells.Count;
                foreach (var m in s.Markers)
                {
                    ruin.Cells.Add(new TemplateCell { X = m.LocalPos.X, Y = m.LocalPos.Y, Z = m.LocalPos.Z, Kind = "marker", Id = m.Type, Config = m.Data });
                }

                TemplateBody.Compact(ruin);
                string path = Path.Combine(folder, ruin.Key + ".json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(ruin, json));
                lines.Add($"{ruin.Key}: {left} of {authored} blocks ({100.0 * (authored - left) / authored:0.0} % fell), {s.Markers.Count} markers");
            }
        }

        File.WriteAllLines(Path.Combine(folder, "ruins.txt"), lines);
    }

    private static StructureTemplate Copy(StructureTemplate t, Action<StructureTemplate> change)
    {
        var copy = new StructureTemplate
        {
            Key = t.Key,
            Name = t.Name,
            Kind = t.Kind,
            SpsTower = true,
            NameKey = t.NameKey,
            Width = t.Width,
            Height = t.Height,
            Length = t.Length,
            GroundRow = t.GroundRow,
            Ruin = t.Ruin,
            RuinProfile = t.RuinProfile,
            RuinStrength = t.RuinStrength,
            Protection = t.Protection,
            MinTerrainGeneration = t.MinTerrainGeneration,
            Requires = new List<string>(t.Requires),
            RuinZones = new List<RuinZone>(t.RuinZones),
            Palette = t.Palette,
            Rows = t.Rows,
            Cells = t.Cells.Select(c => new TemplateCell { X = c.X, Y = c.Y, Z = c.Z, Kind = c.Kind, Id = c.Id, Config = c.Config }).ToList(),
        };
        change(copy);
        return copy;
    }

    private static int Count(SettlementStructure s, ushort block) => AllCells(s).Count(p => s.Get(p.X, p.Y, p.Z) == block);

    private static IEnumerable<Vector3i> AllCells(SettlementStructure s)
    {
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    yield return new Vector3i(x, y, z);
                }
            }
        }
    }
}
