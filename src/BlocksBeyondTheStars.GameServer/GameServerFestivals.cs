// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Festivals of the real-world calendar (#3001, epic #3000). The server is the truth: it reads its bundled
/// <c>data/festivals.json</c> and its UTC wall clock (behind a test seam) every <see cref="FestivalCheckSeconds"/> of
/// uptime and keeps the list of festivals that are on, the primary one first. The list goes to every client in
/// <see cref="ServerRules.ActiveFestivals"/> at join and whenever it changes; the client paints the mood and the chip from it.
/// <para>Order of authority: the operator's <see cref="Shared.Configuration.ServerConfig.FestivalsOverride"/> (never saved —
/// the bundled singleplayer host passes the client's global setting this way), then the world rule
/// <see cref="Shared.Configuration.GameRules.Festivals"/>, then a manual pick <see cref="Shared.Configuration.GameRules.FestivalPick"/>
/// (<c>/festival &lt;key&gt;</c> — the no-FOMO rule: any festival at any time), then the calendar.</para>
/// <para>VEGA greets a festival once per player and festival-year (milestone <c>festival:&lt;key&gt;:&lt;year&gt;</c>), on the
/// system channel so the hints mute never swallows it; the greeting carries the festival's short explanation.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds of uptime between two looks at the calendar — a festival begins within half a minute of midnight UTC.</summary>
    private const double FestivalCheckSeconds = 30.0;

    private const byte FestivalGreetingKind = 3; // VEGA's system channel: never muted, logged in the tips journal

    private DateTime? _festivalClockForTest;
    private double _festivalNextCheck;
    private bool _festivalsEvaluated;
    private string[] _activeFestivals = Array.Empty<string>();

    /// <summary>The wall-clock UTC now the festivals read — a seam, like <c>WallUtcNow</c> (#2595), so a test can pin a date.</summary>
    private DateTime FestivalUtcNow => _festivalClockForTest ?? DateTime.UtcNow;

    /// <summary>The festivals on right now, the primary one first (evaluated on first use, then every 30 s and on every
    /// rule change).</summary>
    public IReadOnlyList<string> ActiveFestivals
    {
        get
        {
            if (!_festivalsEvaluated)
            {
                RefreshFestivals(announce: false);
            }

            return _activeFestivals;
        }
    }

    /// <summary>The primary festival's key, or empty.</summary>
    public string PrimaryFestival => ActiveFestivals.Count > 0 ? ActiveFestivals[0] : string.Empty;

    /// <summary>Whether this festival is on.</summary>
    public bool FestivalActive(string key) => ActiveFestivals.Contains(key, StringComparer.Ordinal);

    /// <summary>Whether a festival of this theme is on (the packs' <c>world:festival_&lt;theme&gt;</c> predicates).</summary>
    public bool FestivalThemeActive(string theme)
        => ActiveFestivals.Any(k => string.Equals(_content.FindFestival(k)?.Theme, theme, StringComparison.Ordinal));

    /// <summary>Test seam: pins the calendar's clock (null = the real clock) and re-evaluates at once.</summary>
    public void SetFestivalClockForTest(DateTime? utcNow)
    {
        _festivalClockForTest = utcNow;
        RefreshFestivals(announce: true);
    }

    /// <summary>The festivals that are on by the order of authority (operator override → world rule → manual pick → calendar).</summary>
    internal string[] ComputeActiveFestivals()
    {
        string op = (_config.FestivalsOverride ?? string.Empty).Trim().ToLowerInvariant();
        if (op is "off" or "none")
        {
            return Array.Empty<string>();
        }

        if (op.Length > 0 && op != "calendar")
        {
            return _content.FindFestival(op) is null ? Array.Empty<string>() : new[] { op };
        }

        if (!Rules.Festivals)
        {
            return Array.Empty<string>();
        }

        string pick = (Rules.FestivalPick ?? string.Empty).Trim();
        if (pick.Length > 0)
        {
            return _content.FindFestival(pick) is null ? Array.Empty<string>() : new[] { pick };
        }

        return FestivalCalendar.ActiveOn(_content.Festivals, FestivalUtcNow).Select(f => f.Key).ToArray();
    }

    /// <summary>Pre-world tick step: looks at the calendar every <see cref="FestivalCheckSeconds"/> of uptime.</summary>
    private void TickFestivals(double dt)
    {
        if (_festivalsEvaluated && _uptime < _festivalNextCheck)
        {
            return;
        }

        _festivalNextCheck = _uptime + FestivalCheckSeconds;
        RefreshFestivals(announce: true);
    }

    /// <summary>Re-evaluates the list; on a change every joined player gets the rules again and VEGA greets the festivals
    /// that just began. The very first evaluation announces nothing — nobody has joined a server that has not ticked, and a
    /// join greets on its own (<see cref="FestivalsOnJoin"/>).</summary>
    private void RefreshFestivals(bool announce)
    {
        bool first = !_festivalsEvaluated;
        _festivalsEvaluated = true;
        var now = ComputeActiveFestivals();
        if (now.SequenceEqual(_activeFestivals, StringComparer.Ordinal))
        {
            return;
        }

        var began = now.Where(k => !_activeFestivals.Contains(k, StringComparer.Ordinal)).ToArray();
        _activeFestivals = now;
        _log.Info($"Festivals: {(now.Length == 0 ? "none" : string.Join(", ", now))}"
                  + (string.IsNullOrEmpty(_config.FestivalsOverride) ? string.Empty : $" (operator override '{_config.FestivalsOverride}')")
                  + (Rules.FestivalPick.Length > 0 ? $" (picked '{Rules.FestivalPick}')" : string.Empty) + ".");
        if (!announce || first)
        {
            return;
        }

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            SendRules(s);
            foreach (var key in began)
            {
                FestivalGreet(s, key);
            }
        }
    }

    /// <summary>Join tail: VEGA greets every festival that is on, once per festival-year.</summary>
    private void FestivalsOnJoin(PlayerSession session)
    {
        foreach (var key in ActiveFestivals)
        {
            FestivalGreet(session, key);
        }
    }

    private void FestivalGreet(PlayerSession session, string key)
    {
        var festival = _content.FindFestival(key);
        if (festival is null)
        {
            return;
        }

        var p = session.State;
        string milestone = $"festival:{key}:{FestivalUtcNow.Year}";
        if (!p.Milestones.Add(milestone))
        {
            return; // greeted this year already
        }

        _repo.SavePlayer(p);
        SendVegaLine(session, festival.VegaKey, FestivalGreetingKind);
    }

    /// <summary>The live world-rules edit's festival part (world admin, <see cref="HandleSetWorldRules"/>): the switch and the
    /// manual pick. Unknown festival keys leave the pick as it was.</summary>
    private void ApplyFestivalRuleIntent(SetWorldRulesIntent intent)
    {
        bool changed = false;
        if (!string.IsNullOrEmpty(intent.Festivals))
        {
            bool on = intent.Festivals.Equals("On", StringComparison.OrdinalIgnoreCase);
            changed |= Rules.Festivals != on;
            Rules.Festivals = on;
        }

        if (!string.IsNullOrEmpty(intent.FestivalPick))
        {
            string pick = intent.FestivalPick.Trim().ToLowerInvariant();
            string next = pick is "calendar" or "none" ? string.Empty
                : _content.FindFestival(pick) is null ? Rules.FestivalPick : pick;
            changed |= !string.Equals(next, Rules.FestivalPick, StringComparison.Ordinal);
            Rules.FestivalPick = next;
        }

        if (changed)
        {
            RefreshFestivals(announce: true);
        }
    }

    /// <summary><c>/festival</c> (world admin): no argument reports the state; <c>calendar</c> hands back to the calendar;
    /// <c>on</c>/<c>off</c> is the world rule; a festival key switches that one on by hand. Persisted with the world's rules
    /// and announced to everyone online.</summary>
    private void AdminFestival(PlayerSession session, string? argument)
    {
        string arg = (argument ?? string.Empty).Trim().ToLowerInvariant();
        if (arg.Length == 0)
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.festival.current").Replace("{value}", FestivalStateWord(session.Locale)),
            });
            return;
        }

        switch (arg)
        {
            case "calendar":
                Rules.Festivals = true;
                Rules.FestivalPick = string.Empty;
                break;
            case "on":
                Rules.Festivals = true;
                break;
            case "off":
                Rules.Festivals = false;
                break;
            default:
                if (_content.FindFestival(arg) is null)
                {
                    Reject(session, "admin", "@srv.festival.unknown");
                    return;
                }

                Rules.Festivals = true;
                Rules.FestivalPick = arg;
                break;
        }

        _meta.RulesOverride = Rules.Clone(); // the world owns its rules — the next launch reads them from the save
        _repo.SaveMetadata(_meta);
        RefreshFestivals(announce: true);

        foreach (var s in _sessions.Values)
        {
            if (!s.Joined)
            {
                continue;
            }

            SendRules(s);
            Send(s, new ServerMessage
            {
                Text = Localize(s.Locale, "srv.festival.set")
                    .Replace("{player}", session.State.Name)
                    .Replace("{value}", FestivalStateWord(s.Locale)),
            });
        }
    }

    /// <summary>"Off", "none" or the localized names of the festivals that are on.</summary>
    private string FestivalStateWord(string locale)
    {
        if (!Rules.Festivals)
        {
            return Localize(locale, "ui.toggle.off");
        }

        var active = ActiveFestivals;
        return active.Count == 0
            ? Localize(locale, "srv.festival.none")
            : string.Join(", ", active.Select(k => Localize(locale, "festival." + k + ".name")));
    }
}
