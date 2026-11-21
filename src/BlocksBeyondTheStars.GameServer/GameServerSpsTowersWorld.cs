// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A guide tower in the living world (#3893) — see GameServerSpsTowerStamp.cs for the stamp. It shares what a landmark is
/// (GameServerLandmarkWorld.cs): its far silhouette rides in the client's <see cref="LandmarkList"/>
/// (<see cref="NetLandmark.Kind"/> = <c>sps_tower</c>; an older client draws it like a landmark), the streaming lift lifts its
/// columns, its chests are the landmark's. What is its own:
/// <list type="bullet">
/// <item><b>The map.</b> On everyone's map (type <see cref="SpsTowerRules.MapType"/>, the ruin's name) once a player on foot
/// comes within <see cref="SpsTowerRules.RevealRange"/> blocks of its footprint.</item>
/// <item><b>Found.</b> The game event <see cref="GameEventKeys.SpsTowerFound"/> (argument: the tower's site id) when a player
/// comes within <see cref="SpsTowerRules.FoundRange"/> blocks — once per approach, measured only while a story step listens.
/// The story predicates <c>world:sps_towers</c> and <c>world:sps_tower_near</c> ask the same data.</item>
/// <item><b>The scanner.</b> Any of its blocks reads as the tower (kind <see cref="SpsTowerScanKind"/>, info
/// <see cref="SpsTowerScanInfo"/>, the ruin's name), worth a relic's knowledge once per body and ruin. The ledger is a
/// landmark's (<c>landmark:&lt;body&gt;:&lt;template&gt;</c>), so the Codex lists a tower among the landmarks with its
/// <c>landmark.&lt;template&gt;.desc</c> text — no client change needed for that.</item>
/// </list>
/// VEGA's own lines about a tower are the tower's life's (GameServerSpsTowerLife*.cs).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The scan readout's kind of a guide tower.</summary>
    internal const string SpsTowerScanKind = SpsTowerRules.MapType;

    /// <summary>The scan readout's info line of a guide tower.</summary>
    internal const string SpsTowerScanInfo = "ui.scan.sps_tower";

    /// <summary>Per player: the tower whose found range they stand in right now (the edge of <c>sps_tower:found</c>).</summary>
    private readonly Dictionary<string, string> _spsTowerNearBy = new(StringComparer.Ordinal);

    /// <summary>The <c>RevealedPois</c> key of a guide tower (per body and record index).</summary>
    private string SpsTowerRevealKey(int index) => SpsTowerRevealKey(_world.LocationId, index);

    private static string SpsTowerRevealKey(string locationId, int index)
        => locationId + "|" + SpsTowerRules.MapType + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The first-scan ledger key of a guide tower: the landmark ledger, once per body and ruin.</summary>
    internal static string SpsTowerLedgerKey(string location, string template) => "landmark:" + location + ":" + template;

    /// <summary>The 1 Hz reveal (from <see cref="TickLandmarks"/>): a tower goes on the map when a player on foot comes near.</summary>
    private void RevealSpsTowers(List<PlayerSession> players)
    {
        foreach (var tower in _spsTowers)
        {
            if (!_meta.RevealedPois.Contains(tower.RevealKey) && players.Any(s => LandmarkGap(tower, s.State.Position) <= SpsTowerRules.RevealRange))
            {
                RevealPoi(tower.RevealKey);
            }
        }
    }

    /// <summary>Appends the guide towers somebody found to the POI list.</summary>
    private void AppendSpsTowerPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var tower in _spsTowers)
        {
            if (_meta.RevealedPois.Contains(tower.RevealKey))
            {
                var (cx, cz, _, _) = tower.Rect;
                pois.Add(new NetPoi { Type = SpsTowerRules.MapType, Name = LandmarkName(tower, session.Locale), X = cx, Z = cz });
            }
        }
    }

    /// <summary>Puts a tower on everyone's map now (the tower's life reveals the others from a lit tower's console). Returns
    /// false when it was on the map already. The tower need not stand on a loaded world.</summary>
    internal bool RevealSpsTower(SpsTowerRecord tower)
    {
        string key = SpsTowerRevealKey(tower.LocationId, tower.Index);
        if (_meta.RevealedPois.Contains(key))
        {
            return false;
        }

        RevealPoi(key);
        return true;
    }

    /// <summary>Whether a tower is on the map (found by a player or revealed).</summary>
    internal bool SpsTowerRevealed(SpsTowerRecord tower) => _meta.RevealedPois.Contains(SpsTowerRevealKey(tower.LocationId, tower.Index));

    // ---------------------------------------------------------------- found

    /// <summary>1 Hz per player from the story pass: raises <see cref="GameEventKeys.SpsTowerFound"/> when the player comes
    /// within the found range of a tower (the nearest one changes from none or another) — measured only while a step listens,
    /// so it costs nothing otherwise.</summary>
    private void TickSpsTowerFound(PlayerSession s)
    {
        string playerId = s.State.PlayerId;
        if (!HasGameEventListener(GameEventKeys.SpsTowerFound))
        {
            _spsTowerNearBy.Remove(playerId);
            return;
        }

        string? site = SpsTowerInFoundRange(s)?.SiteId;
        if (site is null)
        {
            _spsTowerNearBy.Remove(playerId);
            return;
        }

        if (_spsTowerNearBy.TryGetValue(playerId, out string? before) && before == site)
        {
            return;
        }

        _spsTowerNearBy[playerId] = site;
        RaiseGameEvent(s, GameEventKeys.SpsTowerFound, site);
    }

    /// <summary>Test seam: one found pass for a player right now.</summary>
    internal void TickSpsTowerFoundForTest(PlayerSession s) => TickSpsTowerFound(s);

    /// <summary>The nearest guide tower within <see cref="SpsTowerRules.FoundRange"/> of the player's footprint gap on their own
    /// body, or null — none there, or the player is in flight.</summary>
    private SpsTowerRecord? SpsTowerInFoundRange(PlayerSession s)
    {
        if (_meta.SpsTowers.Count == 0 || InSpace(s.State.PlayerId))
        {
            return null;
        }

        var nearest = NearestSpsTower(s.State.Position, s.CurrentLocationId, out float gap);
        return nearest is not null && gap <= SpsTowerRules.FoundRange ? nearest : null;
    }

    // ---------------------------------------------------------------- the scanner

    /// <summary>The guide tower a scan of <paramref name="cell"/> reads: the one whose box holds the aimed cell, when that cell
    /// is one of its blocks.</summary>
    private LandmarkInstance? SpsTowerForScan(PlayerSession session, Vector3i? cell)
        => _spsTowers.Count == 0 || session.CurrentLocationId != _worlds.Active.LocationId || cell is not { } aimed
            ? null
            : SpsTowerAt(aimed) is { } tower && tower.Structure.Get(LocalX(tower, aimed), aimed.Y - tower.Origin.Y, aimed.Z - tower.Origin.Z) != 0
                ? tower
                : null;

    /// <summary>The scan readout of a guide tower (filled into the scanner's readout by <c>ScanSubject</c>): kind, subject, the
    /// localized name of the ruin, the info line, the ledger key.</summary>
    private (string Kind, string SubjectKey, string Display, string InfoKey, string LedgerKey) SpsTowerScan(LandmarkInstance tower, string locale)
        => (SpsTowerScanKind, "landmark_" + tower.Key, LandmarkName(tower, locale), SpsTowerScanInfo,
            SpsTowerLedgerKey(_world.LocationId, tower.Key));
}
