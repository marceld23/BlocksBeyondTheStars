// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Stamps standalone <b>ruins</b> of fallen settlements on a body's surface — a separate, rarer feature
/// from the inhabited settlements (and from the ruined settlements the settlement allocator already mixes
/// in). A ruin is a town/city run through the heavy decay pass in <see cref="SettlementGenerator"/>:
/// partial walls, a half-standing tower, rubble and overgrowth. Unlike intact settlements/stations, ruins
/// are <b>not protected</b> — they are not tracked as structures at all, just terrain blocks (freely
/// mineable) plus scavengeable loot caches. Because they are mineable they are stamped <b>once</b> (guarded
/// by <see cref="WorldManager.LoadedWorld.RuinsStamped"/>) and then persist as block edits — never
/// re-stamped on reload (that would resurrect blocks the player cleared). Deterministic from the world seed.
/// Each ruin still gets a placement record (#3513, kind <c>ruin</c>: origin, tier, footprint), from which every later load
/// restores the footprints the stampers after it keep clear of.
/// </summary>
public sealed partial class GameServer
{
    private bool _ruinsStamped { get => _worlds.Active.RuinsStamped; set => _worlds.Active.RuinsStamped = value; }

    private void StampRuins()
    {
        if (_ruinsStamped || FeatureStamped("ruins"))
        {
            // Already stamped — this session OR any earlier one (#467): the ruin blocks live on as
            // persisted edits. Ruins are deliberately mineable, so re-stamping would resurrect every
            // scavenged wall (the doc comment above always promised this; the persisted registry is
            // what finally makes it true across world unloads).
            if (!_ruinsStamped)
            {
                RestoreRuinFootprints(); // #3513: the later stampers keep clear of them on every load, not only the first
            }

            _ruinsStamped = true;
            return;
        }

        var planet = _world.Planet;

        // A fallen city implies a once-habitable world: skip truly airless bodies (asteroids/airless moons) — the kind's
        // shipped condition (#3517, data/structure_types.json).
        if (!StructureConditionsHold(StructureTypeRules.Ruin, planet))
        {
            return;
        }

        // Ruins ride the same "structures frequency" world option as settlements (Off ⇒ none), but unlike
        // settlements they are NOT gated by hospitability — a dead city can sit on a harsh world.
        double factor = _meta.Description.Settlements.StructureFactor();
        if (factor <= 0)
        {
            return;
        }

        _ruinsStamped = true;
        MarkFeatureStamped("ruins"); // #467: whatever the rolls below decide is decided once, forever

        long rSeed = _meta.Seed ^ WorldGenerator.StableHash("ruins:" + _world.LocationId); // per body (#478)
        var rng = new System.Random(unchecked((int)(rSeed ^ (rSeed >> 32))));

        // Rare: most worlds get none, occasionally one, rarely two — nudged up a touch on bigger worlds and
        // when the structures frequency is set above normal.
        double sizeFactor = System.Math.Clamp(_world.Circumference / SettlementRefCirc, 0.5, 2.0);
        // #1761: a type may bias its roll (the scrap planet: 2.0); 1.0 is the identical draw for every other type.
        double r = rng.NextDouble() / sizeFactor * System.Math.Max(0.0, planet.RuinsBias);
        var rules = StructureType(StructureTypeRules.Ruin); // #3517: thresholds [0.55, 0.85] and cap 3 as shipped
        int count = rules.CountBelow(r);
        count = PinnedCount(RuinRecordKind, System.Math.Min(rules.Cap, (int)System.Math.Round(count * System.Math.Clamp(factor, 0.0, 2.0))));
        if (count <= 0)
        {
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        // Reserve the pads, the wreck zone and every already-stamped settlement so ruins land clear of them.
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        reserved.Add((pad0X - 56, pad0Z + 56, 14, 14)); // wreck zone (see GameServerWrecks.StampWreck)
        foreach (var s in _settlements)
        {
            reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2,
                (s.Max.X - s.Min.X) / 2 + 1, (s.Max.Z - s.Min.Z) / 2 + 1));
        }

        AppendIntercityReservations(reserved); // #2125: the intercity line's stations and route
        reserved = KeepClearFor(StructureTypeRules.Ruin, reserved); // #3514: generation 38 keeps clear through the registry

        var placed = new List<PlacedSettlement>();
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var s in _settlements)
        {
            usedNames.Add(s.Name);
        }

        for (int i = 0; i < count; i++)
        {
            long instSeed = rSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));

            // A fallen city skews large so the rubble field reads as a real settlement that once stood here.
            string tier = ir.NextDouble() < 0.55 ? "town" : "city";
            var structure = SettlementGenerator.Generate(tier, ruined: true, instSeed, surface, _content,
                terrainGeneration: _meta.Description.TerrainGeneration); // #3516: generation 38 ruins through the shared pass

            // #586: ruins stamp exactly once and never re-derive positions — a fresh world gets the guaranteed
            // search (drowned/lava ruins allowed), a legacy world stamping its one-time ruins now keeps the frozen
            // first-fit search. #3513: the outcome is recorded all the same (kind "ruin"), so every kind has a
            // record and the footprints the later stampers keep clear of come back on every load.
            Vector3i origin;
            int groundY;
            bool onIsland;
            string seat = "legacy";
            if (_worlds.Active.VirginAtLoad)
            {
                if (!TryPlaceStructureGuaranteed(structure, RngFor(instSeed, "search"), reserved,
                        wantIsland: false, SeatPolicyOf(StructureTypeRules.Ruin, SeatPolicy.Ruin), avoidPlayerEdits: false,
                        out origin, out groundY, out onIsland, out seat, siteKind: StructureTypeRules.Ruin))
                {
                    RecordPlacementSkip(RuinRecordKind, i);
                    continue;
                }

                ReserveSiteGround(StructureTypeRules.Ruin, structure.Width, structure.Length, origin, groundY, seat); // #3569
            }
            else if (!TryPlaceSettlement(structure, ir, reserved, wantIsland: false, out origin, out groundY, out onIsland))
            {
                RecordPlacementSkip(RuinRecordKind, i);
                continue;
            }

            string name = UniqueName(SettlementDisplayName(tier, true,
                _worlds.Active.VirginAtLoad ? RngFor(instSeed, "name") : ir), usedNames);
            var ruin = new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = tier,
                Ruined = true,
                OnIsland = onIsland,
                Name = name,
                Seat = seat,
                Rng = ir,
            };
            placed.Add(ruin);
            RecordRuin(i, ruin); // #3513
            var footprint = RuinFootprint(origin.X, origin.Z, structure.Width, structure.Length);
            reserved.Add(footprint);
            _worlds.Active.RuinFootprints.Add(footprint); // #2166: the abandoned station decided next keeps clear of it
            RegisterFootprint(StructureTypeRules.Ruin, footprint); // #3514
        }

        SavePlacementRecords();
        ReportStamp("ruin", count, placed.Count);
        if (placed.Count == 0)
        {
            return;
        }

        // Stamp every ruin's voxels in one transaction (same path as settlements).
        _repo.RunInTransaction(() =>
        {
            foreach (var p in placed)
            {
                StampSettlementBlocks(p, surface);
            }
        });

        // Ruins are NOT registered as structures — they are just terrain now. Only their loot caches need
        // recording (idempotent via GeneratedLoot), so the rubble can be scavenged.
        foreach (var p in placed)
        {
            foreach (var m in p.Structure.Markers)
            {
                if (m.Type != "loot")
                {
                    continue;
                }

                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                SpawnStructureLoot("ruin", m.Type, pos, p.Rng); // scavengeable caches in the rubble
            }
        }

        _log.Info($"Stamped {placed.Count} ruin(s) on '{_world.LocationId}'.");
    }
}
