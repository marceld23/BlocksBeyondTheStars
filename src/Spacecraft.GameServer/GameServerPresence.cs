using Spacecraft.Networking.Messages;
using Spacecraft.Shared.State;

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
        if (intent.Hull != 0)
        {
            session.HullColor = intent.Hull; // item 32 (0 = a client that didn't send one — keep the default)
        }
    }

    /// <summary>Public setter for local play / tests.</summary>
    public void SetAppearance(string playerId, int skin, int torso, int arms, int legs, int hull = 0)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            s.SkinColor = skin;
            s.TorsoColor = torso;
            s.ArmColor = arms;
            s.LegColor = legs;
            if (hull != 0)
            {
                s.HullColor = hull;
            }
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
            Stealthed = p.Stealthed,
            Jetpacking = p.Jetpacking,
            Gear = GearMask(p),
            Held = HeldItemKey(p),
        };
    }

    /// <summary>Equipped-gear bitmask from carried items (mirrors the local avatar gear logic).</summary>
    private static int GearMask(PlayerState p)
    {
        int g = 0;
        var inv = p.Inventory;
        if (inv.CountOf("helmet") > 0) g |= 1;
        if (inv.CountOf("armor_chest") > 0 || inv.CountOf("stealth_suit") > 0) g |= 2;
        if (inv.CountOf("armor_legs") > 0) g |= 4;
        if (inv.CountOf("oxygen_tank_2") > 0 || inv.CountOf("jetpack") > 0) g |= 8;
        if (inv.CountOf("suit_lamp") > 0) g |= 16;
        return g;
    }

    /// <summary>The item in the player's selected hotbar slot (shown in the avatar's hand), or empty.</summary>
    private static string HeldItemKey(PlayerState p)
    {
        int slot = p.SelectedHotbarSlot;
        if (slot >= 0 && slot < p.Inventory.SlotCount && p.Inventory.Slots[slot] is { IsEmpty: false } stack)
        {
            return stack.Item;
        }

        return string.Empty;
    }

    /// <summary>Sends the new player the presence of everyone already online.</summary>
    private void SendExistingPresences(PlayerSession newcomer)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other.ConnectionId != newcomer.ConnectionId
                && other.CurrentLocationId == newcomer.CurrentLocationId)
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

        // Presence is per-world: a player only sees others in the same world (the active cursor world,
        // since TickPresence runs once per occupied world).
        var joined = JoinedInActiveWorld().ToList();
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
