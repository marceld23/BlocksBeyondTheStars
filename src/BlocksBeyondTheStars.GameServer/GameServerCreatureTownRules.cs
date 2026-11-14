// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Wild animals and towns (#3453). A generated settlement is the villagers' home, not a pasture — but nothing kept the
/// wildlife out: the spawn reject list (<see cref="SpawnSpotClear"/>) knew bases, ships and player floors, not
/// settlements, and a land spawn stands on the highest solid block of its column, which in a town is a roof (the herd
/// members run the same list, so a whole herd appeared on the rooftops). The step barriers (<see cref="StepBlocked"/>)
/// knew hulls, fences, doors and terrain, so a roaming, fleeing, hunting or herding animal walked into the lanes from
/// the open land round the town, and one dropped off a roof walked on in the streets.
/// <para><b>The town zone.</b> Every intact settlement's box and every lodge's footprint (#2640), grown by
/// <see cref="TownWildMargin"/> blocks on each side; vertically from <see cref="TownWildMargin"/> blocks under the
/// foundation row to <see cref="TownWildMargin"/> above the box top (the 48-block plinth under a town is not part of it,
/// so a cave well under a town stays wild). Ruins stay wild ground, as does the intercity line between the towns. X wraps
/// on the world circumference. Read straight from the settlement and lodge lists — a handful of boxes per world, no cache
/// to keep in sync — and a world without settlements or lodges leaves at the first check, so it behaves exactly as before.</para>
/// <list type="bullet">
/// <item><b>Spawn</b> (<see cref="TownSpawnBlocked"/>, one line of <see cref="SpawnSpotClear"/>, so the ring leader, each
/// herd member and every summon that asks the list obey it): no land animal, no amphibian ashore and no cave dweller in
/// the zone. Fliers may spawn over a town, never under one of its roofs: in the zone the air above the flier up to the
/// box top must be open. Water and lava life keep their own rules.</item>
/// <item><b>Movement</b> (<see cref="TownStepBlocked"/>, one line of <see cref="StepBlocked"/>): a wild ground animal's
/// step into the zone — or one column deeper into it — is a wall, so it steers round and turns away exactly as at a cliff
/// or a fence (#651). An animal already inside (an older world, a lure that ended in the lanes) is never trapped: a step
/// out, or along at the same depth, is always allowed. Hostiles stop at the edge too — a hunter, a provoked animal or a
/// pack keeps outside the zone while its prey stands in town (towns are safe ground; the margin keeps a lunge off the
/// lanes), and the chase gives up on its usual clock.</item>
/// <item><b>Already inside</b>: the intruder sweep (<see cref="SweepBaseIntruders"/>) removes a wild ground animal that
/// has been in the zone for <see cref="TownStayGraceSeconds"/> while no player is looking at it — time enough to walk out
/// on its own first.</item>
/// <item><b>Exempt</b> (<see cref="TownRuleExempt"/>): companions — a player's pet, a tamer's pet (the only animal a
/// settlement keeps), the robot, Rudolpho, the story Soundi — giants (their own rules), bio-lab clones, the Sreekmakra, the
/// Sounders' story fauna, an animal begging, called by a caller block or trotting off after a meal (a player leads it),
/// and the target of anyone's taming session.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The ring of blocks round a town's box (and above and under it) that counts as the town for wild animals.</summary>
    internal const int TownWildMargin = 4;

    /// <summary>How long a wild ground animal may stay in a town zone before the intruder sweep sends it away unseen.</summary>
    internal const double TownStayGraceSeconds = 30.0;

    /// <summary>Whether the active world has anything a town rule could speak for (ruins included — they are skipped later).</summary>
    private bool AnyTownZones => _settlements.Count > 0 || _lodges.Count > 0;

    /// <summary>How deep a feet cell lies in a town zone: 0 outside every zone, otherwise 1 + the whole columns between it
    /// and the zone's nearest side edge (the deepest zone counts where two overlap). <paramref name="topY"/> is that zone's
    /// box top (the highest block row of the town).</summary>
    private int TownZoneDepth(int x, int feetY, int z, out int topY)
    {
        int circ = _world.Circumference;
        int best = 0;
        topY = int.MinValue;
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue;
            }

            if (s.Layout is { HasColumnMap: true })
            {
                int d = PlannedZoneDepth(s, x, feetY, z, circ); // #3592: the claimed columns, not the box
                if (d > best)
                {
                    best = d;
                    topY = s.Max.Y;
                }

                continue;
            }

            ZoneDepth(x, feetY, z, s.Min.X, s.Min.Z, s.Max.X, s.Max.Z, s.GroundY, s.Max.Y, circ, ref best, ref topY);
        }

        foreach (var l in _lodges)
        {
            ZoneDepth(x, feetY, z, l.Origin.X, l.Origin.Z, l.Origin.X + l.Layout.Width - 1, l.Origin.Z + l.Layout.Length - 1,
                l.GroundY, l.GroundY + l.Layout.Height - 1, circ, ref best, ref topY);
        }

        return best;
    }

    private int TownZoneDepth(int x, int feetY, int z) => TownZoneDepth(x, feetY, z, out _);

    /// <summary>One box's share of <see cref="TownZoneDepth(int, int, int, out int)"/>: the box (inclusive block bounds) grown
    /// by the margin, the feet between margin-below-the-foundation and margin-above-the-top.</summary>
    private static void ZoneDepth(int x, int feetY, int z, int minX, int minZ, int maxX, int maxZ, int groundY, int boxTop, int circ,
        ref int best, ref int topY)
    {
        if (feetY < groundY - TownWildMargin || feetY > boxTop + TownWildMargin)
        {
            return;
        }

        int width = maxX - minX + 1 + (2 * TownWildMargin);
        int length = maxZ - minZ + 1 + (2 * TownWildMargin);
        int lx = WorldConstants.WrapDeltaX(x - (minX - TownWildMargin), circ);
        int lz = z - (minZ - TownWildMargin);
        if (lx < 0 || lx >= width || lz < 0 || lz >= length)
        {
            return;
        }

        int depth = 1 + System.Math.Min(System.Math.Min(lx, width - 1 - lx), System.Math.Min(lz, length - 1 - lz));
        if (depth > best)
        {
            best = depth;
            topY = boxTop;
        }
    }

    /// <summary>The spawn half (one line of <see cref="SpawnSpotClear"/>): whether a wild animal of <paramref name="sp"/>
    /// placed with its feet at <paramref name="pos"/> would stand in a town (see the type summary).</summary>
    private bool TownSpawnBlocked(CreatureSpecies sp, Vector3f pos)
    {
        if (!AnyTownZones || sp.Habitat is CreatureHabitat.Water or CreatureHabitat.Lava)
        {
            return false;
        }

        var cell = pos.ToBlock();
        if (TownZoneDepth(cell.X, cell.Y, cell.Z, out int topY) == 0)
        {
            return false;
        }

        return sp.Habitat switch
        {
            CreatureHabitat.Air => UnderTownRoof(cell, topY),                                    // over the town, never under its roofs
            CreatureHabitat.Amphibian => !IsFluid(_world.GetBlockIfLoaded(cell).Value),          // ashore only — a town pond keeps its frogs
            _ => true,                                                                           // land, and a cave too near the foundation
        };
    }

    /// <summary>Whether anything a body bumps into hangs over <paramref name="cell"/> up to the town's top row — a roof, an
    /// upper floor, a hall's ceiling. Plants and fluids do not count (a tree in a lane is no roof). No-load reads.</summary>
    private bool UnderTownRoof(Vector3i cell, int topY)
    {
        for (int y = cell.Y + 1; y <= topY; y++)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(cell.X, y, cell.Z)), fluidsPass: true, foliagePasses: true))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The movement half (one line of <see cref="StepBlocked"/>): whether a wild ground animal stepping from
    /// <paramref name="cur"/> to <paramref name="cand"/> would enter a town zone or go a column deeper into one.
    /// <paramref name="nextFeet"/> is the target column's feet cell when the step preview found it (a drop or a rise).</summary>
    private bool TownStepBlocked(CombatEntity c, CreatureSpecies sp, Vector3f cur, Vector3f cand, int? nextFeet)
    {
        if (!AnyTownZones || sp.Habitat is not (CreatureHabitat.Land or CreatureHabitat.Amphibian or CreatureHabitat.Cave))
        {
            return false;
        }

        int nx = (int)System.Math.Floor(cand.X), nz = (int)System.Math.Floor(cand.Z);
        int feet = nextFeet ?? (int)System.Math.Floor(cand.Y);
        int candDepth = TownZoneDepth(nx, feet, nz);
        if (candDepth == 0)
        {
            return false; // the step stays outside every town
        }

        if (sp.Habitat == CreatureHabitat.Amphibian && IsFluid(_world.GetBlockIfLoaded(new Vector3i(nx, feet, nz)).Value))
        {
            return false; // swimming in the town's water, not walking its lanes
        }

        if (TownRuleExempt(c, sp))
        {
            return false;
        }

        var at = cur.ToBlock();
        return candDepth > TownZoneDepth(at.X, at.Y, at.Z);
    }

    /// <summary>The animals the town rules never speak for (see the type summary): the cheap flags first, the taming scan last.</summary>
    private bool TownRuleExempt(CombatEntity c, CreatureSpecies sp)
        => c.IsCompanion || c.IsGiant || c.CloneOf.Length > 0 || c.BegPhase != BegPhase.None
           || Shared.Sounders.SoundersFauna.IsSoundersPlan(sp.BodyPlan) || IsSreekmakra(c) || IsTamingTarget(c.Id);

    /// <summary>The sweep half (called by <see cref="SweepBaseIntruders"/> for every animal): keeps the animal's town clock
    /// (<see cref="CombatEntity.InTownSince"/>) and answers whether it has been a wild ground animal in a town zone for
    /// <see cref="TownStayGraceSeconds"/>. The sweep removes it only while nobody is looking.</summary>
    private bool TownStayOverdue(CombatEntity c)
    {
        if (!IsTownIntruder(c))
        {
            c.InTownSince = -1;
            return false;
        }

        if (c.InTownSince < 0)
        {
            c.InTownSince = _uptime;
            return false;
        }

        return _uptime - c.InTownSince >= TownStayGraceSeconds;
    }

    /// <summary>A wild ground animal standing in a town zone that nobody is busy with — the base sweep's exemptions
    /// (provoked, held in stasis, hunting or answering a caller) plus <see cref="TownRuleExempt"/>. An amphibian counts only
    /// ashore; water, lava and air life never.</summary>
    private bool IsTownIntruder(CombatEntity c)
    {
        if (!AnyTownZones || !_speciesById.TryGetValue(c.SpeciesId, out var sp)
            || sp.Habitat is not (CreatureHabitat.Land or CreatureHabitat.Amphibian or CreatureHabitat.Cave))
        {
            return false;
        }

        if (c.ProvokeTimer > 0 || c.FrozenTimer > 0 || c.Loco.Mode == MoveMode.Seek || TownRuleExempt(c, sp))
        {
            return false;
        }

        var cell = c.Position.ToBlock();
        if (TownZoneDepth(cell.X, cell.Y, cell.Z) == 0)
        {
            return false;
        }

        return sp.Habitat != CreatureHabitat.Amphibian || !IsFluid(_world.GetBlockIfLoaded(cell).Value);
    }

    /// <summary>Test seam (#3453): how deep a feet position lies in a town zone (0 = outside every zone).</summary>
    public int TownZoneDepthForTest(Vector3f feet)
    {
        var cell = feet.ToBlock();
        return TownZoneDepth(cell.X, cell.Y, cell.Z);
    }
}
