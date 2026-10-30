// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-founded planet bases (the "Grundstein"): the surface analogue of a space station. A player places a
/// <b>base_core</b> block on a planet/moon/asteroid to found a named base. Like a beacon, the base_core IS a real
/// voxel block (drawn + collided + persisted via the normal block-edit store); this entity carries the metadata the
/// voxel grid can't — the owner and the player-typed name. The base marks its host body on the travel screen and the
/// planet map. A player may found up to <c>GameRules.BasesPerWorld</c> bases per body (#2996, <c>GameServerBasesMulti.cs</c>);
/// mining the base_core removes the base. Bases are held
/// server-wide (not per-world) so the travel screen can flag a player's bases on bodies that aren't currently loaded.
/// </summary>
public sealed partial class GameServer
{
    private const int BaseNameMaxLength = 24;
    private const string BaseCoreBlock = "base_core";

    /// <summary>Every founded base across all bodies. The base_core blocks come back via the block-edit store; these
    /// entities (owner, name, cell) are loaded once at server start and kept in sync on place/remove/rename.</summary>
    private readonly List<ServerBase> _bases = new();
    private int _nextBaseId = 1;

    /// <summary>A founded base on the server. The client sees a <see cref="NetBase"/>.</summary>
    internal sealed class ServerBase
    {
        public int Id;
        public string OwnerId = string.Empty;
        public string Name = string.Empty;
        public string Planet = string.Empty; // host body id (e.g. "sys0-p1")
        public Vector3i Cell;                 // the base_core block cell
    }

    /// <summary>Base states (id/owner/name/body/cell) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, string OwnerId, string Name, string Body)> BaseSnapshots
        => _bases.Select(b => (b.Id, b.OwnerId, b.Name, b.Planet)).ToList();

    /// <summary>Half-extent (in blocks) of a base's protected build zone: a cube centred on the base_core. Owner +
    /// allies build/mine freely inside it; everyone else is blocked (place + mine + area tools). The same cube is
    /// the base's life-support field (see <see cref="InAnyBaseZone"/>); shared with the client's HUD hint.</summary>
    private const int BaseProtectionRadius = WorldConstants.BaseZoneRadius;

    /// <summary>Half-extent (in blocks) around a base_core within which wild creature spawning is excluded.
    /// Shared with the world constant so the server's spawn checks use the authoritative radius.</summary>
    private const int BaseSpawnExclusionRadius = WorldConstants.BaseSpawnExclusionRadius;

    /// <summary>True if the cell falls inside some player base's protected zone on the current world AND the actor
    /// may not edit it there. Owner + allies + admins build freely; for anyone else the whole zone is read-only.
    /// The base_core cell itself is owner-only even for allies, so an ally can't dissolve the base out from under
    /// the owner (removing the core deletes the base). Bases on other bodies don't protect this world.</summary>
    private bool IsBaseProtected(Vector3i pos, string actorId, bool actorIsAdmin)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet != body)
            {
                continue;
            }

            bool isCore = WithinBaseZone(b.Cell, pos, 0); // the same cell under either spelling of the seams
            if (!isCore && !WithinBaseZone(b.Cell, pos))
            {
                continue;
            }

            if (actorIsAdmin || b.OwnerId == actorId)
            {
                return false; // owner + admin: full control over their base, including the core
            }

            // Allies share the build zone but may not pull the core (that would delete the owner's base).
            if (!isCore && AreAllied(b.OwnerId, actorId))
            {
                return false;
            }

            return true; // inside someone else's base — and not an ally (or it's their core)
        }

        return false;
    }

    /// <summary>
    /// Chebyshev (cube) test: is <paramref name="pos"/> within the specified
    /// half-extent of the base-core? Measured the short way round both seams of the current world (#2752): a
    /// core a few blocks east of x = 0 protects the cells just west of the seam (x = circumference − n) too,
    /// whichever spelling either cell arrives in. A radius of 0 asks "the same cell".
    /// </summary>
    private bool WithinBaseZone(Vector3i core, Vector3i pos, int radius)
    {
        int circ = _world.Circumference;
        return System.Math.Abs(WorldConstants.WrapDeltaX(pos.X - core.X, circ)) <= radius
            && System.Math.Abs(pos.Y - core.Y) <= radius
            && System.Math.Abs(WorldConstants.WrapDeltaZ(pos.Z - core.Z, circ)) <= radius;
    }

    /// <summary>
    /// Chebyshev (cube) test using the base's configured protection radius.
    /// </summary>
    private bool WithinBaseZone(Vector3i core, Vector3i pos)
        => WithinBaseZone(core, pos, BaseProtectionRadius);

    /// <summary>
    /// True if the cell is within BaseSpawnExclusionRadius blocks of any base core on the current world.
    /// Uses the same Chebyshev (cube) distance as the base protection zone.
    /// </summary>
    private bool IsNearBaseCore(Vector3i cell)
    {
        string body = _world.LocationId;

        foreach (var b in _bases)
        {
            if (b.Planet == body && WithinBaseZone(b.Cell, cell, BaseSpawnExclusionRadius))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the cell lies inside a base zone the PLAYER OWNS on the current world — the "build
    /// at home" test the build missions use (#1116). Allied zones deliberately don't count: the assignment
    /// is to extend YOUR base.</summary>
    private bool InOwnBaseZone(string playerId, Vector3i pos)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet == body && b.OwnerId == playerId && WithinBaseZone(b.Cell, pos))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the cell lies inside ANY founded base's zone on the current world (serve the session
    /// first). Ownership is deliberately ignored: the base's life-support field breathes for visitors too —
    /// the protection rules still keep them from editing anything. Same cube as the build protection, so the
    /// one rule to teach is "where you can build is where you can breathe".</summary>
    private bool InAnyBaseZone(Vector3i cell)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet == body && WithinBaseZone(b.Cell, cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Loads every founded base from persistence at server start (the base_core blocks themselves return via
    /// the block-edit store). Idempotent — clears first. The ids are load-order numbers (they shift when an older core is
    /// gone): nothing that must survive a restart may be keyed by them — the residents' identities are pinned in their
    /// saved plan (#2716), the plan itself is keyed by the core cell.</summary>
    private void LoadAllBases()
    {
        _bases.Clear();
        if (_nextBaseId < 1)
        {
            _nextBaseId = 1;
        }

        foreach (var sb in _repo.ListAllBases())
        {
            _bases.Add(new ServerBase
            {
                Id = _nextBaseId++,
                OwnerId = sb.OwnerId,
                Name = sb.Name,
                Planet = sb.Planet,
                Cell = new Vector3i(sb.X, sb.Y, sb.Z),
            });
        }

        if (_bases.Count > 0)
        {
            _log.Info($"Loaded {_bases.Count} player base(s).");
        }

        PinBaseResidentIdentities(); // #2716: the ids above are load-order numbers — the residents keep who they are
    }

    /// <summary>A player placed a base_core block on a body: found a named base. The placement pre-check
    /// (HandlePlace) already ensured this body is a valid surface and the player has no base here yet. The block
    /// itself is set + persisted by the normal place path; this records the owner + name and lights up the marker.</summary>
    private void PlaceBase(PlayerSession session, Vector3i pos)
    {
        string owner = session.State.PlayerId;
        string body = _world.LocationId;
        var basePoint = new ServerBase
        {
            Id = _nextBaseId++,
            OwnerId = owner,
            Name = (string.IsNullOrWhiteSpace(session.State.Name) ? "Player" : session.State.Name) + "'s Base",
            Planet = body,
            Cell = pos,
        };
        _bases.Add(basePoint);
        MarkPartnerIndexesDirty(body, owner); // #2996: a thing between this core and a neighbour may count here now
        AccrueBaseYield(basePoint, out _); // #2999: the core's yield clock starts with the founding
        _repo.SaveBase(ToStored(basePoint));
        BroadcastBasesOn(body);
        SendStarMap(session); // the player's travel-screen badge for this body lights up
        Send(session, new ServerMessage { Text = "@srv.base.founded:" + basePoint.Name });
        RecordStoryMilestone("base:first"); // the first base of the save advances the arc (#1105)
        RaiseGameEvent(session, GameEventKeys.BaseFounded, body); // #2465
        if (!AtmosphereBreathable)
        {
            // Sealed rooms + the energy door have breathed since #794 and nobody found them (#1267): VEGA
            // explains the three rules once, on the first core founded where they matter.
            ShipAiHintOnce(session, "base_airless");
        }

        ShipAiHintOnce(session, "base_walls"); // #1315: the fence rule, told where the player can act on it
        ShipAiHintOnce(session, "base_yield"); // #2999: the core gathers resources over time — collect them there
    }

    /// <summary>If a base entity sits at this cell (its base_core was just mined or blasted), drop + forget it.
    /// Safe to call for any cleared cell — no-ops when there's no base there.</summary>
    private void RemoveBaseAt(Vector3i pos)
    {
        string body = _world.LocationId;
        var basePoint = _bases.FirstOrDefault(b => b.Planet == body && WithinBaseZone(b.Cell, pos, 0));
        if (basePoint is null)
        {
            return;
        }

        HandOverBaseYield(basePoint); // #2999: the owner takes what lay ready at the core
        _bases.Remove(basePoint);
        MarkPartnerIndexesDirty(body, basePoint.OwnerId); // #2996: what this base counted may go to a neighbour now
        ForgetBaseAir(basePoint.Id); // the life-support field (cube + sealed rooms) dies with the core
        ForgetBaseWalls(basePoint.Id); // ...and so does the fence rule (#1315)
        ForgetBaseIndex(basePoint.Id); // ...and what its residents used (#1865) — they leave on the next scan
        _repo.DeleteBase(body, pos.X, pos.Y, pos.Z);
        BroadcastBasesOn(body);
        if (FindSessionByPlayerId(basePoint.OwnerId) is { } owner)
        {
            SendStarMap(owner); // the badge clears for the owner
        }
    }

    /// <summary>The owner renames their base on a body — pressing E at the stone (current body), or via the Map
    /// detail "Rename base" button (an explicit body id, possibly not the active world). Owner-scoped by lookup; since
    /// #2996 by the core cell (an older client without one renames the first base there).</summary>
    private void HandleSetBaseName(PlayerSession session, SetBaseNameIntent intent)
    {
        string body = string.IsNullOrEmpty(intent.BodyId) ? _world.LocationId : intent.BodyId;
        var basePoint = FindOwnBase(session.State.PlayerId, body, intent.HasCell, new Vector3i(intent.CellX, intent.CellY, intent.CellZ));
        if (basePoint is null)
        {
            Reject(session, "base", "@srv.base.none_here");
            return;
        }

        if (ScreenPlayerName(session, SanitizeBaseName(intent.Name), "base") is not { } name)
        {
            return; // refused by the content screen (#1221) — the player has been told
        }

        basePoint.Name = name;
        RenameBaseSettler(basePoint, name); // the settler + roster entry follow the name (#1262)
        _repo.SaveBase(ToStored(basePoint));
        BroadcastBasesOn(body);
        SendStarMap(session); // refresh the travel-screen badge/name + rename prefill
    }

    /// <summary>Test entrypoint: the owner renames their base on the active world (mirrors the E-at-the-stone path).</summary>
    public void RenameBaseForTest(PlayerSession session, string name)
        => HandleSetBaseName(session, new SetBaseNameIntent { Name = name });

    /// <summary>Test/util entrypoint: found a base for a player at a cell on their active world (mirrors placement).</summary>
    public void PlaceBaseForTest(PlayerSession session, Vector3i pos)
    {
        Serve(session);
        PlaceBase(session, pos);
    }

    /// <summary>Whether a cell is base-protected against the given actor on the active world (test seam, #2752).</summary>
    public bool BaseProtectedForTest(Vector3i pos, string actorId, bool isAdmin = false) => IsBaseProtected(pos, actorId, isAdmin);

    /// <summary>Test/util entrypoint: rename a base as a given player (mirrors the rename intent).</summary>
    public void SetBaseNameForTest(PlayerSession session, string bodyId, string name)
        => HandleSetBaseName(session, new SetBaseNameIntent { BodyId = bodyId, Name = name });

    /// <summary>The given player's bases, each with its body, current name and core cell (for the travel screen; #2996: one
    /// row per base, several may share a body).</summary>
    private NetMapBase[] MyBaseList(string ownerId)
        => _bases.Where(b => b.OwnerId == ownerId)
            .Select(b => new NetMapBase { BodyId = b.Planet, Name = b.Name, CellX = b.Cell.X, CellY = b.Cell.Y, CellZ = b.Cell.Z })
            .ToArray();

    private void BroadcastBasesOn(string planet)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined && s.CurrentLocationId == planet))
        {
            SendBases(session);
        }
    }

    /// <summary>The bases on the player's body that this player may see (#2997: own and allied always, anyone else's once
    /// discovered), each with its relation to them and — for their own — the yield ready at the core (#2999).</summary>
    private void SendBases(PlayerSession session)
    {
        string me = session.State.PlayerId;
        Send(session, new BaseList
        {
            Bases = _bases.Where(b => b.Planet == session.CurrentLocationId && SeesBase(session, b))
                .Select(b => ToNetBaseFor(b, me)).ToArray(),
        });
    }

    private NetBase ToNetBaseFor(ServerBase b, string viewerId)
    {
        var net = ToNetBase(b);
        net.Relation = BaseRelation(b, viewerId);
        net.YieldReady = b.OwnerId == viewerId ? BaseYieldReady(b) : 0;
        return net;
    }

    private NetBase ToNetBase(ServerBase b)
    {
        // The core's air readout (#1267): only meaningful on the world the base stands on — and the fill
        // must be current even when nobody's oxygen tick asked for it (a player standing IN the cube
        // short-circuits the sealed-room check, so the cache could otherwise sit empty forever).
        var air = b.Planet == _world.LocationId ? RefreshBaseAir(b, broadcastOnChange: false) : null;
        return new NetBase
        {
            Id = b.Id,
            X = b.Cell.X + 0.5f,
            Y = b.Cell.Y,
            Z = b.Cell.Z + 0.5f,
            Name = b.Name,
            OwnerId = b.OwnerId,
            BodyId = b.Planet,
            AirCells = air?.Cells.Count ?? 0,
            SealedRooms = air?.Rooms ?? 0,
            Residents = BaseResidentCount(b), // #2679: the saved plan, so it is known on any world
            ResidentCap = BaseResidentRoom(b), // #2996: the residents a player's bases on one body hold together
        };
    }

    private static StoredBase ToStored(ServerBase b) => new()
    {
        Planet = b.Planet,
        X = b.Cell.X,
        Y = b.Cell.Y,
        Z = b.Cell.Z,
        Name = b.Name,
        OwnerId = b.OwnerId,
    };

    /// <summary>Trims a player-typed base name to a single short line (drops newlines, clamps length).</summary>
    private static string SanitizeBaseName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty; // the client shows a localized default for an empty name
        }

        var trimmed = StripControlChars(raw);
        return trimmed.Length > BaseNameMaxLength ? trimmed.Substring(0, BaseNameMaxLength) : trimmed;
    }
}
