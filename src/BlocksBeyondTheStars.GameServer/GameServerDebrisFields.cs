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
/// Debris fields (#2353), combat debris (#2356) and the salvage ledger (#2354).
/// <para>A <see cref="CelestialKind.DebrisField"/> body (placed by <see cref="DebrisFieldPlacer"/>) enters its
/// system's flight instances as a cloud at the body's chart position: a marker entity (<see cref="CombatEntityKind.DebrisField"/>
/// — the chart, radar, waypoint and scanner target, and the flight recorder read on approach), 8–12 wreckage fragments
/// (<see cref="CombatEntityKind.Debris"/>: small voxel hulls carved for scrap exactly like an asteroid) and one or two
/// sealed salvage capsules (<see cref="CombatEntityKind.SalvageCapsule"/>: collected like a resource drop). Which theme a
/// field is — a freighter accident, a shipyard scrapyard, a broken satellite, alien debris, an old battle — comes from
/// <c>data/space_salvage.json</c>, rolled seed-pure per body; so does every loot table.</para>
/// <para>Salvage pays <b>once per galaxy</b>: the capsules are remembered in <see cref="Shared.State.WorldMetadata.SpaceSalvage"/>
/// (a bitmask per field), the space wreck's remaining hull cells too (0 = salvaged, the wreck is gone for good) — before
/// #2354 the wreck rebuilt itself with full salvage on every flight. Fragments are scenery like belt rocks and are rebuilt
/// per instance.</para>
/// <para>Drifting rubble taps the <b>shield only</b> (#2355) while a ship moves inside the field — never the hull, so a
/// field stays peaceful content under every preset. A destroyed drone, saucer, cruiser or raider leaves fragments behind
/// (#2356), a raider's cut from its own hull.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Knowledge for reading a debris field's flight recorder (the wreck's manifest pays the same).</summary>
    private const int KnowledgeDebrisField = 6;

    /// <summary>Scan-ledger prefix of a debris field (per body id): the approach reading is once per player per field.</summary>
    private const string DebrisFieldScanPrefix = "debris:";

    /// <summary>Hull points a fragment needs at least — the starter laser (14) still pops any fragment in one shot.</summary>
    private const int DebrisFragmentMinHull = 4;

    /// <summary>Flight units around a destroyed ship its fragments scatter in.</summary>
    private const float CombatDebrisScatter = 4f;

    /// <summary>True while the salvage ledger changed since it was last written (saved at instance teardown and on
    /// every completed salvage; the autosave writes the metadata anyway).</summary>
    private bool _spaceSalvageDirty;

    private static string DebrisFieldScanKey(string bodyId) => DebrisFieldScanPrefix + bodyId;
    private static string WreckLedgerKey(string bodyId) => "wreck:" + bodyId;
    private static string DebrisLedgerKey(string bodyId) => "debris:" + bodyId;

    /// <summary>The archetype name the salvage data keys its multipliers by.</summary>
    private string ArchetypeNameOf(string? systemId) => SystemArchetypeOf(systemId).ToString();

    /// <summary>The theme of a debris field body (seed-pure), or null when the content has no themes.</summary>
    private DebrisThemeDefinition? ThemeOfField(string bodyId, out string themeKey)
    {
        var def = _content.SpaceSalvage;
        string systemId = _galaxy?.FindBody(bodyId)?.SystemId ?? string.Empty;
        themeKey = DebrisFieldPlacer.ThemeFor(_meta.Seed, bodyId, ArchetypeNameOf(systemId), def);
        return def.Themes.TryGetValue(themeKey, out var theme) ? theme : null;
    }

    // ---------------- the field in flight ----------------

    /// <summary>Parks every debris field body of the anchor's system in the instance: the marker at the body's flight-view
    /// position (star-map delta to the anchor × <see cref="SystemBodyLayout.FlightViewScale"/>, a small stable height),
    /// the fragments scattered inside the field radius, the capsules closer to the middle — minus the capsules this
    /// galaxy has already collected. Every roll is seeded from the body, so the same field greets every pilot.</summary>
    private void AddDebrisFields(SpaceInstance instance, CelestialBody? anchor)
    {
        if (anchor is null || _galaxy?.Systems.FirstOrDefault(s => s.Id == anchor.SystemId) is not { } system)
        {
            return;
        }

        var fields = _content.SpaceSalvage.Fields;
        foreach (var body in system.Bodies)
        {
            if (body.Kind != CelestialKind.DebrisField || instance.Entities.Any(e => e.Id == body.Id))
            {
                continue;
            }

            if (ThemeOfField(body.Id, out _) is not { } theme)
            {
                continue; // no themes in content → nothing to drift here
            }

            long seed = _meta.Seed ^ WorldGenerator.StableHash("debrisfield:" + body.Id);
            uint state = (uint)(seed ^ (seed >> 32)) | 1u;
            float height = ((int)(NextAsteroidRand(ref state) % 9) - 4) * 3f; // -12 .. +12, stable per field
            var centre = new Vector3f(
                (body.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale,
                height,
                (body.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale);

            instance.Entities.Add(new CombatEntity
            {
                Id = body.Id, // the star-map body id: the client's chart, radar and waypoints key on it
                Kind = CombatEntityKind.DebrisField,
                Name = body.Name,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = centre,
                FieldId = body.Id,
            });

            int fragments = RangeRoll(fields.FragmentsMin, fields.FragmentsMax, ref state);
            for (int i = 0; i < fragments; i++)
            {
                var pos = Scatter(centre, fields.Radius, ref state);
                var loot = RollLoot(theme.FragmentLoot, ref state);
                SpawnDebrisFragment(instance, pos, theme.Hull, theme.Accent, theme.Scorch, theme.ScorchShare, loot, body.Id, ref state, broadcast: false);
            }

            int capsules = RangeRoll(fields.CapsulesMin, fields.CapsulesMax, ref state);
            int collected = _meta.SpaceSalvage.TryGetValue(DebrisLedgerKey(body.Id), out int mask) ? mask : 0;
            for (int i = 0; i < capsules; i++)
            {
                var pos = Scatter(centre, fields.Radius * 0.6f, ref state);
                var loot = RollLoot(theme.CapsuleLoot, ref state); // rolled even when collected, so the stream stays stable
                if ((collected & (1 << i)) != 0 || loot.Count == 0)
                {
                    continue; // paid once per galaxy (#2354)
                }

                instance.Entities.Add(new CombatEntity
                {
                    Id = body.Id + "-c" + i, // stable per capsule: the ledger bit is its index
                    Kind = CombatEntityKind.SalvageCapsule,
                    Name = body.Name,
                    Hostile = false,
                    Hull = 1f,
                    HullMax = 1f,
                    Position = pos,
                    FieldId = body.Id,
                    Loot = loot,
                });
            }
        }
    }

    /// <summary>One wreckage fragment: a combat entity paired with a small voxel structure of the same id (kind
    /// <c>debris</c>), hull == cells like an asteroid so the laser carves it. Returns null when the content has no
    /// hull block at all.</summary>
    private CombatEntity? SpawnDebrisFragment(SpaceInstance instance, Vector3f pos, string hullKey, string accentKey,
        string scorchKey, double scorchShare, List<ItemAmount> loot, string fieldId, ref uint state, bool broadcast)
    {
        var entity = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Debris,
            Hostile = false,
            AsteroidTier = 0, // carves + depletes like a voxel asteroid, never splits
            Position = pos,
            FieldId = fieldId,
            Loot = loot,
        };

        var s = MakeDebrisStructure(entity.Id, pos, hullKey, accentKey, scorchKey, scorchShare, ref state);
        if (s.Cells.Count == 0)
        {
            return null;
        }

        return AddDebrisFragment(instance, entity, s, broadcast);
    }

    private CombatEntity AddDebrisFragment(SpaceInstance instance, CombatEntity entity, SpaceStructure s, bool broadcast)
    {
        entity.HullMax = entity.Hull = Math.Max(DebrisFragmentMinHull, s.Cells.Count);
        instance.Entities.Add(entity);
        instance.Structures[s.Id] = s;
        if (broadcast)
        {
            foreach (var pid in instance.Players)
            {
                if (FindSessionByPlayerId(pid) is { } session)
                {
                    SendShipDesign(session, s);
                }
            }
        }

        return entity;
    }

    /// <summary>A 3–10 cell random walk inside a 3×2×3 box: plating with an accent block mixed in and a scorched share —
    /// a torn-off piece of hull, not a rock. Unknown block keys fall back to iron plating, then to stone.</summary>
    private SpaceStructure MakeDebrisStructure(string id, Vector3f pos, string hullKey, string accentKey, string scorchKey,
        double scorchShare, ref uint state)
    {
        var fallback = _content.GetBlock("iron_wall")?.NumericId ?? _content.GetBlock("stone")?.NumericId ?? BlockId.Air;
        BlockId B(string key) => _content.GetBlock(key)?.NumericId ?? fallback;
        var hull = B(hullKey);
        var accent = B(accentKey);
        var scorch = B(scorchKey);

        var s = new SpaceStructure { Id = id, Kind = "debris", OwnerId = string.Empty, Position = pos, Width = 3, Height = 2, Length = 3 };
        if (hull.IsAir)
        {
            return s;
        }

        int count = 3 + (int)(NextAsteroidRand(ref state) % 8); // 3..10
        var cells = new List<Vector3i> { new(0, 0, 0) };
        var taken = new HashSet<Vector3i> { new(0, 0, 0) };
        for (int guard = 0; cells.Count < count && guard < 64; guard++)
        {
            var from = cells[(int)(NextAsteroidRand(ref state) % (uint)cells.Count)];
            int axis = (int)(NextAsteroidRand(ref state) % 3);
            int dir = (NextAsteroidRand(ref state) & 1) == 0 ? -1 : 1;
            var next = axis switch
            {
                0 => new Vector3i(from.X + dir, from.Y, from.Z),
                1 => new Vector3i(from.X, from.Y + dir, from.Z),
                _ => new Vector3i(from.X, from.Y, from.Z + dir),
            };
            if (next.X < -1 || next.X > 1 || next.Y < 0 || next.Y > 1 || next.Z < -1 || next.Z > 1 || !taken.Add(next))
            {
                continue;
            }

            cells.Add(next);
        }

        foreach (var c in cells)
        {
            double roll = NextAsteroidRand(ref state) % 1000 / 1000.0;
            var block = roll < scorchShare ? scorch : roll < scorchShare + 0.3 ? accent : hull;
            s.Set(c, block);
        }

        return s;
    }

    /// <summary>A seeded point inside the sphere of <paramref name="radius"/> around <paramref name="centre"/> — rejection
    /// sampling on the xorshift stream, no trig (the field must look the same on every platform).</summary>
    private static Vector3f Scatter(Vector3f centre, float radius, ref uint state)
    {
        float x = 0f, y = 0f, z = 0f;
        for (int tries = 0; tries < 8; tries++)
        {
            x = (NextAsteroidRand(ref state) % 2001 / 1000f - 1f) * radius;
            y = (NextAsteroidRand(ref state) % 2001 / 1000f - 1f) * radius * 0.5f; // flatter than wide, like a belt
            z = (NextAsteroidRand(ref state) % 2001 / 1000f - 1f) * radius;
            if (x * x + y * y + z * z <= radius * radius)
            {
                break;
            }
        }

        return new Vector3f(centre.X + x, centre.Y + y, centre.Z + z);
    }

    private static int RangeRoll(int min, int max, ref uint state)
    {
        if (max <= min)
        {
            return Math.Max(0, min);
        }

        return min + (int)(NextAsteroidRand(ref state) % (uint)(max - min + 1));
    }

    /// <summary>Rolls a loot table: each row with its chance, then a count in its range. Rows naming an unknown item are
    /// skipped (content validation rejects them at load; this is belt and braces for a modded data folder).</summary>
    private List<ItemAmount> RollLoot(List<SalvageRoll> rows, ref uint state)
    {
        var loot = new List<ItemAmount>();
        foreach (var row in rows)
        {
            double chance = NextAsteroidRand(ref state) % 10000 / 10000.0; // drawn for every row — a stable stream
            if (chance >= row.Chance || string.IsNullOrEmpty(row.Item) || _content.GetItem(row.Item) is null)
            {
                continue;
            }

            int count = RangeRoll(row.Min, row.Max, ref state);
            if (count > 0)
            {
                loot.Add(new ItemAmount(row.Item, count));
            }
        }

        return loot;
    }

    // ---------------- combat debris (#2356) ----------------

    /// <summary>The hostile ship kinds that leave wreckage behind when destroyed.</summary>
    private static bool LeavesCombatDebris(CombatEntityKind kind)
        => kind is CombatEntityKind.Drone or CombatEntityKind.Ufo or CombatEntityKind.Cruiser or CombatEntityKind.BanditShip;

    /// <summary>Scatters 2–3 fragments around a destroyed ship. A raider's are cut from its own voxel hull (dye and all),
    /// the Guardian machines' from a dark generic palette. A per-instance cap drops the oldest fragment first, so a long
    /// fight never fills the sky with scrap. Called BEFORE the raider's hull structure is removed.</summary>
    private void SpawnCombatDebris(SpaceInstance instance, CombatEntity destroyed)
    {
        var def = _content.SpaceSalvage.CombatDebris;
        int count = _banditRng.Next(def.FragmentsMin, Math.Max(def.FragmentsMin, def.FragmentsMax) + 1);
        if (count <= 0)
        {
            return;
        }

        uint state = (uint)_banditRng.Next() | 1u;
        instance.Structures.TryGetValue(RaiderStructureId(destroyed.Id), out var hullSource);
        for (int i = 0; i < count; i++)
        {
            var pos = Scatter(destroyed.Position, CombatDebrisScatter, ref state);
            var loot = RollLoot(def.Loot, ref state);
            if (hullSource is { Cells.Count: >= 3 })
            {
                var entity = new CombatEntity
                {
                    Id = NextEntityId(),
                    Kind = CombatEntityKind.Debris,
                    Hostile = false,
                    AsteroidTier = 0,
                    Position = pos,
                    Loot = loot,
                };
                var piece = CutFragmentFrom(hullSource, entity.Id, pos, ref state);
                if (piece.Cells.Count > 0)
                {
                    AddDebrisFragment(instance, entity, piece, broadcast: true);
                    continue;
                }
            }

            SpawnDebrisFragment(instance, pos, "iron_wall", "steel_wall", "carbon", 0.5, loot, string.Empty, ref state, broadcast: true);
        }

        // The cap: combat fragments only (a field's fragments carry their field id), oldest first.
        var combatDebris = instance.Entities.Where(e => e.Kind == CombatEntityKind.Debris && e.FieldId.Length == 0).ToList();
        for (int i = 0; combatDebris.Count - i > def.Cap && i < combatDebris.Count; i++)
        {
            var old = combatDebris[i];
            instance.Entities.Remove(old);
            RemoveAsteroidStructure(instance, old.Id);
        }
    }

    /// <summary>A connected piece of 3–8 cells cut out of a hull (its blocks, dye and glow copied), re-centred on its own
    /// middle so it drifts as a chunk of that ship.</summary>
    private static SpaceStructure CutFragmentFrom(SpaceStructure source, string id, Vector3f pos, ref uint state)
    {
        var piece = new SpaceStructure { Id = id, Kind = "debris", OwnerId = string.Empty, Position = pos, Width = 3, Height = 3, Length = 3 };
        var keys = source.Cells.Keys.ToList();
        if (keys.Count == 0)
        {
            return piece;
        }

        int want = 3 + (int)(NextAsteroidRand(ref state) % 6); // 3..8
        var start = keys[(int)(NextAsteroidRand(ref state) % (uint)keys.Count)];
        var taken = new List<Vector3i> { start };
        var frontier = new List<Vector3i> { start };
        while (taken.Count < want && frontier.Count > 0)
        {
            var from = frontier[(int)(NextAsteroidRand(ref state) % (uint)frontier.Count)];
            frontier.Remove(from);
            foreach (var n in new[]
                     {
                         new Vector3i(from.X + 1, from.Y, from.Z), new Vector3i(from.X - 1, from.Y, from.Z),
                         new Vector3i(from.X, from.Y + 1, from.Z), new Vector3i(from.X, from.Y - 1, from.Z),
                         new Vector3i(from.X, from.Y, from.Z + 1), new Vector3i(from.X, from.Y, from.Z - 1),
                     })
            {
                if (taken.Count >= want)
                {
                    break;
                }

                if (source.Cells.ContainsKey(n) && !taken.Contains(n))
                {
                    taken.Add(n);
                    frontier.Add(n);
                }
            }
        }

        int cx = (int)Math.Round(taken.Average(c => c.X));
        int cy = (int)Math.Round(taken.Average(c => c.Y));
        int cz = (int)Math.Round(taken.Average(c => c.Z));
        foreach (var c in taken)
        {
            var local = new Vector3i(c.X - cx, c.Y - cy, c.Z - cz);
            var mods = source.Mods.TryGetValue(c, out var m) ? m : (0, 0);
            piece.Set(local, source.Cells[c], mods.Item1, mods.Item2, source.Shapes.TryGetValue(c, out int shape) ? shape : 0);
        }

        return piece;
    }

    // ---------------- the shield taps (#2355) ----------------

    /// <summary>Drifting rubble taps the shield of a ship that moves inside a debris field: every
    /// <see cref="DebrisFieldSettings.BumpIntervalSeconds"/> (a little jitter) the shield loses
    /// <see cref="DebrisFieldSettings.BumpShield"/> points, never below zero — the hull is never touched, a shieldless
    /// ship just hears the clank. Not on an EVA (the suit has no shield), not for a ship that sits still. The first tap of
    /// a flight says so once. The client plays the shield-hit cue and the camera jolt on the status drop itself.</summary>
    private void TickDebrisBump(SpaceInstance instance, PlayerSession pilot, PilotSim sim, SpacePlayerPose pose, float speed,
        bool inField, double dt)
    {
        sim.DebrisBumpCooldown = Math.Max(0.0, sim.DebrisBumpCooldown - dt);
        var fields = _content.SpaceSalvage.Fields;
        if (!inField || pose.Eva || speed < fields.BumpMinSpeed || sim.DebrisBumpCooldown > 0.0)
        {
            return;
        }

        sim.DebrisBumpCooldown = fields.BumpIntervalSeconds * (0.8 + 0.4 * _banditRng.NextDouble());
        if (_ship.Shield > 0f)
        {
            _ship.Shield = Math.Max(0f, _ship.Shield - fields.BumpShield);
            SendShipCombatStatus(pilot);
        }

        if (!sim.DebrisBumpToasted)
        {
            sim.DebrisBumpToasted = true;
            Send(pilot, new ServerMessage { Text = "@srv.space.debris_bump" });
        }
    }

    /// <summary>Whether a pose is inside the debris field whose marker is <paramref name="marker"/>.</summary>
    private bool InsideDebrisField(CombatEntity marker, Vector3f pos)
    {
        float r = _content.SpaceSalvage.Fields.Radius;
        return marker.Position.DistanceSquared(pos) <= r * r;
    }

    // ---------------- the flight recorder: approach + scanner ----------------

    /// <summary>Reads a debris field's flight recorder: a scan readout naming the theme and the scrap its fragments yield
    /// (knowledge once per player per field), one of the theme's lore texts, and the body marked visited on the chart.</summary>
    private ScanResult ScanDebrisField(PlayerSession session, CombatEntity marker)
    {
        var theme = ThemeOfField(marker.Id, out string themeKey);
        var kinds = (theme?.FragmentLoot ?? new List<SalvageRoll>()).Select(r => r.Item).Distinct().ToArray();
        var readout = new ScanReadout
        {
            Kind = "debris_field",
            SubjectKey = "debris_field",
            Display = marker.Name, // the coined name — language-neutral, shown as-is
            Drops = kinds.Select(k => new NetTradeItem { Item = k, Count = 0 }).ToArray(), // types only (client omits ×n)
            InfoKey = "ui.scan.debris_field",
            TraitKeys = themeKey.Length > 0 ? new[] { "ui.scan.trait.debris_" + themeKey } : Array.Empty<string>(),
            LegacyInfo = "Debris field — the flight recorder still answers. Scrap: " + string.Join(", ", kinds),
        };

        var result = Award(session, DebrisFieldScanKey(marker.Id), readout, KnowledgeDebrisField);
        TryRevealLoreText(session, theme?.Lore ?? "debris");
        MarkSpaceWreckVisited(session, marker.Id); // a field is charted like a wreck: a Places entry, "visited" for everyone
        return result;
    }

    // ---------------- the salvage ledger (#2354) ----------------

    /// <summary>True once the space wreck of this body was salvaged down to nothing — it is never spawned again.</summary>
    private bool IsWreckSalvaged(string bodyId)
        => _meta.SpaceSalvage.TryGetValue(WreckLedgerKey(bodyId), out int remaining) && remaining <= 0;

    /// <summary>Remembers how much of a wreck's hull is left after a carve (laser or EVA pick) — written to the save at
    /// instance teardown, on completion, or by the autosave.</summary>
    private void NoteWreckHull(SpaceInstance instance, CombatEntity wreck)
    {
        if (!instance.Structures.TryGetValue(wreck.Id, out var s))
        {
            return;
        }

        _meta.SpaceSalvage[WreckLedgerKey(wreck.Id)] = Math.Max(1, s.Cells.Count);
        _spaceSalvageDirty = true;
    }

    /// <summary>The wreck is gone: the ledger reads 0 for good, the save is written, every chart says "salvaged".</summary>
    private void CompleteWreckSalvage(string bodyId)
    {
        _meta.SpaceSalvage[WreckLedgerKey(bodyId)] = 0;
        _repo.SaveMetadata(_meta);
        _spaceSalvageDirty = false;
        BroadcastStarMap();
    }

    /// <summary>A salvage capsule was emptied into a hold: its bit is set for the galaxy and the save written.</summary>
    private void OnSalvageCapsuleCollected(CombatEntity capsule)
    {
        if (capsule.FieldId.Length == 0)
        {
            return;
        }

        int at = capsule.Id.LastIndexOf("-c", StringComparison.Ordinal);
        if (at < 0 || !int.TryParse(capsule.Id.Substring(at + 2), out int index) || index < 0 || index > 30)
        {
            return;
        }

        string key = DebrisLedgerKey(capsule.FieldId);
        int mask = _meta.SpaceSalvage.TryGetValue(key, out int m) ? m : 0;
        _meta.SpaceSalvage[key] = mask | (1 << index);
        _repo.SaveMetadata(_meta);
        _spaceSalvageDirty = false;
    }

    /// <summary>Writes the ledger if a carve changed it since the last write (instance teardown).</summary>
    private void PersistSpaceSalvageLedger()
    {
        if (_spaceSalvageDirty)
        {
            _repo.SaveMetadata(_meta);
            _spaceSalvageDirty = false;
        }
    }

    /// <summary>Test seam (#2354): the stored ledger value for a key, or -1 when untouched.</summary>
    public int SpaceSalvageLedgerForTest(string key) => _meta.SpaceSalvage.TryGetValue(key, out int v) ? v : -1;

    /// <summary>Test seam: the fragments of a debris field (or of combat debris with an empty field id) in a player's instance.</summary>
    public int DebrisFragmentCountForTest(string playerId, string fieldId)
        => SpaceEntitiesFor(playerId).Count(e => e.Kind == CombatEntityKind.Debris && e.FieldId == fieldId);

    /// <summary>Test seam (#2357): how many cells of a structure carry a dye (the raider livery), -1 when there is no such structure.</summary>
    public int StructureDyedCellCountForTest(string structureId)
    {
        foreach (var instance in _spaceInstances.Values)
        {
            if (instance.Structures.TryGetValue(structureId, out var s))
            {
                return s.Mods.Count(m => m.Value.Tint != 0);
            }
        }

        return -1;
    }
}
