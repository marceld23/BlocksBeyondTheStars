// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
    // #1530: a subject whose presence did not change is re-sent to a viewer only every this many beats
    // (0.5 s at 10 Hz) — the client hides an avatar after 3 s of silence (RemotePlayers.StaleHideSeconds),
    // so this keeps 6× headroom while a standing player costs a fifth of the traffic.
    private const int PresenceKeepAliveBeats = 5;

    // Per-world (routes through the active world) — see LoadedWorld.SincePresence for why a shared field starves worlds.
    private double _sincePresence { get => _worlds.Active.SincePresence; set => _worlds.Active.SincePresence = value; }

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
    // The custom face is a square grid of palette indices, one hex char per pixel (client FacePalette).
    // The server treats it as opaque, but MUST bound and charset-check it: it is persisted to disk and
    // rebroadcast to every player, so an unvalidated blob is a memory/disk/bandwidth vector (audit
    // 2026-07-05). An empty string is allowed and means "no custom face".
    //
    // Two sizes are legal, not one: the editor moved from 16×16 to 32×32 (#840 — a player asked for finer
    // pixels), and faces drawn before that still arrive at 256 chars from older clients and older saves.
    // Accepting exactly these two keeps the bound tight while nobody's existing face is rejected.
    private const int FacePixelCountLegacy = 16 * 16;
    private const int FacePixelCount = 32 * 32;

    private static bool IsValidFace(string face)
    {
        if (face.Length == 0)
        {
            return true;
        }

        if (face.Length != FacePixelCount && face.Length != FacePixelCountLegacy)
        {
            return false;
        }

        return PaintPayload.IsValidSymbols(face);
    }

    private void HandleSetFace(PlayerSession session, SetFaceIntent intent)
    {
        var pixels = intent.Pixels ?? string.Empty;
        if (!IsValidFace(pixels))
        {
            return; // malformed (wrong length or non-hex) — drop it, never persist or relay
        }

        if (pixels == session.State.FacePixels)
        {
            return; // unchanged (e.g. the redundant on-join send) — no save, no broadcast
        }

        // Simple anti-spam: a face changes rarely, so one accepted change per 2 s per player is generous.
        // Without it, alternating two valid faces would force repeated disk saves + world-wide rebroadcast.
        if (_uptime < session.NextFaceChangeAt)
        {
            return;
        }

        session.NextFaceChangeAt = _uptime + 2.0;
        session.State.FacePixels = pixels;
        _repo.SavePlayer(session.State);
        BroadcastFace(session);
    }

    // Avatar body paint (#874): the face's siblings for torso/arms/legs/helmet. Same treatment — opaque
    // hex payload, but bounded to the part's EXACT expected length (concatenated 32×32 chunks; no legacy
    // size exists for paints) and charset-checked before it is persisted or relayed. The 2 s face rate
    // limit is shared across all appearance edits, so alternating face/torso spam can't dodge it.
    private static bool IsValidBodyPaint(int part, string pixels)
    {
        if (!BodyPaint.IsValidPart(part))
        {
            return false;
        }

        if (pixels.Length == 0)
        {
            return true;
        }

        if (pixels.Length != BodyPaint.ExpectedLength(part))
        {
            return false;
        }

        return PaintPayload.IsValidSymbols(pixels);
    }

    private void HandleSetBodyPaint(PlayerSession session, SetBodyPaintIntent intent)
    {
        var pixels = intent.Pixels ?? string.Empty;
        if (!IsValidBodyPaint(intent.Part, pixels))
        {
            return; // malformed (unknown part, wrong length or non-hex) — drop it, never persist or relay
        }

        if (pixels == session.State.GetBodyPaint(intent.Part))
        {
            return; // unchanged (e.g. the redundant on-join send) — no save, no broadcast
        }

        if (_uptime < session.NextFaceChangeAt)
        {
            return; // shared appearance anti-spam (see HandleSetFace)
        }

        session.NextFaceChangeAt = _uptime + 2.0;
        session.State.SetBodyPaint(intent.Part, pixels);
        _repo.SavePlayer(session.State);
        BroadcastBodyPaint(session, intent.Part);
    }

    /// <summary>Sends one of a player's body paintings to every other joined player on the same world.</summary>
    private void BroadcastBodyPaint(PlayerSession subject, int part)
    {
        if (subject.Spectating)
        {
            return; // observers are invisible — nothing about them goes out (issue #487)
        }

        var msg = BodyPaintOf(subject, part);
        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined && viewer.ConnectionId != subject.ConnectionId
                && viewer.CurrentLocationId == subject.CurrentLocationId)
            {
                Send(viewer, msg);
            }
        }
    }

    private static PlayerBodyPaint BodyPaintOf(PlayerSession s, int part)
        => new() { PlayerId = s.State.PlayerId, Part = part, Pixels = s.State.GetBodyPaint(part) ?? string.Empty };

    /// <summary>Test seam: runs the body-paint handler without a socket.</summary>
    public void SetBodyPaintForTest(PlayerSession session, int part, string pixels)
        => HandleSetBodyPaint(session, new SetBodyPaintIntent { Part = part, Pixels = pixels });

    /// <summary>Sends a player's face to every other joined player on the same world.</summary>
    private void BroadcastFace(PlayerSession subject)
    {
        if (subject.Spectating)
        {
            return; // observers are invisible — nothing about them goes out (issue #487)
        }

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

    /// <summary>Exchanges custom appearance BOTH ways for a player entering a world (#982): they receive
    /// everyone else's face + body paintings, and everyone else receives theirs.
    ///
    /// Only the pull half existed, and only on join. Faces and paintings are out-of-band, one-shot messages
    /// filtered by world — so a player who joined (or travelled in) was the only one who saw the others
    /// correctly, while everyone already there kept rendering them as a blank default avatar until they
    /// happened to repaint themselves. Presence updates carry the colour scheme but not the pixel art.</summary>
    private void SyncAppearance(PlayerSession session)
    {
        SendExistingFaces(session);
        if (session.Spectating)
        {
            return; // observers are invisible — nothing about them goes out (issue #487)
        }

        if (!string.IsNullOrEmpty(session.State.FacePixels))
        {
            BroadcastFace(session);
        }

        for (int part = 0; part < BodyPaint.PartCount; part++)
        {
            if (!string.IsNullOrEmpty(session.State.GetBodyPaint(part)))
            {
                BroadcastBodyPaint(session, part);
            }
        }
    }

    /// <summary>Sends the new player the custom faces AND body paintings of everyone already online on
    /// their world (one message per painted part — most parts are unpainted and cost nothing).</summary>
    private void SendExistingFaces(PlayerSession newcomer)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && !other.Spectating && other.ConnectionId != newcomer.ConnectionId
                && other.CurrentLocationId == newcomer.CurrentLocationId)
            {
                if (!string.IsNullOrEmpty(other.State.FacePixels))
                {
                    Send(newcomer, FaceOf(other));
                }

                for (int part = 0; part < BodyPaint.PartCount; part++)
                {
                    if (!string.IsNullOrEmpty(other.State.GetBodyPaint(part)))
                    {
                        Send(newcomer, BodyPaintOf(other, part));
                    }
                }
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

    /// <summary>FNV-1a over every wire field of a presence — equal hashes mean an identical message (#1530).</summary>
    private static ulong PresenceHash(PlayerPresence p)
    {
        ulong h = 14695981039346656037UL;
        void Mix(ulong v) => h = (h ^ v) * 1099511628211UL;
        void MixText(string? s)
        {
            if (s == null)
            {
                Mix(0xFFFFFFFFUL);
                return;
            }

            foreach (char c in s)
            {
                Mix(c);
            }

            Mix(0x1FUL);
        }

        MixText(p.PlayerId);
        MixText(p.Name);
        Mix((uint)BitConverter.SingleToInt32Bits(p.X));
        Mix((uint)BitConverter.SingleToInt32Bits(p.Y));
        Mix((uint)BitConverter.SingleToInt32Bits(p.Z));
        Mix((uint)BitConverter.SingleToInt32Bits(p.Yaw));
        Mix((uint)p.Skin);
        Mix((uint)p.Torso);
        Mix((uint)p.Arms);
        Mix((uint)p.Legs);
        Mix((p.Stealthed ? 1UL : 0UL) | (p.Jetpacking ? 2UL : 0UL) | (p.Seated ? 4UL : 0UL));
        Mix((uint)p.Gear);
        MixText(p.Held);
        return h;
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
            Seated = p.Seated,
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

    /// <summary>Sends the new player the presence of everyone already online. Observers are skipped: a
    /// newcomer must never learn that one is there (issue #487).</summary>
    private void SendExistingPresences(PlayerSession newcomer)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && !other.Spectating && other.ConnectionId != newcomer.ConnectionId
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

        // #1530: a change of the joined set (a join, a leave, a spectate toggle) forgets what every viewer has
        // seen, so the next beat is a full resend — a joiner or an un-spectating admin sees everyone at once.
        long signature = 17;
        foreach (var s in joined)
        {
            signature = unchecked(signature * 31 + s.ConnectionId * 2 + (s.Spectating ? 1 : 0));
        }

        var world = _worlds.Active;
        if (world.PresenceViewerSignature != signature)
        {
            world.PresenceViewerSignature = signature;
            foreach (var s in joined)
            {
                s.PresenceSentTo.Clear();
            }
        }

        foreach (var subject in joined)
        {
            if (subject.Spectating)
            {
                continue; // observer: nothing about them is ever broadcast (issue #487)
            }

            // #1530: send on change + keep-alive. The presence is hashed per beat; a viewer who already holds
            // this exact presence and received it fewer than PresenceKeepAliveBeats ago gets nothing. Encoded
            // lazily and once per subject (the old per-viewer Send re-serialized it, O(players²) encodes).
            int beat = ++subject.PresenceBeat;
            var presence = PresenceOf(subject);
            ulong hash = PresenceHash(presence);
            byte[]? payload = null;
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

                if (subject.PresenceSentTo.TryGetValue(viewer.ConnectionId, out var last)
                    && last.Hash == hash && beat - last.Beat < PresenceKeepAliveBeats)
                {
                    continue; // unchanged and recent — the client keeps rendering the pose it has
                }

                if (_objectTransport != null)
                {
                    _objectTransport.SendMessage(viewer.ConnectionId, presence, DeliveryMode.Sequenced); // #1531
                }
                else
                {
                    payload ??= NetCodec.Encode(presence);
                    SendEncoded(viewer.ConnectionId, payload, DeliveryMode.Sequenced); // #1534: never behind a chunk resend
                }

                subject.PresenceSentTo[viewer.ConnectionId] = (hash, beat);
            }

            if (subject.PresenceSentTo.Count > 64)
            {
                subject.PresenceSentTo.Clear(); // viewers that left accumulate here — a cheap reset now and then
            }
        }
    }
}
