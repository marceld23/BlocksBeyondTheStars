// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Operator actions that must cover the whole report pair (#1380). One in-game F1 report is two rows
/// (client-direct + the server's /bump forward) that the admin list shows as ONE report; the rows stay
/// separate records, so a status change or delete addressed to one id used to leave the other half behind —
/// still <c>new</c> under the status filter, or surviving a delete as a lone row. Every route that mutates a
/// report goes through here: the pair is resolved the way the list pairs (<see cref="ReportHostPages.PairOf"/>
/// over <see cref="ReportStore.Around"/>) and the action applied to each member.
/// </summary>
public static class ReportPairActions
{
    /// <summary>The rows an action on <paramref name="r"/> covers — <paramref name="r"/> first.</summary>
    public static List<BugReportRecord> PairOf(ReportStore store, BugReportRecord r)
        => ReportHostPages.PairOf(r, store.Around(r.CreatedUnix, ReportHostPages.DuplicateWindowSeconds));

    /// <summary>Sets <paramref name="status"/> on the report <paramref name="id"/> and its paired half. Returns
    /// the ids changed (the addressed one first), or null when <paramref name="id"/> is unknown or the status
    /// invalid.</summary>
    public static List<string>? SetStatus(ReportStore store, string id, string status)
    {
        if (!BugReportStatus.IsValid(status) || store.Get(id) is not { } record)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var member in PairOf(store, record))
        {
            if (store.SetStatus(member.Id, status))
            {
                ids.Add(member.Id);
            }
        }

        return ids;
    }

    /// <summary>Deletes the report <paramref name="id"/> and its paired half — rows, screenshot files and reply
    /// threads. Returns the ids removed (the addressed one first), or null when <paramref name="id"/> is unknown.</summary>
    public static List<string>? Delete(ReportStore store, string id)
    {
        if (store.Get(id) is not { } record)
        {
            return null;
        }

        var ids = new List<string>();
        foreach (var member in PairOf(store, record))
        {
            if (store.Delete(member.Id))
            {
                ids.Add(member.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Copies the status of <paramref name="ownerId"/> onto its paired half. Called after a reply moved the
    /// thread owner's status on its own — a developer question (<c>waiting_for_player</c>) or the player's
    /// answer (<c>player_replied</c>) is recorded on the keyed half only (<see cref="ReportHostPages.ThreadOwner"/>),
    /// and without this the screenshot half would stay <c>new</c> forever. Returns how many rows changed.
    /// </summary>
    public static int MirrorStatus(ReportStore store, string ownerId)
    {
        if (store.Get(ownerId) is not { } owner)
        {
            return 0;
        }

        int changed = 0;
        foreach (var member in PairOf(store, owner))
        {
            if (member.Id != owner.Id && member.Status != owner.Status && store.SetStatus(member.Id, owner.Status))
            {
                changed++;
            }
        }

        return changed;
    }
}
