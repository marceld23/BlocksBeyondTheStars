// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Abandoned SPS research stations (2026-09, Titas): 3–6 rusted compounds per world of a type that allows
/// <c>sps_labs</c>. Placed and stamped like bandit camps (placement records, voxels once, instances re-derived every
/// load); their salvage and the lore terminal are one-time containers. Inside a roofed module there is no air and it is
/// −90 °C; the planet machines gather around them.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The temperature inside a roofed lab module.</summary>
    private const float SpsLabInsideC = -90f;

    /// <summary>A planet machine spawning for a player this close to a lab appears at the lab instead.</summary>
    private const float SpsLabGuardRange = 96f;

    private List<SpsLabInstance> _spsLabs => _worlds.Active.SpsLabs;

    /// <summary>Whether the active world's type stamps SPS labs (generation 8).</summary>
    private bool SpsLabWorld()
        => _world.Planet is { Void: false } planet && planet.AllowedStructures.Contains("sps_labs")
            && _generator.TerrainGeneration >= WorldDescription.ExtremePlanetsGeneration;

    private void StampSpsLabs()
    {
        if (!SpsLabWorld())
        {
            return;
        }

        var planet = _world.Planet;
        long lSeed = _meta.Seed ^ WorldGenerator.StableHash("spslab:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(lSeed ^ (lSeed >> 32))));
        int count = 3 + rng.Next(4); // 3..6
        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        reserved.Add((pad0X - 56, pad0Z + 56, 14, 14)); // the wreck zone (see GameServerWrecks.StampWreck)

        bool blocksAlreadyStamped = FeatureStamped("spslabs");
        var placed = new List<PlacedSettlement>();
        for (int i = 0; i < count; i++)
        {
            long instSeed = lSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));
            var structure = SpsLabGenerator.Generate(instSeed, surface, _content);

            Vector3i origin;
            int groundY;
            bool onIsland;
            string seat;
            var rec = FindPlacementRecord("spslab", i);
            if (rec is not null)
            {
                if (!rec.Placed)
                {
                    continue;
                }

                origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
                groundY = rec.GroundY;
                onIsland = rec.OnIsland;
                seat = rec.Seat;
            }
            else if (!TryPlaceStructureGuaranteed(structure, RngFor(instSeed, "search"), reserved,
                         wantIsland: false, SeatPolicy.Camp, avoidPlayerEdits: !_worlds.Active.VirginAtLoad,
                         out origin, out groundY, out onIsland, out seat))
            {
                RecordPlacementSkip("spslab", i);
                continue;
            }
            else
            {
                RecordPlacement("spslab", i, origin, groundY, onIsland, seat, "sps_lab_" + i);
            }

            placed.Add(new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = SpsLabGenerator.Tier,
                Ruined = true,
                OnIsland = onIsland,
                Name = "sps_lab_" + i,
                Rng = ir,
                Seat = seat,
            });
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        SavePlacementRecords();
        ReportStamp("spslab", count, placed.Count);
        if (placed.Count == 0)
        {
            return;
        }

        if (!blocksAlreadyStamped)
        {
            _repo.RunInTransaction(() =>
            {
                foreach (var p in placed)
                {
                    StampSettlementBlocks(p, surface);
                }
            });
            MarkFeatureStamped("spslabs");
        }

        foreach (var p in placed)
        {
            var lab = new SpsLabInstance
            {
                Min = new Vector3i(p.Origin.X, p.GroundY, p.Origin.Z),
                Max = new Vector3i(p.Origin.X + p.Structure.Width - 1, p.GroundY + p.Structure.Height, p.Origin.Z + p.Structure.Length - 1),
                Center = new Vector3f(p.Origin.X + p.Structure.Width / 2f, p.GroundY + 1, p.Origin.Z + p.Structure.Length / 2f),
            };
            foreach (var m in p.Structure.Markers)
            {
                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                if (m.Type is "sps_cache" or "data_terminal")
                {
                    SpawnStructureLoot("sps_lab", m.Type, pos, p.Rng);
                }
            }

            _spsLabs.Add(lab);
        }

        _log.Info($"SPS labs on '{_world.LocationId}': {placed.Count} placed.");
    }

    /// <summary>True inside a lab's footprint under a roof — no air, −90 °C.</summary>
    private bool InSpsLab(Vector3f pos)
    {
        if (_spsLabs.Count == 0)
        {
            return false;
        }

        int px = (int)System.Math.Floor(pos.X), py = (int)System.Math.Floor(pos.Y), pz = (int)System.Math.Floor(pos.Z);
        foreach (var lab in _spsLabs)
        {
            int dx = WorldConstants.WrapDeltaX(px - lab.Min.X, _world.Circumference);
            if (dx >= 0 && dx <= lab.Max.X - lab.Min.X && pz >= lab.Min.Z && pz <= lab.Max.Z
                && py >= lab.Min.Y && py <= lab.Max.Y && RoofedAt(pos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The centre of the lab nearest to a position within <paramref name="range"/>, or null.</summary>
    private Vector3f? NearestSpsLab(Vector3f pos, float range)
    {
        Vector3f? best = null;
        float bestSq = range * range;
        foreach (var lab in _spsLabs)
        {
            float dx = (float)WorldConstants.WrapDeltaX(lab.Center.X - pos.X, _world.Circumference);
            float dz = lab.Center.Z - pos.Z;
            float sq = dx * dx + dz * dz;
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = lab.Center;
            }
        }

        return best;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the SPS labs derived for the active world (their box and centre).</summary>
    public IReadOnlyList<(Vector3i Min, Vector3i Max, Vector3f Center)> SpsLabsForTest()
        => _spsLabs.Select(l => (l.Min, l.Max, l.Center)).ToList();

    /// <summary>Test/util: the planet-machine cap for a number of surface players on the active world.</summary>
    public int PlanetEnemyCapForTest(int targets) => PlanetEnemyCap(targets);

    /// <summary>Test/util: whether a position counts as inside a lab module.</summary>
    public bool InSpsLabForTest(Vector3f pos) => InSpsLab(pos);
}
