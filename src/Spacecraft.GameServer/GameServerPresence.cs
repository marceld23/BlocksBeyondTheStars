using Spacecraft.Networking.Messages;

namespace Spacecraft.GameServer;

/// <summary>
/// Multiplayer presence (M24): the server periodically broadcasts each player's position +
/// heading + avatar colours to the other players so clients can render them with nameplates,
/// and announces when a player leaves. Appearance is cosmetic; identity stays authoritative.
/// </summary>
public sealed partial class GameServer
{
    private const double PresenceInterval = 0.1; // ~10 Hz

    private double _sincePresence;

    private void HandleSetAppearance(PlayerSession session, SetAppearanceIntent intent)
    {
        session.SkinColor = intent.Skin;
        session.TorsoColor = intent.Torso;
        session.ArmColor = intent.Arms;
        session.LegColor = intent.Legs;
    }

    /// <summary>Public setter for local play / tests.</summary>
    public void SetAppearance(string playerId, int skin, int torso, int arms, int legs)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.SkinColor = skin;
            s.TorsoColor = torso;
            s.ArmColor = arms;
            s.LegColor = legs;
        }
    }

    private static PlayerPresence PresenceOf(PlayerSession s)
    {
        var p = s.State;
        return new PlayerPresence
        {
            PlayerId = p.PlayerId,
            Name = p.Name,
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Yaw = p.Yaw,
            Skin = s.SkinColor,
            Torso = s.TorsoColor,
            Arms = s.ArmColor,
            Legs = s.LegColor,
        };
    }

    /// <summary>Sends the new player the presence of everyone already online.</summary>
    private void SendExistingPresences(PlayerSession newcomer)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other.ConnectionId != newcomer.ConnectionId)
            {
                Send(newcomer, PresenceOf(other));
            }
        }
    }

    /// <summary>Broadcasts each player's presence to the others (rate-limited).</summary>
    private void TickPresence(double dt)
    {
        _sincePresence += dt;
        if (_sincePresence < PresenceInterval)
        {
            return;
        }

        _sincePresence = 0;

        var joined = _sessions.Values.Where(s => s.Joined).ToList();
        if (joined.Count < 2)
        {
            return; // nobody else to inform
        }

        foreach (var subject in joined)
        {
            var presence = PresenceOf(subject);
            foreach (var viewer in joined)
            {
                if (viewer.ConnectionId != subject.ConnectionId)
                {
                    Send(viewer, presence);
                }
            }
        }
    }
}
