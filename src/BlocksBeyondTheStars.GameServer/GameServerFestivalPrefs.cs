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
/// "My festivals" (#3021, epic #3019): each player celebrates their own selection of the festivals that are on — a preset
/// (the big world festivals + their language by default, all, their language, none) or their own list. The world's list
/// (<see cref="ActiveFestivals"/>: operator override → world rule → manual pick → calendar) stays the truth of what is on;
/// <see cref="ActiveFestivalsFor"/> is what one player sees of it: their <c>ServerRules.ActiveFestivals</c>, VEGA's greeting,
/// the challenge packs' <c>world:festival_*</c> predicates and the pumpkins around them. A festival switched on by hand
/// (<c>/festival &lt;key&gt;</c>, the operator's key) is forced: it shows for everybody — the no-FOMO promise.
/// <para>The selection arrives with the join (<c>JoinRequest.FestivalPreset</c>) and on every change
/// (<see cref="SetFestivalPrefsIntent"/>, served while the singleplayer pause holds the world — the settings live in the
/// pause menu). It is never saved: it is the client's setting, like VEGA's hints mute.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Most keys an own festival list may carry (it is client input).</summary>
    private const int FestivalCustomCap = 128;

    /// <summary>Whether the festivals that are on were switched on by hand (a world admin's pick or the operator's key) — then
    /// everybody celebrates them, whatever their selection.</summary>
    internal bool FestivalsForced
    {
        get
        {
            string op = (_config.FestivalsOverride ?? string.Empty).Trim().ToLowerInvariant();
            if (op.Length > 0 && op is not ("off" or "none" or "calendar"))
            {
                return true;
            }

            return op.Length == 0 && Rules.Festivals && !string.IsNullOrWhiteSpace(Rules.FestivalPick);
        }
    }

    /// <summary>The festivals this player celebrates of those that are on, the one that leads the chip and the mood first
    /// (#3022: priority, then the one that started last).</summary>
    public IReadOnlyList<string> ActiveFestivalsFor(PlayerSession session)
    {
        var active = ActiveFestivals;
        if (active.Count == 0)
        {
            return Array.Empty<string>();
        }

        var celebrated = FestivalSelectionOf(session);
        var visible = FestivalsForced ? active : active.Where(celebrated.Contains);
        return FestivalSelection.ByPriority(visible, _content.FindFestival);
    }

    /// <summary>Whether this player celebrates a festival of this theme that has no pack of its own (the theme packs'
    /// <c>world:festival_&lt;theme&gt;</c> predicates, #3023).</summary>
    private bool FestivalThemeActiveFor(PlayerSession session, string theme)
        => ActiveFestivalsFor(session).Any(k => _content.FindFestival(k) is { } f
            && string.Equals(f.Theme, theme, StringComparison.Ordinal) && string.IsNullOrEmpty(f.Pack));

    /// <summary>The player's resolved selection — the default (big world festivals + the session's language) until one arrived.</summary>
    private HashSet<string> FestivalSelectionOf(PlayerSession session)
        => session.CelebratedFestivals ??= FestivalSelection.Resolve(_content.Festivals, FestivalPreset.BigAndLanguage, session.Locale, null);

    /// <summary>Takes a selection (join or intent): −1 / an unknown preset is the default; the own list is bounded.</summary>
    private void ApplyFestivalPrefs(PlayerSession session, int preset, string[]? custom, string? locale)
    {
        var own = (custom ?? Array.Empty<string>()).Where(k => !string.IsNullOrEmpty(k) && k.Length <= 48).Take(FestivalCustomCap);
        string language = string.IsNullOrWhiteSpace(locale) ? session.Locale : locale!;
        session.CelebratedFestivals = FestivalSelection.Resolve(_content.Festivals, FestivalSelection.Parse(preset), language, own);
    }

    /// <summary><see cref="SetFestivalPrefsIntent"/>: a changed selection takes effect at once — the rules go out again (chip,
    /// mood) and VEGA greets a festival that is on and newly celebrated (once per festival-year, as always).</summary>
    private void HandleSetFestivalPrefs(PlayerSession session, SetFestivalPrefsIntent intent)
    {
        var before = ActiveFestivalsFor(session);
        ApplyFestivalPrefs(session, intent.Preset, intent.Custom, intent.Locale);
        var now = ActiveFestivalsFor(session);
        if (before.SequenceEqual(now, StringComparer.Ordinal))
        {
            return;
        }

        SendRules(session);
        foreach (var key in now.Where(k => !before.Contains(k, StringComparer.Ordinal)))
        {
            FestivalGreet(session, key);
        }
    }

    /// <summary>Test seam: the selection as an intent would set it.</summary>
    public void SetFestivalPrefsForTest(string playerId, int preset, string[]? custom = null, string? locale = null)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleSetFestivalPrefs(s, new SetFestivalPrefsIntent { Preset = preset, Custom = custom ?? Array.Empty<string>(), Locale = locale ?? string.Empty });
        }
    }

    /// <summary>Test seam: what this player sees of the festivals that are on.</summary>
    public IReadOnlyList<string> ActiveFestivalsForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? ActiveFestivalsFor(s) : Array.Empty<string>();

    /// <summary>Test seam: a <c>world:</c> story predicate as this player's packs see it (<c>festival_halloween</c> …).</summary>
    public bool WorldPredicateForTest(string playerId, string predicate)
        => FindSessionByPlayerId(playerId) is { } s && WorldPredicates.TryGetValue(predicate, out var test) && test(s);
}
