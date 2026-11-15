// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giant's block body (#3676) — <see cref="GuardianGiantBody.Lying"/> about a record's anchor. Every change is a
/// server write (no owner) with a <c>BlockChanged</c> for the clients, so the body stays ordinary, mineable blocks:
/// <list type="bullet">
/// <item><b>The stir</b> swaps one visor light between <see cref="GuardianGiantBody.LightOff"/> and
/// <see cref="GuardianGiantBody.LightOn"/>; <b>the wake</b> switches every light that still holds a light block.</item>
/// <item><b>The rise</b> removes only the cells that still hold the body's own block (a light either way) — a block a player
/// put there stays.</item>
/// <item><b>Lying down</b> (lit, at the resting place), <b>the corpse</b> (dark, where it fell) and <b>the restore</b> stamp the
/// body only into air and plants — never over a player's block or the terrain. The admin's <c>/giant guardian</c> stamps it
/// whole.</item>
/// <item><b>Mining</b> one of its cells, where planet machines are active, wakes a giant that can wake.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The body's cell offsets per facing (the shape never depends on the seed — only its blocks do).</summary>
    private static HashSet<(int, int, int)>[]? s_guardianGiantShapes;

    private readonly Dictionary<string, BlockId> _guardianGiantBlockIds = new(System.StringComparer.Ordinal);

    /// <summary>A body block's id (air when the content lacks it — then the cell is left alone).</summary>
    private BlockId GuardianGiantBlock(string key)
    {
        if (!_guardianGiantBlockIds.TryGetValue(key, out var id))
        {
            id = _content.GetBlock(key)?.NumericId ?? BlockId.Air;
            _guardianGiantBlockIds[key] = id;
        }

        return id;
    }

    /// <summary>Whether a cell holds one of the giant's light blocks (lit or dark).</summary>
    private bool IsGuardianGiantLight(BlockId id)
        => !id.IsAir && (id == GuardianGiantBlock(GuardianGiantBody.LightOn) || id == GuardianGiantBlock(GuardianGiantBody.LightOff));

    /// <summary>Writes one cell (a server write — no owner) and tells the world's clients; a hole next to water lets it flow.
    /// Sent as <c>Moved</c>: the body rising or lying down, a light switching — never a hand edit, so the clients play no
    /// mining crumble or place pop for its ~260 cells (the giant's own animation and sounds carry the moment).</summary>
    private void SetGuardianGiantCell(Vector3i cell, BlockId id)
    {
        if (!WithinBuildHeight(cell.Y))
        {
            return;
        }

        _world.SetBlock(cell, id);
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = id.Value, Moved = true });
        if (id.IsAir && HasFluidNeighbor(cell))
        {
            OnFluidRemoved(cell);
        }
    }

    /// <summary>Switches every light of the body at the anchor that still holds a light block to lit or dark.</summary>
    private void SetGuardianGiantLights(int ax, int ay, int az, int facing, int seed, bool lit)
    {
        var want = GuardianGiantBlock(lit ? GuardianGiantBody.LightOn : GuardianGiantBody.LightOff);
        if (want.IsAir)
        {
            return;
        }

        _repo.RunInTransaction(() =>
        {
            foreach (var (dx, dy, dz, _, isLight) in GuardianGiantBody.Lying(seed, lit, facing))
            {
                var cell = new Vector3i(ax + dx, ay + dy, az + dz);
                var now = _world.GetBlock(cell);
                if (isLight && IsGuardianGiantLight(now) && now != want)
                {
                    SetGuardianGiantCell(cell, want);
                }
            }
        });
    }

    /// <summary>The stirring eye on or off — one of the visor's lights, picked by the seed; left alone when it was mined.</summary>
    private void SetGuardianGiantStirLight(GuardianGiantRecord rec, bool on)
    {
        var cell = GuardianGiantStirCell(rec);
        var now = _world.GetBlock(cell);
        var want = GuardianGiantBlock(on ? GuardianGiantBody.LightOn : GuardianGiantBody.LightOff);
        if (IsGuardianGiantLight(now) && now != want)
        {
            SetGuardianGiantCell(cell, want);
        }
    }

    /// <summary>The world cell of the stirring eye: one of the head's lights (the visor), by the seed.</summary>
    private static Vector3i GuardianGiantStirCell(GuardianGiantRecord rec)
    {
        var visor = new List<GuardianGiantCell>();
        foreach (var c in GuardianGiantBody.Standing(rec.Seed, lit: false))
        {
            if (c.IsLight && c.Part == GuardianGiantPart.Head)
            {
                visor.Add(c);
            }
        }

        var eye = visor[(int)((uint)rec.Seed % (uint)visor.Count)];
        var (lx, ly, lz) = GuardianGiantBody.ToLying(eye.X, eye.Y, eye.Z);
        var (fx, fz) = GuardianGiantBody.Face(lx, lz, rec.Facing);
        return new Vector3i(rec.X + fx, rec.Y + ly, rec.Z + fz);
    }

    /// <summary>The rise: every cell of the lying body that still holds the body's own block (a light either way) turns to
    /// air; anything else — terrain, a block a player put there, a mined hole — stays as it is.</summary>
    private void RemoveGuardianGiantBody(GuardianGiantRecord rec)
    {
        _repo.RunInTransaction(() =>
        {
            foreach (var (dx, dy, dz, key, isLight) in GuardianGiantBody.Lying(rec.Seed, lit: false, rec.Facing))
            {
                var cell = new Vector3i(rec.X + dx, rec.Y + dy, rec.Z + dz);
                var now = _world.GetBlock(cell);
                bool own = isLight ? IsGuardianGiantLight(now) : !now.IsAir && now == GuardianGiantBlock(key);
                if (own)
                {
                    SetGuardianGiantCell(cell, BlockId.Air);
                }
            }
        });
    }

    /// <summary>Stamps the lying body at an anchor. Without <paramref name="overwrite"/> only into air and plants (lying down,
    /// the corpse, the restore); with it into every cell (the admin's stamp).</summary>
    private void StampGuardianGiantBody(int ax, int ay, int az, int facing, int seed, bool lit, bool overwrite)
    {
        _repo.RunInTransaction(() =>
        {
            foreach (var (dx, dy, dz, key, _) in GuardianGiantBody.Lying(seed, lit, facing))
            {
                var id = GuardianGiantBlock(key);
                var cell = new Vector3i(ax + dx, ay + dy, az + dz);
                if (id.IsAir || !WithinBuildHeight(cell.Y))
                {
                    continue;
                }

                var now = _world.GetBlock(cell);
                if (now != id && (overwrite || now.IsAir || IsFlora(now.Value)))
                {
                    SetGuardianGiantCell(cell, id);
                }
            }
        });
    }

    /// <summary>The body's cell offsets at a facing.</summary>
    private static HashSet<(int, int, int)> GuardianGiantShape(int facing)
    {
        var shapes = s_guardianGiantShapes;
        if (shapes is null)
        {
            shapes = new HashSet<(int, int, int)>[4];
            for (int f = 0; f < 4; f++)
            {
                shapes[f] = new HashSet<(int, int, int)>();
                foreach (var (dx, dy, dz, _, _) in GuardianGiantBody.Lying(0, lit: false, f))
                {
                    shapes[f].Add((dx, dy, dz));
                }
            }

            s_guardianGiantShapes = shapes;
        }

        return shapes[((facing % 4) + 4) % 4];
    }

    /// <summary>Whether a cell is one of the body's cells lying at an anchor.</summary>
    private bool GuardianGiantBodyHolds(int ax, int ay, int az, int facing, Vector3i cell)
    {
        int dy = cell.Y - ay;
        if (dy < GuardianGiantBody.LyingBottom || dy > GuardianGiantBody.LyingTop)
        {
            return false;
        }

        int dx = WorldConstants.WrapDeltaX(cell.X - ax, _world.Circumference);
        int dz = WorldConstants.WrapDeltaZ(cell.Z - az, _world.Circumference);
        return GuardianGiantShape(facing).Contains((dx, dy, dz));
    }

    /// <summary>A player broke a block (BreakBlockCore): when it was a cell of a sleeping giant that can wake, and planet
    /// machines are active, the giant wakes (unless another one of this body is up already).</summary>
    private void GuardianGiantOnBlockBroken(Vector3i pos)
    {
        if (_meta.GuardianGiants.Count == 0 || _world.Planet.Void || !GuardianGiantsMayWake)
        {
            return;
        }

        var gw = GuardianGiantWorldFor(_world.LocationId);
        if (gw.Fight is not null)
        {
            return;
        }

        foreach (var rec in GuardianGiantsHere().ToArray())
        {
            bool sleeping = rec.State is GuardianGiantRecord.Dormant or GuardianGiantRecord.Lit;
            if (rec.CanWake && sleeping && GuardianGiantBodyHolds(rec.X, rec.Y, rec.Z, rec.Facing, pos))
            {
                WakeGuardianGiant(gw, rec);
                return;
            }
        }
    }
}
