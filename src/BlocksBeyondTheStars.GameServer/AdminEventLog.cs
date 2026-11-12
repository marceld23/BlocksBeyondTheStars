// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin event log (#3506): a ring buffer of the last <see cref="Capacity"/> events of this server (world), oldest
/// first. RAM only — the durable record is the <c>server.log</c> line every event also writes; the reports themselves are
/// persisted in their own table. Each entry carries the two visibility facts the per-viewer filter needs:
/// <see cref="Entry.StaffOnly"/> (an event a world owner on an official hosted world must not see, e.g. the fleet admin's own
/// observer actions) and the target, so a report about an admin is never shown to that admin.
/// </summary>
internal sealed class AdminEventLog
{
    /// <summary>How many events are kept.</summary>
    public const int Capacity = 500;

    /// <summary>One stored event and its visibility.</summary>
    internal readonly record struct Entry(NetAdminEvent Event, bool StaffOnly);

    private readonly Queue<Entry> _entries = new();
    private long _nextId = 1;

    /// <summary>The number of events held (at most <see cref="Capacity"/>).</summary>
    public int Count => _entries.Count;

    /// <summary>Stamps the next id on <paramref name="ev"/>, appends it and drops the oldest past the capacity.</summary>
    public Entry Add(NetAdminEvent ev, bool staffOnly)
    {
        ev.Id = _nextId++;
        var entry = new Entry(ev, staffOnly);
        _entries.Enqueue(entry);
        while (_entries.Count > Capacity)
        {
            _entries.Dequeue();
        }

        return entry;
    }

    /// <summary>Every held event, oldest first.</summary>
    public IEnumerable<Entry> All => _entries;
}
