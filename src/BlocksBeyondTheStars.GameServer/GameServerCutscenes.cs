// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>One cutscene committed to a player and waiting to be sent (#2467): the client is not in the world yet, or the
/// 90 s cooldown still runs.</summary>
public sealed class PendingCutscene
{
    public PendingCutscene(string packId, string id, int tier, string arg, Vector3f? anchor, bool cooldownExempt)
    {
        PackId = packId;
        Id = id;
        Tier = tier;
        Arg = arg ?? string.Empty;
        Anchor = anchor;
        CooldownExempt = cooldownExempt;
    }

    /// <summary>The pack that defines the scene.</summary>
    public string PackId { get; }

    /// <summary>The (instance) id as it goes on the wire.</summary>
    public string Id { get; }

    /// <summary>The definition id (the part before <c>@</c>).</summary>
    public string DefinitionId => StoryCutsceneIds.DefinitionOf(Id);

    /// <summary>The scene's tier (a banner never waits for the cooldown).</summary>
    public int Tier { get; }

    /// <summary>The caption argument.</summary>
    public string Arg { get; }

    /// <summary>The <c>custom</c> anchor position, if any.</summary>
    public Vector3f? Anchor { get; }

    /// <summary>A "now or never" scene — it never waits for the cooldown.</summary>
    public bool CooldownExempt { get; }
}

/// <summary>
/// The cutscene trigger and the per-player seen ledger (#2467, synthesis §6.3, d4 §3.4–§3.8). The server never plays a
/// scene: it sends an id (<see cref="CutscenePlay"/>, tag 295) to a client that announced <c>cutscenes1</c> and records
/// what it owes and what the client confirmed:
/// <list type="bullet">
/// <item><c>story:&lt;pack&gt;:cut:&lt;id&gt;:sent</c> when the scene is committed (sent, or queued behind the cooldown or
/// until the client is in the world); <c>story:&lt;pack&gt;:cut:&lt;id&gt;</c> when the client confirms it
/// (<see cref="CutsceneSeenIntent"/>, tag 296: watched, skipped, downgraded or unsupported). A scene plays once per
/// player — a template once per binding (<c>id@owner</c>).</item>
/// <item>A client without <c>cutscenes1</c> (an older build, a local player) gets nothing extra — the trigger's narrator
/// line carries the moment — and the scene is stamped seen at once, so it is never owed.</item>
/// <item>At the join every committed but unconfirmed scene is offered again (a world story's backlog of three or more
/// chapter scenes collapses into ONE built-in <c>__recap</c> card).</item>
/// <item>At most one unrequested T2+ scene per <see cref="CutsceneCooldownSeconds"/>, except a scene the previous one
/// chains to (<c>chainNext</c>) or a "now or never" one; banners never wait. Nothing goes out before the client is in the
/// world. Observers (<c>/spectate</c>) never get a scene.</item>
/// </list>
/// Replays (Story tab, <see cref="StoryPackActions.ReplayCutscene"/>) re-send a seen scene with <c>Replay = true</c> and
/// change nothing.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>At most one unrequested T2+ cutscene per this many seconds of server time (synthesis §6.2).</summary>
    internal const double CutsceneCooldownSeconds = 90.0;

    /// <summary>After a network join, cutscenes wait for the client's first position report — or this long at most.</summary>
    internal const double CutsceneJoinSettleSeconds = 15.0;

    /// <summary>A world story's join backlog of this many chapter scenes or more collapses into one recap card.</summary>
    internal const int CutsceneRecapThreshold = 3;

    /// <summary>Most ids one receipt may confirm (client input).</summary>
    private const int MaxSeenIdsPerReceipt = 32;

    /// <summary>Longest (instance) id a receipt may name.</summary>
    private const int MaxCutsceneIdLength = 96;

    /// <summary>Unknown cutscene references already reported (a bad binding would otherwise log once per player).</summary>
    private readonly HashSet<string> _unknownCutscenesLogged = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------------------------------------
    // The trigger API.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Plays a pack's cutscene for one player. <paramref name="cutsceneId"/> is a definition id, an instance id
    /// (<c>id@owner</c>) or <see cref="StoryCutsceneIds.Recap"/>; <paramref name="arg"/> fills the captions' <c>{0}</c>;
    /// <paramref name="anchor"/> is the <c>custom</c> anchor. A first play commits the scene to the ledger and sends or
    /// queues it (cooldown, client not in the world yet); a scene the player was already shown or is still owed is not
    /// played again. <paramref name="replay"/> re-sends at once without touching the ledger. Returns true when the scene
    /// was sent or queued for the player's client.
    /// </summary>
    internal bool PlayCutscene(PlayerSession session, string packId, string cutsceneId, string arg = "", Vector3f? anchor = null, bool replay = false)
    {
        if (session is null || !session.Joined || session.Spectating || string.IsNullOrEmpty(packId) || string.IsNullOrEmpty(cutsceneId)
            || !_content.Stories.TryGetValue(packId, out var pack))
        {
            return false; // an observer never gets a scene (#2460 N1)
        }

        bool recap = cutsceneId == StoryCutsceneIds.Recap;
        var def = recap ? null : pack.FindCutscene(cutsceneId);
        if (!recap && (def is null || IsInstance(cutsceneId) != def.Template))
        {
            if (_unknownCutscenesLogged.Add(pack.Id + "/" + cutsceneId))
            {
                _log.Warn($"Cutscene '{pack.Id}/{cutsceneId}' is not in the pack's cutscenes.json (a template plays only as <id>@<owner>) — not played.");
            }

            return false;
        }

        if (replay)
        {
            if (recap || !session.SupportsCutscenes)
            {
                return false;
            }

            SendCutscenePlay(session, pack.Id, cutsceneId, arg, anchor, replay: true);
            return true;
        }

        if (recap)
        {
            if (!session.SupportsCutscenes)
            {
                return false; // the recap has no line form — an older client simply has its story log
            }

            EnqueueCutscene(session, new PendingCutscene(pack.Id, cutsceneId, CutsceneTiers.Card, arg, null, cooldownExempt: false));
            FlushCutscenes(session);
            return true;
        }

        var m = session.State.Milestones;
        if (m.Contains(StoryPackKeys.CutsceneSeen(pack.Id, cutsceneId)) || m.Contains(StoryPackKeys.CutsceneSent(pack.Id, cutsceneId)))
        {
            return false; // once per player; an owed scene is re-offered at the join
        }

        if (!session.SupportsCutscenes)
        {
            m.Add(StoryPackKeys.CutsceneSeen(pack.Id, cutsceneId)); // the narrator line carried it
            _repo.SavePlayer(session.State);
            return false;
        }

        m.Add(StoryPackKeys.CutsceneSent(pack.Id, cutsceneId));
        _repo.SavePlayer(session.State);
        EnqueueCutscene(session, new PendingCutscene(pack.Id, cutsceneId, def!.Tier, arg, anchor, def.Options?.CooldownExempt ?? false));
        FlushCutscenes(session);
        return true;
    }

    /// <summary>Plays a scene for every story listener (joined, not observing) — for world-story moments (beats, finale;
    /// wired in steps 11/15/17b). Each player's copy is committed, queued and confirmed on its own.</summary>
    internal void PlayCutsceneForListeners(string packId, string cutsceneId, string arg = "", Vector3f? anchor = null)
    {
        foreach (var session in StoryListeners().ToList())
        {
            PlayCutscene(session, packId, cutsceneId, arg, anchor);
        }
    }

    /// <summary>Sends the built-in recap card ("The story so far", d4 §3.6) for a pack: the argument lists the reached
    /// chapters' title keys in chapter order, joined by <see cref="LineText.ArgSeparator"/> — titles only, no beat text. Not
    /// in the ledger; waits for the cooldown like any card.</summary>
    internal bool PlayRecap(PlayerSession session, string packId, IEnumerable<string> chapterTitleKeys)
        => PlayCutscene(session, packId, StoryCutsceneIds.Recap, string.Join(LineText.ArgSeparator.ToString(), chapterTitleKeys));

    /// <summary>Plays a pack binding (<c>introCutscene</c>, a chapter's open/close, a step hook): a template gets its
    /// instance id for <paramref name="owner"/> (the chapter, step or pack id), and the bound chapter's title key is the
    /// argument.</summary>
    private bool PlayBoundCutscene(PlayerSession session, StoryDefinition pack, string? cutsceneId, string owner)
    {
        if (string.IsNullOrEmpty(cutsceneId))
        {
            return false;
        }

        if (pack.FindCutscene(cutsceneId) is not { } def || IsInstance(cutsceneId!))
        {
            return PlayCutscene(session, pack.Id, cutsceneId!); // logs the unknown id once
        }

        string id = def.Template ? StoryCutsceneIds.Instance(def.Id, owner) : def.Id;
        return PlayCutscene(session, pack.Id, id, CutsceneArgOf(pack, def, id));
    }

    private static bool IsInstance(string id) => id.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0;

    /// <summary>The argument a binding plays with: the title key of the chapter the scene is bound to (an instance's owner
    /// chapter, the chapter of an owner step) or of the scene's own <c>chapter</c>; else empty.</summary>
    private static string CutsceneArgOf(StoryDefinition pack, CutsceneDefinition def, string id)
        => ChapterOfCutscene(pack, def, id)?.TitleKey ?? string.Empty;

    private static StoryChapter? ChapterOfCutscene(StoryDefinition pack, CutsceneDefinition def, string id)
    {
        string owner = StoryCutsceneIds.OwnerOf(id);
        if (owner.Length > 0)
        {
            foreach (var chapter in pack.Chapters)
            {
                if (chapter.Id == owner || chapter.Steps.Any(st => st.Id == owner))
                {
                    return chapter;
                }
            }
        }

        return string.IsNullOrEmpty(def.Chapter) ? null : pack.Chapters.FirstOrDefault(c => c.Id == def.Chapter);
    }

    // ---------------------------------------------------------------------------------------------
    // The queue: client in the world, the cooldown, chains.
    // ---------------------------------------------------------------------------------------------

    private static void EnqueueCutscene(PlayerSession session, PendingCutscene item)
    {
        if (!session.CutsceneQueue.Any(q => q.PackId == item.PackId && q.Id == item.Id))
        {
            session.CutsceneQueue.Add(item);
        }
    }

    /// <summary>Whether the client is in the world: it reported a position since the session began, or the join settle time
    /// passed (a session not made by a network join is always ready).</summary>
    private bool CutsceneClientReady(PlayerSession session)
        => session.LastMoveAt > 0 || _uptime - session.CutsceneJoinAt >= CutsceneJoinSettleSeconds;

    /// <summary>Sends every queued scene that may go now, in order: banners and "now or never" scenes at once, a scene the
    /// last one chains to at once, anything else once the cooldown has run out (one per cooldown).</summary>
    private void FlushCutscenes(PlayerSession session)
    {
        if (session.CutsceneQueue.Count == 0 || !session.Joined || session.Spectating || !CutsceneClientReady(session))
        {
            return;
        }

        bool sent;
        do
        {
            sent = false;
            for (int i = 0; i < session.CutsceneQueue.Count; i++)
            {
                var item = session.CutsceneQueue[i];
                if (!CutsceneMayGoNow(session, item))
                {
                    continue;
                }

                session.CutsceneQueue.RemoveAt(i);
                SendQueuedCutscene(session, item);
                sent = true;
                break; // the send moved the cooldown and the chain — look again from the front
            }
        }
        while (sent && session.CutsceneQueue.Count > 0);
    }

    private bool CutsceneMayGoNow(PlayerSession session, PendingCutscene item)
        => item.Tier <= CutsceneTiers.Banner
           || item.CooldownExempt
           || session.CutsceneChain.Contains(item.PackId + StoryCutsceneIds.PackSeparator + item.DefinitionId)
           || (item.DefinitionId == StoryCutsceneIds.Recap && session.CutsceneChain.Contains(StoryCutsceneIds.Recap))
           || _uptime - session.CutsceneLastSentAt >= CutsceneCooldownSeconds;

    private void SendQueuedCutscene(PlayerSession session, PendingCutscene item)
    {
        SendCutscenePlay(session, item.PackId, item.Id, item.Arg, item.Anchor, replay: false);
        if (item.Tier <= CutsceneTiers.Banner)
        {
            return; // a banner never blocks anything
        }

        session.CutsceneLastSentAt = _uptime;
        session.CutsceneChain.Clear();
        var def = item.DefinitionId == StoryCutsceneIds.Recap ? null : FindPackCutscene(item.PackId, item.Id);
        foreach (string next in def?.Options?.ChainNext ?? new List<string>())
        {
            if (string.IsNullOrEmpty(next))
            {
                continue;
            }

            // "__recap" chains to the recap of any pack; "<id>" means this pack; "<pack>/<id>" another one.
            session.CutsceneChain.Add(next == StoryCutsceneIds.Recap || next.IndexOf(StoryCutsceneIds.PackSeparator) >= 0
                ? next
                : item.PackId + StoryCutsceneIds.PackSeparator + next);
        }
    }

    private void SendCutscenePlay(PlayerSession session, string packId, string cutsceneId, string? arg, Vector3f? anchor, bool replay)
        => Send(session, new CutscenePlay
        {
            PackId = packId,
            CutsceneId = cutsceneId,
            Arg = arg ?? string.Empty,
            HasAnchor = anchor.HasValue,
            AnchorX = anchor?.X ?? 0f,
            AnchorY = anchor?.Y ?? 0f,
            AnchorZ = anchor?.Z ?? 0f,
            Replay = replay,
        });

    private CutsceneDefinition? FindPackCutscene(string packId, string id)
        => _content.Stories.TryGetValue(packId, out var pack) ? pack.FindCutscene(id) : null;

    /// <summary>1 Hz per player (from <c>TickShipAi</c>; observers are skipped there): sends what the queue may send now.</summary>
    private void TickCutscenesFor(PlayerSession session) => FlushCutscenes(session);

    // ---------------------------------------------------------------------------------------------
    // The receipt.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The client is through with these scenes (tag 296): each id the pack defines (in the form the server sends
    /// it — a template only as an instance) is stamped seen; unknown ids are ignored; <c>__recap</c> is not in the ledger.
    /// <c>Skipped</c> and <c>Unsupported</c> stamp seen too — a scene the client cannot play must not be offered forever.</summary>
    private void HandleCutsceneSeen(PlayerSession session, CutsceneSeenIntent intent)
    {
        if (intent.Ids is null || !_content.Stories.TryGetValue(intent.PackId ?? string.Empty, out var pack))
        {
            return;
        }

        var m = session.State.Milestones;
        bool changed = false;
        foreach (string id in intent.Ids.Take(MaxSeenIdsPerReceipt))
        {
            if (!IsConfirmableCutsceneId(pack, id))
            {
                continue;
            }

            changed |= m.Add(StoryPackKeys.CutsceneSeen(pack.Id, id));
            session.CutsceneQueue.RemoveAll(q => q.PackId == pack.Id && q.Id == id);
        }

        if (changed)
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>An id a receipt may stamp: a cutscene of the pack — a template only with a key-safe owner, any other scene
    /// only without one.</summary>
    private static bool IsConfirmableCutsceneId(StoryDefinition pack, string? id)
    {
        if (string.IsNullOrEmpty(id) || id!.Length > MaxCutsceneIdLength || StoryCutsceneIds.IsBuiltIn(id)
            || pack.FindCutscene(id) is not { } def)
        {
            return false;
        }

        string owner = StoryCutsceneIds.OwnerOf(id);
        return def.Template ? StoryPackValidator.IsKeySafeId(owner, allowDot: true) : !IsInstance(id);
    }

    // ---------------------------------------------------------------------------------------------
    // The join catch-up and the replay request.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The join hook (before anything of the join can commit a new scene): every scene committed in an earlier
    /// session but never confirmed is queued again — it goes out once the client is in the world, spaced by the cooldown.
    /// A world story's backlog of <see cref="CutsceneRecapThreshold"/> or more chapter scenes becomes ONE recap card with
    /// those chapters' titles (the scenes are stamped seen; the Story tab replays them). A client without
    /// <c>cutscenes1</c> gets them stamped seen; a scene its pack no longer defines is dropped (stamped seen); a pack that is
    /// not installed here, or not enabled for the player (opted out, the world story switched off), keeps its promise.</summary>
    private void CutscenesOnJoin(PlayerSession session)
    {
        var m = session.State.Milestones;
        var owed = new List<(string Pack, string Id)>();
        foreach (string key in m)
        {
            if (StoryPackKeys.TryParseCutsceneSent(key, out string packId, out string id) && !m.Contains(StoryPackKeys.CutsceneSeen(packId, id)))
            {
                owed.Add((packId, id));
            }
        }

        if (owed.Count == 0)
        {
            return;
        }

        bool changed = false;
        foreach (var group in owed.GroupBy(o => o.Pack, StringComparer.Ordinal))
        {
            if (!_content.Stories.TryGetValue(group.Key, out var pack) || !StoryPackEnabledFor(session, pack.Id))
            {
                continue; // not here, or switched off for this player / this world: the promise waits for it
            }

            var scenes = new List<(string Id, CutsceneDefinition Def)>();
            foreach (var (_, id) in group)
            {
                if (pack.FindCutscene(id) is { } def && IsInstance(id) == def.Template)
                {
                    scenes.Add((id, def));
                }
                else
                {
                    changed |= m.Add(StoryPackKeys.CutsceneSeen(group.Key, id)); // removed by an update: nothing left to show
                }
            }

            if (!session.SupportsCutscenes)
            {
                foreach (var (id, _) in scenes)
                {
                    changed |= m.Add(StoryPackKeys.CutsceneSeen(group.Key, id));
                }

                continue;
            }

            // The pack's own order: definition order, then the bound chapter's place, then the id.
            scenes = scenes
                .OrderBy(x => pack.Cutscenes.IndexOf(x.Def))
                .ThenBy(x => ChapterOfCutscene(pack, x.Def, x.Id) is { } ch ? pack.Chapters.IndexOf(ch) : -1)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();

            if (string.Equals(StoryRegistry.EffectiveScope(pack), StoryRegistry.ScopeWorld, StringComparison.Ordinal))
            {
                var chapterScenes = scenes
                    .Where(x => x.Def.Tier >= CutsceneTiers.Card && ChapterOfCutscene(pack, x.Def, x.Id) is not null)
                    .ToList();
                if (chapterScenes.Count >= CutsceneRecapThreshold)
                {
                    var titles = pack.Chapters
                        .Where(ch => chapterScenes.Any(x => ChapterOfCutscene(pack, x.Def, x.Id) == ch) && !string.IsNullOrEmpty(ch.TitleKey))
                        .Select(ch => ch.TitleKey)
                        .ToList();
                    foreach (var (id, _) in chapterScenes)
                    {
                        m.Add(StoryPackKeys.CutsceneSeen(pack.Id, id));
                    }

                    changed = true;
                    EnqueueCutscene(session, new PendingCutscene(pack.Id, StoryCutsceneIds.Recap, CutsceneTiers.Card,
                        string.Join(LineText.ArgSeparator.ToString(), titles), null, cooldownExempt: false));
                    scenes = scenes.Except(chapterScenes).ToList();
                }
            }

            foreach (var (id, def) in scenes)
            {
                EnqueueCutscene(session, new PendingCutscene(pack.Id, id, def.Tier, CutsceneArgOf(pack, def, id), null,
                    def.Options?.CooldownExempt ?? false));
            }
        }

        if (changed)
        {
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>"Watch again" from the Story tab (<see cref="StoryPackActions.ReplayCutscene"/>): re-sends a replayable scene
    /// the player has been shown (spoiler guard: its seen or sent key) with <c>Replay = true</c> — any installed pack, the
    /// world story included. Changes nothing; a client without <c>cutscenes1</c> gets nothing.</summary>
    private bool ReplayCutsceneOnRequest(PlayerSession session, string? packId, string? cutsceneId)
    {
        if (!session.SupportsCutscenes || string.IsNullOrEmpty(cutsceneId)
            || !_content.Stories.TryGetValue(packId ?? string.Empty, out var pack)
            || pack.FindCutscene(cutsceneId) is not { } def || !(def.Options?.Replayable ?? true))
        {
            return false;
        }

        var m = session.State.Milestones;
        if (!m.Contains(StoryPackKeys.CutsceneSeen(pack.Id, cutsceneId!)) && !m.Contains(StoryPackKeys.CutsceneSent(pack.Id, cutsceneId!)))
        {
            return false;
        }

        return PlayCutscene(session, pack.Id, cutsceneId!, CutsceneArgOf(pack, def, cutsceneId!), replay: true);
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: <see cref="PlayCutscene"/>.</summary>
    internal bool PlayCutsceneForTest(PlayerSession session, string packId, string cutsceneId, string arg = "", Vector3f? anchor = null, bool replay = false)
        => PlayCutscene(session, packId, cutsceneId, arg, anchor, replay);

    /// <summary>Test seam: <see cref="PlayCutsceneForListeners"/>.</summary>
    internal void PlayCutsceneForListenersForTest(string packId, string cutsceneId) => PlayCutsceneForListeners(packId, cutsceneId);

    /// <summary>Test seam: <see cref="PlayRecap"/>.</summary>
    internal bool PlayRecapForTest(PlayerSession session, string packId, IEnumerable<string> chapterTitleKeys)
        => PlayRecap(session, packId, chapterTitleKeys);

    /// <summary>Test seam: the 1 Hz cutscene tick for one player.</summary>
    internal void TickCutscenesForTest(PlayerSession session) => TickCutscenesFor(session);

    /// <summary>Test seam: the join catch-up for one player.</summary>
    internal void CutscenesOnJoinForTest(PlayerSession session) => CutscenesOnJoin(session);
}
