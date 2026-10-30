// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The server side of the festivals (#3001): the rule's default in old saves, the order of authority (operator
/// override → world rule → manual pick → calendar), the live edit and the admin command, persistence, and VEGA's greeting
/// once per festival-year. Dates are pinned far ahead so the real calendar never interferes.</summary>
public sealed class FestivalServerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
        => new(Content.Value, name, padHalf: 12, configure: configure);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnOldSave_ReadsFestivalsOn_WithNoPick_AndAnOlderServerSendsNothing()
    {
        var rules = JsonSerializer.Deserialize<GameRules>("{\"BaseVisitors\":true}")!;
        Assert.True(rules.Festivals);
        Assert.Equal(string.Empty, rules.FestivalPick);
        Assert.Equal(string.Empty, new ServerRules().Festivals); // an older server: the client hides the row, chip and mood
        Assert.Empty(new ServerRules().ActiveFestivals);
    }

    [Fact]
    public void TheCalendar_DecidesByUtcDate_AndThePrimaryFestivalComesFirst()
    {
        using var w = World("festival_calendar");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        Assert.Equal("halloween", w.Server.PrimaryFestival);
        Assert.True(w.Server.FestivalActive("halloween"));
        Assert.True(w.Server.FestivalThemeActive("harvest"));
        Assert.False(w.Server.FestivalThemeActive("lights"));

        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        Assert.Empty(w.Server.ActiveFestivals);

        w.Server.SetFestivalClockForTest(Utc(2031, 12, 26));
        Assert.Equal("kwanzaa", w.Server.PrimaryFestival); // started today; Christmas two days ago
        Assert.True(w.Server.FestivalActive("christmas"));

        var sent = w.Transport.Sent.Select(s => s.Msg).OfType<ServerRules>().Last();
        Assert.Equal("On", sent.Festivals);
        Assert.Equal(new[] { "kwanzaa", "christmas" }, sent.ActiveFestivals);
    }

    [Fact]
    public void TheOperatorOverride_BeatsTheWorld_AndIsNeverWrittenIntoTheSave()
    {
        using var off = World("festival_op_off", c => c.FestivalsOverride = "off");
        off.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        Assert.Empty(off.Server.ActiveFestivals);
        Assert.True(off.Server.Metadata.RulesOverride!.Festivals); // the world rule stays on …
        Assert.True(off.Server.Metadata.RulesOverride!.Festivals); // … and the save never learns of the operator's switch

        using var forced = World("festival_op_force", c => c.FestivalsOverride = "hanami");
        forced.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        Assert.Equal(new[] { "hanami" }, forced.Server.ActiveFestivals);

        using var junk = World("festival_op_junk", c => c.FestivalsOverride = "no_such_festival");
        junk.Server.SetFestivalClockForTest(Utc(2031, 10, 30));
        Assert.Empty(junk.Server.ActiveFestivals); // an unknown key forces nothing rather than falling back to the calendar
    }

    [Fact]
    public void TheWorldAdmin_EditsTheRuleAndThePickLive_AGuestCannot()
    {
        using var w = World("festival_rule");
        w.Server.SetFestivalClockForTest(Utc(2031, 10, 30));

        w.Player.State.Role = PlayerRole.Player;
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { Festivals = "Off" }));
        Assert.True(w.Server.Metadata.RulesOverride!.Festivals);

        w.Player.State.Role = PlayerRole.Admin;
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { Festivals = "Off" }));
        Assert.False(w.Server.Metadata.RulesOverride!.Festivals);
        Assert.Empty(w.Server.ActiveFestivals);
        Assert.False(w.Server.Metadata.RulesOverride!.Festivals);
        var sent = w.Transport.Sent.Select(s => s.Msg).OfType<ServerRules>().Last();
        Assert.Equal("Off", sent.Festivals);
        Assert.Empty(sent.ActiveFestivals);

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { Festivals = "On", FestivalPick = "diwali" }));
        Assert.Equal(new[] { "diwali" }, w.Server.ActiveFestivals); // the pick beats the calendar's Halloween
        Assert.Equal("diwali", w.Transport.Sent.Select(s => s.Msg).OfType<ServerRules>().Last().FestivalPick);

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { FestivalPick = "no_such_festival" }));
        Assert.Equal("diwali", w.Server.Metadata.RulesOverride!.FestivalPick); // unknown keys leave the pick alone

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new SetWorldRulesIntent { FestivalPick = "calendar" }));
        Assert.Equal(string.Empty, w.Server.Metadata.RulesOverride!.FestivalPick);
        Assert.Equal("halloween", w.Server.PrimaryFestival);
    }

    [Fact]
    public void TheFestivalCommand_PicksAnyFestivalOnAnyDay_AndTheSaveRemembers()
    {
        using var w = World("festival_cmd");
        w.Player.State.Role = PlayerRole.Admin;
        w.Server.SetFestivalClockForTest(Utc(2031, 7, 15));
        Assert.Empty(w.Server.ActiveFestivals);

        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "halloween" });
        Assert.Equal(new[] { "halloween" }, w.Server.ActiveFestivals);
        Assert.Equal("halloween", w.Server.Metadata.RulesOverride!.FestivalPick);
        Assert.Contains(w.MessagesToPlayer(), m => m.Contains("Halloween", StringComparison.Ordinal));

        w.Restart(); // the pick lives in the save's rules — the next launch reads it back (whatever the real date)
        Assert.Equal("halloween", w.Server.Metadata.RulesOverride!.FestivalPick);
        Assert.Equal(new[] { "halloween" }, w.Server.ActiveFestivals);
        w.Player.State.Role = PlayerRole.Admin;

        int rejectedBefore = w.Transport.Sent.Select(s => s.Msg).OfType<ActionRejected>().Count();
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "no_such_festival" });
        Assert.Equal(rejectedBefore + 1, w.Transport.Sent.Select(s => s.Msg).OfType<ActionRejected>().Count());
        Assert.Equal("halloween", w.Server.Metadata.RulesOverride!.FestivalPick);

        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "calendar" });
        Assert.Equal(string.Empty, w.Server.Metadata.RulesOverride!.FestivalPick);
        Assert.True(w.Server.Metadata.RulesOverride!.Festivals);

        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "festival", StringArg = "off" });
        Assert.False(w.Server.Metadata.RulesOverride!.Festivals);
        Assert.False(w.Server.Metadata.RulesOverride!.Festivals);
    }

    [Fact]
    public void Vega_GreetsAFestivalOncePerYear_OnTheSystemChannel()
    {
        using var w = World("festival_greet");
        w.Server.SetFestivalClockForTest(Utc(2031, 5, 1)); // nothing on
        w.Transport.Sent.Clear();

        w.Server.SetFestivalClockForTest(Utc(2031, 3, 30)); // Hanami begins for everyone online
        var greetings = Greetings();
        var line = Assert.Single(greetings);
        Assert.Equal("festival.hanami.vega", line.LineKey);
        Assert.Equal(3, line.Kind); // system — the hints mute never swallows it
        Assert.Contains("festival:hanami:2031", w.Player.State.Milestones);

        w.Server.SetFestivalClockForTest(Utc(2031, 5, 1));
        w.Server.SetFestivalClockForTest(Utc(2031, 3, 31)); // on again in the same year: no second greeting
        Assert.Single(Greetings());

        w.Server.SetFestivalClockForTest(Utc(2031, 5, 1));
        w.Server.SetFestivalClockForTest(Utc(2032, 3, 30)); // next year: greeted again
        Assert.Equal(2, Greetings().Count);
        Assert.Contains("festival:hanami:2032", w.Player.State.Milestones);

        System.Collections.Generic.List<ShipAiLine> Greetings()
            => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
                .Where(l => l.LineKey.StartsWith("festival.", StringComparison.Ordinal)).ToList();
    }
}
