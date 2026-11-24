// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Creature spawning (<see cref="TrySpawnCreatureNear"/>): the spawn ring around a player, water and lava columns for
/// aquatic and lava life, the spawn-spot and habitat checks, the per-species share of the cap, herds placed around a
/// leader — and the clear-spot search that moves a boxed-in animal out of the blocks.
/// </summary>
public sealed partial class GameServer
{
    // Offsets scattered around the player at ~18-45 blocks so fauna appears spread out across the
    // surroundings — not stacked on one spot and not right on top of the player's ship/landing site.
    // Mixed radii and angles (an inner, a mid and an outer band) keep encounters from feeling ringed.
    private static readonly (int Dx, int Dz)[] SpawnRing =
    {
        (18, 5), (13, 16), (3, 22), (-12, 18), (-21, 7), (-19, -12), (-9, -20), (8, -19),
        (28, 10), (15, 27), (-8, 31), (-30, 16), (-33, -14), (-14, -30),
        (12, -32), (34, -18), (40, 12), (-24, 38), (-42, -16), (16, -41),
    };

    // Outward probe offsets (radius 0..8) used to find a water column near a dry ring spot, so aquatic life
    // can spawn in a lake/sea the player is standing beside even though the chosen ring cell is on land.
    private static readonly (int Dx, int Dz)[] WaterProbe =
    {
        (0, 0),
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
        (3, 0), (-3, 0), (0, 3), (0, -3), (3, 3), (-3, 3), (3, -3), (-3, -3),
        (5, 0), (-5, 0), (0, 5), (0, -5), (5, 5), (-5, 5), (5, -5), (-5, -5),
        (8, 0), (-8, 0), (0, 8), (0, -8),
    };

    /// <summary>Finds the nearest water column (global sea, upland pond — or a pool the player built) to a
    /// spot, returning its coordinates and the water-surface / seabed Y. False if no water is within the
    /// probe radius. A column holding fewer than <paramref name="minDepth"/> water cells is passed over (Lyxette report
    /// 2026-11-18: water life asks for <see cref="CreatureBehaviour.MinSwimDepth"/> — a fish seated in one-cell water was
    /// set on the floor beside it by the swimmer's height probe, which calls one cell a puddle), and one holding more than
    /// <paramref name="maxDepth"/> (#3975: a dolphin keeps to the coastal shallows). <paramref name="seaOnly"/> (#4017) passes
    /// over every column the world's sea does not cover (<see cref="WorldGenerator.IsSeaColumn"/>) — a pond, a lake, a river.</summary>
    private bool TryFindWaterColumnNear(int x, int z, out int wx, out int wz, out int waterTopY, out int seabedY, int minDepth = 1,
        int maxDepth = int.MaxValue, bool seaOnly = false)
    {
        foreach (var (dx, dz) in WaterProbe)
        {
            if (TryGetFluidColumn(x + dx, z + dz, _creatureWaterId, out waterTopY, out seabedY)
                && waterTopY - seabedY >= minDepth && waterTopY - seabedY <= maxDepth
                && (!seaOnly || _generator.IsSeaColumn(_world.Planet, x + dx, z + dz)))
            {
                wx = x + dx;
                wz = z + dz;
                return true;
            }
        }

        wx = x;
        wz = z;
        waterTopY = 0;
        seabedY = 0;
        return false;
    }

    /// <summary>The nearest water column a species may be seated in at spawn (<see cref="WaterDepthBand"/>): water life needs
    /// room to swim (Lyxette report 2026-11-18) and keeps to its own depth band (#3975), an amphibian wades at the surface cell
    /// of any water; a sea animal (#4017, <see cref="CreatureSpecies.SeaOnly"/>) only in the world's sea.</summary>
    private bool TryFindSpawnWaterNear(CreatureSpecies sp, int x, int z, out int wx, out int wz, out int waterTopY, out int seabedY)
        => TryFindWaterColumnNear(x, z, out wx, out wz, out waterTopY, out seabedY,
            WaterDepthBand.MinSpawnDepth(sp), WaterDepthBand.MaxSpawnDepth(sp), sp.SeaOnly);

    /// <summary>The molten sibling of <see cref="TryFindWaterColumnNear"/> (#470/F4): the nearest lava
    /// column — a volcano crater pool, the global lava sea, or a lava flow — around a spot.</summary>
    private bool TryFindLavaColumnNear(int x, int z, out int wx, out int wz, out int lavaTopY)
    {
        foreach (var (dx, dz) in WaterProbe)
        {
            if (TryGetFluidColumn(x + dx, z + dz, _creatureLavaId, out lavaTopY, out _))
            {
                wx = x + dx;
                wz = z + dz;
                return true;
            }
        }

        wx = x;
        wz = z;
        lavaTopY = 0;
        return false;
    }

    /// <summary>
    /// Spawns one roster species suited to a spread-out spot around the player (habitat-gated, on the
    /// ground) — or a whole herd/school/flock of it when the species is social (#639). Returns true if
    /// at least one spawned. Two independent rotors (#470): the ring rotor walks all 20 scatter offsets
    /// (advancing even on failure), the species rotor walks the roster on success. <paramref name="cap"/>
    /// is the world's live cap — every group member counts against it individually, so a group that no
    /// longer fits spawns partially, never over the cap.
    /// </summary>
    private bool TrySpawnCreatureNear(Shared.State.PlayerState player, int cap)
    {
        _spawnAttemptsForTest++;
        cap = System.Math.Min(cap, CreatureHardCap);
        if (WildCreatureCount >= cap || RawWildCount >= CreatureHardCap) // #2018: the budget is weighted, the hard cap is bodies
        {
            return false;
        }

        // #470/F2+F3: the ring slot has its OWN rotor (the species rotor runs 0..rosterLength, which kept
        // this index below 6 forever — only the first arc of the 20 offsets was ever used, fauna always
        // appeared from the same direction and the outer band was dead), and it advances on EVERY attempt —
        // a failed spot no longer stalls the spawner on the identical column until the player moves.
        var (dx, dz) = SpawnRing[_creatureRingRotor % SpawnRing.Length];
        _creatureRingRotor = (_creatureRingRotor + 1) % SpawnRing.Length;
        int x = (int)System.Math.Floor(player.Position.X) + dx;
        int z = (int)System.Math.Floor(player.Position.Z) + dz;
        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        LoadSpawnGround(x, z, surface); // #2960: judge the spot by its real blocks, never by an unloaded chunk's air
        // #3575: the biome in the planet TYPE's numbering — the one a species' affinity counts in — and which of the type's
        // biomes this world carries at all (a species whose biome is missing here belongs nowhere in particular).
        int biome = _generator.BiomePoolIndexAt(_world.Planet, x, z);
        var poolOnWorld = _generator.PoolBiomesOnWorld(_world.Planet);
        int share = SpeciesShare(cap);

        // Two passes: first only species native to this biome (so a multi-biome world shows different fauna in
        // different regions), then any species — so a biome never goes empty if none of its natives fit here.
        // A biome with a single native skips the native pass outright (#1325): otherwise every spawn in that
        // region was that one species, and a base there drowned in a monoculture up to the world cap.
        for (int pass = CreatureBehaviour.BiomePassStarved(_speciesRoster, biome, poolOnWorld) ? 1 : 0; pass < 2; pass++)
        {
            for (int n = 0; n < _speciesRoster.Length; n++)
            {
                var sp = _speciesRoster[(_creatureSpawnRotor + n) % _speciesRoster.Length];
                if (sp.Id == SreekmakraSpeciesId)
                {
                    continue; // 2026-09: the shapeshifter never spawns as an animal — TickSreekmakra places the one
                }

                if (pass == 0 && !CreatureBehaviour.NativeHere(sp.BiomeAffinity, biome, poolOnWorld))
                {
                    continue; // not native to this biome (relaxed on the second pass)
                }

                if (WeightedWildCountOf(sp) >= share)
                {
                    continue; // this species already holds its share of the world (#1325) — let another fill the cap
                }

                if (HerdRules.IsBigHerd(sp) && WildCountOf(sp.Id) > 0)
                {
                    continue; // #2018: one herd at a time — a second one only once the first has been pruned away
                }

                if (HoundAlreadyAbout(sp))
                {
                    continue; // #3326: the Schuppenhund lives alone — one about at a time
                }

                if (SoundersCapReached(sp))
                {
                    continue; // #3359: one Soundrak guards its patch alone, a few Qualis drift in the sky
                }

                float y;
                int px = x, pz = z; // the actual spawn column (water species relocate to nearby water)
                if (sp.Habitat == CreatureHabitat.Cave)
                {
                    int caveY = FindCaveFloorY(x, z, surface);
                    if (caveY < 0)
                    {
                        continue; // no open cave under this spot — try another species/spot
                    }

                    y = caveY;
                }
                else if (sp.Habitat == CreatureHabitat.Water || sp.Habitat == CreatureHabitat.Amphibian)
                {
                    // Aquatic life must spawn IN water. The ring spot is usually dry land, and the visible lakes
                    // (upland ponds) fill flush to the surface — so probing surface+1 always hit air and water
                    // creatures never spawned. Seek the nearest water column (sea/pond) around the spot and place
                    // inside it; skip the species if there's no water nearby (no land fallback).
                    if (!TryFindSpawnWaterNear(sp, x, z, out px, out pz, out int waterTopY, out int seabedY))
                    {
                        continue;
                    }

                    // Swimmers sit mid-water; amphibians wade up at the surface cell (still counts as water).
                    y = sp.Habitat == CreatureHabitat.Water
                        ? (seabedY + 1 + waterTopY) * 0.5f
                        : waterTopY;
                }
                else if (sp.Habitat == CreatureHabitat.Lava)
                {
                    // #470/F4: lava fauna needs the same courtesy water got — the ring spot is practically
                    // never molten itself (surface+1 is air above the melt), so these species were rolled
                    // into rosters but never appeared. Seek the nearest lava column (crater pool, lava sea
                    // or flow) and bask at its surface; skip the species when no melt is nearby.
                    if (!TryFindLavaColumnNear(x, z, out px, out pz, out int lavaTopY))
                    {
                        continue;
                    }

                    y = lavaTopY;
                }
                else
                {
                    // Land spawns stand on the REAL ground (#650) — a dug pit's floor or a built platform,
                    // not the generator's original surface (which would float them over player terraforming).
                    // Air spawns start at their own band over it (#2552) — over a pond, over the water.
                    y = sp.Habitat == CreatureHabitat.Air ? AirSpawnY(sp, x, z, surface) : GroundFeetYAt(x, z, surface + 1, canopyCarries: false);
                }

                var pos = new Vector3f(px + 0.5f, y, pz + 0.5f);
                if (!SpawnSpotClear(sp, pos, px, pz, surface))
                {
                    continue;
                }

                _creatureSpawnRotor = (_creatureSpawnRotor + n + 1) % _speciesRoster.Length;
                SpawnCreature(sp, pos);
                SpawnGroupAround(sp, px, pz, cap); // social species (#639) bring their herd/school/flock
                return true;
            }
        }

        return false;
    }

    /// <summary>The one reject list every spawn placement runs — the ring leader and each herd member
    /// alike (#1314: the two used to drift apart, and a gate added to one leaked through the other):
    /// habitat, the parked-ship volume (body-aware for large species, #1320), the body cells (#855), titan
    /// flatness (#638), the large-body volume (#750) and — new — a founded base's SEALED rooms (#1314): the
    /// spawner never consulted the base systems, so a room the player had made airtight still filled with
    /// wildlife. <see cref="InSealedBaseRoom"/> is a cached set lookup, effectively free. Last, #2753: another animal's
    /// footprint (<see cref="CrowdedByCreatures"/>).</summary>
    private bool SpawnSpotClear(CreatureSpecies sp, Vector3f pos, int px, int pz, int surface)
    {
        var cell = pos.ToBlock();

        if (sp.Habitat != CreatureHabitat.Air && IsNearBaseCore(cell))
        {
            // Reject wild spawns within BaseSpawnExclusionRadius blocks of any base_core on the current world.
            return false;
        }

        // #3453: nor in a town — no ground animal on its roofs or in its lanes, no flier under its roofs (GameServerCreatureTownRules).
        if (TownSpawnBlocked(sp, pos))
        {
            return false;
        }

        // #3658: nor in a train's wagon or (one tall enough to reach the wagons) on a running line (GameServerRailWildlife).
        if (RailSpawnBlocked(sp, pos))
        {
            return false;
        }

        if (!HabitatSuitable(sp, pos) || EntityBlockedByShip(pos, CreatureShipMargin(sp)))
        {
            // Reject the SAME volume the movement barrier guards (not just the tight interior box), so a
            // creature never spawns in the thin shell where it would immediately be frozen against the hull.
            return false; // never spawn inside (or clipping into) a landed ship
        }

        // #2583: nor where the gas sea burns. A gas giant's animals all fly, and a band measured from the heightfield buried
        // under the gas put 91 of 102 spawns into it — the burn-off kept the spawner refilling the cap for good. Every
        // placement runs this list, so a band that ever dips into the gas can no longer feed that loop.
        if (InGasSea(pos))
        {
            return false;
        }

        if (CreatureBodyBlocked(sp, pos) || BodySpawnBlocked(sp, pos))
        {
            return false; // its body would materialise inside a wall / ruin masonry (#855), its wings in one (#2943), on a crown (#2945)
        }

        // #1763: an exclusive species (Leni: snow and ice) stands only on its own ground — the real block under
        // its feet where the column is streamed in, the generator's biome surface where it is not.
        if (sp.BiomeExclusive && !OnExclusiveGround(sp, cell))
        {
            return false;
        }

        // Titans need level ground (#638): a 3×3 clearance whose surface stays within ±1 of the
        // centre column, so a six-block giant doesn't materialise half-buried in a cliff face —
        // creatures have no colliders, so the spawn spot is the only terrain check they ever get.
        if (sp.BodyPlan == CreatureBodyPlan.Titan && !TitanGroundClear(px, pz, surface))
        {
            return false;
        }

        // A big body must actually FIT (#750): flatness alone let titans materialise inside
        // ruin rooms (stamped floors are perfectly flat) — the only other spatial gate was a
        // single 1×1 column with two air cells, for bodies that render ~5×10×11 blocks.
        if (sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land
            && !LargeBodyFits(sp, px, (int)System.Math.Floor(pos.Y), pz))
        {
            return false;
        }

        // #1747: a room a player built is not a cave. The cave probe accepts any air pocket under the surface
        // with a floor and headroom — which a hall carved out of a natural cave still is, seventy blocks from
        // the base core where neither the spawn exclusion (24) nor the sealed-room fill (48) reaches, and the
        // walled-yard gate below exempts cave dwellers on purpose (#1315). The player's own block edits around
        // the spot settle it; only cave candidates pay the lookup.
        if (sp.Habitat == CreatureHabitat.Cave && PlayerBuiltPocket(cell))
        {
            return false;
        }

        // #2521: nor on a block a player placed — a floor, a deck, a paved yard. One indexed lookup, independent of
        // the walled-yard fill below (no budget, no unloaded wall columns, no gap in the wall to leak through), and
        // the plain answer to "does the floor matter?". Land animals and amphibians ashore only: water and lava life is
        // never on a floor (a moat, an aquarium), fliers and cave dwellers keep their exemption, as with walls.
        if (FloorRuleApplies(sp) && OnPlayerBuiltFloor(pos))
        {
            return false;
        }

        // #1314: nothing spawns inside a base's sealed rooms — the volume the air fill already knows.
        if (InSealedBaseRoom(cell))
        {
            return false;
        }

        // #1315: nor inside a WALLED area of a base — an open-topped yard the outside-in fill cannot reach.
        // Ground-bound life only: a flier spawns above the wall, a cave dweller below it.
        if (sp.Habitat is not (CreatureHabitat.Air or CreatureHabitat.Cave) && InWalledBaseArea(cell))
        {
            return false;
        }

        // #3148: …except over the yards of a base that uses energy fences — its fence guards the air above it, so a flier
        // born inside would be penned in for good.
        if (sp.Habitat == CreatureHabitat.Air && InFencedBaseYard(pos))
        {
            return false;
        }

        // #2753: nor inside another animal's body — a herd member two blocks from the leader, a neighbouring herd, a summon.
        return !CrowdedByCreatures(sp, pos);
    }

    /// <summary>#1763: whether the ground under a spawn cell is one of the species' <see cref="CreatureSpecies.BiomeSurfaces"/>
    /// or (#3973) lies in a biome of one of its climate zones (<see cref="CreatureSpecies.FloraThemes"/>) — the one rule of
    /// <see cref="CreatureBiomes.OnOwnGround"/>. A species that names neither never rejects.</summary>
    private bool OnExclusiveGround(CreatureSpecies sp, Vector3i cell)
    {
        bool surfaces = sp.BiomeSurfaces is { Length: > 0 }, zones = sp.FloraThemes is { Length: > 0 };
        if (!surfaces && !zones)
        {
            return true;
        }

        string theme = zones ? CreatureBiomes.ThemeAt(_world.Planet, _generator.BiomePoolIndexAt(_world.Planet, cell.X, cell.Z)) : string.Empty;
        return CreatureBiomes.OnOwnGround(sp, surfaces ? GroundKeyAt(cell) : null, theme);
    }

    /// <summary>The block key of the ground under a spawn cell: real blocks first (a snow field the player paved over is not
    /// Leni country any more); where the column is not loaded, the generator's biome surface answers.</summary>
    private string? GroundKeyAt(Vector3i cell)
    {
        string? ground = null;
        if (_world.IsChunkLoaded(WorldConstants.WorldToChunk(cell)))
        {
            for (int dy = 1; dy <= 3 && ground is null; dy++)
            {
                var below = _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y - dy, cell.Z));
                if (!below.IsAir)
                {
                    ground = _content.BlockById(below)?.Key;
                }
            }
        }

        return ground ?? _generator.BiomeSurfaceKeyAt(_world.Planet, cell.X, cell.Z);
    }

    /// <summary>#1747: player block edits in the 3×5×3 box around a cave spawn spot — the floor row, the two
    /// cells of body room and the ceiling above. A built floor, a built ceiling or a built wall each make the
    /// pocket a room, not a cave.</summary>
    private bool PlayerBuiltPocket(Vector3i cell)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        return _repo.HasPlayerBlockEdits(_world.LocationId,
            new Vector3i(c.X - 1, c.Y - 1, c.Z - 1), new Vector3i(c.X + 1, c.Y + 3, c.Z + 1));
    }

    /// <summary>How many live wild individuals of one species a world may hold (#1325): a share of the
    /// live cap, floored at <see cref="SpeciesShareMin"/> (a herd must still be possible on a sparse
    /// world) and never below cap ÷ roster size (a two-species world must still reach its cap).</summary>
    private int SpeciesShare(int cap)
    {
        int roster = System.Math.Max(1, _speciesRoster.Length);
        int share = (int)System.Math.Ceiling(cap * SpeciesShareOfCap);
        return System.Math.Max(SpeciesShareMin, System.Math.Max(share, (cap + roster - 1) / roster));
    }

    /// <summary>What the species' live wild animals cost against its share (#2018): every animal, or one per
    /// <see cref="HerdRules.BigHerdCapDivisor"/> for a big herd. The spawner and the crowding prune MUST both read this one —
    /// a prune on the raw count would shed the herd the spawner just placed.</summary>
    private int WeightedWildCountOf(CreatureSpecies sp) => HerdRules.WeightedCount(sp, WildCountOf(sp.Id));

    /// <summary>Live WILD individuals of one species (companions never count, as with the world cap).</summary>
    private int WildCountOf(string speciesId)
    {
        int n = 0;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion && c.SpeciesId == speciesId)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>The footprint radius a species adds to the parked-ship guard (#1320): large species keep
    /// their body clear of the hull, small fauna keeps the plain point test (fliers still cross over).</summary>
    private static float CreatureShipMargin(CreatureSpecies sp)
        => sp.Size >= LargeBodySize && sp.Habitat != CreatureHabitat.Air ? sp.Size * 0.5f : 0f;

    /// <summary>3×3 flatness gate for titan spawns (#638): every neighbouring column's ground must sit
    /// within ±1 block of the centre's. Reads REAL blocks (#650), so a titan neither materialises against
    /// a player wall nor half-buried in an edited cliff face.</summary>
    private bool TitanGroundClear(int x, int z, int centerSurface)
    {
        int centerFeet = GroundFeetYAt(x, z, centerSurface + 1);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int feet = GroundFeetYAt(x + dx, z + dz, centerFeet);
                if (System.Math.Abs(feet - centerFeet) > 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Places the rest of a social species' group (#639) around a just-spawned member: up to
    /// <see cref="CreatureSpecies.SocialGroupSize"/> − 1 more individuals, 4–11.5 blocks out on spread
    /// golden-angle bearings (further for a big body, <see cref="HerdRingScale"/>), each habitat-gated and each
    /// counting against the cap — unsuitable spots
    /// are simply skipped, so a group spawns partially rather than forcing bad placements.</summary>
    private void SpawnGroupAround(CreatureSpecies sp, int x, int z, int cap)
    {
        int group = System.Math.Clamp(sp.SocialGroupSize, 1, HerdRules.MaxGroupSize); // #2018: was 5 before the big herds
        int share = SpeciesShare(cap);
        float scale = HerdRingScale(sp, group); // #2753: 1 for every body that fits the classic rings
        for (int k = 1; k < group && WildCreatureCount < cap && RawWildCount < CreatureHardCap && WeightedWildCountOf(sp) < share; k++)
        {
            // Golden-angle bearings with alternating radii — spread out, not a neat ring. #2018: four radii out to 11.5
            // blocks so a herd of twelve has room, and a member whose spot is taken by a wall or a pond tries once more a
            // little further round — rough ground otherwise left a "herd of twelve" at seven. #2753: the whole pattern
            // grows with a big body, so a titan herd keeps its members instead of losing them to the footprint check.
            double a = k * 2.399963;
            float dist = (4f + (k % 4) * 2.5f) * scale;
            if (!TryPlaceGroupMember(sp, x, z, a, dist))
            {
                TryPlaceGroupMember(sp, x, z, a + 1.2, dist + 1.5f * scale);
            }
        }
    }

    /// <summary>One herd member's placement at bearing <paramref name="a"/> and distance <paramref name="dist"/> from the leader:
    /// the habitat probe from its own spot and the leader's full reject list. False when the spot will not do.</summary>
    private bool TryPlaceGroupMember(CreatureSpecies sp, int x, int z, double a, float dist)
    {
        int mx = x + (int)System.Math.Round(System.Math.Cos(a) * dist);
        int mz = z + (int)System.Math.Round(System.Math.Sin(a) * dist);
        int surface = _generator.SurfaceHeight(_world.Planet, mx, mz);
        LoadSpawnGround(mx, mz, surface); // #2960: a member's own column, as the leader's

        float y;
        if (sp.Habitat == CreatureHabitat.Water || sp.Habitat == CreatureHabitat.Amphibian)
        {
            // #1718: a member runs the leader's probe from its own spot. It used to ask only its own column,
            // so beside a small pond the golden-angle spots landed on the bank and the school was the leader
            // alone — the water four blocks away was never looked at.
            if (!TryFindSpawnWaterNear(sp, mx, mz, out mx, out mz, out int waterTopY, out int seabedY))
            {
                return false; // no water near this member's spot — the school stays smaller
            }

            surface = _generator.SurfaceHeight(_world.Planet, mx, mz);
            y = sp.Habitat == CreatureHabitat.Water ? (seabedY + 1 + waterTopY) * 0.5f : waterTopY;
        }
        else if (sp.Habitat == CreatureHabitat.Lava)
        {
            if (!TryFindLavaColumnNear(mx, mz, out mx, out mz, out int lavaTop))
            {
                return false;
            }

            surface = _generator.SurfaceHeight(_world.Planet, mx, mz);
            y = lavaTop;
        }
        else if (sp.Habitat == CreatureHabitat.Cave)
        {
            int caveY = FindCaveFloorY(mx, mz, surface);
            if (caveY < 0)
            {
                return false;
            }

            y = caveY;
        }
        else
        {
            y = sp.Habitat == CreatureHabitat.Air ? AirSpawnY(sp, mx, mz, surface) : GroundFeetYAt(mx, mz, surface + 1, canopyCarries: false); // real ground (#650), the band (#2552), never a crown (#2945)
        }

        // Herd members run the leader's full reject list (#638/#750/#855/#1314): the herd stays smaller
        // rather than planting a member inside a wall, a ship, or a sealed base room.
        var pos = new Vector3f(mx + 0.5f, y, mz + 0.5f);
        if (!SpawnSpotClear(sp, pos, mx, mz, surface) || SoundersCapReached(sp)) // #3359: a herd never overfills the Qualis' sky
        {
            return false;
        }

        SpawnCreature(sp, pos);
        return true;
    }

    /// <summary>Adds a live creature of the species at the position.</summary>
    private void SpawnCreature(CreatureSpecies sp, Vector3f pos)
    {
        string id = NextEntityId();
        _creatures.Add(new CombatEntity
        {
            Id = id,
            Kind = sp.Hostile ? CombatEntityKind.AlienMonster : CombatEntityKind.Creature,
            SpeciesId = sp.Id,
            Hostile = sp.Hostile,
            Hull = sp.MaxHealth,
            HullMax = sp.MaxHealth,
            Position = pos,
            DamagePerSecond = sp.AttackDamage,
            AnimalSeed = CoatVariants.AnimalSeedOf("wild:" + id), // #3972: picks a coat variant, like the size below
            SizeScale = Shared.Sounders.SoundersFauna.SizeScaleFor(sp, FaunaSizeScale(id)), // this individual's own size (cosmetic; #3359: the giants of Soaniri vary less)
            Loot = { new ItemAmount(sp.DropItem, sp.DropCount) },
        });
    }

    /// <summary>A per-individual COSMETIC size factor (a "bell" centred on 1.0, ±30% — the average of two
    /// pseudo-randoms is triangular, so most animals are about normal and runts/giants are rare). Derived
    /// from the entity id so it stays stable for that individual. Does not change health/damage/loot.</summary>
    /// <remarks>#2687: the two uniforms are the two 32-bit halves of the well-mixed <see cref="StableHashing"/> hash.
    /// The old h·31 string hash moved by only a few units per trailing digit of <c>e{n}</c>, so a herd of six
    /// consecutive spawns came out the same size and the top of the bell (the giants) never occurred.</remarks>
    private static float FaunaSizeScale(string id)
    {
        ulong m = StableHashing.Mixed("fauna-size:" + id);
        double a = (uint)m / 4294967296.0;
        double b = (uint)(m >> 32) / 4294967296.0;
        double t = (a + b) * 0.5;
        return (float)(1.0 + (t - 0.5) * 2.0 * 0.30);
    }

    /// <summary>Test seam (#2687): the cosmetic size factor an individual with this entity id is spawned with.</summary>
    internal static float FaunaSizeScaleForTest(string id) => FaunaSizeScale(id);

    /// <summary>
    /// Immediately seeds fauna around a spot so a world feels alive the moment a player enters or
    /// arrives (instead of trickling in one creature every few seconds). Habitat-gated + capped; no-op
    /// on barren worlds. The caller sends/broadcasts the creature list.
    /// </summary>
    private void PopulateCreaturesNear(Shared.State.PlayerState player, int count)
    {
        // World options: the join-time seeding respects the abundance rule too — at Off the world
        // stays lifeless, and at low settings the initial burst doesn't overshoot the world cap.
        int cap = WorldCreatureCap(System.Math.Max(1, JoinedInActiveWorld().Count()));
        if (_speciesRoster.Length == 0 || cap <= 0)
        {
            return;
        }

        for (int i = 0; i < count && WildCreatureCount < System.Math.Min(cap, CreatureHardCap) && RawWildCount < CreatureHardCap; i++)
        {
            TrySpawnCreatureNear(player, cap);
        }
    }

    /// <summary>Land/air creatures spawn near the player; water/lava ones only in their fluid.</summary>
    private bool HabitatSuitable(CreatureSpecies sp, Vector3f at)
    {
        switch (sp.Habitat)
        {
            case CreatureHabitat.Water:
                return BlockValueAt(at) == _creatureWaterId && _creatureWaterId != 0;
            case CreatureHabitat.Lava:
                return BlockValueAt(at) == _creatureLavaId && _creatureLavaId != 0;
            case CreatureHabitat.Cave:
                // a standable air pocket on solid ground (the spawn probe places it in a real cave); #1719: no-load
                // reads — an unloaded column has no solid floor, so it is not a cave
                return _world.GetBlockIfLoaded(at.ToBlock()).IsAir
                    && !_world.GetBlockIfLoaded(new Vector3i((int)System.Math.Floor(at.X), (int)System.Math.Floor(at.Y) - 1, (int)System.Math.Floor(at.Z))).IsAir;
            case CreatureHabitat.Amphibian:
                return BlockValueAt(at) == _creatureWaterId || WaterWithin(at, 2); // in or beside water
            case CreatureHabitat.Land:
                // #2553: never with water standing on its feet. Over open water the land placement falls back to the
                // generator surface — the pond floor — and over kelp or seagrass the floor reads as standable ground,
                // so land animals appeared on pond beds and walked along them under water. A plant in the water
                // counts as water here as in the wading gate.
                return WaterDepthAtFeet((int)System.Math.Floor(at.X), (int)System.Math.Floor(at.Z), (int)System.Math.Floor(at.Y)) == 0;
            default:
                return true; // Air
        }
    }

    private ushort BlockValueAt(Vector3f at)
        => _world.GetBlock(at.ToBlock()).Value;

    private static readonly (int Dx, int Dz)[] RelocateDirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
    };

    /// <summary>The nearest spot around <paramref name="from"/> where the species can actually stand: rings
    /// of 1..<see cref="SleeperRelocateRadius"/> blocks in eight directions, each column probed for a REAL
    /// standable feet cell with the species' own headroom, then the body, ship and large-body gates the
    /// spawner uses. Spots on the creature's own level (±1) are tried first, so an animal steps ASIDE out
    /// of a wall before it would climb onto the floor built over it. Never falls back to the noise
    /// surface — no real floor, no relocation.</summary>
    private bool TryFindClearSpotNear(CreatureSpecies sp, Vector3f from, out Vector3f spot)
    {
        int ox = (int)System.Math.Floor(from.X), oz = (int)System.Math.Floor(from.Z);
        int refY = (int)System.Math.Floor(from.Y);
        float margin = CreatureShipMargin(sp);
        bool large = sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land;
        int headroom = CreatureHeadroom(sp);
        for (int pass = 0; pass < 2; pass++)
        {
            int vertical = pass == 0 ? 1 : SleeperRelocateRadius;
            for (int r = 1; r <= SleeperRelocateRadius; r++)
            {
                foreach (var (dx, dz) in RelocateDirs)
                {
                    int x = ox + dx * r, z = oz + dz * r;
                    if (!TryGroundFeetYAt(x, z, refY, headroom, vertical, out int feet, CanopyCarries(sp)))
                    {
                        continue;
                    }

                    var cand = new Vector3f(x + 0.5f, feet, z + 0.5f);
                    if (CreatureBodyBlocked(sp, cand) || EntityBlockedByShip(cand, margin)
                        || (large && !LargeBodyFits(sp, x, feet, z)))
                    {
                        continue;
                    }

                    spot = cand;
                    return true;
                }
            }
        }

        spot = from;
        return false;
    }

    /// <summary>#3551: the water sibling of <see cref="TryFindClearSpotNear"/> — the nearest spot in REAL water where the
    /// body fits: its own column first, then rings of 1..<see cref="SleeperRelocateRadius"/> blocks in eight directions.
    /// The first pass measures each column's water from just above the body (the water it is in, a step aside); only then
    /// a second pass looks from a ring's height above it, so a pond a step higher counts, but never before the lake beside
    /// the fish. The water must be at least two cells deep — <see cref="WaterColumnY"/> calls one cell a puddle, no column
    /// to swim in — and the spot sits mid-band, where that probe holds a swimmer at rest. No water in reach: false, and the
    /// caller removes the animal — never sets it on land. No-load reads.</summary>
    private bool TryFindWaterSpotNear(CreatureSpecies sp, Vector3f from, out Vector3f spot)
    {
        int ox = (int)System.Math.Floor(from.X), oz = (int)System.Math.Floor(from.Z);
        int refY = (int)System.Math.Floor(from.Y);
        float margin = CreatureShipMargin(sp);
        for (int pass = 0; pass < 2; pass++)
        {
            int probeY = pass == 0 ? refY + 1 : refY + 1 + SleeperRelocateRadius;
            for (int r = 0; r <= SleeperRelocateRadius; r++)
            {
                for (int d = 0; d < (r == 0 ? 1 : RelocateDirs.Length); d++)
                {
                    int x = ox + (r == 0 ? 0 : RelocateDirs[d].Dx * r), z = oz + (r == 0 ? 0 : RelocateDirs[d].Dz * r);
                    if (WaterSpotIn(sp, x, z, probeY, from.Y, margin, out spot))
                    {
                        return true;
                    }
                }
            }
        }

        spot = from;
        return false;
    }

    /// <summary>One column of <see cref="TryFindWaterSpotNear"/>: the water body found from <paramref name="probeY"/>
    /// downward, the swim band's middle, accepted when it is water, within reach of <paramref name="fromY"/> and holds the
    /// body clear of every block and of a parked ship — at the middle and at the band's top, where the swimmer's wave
    /// carries it, so a fish set here is not stuck again a moment later.</summary>
    private bool WaterSpotIn(CreatureSpecies sp, int x, int z, int probeY, float fromY, float margin, out Vector3f spot)
    {
        spot = default;
        int bedFeet = SubmergedFeetYAt(x, z, probeY, FluidColumnScan);
        if (bedFeet == int.MinValue)
        {
            return false;
        }

        int top = FluidTopAt(x, z, bedFeet);
        if (top <= bedFeet)
        {
            return false; // one cell: a puddle (WaterColumnY's rule) — a fish set there is ashore the next tick
        }

        var cand = new Vector3f(x + 0.5f, bedFeet + ((top - 0.5f - bedFeet) * 0.5f), z + 0.5f);
        if (System.Math.Abs(cand.Y - fromY) > SleeperRelocateRadius || !HabitatSuitable(sp, cand)
            || CreatureBodyBlocked(sp, cand) || CreatureBodyBlocked(sp, new Vector3f(cand.X, top - 0.5f, cand.Z))
            || EntityBlockedByShip(cand, margin))
        {
            return false;
        }

        spot = cand;
        return true;
    }
}
