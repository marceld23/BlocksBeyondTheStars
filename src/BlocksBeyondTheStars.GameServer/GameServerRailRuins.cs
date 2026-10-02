// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Abandoned monorail stations (#2166, Justus' "Verlassene Bahnhöfe!" from 2026-09-27; terrain generation 20). On some worlds
/// the ruin of an old station hall stands out in the open country — what it looks like is <see cref="RailRuinGenerator"/>'s
/// business; this partial decides, places, stamps and remembers it.
/// <list type="bullet">
/// <item><b>Which worlds.</b> A world that could once have had towns: ground underfoot (not void, not a gas world), an
/// atmosphere (not airless), no structure whitelist. At most one per world, with the chance
/// <c>ServerConfig.RailRuinChance</c> (0.35) scaled by the structures-frequency option, rolled once on a lane of its own
/// (<see cref="LaneRoll"/>, seed + body id).</item>
/// <item><b>Placement.</b> Decided once and pinned in the placement records (<c>rail_ruin</c>/0 with the heading in the
/// template; a "no" is pinned as a skip), right after the standalone ruins: on dry land (no stilts, no lava), clear of
/// the pads, the wreck site, the settlements, the intercity line and the ruins of this load. Every later stamper keeps
/// clear of it (<see cref="AppendRailRuinReservations"/>, <see cref="OverlapsRailRuin"/>).</item>
/// <item><b>Voxels once.</b> Stamped through the settlement seat pipeline once ever (feature <c>railruins</c>) and NOT
/// protected: like every ruin it is plain terrain afterwards, freely mineable, and a mined wall stays mined.</item>
/// <item><b>Salvage and lore.</b> Its two <see cref="RailRuinGenerator.CacheMarker"/> markers become one-time loot
/// containers (old line parts, sometimes salvaged pylons or a stop block — enough to start a line of one's own); looting
/// one can reveal a station notice (lore site <c>rail_ruin</c>).</item>
/// <item><b>Finding it.</b> Not on the map — discovery content, like the ruins. VEGA mentions it within 120 blocks
/// ("Ruins nearby — Abandoned station"), and an admin can <c>/tp railruin</c>.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const string RailRuinKind = "rail_ruin";
    private const string RailRuinFeature = "railruins";

    private List<RailRuinInstance> _railRuins => _worlds.Active.RailRuins;

    /// <summary>Whether a world could once have had a station: ground underfoot, air to breathe, no structure whitelist.</summary>
    private static bool RailRuinEligible(PlanetType planet)
        => !planet.Void && !planet.IsAirless && !planet.IsGasWorld && !planet.RestrictStructures;

    /// <summary>Decides (once, pinned), stamps (once) and re-derives this world's abandoned station. Runs right after the ruins.</summary>
    private void StampRailRuins()
    {
        _railRuins.Clear();
        var planet = _world.Planet;
        if (planet is null || planet.Void)
        {
            return;
        }

        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        long instSeed = _meta.Seed ^ WorldGenerator.StableHash("railruin:" + _world.LocationId) ^ unchecked((long)0x9E3779B97F4A7C15);

        int heading;
        Vector3i origin;
        int groundY;
        string seat;
        var rec = FindPlacementRecord(RailRuinKind, 0);
        if (rec is null)
        {
            if (!_config.PlaceRailRuins || _generator.TerrainGeneration < WorldDescription.RailRuinGeneration || !RailRuinEligible(planet))
            {
                return; // an older world never grows one; a server that switched the feature off decides nothing
            }

            double factor = _meta.Description.Settlements.StructureFactor();
            double chance = Math.Clamp(_config.RailRuinChance * Math.Clamp(factor, 0.0, 2.0), 0.0, 1.0);
            if (LaneRoll(_meta.Seed, "railruin:" + _world.LocationId) >= chance)
            {
                RecordPlacementSkip(RailRuinKind, 0);
                SavePlacementRecords();
                return;
            }

            heading = RngFor(instSeed, "heading").Next(4);
            var fresh = RailRuinGenerator.Generate(instSeed, heading, surface, _content);
            if (!TryPlaceStructureGuaranteed(fresh, RngFor(instSeed, "search"), RailRuinReservedFootprints(), wantIsland: false,
                    SeatPolicy.Factory, avoidPlayerEdits: !_worlds.Active.VirginAtLoad,
                    out origin, out groundY, out bool onIsland, out seat))
            {
                RecordPlacementSkip(RailRuinKind, 0);
                SavePlacementRecords();
                ReportStamp(RailRuinKind, 1, 0);
                return;
            }

            RecordPlacement(RailRuinKind, 0, origin, groundY, onIsland, seat, RailRuinKind,
                template: "heading=" + heading.ToString(CultureInfo.InvariantCulture));
            SavePlacementRecords();
        }
        else if (!rec.Placed)
        {
            return;
        }
        else if (!rec.Template.StartsWith("heading=", StringComparison.Ordinal)
                 || !int.TryParse(rec.Template.Substring("heading=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out heading)
                 || heading is < 0 or > 3)
        {
            _log.Warn($"The abandoned-station record of '{_world.LocationId}' could not be replayed — it stays off this load.");
            return;
        }
        else
        {
            origin = new Vector3i(rec.X, rec.GroundY, rec.Z);
            groundY = rec.GroundY;
            seat = rec.Seat;
        }

        var structure = RailRuinGenerator.Generate(instSeed, heading, surface, _content);
        _railRuins.Add(new RailRuinInstance
        {
            Origin = new Vector3i(origin.X, groundY, origin.Z),
            Heading = heading,
            Width = structure.Width,
            Length = structure.Length,
            Seat = seat,
        });

        if (!FeatureStamped(RailRuinFeature))
        {
            var placed = new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = RailRuinGenerator.Tier,
                Ruined = true,
                OnIsland = false,
                Name = RailRuinKind,
                Rng = RngFor(instSeed, "stamp"),
                Seat = seat,
            };
            _repo.RunInTransaction(() => StampSettlementBlocks(placed, surface));
            MarkFeatureStamped(RailRuinFeature); // after the transaction: a crash mid-stamp re-stamps, never half-marks
        }

        // The salvage: one-time containers (idempotent across loads — a looted cache never comes back).
        var lootRng = RngFor(instSeed, "loot");
        foreach (var m in structure.Markers)
        {
            if (m.Type == RailRuinGenerator.CacheMarker)
            {
                var pos = new Vector3f(origin.X + m.LocalPos.X + 0.5f, groundY + m.LocalPos.Y + 0.5f, origin.Z + m.LocalPos.Z + 0.5f);
                SpawnStructureLoot(RailRuinKind, m.Type, pos, lootRng);
            }
        }

        ReportStamp(RailRuinKind, 1, 1);
        _log.Info($"Abandoned monorail station on '{_world.LocationId}' at ({origin.X}, {groundY}, {origin.Z}), heading {heading}, seat {seat}.");
    }

    /// <summary>What the abandoned station keeps clear of when it is placed: the pads, the wreck site, every settlement, the
    /// intercity line and the ruins stamped during this load.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> RailRuinReservedFootprints()
    {
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        reserved.Add((wreckX, wreckZ, WreckReservedHalfExtent, WreckReservedHalfExtent));
        foreach (var s in _settlements)
        {
            reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2, (s.Max.X - s.Min.X) / 2 + 1, (s.Max.Z - s.Min.Z) / 2 + 1));
        }

        AppendIntercityReservations(reserved);
        reserved.AddRange(_worlds.Active.RuinFootprints);
        return reserved;
    }

    /// <summary>Adds the abandoned station's footprint to a later stamper's reserved footprints (#2166).</summary>
    private void AppendRailRuinReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        foreach (var r in _railRuins)
        {
            reserved.Add(r.Rect);
        }
    }

    /// <summary>Whether a point (or a small area around it) lies on the abandoned station (#2166) — the surface stampers
    /// that ask <see cref="OverlapsAnySettlement"/> keep clear of it the same way.</summary>
    private bool OverlapsRailRuin(int x, int z, int halfExtent)
        => _railRuins.Count > 0
           && OverlapsFootprint(x, z, halfExtent, halfExtent, _railRuins.Select(r => r.Rect).ToList(), SettlementCollisionMargin);

    /// <summary>A standing spot in the abandoned station for <c>/tp railruin</c>: on the platform in the middle of the hall,
    /// climbing out of whatever rubble lies there.</summary>
    private Vector3f RailRuinSpot(RailRuinInstance r)
    {
        int u = RailStationGenerator.Length / 2;
        int v = 2;
        var (lx, lz) = RailRuinGenerator.ToStructure(r.Heading, u, v);
        int x = r.Origin.X + lx, z = r.Origin.Z + lz;
        int y = r.Origin.Y + 1;
        for (int i = 0; i < TeleportClearProbeHeight; i++, y++)
        {
            if (_world.GetBlock(new Vector3i(x, y, z)).IsAir && _world.GetBlock(new Vector3i(x, y + 1, z)).IsAir)
            {
                break;
            }
        }

        return new Vector3f(x + 0.5f, y, z + 0.5f);
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam (#2166): the abandoned stations of the active world (0 or 1) — origin (Y = the floor row), heading,
    /// footprint and seat.</summary>
    public IReadOnlyList<(Vector3i Origin, int Heading, int Width, int Length, string Seat)> RailRuinsForTest()
        => _railRuins.Select(r => (r.Origin, r.Heading, r.Width, r.Length, r.Seat)).ToList();

    /// <summary>Test seam (#2166): the chance roll of a world and body for its abandoned station.</summary>
    public static double RailRuinRollForTest(long worldSeed, string locationId) => LaneRoll(worldSeed, "railruin:" + locationId);
}
