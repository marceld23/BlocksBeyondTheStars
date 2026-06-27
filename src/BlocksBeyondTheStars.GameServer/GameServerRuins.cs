// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
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
/// </summary>
public sealed partial class GameServer
{
    private const int RuinHardCap = 3;

    private bool _ruinsStamped { get => _worlds.Active.RuinsStamped; set => _worlds.Active.RuinsStamped = value; }

    private void StampRuins()
    {
        if (_ruinsStamped)
        {
            return; // already stamped once — the ruin blocks live on as persisted edits
        }

        var planet = _world.Planet;

        // A fallen city implies a once-habitable world: skip truly airless bodies (asteroids/airless moons).
        if (planet.IsAirless)
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

        long rSeed = _meta.Seed ^ WorldGenerator.StableHash("ruins:" + planet.Key);
        var rng = new System.Random(unchecked((int)(rSeed ^ (rSeed >> 32))));

        // Rare: most worlds get none, occasionally one, rarely two — nudged up a touch on bigger worlds and
        // when the structures frequency is set above normal.
        double sizeFactor = System.Math.Clamp(_world.Circumference / SettlementRefCirc, 0.5, 2.0);
        double r = rng.NextDouble() / sizeFactor;
        int count = r < 0.55 ? 0 : r < 0.85 ? 1 : 2;
        count = System.Math.Min(RuinHardCap, (int)System.Math.Round(count * System.Math.Clamp(factor, 0.0, 2.0)));
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
            var structure = SettlementGenerator.Generate(tier, ruined: true, instSeed, surface, _content);

            if (!TryPlaceSettlement(structure, ir, reserved, wantIsland: false, out var origin, out int groundY, out bool onIsland))
            {
                continue;
            }

            string name = UniqueName(SettlementDisplayName(tier, true, ir), usedNames);
            placed.Add(new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = tier,
                Ruined = true,
                OnIsland = onIsland,
                Name = name,
                Rng = ir,
            });
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

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
