// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3905: this animal lives on the other side — it is in the creature list of the players who are over there
    /// and of nobody else, and it is outside the world's fauna cap. Server-only runtime state, never persisted.</summary>
    public bool OtherSideOnly { get; set; }
}

/// <summary>
/// Who sees whom across the threshold (#3905). A player who is over there is <b>withheld</b> from everything the server
/// sends the players on our side about them — like an observer, where the data is produced, never by asking a client not
/// to draw it (a cloaked player is only flagged; this is not that). Players over there see each other, and they receive
/// everything of our side unchanged.
/// <para>The rule is one predicate, <see cref="HiddenFrom"/>, asked at every place that tells one player about another:
/// the presence stream and its join handshake, the face, the body paint and the tool looks, gestures, the effects of an
/// action, free play on the melody bar, the star map's player list, an ally's teleport, the bump snapshot, VEGA's "a
/// pilot nearby" and the story's "a player is near". The world's own senses — slide doors, Crystal Net presence sensors,
/// animals, giants listening for steps, villagers greeting a passer-by, drops on the ground — skip a player over there
/// at their own gathering points (each names #3905). <c>docs/developer/OTHER_SIDE.md</c> lists every site.</para>
/// <para>The other direction — the animals and figures that exist only over there — is the entity lists: the creature
/// list goes out in two variants, each encoded once per flush (<see cref="SendCreatureListBySide"/>); the NPC list is
/// built per receiver anyway.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether <paramref name="viewer"/> must learn nothing about <paramref name="subject"/>: the subject is on
    /// the other side and the viewer is not.</summary>
    internal static bool HiddenFrom(PlayerSession viewer, PlayerSession subject) => subject.OtherSide && !viewer.OtherSide;

    /// <summary>The crossing, seen from the others: our side drops the avatar (a <see cref="PlayerLeft"/>, as for an
    /// observer), and the players already over there — withheld from the newcomer until now — show their looks. Their
    /// presence follows with the next beat (the viewer signature changed, <c>TickPresence</c>).</summary>
    private void HideFromOurSide(PlayerSession s)
    {
        var left = new PlayerLeft { PlayerId = s.State.PlayerId };
        foreach (var other in _sessions.Values)
        {
            if (!other.Joined || ReferenceEquals(other, s) || other.CurrentLocationId != s.CurrentLocationId)
            {
                continue;
            }

            if (other.OtherSide)
            {
                SendAppearanceOf(other, s);
            }
            else
            {
                Send(other, left);
            }
        }
    }

    /// <summary>The return, seen from the others: the one who came back drops the avatars of those still over there, and
    /// our side gets the looks back (the presence resumes with the next beat by itself; face, paint and tool looks are
    /// one-shot messages and were withheld meanwhile).</summary>
    private void ShowOnOurSide(PlayerSession s)
    {
        foreach (var other in _sessions.Values)
        {
            if (other.Joined && other.OtherSide && !ReferenceEquals(other, s) && other.CurrentLocationId == s.CurrentLocationId)
            {
                Send(s, new PlayerLeft { PlayerId = other.State.PlayerId });
            }
        }

        if (s.Spectating)
        {
            return; // an observer stays unseen (#487)
        }

        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined && !viewer.OtherSide && !ReferenceEquals(viewer, s) && viewer.CurrentLocationId == s.CurrentLocationId)
            {
                SendAppearanceOf(s, viewer);
            }
        }
    }

    /// <summary>The one-shot looks of <paramref name="subject"/> for <paramref name="viewer"/>: the face, every painted
    /// body part, every tool look.</summary>
    private void SendAppearanceOf(PlayerSession subject, PlayerSession viewer)
    {
        if (!string.IsNullOrEmpty(subject.State.FacePixels))
        {
            Send(viewer, FaceOf(subject));
        }

        for (int part = 0; part < BodyPaint.PartCount; part++)
        {
            if (!string.IsNullOrEmpty(subject.State.GetBodyPaint(part)))
            {
                Send(viewer, BodyPaintOf(subject, part));
            }
        }

        foreach (string itemKey in subject.State.ToolLooks.Keys)
        {
            Send(viewer, ToolLookOf(subject, itemKey));
        }
    }

    /// <summary>A flip changed who gets which list: the entity lists of the player's world go out with this tick's flush,
    /// and the star map's player list is told again.</summary>
    private void OtherSideListsChanged(PlayerSession s)
    {
        if (_worlds.Active is { } active && active.LocationId == s.CurrentLocationId)
        {
            BroadcastCreatures();
            BroadcastNpcs();
        }

        BroadcastStarMap();
    }

    /// <summary>The star map's player list as <paramref name="session"/> is sent it: everybody joined, without the
    /// observers (#3052: an observer is listed to nobody but themselves) and without whoever is over there while the
    /// receiver is not (#3905).</summary>
    private NetPlayerLocation[] StarMapPlayersFor(PlayerSession session)
        => _sessions.Values
            .Where(s => s.Joined && (!s.Spectating || s == session) && !HiddenFrom(session, s))
            .Select(s => new NetPlayerLocation { Name = s.State.Name, LocationId = s.CurrentLocationId })
            .ToArray();

    // ---------------- The entity lists ----------------

    /// <summary>The creatures <paramref name="session"/>'s client is told about: all of them over there, the ones of our
    /// side otherwise.</summary>
    private NetCreature[] CreaturesFor(PlayerSession session)
    {
        var list = new List<NetCreature>(_creatures.Count);
        foreach (var c in _creatures)
        {
            if (!c.OtherSideOnly || session.OtherSide)
            {
                list.Add(ToNetCreature(c));
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// The creature list of the active world for everybody on it (the end-of-tick flush, #1530). While no animal of the
    /// other side lives here it is the one broadcast it always was. Otherwise two lists — our side's, and our side's plus
    /// the other side's — each built and encoded once, never per player.
    /// </summary>
    private void SendCreatureListBySide()
    {
        bool anyOver = false;
        foreach (var c in _creatures)
        {
            if (c.OtherSideOnly)
            {
                anyOver = true;
                break;
            }
        }

        if (!anyOver)
        {
            BroadcastToWorld(new CreatureList { Creatures = _creatures.Select(ToNetCreature).ToArray(), ServerTime = SnapshotTime });
            return;
        }

        var all = new NetCreature[_creatures.Count];
        var ours = new List<NetCreature>(_creatures.Count);
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = ToNetCreature(_creatures[i]);
            if (!_creatures[i].OtherSideOnly)
            {
                ours.Add(all[i]);
            }
        }

        SendBySide(
            new CreatureList { Creatures = ours.ToArray(), ServerTime = SnapshotTime },
            new CreatureList { Creatures = all, ServerTime = SnapshotTime });
    }

    /// <summary>Sends every player of the active world one of two messages — <paramref name="ours"/> on our side,
    /// <paramref name="over"/> over there — each encoded at most once.</summary>
    private void SendBySide(object ours, object over)
    {
        byte[]? oursPayload = null, overPayload = null;
        foreach (var s in JoinedInActiveWorld())
        {
            if (_objectTransport != null)
            {
                _objectTransport.SendMessage(s.ConnectionId, s.OtherSide ? over : ours, DeliveryMode.ReliableOrdered);
            }
            else if (s.OtherSide)
            {
                SendEncoded(s.ConnectionId, overPayload ??= NetCodec.Encode(over));
            }
            else
            {
                SendEncoded(s.ConnectionId, oursPayload ??= NetCodec.Encode(ours));
            }
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the ids of the creatures this player's client is told about.</summary>
    public IReadOnlyList<string> CreatureIdsSentToForTest(PlayerSession session) => CreaturesFor(session).Select(c => c.Id).ToList();

    /// <summary>Test seam: the names on the star map's player list as this player is sent it.</summary>
    public IReadOnlyList<string> StarMapPlayerNamesForTest(PlayerSession session)
        => StarMapPlayersFor(session).Select(p => p.Name).ToList();
}
