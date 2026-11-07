// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The server side of the festival day calendars (#3315), on the shipped Advent calendar: who sees it (the players
/// who celebrate Christmas, everybody with every door when it is switched on by hand, nobody while festivals are off), the
/// player's local day from the join and the door request, doors that open once per season with seeded contents that never
/// re-roll, a full bag that keeps the door shut, the cosmetics a door unlocks, VEGA's greeting once per season, and the door
/// request served while the singleplayer pause holds the world. The festival clock is pinned per test.</summary>
public sealed class FestivalCalendarServerTests
{
    private const string Advent = "advent";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
        => new(Content.Value, name, padHalf: 8, configure: configure);

    private static DateTime Utc(int y, int m, int d, int h = 12) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static List<FestivalCalendarState> States(NpcLifeWorld w, PlayerSession who)
        => w.Transport.To<FestivalCalendarState>(who).Where(s => s.Calendar == Advent).ToList();

    private static PlayerSession Join(NpcLifeWorld w, int conn, string name, int utcOffsetMinutes, int preset = -1)
    {
        w.Server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            UtcOffsetMinutes = utcOffsetMinutes,
            FestivalPreset = preset,
        }));
        return w.Server.Sessions[conn];
    }

    /// <summary>Every backpack slot full of stone (never in the pool), on foot — no room for anything new.</summary>
    private static void FillBag(PlayerSession p)
    {
        p.State.AboardShip = false;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, new ItemStack("stone", Content.Value.MaxStackOf("stone")));
        }
    }

    private static void EmptyBag(PlayerSession p)
    {
        p.State.AboardShip = false;
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }
    }

    // ---------------- who and when ----------------

    [Fact]
    public void AtJoin_TheCalendarShows_ByThePlayersLocalDay()
    {
        using var w = World("calendar_join");
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1, 9)); // 18:00 in Tokyo on the 1st, 23:00 on 30 November in Hawaii

        var tokyo = Join(w, 71, "Aiko", +540);
        var hawaii = Join(w, 72, "Kai", -600);

        var state = Assert.Single(States(w, tokyo));
        Assert.True(state.Active);
        Assert.Equal("christmas", state.Festival);
        Assert.Equal(2031, state.Season);
        Assert.Equal(24, state.Doors);
        Assert.Equal(1, state.Available);
        Assert.Equal("2032-01-06", state.CatchUpUntil);
        Assert.Empty(States(w, hawaii)); // not December yet over there
        Assert.Single(w.Transport.To<CosmeticUnlocks>(tokyo)); // the unlocks go out at every join, even empty
        Assert.Single(w.Transport.To<CosmeticUnlocks>(hawaii));

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1, 10)); // midnight in Hawaii: the festival tick's look sends it
        Assert.Equal(1, Assert.Single(States(w, hawaii)).Available);
        Assert.Single(States(w, tokyo)); // nothing new in Tokyo — the same local day

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1, 15)); // midnight in Tokyo: its second door
        Assert.Equal(2, States(w, tokyo).Last().Available);
        Assert.Single(States(w, hawaii));
    }

    [Fact]
    public void ADoorRequest_CarriesTheLocalOffset_AndTheServerTakesIt()
    {
        using var w = World("calendar_offset");
        var p = w.Player;
        EmptyBag(p);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1, 20)); // the 1st in UTC, the 2nd in Tokyo

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 2, utcOffsetMinutes: 0);
        Assert.DoesNotContain(FestivalCalendarRules.DoorMilestone(Advent, 2031, 2), p.State.Milestones);

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 2, utcOffsetMinutes: +540);
        Assert.Contains(FestivalCalendarRules.DoorMilestone(Advent, 2031, 2), p.State.Milestones);
        Assert.Equal(540, p.UtcOffsetMinutes);
        Assert.Equal(2, States(w, p).Last().Reveal);
    }

    [Fact]
    public void APlayerWhoDoesNotCelebrateChristmas_GetsNoCalendar_AndOneWhoStopsSeesItEnd()
    {
        using var w = World("calendar_selection");
        string id = w.Player.State.PlayerId;
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 3));
        Assert.True(Assert.Single(States(w, w.Player)).Active); // the default selection has the big world festivals

        w.Server.SetFestivalPrefsForTest(id, (int)FestivalPreset.None);
        var end = States(w, w.Player).Last();
        Assert.False(end.Active); // one last state hides it
        Assert.False(w.Server.CalendarDayForTest(id, Advent).Active);

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 4));
        Assert.Equal(2, States(w, w.Player).Count); // nothing more while not celebrated

        var stranger = Join(w, 73, "Mia", 0, (int)FestivalPreset.None);
        Assert.Empty(States(w, stranger));
    }

    [Fact]
    public void FestivalsOff_ByTheWorldRuleOrTheOperator_MeanNoCalendar()
    {
        using var off = World("calendar_rule_off", c => c.Rules.Festivals = false);
        off.Server.SetFestivalClockForTest(Utc(2031, 12, 3));
        Assert.Empty(States(off, off.Player));
        Assert.False(off.Server.CalendarDayForTest(off.Player.State.PlayerId, Advent).Active);

        using var op = World("calendar_op_off", c => c.FestivalsOverride = "off");
        op.Server.SetFestivalClockForTest(Utc(2031, 12, 3));
        Assert.Empty(States(op, op.Player));

        using var live = World("calendar_live_off");
        live.Server.SetFestivalClockForTest(Utc(2031, 12, 3));
        Assert.True(States(live, live.Player).Last().Active);
        live.Player.State.Role = PlayerRole.Admin;
        live.Server.HandleForTest(live.Player, new AdminCommandIntent { Command = "festival", StringArg = "off" });
        Assert.False(States(live, live.Player).Last().Active); // switched off live: the calendar goes at once
    }

    [Fact]
    public void ChristmasSwitchedOnByHand_OpensEveryDoor_EvenInSummer_ForEverybody()
    {
        using var w = World("calendar_forced");
        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        w.Server.SetFestivalPrefsForTest(w.Player.State.PlayerId, (int)FestivalPreset.None); // even for one who celebrates nothing
        Assert.Empty(States(w, w.Player));

        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "christmas" });

        var state = States(w, w.Player).Last();
        Assert.True(state.Active);
        Assert.Equal(24, state.Available);
        Assert.Equal(2030, state.Season); // before this year's 1 December
    }

    // ---------------- doors ----------------

    [Fact]
    public void ADoor_OpensOnce_IntoTheBackpack_AndOnlyWhenItsDayHasCome()
    {
        using var w = World("calendar_doors");
        var p = w.Player;
        EmptyBag(p);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 2));
        var expected = FestivalCalendarRules.RollDoor(Content.Value.FindFestivalCalendar(Advent)!, w.Server.Metadata.Seed, p.State.PlayerId, 2031, 1);

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 1);

        foreach (var item in expected.Items)
        {
            Assert.Equal(item.Count, p.State.Inventory.CountOf(item.Item));
        }

        var state = States(w, p).Last();
        Assert.Equal(1, state.Reveal);
        Assert.Equal(1, state.OpenedMask);
        var content = Assert.Single(state.Contents);
        Assert.Equal(expected.Items.Select(i => i.Item), content.Items);
        Assert.Equal(expected.Items.Select(i => i.Count), content.Counts);

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 1); // once only
        foreach (var item in expected.Items)
        {
            Assert.Equal(item.Count, p.State.Inventory.CountOf(item.Item));
        }

        Assert.Equal(0, States(w, p).Last().Reveal);

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 5); // the 5th's door on the 2nd: still shut
        Assert.DoesNotContain(FestivalCalendarRules.DoorMilestone(Advent, 2031, 5), p.State.Milestones);
        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 2); // a missed door waits — and opens
        Assert.Equal(0b11, States(w, p).Last().OpenedMask);
    }

    [Fact]
    public void AFullBag_KeepsTheDoorShut_WithANotice_AndNothingIsLost()
    {
        using var w = World("calendar_bag_full");
        var p = w.Player;
        FillBag(p);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1));

        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 1);

        var state = States(w, p).Last();
        Assert.Equal(SvGameServer.CalendarBagFullNotice, state.Notice);
        Assert.Equal(0, state.Reveal);
        Assert.Equal(0, state.OpenedMask);
        Assert.DoesNotContain(FestivalCalendarRules.DoorMilestone(Advent, 2031, 1), p.State.Milestones);

        EmptyBag(p);
        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 1);
        Assert.Equal(1, States(w, p).Last().Reveal); // room made: the very same door opens
    }

    [Fact]
    public void TheContents_NeverReRoll_AcrossARestart_AndTheOpenedDoorsAreRemembered()
    {
        using var w = World("calendar_restart");
        EmptyBag(w.Player);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 3));
        for (int door = 1; door <= 3; door++)
        {
            w.Server.OpenCalendarDoorForTest(w.Player.State.PlayerId, Advent, door);
        }

        var before = Describe(States(w, w.Player).Last());

        w.Restart();
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 3, 13));
        w.Server.CalendarsOnJoinForTest(w.Player.State.PlayerId);

        var after = States(w, w.Player).Last();
        Assert.Equal(0b111, after.OpenedMask);
        Assert.Equal(before, Describe(after));

        static string Describe(FestivalCalendarState s)
            => string.Join(";", s.Contents.OrderBy(c => c.Door).Select(c => c.Door + ":" + string.Join(",", c.Items.Zip(c.Counts, (i, n) => i + "x" + n))));
    }

    [Fact]
    public void ACosmeticDoor_UnlocksItsTemplate_ForThisWorld_AndTheUnlockSurvivesARestart()
    {
        using var w = World("calendar_cosmetic");
        EmptyBag(w.Player);
        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "christmas" });
        int season = w.Server.CalendarDayForTest(w.Player.State.PlayerId, Advent).Season;

        w.Server.OpenCalendarDoorForTest(w.Player.State.PlayerId, Advent, 12); // the Christmas lights HUD frame

        Assert.Contains("cosmetic:hudframe:christmas_lights", w.Player.State.Milestones);
        Assert.Equal(new[] { "hudframe:christmas_lights" }, w.Transport.To<CosmeticUnlocks>(w.Player).Last().Keys);
        Assert.Equal("hudframe:christmas_lights", Assert.Single(States(w, w.Player).Last().Contents).Cosmetic);

        w.Server.OpenCalendarDoorForTest(w.Player.State.PlayerId, Advent, 24); // Rudolpho's star bell
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SvGameServer.StarBellItem));

        w.Restart();
        Assert.Contains(FestivalCalendarRules.DoorMilestone(Advent, season, 12), w.Player.State.Milestones);
        var ben = Join(w, 74, "Ben", 0);
        Assert.Empty(w.Transport.To<CosmeticUnlocks>(ben).Single().Keys); // per player: Ben has unlocked nothing
        w.Server.CalendarsOnJoinForTest(w.Player.State.PlayerId);
        Assert.Equal(new[] { "hudframe:christmas_lights" }, w.Transport.To<CosmeticUnlocks>(w.Player).Last().Keys);
    }

    [Fact]
    public void Vega_GreetsTheCalendar_OncePerSeason_AndOldSeasonsArePruned()
    {
        using var w = World("calendar_vega");
        EmptyBag(w.Player);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 1));
        w.Server.OpenCalendarDoorForTest(w.Player.State.PlayerId, Advent, 1);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 2));
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 3));

        var line = Assert.Single(w.Transport.Lines(w.Player, "calendar.advent.vega"));
        Assert.Equal(3, line.Kind); // the system channel, like the festival greeting
        Assert.Contains(FestivalCalendarRules.GreetedMilestone(Advent, 2031), w.Player.State.Milestones);

        w.Server.SetFestivalClockForTest(Utc(2032, 2, 1)); // over
        w.Server.SetFestivalClockForTest(Utc(2032, 12, 1)); // next season
        Assert.Equal(2, w.Transport.Lines(w.Player, "calendar.advent.vega").Count);
        Assert.DoesNotContain(w.Player.State.Milestones, m => FestivalCalendarRules.IsOlderSeason(m, Advent, 2032));
        Assert.Equal(0, States(w, w.Player).Last().OpenedMask); // a fresh calendar
    }

    // ---------------- the wire ----------------

    [Fact]
    public void WhileTheSingleplayerPauseHolds_TheDoorRequestIsStillServed()
    {
        using var w = World("calendar_paused");
        var p = w.Player;
        EmptyBag(p);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 5));
        w.Server.PauseForTest(p, true);
        Assert.True(w.Server.IsPaused);

        w.Server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new OpenCalendarDoorIntent { Calendar = Advent, Door = 4, UtcOffsetMinutes = 60 }));

        Assert.True(w.Server.IsPaused);
        Assert.Contains(FestivalCalendarRules.DoorMilestone(Advent, 2031, 4), p.State.Milestones);
        Assert.Equal(4, States(w, p).Last().Reveal);
    }

    [Fact]
    public void AnUnknownCalendarOrDoor_ChangesNothing()
    {
        using var w = World("calendar_junk");
        var p = w.Player;
        EmptyBag(p);
        w.Server.SetFestivalClockForTest(Utc(2031, 12, 30));
        int milestones = p.State.Milestones.Count;

        w.Server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new OpenCalendarDoorIntent { Calendar = "no_such_calendar", Door = 1 }));
        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 0);
        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, 25);
        w.Server.OpenCalendarDoorForTest(p.State.PlayerId, Advent, -3);

        Assert.Equal(milestones, p.State.Milestones.Count);
        Assert.All(States(w, p), s => Assert.Equal(0, s.Reveal));
    }
}
