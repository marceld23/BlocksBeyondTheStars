// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Energy fences: player-built pens that fauna cannot cross.
///
/// Creatures, companions and planet enemies never consult the voxel world for collision — they steer
/// over the generator surface and only stop at parked ship hulls — so ordinary solid blocks cannot
/// contain them. This check makes exactly two blocks read as walls to fauna: the <b>energy fence</b>
/// pylon and the <b>energy gate</b> membrane. Players and NPCs keep their normal rules: the pylon is
/// Solid (blocks both), the gate is not (both walk through it) — which is what makes the gate a door
/// that needs no opening: fauna alone are held back.
/// </summary>
public sealed partial class GameServer
{
    private ushort _energyFenceId, _energyGateId;

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
    /// the sampled Y lands at its base or a block above (hop/bob wobble). Sampled in <paramref name="from"/>'s
    /// unwrapped frame so a step across the longitude seam doesn't read as a world-length sweep.</summary>
    private bool BlockedByEnergyFence(Vector3f from, Vector3f to)
    {
        if (_energyFenceId == 0 && _energyGateId == 0)
        {
            return false;
        }

        var dst = Unwrapped(from, to);
        float dx = dst.X - from.X, dz = dst.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / 0.25f));
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
        }

        return false;
    }

    private bool IsFenceCell(int x, int y, int z)
    {
        ushort v = _world.GetBlock(new Vector3i(x, y, z)).Value;
        if (v == 0)
        {
            return false;
        }

        // #2053: a gate a conduit holds open lets fauna through — the pen's door on a signal.
        return v == _energyFenceId || (v == _energyGateId && !CrystalGateOpen(new Vector3i(x, y, z)));
    }

    /// <summary>Test/util: expose the fauna fence sweep so tests can probe exact steps without fighting
    /// the steering randomness (mirrors <see cref="HasLineOfSightForTest"/>).</summary>
    public bool BlockedByEnergyFenceForTest(Vector3f from, Vector3f to) => BlockedByEnergyFence(from, to);
}
