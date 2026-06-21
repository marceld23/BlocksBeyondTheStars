using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Placed beam blocks (teleporter pads). Like a radio beacon, a beam block IS a real voxel block — the chunk
/// mesher draws + collides it and the voxel persists through the normal block-edit store. This entity holds the
/// metadata the voxel grid can't: the owner and the player-typed name. Stepping onto a pad and pressing E opens
/// the transporter, which beams the player to any of their own or an allied player's beam blocks on the SAME
/// world. The jump costs suit energy and has a short cooldown. Everyone sees a pad's name as a world landmark;
/// only the owner (or an admin) may rename it, and only the owner + allies may use one as a source/destination.
/// </summary>
public sealed partial class GameServer
{
    private const int BeamNameMaxLength = 24;   // a map label, not a paragraph — keep it short + readable
    private const float BeamEnergyCost = 6f;    // suit energy per jump (cheaper than the suit teleporter's 10)
    private const double BeamCooldownSeconds = 6.0;

    /// <summary>Per-player beam cooldown (seconds remaining), separate from the suit-teleporter cooldown.</summary>
    private readonly Dictionary<string, double> _beamCooldown = new();

    /// <summary>A placed beam block on the server. The client sees a <see cref="NetBeam"/>.</summary>
    internal sealed class ServerBeam
    {
        public int Id;
        public Vector3i Cell;           // the beam_block cell
        public string Name = string.Empty;
        public string OwnerId = string.Empty;
    }

    private List<ServerBeam> _beams => _worlds.Active.Beams;
    private int _nextBeamId { get => _worlds.Active.NextBeamId; set => _worlds.Active.NextBeamId = value; }

    /// <summary>Beam states (id/cell/name/owner) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, Vector3i Cell, string Name, string OwnerId)> BeamSnapshots
        => _beams.Select(b => (b.Id, b.Cell, b.Name, b.OwnerId)).ToList();

    /// <summary>Number of beam blocks registered in the active world.</summary>
    public int BeamCount => _beams.Count;

    /// <summary>A player placed a beam block: register the entity that carries its name + owner and persist it by
    /// its cell (the block itself is already set + persisted by the normal place path). Broadcasts the change.</summary>
    private void PlaceBeam(PlayerSession session, Vector3i pos, string name)
    {
        string clean = SanitizeBeamName(name);
        _beams.Add(new ServerBeam
        {
            Id = _nextBeamId++,
            Cell = pos,
            Name = clean,
            OwnerId = session.State.PlayerId,
        });
        _repo.SaveBeam(new StoredBeam
        {
            Planet = _world.LocationId,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Name = clean,
            OwnerId = session.State.PlayerId,
        });
        BroadcastBeams();
    }

    /// <summary>If a beam entity sits at this cell (e.g. its block was just mined or blasted), drop + forget it.
    /// Safe to call for any cleared cell — it no-ops when there's no beam there.</summary>
    private void RemoveBeamAt(Vector3i pos)
    {
        var beam = _beams.FirstOrDefault(b => b.Cell.X == pos.X && b.Cell.Y == pos.Y && b.Cell.Z == pos.Z);
        if (beam is null)
        {
            return;
        }

        _beams.Remove(beam);
        _repo.DeleteBeam(_world.LocationId, pos.X, pos.Y, pos.Z);
        BroadcastBeams();
    }

    /// <summary>The owner (or an admin) renames a beam block they're standing at — press E, type a new name.</summary>
    private void HandleSetBeamName(PlayerSession session, SetBeamNameIntent intent)
    {
        var beam = _beams.FirstOrDefault(b => b.Id == intent.BeamId);
        if (beam is null)
        {
            return; // already mined, or in another world
        }

        if (!session.State.IsAdmin && beam.OwnerId != session.State.PlayerId)
        {
            Reject(session, "beam", "Only the owner can rename this beam block.");
            return;
        }

        if (!WithinReach(session.State, beam.Cell))
        {
            Reject(session, "beam", "Out of reach.");
            return;
        }

        beam.Name = SanitizeBeamName(intent.Name);
        _repo.SaveBeam(new StoredBeam
        {
            Planet = _world.LocationId,
            X = beam.Cell.X,
            Y = beam.Cell.Y,
            Z = beam.Cell.Z,
            Name = beam.Name,
            OwnerId = beam.OwnerId,
        });
        BroadcastBeams();
    }

    /// <summary>The player beams from the pad they're standing at to a chosen destination pad on this world. Both
    /// pads must be the player's own or an ally's; the player must be within reach of the source, off cooldown and
    /// have enough suit energy. On success the player is moved on top of the destination pad and everyone on the
    /// world sees the beam VFX at both ends.</summary>
    private void HandleBeamTeleport(PlayerSession session, BeamTeleportIntent intent)
    {
        var source = _beams.FirstOrDefault(b => b.Id == intent.SourceId);
        var target = _beams.FirstOrDefault(b => b.Id == intent.TargetId);
        if (source is null || target is null || source.Id == target.Id)
        {
            Reject(session, "beam", "That beam block is gone.");
            return;
        }

        string me = session.State.PlayerId;
        if (!CanUseBeam(source, me) || !CanUseBeam(target, me))
        {
            Reject(session, "beam", "You can only beam between your own and allied beam blocks.");
            return;
        }

        if (!WithinReach(session.State, source.Cell))
        {
            Reject(session, "beam", "Step onto the beam block first.");
            return;
        }

        if (_beamCooldown.GetValueOrDefault(me) > 0)
        {
            Reject(session, "beam", "The beam emitter is still recharging.");
            return;
        }

        if (session.State.SuitEnergy < BeamEnergyCost)
        {
            Reject(session, "beam", "Not enough suit energy to beam.");
            return;
        }

        var from = new Vector3f(source.Cell.X + 0.5f, source.Cell.Y + 1f, source.Cell.Z + 0.5f);
        var to = new Vector3f(target.Cell.X + 0.5f, target.Cell.Y + 1f, target.Cell.Z + 0.5f);

        session.State.SuitEnergy -= BeamEnergyCost;
        session.State.Position = to;
        _beamCooldown[me] = BeamCooldownSeconds;

        SendPlayerState(session); // sync the spent energy + authoritative position to the HUD
        Send(session, new BeamTeleported { X = to.X, Y = to.Y, Z = to.Z }); // snap the body + arrival effect
        BroadcastToWorld(new BeamFx
        {
            FromX = from.X,
            FromY = from.Y,
            FromZ = from.Z,
            ToX = to.X,
            ToY = to.Y,
            ToZ = to.Z,
        });

        string label = string.IsNullOrEmpty(target.Name) ? "beam block" : target.Name;
        Send(session, new ServerMessage { Text = $"Beamed to {label}." });
    }

    /// <summary>True if the player may use this beam block as a source/destination: they own it, are allied with
    /// the owner, or are an admin.</summary>
    private bool CanUseBeam(ServerBeam beam, string playerId)
        => beam.OwnerId == playerId
           || AreAllied(beam.OwnerId, playerId)
           || (FindSessionByPlayerId(playerId)?.State.IsAdmin ?? false);

    /// <summary>Counts down a player's beam cooldown (called from the environment tick).</summary>
    private void DecayBeamCooldown(string playerId, double dt)
    {
        if (_beamCooldown.TryGetValue(playerId, out var cd) && cd > 0)
        {
            _beamCooldown[playerId] = System.Math.Max(0, cd - dt);
        }
    }

    /// <summary>Test/util entrypoint: place a beam block for a player at a cell (mirrors placement).</summary>
    public void PlaceBeamForTest(PlayerSession session, Vector3i pos, string name = "")
    {
        Serve(session);
        PlaceBeam(session, pos, name);
    }

    /// <summary>Test/util entrypoint: rename a beam block as a given player (mirrors the rename intent).</summary>
    public void SetBeamNameForTest(PlayerSession session, int beamId, string name)
        => HandleSetBeamName(session, new SetBeamNameIntent { BeamId = beamId, Name = name });

    /// <summary>Test/util entrypoint: beam a player from one pad to another (mirrors the teleport intent).</summary>
    public void BeamTeleportForTest(PlayerSession session, int sourceId, int targetId)
        => HandleBeamTeleport(session, new BeamTeleportIntent { SourceId = sourceId, TargetId = targetId });

    /// <summary>Rebuilds the active world's beam entities from persistence (the blocks themselves come back via
    /// the normal block-edit store). Idempotent — clears first, so it's safe to call on every world activation.</summary>
    private void LoadBeams()
    {
        _beams.Clear();
        if (_nextBeamId < 1)
        {
            _nextBeamId = 1; // id 0 means "none" to the client — never hand it out
        }

        foreach (var sb in _repo.ListBeams(_world.LocationId))
        {
            _beams.Add(new ServerBeam
            {
                Id = _nextBeamId++,
                Cell = new Vector3i(sb.X, sb.Y, sb.Z),
                Name = sb.Name,
                OwnerId = sb.OwnerId,
            });
        }
    }

    private void BroadcastBeams() => BroadcastToWorld(new BeamList { Beams = _beams.Select(ToNetBeam).ToArray() });

    private void SendBeams(PlayerSession session)
        => Send(session, new BeamList { Beams = _beams.Select(ToNetBeam).ToArray() });

    private static NetBeam ToNetBeam(ServerBeam b) => new()
    {
        Id = b.Id,
        X = b.Cell.X + 0.5f,
        Y = b.Cell.Y,
        Z = b.Cell.Z + 0.5f,
        Name = b.Name,
        OwnerId = b.OwnerId,
    };

    /// <summary>Trims a player-typed name to a single short line (drops newlines, clamps length).</summary>
    private static string SanitizeBeamName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty; // the client shows a localized default for an empty name
        }

        var trimmed = raw.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length > BeamNameMaxLength ? trimmed.Substring(0, BeamNameMaxLength) : trimmed;
    }
}
