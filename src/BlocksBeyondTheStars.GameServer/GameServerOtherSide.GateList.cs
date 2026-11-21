// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The gate list (#3906): the rune gates around a player who is over there, sent to that player so the client can stand
/// a tall light over every door home (<see cref="OtherSideGates"/>). Over there everything is grey, so a gate must be
/// recognisable from far away — and the server is the one that knows where gates are.
/// <para><b>The set</b> is what the server knows without searching the world, within <see cref="GateListRange"/> blocks:
/// the entry gate (always, however far), the monuments whose stones carry a rune group
/// (<see cref="MonumentInstance.RuneGate"/>, counted once from the generated relic), the complete rune shrines (seed
/// arithmetic), the melody sites (the Singing Shrine, the Choir Tower, a ring) and the rune groups the folk pass found
/// in the loaded chunks around the players (<c>OtherSideWorld.Gates</c>). A known site whose surroundings are loaded is
/// checked against the blocks (<see cref="IsGate"/>): stones that were mined are no gate any more. Two candidates closer
/// than <see cref="FolkGateSpacing"/> are one gate.</para>
/// <para><b>When</b>: at the crossing, then whenever the set changed (the folk pass found or lost a gate, a crack was
/// mended) or the player moved <see cref="GateListMoveStep"/> blocks — at most once every
/// <see cref="GateListMinSeconds"/> — and once more, empty, at the return. A list equal to the last one is not sent.</para>
/// <para><b>Cracks</b> belong to the Sounders' story: <see cref="OtherSideGateCracked"/> is its hook, and it calls
/// <see cref="ResendOtherSideGates"/> after a crack was mended.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far from the player a gate is listed (blocks, horizontally and across the seams).</summary>
    internal const float GateListRange = 400f;

    /// <summary>The player moved this far since the last list: it is built again.</summary>
    internal const float GateListMoveStep = 64f;

    /// <summary>Seconds between two lists for one player at least (an explicit resend is not held back).</summary>
    internal const double GateListMinSeconds = 3.0;

    /// <summary>No list names more gates than this (the nearest are kept, the entry gate always).</summary>
    internal const int GateListMax = 24;

    /// <summary>What one player over there was last told.</summary>
    private sealed class GateListState
    {
        public string LocationId = string.Empty;
        public Vector3f SentAt;
        public double NextAt;
        public bool Dirty;
        public int FolkSignature;
        public NetOtherSideGate[] Last = Array.Empty<NetOtherSideGate>();
    }

    private readonly Dictionary<string, GateListState> _otherSideGateLists = new(StringComparer.Ordinal);

    private readonly List<string> _otherSideGateListGone = new();

    /// <summary>The Sounders' story's hook (#3909): whether a crack runs through the gate at <paramref name="centre"/> of
    /// the world <paramref name="locationId"/>. Without an implementation no gate is cracked.</summary>
    partial void OtherSideGateCracked(string locationId, Vector3f centre, ref bool cracked);

    // ---------------- The API ----------------

    /// <summary>Sends <paramref name="s"/> the gate list again, at once (a crack was mended, a gate was built). Nothing
    /// happens for a player who is not over there. Expects the player's world under the cursor, like every handler; from
    /// anywhere else the list follows with the next pass of the watch.</summary>
    internal void ResendOtherSideGates(PlayerSession s)
    {
        if (!s.OtherSide)
        {
            return;
        }

        var state = GateListStateOf(s);
        if (s.CurrentLocationId != _world.LocationId)
        {
            state.Dirty = true;
            state.NextAt = 0;
            return;
        }

        SendOtherSideGates(s, state, force: true);
    }

    /// <summary>The crossing: the first list.</summary>
    private void OtherSideGatesOnCross(PlayerSession s)
    {
        if (s.CurrentLocationId == _world.LocationId)
        {
            var state = GateListStateOf(s);
            state.Last = Array.Empty<NetOtherSideGate>();
            SendOtherSideGates(s, state, force: true);
        }
    }

    /// <summary>The return: the lights go out — an empty list.</summary>
    private void OtherSideGatesOnReturn(PlayerSession s)
    {
        _otherSideGateLists.Remove(s.State.PlayerId);
        Send(s, new OtherSideGates());
    }

    private GateListState GateListStateOf(PlayerSession s)
    {
        if (!_otherSideGateLists.TryGetValue(s.State.PlayerId, out var state))
        {
            _otherSideGateLists[s.State.PlayerId] = state = new GateListState { Dirty = true };
        }

        state.LocationId = s.CurrentLocationId;
        return state;
    }

    // ---------------- The pass ----------------

    /// <summary>Once a second per world (from <see cref="TickOtherSide"/>, after the folk pass): a player over there whose
    /// set of gates may have changed — the folk pass found or lost one, or the player moved on — gets the list again, at
    /// most once every <see cref="GateListMinSeconds"/>. Without anybody over there and nothing remembered: one field
    /// read.</summary>
    private void TickOtherSideGateLists(OtherSideWorld world)
    {
        int signature = FolkGateSignature(world);
        double stepSq = (double)GateListMoveStep * GateListMoveStep;
        bool any = false;
        foreach (var s in PlayersOverThere())
        {
            any = true;
            var state = GateListStateOf(s);
            if (state.FolkSignature != signature || WrapDistSq(state.SentAt, s.State.Position) >= stepSq)
            {
                state.Dirty = true;
            }

            if (state.Dirty && _uptime >= state.NextAt)
            {
                state.FolkSignature = signature;
                SendOtherSideGates(s, state, force: false);
            }
        }

        if (_otherSideGateLists.Count > 0)
        {
            ForgetGateListsOfThoseWhoLeft(any);
        }
    }

    /// <summary>A player who left the game over there never got the return: their entry goes with this pass.</summary>
    private void ForgetGateListsOfThoseWhoLeft(bool anyOverThere)
    {
        _otherSideGateListGone.Clear();
        foreach (var pair in _otherSideGateLists)
        {
            if (pair.Value.LocationId == _world.LocationId && !(anyOverThere && IsOverThereHere(pair.Key)))
            {
                _otherSideGateListGone.Add(pair.Key);
            }
        }

        foreach (string id in _otherSideGateListGone)
        {
            _otherSideGateLists.Remove(id);
        }
    }

    private bool IsOverThereHere(string playerId)
    {
        foreach (var s in PlayersOverThere())
        {
            if (s.State.PlayerId == playerId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A number that moves when the folk pass found or lost a gate.</summary>
    private static int FolkGateSignature(OtherSideWorld world)
    {
        int h = world.Gates.Count;
        foreach (var gate in world.Gates)
        {
            h = unchecked((h * 31) + (int)MathF.Floor(gate.Centre.X) + ((int)MathF.Floor(gate.Centre.Z) * 7919));
        }

        return h;
    }

    private void SendOtherSideGates(PlayerSession s, GateListState state, bool force)
    {
        var gates = OtherSideGatesFor(s);
        state.Dirty = false;
        state.SentAt = s.State.Position;
        state.NextAt = _uptime + GateListMinSeconds;
        if (!force && SameGates(gates, state.Last))
        {
            return;
        }

        state.Last = gates;
        Send(s, new OtherSideGates { Gates = gates });
    }

    private static bool SameGates(NetOtherSideGate[] a, NetOtherSideGate[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z || a[i].Cracked != b[i].Cracked || a[i].Entry != b[i].Entry)
            {
                return false;
            }
        }

        return true;
    }

    // ---------------- The set ----------------

    /// <summary>The gates <paramref name="s"/> is told about, nearest first (see the class summary for the set). On the
    /// active world, which must be the player's.</summary>
    internal NetOtherSideGate[] OtherSideGatesFor(PlayerSession s)
    {
        var at = s.State.Position;
        var found = new List<(Vector3f Centre, bool Entry, double DistSq)>();
        if (EntryGateOf(s) is { } entry)
        {
            found.Add((entry, true, WrapDistSq(entry, at)));
        }

        if (_otherSideWorlds.TryGetValue(_world.LocationId, out var world))
        {
            foreach (var gate in world.Gates)
            {
                AddGateCandidate(found, gate.Centre, at); // found in the loaded chunks, checked against the blocks
            }
        }

        foreach (var (site, trusted) in KnownGateSites(at))
        {
            if (GateListReaches(site, at, out _) && GateStandsAt(site, trusted, out var centre))
            {
                AddGateCandidate(found, centre, at);
            }
        }

        found.Sort((a, b) => a.Entry != b.Entry ? (a.Entry ? -1 : 1) : a.DistSq.CompareTo(b.DistSq));
        if (found.Count > GateListMax)
        {
            found.RemoveRange(GateListMax, found.Count - GateListMax);
        }

        found.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        var list = new NetOtherSideGate[found.Count];
        for (int i = 0; i < list.Length; i++)
        {
            var c = found[i].Centre;
            bool cracked = false;
            OtherSideGateCracked(_world.LocationId, c, ref cracked);
            list[i] = new NetOtherSideGate { X = c.X, Y = c.Y, Z = c.Z, Cracked = cracked, Entry = found[i].Entry };
        }

        return list;
    }

    /// <summary>Adds a gate within <see cref="GateListRange"/> of the player that is not one of the gates already found.</summary>
    private void AddGateCandidate(List<(Vector3f Centre, bool Entry, double DistSq)> found, Vector3f centre, Vector3f player)
    {
        if (!GateListReaches(centre, player, out double distSq))
        {
            return;
        }

        foreach (var known in found)
        {
            if (WrapDistSq(known.Centre, centre) <= FolkGateSpacing * FolkGateSpacing)
            {
                return;
            }
        }

        found.Add((centre, false, distSq));
    }

    /// <summary>Whether <paramref name="at"/> lies within <see cref="GateListRange"/> of the player, horizontally and across
    /// the seams; <paramref name="distSq"/> is that distance squared.</summary>
    private bool GateListReaches(Vector3f at, Vector3f player, out double distSq)
    {
        distSq = WrapDistSq(new Vector3f(at.X, player.Y, at.Z), player);
        return distSq <= (double)GateListRange * GateListRange;
    }

    /// <summary>Where the server knows a gate may stand, without reading a block: the monuments that carry a rune group,
    /// the complete rune shrines no stamp was built over, and the melody sites of this world. <c>Trusted</c> sites are
    /// known to carry rune stones (the generated relic, the shrine's ring, the Singing Shrine's pillars) and are listed
    /// from afar; any other melody site (a ring of other stone, the Choir Tower's crystal pipes) is a gate only when the
    /// blocks around it say so.</summary>
    private IEnumerable<(Vector3f Site, bool Trusted)> KnownGateSites(Vector3f at)
    {
        foreach (var m in _monuments)
        {
            if (m.RuneGate is { } gate)
            {
                yield return (gate, true);
            }
        }

        foreach (var shrine in _generator.RuneShrinesNear(_world.Planet, (int)MathF.Floor(at.X), (int)MathF.Floor(at.Z), (int)GateListRange))
        {
            if (shrine.Complete && !RuneShrineBuiltOver(shrine))
            {
                yield return (new Vector3f(shrine.X + 0.5f, shrine.FloorY + 1f, shrine.Z + 0.5f), true);
            }
        }

        ShrineMelodySite(); // the Singing Shrine registers on first need
        if (_melodySites.TryGetValue(_world.LocationId, out var sites))
        {
            foreach (var site in sites.Values)
            {
                yield return (site.CentreF, site.Kind == MelodySiteKindShrine);
            }
        }
    }

    /// <summary>Whether a gate stands at a known site. Where the chunks around it are loaded the blocks decide (and give
    /// the real centre) — stones a player mined are no gate; a trusted site far away is taken at its word.</summary>
    private bool GateStandsAt(Vector3f site, bool trusted, out Vector3f centre)
    {
        centre = site;
        var cell = new Vector3i((int)MathF.Floor(site.X), (int)MathF.Floor(site.Y), (int)MathF.Floor(site.Z));
        return _world.IsNeighbourhoodLoaded(cell, EchoRuneScanRadius) ? IsGate(site, out centre) : trusted;
    }

    /// <summary>
    /// The rune group of a monument as it was generated: the middle of the largest group of rune stones that lie within
    /// <see cref="EchoRuneScanRadius"/> blocks of one of them — or null when no three stand that close (a lone inscription
    /// is no gate). Called once per monument when its instance is registered.
    /// </summary>
    private Vector3f? MonumentRuneGate(SettlementStructure structure, Vector3i origin, int groundY)
    {
        if (_content.GetBlock("rune_stone") is not { } rune)
        {
            return null;
        }

        var stones = new List<Vector3i>();
        for (int x = 0; x < structure.Width; x++)
        {
            for (int y = 0; y < structure.Height; y++)
            {
                for (int z = 0; z < structure.Length; z++)
                {
                    if (structure.Get(x, y, z) == rune.NumericId.Value)
                    {
                        stones.Add(new Vector3i(origin.X + x, groundY + y, origin.Z + z));
                    }
                }
            }
        }

        return LargestRuneGroup(stones, EchoRuneScanRadius, EchoRuneGroupSize);
    }

    /// <summary>The middle of the largest group of <paramref name="stones"/> within <paramref name="radius"/> cells (per
    /// axis) of one of them, when it has at least <paramref name="minStones"/> — the rule of <c>TryFindRuneGroup</c>, on
    /// a list instead of the world.</summary>
    internal static Vector3f? LargestRuneGroup(IReadOnlyList<Vector3i> stones, int radius, int minStones)
    {
        int best = 0;
        Vector3f? centre = null;
        foreach (var anchor in stones)
        {
            int count = 0;
            double sx = 0, sy = 0, sz = 0;
            foreach (var p in stones)
            {
                if (Math.Abs(p.X - anchor.X) <= radius && Math.Abs(p.Y - anchor.Y) <= radius && Math.Abs(p.Z - anchor.Z) <= radius)
                {
                    count++;
                    sx += p.X + 0.5;
                    sy += p.Y;
                    sz += p.Z + 0.5;
                }
            }

            if (count >= minStones && count > best)
            {
                best = count;
                centre = new Vector3f((float)(sx / count), (float)(sy / count), (float)(sz / count));
            }
        }

        return centre;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: <see cref="OtherSideGatesFor"/> with the player's world under the cursor.</summary>
    public NetOtherSideGate[] OtherSideGatesForTest(PlayerSession s)
    {
        Serve(s);
        return OtherSideGatesFor(s);
    }

    /// <summary>Test seam: <see cref="ResendOtherSideGates"/> with the player's world under the cursor.</summary>
    public void ResendOtherSideGatesForTest(PlayerSession s)
    {
        Serve(s);
        ResendOtherSideGates(s);
    }
}
