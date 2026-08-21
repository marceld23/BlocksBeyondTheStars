// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC story threads (#1112): the people of the world carry pieces of the story. A story pack declares
/// <see cref="NpcThread"/>s — (role, relationship stage, knowledge level, spoken line, optional fragment) —
/// and greeting a matching NPC fires the thread once per player: the NPC speaks the line, and a carried
/// fragment is recorded + revealed like any other find. Deterministic and offline-safe (<c>AiLevel.Off</c>);
/// the relationship gate rides the existing NPC memory (<c>GameServerNpcMemory</c>).
/// </summary>
public sealed partial class GameServer
{
    private const string NpcThreadMilestonePrefix = "npcthread:";

    /// <summary>Ordinal rank of a relationship stage requirement ("" &lt; known &lt; trusted).</summary>
    private static int StageRank(string stage) => stage switch
    {
        "trusted" => 2,
        "known" => 1,
        _ => 0,
    };

    /// <summary>Fires the first eligible story thread of this NPC for this player, returning the localized
    /// spoken line — or empty when nothing (new) applies. Game-thread only.</summary>
    private string TryEmitNpcThread(PlayerSession session, ServerNpc npc, string npcKey)
    {
        if (PeekNpcThread(session, npc.Role, npcKey) is not { } thread)
        {
            return string.Empty;
        }

        CommitNpcThread(session, thread);
        return Localize(session.Locale, thread.TextKey);
    }

    /// <summary>The first eligible, untold story thread of an NPC (by role + memory key) for this player —
    /// WITHOUT side effects. The radio scan (#1158) peeks first so a call blocked by a radio gate never
    /// burns the once-per-player milestone; a taker commits via <see cref="CommitNpcThread"/>.</summary>
    private NpcThread? PeekNpcThread(PlayerSession session, string role, string npcKey)
    {
        if (!StoryActive || _story is null || _story.NpcThreads.Count == 0)
        {
            return null;
        }

        var p = session.State;
        int knowledge = WorldKnowledgeLevel();
        int rel = p.NpcMemory.TryGetValue(npcKey, out var r) ? r.Value : 0;
        int stage = StageRank(RelationshipTier(rel));

        foreach (var t in _story.NpcThreads)
        {
            if (string.IsNullOrEmpty(t.Key)
                || (!string.IsNullOrEmpty(t.Role) && !string.Equals(t.Role, role, System.StringComparison.OrdinalIgnoreCase))
                || t.MinKnowledge > knowledge
                || StageRank(t.MinStage) > stage
                || p.Milestones.Contains(NpcThreadMilestonePrefix + t.Key))
            {
                continue;
            }

            return t;
        }

        return null;
    }

    /// <summary>Marks a peeked thread told (once per player) and hands over a carried fragment: the reader
    /// opens with the archive text and the shared arc advances (deduped — a fragment already found elsewhere
    /// only re-reads).</summary>
    private void CommitNpcThread(PlayerSession session, NpcThread thread)
    {
        var p = session.State;
        p.Milestones.Add(NpcThreadMilestonePrefix + thread.Key);
        _repo.SavePlayer(p);

        if (!string.IsNullOrEmpty(thread.FragmentKey)
            && _story is not null
            && _story.Fragments.FirstOrDefault(f => f.Key == thread.FragmentKey) is { } frag)
        {
            Send(session, new NetFragmentRevealed { Category = frag.Category, TextKey = frag.TextKey });
            RecordStoryFragment(frag.Key);
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: the relationship-memory key of the nearest in-reach NPC of a role (so a test can
    /// seed a standing exactly where the thread gate will look). Null when no NPC is in reach.</summary>
    public string? NpcKeyForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session
            || NearestNpc(session.State, role) is not { } npc
            || WrapDistSq(session.State.Position, npc.Pos) > NpcGreetRange * NpcGreetRange)
        {
            return null;
        }

        var (_, npcKey, _) = NpcContext(session, npc);
        return npcKey;
    }

    /// <summary>Test seam: run the thread flow for the nearest in-reach NPC of <paramref name="role"/> exactly
    /// like a greeting would. Null when no NPC is in reach; empty when no thread fires; else the spoken line.</summary>
    public string? NpcThreadLineForTest(string playerId, string role)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        if (NearestNpc(session.State, role) is not { } npc
            || WrapDistSq(session.State.Position, npc.Pos) > NpcGreetRange * NpcGreetRange)
        {
            return null;
        }

        var (_, npcKey, _) = NpcContext(session, npc);
        return TryEmitNpcThread(session, npc, npcKey);
    }
}
