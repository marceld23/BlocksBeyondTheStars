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
    private const int KnowledgeMicroFauna = 2; // small per find, but 28 kinds add up across worlds (#757)

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
            return Rejected(null, subjectKey, "ui.scan.no_scanner", "No scanner.");
        }

        var readout = new ScanReadout { Kind = subjectType, SubjectKey = subjectKey, Display = subjectKey };
        int value;
        // First-scan ledger key. Defaults to the species/block key; trees override it to a shared key so the
        // trunk and the leaves count as one discovery.
        string ledgerKey = $"{subjectType}:{subjectKey}";

        if (subjectType == "creature" && _speciesById.TryGetValue(subjectKey, out var sp))
        {
            // The voice descriptor (#907) makes a species' call a readable trait like its colour or gait,
            // instead of something the player can only recognise subconsciously.
            var voice = Shared.Definitions.CreatureVoices.Derive(
                sp.VoiceSeed, Shared.Definitions.VoiceTraits.From(sp));
            readout.TraitKeys = new[]
            {
                "ui.scan.habitat." + sp.Habitat.ToString().ToLowerInvariant(),
                "ui.scan.activity." + sp.Activity.ToString().ToLowerInvariant(),
                "ui.scan.temperament." + sp.Temperament.ToString().ToLowerInvariant(),
                Shared.Definitions.CreatureVoices.DescriptorKey(voice),
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
        else if (subjectType == "microfauna" && Shared.Content.MicroFaunaCatalog.IsKnown(subjectKey))
        {
            // Micro-fauna live purely client-side (#757) — the server can't verify a critter was at the
            // crosshair, only that the kind exists. That is exactly the trust level creature scans already
            // run at (no position check either), and the knowledge value is deliberately small.
            readout.ThreatKey = "ui.scan.threat.safe";
            readout.InfoKey = "ui.scan.microfauna";
            readout.LegacyInfo = "Ambient micro-fauna.";
            readout.LegacyThreat = "Safe";
            value = KnowledgeMicroFauna;
            // Display stays the raw kind key — the client localizes it via ui.scan.subject.<key>.
        }
        else
        {
            return Rejected(session, subjectKey, "ui.scan.unknown", "Unknown subject.");
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

    /// <summary>A scan that produced nothing to award (no session / unscannable subject). The result is
    /// still SENT when a session exists: a silently dropped scan leaves the client's readout pinned on the
    /// previous subject, which reads as "the scanner is stuck" (#1005).</summary>
    private ScanResult Rejected(PlayerSession? session, string subjectKey, string infoKey, string legacyInfo)
    {
        var result = new ScanResult { Subject = subjectKey, SubjectKey = subjectKey, InfoKey = infoKey, Info = legacyInfo, Threat = "—" };
        if (session is not null)
        {
            Send(session, result);
        }

        return result;
    }

    /// <summary>Ship scan of a space asteroid — reveals whether it holds resources (server knows the loot).</summary>
    public ScanResult ScanSpaceEntity(string playerId, string entityId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return Rejected(null, entityId, "ui.scan.no_scanner", "No scanner.");
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId)
            || !_spaceInstances.TryGetValue(instanceId, out var instance)
            || instance.Entities.FirstOrDefault(e => e.Id == entityId) is not { } target
            || target.Kind is not (CombatEntityKind.Asteroid or CombatEntityKind.Anomaly))
        {
            return Rejected(session, entityId, "ui.scan.not_scannable", "Not a scannable object.");
        }

        // An anomaly (#1129): the scan is the whole encounter — knowledge once per save per player,
        // and the archive opens one of its lore texts (deduped per player inside the reveal).
        if (target.Kind == CombatEntityKind.Anomaly)
        {
            var anomalyReadout = new ScanReadout
            {
                Kind = "anomaly",
                SubjectKey = "anomaly",
                Display = target.Name,
                InfoKey = "ui.scan.anomaly",
                LegacyInfo = "Readings defy the catalogue — logged for the archive.",
            };
            var anomalyResult = Award(session, "anomaly:signature", anomalyReadout, KnowledgeAnomaly);
            TryRevealLoreText(session, "anomaly");
            return anomalyResult;
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

            OnAchievementScan(session, readout.Kind); // "Scholar" / "Archaeologist" and friends (#1102)
            if (readout.Kind == "monument")
            {
                RecordStoryMilestone("monument:" + _world.LocationId); // first rune read on this world (#1105)
                TryRevealLoreText(session, "monument"); // the runes' inscription opens in the reader (#1111)
            }
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
