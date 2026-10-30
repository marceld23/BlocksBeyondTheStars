// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Runtime.CompilerServices;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Pins the festivals' clock (#3001) for every <c>GameServer</c> this suite builds: the default is a day with no festival, so no
/// test depends on the real date — in Halloween week the calendar started the harvest challenge for every joining test player,
/// took the objective chip and spawned pumpkins in tests that never asked for a festival (#3015's first CI run). A festival test
/// pins its own date (<c>server.SetFestivalClockForTest(…)</c>), picks a festival (<c>Metadata.RulesOverride.FestivalPick</c>)
/// or sets the operator override (<c>ServerConfig.FestivalsOverride</c>). <c>FestivalCalendarTests</c> guards that the day stays
/// quiet when festivals are added; the client suite's <c>ClientServerHarness</c> passes <c>FestivalsOverride = "off"</c> instead.
/// </summary>
internal static class TestFestivalClock
{
    /// <summary>Noon UTC on a day no festival covers: 2031 lies beyond the dated festivals' years, and no fixed or weekday window
    /// reaches 1 May.</summary>
    public static readonly DateTime QuietDay = new(2031, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    [ModuleInitializer]
    internal static void PinTheQuietDay() => SvGameServer.FestivalClockDefaultForTest = QuietDay;
}
