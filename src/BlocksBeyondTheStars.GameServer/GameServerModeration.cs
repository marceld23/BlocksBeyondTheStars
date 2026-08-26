// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Moderation;
using BlocksBeyondTheStars.Shared.Notifications;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Kicking a player out of a running world (#497). A ban — fleet-wide or the world owner's own list — is
/// enforced by the control plane at the NEXT join grant; without this the offender simply keeps playing
/// until they disconnect on their own, which is exactly the moment when moderation matters least.
///
/// Two intake paths, same queue: the instance gateway's token-gated <c>POST /kick</c> (accept-loop thread)
/// and the in-game <c>/kick</c> command of a world admin (tick thread) — so intake is lock-guarded and the
/// tick applies it, mirroring the maintenance-announcement plumbing.
///
/// The player is told first (<see cref="JoinRejected"/>, which the client already renders as "back to the
/// menu with this reason") and the socket is closed a moment later: the notice needs to leave the transport
/// first, and a modified client must not be able to ignore it and play on.
/// </summary>
public sealed partial class GameServer
{
    private const int MaxKickReasonLength = 200;

    /// <summary>Grace between the rejection message and closing the pipe, so the packet still goes out.</summary>
    private const double KickFlushSeconds = 1.0;

    private readonly Lock _kickGate = new();
    private readonly List<(string PlayerName, string Reason)> _kickQueue = new();
    private readonly List<(int ConnectionId, double Remaining)> _kickFlush = new();

    /// <summary>
    /// Queues a kick for the next tick. Safe to call from any thread (the HTTP gateway's accept loop uses
    /// it). Returns false only when the request is unusable — whether anyone of that name is actually
    /// online is deliberately NOT answered here: the session table belongs to the tick thread, and reading
    /// it from the accept loop to produce a nicer status code would be a data race for no real gain.
    /// </summary>
    public bool EnqueueKick(string? playerName, string? reason)
    {
        string name = StripControlChars(playerName).Trim();
        if (name.Length is < 1 or > 24)
        {
            return false;
        }

        string clean = StripControlChars(reason).Trim();
        if (clean.Length > MaxKickReasonLength)
        {
            clean = clean[..MaxKickReasonLength];
        }

        lock (_kickGate)
        {
            _kickQueue.Add((name, clean));
        }

        return true;
    }

    /// <summary>Per-tick moderation bookkeeping: applies queued kicks, then closes the pipes whose grace
    /// period has run out.</summary>
    private void TickModeration(double deltaSeconds)
    {
        List<(string PlayerName, string Reason)>? pending = null;
        lock (_kickGate)
        {
            if (_kickQueue.Count > 0)
            {
                pending = new List<(string, string)>(_kickQueue);
                _kickQueue.Clear();
            }
        }

        if (pending is { })
        {
            foreach (var (playerName, reason) in pending)
            {
                ApplyKick(playerName, reason);
            }
        }

        for (int i = _kickFlush.Count - 1; i >= 0; i--)
        {
            var entry = _kickFlush[i];
            entry.Remaining -= deltaSeconds;
            if (entry.Remaining <= 0)
            {
                _transport.DisconnectClient(entry.ConnectionId);
                _kickFlush.RemoveAt(i);
            }
            else
            {
                _kickFlush[i] = entry;
            }
        }
    }

    // ---------------- Name screening + operator notifications (#938) ----------------

    /// <summary>Optional operator push channel (BBS_NOTIFY_URL) — set by the host shell like
    /// <see cref="CrashUploader"/>; null/unconfigured on the fleet (the WorldHost pings there) and in
    /// singleplayer. Everything through it is fire-and-forget.</summary>
    public AdminNotifier? AdminNotifier { get; set; }

    private NameScreen? _nameScreen;

    /// <summary>The join-name screen, built lazily from the config lists (defaults + BBS_BLOCKED_WORDS /
    /// BBS_WATCH_WORDS). Shared implementation with the WorldHost gates, so a name the portal rejects is
    /// rejected on direct connect too.</summary>
    private NameScreen JoinNameScreen => _nameScreen ??= new NameScreen(_config.BlockedNameWords, _config.WatchNameWords);

    private ChatScreen? _chatScreen;

    /// <summary>The chat content screen (#1207), built lazily from the config lists (defaults +
    /// BBS_CHAT_BLOCKED_WORDS / BBS_CHAT_MASKED_WORDS / BBS_CHAT_WATCH_WORDS / BBS_CHAT_ALLOW_WORDS).</summary>
    private ChatScreen ChatContentScreen => _chatScreen ??= new ChatScreen(
        _config.ChatBlockedWords, _config.ChatMaskedWords, _config.ChatWatchWords, _config.ChatAllowWords);

    /// <summary>The chat mode actually applied: the operator's server-wide switch caps or overrides the world
    /// rule — <c>BBS_CHAT_FILTER=off</c> opens every world (private family LAN), <c>strict</c> forces Safe on
    /// every world (public kids' fleet), <c>mask</c> (default) leaves the decision to the world.</summary>
    public ChatMode EffectiveChatMode => _config.ChatFilter switch
    {
        ChatFilterLevel.Off => ChatMode.Open,
        ChatFilterLevel.Strict => ChatMode.Safe,
        _ => Rules.ChatMode,
    };

    /// <summary>Screens one chat line for a sender: drops slurs/hate terms (the sender is told), masks
    /// profanity and personal data (the sender is told once per session), pings the operator on watch-list
    /// hits. Returns the text to relay, or <c>null</c> when the line must not be relayed. Logs the matched
    /// list entry only — never the line.</summary>
    private string? ScreenChatLine(PlayerSession session, string text)
    {
        var mode = EffectiveChatMode;
        if (mode == ChatMode.Open)
        {
            return text;
        }

        var result = ChatContentScreen.Screen(text, mode);
        string who = session.State.Name ?? "?";
        if (result.Watch)
        {
            string term = result.MatchedTerm.Length > 0 ? result.MatchedTerm : "mixed-script";
            _log.Info($"Chat watch: '{who}' used watch-listed '{term}' (relayed, verdict {result.Verdict}).");
            NotifyOperator($"chat watch [{_meta.WorldName}]", $"Player '{who}' used watch-listed term '{term}'.", "eyes");
        }

        switch (result.Verdict)
        {
            case ChatVerdict.Block:
                _log.Info($"Chat filter: dropped a line from '{who}' ({(result.Pii ? "personal data: " : "term: ")}{result.MatchedTerm}).");
                Send(session, new ServerMessage { Text = result.Pii ? "@srv.chat.pii_blocked" : "@srv.chat.blocked" });
                NoteChatFilterHit(session); // #1208: keep doing this and the channel goes quiet for a while
                return null;
            case ChatVerdict.Mask:
                if (!session.ChatMaskNoticeSent)
                {
                    session.ChatMaskNoticeSent = true;
                    Send(session, new ServerMessage { Text = "@srv.chat.masked" });
                }

                NoteChatFilterHit(session); // #1208
                return result.Text;
            default:
                return text;
        }
    }

    // ---------------- Names + AI text go through the same screen (#1221) ----------------
    // Before this, ONLY the join name was screened. Everything else a player types and everyone else then
    // reads — a base name, a station name, a beacon or beam-pad label, a companion's name — was merely
    // control-char-stripped and length-clamped, and the AI backend's greetings and mission flavour went out
    // with nothing but a prompt guard behind them. Same words, same server, same children reading them.

    /// <summary>Screens a player-typed NAME. Returns the name to store, or null when it was refused — in
    /// which case the player has already been told through <paramref name="surface"/>.
    ///
    /// Screened with the CHAT lists, not the join-name list, on purpose: a name screen substring-matches
    /// (right for a 24-character handle, wrong for "Dickichtlager"), while the chat screen matches whole
    /// tokens — and a base name is a short phrase, not a handle.
    ///
    /// A masked verdict is treated as a refusal too, unlike in chat. A chat line is gone in a minute; a
    /// name is persistent and shown to everyone who walks past it, so "Basis f***" is a worse answer than
    /// "pick another name" — and Mask is also the verdict that carries personal data, which must not be
    /// stored at all.</summary>
    private string? ScreenPlayerName(PlayerSession session, string clean, string surface)
    {
        var mode = EffectiveChatMode;
        if (clean.Length == 0 || mode == ChatMode.Open)
        {
            return clean; // private LAN family world: nothing is screened anywhere
        }

        var result = ChatContentScreen.Screen(clean, mode);
        string who = session.State.Name ?? "?";
        if (result.Watch)
        {
            string term = result.MatchedTerm.Length > 0 ? result.MatchedTerm : "mixed-script";
            _log.Info($"Name watch: '{who}' used watch-listed '{term}' in a {surface} name (allowed).");
            NotifyOperator($"name watch [{_meta.WorldName}]",
                $"Player '{who}' used watch-listed term '{term}' in a {surface} name.", "eyes");
        }

        if (result.Verdict == ChatVerdict.Ok)
        {
            return clean;
        }

        _log.Info($"Name filter: refused a {surface} name from '{who}' " +
                  $"({(result.Pii ? "personal data: " : "term: ")}{result.MatchedTerm}).");
        Reject(session, surface, "@srv.name.blocked");
        return null;
    }

    /// <summary>Screens one piece of AI-written text. Returns the text to show, or null when it must not be
    /// shown — which every AI call site already handles as "the backend gave us nothing", falling back to
    /// the authored localized line. Wired in once by wrapping the provider (see
    /// <see cref="ScreenedAiTextProvider"/>), so all six call sites are covered.
    ///
    /// Called from BACKGROUND threads (AI generation never runs on the tick): it only reads the screen and
    /// the world rule and writes nothing, and the operator ping is fire-and-forget.</summary>
    private string? ScreenAiText(string? text)
    {
        var mode = EffectiveChatMode;
        if (string.IsNullOrWhiteSpace(text) || mode == ChatMode.Open)
        {
            return text;
        }

        var result = ChatContentScreen.Screen(text!, mode);
        if (result.Watch)
        {
            string term = result.MatchedTerm.Length > 0 ? result.MatchedTerm : "mixed-script";
            _log.Info($"AI text watch: watch-listed '{term}' (shown).");
            NotifyOperator($"AI text watch [{_meta.WorldName}]",
                $"AI-written text used watch-listed term '{term}'.", "eyes");
        }

        if (result.Verdict == ChatVerdict.Ok)
        {
            return text;
        }

        // Not masked and relayed like a chat line: our own backend wrote this, so the honest fallback is
        // the line we authored ourselves rather than a starred-out sentence in an NPC's mouth.
        _log.Warn($"AI text dropped: matched '{result.MatchedTerm}' — using the authored line instead.");
        return null;
    }

    // ---------------- Anti-spam + temporary auto-mute (#1208) ----------------
    // The 700 ms per-line limit stops a key held down; it does nothing about a burst of distinct lines, and
    // nothing at all about someone who keeps tripping the content filter. Two sliding windows close that gap
    // and both end in the same place: a ten-minute cool-down the sender is TOLD about, plus one operator ping
    // so a human can decide whether more is needed. Counters are RAM-only (see PlayerSession).

    /// <summary>More than this many accepted lines inside <see cref="SpamBurstWindowSeconds"/> is a burst.</summary>
    private const int SpamBurstLines = 6;
    private const double SpamBurstWindowSeconds = 10.0;

    /// <summary>More than this many filter hits inside <see cref="FilterHitWindowSeconds"/> earns a mute.</summary>
    private const int SpamFilterHits = 3;
    private const double FilterHitWindowSeconds = 300.0;

    /// <summary>How long an automatic mute lasts. Long enough to break a flood, short enough that a child who
    /// got carried away is back in the conversation in the same session.</summary>
    private const double ChatAutoMuteSeconds = 600.0;

    /// <summary>Active chat mutes, player id → server uptime (seconds) at which the mute ends (#1294). RAM only,
    /// never persisted: a mute is a cool-down, not a mark on the record — but it is keyed by PLAYER, not by
    /// session, so leaving and rejoining (a ten-second detour on glitch.fun) does not lift it. Expired entries
    /// are pruned whenever a mute is written; there is no per-tick scan. Tick-thread only, like the sessions.</summary>
    private readonly Dictionary<string, double> _chatMutes = new(System.StringComparer.Ordinal);

    /// <summary>Server uptime (seconds) until which this player's chat lines are dropped; 0 = not muted. This is
    /// the SERVER's mute (automatic cool-down or an admin's /silence) — unrelated to the per-player mute a client
    /// applies to someone else's lines (#1209).</summary>
    private double ChatMutedUntil(PlayerSession session)
        => _chatMutes.TryGetValue(session.State.PlayerId, out double until) ? until : 0;

    /// <summary>Starts (or restarts) a mute for this player and drops the entries that have run out — the
    /// dictionary only ever holds the players muted right now.</summary>
    private void SetChatMute(PlayerSession session, double until)
    {
        PruneExpiredChatMutes();
        _chatMutes[session.State.PlayerId] = until;
    }

    private void ClearChatMute(string playerId) => _chatMutes.Remove(playerId);

    private void PruneExpiredChatMutes()
    {
        if (_chatMutes.Count == 0)
        {
            return;
        }

        List<string>? expired = null;
        foreach (var (playerId, until) in _chatMutes)
        {
            if (until <= _uptime)
            {
                (expired ??= new List<string>()).Add(playerId);
            }
        }

        if (expired is not null)
        {
            foreach (var playerId in expired)
            {
                _chatMutes.Remove(playerId);
            }
        }
    }

    /// <summary>Whether this player is currently muted. The notice normally goes out the moment the mute
    /// starts (<see cref="AutoMuteChat"/>); this is the fallback for a mute that began some other way — or in
    /// an earlier session of the same player (#1294) — and it fires at most once per mute AND session, so
    /// hammering Enter does not earn a wall of notices (#1208).</summary>
    private bool ChatMuted(PlayerSession session)
    {
        if (ChatMutedUntil(session) <= _uptime)
        {
            return false;
        }

        if (!session.ChatMuteNoticeSent)
        {
            SendMuteNotice(session);
        }

        return true;
    }

    /// <summary>Tells the sender how long the chat stays paused for them. Silence with no explanation reads
    /// as a broken game — especially to a child — so a mute is never applied quietly.</summary>
    private void SendMuteNotice(PlayerSession session)
    {
        session.ChatMuteNoticeSent = true;
        int minutes = System.Math.Max(1, (int)System.Math.Ceiling((ChatMutedUntil(session) - _uptime) / 60.0));
        Send(session, new ServerMessage { Text = "@srv.chat.muted_until:" + minutes });
    }

    /// <summary>Records an accepted line in the burst window and mutes the sender if it tipped over. Returns
    /// true when the line must be dropped — the line that trips the limit is itself part of the flood.</summary>
    private bool NoteChatLine(PlayerSession session)
    {
        TrimWindow(session.RecentChatAt, SpamBurstWindowSeconds);
        session.RecentChatAt.Add(_uptime);
        if (session.RecentChatAt.Count <= SpamBurstLines)
        {
            return false;
        }

        AutoMuteChat(session, "flooding the channel");
        return true;
    }

    /// <summary>Records a content-filter hit (a line that was blocked or masked) and mutes the sender when
    /// they keep tripping it. Called from <see cref="ScreenChatLine"/>, so it counts what the filter ACTED
    /// on — not a watch-list term, which is relayed untouched and only pings the operator.</summary>
    private void NoteChatFilterHit(PlayerSession session)
    {
        TrimWindow(session.RecentFilterHitsAt, FilterHitWindowSeconds);
        session.RecentFilterHitsAt.Add(_uptime);
        if (session.RecentFilterHitsAt.Count > SpamFilterHits)
        {
            AutoMuteChat(session, "repeatedly tripping the chat filter");
        }
    }

    /// <summary>Starts (or restarts) the cool-down and pings the operator once.</summary>
    private void AutoMuteChat(PlayerSession session, string why)
    {
        SetChatMute(session, _uptime + ChatAutoMuteSeconds);
        session.RecentChatAt.Clear();
        session.RecentFilterHitsAt.Clear();
        SendMuteNotice(session); // straight away — the line that vanished is the one they want explained

        string who = session.State.Name ?? "?";
        int minutes = (int)(ChatAutoMuteSeconds / 60.0);
        _log.Info($"Chat auto-mute: '{who}' muted for {minutes} min ({why}).");
        NotifyOperator($"chat auto-mute [{_meta.WorldName}]",
            $"Player '{who}' was muted for {minutes} minutes ({why}).", "mute");
    }

    /// <summary>Drops the timestamps that have fallen out of a sliding window.</summary>
    private void TrimWindow(List<double> stamps, double windowSeconds)
    {
        double cutoff = _uptime - windowSeconds;
        int drop = 0;
        while (drop < stamps.Count && stamps[drop] < cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            stamps.RemoveRange(0, drop);
        }
    }

    // ---------------- An admin pauses someone's chat by hand (#1223) ----------------
    // Until now the only lever between "say something" and "kick" was nothing at all. This is the middle
    // step: a few minutes of quiet, applied by a human, with the same explained notice the automatic
    // cool-down gives — one mute concept on the server, not two.

    /// <summary>Default length of an admin-applied chat pause when no number is given.</summary>
    private const int DefaultSilenceMinutes = 10;

    /// <summary>Longest chat pause an admin may set in one go — a day. Anything beyond that is a ban
    /// decision, and bans live on the portal where the world's identity does (see the kick note above).</summary>
    private const int MaxSilenceMinutes = 1440;

    /// <summary>Pauses (or resumes) a player's chat. Returns the locale token to answer the admin with.
    /// Runs on the tick thread — unlike the kick, there is no off-thread intake to queue behind (the
    /// optional gateway route of #1223 is not wired: nothing calls it yet).</summary>
    private string ApplyChatSilence(PlayerSession admin, string targetName, int minutes, bool lift)
    {
        if (targetName.Length == 0)
        {
            return "@srv.admin.usage_silence";
        }

        if (string.Equals(targetName, admin.State.Name, System.StringComparison.OrdinalIgnoreCase))
        {
            return "@srv.admin.silence_self"; // reads as a bug report, exactly like kicking yourself
        }

        var target = FindJoinedSessionByName(targetName);
        if (target is null)
        {
            // Lifting a mute does not need the player to be here (#1294): the mute outlives their session, so
            // the admin's undo must too. PlayerId == name (see FindJoinedSessionByName), matched the same way.
            if (lift && FindChatMuteKeyByName(targetName) is { } offlineId)
            {
                ClearChatMute(offlineId);
                _log.Info($"Chat un-silenced: '{offlineId}' (offline) by '{admin.State.Name}'.");
                return "@srv.admin.unsilenced:" + offlineId;
            }

            return "@srv.admin.silence_no_target:" + targetName;
        }

        // Same rule as the kick: a world owner must not be able to silence the operator overseeing their
        // world (#495).
        if (target.IsFleetAdmin)
        {
            return "@srv.admin.silence_no_target:" + targetName;
        }

        string who = target.State.Name ?? "?";
        if (lift)
        {
            ClearChatMute(target.State.PlayerId);
            target.ChatMuteNoticeSent = false;
            target.RecentChatAt.Clear();
            target.RecentFilterHitsAt.Clear();
            Send(target, new ServerMessage { Text = "@srv.chat.unmuted" });
            _log.Info($"Chat un-silenced: '{who}' by '{admin.State.Name}'.");
            return "@srv.admin.unsilenced:" + who;
        }

        int span = System.Math.Clamp(minutes <= 0 ? DefaultSilenceMinutes : minutes, 1, MaxSilenceMinutes);
        SetChatMute(target, _uptime + (span * 60.0));
        target.ChatMuteNoticeSent = false;
        SendMuteNotice(target); // the same "chat is paused for you for N minutes" the auto-mute sends
        _log.Info($"Chat silenced: '{who}' for {span} min by '{admin.State.Name}'.");
        NotifyOperator($"chat silenced [{_meta.WorldName}]",
            $"Admin '{admin.State.Name}' paused '{who}' chat for {span} minutes.", "mute");
        return "@srv.admin.silenced:" + who;
    }

    /// <summary>Test hook: the world's verdict on one piece of AI-written text (#1221) — the text to show,
    /// or null when the authored fallback must be used instead.</summary>
    public string? ScreenAiTextForTest(string? text) => ScreenAiText(text);

    /// <summary>Test hook: that the AI provider really is wrapped in the screen (#1221). The wrapping happens
    /// once in the constructor, and nothing else would notice if a refactor dropped it.</summary>
    public bool AiProviderIsScreenedForTest => _ai is ScreenedAiTextProvider;

    /// <summary>The mute-table key (player id) held under this name, case-insensitively, or null. Used for the
    /// offline /unsilence; an expired entry does not count, so the admin gets "no such player" instead of a
    /// phantom success.</summary>
    private string? FindChatMuteKeyByName(string name)
    {
        foreach (var (playerId, until) in _chatMutes)
        {
            if (until > _uptime && string.Equals(playerId, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return playerId;
            }
        }

        return null;
    }

    /// <summary>Test hook: whether this player's chat is currently muted (#1208, #1223) — online or not (#1294).</summary>
    public bool IsChatMutedForTest(string playerId)
        => _chatMutes.TryGetValue(playerId, out double until) && until > _uptime;

    /// <summary>Test hook: advance the server clock without running a tick, so the sliding windows and the
    /// mute expiry can be exercised without waiting ten real minutes (#1208).</summary>
    public void AdvanceUptimeForTest(double seconds) => _uptime += seconds;

    /// <summary>One best-effort operator ping; never throws into the caller.</summary>
    private void NotifyOperator(string title, string message, string tags = "")
    {
        try
        {
            AdminNotifier?.Post(title, message, tags);
        }
        catch
        {
            // the log line upstream is the source of truth; the ping is optional
        }
    }

    /// <summary>Forwards a <c>/reportpaint</c> / <c>/reportshape</c> report to the report inbox through
    /// the crash-upload sink (the world's paint_report row + log line stay the source of truth — before
    /// this, those reports were invisible to the fleet operator, #938) and pings the notify channel.
    /// Same wire shape as <see cref="ForwardBumpSnapshot"/>: no <c>kind</c>, so the inbox keeps it as
    /// category "feedback" with a <c>reportType</c> marker for filtering.</summary>
    private void ForwardContentReport(string kind, PlayerSession session, int designId,
        string ownerId, string ownerName, string planet, int x, int y, int z)
    {
        string description = $"Player '{session.State.Name}' reported {kind} #{designId} owned by " +
                             $"'{ownerName}' at {x},{y},{z} on {planet} (world '{_meta.WorldName}'). " +
                             $"Review with /{kind}wipe #{designId} (or by owner name).";
        NotifyOperator($"{kind} report [{_meta.WorldName}]", description, "triangular_flag_on_post");
        PostReportToInbox(
            $"{kind} report [{_meta.WorldName}]: #{designId} by '{ownerName}'",
            description,
            session,
            kind + "-report",
            new { kind, designId, ownerId, ownerName, planet, x, y, z });
    }

    /// <summary>Builds one report row and hands it to the inbox sink. Shared by the paint/shape reports and
    /// the player report (#1222) so both arrive in the same shape — no <c>kind</c> at the top level, so the
    /// inbox files it as "feedback" with a <c>reportType</c> marker for filtering.
    ///
    /// Returns the JSON it built even when no sink is configured (self-hosted servers have none): the row is
    /// what the caller wants to inspect, and building it is the part that can be wrong. Sending is
    /// fire-and-forget on a background thread — a report must never slow the tick or throw into it.</summary>
    private string? PostReportToInbox(string title, string description, PlayerSession reporter,
        string reportType, object report)
    {
        string json;
        try
        {
            var wire = new
            {
                title = TruncateWire(title, 110),
                description,
                email = string.Empty,
                gameVersion = ServerVersionString,
                buildNumber = string.Empty,
                playerId = reporter.State.PlayerId ?? string.Empty,
                playerName = reporter.State.Name ?? string.Empty,
                sessionId = string.Empty,
                platform = "server",
                clientTimestamp = System.DateTime.UtcNow.ToString("o"),
                reportJson = new
                {
                    schemaVersion = 1,
                    reportType,
                    source = "server",
                    world = _meta.WorldName,
                    serverVersion = ServerVersionString,
                    report,
                },
            };

            json = JsonSerializer.Serialize(wire);
        }
        catch
        {
            return null; // forwarding must never break the report handling or the tick
        }

        var sink = CrashUploader;
        if (sink is { IsConfigured: true })
        {
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    sink.Send(json);
                }
                catch
                {
                    // one best-effort attempt; the local row / log line remains the source of truth
                }
            });
        }

        return json;
    }

    /// <summary>Sends the rejection to every session playing under this name and arms the close.</summary>
    private void ApplyKick(string playerName, string reason)
    {
        // A fleet admin is never a kick target: a world owner must not be able to remove oversight of their
        // own world (#495), and the operator's own lever is stopping the instance, not kicking themselves.
        var targets = _sessions.Values
            .Where(s => s.Joined && !s.IsFleetAdmin
                        && string.Equals(s.State.Name, playerName, System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            _log.Info($"Kick requested for '{playerName}' — not online, nothing to do.");
            return;
        }

        foreach (var session in targets)
        {
            _log.Info($"Kicking '{session.State.Name}'{(reason.Length > 0 ? $" ({reason})" : string.Empty)}.");
            SendTo(session.ConnectionId, new JoinRejected { Reason = reason });
            _kickFlush.Add((session.ConnectionId, KickFlushSeconds));
        }
    }
}
