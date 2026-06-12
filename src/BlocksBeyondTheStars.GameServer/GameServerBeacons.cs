using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Placed radio beacons (item 37). Unlike a door, a beacon IS a real voxel block — the chunk mesher draws +
/// collides it and the voxel persists through the normal block-edit store. This entity holds only the metadata
/// the voxel grid can't: the owner and the player-typed, free-form label that shows on the world map + compass.
/// The entity is keyed by its cell, persisted in its own table, and restored on world load alongside the block.
/// Everyone in the world sees a beacon's marker; only the owner (or an admin) may rename it.
/// </summary>
public sealed partial class GameServer
{
    private const int BeaconLabelMaxLength = 24; // a map label, not a paragraph — keep it short + readable

    /// <summary>A placed beacon on the server. The client sees a <see cref="NetBeacon"/>.</summary>
    internal sealed class ServerBeacon
    {
        public int Id;
        public Vector3f Pos;            // beacon block centre
        public string Label = string.Empty;
        public string OwnerId = string.Empty;
    }

    private List<ServerBeacon> _beacons => _worlds.Active.Beacons;
    private int _nextBeaconId { get => _worlds.Active.NextBeaconId; set => _worlds.Active.NextBeaconId = value; }

    /// <summary>Beacon states (id/pos/label/owner) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, Vector3f Pos, string Label, string OwnerId)> BeaconSnapshots
        => _beacons.Select(b => (b.Id, b.Pos, b.Label, b.OwnerId)).ToList();

    /// <summary>Number of beacons registered in the active world.</summary>
    public int BeaconCount => _beacons.Count;

    /// <summary>A player placed a beacon block: register the entity that carries its label + owner and persist it
    /// by its cell (the block itself is already set + persisted by the normal place path). Broadcasts the change.</summary>
    private void PlaceBeacon(PlayerSession session, Vector3i pos, string label)
    {
        string clean = SanitizeBeaconLabel(label);
        _beacons.Add(new ServerBeacon
        {
            Id = _nextBeaconId++,
            Pos = new Vector3f(pos.X + 0.5f, pos.Y, pos.Z + 0.5f),
            Label = clean,
            OwnerId = session.State.PlayerId,
        });
        _repo.SaveBeacon(new StoredBeacon
        {
            Planet = _world.LocationId,
            X = pos.X, Y = pos.Y, Z = pos.Z,
            Label = clean,
            OwnerId = session.State.PlayerId,
        });
        BroadcastBeacons();
    }

    /// <summary>If a beacon entity sits at this cell (e.g. its block was just mined or blasted), drop + forget it.
    /// Safe to call for any cleared cell — it no-ops when there's no beacon there.</summary>
    private void RemoveBeaconAt(Vector3i pos)
    {
        var beacon = _beacons.FirstOrDefault(b =>
            (int)System.Math.Floor(b.Pos.X) == pos.X &&
            (int)System.Math.Floor(b.Pos.Y) == pos.Y &&
            (int)System.Math.Floor(b.Pos.Z) == pos.Z);
        if (beacon is null)
        {
            return;
        }

        _beacons.Remove(beacon);
        _repo.DeleteBeacon(_world.LocationId, pos.X, pos.Y, pos.Z);
        BroadcastBeacons();
    }

    /// <summary>The owner (or an admin) renames a beacon they're standing at — press E, type a new label.</summary>
    private void HandleSetBeaconLabel(PlayerSession session, SetBeaconLabelIntent intent)
    {
        var beacon = _beacons.FirstOrDefault(b => b.Id == intent.BeaconId);
        if (beacon is null)
        {
            return; // already mined, or in another world
        }

        if (!session.State.IsAdmin && beacon.OwnerId != session.State.PlayerId)
        {
            Reject(session, "beacon", "Only the owner can rename this beacon.");
            return;
        }

        var pos = new Vector3i(
            (int)System.Math.Floor(beacon.Pos.X),
            (int)System.Math.Floor(beacon.Pos.Y),
            (int)System.Math.Floor(beacon.Pos.Z));
        if (!WithinReach(session.State, pos))
        {
            Reject(session, "beacon", "Out of reach.");
            return;
        }

        beacon.Label = SanitizeBeaconLabel(intent.Label);
        _repo.SaveBeacon(new StoredBeacon
        {
            Planet = _world.LocationId,
            X = pos.X, Y = pos.Y, Z = pos.Z,
            Label = beacon.Label,
            OwnerId = beacon.OwnerId,
        });
        BroadcastBeacons();
    }

    /// <summary>Test/util entrypoint: rename a beacon as a given player (mirrors the rename intent).</summary>
    public void SetBeaconLabelForTest(PlayerSession session, int beaconId, string label)
        => HandleSetBeaconLabel(session, new SetBeaconLabelIntent { BeaconId = beaconId, Label = label });

    /// <summary>Rebuilds the active world's beacon entities from persistence (the blocks themselves come back via
    /// the normal block-edit store). Idempotent — clears first, so it's safe to call on every world activation.</summary>
    private void LoadBeacons()
    {
        _beacons.Clear();
        if (_nextBeaconId < 1)
        {
            _nextBeaconId = 1; // id 0 means "none" to the client — never hand it out
        }

        foreach (var sb in _repo.ListBeacons(_world.LocationId))
        {
            _beacons.Add(new ServerBeacon
            {
                Id = _nextBeaconId++,
                Pos = new Vector3f(sb.X + 0.5f, sb.Y, sb.Z + 0.5f),
                Label = sb.Label,
                OwnerId = sb.OwnerId,
            });
        }
    }

    private void BroadcastBeacons() => BroadcastToWorld(new BeaconList { Beacons = _beacons.Select(ToNetBeacon).ToArray() });

    private void SendBeacons(PlayerSession session)
        => Send(session, new BeaconList { Beacons = _beacons.Select(ToNetBeacon).ToArray() });

    private static NetBeacon ToNetBeacon(ServerBeacon b) => new()
    {
        Id = b.Id,
        X = b.Pos.X,
        Y = b.Pos.Y,
        Z = b.Pos.Z,
        Label = b.Label,
        OwnerId = b.OwnerId,
    };

    /// <summary>Trims a player-typed label to a single short line (drops newlines, clamps length).</summary>
    private static string SanitizeBeaconLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty; // the client shows a localized default for an empty label
        }

        var trimmed = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length > BeaconLabelMaxLength ? trimmed.Substring(0, BeaconLabelMaxLength) : trimmed;
    }
}
