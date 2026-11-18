// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3758: a space hostile's shield — damage goes here first, then on the hull. It does not recharge; an
    /// ion pulse drops it at once. On the wire as the fraction <c>NetCombatEntity.Shield</c> (the client draws the
    /// bubble while there is any).</summary>
    public float Shield { get; set; }

    public float ShieldMax { get; set; }
}

/// <summary>
/// What a ship weapon does to things that are not plain hostiles (#3758):
/// <list type="bullet">
/// <item>a <b>hostile with a shield</b> (the cruiser, the raider, an elite UFO) loses shield before hull;</item>
/// <item>an <b>intact station</b> is behind its shield: a shot at it does nothing on the server — the client draws the
/// shot ending on the station's shield bubble;</item>
/// <item>a <b>derelict station or a giant wreck</b> has no shield left: a combat weapon's shot tears hull blocks out
/// where it lands, through the same persistence a spacewalker's pick uses.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Takes <paramref name="damage"/> off the entity's shield and returns what is left for the hull.</summary>
    private static float AbsorbByShield(CombatEntity target, float damage)
    {
        if (target.Shield <= 0f)
        {
            return damage;
        }

        float absorbed = System.Math.Min(target.Shield, damage);
        target.Shield -= absorbed;
        return damage - absorbed;
    }

    /// <summary>The shield share the client draws: 0 without a shield or once it is gone.</summary>
    private static float ShieldFraction(CombatEntity e)
        => e.ShieldMax > 0f ? System.Math.Clamp(e.Shield / e.ShieldMax, 0f, 1f) : 0f;

    /// <summary>How far along the nose a shot at a station is followed to find the hull, in steps of this many units.</summary>
    private const float HullRayStep = 0.5f;

    /// <summary>A shot at a station. Mining tools never fire on one (the old refusal). A combat weapon's shot at an
    /// intact station ends on its shield — nothing happens here and nothing is refused; at a derelict or a giant
    /// wreck it costs its energy and cooldown and breaks hull blocks where it lands.</summary>
    private void FireOnStation(SpaceInstance instance, PlayerSession? session, string playerId, string cdKey,
        WeaponSpec weapon, CombatEntity target, Vector3f shipPos, Vector3f dir)
    {
        if (!weapon.IsCombat || Rules.ShipWeapons is ShipWeaponMode.Off or ShipWeaponMode.MiningOnly)
        {
            RejectSpace(session, "@srv.space.no_fire_station");
            return;
        }

        if (SpaceCombatRules.StationHasShield(target.Id) || !instance.Structures.TryGetValue(target.Id, out var s) || s.Kind != "station")
        {
            return; // intact: the shield takes it (the client draws the bubble)
        }

        if (weapon.Homing)
        {
            dir = default; // a homing shot flies at the hull's centre, wherever the nose points
        }

        if (!TryHullImpact(s, shipPos, dir, weapon.Range, out var impact))
        {
            return; // the shot passes the wreck
        }

        if (!TryDrawShipEnergy(playerId, weapon.Energy))
        {
            RejectSpace(session, "@srv.space.no_energy");
            return;
        }

        if (weapon.Cooldown > 0.0)
        {
            _shipWeaponReadyAt[cdKey] = _uptime + (weapon.Cooldown * 0.95);
        }

        BreachWreckHull(instance, s, impact, HullBlocksFor(weapon.Damage));
    }

    /// <summary>#3758: a ship's wreck and its wreckage are no rock — every weapon tears plating off them wherever the
    /// world allows ship weapons at all.</summary>
    private bool WeaponsTearWreckage(CombatEntity target)
        => target.Kind is CombatEntityKind.Wreck or CombatEntityKind.Debris
           && Rules.ShipWeapons is not (ShipWeaponMode.Off or ShipWeaponMode.MiningOnly);

    /// <summary>How many hull blocks one shot of this damage tears out: two for the starter laser's pulse, up to six for a missile.</summary>
    internal static int HullBlocksFor(float damage) => System.Math.Clamp(1 + (int)(damage / 14f), 1, 6);

    /// <summary>Where a structure's cell lies in the flight space: the structure floats with its cell box centred on
    /// its position (the measure <c>InStructureEditRange</c> uses).</summary>
    private static Vector3i CellAt(SpaceStructure s, (Vector3i Min, Vector3i Max) box, float x, float y, float z)
        => new(
            (int)System.Math.Floor(x - s.Position.X + ((box.Min.X + box.Max.X + 1) * 0.5f)),
            (int)System.Math.Floor(y - s.Position.Y + ((box.Min.Y + box.Max.Y + 1) * 0.5f)),
            (int)System.Math.Floor(z - s.Position.Z + ((box.Min.Z + box.Max.Z + 1) * 0.5f)));

    /// <summary>Follows the shot from the ship along <paramref name="dir"/> (the nose; toward the structure's centre
    /// when the client sent none) and returns the first hull cell it meets within <paramref name="range"/>.</summary>
    private static bool TryHullImpact(SpaceStructure s, Vector3f from, Vector3f dir, float range, out Vector3i impact)
    {
        impact = default;
        float dx = dir.X, dy = dir.Y, dz = dir.Z;
        float len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (len < 0.01f)
        {
            dx = s.Position.X - from.X;
            dy = s.Position.Y - from.Y;
            dz = s.Position.Z - from.Z;
            len = (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (len < 0.01f)
            {
                return false;
            }
        }

        dx /= len;
        dy /= len;
        dz /= len;
        var box = s.CellBox();
        for (float t = 0f; t <= range; t += HullRayStep)
        {
            var cell = CellAt(s, box, from.X + (dx * t), from.Y + (dy * t), from.Z + (dz * t));
            if (!s.Get(cell).IsAir)
            {
                impact = cell;
                return true;
            }
        }

        return false;
    }

    /// <summary>Tears up to <paramref name="count"/> hull blocks out of a derelict around <paramref name="impact"/> —
    /// the hit cell first, then its nearest neighbours — and stores, mirrors and announces each one the way a
    /// spacewalker's pick does. Doors and blocks that cannot be mined stay; nothing drops (it is blown away).</summary>
    private void BreachWreckHull(SpaceInstance instance, SpaceStructure s, Vector3i impact, int count)
    {
        var boxBefore = CellBox(s);
        int removed = 0;
        for (int ring = 0; ring <= 2 && removed < count; ring++)
        {
            for (int ox = -ring; ox <= ring && removed < count; ox++)
            {
                for (int oy = -ring; oy <= ring && removed < count; oy++)
                {
                    for (int oz = -ring; oz <= ring && removed < count; oz++)
                    {
                        if (System.Math.Max(System.Math.Abs(ox), System.Math.Max(System.Math.Abs(oy), System.Math.Abs(oz))) != ring)
                        {
                            continue; // only the shell of this ring — the inner ones were tried already
                        }

                        var pos = new Vector3i(impact.X + ox, impact.Y + oy, impact.Z + oz);
                        if (BreachCell(instance, s, pos))
                        {
                            removed++;
                        }
                    }
                }
            }
        }

        if (removed == 0)
        {
            return;
        }

        if (s.Boardable && !PersistIfDerelict(s))
        {
            PersistStation(instance, s);
        }

        ResendStationDesignIfBoxChanged(instance, s, boxBefore);
        PublishLaidOpenCells(s);
    }

    private bool BreachCell(SpaceInstance instance, SpaceStructure s, Vector3i pos)
    {
        var existing = s.Get(pos);
        if (existing.IsAir || _content.BlockById(existing) is not { Mineable: true } def || DoorBlocks.IsDoorBlock(def.Key))
        {
            return false;
        }

        _structureMiningProgress.Remove((s.Id, pos));
        s.Set(pos, BlockId.Air);
        MirrorStationCellOutside(s, pos, BlockId.Air);
        PublishDerelictHullToOtherOrbits(instance, s, pos, BlockId.AirValue);
        BroadcastToInstance(instance, new StructureBlockChanged
        {
            StructureId = s.Id,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Block = BlockId.AirValue,
        });
        return true;
    }
}
