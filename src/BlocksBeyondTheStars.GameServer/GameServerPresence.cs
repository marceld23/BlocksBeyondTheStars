using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

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
        if (intent.Hull != 0 && intent.Hull != session.HullColor)
        {
            session.HullColor = intent.Hull; // item 32 (0 = a client that didn't send one — keep the default)

            // Ship-as-object: a repaint while the ship stands on a pad re-announces the parked object, so
            // everyone on the world (incl. the owner) sees the new hull colour immediately.
            if (session.Joined && SetActiveWorld(session.CurrentLocationId))
            {
                var rec = _worlds.Active.LandedFor(session.State.PlayerId);
                if (rec.Placed)
                {
                    BroadcastToWorld(LandedShipMessage(session.State.PlayerId, rec, removed: false));
                }
            }
        }
    }

    /// <summary>Stores a player's custom pixel face (persisted, since it follows the player to any server they
    /// set it on) and relays it to the other players on the same world. Out of band from the 10 Hz presence
    /// stream — the bitmap is heavier and changes rarely.</summary>
    private void HandleSetFace(PlayerSession session, SetFaceIntent intent)
    {
        var pixels = intent.Pixels ?? string.Empty;
        if (pixels == session.State.FacePixels)
        {
            return; // unchanged (e.g. the redundant on-join send) — no save, no broadcast
        }

        session.State.FacePixels = pixels;
        _repo.SavePlayer(session.State);
        BroadcastFace(session);
    }

    /// <summary>Sends a player's face to every other joined player on the same world.</summary>
    private void BroadcastFace(PlayerSession subject)
    {
        var msg = FaceOf(subject);
        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined && viewer.ConnectionId != subject.ConnectionId
                && viewer.CurrentLocationId == subject.CurrentLocationId)
            {
                Send(viewer, msg);
            }
        }
    }

    private static PlayerFace FaceOf(PlayerSession s)
        => new() { PlayerId = s.State.PlayerId, Pixels = s.State.FacePixels ?? string.Empty };

    /// <summary>Sends the new player the custom faces of everyone already online on their world.</summary>
    private void SendExistingFaces(PlayerSession newcomer)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other.ConnectionId != newcomer.ConnectionId
                && other.CurrentLocationId == newcomer.CurrentLocationId
                && !string.IsNullOrEmpty(other.State.FacePixels))
            {
                Send(newcomer, FaceOf(other));
            }
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

    private PlayerPresence PresenceOf(PlayerSession s)
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
            // A player flying up in SPACE keeps their world id but must not keep standing on the pad
            // as a frozen ghost — mark them stealthed (clients hide stealthed avatars + nameplates).
            Stealthed = p.Stealthed || InSpace(p.PlayerId),
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

        // Interest management (AoI): only update a viewer about subjects near enough for them to actually see.
        // Beyond the streamed view distance (+ a margin) the remote avatar sits in unloaded/culled terrain, so
        // skipping it saves bandwidth + CPU and lets the player count scale past the small-coop default without
        // an O(players²) presence flood. Derived from the world's view distance so it auto-scales and can never
        // be tighter than what clients render; a player straddling a world-wrap seam still counts as near.
        double aoi = (_config.ViewDistanceChunks + 4) * WorldConstants.ChunkSize;
        double aoiSq = aoi * aoi;

        foreach (var subject in joined)
        {
            // Encode each subject's presence once, then fan it out to every nearby viewer — the old per-viewer
            // Send re-serialized it, making this O(players²) encodes per presence tick.
            var payload = NetCodec.Encode(PresenceOf(subject));
            var subjectPos = subject.State.Position;
            foreach (var viewer in joined)
            {
                if (viewer.ConnectionId == subject.ConnectionId)
                {
                    continue;
                }

                if (WrapDistSq(subjectPos, viewer.State.Position) > aoiSq)
                {
                    continue; // out of this viewer's area of interest
                }

                SendEncoded(viewer.ConnectionId, payload);
            }
        }
    }
}
