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

    /// <summary>Handheld scan of a creature species ("creature") or a block/flora/material ("block").</summary>
    public ScanResult ScanSubject(string playerId, string subjectType, string subjectKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return new ScanResult { Subject = subjectKey, Info = "No scanner.", Threat = "—" };
        }

        string info;
        string threat = "—";
        int value;
        string display = subjectKey;
        // First-scan ledger key. Defaults to the species/block key; trees override it to a shared key so the
        // trunk and the leaves count as one discovery.
        string ledgerKey = $"{subjectType}:{subjectKey}";

        if (subjectType == "creature" && _speciesById.TryGetValue(subjectKey, out var sp))
        {
            info = $"{sp.Habitat} · {sp.Activity} · {sp.Temperament}";
            threat = sp.Hostile ? "Hostile" : sp.Temperament == Shared.Definitions.CreatureTemperament.Territorial ? "Provokable" : "Safe";
            value = sp.Hostile ? KnowledgeCreatureHostile : KnowledgeCreature;
            display = string.IsNullOrEmpty(sp.Name) ? subjectKey : sp.Name; // the coined species name on the readout
        }
        else if (subjectType == "block" && _content.GetBlock(subjectKey) is { } block)
        {
            var drops = string.Join(", ", block.Drops.Select(d => $"{d.Item}×{d.Count}"));
            // Trees and flora read as a named species with an edible/toxic classification; other blocks
            // report yield. Trunk + leaves share the world's one tree species AND a single ledger key, so
            // scanning either part counts as one discovery.
            if (TreeSpeciesForBlock(subjectKey) is { } tree)
            {
                info = drops.Length > 0 ? $"Yields: {drops}" : "Foliage of the tree.";
                threat = tree.Toxic ? "Toxic" : "Edible";
                display = string.IsNullOrEmpty(tree.Name) ? subjectKey : tree.Name;
                ledgerKey = $"tree:{tree.Id}";
            }
            else if (FloraSpeciesForBlock(subjectKey) is { } flora)
            {
                info = drops.Length > 0 ? $"Yields: {drops}" : "Harvestable flora.";
                threat = flora.Toxic ? "Toxic" : "Edible";
                display = string.IsNullOrEmpty(flora.Name) ? subjectKey : flora.Name;
            }
            else
            {
                info = drops.Length > 0 ? $"Yields: {drops}" : "No yield.";
            }

            value = KnowledgeBlock;
        }
        else
        {
            return new ScanResult { Subject = subjectKey, Info = "Unknown subject.", Threat = "—" };
        }

        // Ledger key tracks the first scan (per species/block, or shared per tree); the readout shows `display`.
        return Award(session, ledgerKey, display, info, threat, value);
    }

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
            return new ScanResult { Subject = entityId, Info = "Not a scannable object.", Threat = "—" };
        }

        // Asteroids break down to mineral drops; report the resource types they ultimately yield.
        var loot = target.Loot.Count > 0 ? target.Loot : MakeAsteroid(0, target.Position).Loot;
        string info = loot.Count > 0
            ? "Resources: " + string.Join(", ", loot.Select(l => l.Item).Distinct())
            : "Barren — no resources.";

        return Award(session, "asteroid", "asteroid", info, "—", KnowledgeAsteroid);
    }

    private ScanResult Award(PlayerSession session, string ledgerKey, string subject, string info, string threat, int value)
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
            Subject = subject,
            Info = info,
            Threat = threat,
            FirstTime = firstTime,
            KnowledgeGained = gained,
            KnowledgeTotal = p.KnowledgePoints,
        };
        Send(session, result);
        ShipAiOnScan(session); // VEGA onboarding: first scan (any subject counts)
        return result;
    }

    private void HandleScan(PlayerSession session, ScanIntent intent)
        => ScanSubject(session.State.PlayerId, intent.SubjectType, intent.SubjectKey);

    private void HandleScanEntity(PlayerSession session, ScanEntityIntent intent)
        => ScanSpaceEntity(session.State.PlayerId, intent.EntityId);
}
