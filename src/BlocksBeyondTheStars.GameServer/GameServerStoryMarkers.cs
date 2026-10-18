// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// VEGA's story help marker (#2570). When a "where" step of a story has been open for its <c>markerAfter</c> time, the help
/// escalation marks the spot its provider found (the nearest berries, iron, data cache, settlement …) for this player.
/// Before #2570 that was a 30-second ping: by the time VEGA's queued line ("Follow the pulse!") reached the player it was
/// usually gone again. Now the marker lives on the step's escalation state (<see cref="StepEscalationState.MarkerLocationId"/>)
/// — RAM only, one session, never <c>RevealPoi</c> — and so exactly as long as the step is open: completing or skipping the
/// step (also with its chapter or its story) drops that state and the marker with it, and a story the player switched off
/// hides it. It shows while the player is on the body it was raised on, so leaving the body and coming back brings it
/// back. The client draws it on the planet map and the HUD compass, labelled with the step's objective text
/// (<see cref="NetMarker.Story"/>). The marker view is pushed when it changes: at once when the marker goes up and after a
/// Journal action, otherwise by the 1 Hz story pass (<see cref="SyncStoryMarkers"/>). During the basic training only the
/// step on the objective chip gets a marker (<see cref="MarkerFitsTheChip"/>).
/// <para>#2742: the world story's chapter lead marks its ground target the same way (<see cref="MarkCurrentLead"/>): a story
/// marker of this player only, kept on the lead cache (<see cref="StoryLeadCache.MarkerLocationId"/>), labelled with the
/// lead's objective (<see cref="NetMarker.LabelArg"/> fills its <c>{0}</c>), part of the same signature — and so there until
/// the lead is followed or the current lead changes, hidden while the VEGA hints are muted.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The id of a story marker on the wire: this prefix plus the step's progress key.</summary>
    internal const string StoryMarkerIdPrefix = "story:";

    /// <summary>Connection id → the story markers that client was last sent (<see cref="StoryMarkerSignature"/>). Only
    /// players with a story marker have an entry.</summary>
    private readonly Dictionary<int, string> _storyMarkersSent = new();

    /// <summary>#2570: whether the help marker of <paramref name="step"/> may go up now. During the basic training only the
    /// step on the objective chip gets one — an open extra (a deferred CORE step) or another story's step would have VEGA
    /// talk about a target the chip does not show. Outside the training every escalating step may, as before. A refusal is
    /// not spent: the marker comes once the step owns the chip.</summary>
    private bool MarkerFitsTheChip(PlayerSession s, CompiledStep step, StepEscalationState esc)
    {
        if (!TutorialPackRunning(s))
        {
            return true;
        }

        if (esc.Deferred)
        {
            return false;
        }

        var chip = LiveVegaChip(s);
        return string.Equals(chip.PackId, step.Pack.Id, StringComparison.OrdinalIgnoreCase)
               && string.Equals(chip.StepId, step.Id, StringComparison.Ordinal);
    }

    /// <summary>#2570: puts the help marker of <paramref name="step"/> on <paramref name="at"/> of the player's current body
    /// and pushes their marker view. False when it has no place (no body, or the step's escalation state is gone).</summary>
    private bool RaiseStoryMarker(PlayerSession s, CompiledStep step, Vector3f at)
    {
        string loc = s.CurrentLocationId;
        if (string.IsNullOrEmpty(loc) || !s.StepEscalation.TryGetValue(step.ProgressKey, out var esc))
        {
            return false;
        }

        esc.MarkerLocationId = loc;
        esc.MarkerAt = at;
        SendMarkers(s);
        return true;
    }

    /// <summary>#2570: the step whose help marker an escalation state carries, while that marker is still live: the step is
    /// open (completing or skipping it drops the state anyway) and its story still runs for the player (switched off, through
    /// or skipped as a whole ⇒ hidden). Null otherwise.</summary>
    private CompiledStep? LiveStoryMarkerStep(PlayerSession s, string progressKey, StepEscalationState esc)
    {
        if (esc.MarkerLocationId is not { Length: > 0 } || !s.State.StepProgress.ContainsKey(progressKey)
            || !_stepByProgressKey.TryGetValue(progressKey, out var step) || StepComplete(s, step))
        {
            return null;
        }

        bool runs = step.Pack.IsStoryMissions ? StoryPackEnabledFor(s, step.Pack.Id) : PackRunsFor(s, step.Pack);
        return runs ? step : null;
    }

    /// <summary>#2570: adds the viewer's own live story markers on <paramref name="loc"/> to their marker view — a help
    /// marker is never shown to anyone else. Flagged <see cref="NetMarker.Ping"/> too, so an older client draws it as a
    /// ping.</summary>
    private void AddStoryMarkers(PlayerSession viewer, string loc, List<NetMarker> into)
    {
        foreach (var kv in viewer.StepEscalation)
        {
            if (!string.Equals(kv.Value.MarkerLocationId, loc, StringComparison.Ordinal)
                || LiveStoryMarkerStep(viewer, kv.Key, kv.Value) is not { } step)
            {
                continue;
            }

            var at = kv.Value.MarkerAt;
            into.Add(new NetMarker
            {
                Id = StoryMarkerIdPrefix + kv.Key,
                OwnerId = viewer.State.PlayerId,
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                Ping = true,
                Story = true,
                LabelKey = StepChipKey(viewer, step),
                PackId = step.Pack.Id,
            });
        }

        if (LiveLeadMarker(viewer, loc))
        {
            var lead = viewer.StoryLeads;
            into.Add(new NetMarker
            {
                Id = StoryLeadMarkerIdPrefix + lead.MarkerFor,
                OwnerId = viewer.State.PlayerId,
                X = lead.MarkerAt.X,
                Y = lead.MarkerAt.Y,
                Z = lead.MarkerAt.Z,
                Ping = true,
                Story = true,
                LabelKey = lead.MarkerKey,
                LabelArg = lead.MarkerArg,
                PackId = _story?.Id ?? string.Empty,
            });
        }
    }

    /// <summary>#2742: the id prefix of a chapter lead's marker on the wire (then the lead's identity).</summary>
    internal const string StoryLeadMarkerIdPrefix = StoryMarkerIdPrefix + "lead:";

    /// <summary>#2742: whether the player's chapter-lead marker shows on <paramref name="loc"/> now: it was raised there for the
    /// lead that is still the marked one, the VEGA hints are not muted, and the world story with its leads still runs for the
    /// player. Followed or replaced, the marker was cleared already (<see cref="MarkCurrentLead"/>, <see cref="CompleteLead"/>).</summary>
    private bool LiveLeadMarker(PlayerSession s, string loc)
    {
        var lead = s.StoryLeads;
        return lead.MarkerLocationId.Length > 0
               && string.Equals(lead.MarkerLocationId, loc, StringComparison.Ordinal)
               && string.Equals(lead.MarkerFor, lead.MarkedFor, StringComparison.Ordinal)
               && !s.VegaHintsMuted && LeadsActive && StoryTrackStarted(s);
    }

    /// <summary>#2570: the story markers the player sees now as one comparable string (their progress keys, #2742: and their
    /// chapter lead's); empty — and no allocation — without any.</summary>
    private string StoryMarkerSignature(PlayerSession s)
    {
        string loc = s.CurrentLocationId;
        if (string.IsNullOrEmpty(loc))
        {
            return string.Empty;
        }

        bool lead = LiveLeadMarker(s, loc);
        if (s.StepEscalation.Count == 0)
        {
            return lead ? StoryLeadMarkerIdPrefix + s.StoryLeads.MarkerFor : string.Empty;
        }

        StringBuilder? sb = null;
        foreach (var kv in s.StepEscalation)
        {
            if (string.Equals(kv.Value.MarkerLocationId, loc, StringComparison.Ordinal) && LiveStoryMarkerStep(s, kv.Key, kv.Value) is not null)
            {
                (sb ??= new StringBuilder()).Append(kv.Key).Append('|');
            }
        }

        if (lead)
        {
            (sb ??= new StringBuilder()).Append(StoryLeadMarkerIdPrefix).Append(s.StoryLeads.MarkerFor);
        }

        return sb?.ToString() ?? string.Empty;
    }

    /// <summary>#2570: remembers which story markers the marker view just sent to the player carried (called by
    /// <see cref="SendMarkers"/>).</summary>
    private void NoteStoryMarkersSent(PlayerSession s)
    {
        string sent = StoryMarkerSignature(s);
        if (sent.Length == 0)
        {
            _storyMarkersSent.Remove(s.ConnectionId);
        }
        else
        {
            _storyMarkersSent[s.ConnectionId] = sent;
        }
    }

    /// <summary>#2570: re-sends the player's marker view when their story markers changed since it was last sent — the step
    /// was done or skipped, the story switched off. Run by the 1 Hz story pass and after every Journal action; cheap while the
    /// player has none.</summary>
    private void SyncStoryMarkers(PlayerSession s)
    {
        if (!s.Joined)
        {
            return;
        }

        string now = StoryMarkerSignature(s);
        string sent = _storyMarkersSent.TryGetValue(s.ConnectionId, out var last) ? last : string.Empty;
        if (!string.Equals(now, sent, StringComparison.Ordinal))
        {
            SendMarkers(s);
        }
    }

    /// <summary>Test seam (#2570): the story markers the player sees now.</summary>
    internal IReadOnlyList<NetMarker> StoryMarkersForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? MarkersFor(s).Markers.Where(m => m.Story).ToList()
            : new List<NetMarker>();

    /// <summary>Test seam (#2570): the marker-view sync of the 1 Hz story pass.</summary>
    internal void SyncStoryMarkersForTest(PlayerSession s) => SyncStoryMarkers(s);
}
