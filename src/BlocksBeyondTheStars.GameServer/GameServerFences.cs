// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Energy fences: player-built pens that fauna cannot cross.
///
/// Ordinary solid blocks stop creatures and companions too these days (#855, the wall gate), but a walker still steps up
/// one block and a flier simply flies over a wall. This check makes exactly two blocks read as <b>fauna-proof</b> walls:
/// the <b>energy fence</b> element and the <b>energy gate</b> membrane — sampled per block along every fauna step (there
/// is no field between two fence blocks, so a pen's ring must be gapless, edge to edge), and guarding the air above
/// them too (below). Players and NPCs keep their normal rules: the fence element is Solid (blocks both), the gate is not
/// (both walk through it) — which is what makes the gate a door that needs no opening: fauna alone are held back. No power
/// is needed; a Crystal-Net signal OPENS a fence or gate (the pen's door on a signal, #2053/#2261).
///
/// #3257: a penned companion stays penned. The leash (#855) re-places a player's pet beside an owner more than
/// <c>CompanionLeashRange</c> away — not while a fence or closed gate cuts the straight way between them.
///
/// #3148: a fence also guards the <b>air above it</b>. Fliers hover 3–12 blocks over the ground, so a pen only held what
/// walks; a tester who wanted the big fliers off his spaceport asked for "a raised energy fence the player has to build".
/// An airborne animal's step is blocked by a fence standing anywhere in the column below its path, down to the ground and
/// at most <see cref="FenceAirspaceDepth"/> deep — and a base that uses energy fences keeps wild fliers out of its closed
/// yards: none spawns over one, and one found there (penned in when the ring closed) flies off while nobody looks.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far below an airborne animal a fence still guards the air: the highest a flier hovers (12) plus its body.</summary>
    private const int FenceAirspaceDepth = 13;

    /// <summary>Seconds a base's "does it use energy fences?" answer is trusted before the block-edit store is asked again.</summary>
    private const double BaseFenceRecheckSeconds = 20.0;

    /// <summary>#3257: how far from a companion toward its owner the pen check looks for a fence — its pen is around it.</summary>
    private const float CompanionPenReach = 64f;

    /// <summary>#3257: seconds a companion's "is it penned?" answer is trusted (the leash asks every tick).</summary>
    private const double CompanionPenRecheckSeconds = 1.0;

    private ushort _energyFenceId, _energyGateId;

    private readonly Dictionary<int, (double At, bool Uses)> _baseUsesFence = new();

    private readonly Dictionary<string, (double At, bool Penned)> _companionPenned = new();

    /// <summary>Resolves the fence/gate block ids once per content load (0 = block missing).</summary>
    private void InitFences()
    {
        _energyFenceId = _content.GetBlock("energy_fence")?.NumericId.Value ?? 0;
        _energyGateId = _content.GetBlock("energy_gate")?.NumericId.Value ?? 0;
    }

    /// <summary>True if a fauna step from <paramref name="from"/> to <paramref name="to"/> would cross an
    /// energy fence or gate. Samples the segment every ~quarter block (like the NPC wall sweep) so a step
    /// can't tunnel through a one-block fence line. Each sample checks a small cell column around body
    /// height: fauna Y comes from surface snapping, so a pylon standing on the ground must register whether
    /// the sampled Y lands at its base or a block above (hop/bob wobble). An <paramref name="airborne"/> animal (a flier
    /// in the air, a hoverer) is also stopped by a fence in the column below it (#3148). Sampled in <paramref name="from"/>'s
    /// unwrapped frame so a step across the longitude seam doesn't read as a world-length sweep.</summary>
    private bool BlockedByEnergyFence(Vector3f from, Vector3f to, bool airborne = false)
    {
        if (_energyFenceId == 0 && _energyGateId == 0)
        {
            return false;
        }

        var dst = Unwrapped(from, to);
        float dx = dst.X - from.X, dz = dst.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
        int startX = (int)System.Math.Floor(from.X), startZ = (int)System.Math.Floor(from.Z);
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            int x = (int)System.Math.Floor(from.X + dx * f);
            int z = (int)System.Math.Floor(from.Z + dz * f);
            int y = (int)System.Math.Floor(to.Y);
            // Feet-1 .. head+1: catches a fence at the sampled ground cell, one buried below (terrain
            // seam) and up to head height, so hoppers/bobbing fliers can't pop over a two-high line.
            for (int dy = -1; dy <= 2; dy++)
            {
                if (IsFenceCell(x, y + dy, z))
                {
                    return true;
                }
            }

            // #3148: the air above a fence is a line to cross, so only ENTERING a guarded column blocks. A flier already
            // over one — taking off from a pylon it perched on, or fenced in under it by a player — may always leave it,
            // or every step (the first sample is a quarter block away) would be refused and it would never move again.
            if (airborne && (x != startX || z != startZ) && FenceBelow(x, y - 2, z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#3148: whether a closed fence or gate stands in the column below <paramref name="top"/> — scanned down until
    /// the first other colliding block (the ground, a roof) and at most <see cref="FenceAirspaceDepth"/> cells. Loaded cells
    /// only: an airborne animal is always over streamed ground, and an unloaded column guards nothing.</summary>
    private bool FenceBelow(int x, int top, int z)
    {
        for (int y = top; y > top - FenceAirspaceDepth; y--)
        {
            var cell = new Vector3i(x, y, z);
            var id = _world.GetBlockIfLoaded(cell);
            if (id.IsAir)
            {
                continue;
            }

            if (IsClosedFenceBlock(cell, id.Value))
            {
                return true;
            }

            if (IsCollidingBlock(id, fluidsPass: false, foliagePasses: true))
            {
                return false; // the ground (or a roof) under the animal — a fence beneath it guards nothing up here
            }
        }

        return false;
    }

    private bool IsFenceCell(int x, int y, int z)
    {
        var cell = new Vector3i(x, y, z);
        return IsClosedFenceBlock(cell, _world.GetBlock(cell).Value);
    }

    /// <summary>A fence pylon or gate membrane that holds fauna back — #2053: a gate a conduit holds open lets fauna
    /// through (the pen's door on a signal); #2261: a fence too.</summary>
    private bool IsClosedFenceBlock(Vector3i cell, ushort v)
        => v != 0 && ((v == _energyFenceId && !CrystalNet.OpenFences.Contains(cell)) || (v == _energyGateId && !CrystalGateOpen(cell)));

    /// <summary>#3148: whether an airborne animal at <paramref name="pos"/> is over the closed yard of a base that uses energy
    /// fences — the walled-area fill asked at the ground below it. Bases without a single fence keep the old rule: fliers are
    /// free over their yards.</summary>
    private bool InFencedBaseYard(Vector3f pos)
    {
        if ((_energyFenceId == 0 && _energyGateId == 0) || _bases.Count == 0 || GroundFeetBelow(pos) is not { } feet)
        {
            return false;
        }

        var cell = new Vector3i((int)System.Math.Floor(pos.X), feet, (int)System.Math.Floor(pos.Z));
        return InWalledBaseArea(cell, BaseUsesEnergyFence);
    }

    /// <summary>The feet level of the ground under an airborne position (one above the first colliding block below it,
    /// foliage passed), or null when none is loaded within reach.</summary>
    private int? GroundFeetBelow(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X), z = (int)System.Math.Floor(pos.Z);
        int top = (int)System.Math.Floor(pos.Y);
        for (int y = top; y > top - FenceAirspaceDepth - 4; y--)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(x, y, z)), fluidsPass: false, foliagePasses: true))
            {
                return y + 1;
            }
        }

        return null;
    }

    /// <summary>Whether a base has a single energy fence or gate inside its reach — the opt-in for the fliers' rule. One
    /// bounded store query (every fence was placed by a player), trusted for <see cref="BaseFenceRecheckSeconds"/>.</summary>
    private bool BaseUsesEnergyFence(ServerBase b)
    {
        if (_baseUsesFence.TryGetValue(b.Id, out var known) && _uptime - known.At < BaseFenceRecheckSeconds)
        {
            return known.Uses;
        }

        var ids = new List<ushort>(2);
        if (_energyFenceId != 0)
        {
            ids.Add(_energyFenceId);
        }

        if (_energyGateId != 0)
        {
            ids.Add(_energyGateId);
        }

        bool uses = ids.Count > 0 && BaseIndexEdits(b, ids, System.Array.Empty<int>(), 1).Count > 0;
        _baseUsesFence[b.Id] = (_uptime, uses);
        return uses;
    }

    /// <summary>The leash (#855) for a player's companion farther than <c>CompanionLeashRange</c> from its owner: re-placed
    /// beside them — unless it is penned (#3257): while a fence or closed gate cuts the way, it stays and potters about in
    /// its pen at the follow step's gentle pace (the fence sweep in <see cref="ApplyCreatureStep"/> keeps it inside).</summary>
    private void LeashCompanion(CombatEntity c, CreatureSpecies sp, Vector3f ownerPos, double moveDt)
    {
        if (!CompanionPenned(c, ownerPos))
        {
            c.Position = CompanionSpotNear(sp, c.Id, ownerPos);
            return;
        }

        var profile = ProfileFor(c.SpeciesId);
        var idle = profile;
        idle.CruiseSpeed = profile.CruiseSpeed * 0.45f;
        var res = LocomotionController.Step(c.Loco, idle, c.Position, MoveMode.Roam, null, moveDt, unchecked((uint)StableHashing.Seed32(Hash(c.Id, "wander"))));
        c.Loco = res.State;
        ApplyCreatureStep(c, sp, EffectiveMotion(c, sp), res.Position, res.VertWave, profile, moveDt, MoveMode.Roam, res.Moving, terrainGates: false);
    }

    /// <summary>#3257: whether a companion is penned away from its owner — a closed fence or gate across the straight way
    /// from it toward <paramref name="ownerPos"/>. Asked every tick by the leash, so the answer is kept for
    /// <see cref="CompanionPenRecheckSeconds"/> per companion.</summary>
    private bool CompanionPenned(CombatEntity c, Vector3f ownerPos)
    {
        if (_energyFenceId == 0 && _energyGateId == 0)
        {
            return false;
        }

        if (_companionPenned.TryGetValue(c.Id, out var known) && _uptime - known.At < CompanionPenRecheckSeconds)
        {
            return known.Penned;
        }

        if (_companionPenned.Count > 256)
        {
            // Companions come and go (despawned, released, another world): drop the answers nobody asked for lately.
            foreach (var stale in _companionPenned.Where(kv => _uptime - kv.Value.At > CompanionPenRecheckSeconds * 10).Select(kv => kv.Key).ToList())
            {
                _companionPenned.Remove(stale);
            }
        }

        bool penned = FenceCutsWay(c.Position, ownerPos);
        _companionPenned[c.Id] = (_uptime, penned);
        return penned;
    }

    /// <summary>#3257: whether a closed fence or gate stands across the straight way from <paramref name="from"/> (the
    /// animal) toward <paramref name="to"/> — within <see cref="CompanionPenReach"/> of the animal, where its pen is.
    /// Sampled like a fauna step, a quarter block at a time in <paramref name="from"/>'s unwrapped frame, through the body
    /// column at the way's height and at the animal's own level (the ring stands on the pen's ground), and in the fence's
    /// airspace below the way (a flier over its pen). Loaded cells only: an unloaded column pens nothing.</summary>
    private bool FenceCutsWay(Vector3f from, Vector3f to)
    {
        var dst = Unwrapped(from, to);
        float dx = dst.X - from.X, dy = dst.Y - from.Y, dz = dst.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (dist < 1e-3f)
        {
            return false;
        }

        float reach = System.Math.Min(dist, CompanionPenReach);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(reach / 0.25f));
        int ownY = (int)System.Math.Floor(from.Y);
        for (int s = 1; s <= steps; s++)
        {
            float f = reach * s / steps / dist;
            int x = (int)System.Math.Floor(from.X + dx * f);
            int z = (int)System.Math.Floor(from.Z + dz * f);
            int y = (int)System.Math.Floor(from.Y + dy * f);
            if (LoadedFenceInBody(x, y, z) || (y != ownY && LoadedFenceInBody(x, ownY, z)) || FenceBelow(x, y - 2, z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#3257: a closed fence or gate in the body column of a loaded cell (feet −1 … head +1, as the step sweep).</summary>
    private bool LoadedFenceInBody(int x, int y, int z)
    {
        for (int dy = -1; dy <= 2; dy++)
        {
            var cell = WorldConstants.CanonicalBlock(new Vector3i(x, y + dy, z), _world.Circumference); // the open-fence set is canonical
            if (IsClosedFenceBlock(cell, _world.GetBlockIfLoaded(cell).Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test/util: expose the fauna fence sweep so tests can probe exact steps without fighting
    /// the steering randomness (mirrors <see cref="HasLineOfSightForTest"/>).</summary>
    public bool BlockedByEnergyFenceForTest(Vector3f from, Vector3f to) => BlockedByEnergyFence(from, to);

    /// <summary>Test/util (#3148): the fence sweep for an airborne animal.</summary>
    public bool BlockedByEnergyFenceAirborneForTest(Vector3f from, Vector3f to) => BlockedByEnergyFence(from, to, airborne: true);

    /// <summary>Test/util (#3148): whether an airborne position is over the closed yard of a base that uses energy fences.</summary>
    public bool InFencedBaseYardForTest(Vector3f pos) => InFencedBaseYard(pos);

    /// <summary>Test/util (#3148): forget the cached "does this base use energy fences?" answers.</summary>
    public void ForgetBaseFenceCacheForTest() => _baseUsesFence.Clear();
}
