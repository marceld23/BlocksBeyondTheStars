// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Space wrecks (#1664): the star map's <see cref="CelestialKind.Wreck"/> bodies — coined-name derelicts the
/// travel screen has listed since #678 but that never existed anywhere in flight — now drift in their system's
/// space as <b>voxel hulls</b>. Each is a <see cref="WreckGenerator"/> hull of one of the content ship designs
/// (seeded per body, so the same wreck greets every pilot on every entry), parked at the body's scaled
/// star-map position like a belt cluster (#683 S2). It is never hostile and never a travel destination: you
/// <i>fly</i> there. Coming within <see cref="SpaceWreckApproachRange"/> reads its manifest (a scan readout,
/// a "derelict" lore text, the body marked visited on the star map); a mining laser carves it down for
/// salvage exactly like a voxel asteroid; an EVA suit can hand-mine its plating.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Flight units within which a pilot "visits" a wreck (manifest readout + lore + star-map visited
    /// mark). Just beyond mining-laser reach, so the reading comes before the first shot.</summary>
    internal const float SpaceWreckApproachRange = 45f;

    /// <summary>Knowledge for reading a derelict's manifest (between an asteroid and an anomaly).</summary>
    private const int KnowledgeSpaceWreck = 6;

    /// <summary>Scan-ledger prefix of a wreck (per body id): the approach reading is once per player per wreck.</summary>
    private const string SpaceWreckScanPrefix = "wreck:";

    /// <summary>Parks every <see cref="CelestialKind.Wreck"/> body of the anchor's system in the instance as a
    /// voxel hull + salvage entity, at the body's flight-view position (the client's layout transform: star-map
    /// delta to the anchor × <see cref="SystemBodyLayout.FlightViewScale"/>) plus a small stable height offset,
    /// so it neither sits in the flight plane's clutter nor hides above it.</summary>
    private void AddSpaceWrecks(SpaceInstance instance, CelestialBody? anchor)
    {
        if (anchor is null || _galaxy?.Systems.FirstOrDefault(s => s.Id == anchor.SystemId) is not { } system)
        {
            return;
        }

        foreach (var body in system.Bodies)
        {
            if (body.Kind != CelestialKind.Wreck || instance.Entities.Any(e => e.Id == body.Id))
            {
                continue;
            }

            // Its own salt ("spacewreck:"), NOT the planet wreck's "wreck:" — the two rolls must stay independent
            // so a planet's stamped crash site is byte-identical to before this feature.
            long seed = _meta.Seed ^ WorldGenerator.StableHash("spacewreck:" + body.Id);
            var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
            var designs = _content.Ships.Values.OrderBy(d => d.Key, StringComparer.Ordinal).ToList();
            if (designs.Count == 0)
            {
                return;
            }

            var design = designs[rng.Next(designs.Count)];
            var hull = WreckGenerator.Generate(design, seed, _content);
            float height = (rng.Next(9) - 4) * 3f; // -12 .. +12, stable per wreck
            var pos = new Vector3f(
                (body.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale,
                height,
                (body.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale);

            var structure = MakeSpaceWreckStructure(body.Id, pos, hull);
            if (structure.Cells.Count == 0)
            {
                continue; // no hull blocks in content — nothing to render or salvage
            }

            // #2354: salvage pays once. A wreck carved to nothing stays gone; a half-carved one comes back as the
            // pilot left it (the ledger keeps its remaining cells; the outermost plating is what the laser took).
            int original = structure.Cells.Count;
            bool ledgered = _meta.SpaceSalvage.TryGetValue(WreckLedgerKey(body.Id), out int remaining);
            if (ledgered && remaining <= 0)
            {
                continue;
            }

            if (ledgered && remaining < original)
            {
                PeelOutermost(structure, original - remaining, onRemoved: null);
            }

            var entity = new CombatEntity
            {
                Id = body.Id, // the star-map body id: the client's chart, radar and waypoints key on it
                Kind = CombatEntityKind.Wreck,
                Name = body.Name,
                Hostile = false,
                AsteroidTier = 0, // carves + depletes like a voxel asteroid, never splits
                Position = pos,
            };
            foreach (var drop in SpaceWreckSalvage(hull.Origin, original, rng))
            {
                entity.Loot.Add(drop); // the payout follows the whole hull, not what is left of it
            }

            entity.HullMax = Math.Max(8, original); // hull == blocks → carve maps to damage
            entity.Hull = ledgered ? Math.Min(entity.HullMax, Math.Max(1, structure.Cells.Count)) : entity.HullMax;
            instance.Entities.Add(entity);
            instance.Structures[structure.Id] = structure;
        }
    }

    /// <summary>Adapts a generated wreck hull to a flight-scene voxel structure (kind "wreck"), centred on its
    /// own middle so the entity position is the hull's centre and the laser carve peels the outermost plating
    /// first (mirrors <see cref="MakeAsteroidStructure"/>).</summary>
    private static SpaceStructure MakeSpaceWreckStructure(string id, Vector3f pos, WreckStructure hull)
    {
        var s = new SpaceStructure
        {
            Id = id,
            Kind = "wreck",
            OwnerId = string.Empty, // ownerless → anyone may salvage it
            Position = pos,
            Width = hull.Width,
            Height = hull.Height,
            Length = hull.Length,
        };

        int ox = hull.Width / 2, oy = hull.Height / 2, oz = hull.Length / 2;
        for (int x = 0; x < hull.Width; x++)
            for (int y = 0; y < hull.Height; y++)
                for (int z = 0; z < hull.Length; z++)
                {
                    ushort b = hull.Get(x, y, z);
                    if (b != 0)
                    {
                        s.Set(new Vector3i(x - ox, y - oy, z - oz), new BlockId(b));
                    }
                }

        return s;
    }

    /// <summary>What a salvaged-to-nothing wreck pays out — the rows of <c>data/space_salvage.json</c> (#2352) by hull
    /// origin (human hulls: titanium, alien hulls: crystal): plating + cabling scaled with the hull, a structural metal,
    /// and a chance of the data / memory fragments a dead ship's terminal would hold. The defaults are the numbers that
    /// used to be written here.</summary>
    private List<ItemAmount> SpaceWreckSalvage(string origin, int cells, Random rng)
    {
        var loot = new List<ItemAmount>();
        var rows = origin == "alien" ? _content.SpaceSalvage.Wreck.Alien : _content.SpaceSalvage.Wreck.Human;
        foreach (var row in rows)
        {
            if (row.Chance < 1.0 && rng.NextDouble() >= row.Chance)
            {
                continue;
            }

            int count = row.Base + (row.PerCells > 0 ? cells / row.PerCells : 0) + (row.ExtraMax > 0 ? rng.Next(row.ExtraMax + 1) : 0);
            if (count > 0 && !string.IsNullOrEmpty(row.Item) && _content.GetItem(row.Item) is not null)
            {
                loot.Add(new ItemAmount(row.Item, count));
            }
        }

        return loot;
    }

    /// <summary>Reads a wreck's manifest: a scan readout naming the salvage it yields (knowledge once per player
    /// per wreck), one of the pack's "derelict" lore texts, and the body marked visited on the star map.</summary>
    private ScanResult ScanSpaceWreck(PlayerSession session, CombatEntity target)
    {
        var kinds = target.Loot.Select(l => l.Item).Distinct().ToArray();
        var readout = new ScanReadout
        {
            Kind = "wreck",
            SubjectKey = "wreck",
            Display = target.Name, // the coined ship name — language-neutral, shown as-is
            Drops = kinds.Select(k => new NetTradeItem { Item = k, Count = 0 }).ToArray(), // types only (client omits ×n)
            InfoKey = "ui.scan.wreck",
            LegacyInfo = kinds.Length > 0 ? "Derelict — salvage: " + string.Join(", ", kinds) : "Derelict — stripped bare.",
        };

        var result = Award(session, SpaceWreckScanPrefix + target.Id, readout, KnowledgeSpaceWreck);
        TryRevealLoreText(session, "derelict");
        MarkSpaceWreckVisited(session, target.Id);
        return result;
    }

    /// <summary>Marks a wreck (or debris field, #2353) body visited for this player (star map + Places codex, like a
    /// first landing) and refreshes their star map. No-op for any other body, or on a repeat.</summary>
    private void MarkSpaceWreckVisited(PlayerSession session, string bodyId)
    {
        if (_galaxy?.FindBody(bodyId) is not { Kind: CelestialKind.Wreck or CelestialKind.DebrisField } body)
        {
            return;
        }

        MarkBodyVisited(body.Id); // #1856: the wreck is charted for everyone — it never went through LoadWorld's stamp
        if (!session.State.LandedBodies.Add(body.Id))
        {
            return;
        }

        RecordPlaceDiscovery(session, body); // #1113: a "Places" Codex entry + the knowledge grant
        SendStarMap(session);
    }

    /// <summary>Called on every ship pose update: the first time a pilot comes within
    /// <see cref="SpaceWreckApproachRange"/> of a wreck, VEGA reads its manifest (<see cref="ScanSpaceWreck"/>).
    /// The scan ledger makes it once per player per wreck; instances rarely hold more than one wreck, so the
    /// per-move cost is one short pass over the entity list.</summary>
    private void CheckSpaceWreckApproach(SpaceInstance instance, string playerId, Vector3f pos)
    {
        const float rangeSq = SpaceWreckApproachRange * SpaceWreckApproachRange;
        float fieldRange = _content.SpaceSalvage.Fields.ApproachRange;
        float fieldRangeSq = fieldRange * fieldRange;
        PlayerSession? session = null;
        foreach (var e in instance.Entities)
        {
            bool wreck = e.Kind == CombatEntityKind.Wreck;
            bool field = e.Kind == CombatEntityKind.DebrisField; // #2353: the flight recorder is read the same way
            if ((!wreck && !field) || e.Position.DistanceSquared(pos) > (wreck ? rangeSq : fieldRangeSq))
            {
                continue;
            }

            session ??= FindSessionByPlayerId(playerId);
            if (session is null)
            {
                return;
            }

            if (wreck && !session.State.Scanned.Contains(SpaceWreckScanPrefix + e.Id))
            {
                ScanSpaceWreck(session, e);

                // #1882: flown there once — the pilot knows how to reach a wreck; VEGA stops explaining it.
                RetireVegaTip(session, "wreck_signal");
                RetireVegaTip(session, "wreck_signal_manual");
            }
            else if (field && !session.State.Scanned.Contains(DebrisFieldScanKey(e.Id)))
            {
                ScanDebrisField(session, e);
                RetireVegaTip(session, "debris_signal");
            }
        }
    }
}
