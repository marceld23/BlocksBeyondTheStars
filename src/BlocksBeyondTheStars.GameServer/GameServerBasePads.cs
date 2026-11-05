// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Base landing pads (#3069, epic #3074 — Marcel 2026-11-01): a player who founded a base builds up to
/// <see cref="BasePadRules.MaxPerBase"/> pads next to it and lands their ship at home. A pad is a <c>base_pad</c> block —
/// the centre of a 17 × 17 landing square like a communal pad, so every ship fits — plus this entity: owner, name and who may
/// land (<see cref="BasePadAccess"/>). It belongs to the nearest own base core within <see cref="BasePadRules.ReachXZ"/>
/// blocks, worked out live: mining that core makes the pad inactive (not offered for landing) until a core stands near it
/// again. The landing volume above the pad is kept clear for everyone, and the square is protected like the base.
/// <para>Communal pads are addressed by their index on the body; a base pad by its cell. A session holds one or the other
/// (<see cref="PlayerSession.AssignedPadIndex"/> / <see cref="PlayerSession.AssignedBasePad"/>), and every landing path asks
/// <see cref="PlayerPad"/>, which hands out a base pad as an ordinary <see cref="LandingPad"/> — ship placement, respawn,
/// speeder parking and the transit effects need no case of their own.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A placed base landing pad. Server-wide (the land chooser asks about bodies that are not loaded).</summary>
    internal sealed class ServerBasePad
    {
        public string Planet = string.Empty;
        public Vector3i Cell;
        public string Name = string.Empty;
        public string OwnerId = string.Empty;
        public BasePadAccess Access = BasePadRules.DefaultAccess;
    }

    private readonly List<ServerBasePad> _basePads = new();

    /// <summary>Base pads (body, cell, name, owner, access) for tests and inspection.</summary>
    public IReadOnlyList<(string Body, Vector3i Cell, string Name, string OwnerId, BasePadAccess Access)> BasePadSnapshots
        => _basePads.Select(p => (p.Planet, p.Cell, p.Name, p.OwnerId, p.Access)).ToList();

    /// <summary>Loads every base pad at server start (the blocks come back via the block-edit store). Idempotent.</summary>
    private void LoadAllBasePads()
    {
        _basePads.Clear();
        foreach (var sp in _repo.ListAllBasePads())
        {
            _basePads.Add(new ServerBasePad
            {
                Planet = sp.Planet,
                Cell = new Vector3i(sp.X, sp.Y, sp.Z),
                Name = sp.Name,
                OwnerId = sp.OwnerId,
                Access = BasePadRules.AccessOf(sp.Access),
            });
        }

        if (_basePads.Count > 0)
        {
            _log.Info($"Loaded {_basePads.Count} base landing pad(s).");
        }
    }

    /// <summary>A landing from flight that goes through travel (another body, or a world not loaded): the same pad choice,
    /// a communal index or a base pad's cell (#3069).</summary>
    private static TravelIntent TravelFor(LeaveSpaceIntent landing, string bodyId)
        => new()
        {
            DestinationBodyId = bodyId,
            PadIndex = landing.PadIndex,
            HasPadCell = landing.HasPadCell,
            PadCellX = landing.PadCellX,
            PadCellY = landing.PadCellY,
            PadCellZ = landing.PadCellZ,
        };

    /// <summary>The base pad cell a landing intent names, or null for a communal landing.</summary>
    private static Vector3i? PadCellOf(bool hasCell, int x, int y, int z) => hasCell ? new Vector3i(x, y, z) : null;

    private static StoredBasePad ToStored(ServerBasePad p)
        => new() { Planet = p.Planet, X = p.Cell.X, Y = p.Cell.Y, Z = p.Cell.Z, Name = p.Name, OwnerId = p.OwnerId, Access = (int)p.Access };

    /// <summary>The pad at a cell on a body, or null.</summary>
    private ServerBasePad? BasePadAt(string body, Vector3i cell)
        => _basePads.FirstOrDefault(p => p.Planet == body && p.Cell == cell);

    /// <summary>A placed base_pad block registers its pad (the placement rule already checked the spot). The block itself is set
    /// and persisted by the normal place path.</summary>
    private void PlaceBasePad(PlayerSession session, Vector3i pos, string label)
    {
        string clean = ScreenPlayerName(session, SanitizeBasePadName(label), "landing pad") ?? string.Empty;
        var pad = new ServerBasePad
        {
            Planet = _world.LocationId,
            Cell = pos,
            Name = clean,
            OwnerId = session.State.PlayerId,
            Access = BasePadRules.DefaultAccess,
        };
        _basePads.Add(pad);
        _repo.SaveBasePad(ToStored(pad));
        Send(session, new ServerMessage { Text = "@srv.basepad.placed" });
        ShipAiHintOnce(session, "base_pad"); // VEGA: who may land is set at the pad (E)
        BroadcastLandingPads();
    }

    /// <summary>A base_pad block was mined or blasted: forget its pad. Safe for any cell.</summary>
    private void RemoveBasePadAt(Vector3i pos)
    {
        var pad = BasePadAt(_world.LocationId, pos);
        if (pad is null)
        {
            return;
        }

        _basePads.Remove(pad);
        _repo.DeleteBasePad(pad.Planet, pos.X, pos.Y, pos.Z);
        BroadcastLandingPads();
    }

    /// <summary>The owner names a pad and sets who may land (E at the pad). Admins may too.</summary>
    private void HandleSetBasePad(PlayerSession session, SetBasePadIntent intent)
    {
        var pad = BasePadAt(session.CurrentLocationId, new Vector3i(intent.CellX, intent.CellY, intent.CellZ));
        if (pad is null)
        {
            Reject(session, "basepad", "@srv.basepad.none_here");
            return;
        }

        if (pad.OwnerId != session.State.PlayerId && !session.State.IsAdmin)
        {
            Reject(session, "basepad", "@srv.basepad.not_owner");
            return;
        }

        if (ScreenPlayerName(session, SanitizeBasePadName(intent.Name), "landing pad") is not { } name)
        {
            return; // refused by the content screen (#1221) — the player has been told
        }

        pad.Name = name;
        pad.Access = BasePadRules.AccessOf(intent.Access);
        _repo.SaveBasePad(ToStored(pad));
        Send(session, new ServerMessage { Text = "@srv.basepad.saved" });
        BroadcastLandingPads(); // the sender's body is the active world: a changed access re-draws every chooser and map
    }

    /// <summary>Trims a player-typed pad name to one short line (an empty name: the client shows a localized default).</summary>
    private static string SanitizeBasePadName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = StripControlChars(raw!);
        return trimmed.Length > BasePadRules.NameMaxLength ? trimmed.Substring(0, BasePadRules.NameMaxLength) : trimmed;
    }

    // ---------------- which base a pad belongs to ----------------

    /// <summary>The base a pad belongs to: the owner's base core on the pad's body nearest to it within the pad reach, ties by
    /// the core cell — or null, and the pad is inactive.</summary>
    private ServerBase? BaseOfPad(ServerBasePad pad) => NearestOwnCoreInReach(pad.OwnerId, pad.Planet, pad.Cell);

    private ServerBase? NearestOwnCoreInReach(string ownerId, string body, Vector3i cell)
    {
        int circ = BodyCircumference(body);
        ServerBase? best = null;
        long bestSq = long.MaxValue;
        foreach (var b in _bases)
        {
            if (b.OwnerId != ownerId || b.Planet != body)
            {
                continue;
            }

            long dx = circ > 0 ? WorldConstants.WrapDeltaX(cell.X - b.Cell.X, circ) : cell.X - b.Cell.X;
            long dz = circ > 0 ? WorldConstants.WrapDeltaZ(cell.Z - b.Cell.Z, circ) : cell.Z - b.Cell.Z;
            long dy = cell.Y - b.Cell.Y;
            if (System.Math.Abs(dx) > BasePadRules.ReachXZ || System.Math.Abs(dz) > BasePadRules.ReachXZ || System.Math.Abs(dy) > BasePadRules.ReachY)
            {
                continue;
            }

            long sq = (dx * dx) + (dy * dy) + (dz * dz);
            if (best is null || sq < bestSq || (sq == bestSq && CellBefore(b.Cell, best.Cell)))
            {
                best = b;
                bestSq = sq;
            }
        }

        return best;
    }

    private static bool CellBefore(Vector3i a, Vector3i b)
        => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

    /// <summary>How many pads belong to a base.</summary>
    private int PadsOfBase(ServerBase b)
        => _basePads.Count(p => p.Planet == b.Planet && p.OwnerId == b.OwnerId && BaseOfPad(p) == b);

    // ---------------- protection ----------------

    /// <summary>Whether a cell lies in a pad's square, between <paramref name="below"/> blocks under the pad block and
    /// <paramref name="above"/> blocks over it (active world).</summary>
    private bool InBasePadSquare(ServerBasePad pad, Vector3i pos, int below, int above)
        => System.Math.Abs(WorldConstants.WrapDeltaX(pos.X - pad.Cell.X, _world.Circumference)) <= BasePadRules.HalfSize
           && System.Math.Abs(WorldConstants.WrapDeltaZ(pos.Z - pad.Cell.Z, _world.Circumference)) <= BasePadRules.HalfSize
           && pos.Y >= pad.Cell.Y - below && pos.Y <= pad.Cell.Y + above;

    /// <summary>The landing volume of a base pad on the active world (the 17 × 17 square, one block above the pad up to the
    /// clearance): nobody builds there — the owner included — so the pad can never be built shut.</summary>
    private bool IsBasePadVolume(Vector3i pos)
    {
        foreach (var pad in _basePads)
        {
            if (pad.Planet == _world.LocationId && InBasePadSquare(pad, pos, -1, BasePadRules.Clearance))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A pad's square (two blocks below the pad up to the clearance) is protected like a base: the owner and admins
    /// edit freely, allies everything but the pad block itself, anyone else nothing.</summary>
    private bool IsBasePadProtected(Vector3i pos, string actorId, bool actorIsAdmin)
    {
        foreach (var pad in _basePads)
        {
            if (pad.Planet != _world.LocationId || !InBasePadSquare(pad, pos, 2, BasePadRules.Clearance))
            {
                continue;
            }

            if (actorIsAdmin || pad.OwnerId == actorId)
            {
                continue;
            }

            if (pos != pad.Cell && AreAllied(pad.OwnerId, actorId))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>A pad block with a ship on it (or held by a player up in space, or a trader's) cannot be mined.</summary>
    private bool BasePadBusy(Vector3i pos)
        => BasePadAt(_world.LocationId, pos) is { } pad && BasePadHolderName(pad.Planet, pad.Cell, string.Empty) is not null;

    // ---------------- occupancy + landing ----------------

    /// <summary>Who holds a base pad on a body (a player — also one up in space, like a communal pad, #957 — or a landed
    /// trader), other than <paramref name="exceptPlayerId"/>; null when free.</summary>
    private string? BasePadHolderName(string body, Vector3i cell, string exceptPlayerId)
    {
        if (_landedTraders.TryGetValue(body, out var lt) && lt.BasePad == cell)
        {
            return lt.Name;
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !s.Spectating && s.CurrentLocationId == body && s.State.PlayerId != exceptPlayerId
                && s.AssignedBasePad is { } held && held == cell)
            {
                return s.State.Name;
            }
        }

        return null;
    }

    /// <summary>Whether a player may land on a pad by its access setting (admins always may).</summary>
    private bool MayLandOnBasePad(ServerBasePad pad, string playerId)
        => BasePadRules.Admits(pad.Access, pad.OwnerId == playerId, AreAllied(pad.OwnerId, playerId))
           || (FindSessionByPlayerId(playerId)?.State.IsAdmin ?? false);

    /// <summary>Claims a base pad for a landing: it exists on the body, belongs to a base, admits the player, nobody else
    /// holds it and nobody built in its square (#3146: the no-build rule keeps it clear for everyone but admins — and every
    /// singleplayer host is one; the block-edit store answers for an unloaded body too). The player's own held pad is never
    /// closed to them. False with the refusal's locale key.</summary>
    private bool TryClaimBasePad(PlayerSession session, string body, Vector3i cell, out string reason)
    {
        reason = string.Empty;
        string me = session.State.PlayerId;
        var pad = BasePadAt(body, cell);
        if (pad is null || BaseOfPad(pad) is null)
        {
            reason = "@srv.basepad.gone";
            return false;
        }

        if (!MayLandOnBasePad(pad, me))
        {
            reason = "@srv.basepad.locked";
            return false;
        }

        if (BasePadHolderName(body, cell, me) is not null)
        {
            reason = "@srv.land.pad_taken";
            return false;
        }

        if (!HoldsBasePad(session, body, cell) && BasePadBuiltOver(pad))
        {
            reason = "@srv.land.pad_built_over";
            return false;
        }

        session.AssignedBasePad = cell;
        session.AssignedPadIndex = -1;
        return true;
    }

    /// <summary>#3072: the player's own free, active base pad on a body for a landing they did not pick by hand — "coming
    /// home". Several: a fixed order by cell, so the pick is always the same; one they built over is skipped (#3146) unless
    /// they hold it. Null when there is none.</summary>
    private ServerBasePad? OwnFreeBasePad(PlayerSession session, string body)
    {
        string me = session.State.PlayerId;
        return _basePads
            .Where(p => p.Planet == body && p.OwnerId == me && BaseOfPad(p) is not null && BasePadHolderName(body, p.Cell, me) is null)
            .OrderBy(p => p.Cell.X).ThenBy(p => p.Cell.Y).ThenBy(p => p.Cell.Z)
            .FirstOrDefault(p => HoldsBasePad(session, body, p.Cell) || !BasePadBuiltOver(p));
    }

    /// <summary>The session holds this base pad on the body — its parked (or reserved) ship's pad.</summary>
    private static bool HoldsBasePad(PlayerSession session, string body, Vector3i cell)
        => session.AssignedBasePad is { } held && held == cell
           && string.Equals(session.CurrentLocationId, body, System.StringComparison.Ordinal);

    /// <summary>The base pad a session holds on the active world, as a <see cref="LandingPad"/> for the placement code — or
    /// null when it holds none (then its communal pad applies). A pad that is gone, or that someone else holds, is released.
    /// A pad that went inactive or shut the player out keeps them: nothing evicts a parked ship.</summary>
    private LandingPad? HeldBasePad(PlayerSession session)
    {
        if (session.AssignedBasePad is not { } cell || !string.Equals(_world.LocationId, session.CurrentLocationId, System.StringComparison.Ordinal))
        {
            return null; // none — or the ship is placed on another world (the interior in orbit): the claim stays as it is
        }

        var pad = BasePadAt(_world.LocationId, cell);
        if (pad is null || BasePadHolderName(pad.Planet, cell, session.State.PlayerId) is not null)
        {
            session.AssignedBasePad = null;
            return null;
        }

        return AsLandingPad(pad);
    }

    private static LandingPad AsLandingPad(ServerBasePad pad)
        => new() { Index = -1, CenterX = pad.Cell.X, CenterY = pad.Cell.Y, CenterZ = pad.Cell.Z, Radius = BasePadRules.HalfSize, BasePad = true };

    /// <summary>The ground a ship parks on at a base pad centred on a column of the active world: the pad block's own height
    /// (the ship's floor stands one block above it). Null when no base pad is centred there.</summary>
    private int? BasePadGroundAt(int cx, int cz)
    {
        foreach (var pad in _basePads)
        {
            if (pad.Planet == _world.LocationId && pad.Cell.Z == cz
                && WorldConstants.WrapX(pad.Cell.X, _world.Circumference) == WorldConstants.WrapX(cx, _world.Circumference))
            {
                return pad.Cell.Y;
            }
        }

        return null;
    }

    /// <summary>Columns of a would-be pad's landing volume on the active world that hold something a hull cannot stand in — a
    /// solid block or a fluid (grass, flowers and other open props do not count). 0 means a ship can set down there.</summary>
    private int BlockedColumns(Vector3i cell)
    {
        int blocked = 0;
        for (int dx = -BasePadRules.HalfSize; dx <= BasePadRules.HalfSize; dx++)
        {
            for (int dz = -BasePadRules.HalfSize; dz <= BasePadRules.HalfSize; dz++)
            {
                for (int dy = 1; dy <= BasePadRules.Clearance; dy++)
                {
                    var b = _world.GetBlockIfLoaded(new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz));
                    if (!b.IsAir && (IsFluid(b.Value) || (_world.Definition(b)?.Solid ?? true)))
                    {
                        blocked++;
                        break;
                    }
                }
            }
        }

        return blocked;
    }

    // ---------------- placement ----------------

    /// <summary>Rule "base_pad": a landing pad stands near one of the player's own bases (#3069), at most four per base, with
    /// its whole landing square clear and touching no other pad and no stranger's base.</summary>
    private PlacementVerdict BasePadRule(PlayerSession session, PlacementRequest r)
        => r.BlockDef.Key == BasePadRules.BlockKey && BasePadRefusal(session, r.Pos) is { } refusal
            ? PlacementVerdict.Refuse(refusal)
            : PlacementVerdict.Continue;

    /// <summary>Why a base pad may not stand at <paramref name="pos"/> on the active world (a locale key), or null.</summary>
    private string? BasePadRefusal(PlayerSession session, Vector3i pos)
    {
        string me = session.State.PlayerId;
        string body = _world.LocationId;
        var hereBody = _galaxy?.FindBody(body);
        if (hereBody is null
            || (hereBody.Kind != CelestialKind.Planet && hereBody.Kind != CelestialKind.Moon && hereBody.Kind != CelestialKind.AsteroidField))
        {
            return "@srv.basepad.surface_only";
        }

        if (NearestOwnCoreInReach(me, body, pos) is not { } home)
        {
            // #3206: the server's own reach rides along — the text names no number of its own.
            return "@srv.basepad.no_base:" + BasePadRules.ReachXZ.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (PadsOfBase(home) >= BasePadRules.MaxPerBase)
        {
            return "@srv.basepad.limit:" + BasePadRules.MaxPerBase.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (SquareConflict(me, pos) is { } conflict)
        {
            return conflict;
        }

        int blocked = BlockedColumns(pos);
        return blocked > 0 ? "@srv.basepad.blocked:" + blocked.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Whether a new pad's square would touch a communal pad, another base pad or a stranger's base zone. Two
    /// pads' squares may not share a column at any height — one above the other in a cavern included — so a pad's
    /// column belongs to one pad only (<see cref="BasePadGroundAt"/> reads the parking height by column).</summary>
    private string? SquareConflict(string me, Vector3i pos)
    {
        const int Span = 2 * BasePadRules.HalfSize; // two squares overlap when their centres are this close or closer
        int circ = _world.Circumference;
        bool Near(int cx, int cz, int cy, int reachXZ, int reachY)
            => System.Math.Abs(WorldConstants.WrapDeltaX(pos.X - cx, circ)) <= reachXZ
               && System.Math.Abs(WorldConstants.WrapDeltaZ(pos.Z - cz, circ)) <= reachXZ
               && System.Math.Abs(pos.Y - cy) <= reachY;

        if (_landingPads.Any(p => Near(p.CenterX, p.CenterZ, p.CenterY, Span, int.MaxValue)))
        {
            return "@srv.basepad.near_pad";
        }

        if (_basePads.Any(p => p.Planet == _world.LocationId && Near(p.Cell.X, p.Cell.Z, p.Cell.Y, Span, int.MaxValue)))
        {
            return "@srv.basepad.overlap";
        }

        int zone = BasePadRules.HalfSize + BaseProtectionRadius;
        return _bases.Any(b => b.Planet == _world.LocationId && b.OwnerId != me && !AreAllied(b.OwnerId, me)
                               && Near(b.Cell.X, b.Cell.Z, b.Cell.Y, zone, BasePadRules.Clearance + BaseProtectionRadius))
            ? "@srv.basepad.foreign_base"
            : null;
    }

    // ---------------- the pad list ----------------

    /// <summary>The base pads of a body as the receiver sees them: every active pad they may land on, and — locked — the pads
    /// of bases they can see (own, allied, discovered). A pad of an undiscovered stranger's base is not sent at all.</summary>
    private IEnumerable<NetLandingPad> BasePadsFor(string body, PlayerSession receiver)
    {
        string me = receiver.State.PlayerId;
        foreach (var pad in _basePads)
        {
            if (pad.Planet != body || BaseOfPad(pad) is not { } home)
            {
                continue;
            }

            bool locked = !MayLandOnBasePad(pad, me);
            if (locked && !SeesBase(receiver, home))
            {
                continue;
            }

            string holder = BasePadHolderName(body, pad.Cell, string.Empty) ?? string.Empty;
            bool mine = receiver.AssignedBasePad is { } held && held == pad.Cell && receiver.CurrentLocationId == body;
            var entry = new NetLandingPad
            {
                Index = -1,
                X = pad.Cell.X,
                Z = pad.Cell.Z,
                Base = true,
                CellX = pad.Cell.X,
                CellY = pad.Cell.Y,
                CellZ = pad.Cell.Z,
                Name = pad.Name,
                BaseX = home.Cell.X,
                BaseY = home.Cell.Y,
                BaseZ = home.Cell.Z,
                BaseName = home.Name,
                OwnerName = NameOf(pad.OwnerId),
                Relation = BaseRelation(home, me),
                Access = (int)pad.Access,
                Locked = locked,
                Mine = mine,
                Occupied = holder.Length > 0 && !mine,
                Occupant = holder,
            };
            if (!mine && !locked && holder.Length == 0 && BasePadBuiltOver(pad))
            {
                MarkBuiltOver(entry); // #3146
            }

            yield return entry;
        }
    }

    /// <summary>Appends the receiver's view of a body's base pads to a communal pad array.</summary>
    private NetLandingPad[] WithBasePads(NetLandingPad[] communal, string body, PlayerSession receiver)
    {
        var extra = BasePadsFor(body, receiver).ToList();
        return extra.Count == 0 ? communal : communal.Concat(extra).ToArray();
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam: a player places a base pad at a cell (the placement rule, the block and the entity), as the place
    /// intent does. Returns the refusal key, or null when placed.</summary>
    public string? PlaceBasePadForTest(PlayerSession session, Vector3i pos)
    {
        Serve(session);
        var def = _content.GetBlock(BasePadRules.BlockKey)!;
        if (BasePadRefusal(session, pos) is { } refusal)
        {
            return refusal;
        }

        _world.SetBlock(pos, def.NumericId, owner: session.State.PlayerId);
        PlaceBasePad(session, pos, string.Empty);
        return null;
    }

    /// <summary>Test seam: the owner (or anyone, to test the refusal) sets a pad's name and access.</summary>
    public void SetBasePadForTest(PlayerSession session, Vector3i cell, string name, BasePadAccess access)
    {
        Serve(session);
        HandleSetBasePad(session, new SetBasePadIntent { CellX = cell.X, CellY = cell.Y, CellZ = cell.Z, Name = name, Access = (int)access });
    }

    /// <summary>Test seam: the base pad a session holds, or null.</summary>
    public Vector3i? HeldBasePadForTest(PlayerSession session) => session.AssignedBasePad;

    /// <summary>Test seam: the pad list a player would receive for a body.</summary>
    public IReadOnlyList<NetLandingPad> BasePadListForTest(PlayerSession receiver, string body) => BasePadsFor(body, receiver).ToList();

    /// <summary>Test seam: whether a cell is protected against an actor by a base pad (active world).</summary>
    public bool BasePadProtectedForTest(Vector3i pos, string actorId, bool isAdmin = false) => IsBasePadProtected(pos, actorId, isAdmin);

    /// <summary>Test seam: whether a cell lies in a base pad's landing volume (active world).</summary>
    public bool BasePadVolumeForTest(Vector3i pos) => IsBasePadVolume(pos);

    /// <summary>Test seam: a pilot up in space lands on a base pad of the body they launched from (the chooser's base row).</summary>
    public void LandOnBasePadForTest(PlayerSession session, Vector3i cell)
    {
        Serve(session);
        HandleLeaveSpace(session, new LeaveSpaceIntent
        {
            DestinationBodyId = string.Empty,
            HasPadCell = true,
            PadCellX = cell.X,
            PadCellY = cell.Y,
            PadCellZ = cell.Z,
        });
    }

    /// <summary>Test seam: a map-travel intent as the client sends it (a parked ship turns it into an automatic transit).</summary>
    public void TravelIntentForTest(PlayerSession session, TravelIntent intent)
    {
        Serve(session);
        HandleTravelIntent(session, intent);
    }

    /// <summary>Test seam (#3073): the Market base pad an arriving trader would take on the active world, with these players
    /// standing there.</summary>
    public Vector3i? MarketBasePadForTest(params PlayerSession[] present)
        => MarketBasePadFor(_world.LocationId, present.ToList());
}
