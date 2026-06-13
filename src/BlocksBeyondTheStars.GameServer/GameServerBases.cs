using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-founded planet bases (the "Grundstein"): the surface analogue of a space station. A player places a
/// <b>base_core</b> block on a planet/moon/asteroid to found a named base. Like a beacon, the base_core IS a real
/// voxel block (drawn + collided + persisted via the normal block-edit store); this entity carries the metadata the
/// voxel grid can't — the owner and the player-typed name. The base marks its host body on the travel screen and the
/// planet map. There is one base per body per player; mining the base_core removes the base. Bases are held
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

    /// <summary>True if the player already owns a base on the given body (one base per body per player).</summary>
    private bool PlayerHasBaseOn(string ownerId, string body)
        => _bases.Any(b => b.OwnerId == ownerId && b.Planet == body);

    /// <summary>Half-extent (in blocks) of a base's protected build zone: a cube centred on the base_core. Owner +
    /// allies build/mine freely inside it; everyone else is blocked (place + mine + area tools). Tunable.</summary>
    private const int BaseProtectionRadius = 8;

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

            bool isCore = b.Cell.X == pos.X && b.Cell.Y == pos.Y && b.Cell.Z == pos.Z;
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

    /// <summary>Chebyshev (cube) test: is <paramref name="pos"/> within the base-core's protected half-extent?</summary>
    private static bool WithinBaseZone(Vector3i core, Vector3i pos)
        => System.Math.Abs(pos.X - core.X) <= BaseProtectionRadius
           && System.Math.Abs(pos.Y - core.Y) <= BaseProtectionRadius
           && System.Math.Abs(pos.Z - core.Z) <= BaseProtectionRadius;

    /// <summary>Loads every founded base from persistence at server start (the base_core blocks themselves return via
    /// the block-edit store). Idempotent — clears first.</summary>
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
        _repo.SaveBase(ToStored(basePoint));
        BroadcastBasesOn(body);
        SendStarMap(session); // the player's travel-screen badge for this body lights up
        Send(session, new ServerMessage { Text = $"Base founded: {basePoint.Name}. Press E on the stone to rename it." });
    }

    /// <summary>If a base entity sits at this cell (its base_core was just mined or blasted), drop + forget it.
    /// Safe to call for any cleared cell — no-ops when there's no base there.</summary>
    private void RemoveBaseAt(Vector3i pos)
    {
        string body = _world.LocationId;
        var basePoint = _bases.FirstOrDefault(b => b.Planet == body && b.Cell.X == pos.X && b.Cell.Y == pos.Y && b.Cell.Z == pos.Z);
        if (basePoint is null)
        {
            return;
        }

        _bases.Remove(basePoint);
        _repo.DeleteBase(body, pos.X, pos.Y, pos.Z);
        BroadcastBasesOn(body);
        if (FindSessionByPlayerId(basePoint.OwnerId) is { } owner)
        {
            SendStarMap(owner); // the badge clears for the owner
        }
    }

    /// <summary>The owner renames their base on a body — pressing E at the stone (current body), or via the Map
    /// detail "Rename base" button (an explicit body id, possibly not the active world). Owner-scoped by lookup.</summary>
    private void HandleSetBaseName(PlayerSession session, SetBaseNameIntent intent)
    {
        string body = string.IsNullOrEmpty(intent.BodyId) ? _world.LocationId : intent.BodyId;
        var basePoint = _bases.FirstOrDefault(b => b.OwnerId == session.State.PlayerId && b.Planet == body);
        if (basePoint is null)
        {
            Reject(session, "base", "You have no base on this body.");
            return;
        }

        basePoint.Name = SanitizeBaseName(intent.Name);
        _repo.SaveBase(ToStored(basePoint));
        BroadcastBasesOn(body);
        SendStarMap(session); // refresh the travel-screen badge/name + rename prefill
    }

    /// <summary>Test/util entrypoint: found a base for a player at a cell on their active world (mirrors placement).</summary>
    public void PlaceBaseForTest(PlayerSession session, Vector3i pos)
    {
        Serve(session);
        PlaceBase(session, pos);
    }

    /// <summary>Test/util entrypoint: rename a base as a given player (mirrors the rename intent).</summary>
    public void SetBaseNameForTest(PlayerSession session, string bodyId, string name)
        => HandleSetBaseName(session, new SetBaseNameIntent { BodyId = bodyId, Name = name });

    /// <summary>Bodies where the given player has a base, with each base's current name (for the travel screen).</summary>
    private NetMapBase[] MyBaseList(string ownerId)
        => _bases.Where(b => b.OwnerId == ownerId)
            .Select(b => new NetMapBase { BodyId = b.Planet, Name = b.Name })
            .ToArray();

    private void BroadcastBasesOn(string planet)
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined && s.CurrentLocationId == planet))
        {
            SendBases(session);
        }
    }

    private void SendBases(PlayerSession session)
        => Send(session, new BaseList { Bases = _bases.Where(b => b.Planet == session.CurrentLocationId).Select(ToNetBase).ToArray() });

    private static NetBase ToNetBase(ServerBase b) => new()
    {
        Id = b.Id,
        X = b.Cell.X + 0.5f,
        Y = b.Cell.Y,
        Z = b.Cell.Z + 0.5f,
        Name = b.Name,
        OwnerId = b.OwnerId,
        BodyId = b.Planet,
    };

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

        var trimmed = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length > BaseNameMaxLength ? trimmed.Substring(0, BaseNameMaxLength) : trimmed;
    }
}
