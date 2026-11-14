// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A giant ship wreck in the living world (#3611) — see GameServerGiantWrecks.cs. The landmark's world parts carry it
/// (GameServerLandmarkWorld.cs): its far silhouette and its sound emitters ride in the client's <see cref="LandmarkList"/>
/// (<see cref="NetLandmark.Kind"/> = <c>giant_wreck</c>), the streaming lift lifts its columns, its chests are the landmark's.
/// What is its own:
/// <list type="bullet">
/// <item><b>The map.</b> On everyone's map (type <see cref="GiantShipRules.MapType"/>, the ship's name) once a player on foot
/// comes within <see cref="GiantWreckRevealRange"/> blocks of its footprint.</item>
/// <item><b>VEGA</b> speaks once per player, at the first giant wreck that player walks up to
/// (<see cref="GiantWreckVegaLine"/>, the ship's name as its argument) — an advisor hint: the hints switch keeps it for later.</item>
/// <item><b>The scanner.</b> Any of its blocks reads as the wreck (<see cref="GiantWreckScanInfo"/> and the ship's name), worth a
/// relic's knowledge once per body and ship (ledger <c>giantwreck:&lt;body&gt;:&lt;key&gt;</c> — the Codex groups it under its
/// own heading, and its line is <c>giantship.&lt;key&gt;.desc</c>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How near (blocks, to the footprint) a player on foot must come for a giant wreck to go on the map.</summary>
    private const int GiantWreckRevealRange = 100;

    /// <summary>How near VEGA speaks about it.</summary>
    private const int GiantWreckHintRange = 64;

    /// <summary>The player's once-flag of VEGA's first-wreck line (not under <c>vega:hint:</c>: the tips log does not list it).</summary>
    internal const string GiantWreckVegaMilestone = "vega:giantwreck:first";

    /// <summary>VEGA's line at the first giant wreck, <c>{0}</c> = the ship's name.</summary>
    internal const string GiantWreckVegaLine = "vega.giantwreck.first";

    /// <summary>The scan readout's info line of a giant wreck.</summary>
    internal const string GiantWreckScanInfo = "ui.scan.giant_wreck";

    /// <summary>The scan readout's kind (and the first part of its ledger key's Codex group).</summary>
    internal const string GiantWreckScanKind = GiantShipRules.MapType;

    /// <summary>The first-scan ledger key of a giant wreck: once per body and ship.</summary>
    internal static string GiantWreckLedgerKey(string location, string key) => "giantwreck:" + location + ":" + key;

    /// <summary>The <c>RevealedPois</c> key of a giant wreck (per body and ship).</summary>
    private string GiantWreckRevealKey(LandmarkInstance wreck) => _world.LocationId + "|" + GiantShipRules.MapType + ":" + wreck.Key;

    /// <summary>The 1 Hz reveal (from <see cref="TickLandmarks"/>): a wreck goes on the map when a player on foot comes near.</summary>
    private void RevealGiantWrecks(List<PlayerSession> players)
    {
        foreach (var wreck in _giantWrecks)
        {
            if (!_meta.RevealedPois.Contains(wreck.RevealKey) && players.Any(s => LandmarkGap(wreck, s.State.Position) <= GiantWreckRevealRange))
            {
                RevealPoi(wreck.RevealKey);
            }
        }
    }

    /// <summary>Appends the giant wrecks somebody found to the POI list.</summary>
    private void AppendGiantWreckPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var wreck in _giantWrecks)
        {
            if (_meta.RevealedPois.Contains(wreck.RevealKey))
            {
                var (cx, cz, _, _) = wreck.Rect;
                pois.Add(new NetPoi { Type = GiantShipRules.MapType, Name = LandmarkName(wreck, session.Locale), X = cx, Z = cz });
            }
        }
    }

    /// <summary>VEGA's line at the first giant wreck a player walks up to (from the 1 Hz advisor pass, never aboard): once per
    /// player, with the ship's name. Muted hints and a basic training that holds hints keep it for the next wreck.</summary>
    private void TickGiantWreckHint(PlayerSession session)
    {
        var p = session.State;
        if (_giantWrecks.Count == 0 || p.AboardShip || p.Milestones.Contains(GiantWreckVegaMilestone))
        {
            return;
        }

        var wreck = _giantWrecks.FirstOrDefault(w => LandmarkGap(w, p.Position) <= GiantWreckHintRange);
        if (wreck is null || HintMutedFor(session, 1) || TutorialHoldsHint(session, "giantwreck_first"))
        {
            return;
        }

        p.Milestones.Add(GiantWreckVegaMilestone);
        _repo.SavePlayer(p);
        SendVegaLine(session, GiantWreckVegaLine, 1, LandmarkName(wreck, session.Locale));
    }

    /// <summary>The giant wreck a scan of <paramref name="cell"/> reads: the one whose box holds the aimed cell, when that cell
    /// is one of its blocks.</summary>
    private LandmarkInstance? GiantWreckForScan(PlayerSession session, Vector3i? cell)
        => _giantWrecks.Count == 0 || session.CurrentLocationId != _worlds.Active.LocationId || cell is not { } aimed
            ? null
            : GiantWreckAt(aimed) is { } wreck && wreck.Structure.Get(LocalX(wreck, aimed), aimed.Y - wreck.Origin.Y, aimed.Z - wreck.Origin.Z) != 0
                ? wreck
                : null;

    /// <summary>The scan readout of a giant wreck (filled into the scanner's readout by <c>ScanSubject</c>): kind, subject,
    /// the localized ship name, the info line, the ledger key.</summary>
    private (string Kind, string SubjectKey, string Display, string InfoKey, string LedgerKey) GiantWreckScan(LandmarkInstance wreck, string locale)
        => (GiantWreckScanKind, "giantwreck_" + wreck.Key, LandmarkName(wreck, locale), GiantWreckScanInfo,
            GiantWreckLedgerKey(_world.LocationId, wreck.Key));
}
