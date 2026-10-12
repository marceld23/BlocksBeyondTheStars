// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>One cutscene waiting on the client (#2468): what the server sent, plus what the client knows about it.</summary>
    public sealed class QueuedCutscene
    {
        public QueuedCutscene(string packId, string id, int tier)
        {
            PackId = packId ?? string.Empty;
            Id = id ?? string.Empty;
            Tier = tier;
        }

        /// <summary>The pack that defines the scene.</summary>
        public string PackId { get; }

        /// <summary>The (instance) id as received.</summary>
        public string Id { get; }

        /// <summary>The definition id (the part before <c>@</c>).</summary>
        public string DefinitionId => StoryCutsceneIds.DefinitionOf(Id);

        /// <summary>The scene's tier (a banner never waits for the cooldown).</summary>
        public int Tier { get; }

        /// <summary>The play's argument.</summary>
        public string Arg { get; set; } = string.Empty;

        /// <summary>The <c>custom</c> anchor carried by the message, if any.</summary>
        public bool HasAnchor { get; set; }

        public float AnchorX { get; set; }
        public float AnchorY { get; set; }
        public float AnchorZ { get; set; }

        /// <summary>A replay the player asked for (Story tab, admin preview): no cooldown, no confirmation.</summary>
        public bool Replay { get; set; }

        /// <summary>Played by this client on its own (the dev palette, the clip recorder): no cooldown, no confirmation.</summary>
        public bool Local { get; set; }

        /// <summary>A "now or never" moment (<c>cooldownExempt</c>).</summary>
        public bool CooldownExempt { get; set; }

        /// <summary>The scenes this one may chain to (<c>chainNext</c>, as written in the pack).</summary>
        public IReadOnlyList<string> ChainNext { get; set; } = Array.Empty<string>();

        /// <summary>The scene is bound to a chapter of its pack (an opening, a beat card) and is not the pack's intro — the
        /// built-in recap leads straight into such a scene (#2470).</summary>
        public bool ChapterScene { get; set; }

        /// <summary>When the message arrived (seconds, the director's clock) — the gate's give-up timer.</summary>
        public float ReceivedAt { get; set; }

        /// <summary>Whatever the director keeps with the scene (its definition, the narrator lines it took over).</summary>
        public object? Payload { get; set; }

        /// <summary>Asked for by the player (or a developer) rather than sent by the story.</summary>
        public bool Requested => Replay || Local;

        /// <summary>The queue's identity of the scene: <c>pack/id</c>.</summary>
        public string Key => PackId + StoryCutsceneIds.PackSeparator + Id;

        /// <summary>A <c>chainNext</c> entry in its qualified form: <c>&lt;pack&gt;/&lt;id&gt;</c>, or <c>__recap</c> (any pack's
        /// recap); a bare id means the same pack.</summary>
        public static string Qualify(string packId, string next)
            => next == StoryCutsceneIds.Recap || next.IndexOf(StoryCutsceneIds.PackSeparator) >= 0
                ? next
                : packId + StoryCutsceneIds.PackSeparator + next;
    }

    /// <summary>
    /// The client's cutscene queue (#2468). The server already spaces what it sends (90 s per unrequested card, chains and
    /// "now or never" at once); the client must keep that spacing although it plays scenes later than they arrive — a
    /// scene waiting minutes for a safe moment must not be followed by the next one a second after it ends.
    /// <list type="bullet">
    /// <item>One scene plays at a time; nothing — not even a banner — starts while one plays.</item>
    /// <item>A scene already queued or playing is not queued again (a rejoin re-offer, a double replay request).</item>
    /// <item>Requested scenes (replays, previews, the dev palette) go to the front and never wait for the cooldown.</item>
    /// <item>After an unrequested card or scene ends, the next one waits <see cref="CooldownSeconds"/> — unless the
    /// finished one chains to it (<c>chainNext</c>; the built-in recap chains to its pack's next scene, #2470), it is "now
    /// or never", or it is a banner.</item>
    /// </list>
    /// </summary>
    public sealed class CutsceneQueue
    {
        /// <summary>The spacing between two unrequested cards or scenes (synthesis §6.2).</summary>
        public const float CooldownSeconds = 90f;

        private readonly List<QueuedCutscene> _items = new List<QueuedCutscene>();
        private readonly HashSet<string> _chain = new HashSet<string>(StringComparer.Ordinal);
        private bool _anyEnded;
        private float _lastEndedAt;

        /// <summary>The waiting scenes, in order.</summary>
        public IReadOnlyList<QueuedCutscene> Items => _items;

        public int Count => _items.Count;

        /// <summary>The scene playing now, or null.</summary>
        public QueuedCutscene? Playing { get; private set; }

        /// <summary>Queues <paramref name="item"/>. Returns false (and queues nothing) when the same scene is already queued
        /// or playing.</summary>
        public bool Enqueue(QueuedCutscene item)
        {
            if (item is null || Contains(item.PackId, item.Id))
            {
                return false;
            }

            if (item.Requested)
            {
                int at = 0;
                while (at < _items.Count && _items[at].Requested)
                {
                    at++; // behind earlier requests, ahead of everything the story sent
                }

                _items.Insert(at, item);
            }
            else
            {
                _items.Add(item);
            }

            return true;
        }

        /// <summary>Whether the scene is queued or playing.</summary>
        public bool Contains(string packId, string id)
        {
            if (Playing != null && Playing.PackId == packId && Playing.Id == id)
            {
                return true;
            }

            foreach (var q in _items)
            {
                if (q.PackId == packId && q.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the cooldown after the last unrequested card or scene has run out at <paramref name="now"/>.</summary>
        public bool CooldownOver(float now) => !_anyEnded || now - _lastEndedAt >= CooldownSeconds;

        /// <summary>Whether the scene that ended last chains to <paramref name="item"/>.</summary>
        public bool IsChained(QueuedCutscene item)
            => item != null && (_chain.Contains(item.PackId + StoryCutsceneIds.PackSeparator + item.DefinitionId)
                                || (item.DefinitionId == StoryCutsceneIds.Recap && _chain.Contains(StoryCutsceneIds.Recap))
                                || (item.ChapterScene && _chain.Contains(StoryCutsceneIds.AnyOfPack(item.PackId))));

        /// <summary>Whether <paramref name="item"/> may start now as far as the queue is concerned (the safe-moment gate
        /// still decides).</summary>
        public bool MayGoNow(QueuedCutscene item, float now)
            => item.Tier <= CutsceneTiers.Banner || item.Requested || item.CooldownExempt || IsChained(item) || CooldownOver(now);

        /// <summary>The scenes that may start now, in queue order — none while a scene plays. A card held back by the
        /// cooldown does not block a banner behind it.</summary>
        public IReadOnlyList<QueuedCutscene> Candidates(float now)
        {
            var list = new List<QueuedCutscene>();
            if (Playing != null)
            {
                return list;
            }

            foreach (var q in _items)
            {
                if (MayGoNow(q, now))
                {
                    list.Add(q);
                }
            }

            return list;
        }

        /// <summary>Takes <paramref name="item"/> out of the queue as the playing scene. An unrequested card or scene uses up
        /// the chain of the one before it.</summary>
        public void Start(QueuedCutscene item)
        {
            if (item is null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            _items.Remove(item);
            Playing = item;
            if (item.Tier >= CutsceneTiers.Card && !item.Requested)
            {
                _chain.Clear();
            }
        }

        /// <summary>The playing scene ended at <paramref name="now"/> (watched, skipped, interrupted). An unrequested card or
        /// scene starts the cooldown and opens its <c>chainNext</c>.</summary>
        public void Finish(float now)
        {
            var item = Playing;
            Playing = null;
            if (item is null || item.Tier < CutsceneTiers.Card || item.Requested)
            {
                return; // banners and requests leave the story's pacing alone
            }

            _anyEnded = true;
            _lastEndedAt = now;
            _chain.Clear();
            if (item.DefinitionId == StoryCutsceneIds.Recap)
            {
                _chain.Add(StoryCutsceneIds.AnyOfPack(item.PackId)); // #2470: "the story so far" leads into the pack's next scene
            }

            foreach (string next in item.ChainNext ?? Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(next))
                {
                    _chain.Add(QueuedCutscene.Qualify(item.PackId, next));
                }
            }
        }

        /// <summary>Drops a waiting scene (aborted, unsupported). Returns whether it was queued.</summary>
        public bool Remove(QueuedCutscene item) => _items.Remove(item);

        /// <summary>Forgets everything (the world is gone).</summary>
        public void Clear()
        {
            _items.Clear();
            _chain.Clear();
            Playing = null;
            _anyEnded = false;
        }
    }
}
