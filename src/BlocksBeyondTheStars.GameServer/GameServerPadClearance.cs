// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Built-over landing pads (#3146). A pad's landing volume is kept clear by the no-build rule — for everyone but admins,
/// and every singleplayer host is one. A tester turned the communal pad in the middle of his spaceport into a tower
/// foundation once his ship landed on his own base pads, and the travelling trader kept parking its hull inside the tower:
/// "free" only ever asked who HOLDS a pad, never what stands on it. A pad whose landing square holds a solid block somebody
/// placed (open props such as torches and flowers do not count) is therefore <b>built over</b>: traders skip it, an automatic
/// pick skips it, a hand-picked landing on it is refused and the chooser shows it. Clearing the blocks reopens it. A ship
/// already parked on its pad is never moved because of it. The question goes to the block-edit store, so it has the same
/// answer for a body whose world is not loaded. What the server writes by itself never counts (#3176): a write keeps the
/// owner of a cell a player once touched, so weather snow (the store's <c>weather_deposit</c> rows) and flowing water or lava
/// on a paved pad used to close it until the drift melted.
/// </summary>
public sealed partial class GameServer
{
    private ushort[]? _hullPassableIds;

    /// <summary>Answers of one pad-list broadcast (<see cref="BroadcastLandingPads"/>): the store is asked once per pad, not
    /// once per pad and receiver. Null outside a broadcast — a single answer is never kept, so a build shows at once.</summary>
    private Dictionary<(string Body, int X, int Y, int Z), bool>? _padBuiltOverScope;

    /// <summary>The blocks that never close a pad: walk-through props a parked hull may stand in (flowers, crops, grass, torches —
    /// most flora is marked solid and passes only as a prop) and other non-solid blocks, but no door-like block (an energy gate,
    /// a switched-off force field or an open trapdoor turns solid again inside the hull). And (#3176) no fluid or liquid either:
    /// water and lava flow by themselves, and the server's flow steps keep the owner of a cell a player once mined or paved —
    /// every spaceport is made of such cells — so a puddle that ran onto a pad used to read as "built". Weather snow is the
    /// store's to tell apart (<see cref="BlocksBeyondTheStars.Persistence.IWorldRepository.HasPlayerBuiltBlocks"/>).</summary>
    private IReadOnlyCollection<ushort> HullPassableIds()
        => _hullPassableIds ??= _content.Blocks.Values
            .Where(d => (!d.Solid || BlocksBeyondTheStars.Shared.Definitions.WalkThroughBlocks.IsProp(d.Key) || d.Liquid
                         || d.Key is "water" or "lava")
                        && d.Category != "door")
            .Select(d => d.NumericId.Value).Where(id => id != 0).ToArray();

    /// <summary>Whether a pad centred on (<paramref name="cx"/>, <paramref name="cz"/>) with its ground at
    /// <paramref name="groundY"/> carries a placed block in its landing volume — the square of half-size
    /// <paramref name="half"/>, from the floor a ship stands on up to the clearance.</summary>
    private bool PadVolumeBuiltOver(string bodyId, int cx, int groundY, int cz, int half)
    {
        var key = (bodyId, cx, groundY, cz);
        if (_padBuiltOverScope is not null && _padBuiltOverScope.TryGetValue(key, out bool known))
        {
            return known;
        }

        bool built = PadVolumeBuiltOverUncached(bodyId, cx, groundY, cz, half);
        if (_padBuiltOverScope is not null)
        {
            _padBuiltOverScope[key] = built;
        }

        return built;
    }

    private bool PadVolumeBuiltOverUncached(string bodyId, int cx, int groundY, int cz, int half)
    {
        int circ = BodyCircumference(bodyId);
        if (circ <= 0)
        {
            return false;
        }

        var passable = HullPassableIds();
        int latPeriod = WorldConstants.LatitudePeriodFor(circ);
        foreach (var (xLo, xHi) in CanonicalRanges(cx - half, cx + half, 0, circ))
        {
            foreach (var (zLo, zHi) in CanonicalRanges(cz - half, cz + half, -latPeriod / 2, latPeriod))
            {
                if (_repo.HasPlayerBuiltBlocks(bodyId, new Vector3i(xLo, groundY + 1, zLo),
                        new Vector3i(xHi, groundY + PadClearanceHeight, zHi), passable))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A communal (or base, via <see cref="LandingPad.BasePad"/>) pad of <paramref name="bodyId"/> is built over.</summary>
    private bool PadBuiltOver(string bodyId, LandingPad pad)
        => PadVolumeBuiltOver(bodyId, pad.CenterX, pad.CenterY, pad.CenterZ, pad.Radius);

    /// <summary>A base pad's landing square is built over (its owner, an ally or an admin built in it).</summary>
    private bool BasePadBuiltOver(ServerBasePad pad)
        => PadVolumeBuiltOver(pad.Planet, pad.Cell.X, pad.Cell.Y, pad.Cell.Z, BasePadRules.HalfSize);

    /// <summary>The communal pad is the one <paramref name="playerId"/> holds on the body — their parked (or reserved)
    /// ship's pad, which nothing closes for them.</summary>
    private bool HoldsCommunalPad(string playerId, string bodyId, int padIndex)
        => playerId.Length > 0 && FindSessionByPlayerId(playerId) is { } s
           && s.AssignedPadIndex == padIndex && s.AssignedBasePad is null
           && string.Equals(s.CurrentLocationId, bodyId, System.StringComparison.Ordinal);

    /// <summary>A communal pad is closed to <paramref name="playerId"/> (empty = a trader) because it is built over — unless it
    /// is the pad they already hold.</summary>
    private bool CommunalPadClosed(string bodyId, LandingPad pad, string playerId)
        => !HoldsCommunalPad(playerId, bodyId, pad.Index) && PadBuiltOver(bodyId, pad);

    /// <summary>Marks a pad-list entry the receiver does not hold as built over: drawn locked with its reason, never offered.
    /// <see cref="NetLandingPad.Occupied"/> is set too, so an older client still reads it as not landable.</summary>
    private static void MarkBuiltOver(BlocksBeyondTheStars.Networking.Messages.NetLandingPad entry)
    {
        entry.BuiltOver = true;
        entry.Occupied = true;
    }

    // ---------------- #3147: telling the admin who builds there ----------------

    /// <summary>The landing pad whose landing volume (the floor a ship stands on up to the clearance) holds this cell of the
    /// active world — "&lt;body&gt;/pad:&lt;index&gt;" for a communal pad, "&lt;body&gt;/basepad:&lt;cell&gt;" for a base pad
    /// (pad indices repeat on every body) — or null. Ground cells under the pad do not count: a block there never closes it.</summary>
    private string? LandingPadKeyAt(Vector3i pos)
    {
        string body = _world.LocationId;
        foreach (var pad in _landingPads)
        {
            if (OnPadColumn(pad, pos.X, pos.Z) && pos.Y > pad.CenterY && pos.Y <= pad.CenterY + PadClearanceHeight)
            {
                return body + "/pad:" + pad.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        foreach (var pad in _basePads)
        {
            if (pad.Planet == body && InBasePadSquare(pad, pos, -1, BasePadRules.Clearance))
            {
                return System.FormattableString.Invariant($"{body}/basepad:{pad.Cell.X},{pad.Cell.Y},{pad.Cell.Z}");
            }
        }

        return null;
    }

    /// <summary>Says "while something is built on it, no ship lands here" once per pad and session (#3147) — the no-build
    /// rule lets an admin build there (hint instead of a ban), but nothing else would tell them the pad is now closed.</summary>
    private void SayPadBuildHintOnce(PlayerSession session, string? padKey)
    {
        if (padKey is not null && session.PadBuildHints.Add(padKey))
        {
            Send(session, new BlocksBeyondTheStars.Networking.Messages.ServerMessage { Text = "@srv.place.pad_admin_hint" });
        }
    }

    /// <summary>Test/inspection (#3146): whether a communal pad of a body counts as built over.</summary>
    public bool CommunalPadBuiltOverForTest(string bodyId, int padIndex)
        => PadsForBody(bodyId).FirstOrDefault(p => p.Index == padIndex) is { } pad && PadBuiltOver(bodyId, pad);
}
