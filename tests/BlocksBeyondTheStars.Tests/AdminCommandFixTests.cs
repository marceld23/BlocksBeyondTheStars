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
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Weather;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Three admin commands that answered and did nothing, or did something and said nothing (#2220): <c>/settime</c>
/// wrote a field nothing read, <c>/setweather</c> advertised "cloudy" while the key is "clouds", and <c>/give</c>
/// dropped whatever did not fit without a word — and said nothing when it worked either.
/// </summary>
public sealed class AdminCommandFixTests : IDisposable
{
    /// <summary>Where the admin stands: away from longitude 0, so the world clock and the local clock differ.</summary>
    private const float AdminX = 300f;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_admincmd_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(string name, NpcLifeWorld.RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true }, // the cheat commands' world option
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot in mid-air on rocky (no ship cargo to spill into), with an empty backpack.</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name, bool admin)
    {
        var s = server.AddLocalPlayer(name);
        s.State.Role = admin ? PlayerRole.WorldAdmin : PlayerRole.Player;
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(AdminX, 120, 0);
        for (int i = 0; i < s.State.Inventory.SlotCount; i++)
        {
            s.State.Inventory.SetSlot(i, null);
        }

        return s;
    }

    /// <summary>Runs one admin command and returns what the server sent because of it.</summary>
    private static List<(int Conn, object Msg)> Run(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who,
        string command, string? arg, int count = 0, string? target = null)
    {
        int before = t.Sent.Count;
        server.HandleForTest(who, new AdminCommandIntent { Command = command, StringArg = arg, IntArg = count, TargetPlayer = target });
        return t.Sent.Skip(before).ToList();
    }

    /// <summary>The chat lines one player got (a <see cref="ServerMessage"/> sent to their connection).</summary>
    private static List<string> LinesTo(List<(int Conn, object Msg)> sent, PlayerSession who)
        => sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToList();

    /// <summary>The lines everyone got (a server-wide broadcast).</summary>
    private static List<string> LinesToAll(List<(int Conn, object Msg)> sent)
        => sent.Where(s => s.Conn == int.MinValue).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToList();

    /// <summary>How far two day fractions lie apart on the 0..1 circle.</summary>
    private static double ClockDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % 1.0;
        return Math.Min(d, 1.0 - d);
    }

    // ---------------- /settime ----------------

    [Theory]
    [InlineData("midnight", 0.0, "midnight")]
    [InlineData("dawn", 0.25, "dawn")]
    [InlineData("day", 0.35, "day")]
    [InlineData("noon", 0.5, "noon")]
    [InlineData("dusk", 0.75, "dusk")]
    [InlineData("night", 0.85, "night")]
    [InlineData("NIGHT", 0.85, "night")]      // typed by hand: any letter case
    [InlineData("6", 0.25, "06:00")]          // an hour of the day
    [InlineData("18.5", 18.5 / 24.0, "18:30")]
    [InlineData("24", 0.0, "00:00")]
    [InlineData("0", 0.0, "00:00")]
    [InlineData("0.5", 0.5, "12:00")]         // below 1: a fraction of the day
    [InlineData("0,75", 0.75, "18:00")]       // a German keyboard's decimal comma
    public void SetTime_SetsTheLocalClockWhereTheAdminStands_AndNamesIt(string typed, double expected, string named)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("settime_ok", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, admin, "set_time", typed);

            // The sky above the admin shows the asked time …
            Assert.InRange(ClockDistance(server.LocalDayFractionForTest(AdminX), expected), 0.0, 1e-6);

            // … which the world clock reaches through the longitude: it is NOT simply the asked value.
            double shift = AdminX / (double)server.World.Circumference;
            Assert.InRange(ClockDistance(server.TimeOfDay + shift, expected), 0.0, 1e-4);
            Assert.True(shift > 1e-3);

            Assert.Equal("@srv.admin.time_set:" + named, Assert.Single(LinesToAll(sent)));
        }
    }

    [Fact]
    public void SetTime_NightIsNight_DayIsDay_AndTheClockRunsOnFromThere()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("settime_run", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            bool Night() => server.LocalDayFractionForTest(AdminX) is < 0.25 or > 0.75; // the sky's sunrise and sunset

            Run(server, t, admin, "set_time", "night");
            Assert.True(Night());
            Run(server, t, admin, "set_time", "midnight");
            Assert.True(Night());
            Run(server, t, admin, "set_time", "day");
            Assert.False(Night());
            Run(server, t, admin, "set_time", "noon");
            Assert.False(Night());

            // The command sets the clock, it does not stop it: a moment later it is a little past dusk.
            Run(server, t, admin, "set_time", "dusk");
            admin.State.Position = new Vector3f(AdminX, 120, 0);
            server.TickForTest(1.0);
            double after = server.LocalDayFractionForTest(AdminX);
            Assert.InRange(after, 0.75 + 1e-6, 0.76);
        }
    }

    [Fact]
    public void SetTime_ReachesEveryPlayerInTheWorldAtOnce()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("settime_env", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            var guest = OnFoot(server, "Guest", admin: false);

            var sent = Run(server, t, admin, "set_time", "night");

            // No tick has run: the new clock went out with the command itself, to both of them.
            foreach (var who in new[] { admin, guest })
            {
                var env = Assert.Single(sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<WorldEnvironment>());
                Assert.Equal(server.TimeOfDay, env.TimeOfDay);
            }
        }
    }

    [Theory]
    [InlineData("teatime")]
    [InlineData("25")]
    [InlineData("-3")]
    [InlineData("1e1")]
    [InlineData("nan")]
    [InlineData("")]
    [InlineData(null)]
    public void SetTime_RefusesWhatIsNoTime_AndLeavesTheClockAlone(string? typed)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("settime_bad", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            float before = server.TimeOfDay;

            var sent = Run(server, t, admin, "set_time", typed);

            Assert.Equal(before, server.TimeOfDay);
            Assert.Equal("@srv.admin.time_unknown", Assert.Single(LinesTo(sent, admin)));
            Assert.Empty(LinesToAll(sent)); // nobody is told the time was set
            Assert.DoesNotContain(sent, s => s.Msg is WorldEnvironment);
        }
    }

    [Fact]
    public void TheSetTimeUsageLine_OnlyNamesWordsTheCommandTakes()
    {
        foreach (string language in new[] { "en", "de" })
        {
            var words = UsageWords(TestLocales.Load(language)["ui.cmd.usage_settime"], "/settime");
            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.NotNull(SvGameServer.ParseTimeOfDay(word)));
        }
    }

    // ---------------- /setweather ----------------

    [Theory]
    [InlineData("cloudy", "clouds")]   // the word the usage line advertised
    [InlineData("CLOUDY", "clouds")]
    [InlineData("clouds", "clouds")]   // the exact key still works
    [InlineData(" Storm ", "storm")]
    [InlineData("acid_rain", "acid_rain")]
    public void SetWeather_TakesTheWordAsItIsTyped(string typed, string key)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("setweather_word", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, admin, "set_weather", typed);

            Assert.Equal(key, server.WeatherSimForTest.State);
            Assert.Equal("@srv.admin.weather_set:" + key, Assert.Single(LinesToAll(sent)));
        }
    }

    [Fact]
    public void SetWeather_TakesEveryKeyOfTheCatalogue_AndRefusesAnythingElse()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("setweather_all", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            foreach (string key in WeatherCatalog.AllKeys)
            {
                Run(server, t, admin, "set_weather", key);
                Assert.Equal(key, server.WeatherSimForTest.State);
            }

            Run(server, t, admin, "set_weather", "rain");
            foreach (string? unknown in new[] { "hurricane", "cloud y", string.Empty, null })
            {
                var sent = Run(server, t, admin, "set_weather", unknown);
                Assert.Equal("rain", server.WeatherSimForTest.State);
                Assert.Equal("@srv.admin.weather_unknown", Assert.Single(LinesTo(sent, admin)));
                Assert.Empty(LinesToAll(sent));
            }
        }
    }

    [Fact]
    public void TheWeatherAlias_IsACommandWord_NotAWireKey()
    {
        Assert.Equal("clouds", WeatherCatalog.FindByName("cloudy")?.Key);
        Assert.Null(WeatherCatalog.Find("cloudy"));            // exact lookups (the wire, planet data) are unchanged
        Assert.DoesNotContain("cloudy", WeatherCatalog.AllKeys);
        Assert.Null(WeatherCatalog.FindByName("hurricane"));
        Assert.Null(WeatherCatalog.FindByName(null));
        Assert.All(WeatherCatalog.AllKeys, key =>
        {
            Assert.Same(WeatherCatalog.Find(key), WeatherCatalog.FindByName(key));
            Assert.Same(WeatherCatalog.Find(key), WeatherCatalog.FindByName(key.ToUpperInvariant()));
        });
    }

    [Fact]
    public void TheSetWeatherTexts_OnlyNameWordsTheCommandTakes_AndTheRefusalNamesEveryKey()
    {
        foreach (string language in new[] { "en", "de" })
        {
            var table = TestLocales.Load(language);
            var words = UsageWords(table["ui.cmd.usage_setweather"], "/setweather");
            Assert.NotEmpty(words);
            Assert.All(words, word => Assert.NotNull(WeatherCatalog.FindByName(word)));

            // The answer to an unknown word is where the player reads what IS possible.
            string refusal = table["srv.admin.weather_unknown"];
            Assert.All(WeatherCatalog.AllKeys, key => Assert.Contains(key, refusal, StringComparison.Ordinal));
        }
    }

    /// <summary>The plain words of a usage line's <c>a|b|c</c> list, after the command's name. Placeholders such as
    /// <c>&lt;hour&gt;</c> are no words and are left out.</summary>
    private static List<string> UsageWords(string usage, string command)
    {
        int at = usage.IndexOf(command, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the usage line must name {command}");
        return usage.Substring(at + command.Length).Split('|')
            .Select(token => token.Trim())
            .Where(token => token.Length > 0 && token.All(c => c is (>= 'a' and <= 'z') or '_'))
            .ToList();
    }

    // ---------------- /give ----------------

    /// <summary>The line the admin reads, built from the locale template the way the server fills it. Until the key is
    /// in the locale table the template is the bracketed key — the two outcomes still read differently.</summary>
    private string GiveLine(string key, int given, int count, string item, string player)
    {
        var localizer = _content.CreateLocalizer(GameLocale.English);
        return localizer.Get(key)
            .Replace("{count}", count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{given}", given.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{item}", localizer.Get(_content.GetItem(item)!.NameKey))
            .Replace("{player}", player);
    }

    [Fact]
    public void Give_SaysWhatWasGiven()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_ok", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, admin, "give_item", "titanium_plate", count: 5);

            Assert.Equal(5, admin.State.Inventory.CountOf("titanium_plate"));
            string line = Assert.Single(LinesTo(sent, admin));
            Assert.Equal(GiveLine("srv.admin.gave", 5, 5, "titanium_plate", "Admin"), line);
            Assert.NotEqual(GiveLine("srv.admin.gave_partial", 5, 5, "titanium_plate", "Admin"), line);
            Assert.DoesNotContain("{", line, StringComparison.Ordinal); // every placeholder was filled
        }
    }

    [Fact]
    public void Give_ToAnotherPlayer_NamesThem_AndTellsTheAdmin()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_other", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            var guest = OnFoot(server, "Guest", admin: false);

            var sent = Run(server, t, admin, "give_item", "titanium_plate", count: 3, target: "guest");

            Assert.Equal(3, guest.State.Inventory.CountOf("titanium_plate"));
            Assert.Equal(0, admin.State.Inventory.CountOf("titanium_plate"));
            Assert.Equal(GiveLine("srv.admin.gave", 3, 3, "titanium_plate", "Guest"), Assert.Single(LinesTo(sent, admin)));
        }
    }

    [Fact]
    public void Give_SaysWhenOnlyPartFits()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_part", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);

            // One free slot: it takes exactly one full stack, and off the ship there is no cargo hold behind it.
            for (int i = 0; i < admin.State.Inventory.SlotCount - 1; i++)
            {
                Assert.Equal(0, admin.State.Inventory.Add("stone", 1, 1));
            }

            int stack = _content.MaxStackOf("titanium_plate");
            var sent = Run(server, t, admin, "give_item", "titanium_plate", count: stack + 7);

            Assert.Equal(stack, admin.State.Inventory.CountOf("titanium_plate"));
            string line = Assert.Single(LinesTo(sent, admin));
            Assert.Equal(GiveLine("srv.admin.gave_partial", stack, stack + 7, "titanium_plate", "Admin"), line);
            Assert.NotEqual(GiveLine("srv.admin.gave", stack, stack + 7, "titanium_plate", "Admin"), line);
        }
    }

    [Fact]
    public void Give_SaysWhenNothingFits()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_full", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);
            for (int i = 0; i < admin.State.Inventory.SlotCount; i++)
            {
                Assert.Equal(0, admin.State.Inventory.Add("stone", 1, 1));
            }

            var sent = Run(server, t, admin, "give_item", "titanium_plate", count: 3);

            Assert.Equal(0, admin.State.Inventory.CountOf("titanium_plate"));
            Assert.Equal(GiveLine("srv.admin.gave_partial", 0, 3, "titanium_plate", "Admin"), Assert.Single(LinesTo(sent, admin)));
        }
    }

    [Fact]
    public void Give_AnUnknownItem_IsRefused_AndGivesNoLine()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("give_unknown", t, out var repo);
        using (repo)
        {
            var admin = OnFoot(server, "Admin", admin: true);

            var sent = Run(server, t, admin, "give_item", "no_such_item", count: 2);

            Assert.Equal("@srv.admin.unknown_item", Assert.Single(sent.Select(s => s.Msg).OfType<ActionRejected>()).Reason);
            Assert.Empty(LinesTo(sent, admin));
        }
    }
}
