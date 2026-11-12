// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Admin
{
    /// <summary>
    /// The direct chat between an admin and a player (#3507), Unity-free: one thread per conversation partner for the session,
    /// the reply target of <c>/r</c> and the Reply button, and unread counts. On a player's client a thread is the admin who
    /// wrote; on an admin's client it is the player — also for a conversation between another admin and a player the panel
    /// mirrors (keyed by the player, the side that is not the admin).
    /// </summary>
    public sealed class DirectChatThreads
    {
        /// <summary>The most lines kept per thread.</summary>
        public const int MaxLinesPerThread = 200;

        /// <summary>How long a player may answer an admin after that admin's last line (the server's rule, shown as a hint).</summary>
        public const long ReplyWindowSeconds = 30 * 60;

        private readonly Dictionary<string, DirectChatThread> _threads = new Dictionary<string, DirectChatThread>(StringComparer.Ordinal);

        /// <summary>Counts up with every line added or thread read.</summary>
        public int Version { get; private set; }

        /// <summary>The partner <c>/r</c> answers: the last one who wrote to this player (or admin); empty until a line arrived.</summary>
        public string ReplyTargetId { get; private set; } = string.Empty;

        public string ReplyTargetName { get; private set; } = string.Empty;

        /// <summary>Every thread, the most recent first.</summary>
        public IReadOnlyList<DirectChatThread> Threads => _threads.Values.OrderByDescending(t => t.LastUnix).ToList();

        /// <summary>The thread with this partner, or null.</summary>
        public DirectChatThread? Find(string? partnerId)
            => partnerId != null && _threads.TryGetValue(partnerId, out var t) ? t : null;

        /// <summary>The unread lines over all threads.</summary>
        public int UnreadTotal => _threads.Values.Sum(t => t.Unread);

        /// <summary>Files one line the server sent. <paramref name="myPlayerId"/> is this client's player id: a line from
        /// someone else becomes the new reply target and counts as unread.</summary>
        public DirectChatThread Add(DirectChatMessage line, string myPlayerId)
        {
            bool mine = line.FromId == myPlayerId;
            bool toMe = line.ToId == myPlayerId;
            string partnerId, partnerName;
            if (mine || toMe)
            {
                partnerId = mine ? line.ToId : line.FromId;
                partnerName = mine ? line.ToName : line.FromName;
            }
            else
            {
                // A conversation between another admin and a player: keyed by the player.
                partnerId = line.FromAdmin ? line.ToId : line.FromId;
                partnerName = line.FromAdmin ? line.ToName : line.FromName;
            }

            if (!_threads.TryGetValue(partnerId, out var thread))
            {
                thread = new DirectChatThread(partnerId, partnerName);
                _threads[partnerId] = thread;
            }

            thread.Append(line, MaxLinesPerThread, unread: !mine);
            if (toMe)
            {
                ReplyTargetId = line.FromId;
                ReplyTargetName = line.FromName;
                if (line.FromAdmin)
                {
                    thread.LastAdminLineUnix = line.UnixTime;
                }
            }

            Version++;
            return thread;
        }

        /// <summary>The partner's lines are read (the thread was shown).</summary>
        public void MarkRead(string partnerId)
        {
            if (_threads.TryGetValue(partnerId ?? string.Empty, out var t) && t.Unread > 0)
            {
                t.Unread = 0;
                Version++;
            }
        }

        /// <summary>Whether a player may still answer <paramref name="adminId"/> at <paramref name="nowUnix"/> — a hint for the
        /// Reply button; the server decides.</summary>
        public bool CanAnswer(string adminId, long nowUnix)
            => _threads.TryGetValue(adminId ?? string.Empty, out var t) && t.LastAdminLineUnix > 0
               && nowUnix - t.LastAdminLineUnix <= ReplyWindowSeconds;

        /// <summary>Forgets every thread (a new session).</summary>
        public void Clear()
        {
            _threads.Clear();
            ReplyTargetId = string.Empty;
            ReplyTargetName = string.Empty;
            Version++;
        }
    }

    /// <summary>One conversation of <see cref="DirectChatThreads"/>.</summary>
    public sealed class DirectChatThread
    {
        private readonly List<DirectChatMessage> _lines = new List<DirectChatMessage>();

        public DirectChatThread(string partnerId, string partnerName)
        {
            PartnerId = partnerId;
            PartnerName = partnerName;
        }

        /// <summary>The other side (on an admin's client: the player).</summary>
        public string PartnerId { get; }

        public string PartnerName { get; private set; }

        /// <summary>The lines, oldest first.</summary>
        public IReadOnlyList<DirectChatMessage> Lines => _lines;

        /// <summary>Lines from the partner not shown yet.</summary>
        public int Unread { get; internal set; }

        /// <summary>The time of the newest line, Unix seconds.</summary>
        public long LastUnix { get; private set; }

        /// <summary>The time of the newest line an admin wrote to this client (a player's reply window), Unix seconds.</summary>
        public long LastAdminLineUnix { get; internal set; }

        internal void Append(DirectChatMessage line, int max, bool unread)
        {
            _lines.Add(line);
            if (_lines.Count > max)
            {
                _lines.RemoveAt(0);
            }

            LastUnix = Math.Max(LastUnix, line.UnixTime);
            if (unread)
            {
                Unread++;
            }

            string name = line.FromId == PartnerId ? line.FromName : line.ToId == PartnerId ? line.ToName : string.Empty;
            if (!string.IsNullOrEmpty(name))
            {
                PartnerName = name;
            }
        }
    }
}
