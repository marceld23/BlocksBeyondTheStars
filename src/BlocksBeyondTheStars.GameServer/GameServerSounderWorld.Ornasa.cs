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
/// bell-bloom trail leads (#3367) and opens when the trail's last bloom does. The blocks are <see cref="SoaniriBuilder"/>'s
/// (#3522); this part re-derives the chambers, the caches and the trail from the record on every load.
/// </summary>
public sealed partial class GameServer
{
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
