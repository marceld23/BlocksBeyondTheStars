// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The hidden star map (#3997, <see cref="WorldDescription.HiddenStarMap"/>; docs/developer/HIDDEN_STAR_MAP.md). The whole
/// galaxy exists from the start, but a rolled system is on a player's star map — and a jump or travel target — only once it
/// is charted for them:
/// <list type="bullet">
/// <item>entering a system for the first time charts 1–4 of its nearest systems (<see cref="StarChartRules.NeighbourCount"/>);</item>
/// <item>a jump into another system with a <c>radar_array</c> fitted charts <see cref="StarChartRules.RadarBonus"/> more;</item>
/// <item>scanning a wormhole charts its far end; a place the story, an NPC or a mission names charts its system;</item>
/// <item>allies (an alliance or the same crew) see each other's charted and entered systems while they are allied;</item>
/// <item>a system with a finished relay is visible to everyone; observers and admins in admin mode see everything;</item>
/// <item>a player with nothing visible left to enter while systems are still hidden gets the nearest
/// <see cref="StarChartRules.RescueCount"/> (never stuck).</item>
/// </list>
/// Hand-made, pack and story systems keep their own rules (<see cref="SystemHiddenFor"/>); Sol joins a hidden-map world by
/// its <c>afterSystems</c> rule (<see cref="RevealFixedSystemsBySystems"/>). A world without the flag is untouched.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The locale key of the "new systems on your map" line ({count}).</summary>
    internal const string ChartFoundKey = "srv.chart.found";

    /// <summary>The locale key of the line a hand-made system revealed by the systems a player entered speaks ({name}).</summary>
    internal const string ChartFarSystemKey = "srv.chart.far_system";

    /// <summary>Offline players' charts, read once from the save (an ally's systems count while that ally is away). An entry
    /// goes the moment its player joins — their live state is read then — so the next read after they leave is fresh.</summary>
    private readonly Dictionary<string, (HashSet<string> Known, HashSet<string> Charted)> _offlineCharts = new(StringComparer.Ordinal);

    /// <summary>Whether this world's star map is charted per player.</summary>
    private bool HiddenStarMap => _meta.Description.HiddenStarMap;

    /// <summary>Whether a rolled system is kept off <paramref name="s"/>'s star map and out of their jumps and travel. Always
    /// false in a world without the hidden map, for every non-rolled system (those have their own rules), for observers and
    /// admins in admin mode, for a relay system, the player's current system and anything they or an ally entered or charted.</summary>
    internal bool ChartHiddenFor(PlayerSession s, string? systemId)
    {
        if (!HiddenStarMap || !StarSystemIds.IsRolled(systemId) || SeesWholeChart(s) || _relaySystems.Contains(systemId!))
        {
            return false;
        }

        return !ChartedForSelfOrAlly(s, systemId!) && !string.Equals(CurrentSystemOf(s), systemId, StringComparison.Ordinal);
    }

    /// <summary>Observers and admins in admin mode read the whole chart.</summary>
    private static bool SeesWholeChart(PlayerSession s) => s.Spectating || s.Admin.Mode != AdminModes.Off;

    /// <summary>Whether the player or one of their allies entered or charted the system.</summary>
    private bool ChartedForSelfOrAlly(PlayerSession s, string systemId)
    {
        if (s.State.KnownSystems.Contains(systemId) || s.State.ChartedSystems.Contains(systemId))
        {
            return true;
        }

        foreach (string ally in StarChartAllyIds(s.State.PlayerId))
        {
            var (known, charted) = ChartOf(ally);
            if (known.Contains(systemId) || charted.Contains(systemId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The star system the player is in right now (a station's host system), or null.</summary>
    private string? CurrentSystemOf(PlayerSession s) => ResolveLocationHostBody(s.CurrentLocationId)?.SystemId;

    /// <summary>The players whose chart <paramref name="playerId"/> shares: manual alliance partners and crew mates.</summary>
    private IEnumerable<string> StarChartAllyIds(string playerId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (_allies.TryGetValue(playerId, out var partners))
        {
            foreach (string id in partners)
            {
                if (seen.Add(id))
                {
                    yield return id;
                }
            }
        }

        if (CrewOf(playerId) is { } crew)
        {
            foreach (string id in crew.Members.Keys)
            {
                if (id != playerId && seen.Add(id))
                {
                    yield return id;
                }
            }
        }
    }

    /// <summary>The entered and charted systems of a player: the live state while they are online, else their save (cached).</summary>
    private (HashSet<string> Known, HashSet<string> Charted) ChartOf(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } online)
        {
            _offlineCharts.Remove(playerId);
            return (online.State.KnownSystems, online.State.ChartedSystems);
        }

        if (!_offlineCharts.TryGetValue(playerId, out var chart))
        {
            var saved = _repo.LoadPlayer(playerId);
            chart = (new HashSet<string>(saved?.KnownSystems ?? new HashSet<string>(), StringComparer.Ordinal),
                new HashSet<string>(saved?.ChartedSystems ?? new HashSet<string>(), StringComparer.Ordinal));
            _offlineCharts[playerId] = chart;
        }

        return chart;
    }

    /// <summary>The systems the player's star map reveals the worlds and names of: their own entered ones and, on a hidden
    /// map, their allies' (<see cref="StarMapData.KnownSystemIds"/>).</summary>
    private HashSet<string> StarMapKnownSystems(PlayerSession s)
    {
        var known = new HashSet<string>(s.State.KnownSystems, StringComparer.Ordinal);
        if (HiddenStarMap)
        {
            foreach (string ally in StarChartAllyIds(s.State.PlayerId))
            {
                known.UnionWith(ChartOf(ally).Known);
            }
        }

        return known;
    }

    /// <summary>A player joins: their save is the truth from now on (drop a stale offline copy), then the never-stuck check.</summary>
    private void OnStarChartJoin(PlayerSession s)
    {
        _offlineCharts.Remove(s.State.PlayerId);
        if (HiddenStarMap && !SeesWholeChart(s) && RescueIfStuck(s, CurrentSystemOf(s)) is > 0 and var found)
        {
            AnnounceCharted(s, found, speak: true);
        }
    }

    /// <summary>A player arrived in <paramref name="systemId"/> (a landing, a respawn, a hyperjump, a wormhole). A first entry
    /// charts its neighbours, a jump with a radar array charts more, the never-stuck check runs, and Sol may join.</summary>
    private void ChartOnArrival(PlayerSession s, string? systemId, bool firstEntry, bool jumped)
    {
        if (!HiddenStarMap || string.IsNullOrEmpty(systemId) || _galaxy is null || SeesWholeChart(s))
        {
            return;
        }

        int found = 0;
        if (firstEntry)
        {
            found += ChartAround(s, systemId!, StarChartRules.NeighbourCount(_meta.Seed, systemId!, IsStartSystem(systemId)));
        }

        if (jumped && ShipOf(s).HasModule("radar_array"))
        {
            found += ChartAround(s, systemId!, StarChartRules.RadarBonus);
        }

        found += RescueIfStuck(s, systemId);
        bool farSystem = RevealFixedSystemsBySystems(s);
        if (found > 0 || farSystem)
        {
            // The spawn system maps nothing new (like OnSystemMapped's story milestone): no line on the first join.
            AnnounceCharted(s, found, speak: s.State.KnownSystems.Count > 1);
        }
    }

    /// <summary>Charts up to <paramref name="count"/> of the rolled systems nearest to <paramref name="fromSystemId"/> that the
    /// player has neither entered nor charted THEMSELVES (an ally's chart does not count, so the player keeps a frontier of
    /// their own when an alliance ends). Returns how many were charted.</summary>
    private int ChartAround(PlayerSession s, string fromSystemId, int count)
    {
        var from = _galaxy?.Systems.FirstOrDefault(x => x.Id == fromSystemId);
        if (from is null || count <= 0)
        {
            return 0;
        }

        var p = s.State;
        var pick = StarChartRules.Nearest(
            _galaxy!.Systems.Where(x => x.IsRolled && x.Id != from.Id && !p.KnownSystems.Contains(x.Id) && !p.ChartedSystems.Contains(x.Id)),
            from.MapX, from.MapY, count);
        foreach (var sys in pick)
        {
            p.ChartedSystems.Add(sys.Id);
        }

        return pick.Count;
    }

    /// <summary>Never stuck: when no visible system is left that the player has not entered, while some are still hidden, the
    /// nearest <see cref="StarChartRules.RescueCount"/> to <paramref name="nearSystemId"/> (else home) are charted.</summary>
    private int RescueIfStuck(PlayerSession s, string? nearSystemId)
    {
        if (_galaxy is null)
        {
            return 0;
        }

        string? here = CurrentSystemOf(s);
        bool anyHidden = false;
        foreach (var sys in _galaxy.Systems)
        {
            if (!sys.IsRolled)
            {
                continue;
            }

            if (ChartHiddenFor(s, sys.Id))
            {
                anyHidden = true;
            }
            else if (!s.State.KnownSystems.Contains(sys.Id) && !string.Equals(sys.Id, here, StringComparison.Ordinal))
            {
                return 0; // something visible is still waiting to be entered
            }
        }

        string? from = nearSystemId ?? here ?? StartSystemId;
        return anyHidden && from is not null ? ChartAround(s, from, StarChartRules.RescueCount) : 0;
    }

    /// <summary>Charts the system of a place that was just named to the player (a story lead or marker, an NPC hint, a mission
    /// target) — the story may never point into a system the player cannot jump to. Tells them when it was new.</summary>
    private void ChartNamedPlace(PlayerSession s, string? bodyOrSystemId)
    {
        if (!HiddenStarMap || string.IsNullOrEmpty(bodyOrSystemId) || _galaxy is null || SeesWholeChart(s))
        {
            return;
        }

        string? systemId = _galaxy.FindBody(bodyOrSystemId!)?.SystemId ?? bodyOrSystemId;
        if (StarSystemIds.IsRolled(systemId) && ChartHiddenFor(s, systemId) && s.State.ChartedSystems.Add(systemId!))
        {
            AnnounceCharted(s, 1, speak: true);
        }
    }

    /// <summary>Tells the player how many systems just appeared on their map (with <paramref name="speak"/>, nothing for 0)
    /// and re-sends the star map to them and their online allies, whose shared chart grew with it.</summary>
    private void AnnounceCharted(PlayerSession s, int found, bool speak)
    {
        if (speak && found > 0)
        {
            Send(s, new ServerMessage { Text = Localize(s.Locale, ChartFoundKey).Replace("{count}", found.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
        }

        if (!s.Joined)
        {
            return;
        }

        SendStarMap(s);
        foreach (string ally in StarChartAllyIds(s.State.PlayerId))
        {
            if (FindSessionByPlayerId(ally) is { Joined: true } other)
            {
                SendStarMap(other);
            }
        }
    }

    /// <summary>An alliance or crew changed: both sides' shared charts changed, so their star maps go out again.</summary>
    private void RefreshSharedCharts(params string[] playerIds)
    {
        if (!HiddenStarMap)
        {
            return;
        }

        foreach (string id in playerIds)
        {
            if (FindSessionByPlayerId(id) is { Joined: true } s)
            {
                SendStarMap(s);
            }
        }
    }

    /// <summary>A mission was taken: a player-posted travel target's system is charted; an "unlinked system" survey gets one
    /// visible unlinked system if none is (the offer counts the whole galaxy).</summary>
    private void ChartMissionTargets(PlayerSession s, MissionDefinition def)
    {
        if (!HiddenStarMap || _galaxy is null)
        {
            return;
        }

        foreach (var o in def.Objectives.Where(o => o.Type == MissionObjectiveType.Travel))
        {
            if (o.Target == MissionChains.TravelUnlinkedSystem)
            {
                ChartNearestUnlinked(s);
            }
            else if (o.Target != MissionChains.TravelOtherBody)
            {
                var body = _galaxy.FindBody(o.Target) ?? _galaxy.AllBodies().FirstOrDefault(b => string.Equals(b.Name, o.Target, StringComparison.OrdinalIgnoreCase));
                ChartNamedPlace(s, body?.Id);
            }
        }
    }

    /// <summary>The "survey an unlinked system" step needs a visible system without a relay that the player has not entered;
    /// when there is none, the nearest hidden one is charted.</summary>
    private void ChartNearestUnlinked(PlayerSession s)
    {
        var open = _galaxy!.Systems.Where(x => x.IsRolled && !_relaySystems.Contains(x.Id) && !s.State.KnownSystems.Contains(x.Id)).ToList();
        if (open.Count == 0 || open.Any(x => !ChartHiddenFor(s, x.Id)))
        {
            return;
        }

        var here = _galaxy.Systems.FirstOrDefault(x => x.Id == (CurrentSystemOf(s) ?? StartSystemId));
        if (here is not null && StarChartRules.Nearest(open, here.MapX, here.MapY, 1).FirstOrDefault() is { } target)
        {
            ChartNamedPlace(s, target.Id);
        }
    }

    /// <summary>How many rolled systems a player has entered (Sol's <c>afterSystems</c> rule).</summary>
    private static int RolledSystemsEntered(IEnumerable<string> known) => known.Count(StarSystemIds.IsRolled);

    /// <summary>Whether the player or an ally has entered at least <paramref name="needed"/> rolled systems.</summary>
    private bool EnteredEnoughSystems(PlayerSession s, int needed)
    {
        if (RolledSystemsEntered(s.State.KnownSystems) >= needed)
        {
            return true;
        }

        return StarChartAllyIds(s.State.PlayerId).Any(ally => RolledSystemsEntered(ChartOf(ally).Known) >= needed);
    }

    /// <summary>Whether a hand-made system with an <c>afterSystems</c> rule is hidden from the player on a hidden map: until
    /// they (or an ally) entered that many rolled systems, unless they see the whole chart, are in it or charted it.</summary>
    private bool FixedSystemHiddenBySystems(PlayerSession s, FixedSystemDefinition def)
    {
        if (!HiddenStarMap || def.Appear.AfterSystems <= 0 || SeesWholeChart(s))
        {
            return false;
        }

        return !EnteredEnoughSystems(s, def.Appear.AfterSystems) && !ChartedForSelfOrAlly(s, def.Id)
               && !string.Equals(CurrentSystemOf(s), def.Id, StringComparison.Ordinal);
    }

    /// <summary>On a hidden map a data system with an <c>afterSystems</c> rule joins the galaxy the first time a player has
    /// entered that many rolled systems (they or an ally); each player who gets there is told once — the system's id goes
    /// into their <see cref="Shared.State.PlayerState.ChartedSystems"/> as the "told" mark. Returns whether one was new.</summary>
    private bool RevealFixedSystemsBySystems(PlayerSession s)
    {
        bool any = false;
        foreach (var def in DataFixedSystems())
        {
            if (def.Appear.AfterSystems <= 0 || s.State.ChartedSystems.Contains(def.Id) || !EnteredEnoughSystems(s, def.Appear.AfterSystems))
            {
                continue;
            }

            s.State.ChartedSystems.Add(def.Id);
            RevealFixedSystem(def, broadcast: false);
            Send(s, new ServerMessage { Text = Localize(s.Locale, ChartFarSystemKey).Replace("{name}", FixedSystemName(def, s.Locale)) });
            any = true;
        }

        return any;
    }

    // ---- test seams ----

    internal bool ChartHiddenForTest(PlayerSession s, string systemId) => ChartHiddenFor(s, systemId);

    internal void ChartNamedPlaceForTest(PlayerSession s, string bodyOrSystemId) => ChartNamedPlace(s, bodyOrSystemId);

    internal void DropOfflineChartsForTest() => _offlineCharts.Clear();

    /// <summary>Test seam: the player warps into a system (as a hyperjump arrival books it) without moving their flight.</summary>
    internal void EnterSystemForTest(PlayerSession s, string systemId) => MarkSystemKnown(s, systemId);
}
