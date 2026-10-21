// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The queries server tests ask of the shared <see cref="RecordingTransport"/>: who was told what, which VEGA lines and
/// scenes a player got, and in which order. They used to be grown separately on 42 private copies of the transport; a
/// query only one test file wants belongs in a file-scoped <c>file static class</c> at the bottom of that file instead.
/// </summary>
public static class RecordingTransportExtensions
{
    /// <summary>Every message sent, per connection and broadcast alike, in recording order.</summary>
    public static IEnumerable<object> Messages(this RecordingTransport t) => t.Sent.Select(x => x.Msg);

    /// <summary>Every message sent to this player's connection, in order (broadcasts left out).</summary>
    public static List<object> To(this RecordingTransport t, PlayerSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).ToList();

    /// <summary>The messages of one type sent to this player's connection, in order.</summary>
    public static List<T> To<T>(this RecordingTransport t, PlayerSession who) => t.To(who).OfType<T>().ToList();

    /// <summary>The text of every chat/server line sent, to anyone, in order.</summary>
    public static List<string> Texts(this RecordingTransport t) => t.Messages().OfType<ServerMessage>().Select(m => m.Text).ToList();

    /// <summary>The text of every chat/server line sent to this player, in order.</summary>
    public static List<string> TextsTo(this RecordingTransport t, PlayerSession who)
        => t.To<ServerMessage>(who).Select(m => m.Text).ToList();

    /// <summary>The line keys VEGA spoke to this player, in order (chip-only updates left out).</summary>
    public static List<string> LineKeys(this RecordingTransport t, PlayerSession who)
        => t.To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

    /// <summary>The VEGA lines spoken to this player, in order (chip-only updates left out).</summary>
    public static List<ShipAiLine> Lines(this RecordingTransport t, PlayerSession who)
        => t.To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).ToList();

    /// <summary>The VEGA lines with this key spoken to this player, in order.</summary>
    public static List<ShipAiLine> Lines(this RecordingTransport t, PlayerSession who, string key)
        => t.To<ShipAiLine>(who).Where(l => l.LineKey == key).ToList();

    /// <summary>The lines of one story pack this player was told, in order.</summary>
    public static List<ShipAiLine> PackLines(this RecordingTransport t, PlayerSession who, string pack)
        => t.To<ShipAiLine>(who).Where(l => l.PackId == pack && l.LineKey.Length > 0).ToList();

    /// <summary>The cutscene ids this player was sent, in order.</summary>
    public static List<string> Plays(this RecordingTransport t, PlayerSession who)
        => t.To<CutscenePlay>(who).Select(c => c.CutsceneId).ToList();

    /// <summary>The cutscene ids of one story pack this player was sent, in order.</summary>
    public static List<string> Plays(this RecordingTransport t, PlayerSession who, string pack)
        => t.To<CutscenePlay>(who).Where(c => c.PackId == pack).Select(c => c.CutsceneId).ToList();

    /// <summary>The cutscenes this player was sent as (pack, id), in order.</summary>
    public static List<(string Pack, string Id)> PackPlays(this RecordingTransport t, PlayerSession who)
        => t.To<CutscenePlay>(who).Select(c => (c.PackId, c.CutsceneId)).ToList();

    /// <summary>The one cutscene with this id sent to this player (fails unless there is exactly one).</summary>
    public static CutscenePlay Play(this RecordingTransport t, PlayerSession who, string id)
        => t.To<CutscenePlay>(who).Single(c => c.CutsceneId == id);

    /// <summary>The chapter offers VEGA made this player, in order.</summary>
    public static List<ShipAiLine> Offers(this RecordingTransport t, PlayerSession who)
        => t.To<ShipAiLine>(who).Where(l => l.LineKey == SvGameServer.ChapterOfferLine).ToList();

    /// <summary>What each chapter offer made to this player offered (its line argument), in order.</summary>
    public static List<string> OfferArgs(this RecordingTransport t, PlayerSession who) => t.Offers(who).Select(l => l.LineArg).ToList();

    /// <summary>The mission-chain follow-up lines (next mission, next research) spoken to this player, in order.</summary>
    public static List<ShipAiLine> FollowUps(this RecordingTransport t, PlayerSession who)
        => t.To<ShipAiLine>(who).Where(l => l.LineKey is SvGameServer.VegaChainNextLine or SvGameServer.VegaChainNextResearchLine).ToList();

    /// <summary>Where a scene of <paramref name="pack"/> and a line went out in the player's stream (-1 = never).</summary>
    public static (int Scene, int Line) Order(this RecordingTransport t, PlayerSession who, string pack, string scene, string lineKey)
    {
        var sent = t.To(who);
        return (sent.FindIndex(m => m is CutscenePlay c && c.PackId == pack && c.CutsceneId == scene),
            sent.FindIndex(m => m is ShipAiLine l && l.LineKey == lineKey));
    }
}
