// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2500: the stories a server runs that this client does not have — a pack of a newer build, or one a server added. The
    /// client has none of their texts, so VEGA's panel, the objective chip and the Journal would show raw <c>[key]</c>s. This
    /// notes such packs from what the server names (<see cref="StoryPackStates"/>, <see cref="StoryStateMessage"/>), makes a
    /// one-time notice due ("This world plays a story your game doesn't know yet — update the game to follow it.", once per
    /// world session) and gives their missing keys the neutral "needs a newer version" note instead. A missing key of a story
    /// this client HAS stays visible as <c>[key]</c> — that is a content bug, not an older build. Pure, unit-tested headless;
    /// one instance per world session (GameBootstrap).
    /// </summary>
    public sealed class UnknownStoryPacks
    {
        /// <summary>The one-time notice.</summary>
        public const string NoticeKey = "ui.story.pack_missing";

        /// <summary>The neutral text a missing key of such a story reads as (the Story Log's note for the same case, #2462).</summary>
        public const string FallbackKey = "ui.story.entry_missing";

        private readonly HashSet<string> _missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _noticeDue;
        private bool _noticeGiven;

        /// <summary>The story ids noted as missing so far.</summary>
        public IReadOnlyCollection<string> Missing => _missing;

        /// <summary>Whether this world runs at least one story this client does not have.</summary>
        public bool AnyMissing => _missing.Count > 0;

        /// <summary>Whether the notice waits to be shown (<see cref="TakeNotice"/> takes it).</summary>
        public bool NoticeDue => _noticeDue;

        /// <summary>The story ids the server's story list names (the world story's entry included).</summary>
        public static IEnumerable<string> PackIdsOf(StoryPackStates? states)
            => states?.Packs?.Where(p => p != null).Select(p => p.Id) ?? Enumerable.Empty<string>();

        /// <summary>The world story the story state names — none for the "none" sandbox or an inactive story.</summary>
        public static IEnumerable<string> PackIdsOf(StoryStateMessage? story)
            => story != null && story.Active && !string.IsNullOrEmpty(story.StoryId)
               && !string.Equals(story.StoryId, StoryRegistry.NoneStoryId, StringComparison.OrdinalIgnoreCase)
                ? new[] { story.StoryId }
                : Enumerable.Empty<string>();

        /// <summary>Whether an id can name a story pack at all (not empty, not one of the reserved words the runtime uses for
        /// its own lines).</summary>
        public static bool NamesAPack(string? packId)
            => !string.IsNullOrEmpty(packId) && !StoryRegistry.IsReservedPackId(packId);

        /// <summary>Notes the stories a server message names; <paramref name="clientHas"/> says whether this client has a story.
        /// A story it does not have makes the notice due — once per session: a second unknown story later says nothing more.
        /// Returns whether a new missing story was noted.</summary>
        public bool Observe(IEnumerable<string?>? packIds, Func<string, bool> clientHas)
        {
            if (packIds == null || clientHas == null)
            {
                return false;
            }

            bool added = false;
            foreach (string? id in packIds)
            {
                if (NamesAPack(id) && !clientHas(id!) && _missing.Add(id!))
                {
                    added = true;
                }
            }

            if (added && !_noticeGiven)
            {
                _noticeDue = true;
            }

            return added;
        }

        /// <summary>Takes the due notice: true once (the caller shows <see cref="NoticeKey"/>), false afterwards and while none is
        /// due.</summary>
        public bool TakeNotice()
        {
            if (!_noticeDue)
            {
                return false;
            }

            _noticeDue = false;
            _noticeGiven = true;
            return true;
        }

        /// <summary>Whether a line or objective of <paramref name="packId"/> belongs to a story this client does not have: the
        /// pack is unknown to it (<paramref name="clientHas"/> — a line may arrive before the story list), or noted missing.
        /// A line without a pack id (VEGA's own lines and the world story's beats carry none) counts when any story is missing —
        /// the world story is the one those lines come from.</summary>
        public bool BelongsToMissing(string? packId, Func<string, bool>? clientHas = null)
        {
            if (string.IsNullOrEmpty(packId))
            {
                return AnyMissing;
            }

            return NamesAPack(packId) && (_missing.Contains(packId!) || (clientHas != null && !clientHas(packId!)));
        }

        /// <summary>The text of a story key as the VEGA panel, the chip and the Journal show it: the client's own text when it
        /// has the key; the neutral note (<see cref="FallbackKey"/>) when it does not and the key belongs to a story this client
        /// lacks (<see cref="BelongsToMissing"/>); otherwise <paramref name="localize"/>'s answer as before (a content bug stays
        /// visible).</summary>
        public string Text(string? key, string? packId, Func<string, string> localize, Func<string, bool> has,
            Func<string, bool>? clientHas = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (has(key!) || !BelongsToMissing(packId, clientHas))
            {
                return localize(key!);
            }

            return localize(FallbackKey);
        }
    }
}
