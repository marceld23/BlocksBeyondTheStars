// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: <b>Ornasa</b>, the ruin city of the old Sounders — the contrast between then and now. A levelled moss ground with
/// broken carved-obsidian paving, a broken outer wall with gaps, an amphitheatre in the middle (a paved disc, eight broken
/// rune pillars that still sing their words, a crystal on a plinth), and six slots on a ring around it
/// (<see cref="SounderSiteLayout"/>): three ruined halls (brick under obsidian, every wall column broken to its own height,
/// a crystal growing inside, two with a relic cache) and three sealed <b>sound chambers</b> — solid cubes of carved obsidian
/// with a two-high sealed doorway toward the amphitheatre, a crystal above it and a relic cache inside. Chambers 0 and 1 are
/// for the Resonance Blaster (the items pack opens them through <see cref="OpenOrnasaChamber"/>); chamber 2 is where the
/// bell-bloom trail leads (#3367) and opens when the trail's last bloom does.
/// </summary>
public sealed partial class GameServer
{
    private const int OrnasaRadius = 22;

    /// <summary>One of Ornasa's sound chambers on the active world (re-derived every load from the site record).</summary>
    internal sealed class OrnasaChamber
    {
        public OrnasaChamber(int index, Vector3i[] seal, Vector3i inside)
        {
            Index = index;
            Seal = seal;
            Inside = inside;
        }

        /// <summary>0 and 1: the blaster's chambers; <see cref="SounderSiteLayout.BloomTrailChamber"/>: the trail's.</summary>
        public int Index { get; }

        /// <summary>The doorway cells (canonical) — carved obsidian while sealed, air once opened.</summary>
        public IReadOnlyList<Vector3i> Seal { get; }

        /// <summary>The interior floor cell (canonical) — where the cache stands.</summary>
        public Vector3i Inside { get; }
    }

    private readonly Dictionary<string, List<OrnasaChamber>> _ornasaChambers = new(StringComparer.Ordinal);

    private void WriteOrnasa(in SounderBlocks b, int cx, int floorY, int cz)
    {
        LevelSounderDisc(cx, cz, floorY, OrnasaRadius, b.Moss, b.Dirt);
        WriteOrnasaGround(b, cx, floorY, cz);
        WriteOrnasaAmphitheatre(b, cx, floorY, cz);
        for (int slot = 0; slot < SounderSiteLayout.OrnasaSlots; slot++)
        {
            var (sx, sz) = SounderSiteLayout.OrnasaSlotCentre(cx, cz, slot);
            if (slot % 2 == 0)
            {
                WriteOrnasaHall(b, sx, floorY, sz, cx, cz);
            }
            else
            {
                WriteOrnasaChamber(b, sx, floorY, sz, cx, cz);
            }
        }

        for (int k = 0; k < SounderSiteLayout.BloomTrailLength; k++)
        {
            var cell = SounderSiteLayout.BloomTrailCell(cx, floorY, cz, k);
            SounderClear(cell.X, cell.Y, cell.Z);
            SounderPlant(b, cell.X, floorY, cell.Z, b.Bloom);
        }
    }

    /// <summary>The broken paving and the broken outer wall (gaps where a wall section fell).</summary>
    private void WriteOrnasaGround(in SounderBlocks b, int cx, int floorY, int cz)
    {
        for (int dx = -OrnasaRadius; dx <= OrnasaRadius; dx++)
        {
            for (int dz = -OrnasaRadius; dz <= OrnasaRadius; dz++)
            {
                double d = Math.Sqrt((dx * dx) + (dz * dz));
                int x = cx + dx, z = cz + dz;
                if (d > OrnasaRadius)
                {
                    continue;
                }

                if (d >= OrnasaRadius - 1.5)
                {
                    double sector = SounderHash("ornasa-wall", (int)Math.Floor(Math.Atan2(dx, dz) * 6.0), 0);
                    int height = sector < 0.22 ? 0 : 1 + (int)(SounderHash("ornasa-wall-h", x, z) * 4.0);
                    SounderColumn(x, z, floorY + 1, floorY + height, b.CarvedObsidian);
                    continue;
                }

                if (SounderHash("ornasa-paving", x, z) < 0.35)
                {
                    SounderSet(x, floorY, z, b.CarvedObsidian);
                }
            }
        }
    }

    /// <summary>The amphitheatre: a paved disc, eight broken rune pillars (one to three stones) and a crystal on a plinth.</summary>
    private void WriteOrnasaAmphitheatre(in SounderBlocks b, int cx, int floorY, int cz)
    {
        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dz = -6; dz <= 6; dz++)
            {
                if ((dx * dx) + (dz * dz) <= 40)
                {
                    SounderSet(cx + dx, floorY, cz + dz, b.CarvedObsidian);
                }
            }
        }

        for (int i = 0; i < 8; i++)
        {
            var (px, pz) = SounderSiteLayout.Polar(cx, cz, 5.0, (i * Math.PI / 4.0) + (Math.PI / 8.0));
            int height = 1 + (int)(SounderHash("ornasa-rune", px, pz) * 3.0);
            SounderColumn(px, pz, floorY + 1, floorY + height, b.Rune);
        }

        SounderSet(cx, floorY + 1, cz, b.CarvedObsidian);
        SounderSet(cx, floorY + 2, cz, b.Crystal);
    }

    /// <summary>A ruined hall (7×7): every wall column broken to its own height (brick at the foot, obsidian above, now and
    /// then a rune stone on top), a doorway toward the amphitheatre and a crystal growing in the middle.</summary>
    private void WriteOrnasaHall(in SounderBlocks b, int sx, int floorY, int sz, int cx, int cz)
    {
        var (tx, tz) = SounderSiteLayout.TowardCentre(sx, sz, cx, cz);
        for (int dx = -3; dx <= 3; dx++)
        {
            for (int dz = -3; dz <= 3; dz++)
            {
                int x = sx + dx, z = sz + dz;
                if (Math.Abs(dx) != 3 && Math.Abs(dz) != 3)
                {
                    SounderSet(x, floorY, z, b.Brick); // the old floor
                    continue;
                }

                bool doorway = (dx == tx * 3 && dz == 0 && tz == 0) || (dz == tz * 3 && dx == 0 && tx == 0);
                int height = doorway ? 0 : 1 + (int)(SounderHash("ornasa-hall", x, z) * 5.0);
                for (int y = 1; y <= height; y++)
                {
                    bool top = y == height && height >= 3 && SounderHash("ornasa-hall-rune", x, z) < 0.2;
                    SounderSet(x, floorY + y, z, top ? b.Rune : y == 1 ? b.Brick : b.CarvedObsidian);
                }
            }
        }

        SounderSet(sx, floorY + 1, sz, b.Crystal);
    }

    /// <summary>A sealed sound chamber: a 5×5×4 cube of carved obsidian, hollow inside (3×3×3), its doorway toward the
    /// amphitheatre sealed with obsidian and a crystal set into the wall above it.</summary>
    private void WriteOrnasaChamber(in SounderBlocks b, int sx, int floorY, int sz, int cx, int cz)
    {
        var (tx, tz) = SounderSiteLayout.TowardCentre(sx, sz, cx, cz);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                bool inside = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1;
                for (int y = 1; y <= 4; y++)
                {
                    if (inside && y <= 3)
                    {
                        SounderClear(sx + dx, floorY + y, sz + dz);
                    }
                    else
                    {
                        SounderSet(sx + dx, floorY + y, sz + dz, b.CarvedObsidian); // the walls, the roof — and the seal
                    }
                }
            }
        }

        SounderSet(sx + (tx * 2), floorY + 3, sz + (tz * 2), b.Crystal);
    }

    /// <summary>Every load: registers the chambers and the bloom trail of Ornasa and spawns its caches (idempotent).</summary>
    private void AfterOrnasa(int cx, int floorY, int cz)
    {
        int circ = _world.Circumference;
        var chambers = new List<OrnasaChamber>(SounderSiteLayout.OrnasaChambers);
        var rng = new Random(unchecked((int)WorldGenerator.StableHash("ornasa-loot:" + _world.LocationId)));
        for (int i = 0; i < SounderSiteLayout.OrnasaChambers; i++)
        {
            var seal = Array.ConvertAll(SounderSiteLayout.ChamberSeal(cx, floorY, cz, i), c => WorldConstants.CanonicalBlock(c, circ));
            var inside = WorldConstants.CanonicalBlock(SounderSiteLayout.ChamberInside(cx, floorY, cz, i), circ);
            chambers.Add(new OrnasaChamber(i, seal, inside));
            SpawnStructureLoot("ornasa", "relic_cache", new Vector3f(inside.X + 0.5f, inside.Y, inside.Z + 0.5f), rng);
        }

        _ornasaChambers[_world.LocationId] = chambers;
        for (int slot = 0; slot < SounderSiteLayout.OrnasaSlots; slot += 4)
        {
            var (hx, hz) = SounderSiteLayout.OrnasaSlotCentre(cx, cz, slot);
            SpawnStructureLoot("ornasa", "relic_cache", new Vector3f(hx + 1.5f, floorY + 1f, hz + 1.5f), rng);
        }

        RegisterBloomTrail(cx, floorY, cz);
    }

    /// <summary>The chambers of the active world's Ornasa (empty on every other world).</summary>
    internal IReadOnlyList<OrnasaChamber> OrnasaChambers
        => _ornasaChambers.TryGetValue(_world.LocationId, out var list) ? list : Array.Empty<OrnasaChamber>();

    /// <summary>The chamber whose sealed doorway holds <paramref name="cell"/> (the Resonance Blaster's target), or null.</summary>
    internal OrnasaChamber? OrnasaChamberAtSeal(Vector3i cell)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var chamber in OrnasaChambers)
        {
            foreach (var s in chamber.Seal)
            {
                if (s == c)
                {
                    return chamber;
                }
            }
        }

        return null;
    }

    /// <summary>Whether chamber <paramref name="index"/> of the active world's Ornasa is open (its doorway is air).</summary>
    internal bool IsOrnasaChamberOpen(int index)
    {
        var list = OrnasaChambers;
        if (index < 0 || index >= list.Count)
        {
            return false;
        }

        foreach (var s in list[index].Seal)
        {
            if (!_world.GetBlock(s).IsAir)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Opens chamber <paramref name="index"/> of the active world's Ornasa: its seal cells become air (a persisted edit, so it
    /// stays open). <paramref name="byBlaster"/> raises <c>chamber:opened</c> for <paramref name="session"/> (the items pack's
    /// Resonance Blaster calls it so); the bloom trail opens its chamber with a song instead. False when there is no such
    /// chamber or it was open already.
    /// </summary>
    internal bool OpenOrnasaChamber(PlayerSession? session, int index, bool byBlaster)
    {
        if (index < 0 || index >= OrnasaChambers.Count || IsOrnasaChamberOpen(index))
        {
            return false;
        }

        foreach (var s in OrnasaChambers[index].Seal)
        {
            SetSounderBlockLive(s, BlockId.Air);
        }

        if (byBlaster && session is not null)
        {
            RaiseGameEvent(session, GameEventKeys.ChamberOpened);
        }

        _log.Info($"Ornasa chamber {index} opened on {_world.LocationId}.");
        return true;
    }
}
