// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Scanning &amp; research (World systems / progression). The handheld scanner identifies a creature,
/// flora or material and reports a <b>threat assessment</b>; the ship scanner reveals an asteroid's
/// resources. Scanning something <b>for the first time</b> grants <b>knowledge points</b>
/// (<see cref="Shared.State.PlayerState.KnowledgePoints"/>) — a research currency blueprints also
/// require. Server-authoritative over the first-scan ledger, the knowledge balance and any hidden
/// info (asteroid contents).
/// </summary>
public sealed partial class GameServer
{
    private const int KnowledgeCreatureHostile = 5;
    private const int KnowledgeCreature = 3;
    private const int KnowledgeBlock = 1;
    private const int KnowledgeAsteroid = 4;
    private const int KnowledgeMonument = 8; // the richest find: a whole culture's writing, not one material

    /// <summary>Block keys whose inscriptions the scanner can read. Scanning one AT a monument identifies the
    /// relic (worth <see cref="KnowledgeMonument"/>); scanning one the player mined and carried home is just
    /// an ordinary material scan.</summary>
    private static readonly string[] RuneBlocks = { "rune_stone" };

    /// <summary>Handheld scan of a creature species ("creature") or a block/flora/material ("block").</summary>
    public ScanResult ScanSubject(string playerId, string subjectType, string subjectKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return Rejected(subjectKey, "ui.scan.no_scanner", "No scanner.");
        }

        var readout = new ScanReadout { Kind = subjectType, SubjectKey = subjectKey, Display = subjectKey };
        int value;
        // First-scan ledger key. Defaults to the species/block key; trees override it to a shared key so the
        // trunk and the leaves count as one discovery.
        string ledgerKey = $"{subjectType}:{subjectKey}";

        if (subjectType == "creature" && _speciesById.TryGetValue(subjectKey, out var sp))
        {
            readout.TraitKeys = new[]
            {
                "ui.scan.habitat." + sp.Habitat.ToString().ToLowerInvariant(),
                "ui.scan.activity." + sp.Activity.ToString().ToLowerInvariant(),
                "ui.scan.temperament." + sp.Temperament.ToString().ToLowerInvariant(),
            };
            readout.ThreatKey = sp.Hostile ? "ui.scan.threat.hostile"
                : sp.Temperament == Shared.Definitions.CreatureTemperament.Territorial ? "ui.scan.threat.provokable"
                : "ui.scan.threat.safe";
            readout.LegacyInfo = $"{sp.Habitat} · {sp.Activity} · {sp.Temperament}";
            readout.LegacyThreat = sp.Hostile ? "Hostile" : sp.Temperament == Shared.Definitions.CreatureTemperament.Territorial ? "Provokable" : "Safe";
            value = sp.Hostile ? KnowledgeCreatureHostile : KnowledgeCreature;
            readout.Display = string.IsNullOrEmpty(sp.Name) ? subjectKey : sp.Name; // the coined species name on the readout
        }
        else if (subjectType == "block" && System.Array.IndexOf(RuneBlocks, subjectKey) >= 0
                 && MonumentForScan(session) is { } monument)
        {
            // The runes ARE the discovery — the block is just how the player points the scanner at it. The
            // ledger is per body AND per archetype, so the next planet's relics are worth finding too.
            readout.Kind = "monument";
            readout.SubjectKey = "monument_" + monument.Archetype;
            readout.Display = readout.SubjectKey; // the client localizes it via ui.scan.subject.*
            readout.InfoKey = "ui.scan.monument." + monument.Archetype;
            readout.ThreatKey = "ui.scan.threat.inert";
            readout.LegacyInfo = "Ancient inscriptions — origin unknown.";
            readout.LegacyThreat = "Inert";
            value = KnowledgeMonument;
            ledgerKey = $"monument:{_world.LocationId}:{monument.Archetype}";
        }
        else if (subjectType == "block" && _content.GetBlock(subjectKey) is { } block)
        {
            readout.Drops = block.Drops.Select(d => new NetTradeItem { Item = d.Item, Count = d.Count }).ToArray();
            readout.LegacyInfo = string.Join(", ", block.Drops.Select(d => $"{d.Item}×{d.Count}"));
            bool hasDrops = readout.Drops.Length > 0;

            // Trees and flora read as a named species with an edible/toxic classification; other blocks
            // report yield. Trunk + leaves share the world's one tree species AND a single ledger key, so
            // scanning either part counts as one discovery.
            if (TreeSpeciesForBlock(subjectKey) is { } tree)
            {
                readout.Kind = "tree";
                readout.InfoKey = hasDrops ? string.Empty : "ui.scan.foliage";
                readout.ThreatKey = tree.Toxic ? "ui.scan.threat.toxic" : "ui.scan.threat.edible";
                readout.LegacyInfo = hasDrops ? $"Yields: {readout.LegacyInfo}" : "Foliage of the tree.";
                readout.LegacyThreat = tree.Toxic ? "Toxic" : "Edible";
                readout.Display = string.IsNullOrEmpty(tree.Name) ? subjectKey : tree.Name;
                ledgerKey = $"tree:{tree.Id}";
            }
            else if (FloraSpeciesForBlock(subjectKey) is { } flora)
            {
                readout.Kind = "flora";
                readout.InfoKey = hasDrops ? string.Empty : "ui.scan.flora_harvest";
                readout.ThreatKey = flora.Toxic ? "ui.scan.threat.toxic" : "ui.scan.threat.edible";
                readout.LegacyInfo = hasDrops ? $"Yields: {readout.LegacyInfo}" : "Harvestable flora.";
                readout.LegacyThreat = flora.Toxic ? "Toxic" : "Edible";
                readout.Display = string.IsNullOrEmpty(flora.Name) ? subjectKey : flora.Name;
            }
            else
            {
                readout.InfoKey = hasDrops ? string.Empty : "ui.scan.no_yield";
                readout.LegacyInfo = hasDrops ? $"Yields: {readout.LegacyInfo}" : "No yield.";
            }

            value = KnowledgeBlock;
        }
        else
        {
            return Rejected(subjectKey, "ui.scan.unknown", "Unknown subject.");
        }

        // Ledger key tracks the first scan (per species/block, or shared per tree); the readout shows `Display`.
        return Award(session, ledgerKey, readout, value);
    }

    /// <summary>The monument the scanning player is standing at, or null. The scan intent carries no position
    /// (#524) — but the player's position is server-authoritative, so the relic is resolved from where they
    /// actually are. Only valid while the player's own world is the active one; a scan from elsewhere falls
    /// back to the ordinary block readout rather than crediting the wrong body's relic.</summary>
    private MonumentInstance? MonumentForScan(PlayerSession session)
        => _monuments.Count > 0 && session.CurrentLocationId == _worlds.Active.LocationId
            ? MonumentNear(session.State.Position)
            : null;

    /// <summary>The structured readout the client localizes, plus the legacy English strings kept for one
    /// release so an old client still shows something (#484).</summary>
    private sealed class ScanReadout
    {
        public string Kind = string.Empty;
        public string SubjectKey = string.Empty;
        public string Display = string.Empty;
        public string ThreatKey = string.Empty;
        public string InfoKey = string.Empty;
        public string[] TraitKeys = System.Array.Empty<string>();
        public NetTradeItem[] Drops = System.Array.Empty<NetTradeItem>();
        public string LegacyInfo = string.Empty;
        public string LegacyThreat = "—";
    }

    /// <summary>A scan that produced nothing to award (no session / unscannable subject).</summary>
    private static ScanResult Rejected(string subjectKey, string infoKey, string legacyInfo)
        => new() { Subject = subjectKey, SubjectKey = subjectKey, InfoKey = infoKey, Info = legacyInfo, Threat = "—" };

    /// <summary>Ship scan of a space asteroid — reveals whether it holds resources (server knows the loot).</summary>
    public ScanResult ScanSpaceEntity(string playerId, string entityId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return new ScanResult { Subject = entityId, Info = "No scanner.", Threat = "—" };
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId)
            || !_spaceInstances.TryGetValue(instanceId, out var instance)
            || instance.Entities.FirstOrDefault(e => e.Id == entityId) is not { } target
            || target.Kind != CombatEntityKind.Asteroid)
        {
            return Rejected(entityId, "ui.scan.not_scannable", "Not a scannable object.");
        }

        // Asteroids break down to mineral drops; report the resource types they ultimately yield.
        // Count 0 = "type only, no quantity" — the client then omits the "×n" suffix.
        var loot = target.Loot.Count > 0 ? target.Loot : MakeAsteroid(0, target.Position).Loot;
        var kinds = loot.Select(l => l.Item).Distinct().ToArray();
        var readout = new ScanReadout
        {
            Kind = "asteroid",
            SubjectKey = "asteroid",
            Display = "asteroid",
            Drops = kinds.Select(k => new NetTradeItem { Item = k, Count = 0 }).ToArray(),
            InfoKey = kinds.Length > 0 ? string.Empty : "ui.scan.barren",
            LegacyInfo = kinds.Length > 0 ? "Resources: " + string.Join(", ", kinds) : "Barren — no resources.",
        };

        return Award(session, "asteroid", readout, KnowledgeAsteroid);
    }

    private ScanResult Award(PlayerSession session, string ledgerKey, ScanReadout readout, int value)
    {
        var p = session.State;
        bool firstTime = p.Scanned.Add(ledgerKey); // HashSet.Add returns false if already present
        int gained = firstTime ? (int)System.Math.Round(value * ScanMultiplier(p)) : 0;
        if (gained > 0)
        {
            p.KnowledgePoints += gained;
        }

        var result = new ScanResult
        {
            Subject = readout.Display,
            SubjectKey = readout.SubjectKey,
            Kind = readout.Kind,
            ThreatKey = readout.ThreatKey,
            TraitKeys = readout.TraitKeys,
            Drops = readout.Drops,
            InfoKey = readout.InfoKey,
            Info = readout.LegacyInfo,     // legacy English, for an old client only
            Threat = readout.LegacyThreat, // ditto
            FirstTime = firstTime,
            KnowledgeGained = gained,
            KnowledgeTotal = p.KnowledgePoints,
        };
        Send(session, result);
        if (firstTime)
        {
            // Remember the display name NOW: creature/tree/flora species are per-world, so this coined name
            // is unresolvable once the player leaves this planet (#484).
            p.ScannedNames[ledgerKey] = readout.Display;

            // Append the new entry to the client's Codex discovery list.
            Send(session, new DiscoveryLog
            {
                Entries = new[] { ledgerKey },
                Names = new[] { readout.Display },
                Full = false,
            });
        }

        ShipAiOnScan(session); // VEGA onboarding: first scan (any subject counts)
        return result;
    }

    /// <summary>Sends the player's whole first-scan ledger — the Codex "Discoveries" snapshot on join.</summary>
    private void SendDiscoveryLog(PlayerSession session)
    {
        var entries = session.State.Scanned.ToArray();
        var names = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            // Pre-#484 entries have no recorded name — send empty and let the client show the raw key.
            names[i] = session.State.ScannedNames.TryGetValue(entries[i], out var n) ? n : string.Empty;
        }

        Send(session, new DiscoveryLog { Entries = entries, Names = names, Full = true });
    }

    private void HandleScan(PlayerSession session, ScanIntent intent)
        => ScanSubject(session.State.PlayerId, intent.SubjectType, intent.SubjectKey);

    private void HandleScanEntity(PlayerSession session, ScanEntityIntent intent)
        => ScanSpaceEntity(session.State.PlayerId, intent.EntityId);
}
