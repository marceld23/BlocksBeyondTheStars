// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The festival day calendars (#3315) — the Advent calendar first (<c>data/festival_calendars.json</c>). The rules are pure
/// (<see cref="FestivalCalendarRules"/>); this part decides who sees which calendar, tells the clients and opens doors.
/// <list type="bullet">
/// <item><b>Who</b>: every player whose festival selection ("My festivals") includes the calendar's festival — nobody when
/// the festivals are off (the operator's <c>off</c>/<c>none</c>, or the world rule). A festival switched on by hand
/// (<c>/festival christmas</c>, the operator's key) shows its calendar to everybody with every door open to them.</item>
/// <item><b>When</b>: the player's <b>local</b> day — the server's festival clock plus the UTC offset the client sends with
/// the join and every door request. The festival tick (every 30 s) notices a new door or the end of the catch-up and
/// sends the state again; so do a rule change and a changed festival selection.</item>
/// <item><b>Doors</b>: <see cref="OpenCalendarDoorIntent"/> opens an available, unopened door — its seeded contents go into
/// the backpack (spilling into the hold aboard, like every reward), or the door stays shut with a "make room" notice. A
/// special door unlocks its cosmetic for this world (milestone <c>cosmetic:&lt;key&gt;</c>, <see cref="CosmeticUnlocks"/>).
/// Served while the singleplayer pause holds the world: the calendar opens from the in-game menu.</item>
/// <item><b>VEGA</b> greets each calendar once per player and season, when it first shows.</item>
/// </list>
/// Everything lives in the player's milestones, so it is per world and survives a restart.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The notice of a door whose contents do not fit — the door stays shut.</summary>
    internal const string CalendarBagFullNotice = "ui.calendar.bag_full";

    /// <summary>Whether festivals are on at all for the calendars: the operator's <c>off</c>/<c>none</c> and the world rule
    /// switch them off; an operator's festival key beats the world rule (as it does for the festivals themselves).</summary>
    private bool FestivalCalendarsSwitchedOn
    {
        get
        {
            string op = (_config.FestivalsOverride ?? string.Empty).Trim().ToLowerInvariant();
            if (op is "off" or "none")
            {
                return false;
            }

            return op.Length > 0 && op != "calendar" ? _content.FindFestival(op) is not null : Rules.Festivals;
        }
    }

    /// <summary>The join's festival selection (#3021) and local UTC offset (#3315) — before the join greets.</summary>
    private void ApplyJoinFestivalPrefs(PlayerSession session, JoinRequest join)
    {
        session.UtcOffsetMinutes = FestivalCalendarRules.ClampUtcOffset(join.UtcOffsetMinutes);
        ApplyFestivalPrefs(session, join.FestivalPreset, join.FestivalCustom, join.Locale);
    }

    /// <summary>The calendar for this player right now.</summary>
    private CalendarDay CalendarDayFor(PlayerSession session, FestivalCalendarDefinition calendar)
    {
        var local = FestivalCalendarRules.LocalDate(FestivalUtcNow, session.UtcOffsetMinutes);
        if (!FestivalCalendarsSwitchedOn || _content.FindFestival(calendar.Festival) is null)
        {
            return new CalendarDay(false, FestivalCalendarRules.SeasonOf(calendar, local), 0);
        }

        bool forced = FestivalsForced && FestivalActive(calendar.Festival);
        if (!forced && !FestivalSelectionOf(session).Contains(calendar.Festival))
        {
            return new CalendarDay(false, FestivalCalendarRules.SeasonOf(calendar, local), 0);
        }

        return FestivalCalendarRules.Evaluate(calendar, local, forced);
    }

    /// <summary>Join tail (with the festival greeting): every calendar that shows for this player, then their unlocked
    /// cosmetics.</summary>
    private void CalendarsOnJoin(PlayerSession session)
    {
        session.CalendarsShown.Clear();
        RefreshFestivalCalendars(session);
        SendCosmeticUnlocks(session);
    }

    /// <summary>Every joined player's calendars — after the festival tick and every festival rule change.</summary>
    private void RefreshFestivalCalendars()
    {
        if (_content.FestivalCalendars.Count == 0)
        {
            return;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined)
            {
                RefreshFestivalCalendars(s);
            }
        }
    }

    /// <summary>Sends a calendar's state when it newly shows or its day changed (a new door, a new season), and one last
    /// inactive state when it stops showing. VEGA greets a calendar the first time it shows in a season.</summary>
    private void RefreshFestivalCalendars(PlayerSession session)
    {
        foreach (var calendar in _content.FestivalCalendars)
        {
            var day = CalendarDayFor(session, calendar);
            bool shown = session.CalendarsShown.TryGetValue(calendar.Key, out var last);
            if (day.Active && (!shown || last != day))
            {
                SendCalendarState(session, calendar, day);
                GreetCalendarOnce(session, calendar, day);
            }
            else if (!day.Active && shown)
            {
                session.CalendarsShown.Remove(calendar.Key);
                Send(session, new FestivalCalendarState { Calendar = calendar.Key, Festival = calendar.Festival, Active = false, Season = last.Season, Doors = calendar.Doors });
            }
        }
    }

    private void GreetCalendarOnce(PlayerSession session, FestivalCalendarDefinition calendar, CalendarDay day)
    {
        var p = session.State;
        if (!p.Milestones.Add(FestivalCalendarRules.GreetedMilestone(calendar.Key, day.Season)))
        {
            return; // greeted this season already
        }

        PruneOlderCalendarSeasons(p, calendar.Key, day.Season);
        _repo.SavePlayer(p);
        SendVegaLine(session, calendar.VegaKey, FestivalGreetingKind);
    }

    /// <summary>Drops the milestones of this calendar's earlier seasons — a long-lived world never piles them up.</summary>
    private static void PruneOlderCalendarSeasons(Shared.State.PlayerState p, string key, int season)
        => p.Milestones.RemoveWhere(m => FestivalCalendarRules.IsOlderSeason(m, key, season));

    /// <summary>Sends one calendar's state (with the contents of every opened door) and remembers what was sent.</summary>
    private void SendCalendarState(PlayerSession session, FestivalCalendarDefinition calendar, CalendarDay day, int reveal = 0, string notice = "")
    {
        if (!day.Active)
        {
            session.CalendarsShown.Remove(calendar.Key);
            Send(session, new FestivalCalendarState { Calendar = calendar.Key, Festival = calendar.Festival, Active = false, Season = day.Season, Doors = calendar.Doors, Notice = notice });
            return;
        }

        session.CalendarsShown[calendar.Key] = day;
        Send(session, BuildCalendarState(session, calendar, day, reveal, notice));
    }

    private FestivalCalendarState BuildCalendarState(PlayerSession session, FestivalCalendarDefinition calendar, CalendarDay day, int reveal, string notice)
    {
        var p = session.State;
        int mask = FestivalCalendarRules.OpenedMask(calendar, day.Season, p.Milestones);
        var contents = new List<CalendarDoorContent>();
        for (int door = 1; door <= Math.Min(calendar.Doors, Shared.Content.GameContent.MaxCalendarDoors); door++)
        {
            if ((mask & (1 << (door - 1))) == 0)
            {
                continue;
            }

            var roll = FestivalCalendarRules.RollDoor(calendar, _meta.Seed, p.PlayerId, day.Season, door);
            contents.Add(new CalendarDoorContent
            {
                Door = door,
                Items = roll.Items.Select(i => i.Item).ToArray(),
                Counts = roll.Items.Select(i => i.Count).ToArray(),
                Cosmetic = roll.Cosmetic,
            });
        }

        return new FestivalCalendarState
        {
            Calendar = calendar.Key,
            Festival = calendar.Festival,
            Active = true,
            Season = day.Season,
            Doors = calendar.Doors,
            Available = day.Available,
            OpenedMask = mask,
            CatchUpUntil = FestivalCalendarRules.CatchUpUntil(calendar, day.Season),
            Contents = contents.ToArray(),
            Reveal = reveal,
            Notice = notice ?? string.Empty,
        };
    }

    // ---------------- Opening a door ----------------

    /// <summary><see cref="OpenCalendarDoorIntent"/>: opens an available, unopened door. Anything else (an unknown calendar is
    /// ignored) answers with the current state, so the client's view heals.</summary>
    private void HandleOpenCalendarDoor(PlayerSession session, OpenCalendarDoorIntent intent)
    {
        session.UtcOffsetMinutes = FestivalCalendarRules.ClampUtcOffset(intent.UtcOffsetMinutes);
        if (_content.FindFestivalCalendar(intent.Calendar) is not { } calendar)
        {
            return;
        }

        var day = CalendarDayFor(session, calendar);
        var p = session.State;
        if (!day.Active || intent.Door < 1 || intent.Door > day.Available
            || p.Milestones.Contains(FestivalCalendarRules.DoorMilestone(calendar.Key, day.Season, intent.Door)))
        {
            SendCalendarState(session, calendar, day);
            return;
        }

        var roll = FestivalCalendarRules.RollDoor(calendar, _meta.Seed, p.PlayerId, day.Season, intent.Door);
        var items = roll.Items.Where(i => _content.GetItem(i.Item) is not null).ToList();
        var pool = new MaterialPool(_content, p, ShipOf(session));
        if (items.Count > 0 && !pool.CanFit(items))
        {
            SendCalendarState(session, calendar, day, notice: CalendarBagFullNotice); // the door stays shut — nothing is lost
            return;
        }

        bool newCosmetic = GrantCalendarDoor(session, calendar, day, intent.Door, roll.Cosmetic, items, pool);
        SendInventory(session);
        if (newCosmetic)
        {
            SendCosmeticUnlocks(session);
        }

        SendCalendarState(session, calendar, day, reveal: intent.Door);
        _log.Info($"'{p.Name}' opened door {intent.Door} of the '{calendar.Key}' calendar ({day.Season}).");
    }

    /// <summary>Pays a door out and writes it down, in one transaction. True when it unlocked a cosmetic new to this player.</summary>
    private bool GrantCalendarDoor(PlayerSession session, FestivalCalendarDefinition calendar, CalendarDay day, int door,
        string cosmetic, List<ItemAmount> items, MaterialPool pool)
    {
        var p = session.State;
        bool newCosmetic = false;
        _repo.RunInTransaction(() =>
        {
            foreach (var item in items)
            {
                pool.Add(item.Item, item.Count);
            }

            PruneOlderCalendarSeasons(p, calendar.Key, day.Season);
            p.Milestones.Add(FestivalCalendarRules.DoorMilestone(calendar.Key, day.Season, door));
            newCosmetic = CosmeticKeys.IsCosmetic(cosmetic) && p.Milestones.Add(CosmeticKeys.MilestonePrefix + cosmetic);
            if (p.AboardShip)
            {
                SaveFleet(session); // aboard, a reward may spill into the hold — keep it with the opened door
            }

            _repo.SavePlayer(p);
        });
        return newCosmetic;
    }

    // ---------------- Cosmetics ----------------

    /// <summary>The cosmetics this player unlocked in this world (<c>hudframe:&lt;key&gt;</c>, <c>skin:&lt;key&gt;</c>).</summary>
    private static string[] CosmeticUnlocksOf(Shared.State.PlayerState p)
        => p.Milestones
            .Where(m => m.StartsWith(CosmeticKeys.MilestonePrefix, StringComparison.Ordinal))
            .Select(m => m.Substring(CosmeticKeys.MilestonePrefix.Length))
            .Where(CosmeticKeys.IsCosmetic)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    private void SendCosmeticUnlocks(PlayerSession session)
        => Send(session, new CosmeticUnlocks { Keys = CosmeticUnlocksOf(session.State) });

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the calendar <paramref name="key"/> as this player sees it now.</summary>
    public CalendarDay CalendarDayForTest(string playerId, string key)
        => FindSessionByPlayerId(playerId) is { } s && _content.FindFestivalCalendar(key) is { } c ? CalendarDayFor(s, c) : default;

    /// <summary>Test seam: opens a door as the intent would (with the player's UTC offset).</summary>
    public void OpenCalendarDoorForTest(string playerId, string key, int door, int utcOffsetMinutes = 0)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleOpenCalendarDoor(s, new OpenCalendarDoorIntent { Calendar = key, Door = door, UtcOffsetMinutes = utcOffsetMinutes });
        }
    }

    /// <summary>Test seam: the calendar part of a join for a local player (the network join runs it with the festival
    /// greeting); <paramref name="utcOffsetMinutes"/> as the join would carry it.</summary>
    public void CalendarsOnJoinForTest(string playerId, int utcOffsetMinutes = 0)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.UtcOffsetMinutes = FestivalCalendarRules.ClampUtcOffset(utcOffsetMinutes);
            CalendarsOnJoin(s);
        }
    }
}
