// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3420: ram damage by the rock's real size. The flight view keeps the flown ship out of every rock — it stops at the
/// surface and slides along (<c>RockKeepOut</c> in Client.Core) — so a pilot meets a rock at its surface. A pilot touches an
/// asteroid while the ship's centre is within <see cref="SpaceCombatRules.ShipRockRadius"/> plus
/// <see cref="RockContactSlack"/> of the rock's blocks (<see cref="VoxelSurface"/>): a big boulder is touched farther from
/// its centre than a pebble. The old check measured a fixed 3 units to the centre, so the surface of a big rock was out of
/// reach and the ship could be flown into it at speed. What hurts is the speed INTO the rock — the part of the ship's last
/// move that pointed at the surface it touches: a fast hit dents the shield and hull as before (same threshold, factor, cap
/// and cooldown), a slow touch, resting at the surface or sliding along it does not. The server does not move the ship: a
/// pose is the client's (it was never clamped at a station hull either).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far beyond the client's keep-out distance a pose still counts as touching a rock: slack for float error
    /// and a rock drawn a fraction of a block off (a re-sent design of a carved rock), so a ship held at the surface is
    /// always in contact — and small, so a pass that clearly misses the rock is no hit.</summary>
    private const float RockContactSlack = 0.25f;

    /// <summary>How fast the pilot at <paramref name="pos"/> (at <paramref name="last"/> one tick of <paramref name="dt"/>
    /// ago) rams the asteroid <paramref name="rock"/>: its closing speed while it touches the rock, -1 when it does not
    /// touch it. Inside the rock's blocks (an old client, a rock that appeared around the ship) the closing speed is
    /// measured towards the rock's middle, so a ship easing out of it is not hurt.</summary>
    private static float RamSpeed(SpaceInstance instance, CombatEntity rock, Vector3f pos, Vector3f last, double dt)
    {
        float seconds = (float)Math.Max(dt, 0.0001);
        float dx = pos.X - last.X, dy = pos.Y - last.Y, dz = pos.Z - last.Z;
        float speed = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / seconds;
        if (!instance.Structures.TryGetValue(rock.Id, out var s) || s.Cells.Count == 0)
        {
            // A rock without a voxel body (the old split chunks): the old fixed distance to its centre.
            return rock.Position.DistanceSquared(pos) <= ShipCollisionRadius * ShipCollisionRadius ? speed : -1f;
        }

        float contact = SpaceCombatRules.ShipRockRadius + RockContactSlack;
        int size = Math.Max(s.Width, Math.Max(s.Height, s.Length));
        float bound = (size > 0 ? 0.87f * size : VoxelSurface.MaxReach) + contact; // half the box's diagonal, and the ship
        if (rock.Position.DistanceSquared(pos) > bound * bound)
        {
            return -1f;
        }

        // An asteroid's cells lie around its centre (-r..r), so cell k fills the unit box around Position + k — the box the
        // flight view draws. In the cells' own grid (cell k from k to k + 1) the pose sits half a block further on.
        var local = new Vector3f(pos.X - rock.Position.X + 0.5f, pos.Y - rock.Position.Y + 0.5f, pos.Z - rock.Position.Z + 0.5f);
        if (!VoxelSurface.TryNearest(s.Cells, local, contact, out var nearest, out float distance))
        {
            return -1f;
        }

        // The way out of the rock at the touched spot: away from the nearest block, or from the rock's middle when inside.
        bool inside = distance <= 1e-4f;
        float nx = inside ? pos.X - rock.Position.X : local.X - nearest.X;
        float ny = inside ? pos.Y - rock.Position.Y : local.Y - nearest.Y;
        float nz = inside ? pos.Z - rock.Position.Z : local.Z - nearest.Z;
        float length = inside ? (float)Math.Sqrt(nx * nx + ny * ny + nz * nz) : distance;
        if (length <= 1e-4f)
        {
            return speed; // right at its middle: the whole speed counts
        }

        float closing = -(dx * nx + dy * ny + dz * nz) / (length * seconds);
        return Math.Max(0f, closing);
    }

    /// <summary>Test hook (#3420): parks a voxel asteroid of the given radius at <paramref name="pos"/> in the player's flight
    /// instance and returns its id.</summary>
    public string SpawnAsteroidForTest(string playerId, Vector3f pos, int radius, string family = "stony")
    {
        var instance = _spaceInstances[_playerInstance[playerId]];
        var entity = new CombatEntity
        {
            Id = NextEntityId(),
            Kind = CombatEntityKind.Asteroid,
            Hostile = false,
            AsteroidTier = 0,
            Position = pos,
        };
        var s = MakeAsteroidStructure(entity.Id, pos, family, radius);
        entity.HullMax = entity.Hull = Math.Max(8, s.Cells.Count);
        instance.Entities.Add(entity);
        instance.Structures[s.Id] = s;
        return entity.Id;
    }
}
