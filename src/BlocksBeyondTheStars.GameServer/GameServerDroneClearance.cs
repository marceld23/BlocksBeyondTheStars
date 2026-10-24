// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2950: a scan-drone lives over the real surface. A player had raised a whole spaceport five or six blocks over the
/// natural ground, and a drone patrolled inside the tunnels under it with its body in the roof: the spawn probe started
/// at the generator's surface — inside the fill — and took the nearest cavity, the tunnel, for the ground; in flight no
/// cell between the ground and the hovering body was ever tested, so a drone could also drift through a wall into a
/// tunnel; and nothing lifted a buried drone out again. Now the spawn reads the live top of the column, every step and
/// knockback needs the cells up to the body clear (water and tree crowns still pass), and a buried drone is lifted over
/// its column's top — or dropped when there is no room there.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far over (and under) the generator's surface a drone's spawn probe looks for the column's live top —
    /// room for a raised base or a tall build, still only a column of block reads per spawn.</summary>
    private const int DroneSkyScan = 48;

    /// <summary>
    /// Where a scan-drone's ground probe starts: the cell over the live top of the column — scanning down from
    /// <see cref="DroneSkyScan"/> over the generator's surface, the first block a body stands on with open air over it
    /// (a raised spaceport, a roof, a tree crown) — so the probe finds the real ground, never a tunnel under it. A gas
    /// giant keeps its gas-aware probe (#2586), and an unloaded column or one with nothing in the window the generator's.
    /// </summary>
    private int DroneSpawnRefY(int x, int z)
    {
        if (GasSeaTopY() != int.MinValue)
        {
            return GroundProbeRefY(x, z);
        }

        int natural = _generator.SurfaceHeight(_world.Planet, x, z);
        if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, natural, z))))
        {
            return natural + 1;
        }

        bool open = !IsSupportCell(x, natural + DroneSkyScan + 1, z);
        for (int y = natural + DroneSkyScan; y >= natural - DroneSkyScan; y--)
        {
            bool support = IsSupportCell(x, y, z);
            if (support && open)
            {
                return y + 1;
            }

            open = !support;
        }

        return natural + 1;
    }

    /// <summary>Whether a drone hovering <paramref name="hover"/> cells over <paramref name="ground"/> fits there: no cell
    /// from the ground up to its body collides. Water and foliage pass — a drone crosses a lake and weaves through tree
    /// crowns as before — but a roof, a wall or a low tunnel does not. No-load reads (an unstreamed cell is open).</summary>
    private bool DroneBodyClear(int x, int z, int ground, int hover)
    {
        for (int dy = 0; dy <= hover; dy++)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(x, ground + dy, z)), fluidsPass: true, foliagePasses: true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A scan-drone whose body is in a block (a roof built over it, or one that got into a tunnel before #2950) is
    /// lifted to its hover over the live top of its column. One that finds no room there is dropped after the movement
    /// pass, like a robot stranded in deep water. A derelict station's guardians (#2635) are left alone — their column's
    /// top is the station's roof.</summary>
    private bool TryLiftEmbeddedDrone(CombatEntity enemy, out Vector3f lifted)
    {
        lifted = enemy.Position;
        if (enemy.Kind != CombatEntityKind.ScanDrone || _world.Planet.Void)
        {
            return false;
        }

        int x = (int)System.Math.Floor(enemy.Position.X), z = (int)System.Math.Floor(enemy.Position.Z);
        var body = new Vector3i(x, (int)System.Math.Floor(enemy.Position.Y), z);
        if (!IsCollidingBlock(_world.GetBlockIfLoaded(body), fluidsPass: true, foliagePasses: true))
        {
            return false;
        }

        int ground = DroneGroundAt(x, z, DroneSpawnRefY(x, z));
        if (DroneBodyClear(x, z, ground, ScanDroneHover))
        {
            lifted = new Vector3f(enemy.Position.X, ground + ScanDroneHover, enemy.Position.Z);
            return true;
        }

        if (!_strandedMachines.Contains(enemy))
        {
            _strandedMachines.Add(enemy);
        }

        return false;
    }

    /// <summary>Test seam (#2950): one run of the surface spawner for a scan-drone near <paramref name="player"/> — the
    /// first one stands 35 blocks east of the player (golden angle 0) when no wreck or lab pulls it elsewhere.</summary>
    internal bool SpawnPlanetDroneNearForTest(Shared.State.PlayerState player) => SpawnPlanetEnemyNear(player, asDrone: true);
}
