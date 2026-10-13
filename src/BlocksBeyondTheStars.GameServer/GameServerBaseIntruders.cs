// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Wild animals inside a base (#2521). The spawner keeps them out of sealed rooms, walled yards and — since #2521 —
/// off every floor a player placed, but the enclosure is only ever asked at spawn time: an animal that walked in
/// through an open gate, a gap, a wall column that had not loaded yet, or across a one-deep lava trench the walled
/// fill counts as walkable, stayed there for good. This sweep sends such an animal away again: a wild animal
/// standing on player-placed floor, or in a sealed room or a walled area of a base, despawns while no player is
/// looking at it (the crowd-prune idea, #1356 — what is on screen stays put).
/// <list type="bullet">
/// <item>Never pets (a player's companion or a tamer's), bio-lab clones, giants or the shapeshifter.</item>
/// <item>Never an animal a player is busy with: provoked, hunting, begging or called by a caller block, held in stasis,
/// or the target of anyone's taming session (a wild animal lured home to be tamed).</item>
/// <item>Only land animals are swept. Amphibians answer to the floor rule alone, and only while they stand on a solid
/// block; water, lava, air and cave life is never swept (a fish in a deep moat reads as walled in).</item>
/// <item>"Looking" is generous on purpose: within the player's streaming radius, inside a wide cone around the look
/// direction (pitch ignored) and with a clear sightline — glass, shaped blocks, plants and fluids do not hide it.
/// An animal within a few blocks always counts as seen.</item>
/// </list>
/// Runs every <see cref="BaseIntruderSweepInterval"/> seconds per world, not every tick: one indexed floor lookup per
/// ground animal plus the cached sealed-room and walled-yard sets near a base, and sightlines only for the intruders.
/// </summary>
public sealed partial class GameServer
{
    private const double BaseIntruderSweepInterval = 3.0;

    /// <summary>An animal this close to a player counts as seen whatever the look direction — it brushes past you.</summary>
    private const float IntruderNearRange = 3f;

    /// <summary>Cosine of the half-angle of the "looking" cone around the player's yaw (≈70° either side) — wider than
    /// the client camera shows, so nothing vanishes at the edge of the screen.</summary>
    private const float IntruderViewCos = 0.34f;

    private readonly List<PlayerSession> _intruderWatchers = new();  // reused per sweep (no per-sweep alloc)
    private readonly List<CombatEntity> _intrudersScratch = new();

    /// <summary>Throttled per world; returns true if any animal was removed (the caller re-broadcasts).</summary>
    private bool PruneBaseIntruders(double dt)
    {
        var world = _worlds.Active;
        world.SinceBaseIntruderSweep += dt;
        if (world.SinceBaseIntruderSweep < BaseIntruderSweepInterval)
        {
            return false;
        }

        world.SinceBaseIntruderSweep = 0;
        return SweepBaseIntruders();
    }

    private bool SweepBaseIntruders()
    {
        // Everyone on this world who could be looking — a player aboard the parked ship looks out of its windows too.
        _intruderWatchers.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId))
            {
                _intruderWatchers.Add(s);
            }
        }

        if (_intruderWatchers.Count == 0)
        {
            return false;
        }

        // Decide first, remove after: the sealed-room and walled-yard lookups may refresh their caches as they go.
        _intrudersScratch.Clear();
        foreach (var c in _creatures)
        {
            if (IsBaseIntruder(c) && !SeenByAnyPlayer(c.Position, _intruderWatchers))
            {
                _intrudersScratch.Add(c);
            }
        }

        foreach (var c in _intrudersScratch)
        {
            _creatures.Remove(c);
        }

        int removed = _intrudersScratch.Count;
        _intrudersScratch.Clear();
        return removed > 0;
    }

    /// <summary>A wild animal where no wild animal would spawn: on player-placed floor, in a sealed room or in a walled
    /// area of a base. The exemptions come first — they cost nothing and cover every animal a player cares about.</summary>
    private bool IsBaseIntruder(CombatEntity c)
    {
        if (c.IsCompanion || c.IsGiant || c.CloneOf.Length > 0 || IsSreekmakra(c))
        {
            return false;
        }

        if (c.ProvokeTimer > 0 || c.FrozenTimer > 0 || c.BegPhase != BegPhase.None || c.Loco.Mode == MoveMode.Seek)
        {
            return false; // mid-fight, mid-hunt, begging or answering a caller, held in stasis — someone is busy with it
        }

        if (IsTamingTarget(c.Id))
        {
            return false; // lured home to be tamed: it must not vanish mid-taming, whoever runs the session
        }

        if (!_speciesById.TryGetValue(c.SpeciesId, out var sp))
        {
            return false;
        }

        // Land animals answer to all three rules. An amphibian only to the floor rule, and only while it stands on a
        // solid block (OnPlayerBuiltFloor never counts water). Water, lava, air and cave life is never swept: a fish in a
        // deep moat reads as "walled in" (the fill treats deep fluid as a wall), and a moat is exactly where it belongs.
        if (sp.Habitat == CreatureHabitat.Amphibian)
        {
            return OnPlayerBuiltFloor(c.Position);
        }

        if (sp.Habitat != CreatureHabitat.Land)
        {
            return false;
        }

        var cell = new Vector3i((int)System.Math.Floor(c.Position.X), (int)System.Math.Floor(c.Position.Y), (int)System.Math.Floor(c.Position.Z));
        return OnPlayerBuiltFloor(c.Position) || InSealedBaseRoom(cell) || InWalledBaseArea(cell);
    }

    /// <summary>The habitats the floor rule speaks for (spawn and sweep): land walkers, and amphibians — those only
    /// while ashore, because <see cref="OnPlayerBuiltFloor"/> never counts a body standing in fluid.</summary>
    private static bool FloorRuleApplies(CreatureSpecies sp) => sp.Habitat is CreatureHabitat.Land or CreatureHabitat.Amphibian;

    /// <summary>True while any player's taming session (the translator attempt that drives <c>TameProgress</c>) is on
    /// this creature. At most one attempt per player, so a plain scan is cheap.</summary>
    private bool IsTamingTarget(string creatureId)
    {
        foreach (var attempt in _tameAttempts.Values)
        {
            if (attempt.CreatureId == creatureId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#2521: whether a body with its feet at <paramref name="feet"/> stands on a block a player placed — the
    /// cell under the feet, plus the feet cell itself when a floor block fills it (a slab). One indexed lookup in the
    /// block-edit store (<c>owner_id &lt;&gt; 0</c>), the same query <see cref="PlayerBuiltPocket"/> uses.
    /// <para>Only real floor blocks are asked about (<see cref="IsFloorBlock"/>), and a body standing in fluid is on no
    /// floor at all. The reason is that a cell's owner STICKS: the store keeps the last player editor when the server
    /// later writes the cell (water flowing back into a dug channel, a harvested plant regrowing), so the owner of a
    /// water or plant cell says nothing about whether a player built there. A cell the player MINED is air, so a dug
    /// pit's floor stays wild ground.</para></summary>
    private bool OnPlayerBuiltFloor(Vector3f feet)
    {
        var cell = new Vector3i((int)System.Math.Floor(feet.X), (int)System.Math.Floor(feet.Y), (int)System.Math.Floor(feet.Z));
        var at = _world.GetBlockIfLoaded(cell);
        if (IsFluid(at.Value))
        {
            return false; // wading or swimming — water is never "floor", whoever last touched it
        }

        bool feetFloor = IsFloorBlock(at);
        bool belowFloor = IsFloorBlock(_world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y - 1, cell.Z)));
        if (!feetFloor && !belowFloor)
        {
            return false; // on fluid, on plants or over nothing loaded: no floor anyone could own — no query
        }

        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        return _repo.HasPlayerBlockEdits(_world.LocationId,
            new Vector3i(c.X, belowFloor ? c.Y - 1 : c.Y, c.Z),
            new Vector3i(c.X, feetFloor ? c.Y : c.Y - 1, c.Z));
    }

    /// <summary>A block a body can stand on that only a builder would have put there: solid, not a fluid or a still
    /// liquid, not flora (plants regrow under their harvester's name) and not a walk-through prop or tree crown.</summary>
    private bool IsFloorBlock(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
    {
        if (id.IsAir || IsFluid(id.Value) || IsFlora(id.Value))
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def is { Solid: true, Liquid: false } && !IsWalkThroughProp(def.Key, foliagePasses: true);
    }

    /// <summary>Whether any of these players may be looking at a spot (see the type summary for how generous it is).</summary>
    private bool SeenByAnyPlayer(Vector3f at, List<PlayerSession> watchers)
    {
        foreach (var s in watchers)
        {
            var p = s.State;
            var to = Unwrapped(p.Position, at);
            float dx = to.X - p.Position.X, dy = to.Y - p.Position.Y, dz = to.Z - p.Position.Z;
            float distSq = dx * dx + dy * dy + dz * dz;
            if (distSq <= IntruderNearRange * IntruderNearRange)
            {
                return true;
            }

            float stream = (EffectiveViewRadius(s) + 1) * WorldConstants.ChunkSize;
            if (distSq > stream * stream)
            {
                continue; // not even streamed to this player
            }

            // Yaw 0 = +Z, 90 = +X (as the streaming look-ahead reads it). The pitch is ignored: a player looking down
            // at the floor of the yard still has the animal in the picture.
            double yawRad = p.Yaw * System.Math.PI / 180.0;
            float horiz = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float ahead = (float)(System.Math.Sin(yawRad) * dx + System.Math.Cos(yawRad) * dz);
            if (horiz > 0.5f && ahead < IntruderViewCos * horiz)
            {
                continue; // beside or behind the player
            }

            // Two heights on the body (low and head-high), so a waist-high wall does not hide a tall animal.
            if (IntruderSightClear(p.Position, to, 0.5f) || IntruderSightClear(p.Position, to, 1.5f))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A sightline from the player's eye to a point on the body that only real walls block: glass, shaped
    /// forms (slabs, panes, ramps leave gaps), walk-through props, fluids and unloaded cells are all see-through —
    /// it errs towards "seen". <paramref name="to"/> is already unwrapped into the player's frame.</summary>
    private bool IntruderSightClear(Vector3f from, Vector3f to, float lift)
    {
        float ax = from.X, ay = from.Y + SightEyeHeight, az = from.Z;
        float dx = to.X - ax, dy = (to.Y + lift) - ay, dz = to.Z - az;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
        int px = int.MinValue, py = int.MinValue, pz = int.MinValue;
        for (int i = 1; i < steps; i++) // both endpoints skipped — the bodies themselves are not cover
        {
            float f = i / (float)steps;
            int x = (int)System.Math.Floor(ax + dx * f);
            int y = (int)System.Math.Floor(ay + dy * f);
            int z = (int)System.Math.Floor(az + dz * f);
            if (x == px && y == py && z == pz)
            {
                continue;
            }

            px = x;
            py = y;
            pz = z;
            var cell = new Vector3i(x, y, z);
            var id = _world.GetBlockIfLoaded(cell);
            if (!IsCollidingBlock(id, fluidsPass: true, foliagePasses: true) || _world.GetShapeIfLoaded(cell) != 0)
            {
                continue;
            }

            if (_content.BlockById(id)?.Key is "glass" or "glass_clear")
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>Test seam (#2521): runs the base-intruder sweep now, without waiting for its interval.</summary>
    public bool SweepBaseIntrudersForTest() => SweepBaseIntruders();

    /// <summary>Test seam (#2521): whether a body with its feet here stands on player-placed floor.</summary>
    public bool OnPlayerBuiltFloorForTest(Vector3f feet) => OnPlayerBuiltFloor(feet);
}
