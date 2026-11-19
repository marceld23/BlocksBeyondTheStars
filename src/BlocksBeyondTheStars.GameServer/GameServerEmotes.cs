// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Emotes and the body states other players see. A player's emote (a wave, a dance — a fixed list, never text) is
/// checked and passed on to the players close enough to see it; the server adds the two body states only it knows:
/// a short flinch when a player is hurt and the fall when one is defeated. A villager who sees a wave waves back
/// with a hello of its own. Nothing here is saved, nothing changes the world: it is a relay with a pace limit, so a
/// held key cannot flood the others.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The shortest time between two emotes of one player, seconds.</summary>
    internal const double EmoteCooldownSeconds = 0.8;

    /// <summary>The shortest time between two flinches of one player the others are told about, seconds — damage that
    /// ticks (thin air, heat) would otherwise send one per tick.</summary>
    internal const double HurtEmoteGapSeconds = 0.6;

    /// <summary>Health per second from which one damage call counts as a blow (a bite, a strike, a hard landing) rather
    /// than as damage that ticks: the hazards and the machines' steady fire stay far below it, a single hit far above.</summary>
    internal const float HurtBlowPerSecond = 45f;

    /// <summary>How far an emote is seen, blocks.</summary>
    internal const float EmoteRange = 48f;

    /// <summary>How close a villager must stand to wave back, blocks.</summary>
    internal const float WaveBackRange = 8f;

    /// <summary>The shortest time between two villagers waving back at one player, seconds.</summary>
    internal const double WaveBackGapSeconds = 12.0;

    private readonly Dictionary<string, double> _emoteReadyAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _hurtEmoteReadyAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _hurtBlowReadyAt = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _waveBackReadyAt = new(StringComparer.Ordinal);

    /// <summary>A player plays an emote: a known one, not faster than <see cref="EmoteCooldownSeconds"/>, and not as
    /// an unseen observer.</summary>
    private void HandleEmote(PlayerSession session, PlayerEmoteIntent intent)
    {
        var p = session.State;
        if (!PlayerEmotes.MaySend(intent.Emote) || session.Spectating)
        {
            return;
        }

        if (_emoteReadyAt.TryGetValue(p.PlayerId, out double ready) && _uptime < ready)
        {
            return;
        }

        _emoteReadyAt[p.PlayerId] = _uptime + EmoteCooldownSeconds;
        BroadcastPlayerEmote(p, intent.Emote);
        if (EmoteEventArg(intent.Emote) is { } arg)
        {
            RaiseGameEvent(session, GameEventKeys.EmotePlayed, arg); // #3832
        }

        if (intent.Emote == PlayerEmotes.Wave)
        {
            WaveBack(session);
        }
    }

    /// <summary>The argument of <see cref="GameEventKeys.EmotePlayed"/> for an emote of the wheel; null for anything
    /// else a client may send (getting up from sitting is the end of an emote, not one).</summary>
    internal static string? EmoteEventArg(byte emote) => emote switch
    {
        PlayerEmotes.Wave => "wave",
        PlayerEmotes.Nod => "nod",
        PlayerEmotes.Cheer => "cheer",
        PlayerEmotes.Point => "point",
        PlayerEmotes.HeadShake => "head_shake",
        PlayerEmotes.Dance => "dance",
        PlayerEmotes.Clap => "clap",
        PlayerEmotes.Sit => "sit",
        PlayerEmotes.Bow => "bow",
        _ => null,
    };

    /// <summary>Tells every other player near <paramref name="p"/> that its figure plays <paramref name="emote"/>.
    /// Aboard and outside do not see each other: the ship's hull is in between. A cloaked player's figure is hidden on
    /// every other client, so nothing is sent for it at all — the emote's sound would give the place away.
    /// <paramref name="quiet"/>: a flinch that is seen but not heard (<see cref="PlayerEmote.Quiet"/>).</summary>
    private void BroadcastPlayerEmote(PlayerState p, byte emote, bool quiet = false)
    {
        if (p.Stealthed)
        {
            return;
        }

        double rangeSq = (double)EmoteRange * EmoteRange;
        // #3767: a hit and a fall are heard in the figure's own voice.
        int voice = emote is PlayerEmotes.Hurt or PlayerEmotes.Down ? FindSessionByPlayerId(p.PlayerId)?.Voice ?? 0 : 0;
        foreach (var s in JoinedInActiveWorld())
        {
            var other = s.State;
            if (other.PlayerId == p.PlayerId || other.AboardShip != p.AboardShip
                || WrapDistSq(other.Position, p.Position) > rangeSq)
            {
                continue;
            }

            Send(s, new PlayerEmote { PlayerId = p.PlayerId, Emote = emote, Voice = voice, Quiet = quiet });
        }
    }

    /// <summary>After a hit took <paramref name="lost"/> health from <paramref name="p"/>: the others see the flinch
    /// (paced), or the fall when it was the last of the health.</summary>
    private void NotePlayerHurt(PlayerState p, float lost)
    {
        if (lost <= 0f)
        {
            return;
        }

        if (p.Health <= 0f)
        {
            BroadcastPlayerEmote(p, PlayerEmotes.Down);
            return;
        }

        // A blow is heard, damage that ticks is only seen: thin air, cold or fire take a sliver of health every tick, and
        // a pain grunt with each paced flinch would never stop for whoever stands beside the victim. Measured against
        // the tick's length, so the line between the two does not move with the tick rate (a very long tick counts as a
        // tenth of a second: a hit must stay a hit on a slow host).
        bool blow = lost >= HurtBlowPerSecond * (float)System.Math.Clamp(_tickSeconds, 0.001, 0.1);
        var paced = blow ? _hurtBlowReadyAt : _hurtEmoteReadyAt;
        if (paced.TryGetValue(p.PlayerId, out double ready) && _uptime < ready)
        {
            return;
        }

        _hurtEmoteReadyAt[p.PlayerId] = _uptime + HurtEmoteGapSeconds;
        if (blow)
        {
            _hurtBlowReadyAt[p.PlayerId] = _uptime + HurtEmoteGapSeconds; // its own pace: a silent flinch never swallows a blow
        }

        BroadcastPlayerEmote(p, PlayerEmotes.Hurt, quiet: !blow);
    }

    /// <summary>A villager who sees the wave waves back and says hello — the nearest one that may greet at all
    /// (awake, not busy, in sight, not hostile to this player), at most every <see cref="WaveBackGapSeconds"/>.</summary>
    private void WaveBack(PlayerSession session)
    {
        var p = session.State;
        if (!OnFootHere(session) || InConversationNearby(session)
            || (_waveBackReadyAt.TryGetValue(p.PlayerId, out double ready) && _uptime < ready))
        {
            return;
        }

        ServerNpc? nearest = null;
        double best = (double)WaveBackRange * WaveBackRange;
        foreach (var npc in _npcs)
        {
            if (!MayGreetInPassing(npc) || NpcInPlayerDialog(npc))
            {
                continue;
            }

            double d = WrapDistSq(p.Position, npc.Pos);
            if (d <= best && HasLineOfSight(npc.Pos, p.Position))
            {
                best = d;
                nearest = npc;
            }
        }

        if (nearest is null)
        {
            return;
        }

        string npcKey = NpcKeyForNpc(session, nearest);
        if (RelationshipTier(RelationshipValue(p, npcKey)) == "hostile")
        {
            return;
        }

        _waveBackReadyAt[p.PlayerId] = _uptime + WaveBackGapSeconds;
        SendPassingGreeting(session, nearest, npcKey, welcomeBack: false); // a wave and a hello of its own
        nearest.AttendUntil = _uptime + PassingAttendSeconds;
        nearest.AttendPos = p.Position;
    }

    /// <summary>Test seam: a player's emote as the dispatch would hand it over.</summary>
    public void EmoteForTest(string playerId, byte emote)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleEmote(session, new PlayerEmoteIntent { Emote = emote });
        }
    }

    /// <summary>Test seam: takes health from a player the way a hazard's tick or a hit does (unmitigated).</summary>
    public void HurtForTest(string playerId, float amount)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            DamagePlayer(session.State, amount);
        }
    }

    /// <summary>Test seam (#3767): a player's appearance (colours and voice) as the dispatch would hand it over.</summary>
    internal void AppearanceForTest(PlayerSession session, SetAppearanceIntent intent) => HandleSetAppearance(session, intent);
}
