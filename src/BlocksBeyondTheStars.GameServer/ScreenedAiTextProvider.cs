// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Runs everything the AI backend writes for players through the world's content screen (#1221) before the
/// game ever shows it. The backend has prompt-level guards, but a prompt is a request, not a guarantee —
/// and on a kids' server the same words are unacceptable whether a player or a model typed them.
///
/// It is a decorator around <see cref="IAiMissionProvider"/> rather than a check at the call sites: there
/// are six of those today (settlement greetings, VEGA banter, board-mission flavour) and the next one would
/// be written without remembering this. Wrapping the ONE provider field covers all of them, now and later.
///
/// A refused text returns <c>null</c>, which every caller already handles — that is exactly the "backend is
/// down" path, and it falls back to the authored, localized line. So the failure mode of screening is the
/// game's normal offline behaviour, not an error.
/// </summary>
public sealed class ScreenedAiTextProvider : IAiMissionProvider
{
    private readonly IAiMissionProvider _inner;
    private readonly Func<string?, string?> _screen;

    /// <param name="inner">The real provider (HTTP backend, or the null provider when AI is off).</param>
    /// <param name="screen">Returns the text to use, or null when it must not be shown.</param>
    public ScreenedAiTextProvider(IAiMissionProvider inner, Func<string?, string?> screen)
    {
        _inner = inner;
        _screen = screen;
    }

    /// <summary>An admin-requested mission plan. Every player-visible string is screened and a single
    /// refusal drops the WHOLE plan: the admin is told generation failed and can ask again, which is a far
    /// better outcome than a mission whose title is clean and whose completion line is not.</summary>
    public MissionPlan? Generate(string context)
    {
        var plan = _inner.Generate(context);
        if (plan is null)
        {
            return null;
        }

        if (Refused(plan.Title, out string? title)
            || Refused(plan.Description, out string? description)
            || Refused(plan.GiverName, out string? giver)
            || Refused(plan.StartDialog, out string? start)
            || Refused(plan.CompleteDialog, out string? complete))
        {
            return null;
        }

        plan.Title = title ?? string.Empty;
        plan.Description = description ?? string.Empty;
        plan.GiverName = giver;
        plan.StartDialog = start;
        plan.CompleteDialog = complete;
        return plan;
    }

    public string? GenerateNpcLine(NpcLineRequest request) => _screen(_inner.GenerateNpcLine(request));

    /// <summary>Board-mission flavour. Title and description are one posting, so if either half is refused
    /// the whole result is dropped and the caller's deterministic locale text takes over.</summary>
    public MissionTextResult? GenerateMissionText(MissionTextRequest request)
    {
        var text = _inner.GenerateMissionText(request);
        if (text is null)
        {
            return null;
        }

        if (Refused(text.Title, out string? title) || Refused(text.Description, out string? description))
        {
            return null;
        }

        text.Title = title ?? string.Empty;
        text.Description = description ?? string.Empty;
        return text;
    }

    /// <summary>Screens one field. Returns true when it must not be shown; otherwise <paramref name="kept"/>
    /// carries the text to use. An empty field is never a refusal — several of these are optional.</summary>
    private bool Refused(string? value, out string? kept)
    {
        if (string.IsNullOrEmpty(value))
        {
            kept = value;
            return false;
        }

        kept = _screen(value);
        return kept is null;
    }
}
