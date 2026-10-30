// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The festivals follow-up (#3019): "My festivals" (#3021) — presets, the per-player list the server greets, sends
/// and grows pumpkins for, a forced festival for everybody, the intent while the pause holds the world; the mood's core days
/// (#3022); Halloween's and Christmas' own packs and the harvest pack for every plant (#3023); the spawner's guards (#3020).
/// Dates are pinned in 2031 so the real calendar never interferes (the dated festivals have no 2031 ranges).</summary>
public sealed class FestivalFollowUpTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    private static FestivalDefinition F(string key) => Content.Value.FindFestival(key)!;

    /// <summary>A breathable world (pumpkins and trees grow there) with natural grass — no owner, so it is no player's build.</summary>
    private static NpcLifeWorld World(string name, bool ownedGrass = false)
    {
        string planet = Content.Value.Planets.Values.First(p => string.Equals(p.Atmosphere, "breathable", StringComparison.OrdinalIgnoreCase) && !p.Void).Key;
        var w = new NpcLifeWorld(Content.Value, name, padHalf: 16, configure: c =>
        {
            c.StartPlanet = planet;
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.CreatureAbundance = AlienActivity.Off;
        });
        for (int dx = -14; dx <= 14; dx++)
        {
            for (int dz = -14; dz <= 14; dz++)
            {
                if (ownedGrass)
                {
                    w.Set(NpcLifeWorld.Cx + dx, w.PadY, NpcLifeWorld.Cz + dz, "grass"); // a player's lawn
                }
                else
                {
                    w.Server.World.SetBlock(new Vector3i(NpcLifeWorld.Cx + dx, w.PadY, NpcLifeWorld.Cz + dz), Content.Value.GetBlock("grass")!.NumericId);
                }
            }
        }

        return w;
    }

    // ---------------- "My festivals": the presets (#3021) ----------------

    [Fact]
    public void ThePresets_ResolveByBigFestivalsAndLanguage()
    {
        var all = Content.Value.Festivals;
        var de = FestivalSelection.Resolve(all, FestivalPreset.BigAndLanguage, "de", null);
        Assert.Contains("erntedank", de);
        Assert.Contains("st_martin", de);
        Assert.Contains("christmas", de);
        Assert.Contains("diwali", de); // a big world festival — everybody's default
        Assert.DoesNotContain("seollal", de);

        var ru = FestivalSelection.Resolve(all, FestivalPreset.BigAndLanguage, "ru-RU", null);
        Assert.Contains("orthodox_christmas", ru); // #3026
        Assert.Contains("maslenitsa", ru);

        Assert.Equal(all.Count, FestivalSelection.Resolve(all, FestivalPreset.All, "en", null).Count);
        Assert.Empty(FestivalSelection.Resolve(all, FestivalPreset.None, "en", null));
        var ja = FestivalSelection.Resolve(all, FestivalPreset.Language, "ja", null);
        Assert.Contains("tanabata", ja);
        Assert.Contains("new_year", ja); // "*": every language
        Assert.DoesNotContain("diwali", ja);
        Assert.Equal(new HashSet<string> { "holi", "hanami" },
            FestivalSelection.Resolve(all, FestivalPreset.Custom, "en", FestivalSelection.ParseCustom("Holi, hanami,no_such")));

        Assert.Equal(FestivalPreset.BigAndLanguage, FestivalSelection.Parse(-1));
        Assert.Equal(53, all.Count);
        Assert.Equal(14, all.Count(f => f.Big));
    }

    [Fact]
    public void TheMoodShowsOnCoreDaysOnly()
    {
        Assert.True(FestivalCalendar.IsCoreDay(F("halloween"), Utc(2031, 10, 24))); // the whole Halloween week
        Assert.True(FestivalCalendar.IsCoreDay(F("halloween"), Utc(2031, 11, 1)));
        Assert.True(FestivalCalendar.IsCoreDay(F("st_lucia"), Utc(2031, 12, 13)));
        Assert.True(FestivalCalendar.IsCoreDay(F("kwanzaa"), Utc(2031, 12, 28))); // day 3 of 7
        Assert.False(FestivalCalendar.IsCoreDay(F("kwanzaa"), Utc(2031, 12, 29)));
        Assert.True(FestivalCalendar.IsCoreDay(F("hanami"), Utc(2031, 3, 31))); // day 7
        Assert.False(FestivalCalendar.IsCoreDay(F("hanami"), Utc(2031, 4, 1)));
        Assert.True(FestivalCalendar.IsCoreDay(F("advent"), Utc(2031, 12, 7))); // a Sunday of Advent
        Assert.False(FestivalCalendar.IsCoreDay(F("advent"), Utc(2031, 12, 8)));
        Assert.False(FestivalCalendar.IsCoreDay(F("halloween"), Utc(2031, 7, 15))); // outside the window

        // A festival that starts inside Halloween week does not take over its chip and mood (priority, #3022).
        Assert.Equal(new[] { "halloween", "dia_de_muertos" }, FestivalSelection.ByPriority(new[] { "dia_de_muertos", "halloween" }, Content.Value.FindFestival));
    }

    // ---------------- "My festivals" on the server (#3021) ----------------

    [Fact]
    public void ThePlayersSelection_DecidesWhatTheyCelebrate_AForcedFestivalShowsForEverybody()
    {
        using var w = World("festival_myfestivals");
        string id = w.Player.State.PlayerId;
        w.Server.SetFestivalClockForTest(Utc(2031, 11, 1)); // Halloween's last day + Día de los Muertos
        Assert.Equal("dia_de_muertos", w.Server.PrimaryFestival); // the world's calendar: the one that started last
        Assert.Equal(new[] { "halloween", "dia_de_muertos" }, w.Server.ActiveFestivalsForTest(id)); // the default, Halloween leads

        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.Custom, new[] { "dia_de_muertos" });
        Assert.Equal(new[] { "dia_de_muertos" }, w.Server.ActiveFestivalsForTest(id));
        var rules = w.Transport.Sent.Select(s => s.Msg).OfType<ServerRules>().Last();
        Assert.Equal(new[] { "dia_de_muertos" }, rules.ActiveFestivals); // the chip and the mood follow at once
        Assert.False(rules.FestivalForced);

        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.None);
        Assert.Empty(w.Server.ActiveFestivalsForTest(id));

        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "hanami" });
        Assert.Equal(new[] { "hanami" }, w.Server.ActiveFestivalsForTest(id)); // forced: for everybody, "None" or not
        Assert.True(w.Transport.Sent.Select(s => s.Msg).OfType<ServerRules>().Last().FestivalForced);
    }

    [Fact]
    public void VegaGreetsOnlyWhatThePlayerCelebrates_AndANewlyCelebratedFestivalAtOnce()
    {
        using var w = World("festival_myfestivals_greet");
        string id = w.Player.State.PlayerId;
        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.None);
        w.Server.SetFestivalClockForTest(Utc(2031, 5, 1));
        w.Transport.Sent.Clear();

        w.Server.SetFestivalClockForTest(Utc(2031, 3, 26)); // Hanami begins — not for a player who celebrates nothing
        Assert.Empty(Greetings());

        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.All);
        Assert.Equal("festival.hanami.vega", Assert.Single(Greetings()).LineKey);

        List<ShipAiLine> Greetings()
            => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
                .Where(l => l.LineKey.StartsWith("festival.", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public void TheSelection_IsServedWhileThePauseHoldsTheWorld()
    {
        using var w = World("festival_myfestivals_pause");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        Assert.NotEmpty(w.Server.ActiveFestivalsForTest(w.Player.State.PlayerId));

        w.Server.PauseForTest(w.Player, true); // the settings live in the pause menu
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetFestivalPrefsIntent { Preset = (int)FestivalPreset.None }));
        Assert.Empty(w.Server.ActiveFestivalsForTest(w.Player.State.PlayerId));
        w.Server.PauseForTest(w.Player, false);
    }

    // ---------------- Halloween's and Christmas' own packs (#3023) ----------------

    [Fact]
    public void HalloweenAndChristmas_StartTheirOwnPacks_NotTheirThemes()
    {
        using var w = World("festival_ownpacks");
        string id = w.Player.State.PlayerId;
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        Assert.True(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalHalloween));
        Assert.False(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalHarvest));

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 25)); // Christmas
        Assert.True(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalChristmas));
        Assert.False(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalLights));

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 26)); // Kwanzaa (lights) begins beside Christmas
        Assert.True(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalLights));

        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.All);
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 5)); // Erntedank (and World Space Week)
        Assert.True(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalHarvest));
        Assert.False(w.Server.WorldPredicateForTest(id, StoryWorldPredicates.FestivalHalloween));
    }

    // ---------------- The spawner's guards and the selection (#3020, #3021) ----------------

    [Fact]
    public void Pumpkins_NeverGrowOnAPlayersGround_NorRightBesideABody()
    {
        using (var lawn = World("festival_guard_lawn", ownedGrass: true))
        {
            lawn.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
            for (int i = 0; i < 10; i++)
            {
                lawn.Server.RunFestivalSpawnPassForTest();
            }

            Assert.Empty(lawn.Server.FestivalDepositCellsForTest); // a lawn somebody laid is theirs
        }

        using var w = World("festival_guard_body");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        for (int i = 0; i < 20; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        var feet = w.Player.State.Position.ToBlock();
        Assert.NotEmpty(w.Server.FestivalDepositCellsForTest);
        Assert.All(w.Server.FestivalDepositCellsForTest, c => Assert.True(Math.Max(Math.Abs(c.X - feet.X), Math.Abs(c.Z - feet.Z)) > 2, $"{c} beside the player at {feet}"));
    }

    [Fact]
    public void Pumpkins_GrowOnlyAroundAPlayerWhoCelebrates_OrForAForcedHalloween()
    {
        using var w = World("festival_guard_none");
        string id = w.Player.State.PlayerId;
        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.None);
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        for (int i = 0; i < 5; i++)
        {
            w.Server.RunFestivalSpawnPassForTest();
        }

        Assert.Empty(w.Server.FestivalDepositCellsForTest);

        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "halloween" });
        w.Server.RunFestivalSpawnPassForTest();
        Assert.NotEmpty(w.Server.FestivalDepositCellsForTest);
    }

    [Fact]
    public void TheAutumnEquinox_BringsPumpkinsToo_ForAPlayerWhoCelebratesIt()
    {
        using var w = World("festival_equinox");
        w.Server.SetFestivalPrefsForTest(w.Player.State.PlayerId, (int)FestivalPreset.All);
        w.Server.SetFestivalClockForTest(Utc(2031, 9, 22));
        w.Server.RunFestivalSpawnPassForTest();
        Assert.NotEmpty(w.Server.FestivalDepositCellsForTest);
        Assert.All(w.Server.FestivalDepositCellsForTest, c => Assert.Equal(Content.Value.GetBlock("flora_pumpkin")!.NumericId.Value, w.Server.World.GetBlock(c).Value));
    }

    // ---------------- Counting trees, not needles (#3023, #3020) ----------------

    [Fact]
    public void ScanningFirTrees_CountsEachTreeOnce_AndOnlyRealCellsInReach()
    {
        using var w = World("festival_firs");
        string id = w.Player.State.PlayerId;
        var events = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.ScanTree, events);
        Fir(w, NpcLifeWorld.Cx + 3, NpcLifeWorld.Cz + 3);
        Fir(w, NpcLifeWorld.Cx - 4, NpcLifeWorld.Cz + 4);

        // Ten different needle cells of the first fir: one tree.
        foreach (var (dx, dz) in new[] { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) })
        {
            Scan(NpcLifeWorld.Cx + 3 + dx, w.Feet + 4, NpcLifeWorld.Cz + 3 + dz, "pine_needles");
        }

        Scan(NpcLifeWorld.Cx + 3, w.Feet + 5, NpcLifeWorld.Cz + 3, "pine_needles");
        Scan(NpcLifeWorld.Cx + 3, w.Feet + 1, NpcLifeWorld.Cz + 3, "wood_log");
        Assert.Single(events.Of(GameEventKeys.ScanTree));
        Assert.Equal("conifer", events.Of(GameEventKeys.ScanTree).Single().Arg);

        // Its trunk from the second fir: a second tree.
        Scan(NpcLifeWorld.Cx - 4, w.Feet, NpcLifeWorld.Cz + 4, "wood_log");
        Assert.Equal(2, events.Of(GameEventKeys.ScanTree).Count());
        Assert.Equal(2, w.Player.State.AchievementCounters[AchievementCounters.ScanTree("conifer")]);

        // A cell that does not hold what the client names, and one out of reach, count nothing.
        Scan(NpcLifeWorld.Cx, w.PadY, NpcLifeWorld.Cz, "pine_needles"); // grass under the feet
        w.Server.World.SetBlock(new Vector3i(NpcLifeWorld.Cx + 14, w.Feet + 4, NpcLifeWorld.Cz - 14), Content.Value.GetBlock("pine_needles")!.NumericId);
        Scan(NpcLifeWorld.Cx + 14, w.Feet + 4, NpcLifeWorld.Cz - 14, "pine_needles");
        Assert.Equal(2, events.Of(GameEventKeys.ScanTree).Count());

        void Scan(int x, int y, int z, string key) => w.Server.ScanSubject(id, "block", key, null, new Vector3i(x, y, z));
    }

    /// <summary>A small natural fir: four logs on the pad, a 3×3 needle crown on top and one needle cell above.</summary>
    private static void Fir(NpcLifeWorld w, int x, int z)
    {
        var log = Content.Value.GetBlock("wood_log")!.NumericId;
        var needles = Content.Value.GetBlock("pine_needles")!.NumericId;
        for (int y = w.Feet; y < w.Feet + 4; y++)
        {
            w.Server.World.SetBlock(new Vector3i(x, y, z), log);
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                w.Server.World.SetBlock(new Vector3i(x + dx, w.Feet + 4, z + dz), needles);
            }
        }

        w.Server.World.SetBlock(new Vector3i(x, w.Feet + 5, z), needles);
    }

    // ---------------- Every harvest counts (#3023) ----------------

    [Fact]
    public void HarvestingAPlant_RaisesHarvestAny_WithThePlantsKey()
    {
        using var w = World("festival_harvest_any");
        var events = new RecordingGameEventListener();
        w.Server.AddGameEventListener(GameEventKeys.HarvestAny, events);
        var cell = new Vector3i(NpcLifeWorld.Cx + 1, w.Feet, NpcLifeWorld.Cz + 1);
        w.Server.World.SetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z), Content.Value.GetBlock("dirt")!.NumericId);
        w.Server.World.SetBlock(cell, Content.Value.GetBlock("flora_cropberry")!.NumericId);
        for (int i = 0; i < 40 && w.Server.World.GetBlock(cell).Value != 0; i++)
        {
            w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new MineBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
            w.Server.Tick(0.1);
        }

        Assert.Equal(0, w.Server.World.GetBlock(cell).Value);
        Assert.Equal("flora_cropberry", Assert.Single(events.Of(GameEventKeys.HarvestAny)).Arg);
    }
}
