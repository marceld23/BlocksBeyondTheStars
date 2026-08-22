// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>
/// Mission chains (#1212) — the vocabulary shared by the content validator, the server's offer/accept
/// rules and the test suite, so the three can never disagree. A chain is a set of
/// <see cref="MissionDefinition"/>s with the same <see cref="MissionDefinition.ChainId"/>; each carries a
/// 1-based <see cref="MissionDefinition.Step"/>, the ids it <see cref="MissionDefinition.Prerequisites"/>
/// (turned in first), who hands it out (<see cref="MissionDefinition.GiverRole"/>), how friendly the giver
/// must be (<see cref="MissionDefinition.MinStage"/>), where it surfaces (<see cref="MissionDefinition.Surface"/>)
/// and at which kind of place (<see cref="MissionDefinition.OfferAt"/>). Two definitions sharing ChainId AND
/// Step are <em>alternatives</em> — the server offers the first feasible one (lowest id), so an authored
/// chain can end in "clear the camp" on bandit worlds and "scout the neighbour" everywhere else.
/// </summary>
public static class MissionChains
{
    public const string SurfaceBoard = "board";
    public const string SurfaceRadio = "radio";
    public const string SurfaceDialog = "dialog";

    public const string OfferAtSettlement = "settlement";
    public const string OfferAtStation = "station";
    public const string OfferAtAny = "any";

    public const string StageKnown = "known";
    public const string StageTrusted = "trusted";

    /// <summary>Travel-objective target for chains (#1212): "land on any body other than the one you took
    /// the job on". Authored content cannot know the world's bodies, so the check is relative to
    /// <see cref="MissionProgress.AcceptedBodyId"/>.</summary>
    public const string TravelOtherBody = "other_body";

    /// <summary>The dialogue consequence that hands a mission out (<c>mission:&lt;id&gt;</c>).</summary>
    public const string DialogConsequence = "mission";

    /// <summary>Prefix of an authored-character giver role (<c>character:&lt;id&gt;</c>).</summary>
    public const string CharacterRolePrefix = "character:";

    public static bool IsValidStage(string? stage) => string.IsNullOrEmpty(stage) || stage is StageKnown or StageTrusted;

    public static bool IsValidSurface(string? surface) => string.IsNullOrEmpty(surface) || surface is SurfaceBoard or SurfaceRadio or SurfaceDialog;

    public static bool IsValidOfferAt(string? offerAt) => string.IsNullOrEmpty(offerAt) || offerAt is OfferAtSettlement or OfferAtStation or OfferAtAny;

    /// <summary>quartermaster | vendor | settler | character:&lt;id&gt; | "" (the board's quartermaster).</summary>
    public static bool IsValidGiverRole(string? role)
        => string.IsNullOrEmpty(role)
           || role is "quartermaster" or "vendor" or "settler"
           || (role.StartsWith(CharacterRolePrefix, System.StringComparison.Ordinal) && role.Length > CharacterRolePrefix.Length);

    /// <summary>The effective surface: an unset surface means the board.</summary>
    public static string SurfaceOf(MissionDefinition def) => string.IsNullOrEmpty(def.Surface) ? SurfaceBoard : def.Surface;

    /// <summary>The effective giver role: an unset role means the board's quartermaster.</summary>
    public static string GiverRoleOf(MissionDefinition def) => string.IsNullOrEmpty(def.GiverRole) ? "quartermaster" : def.GiverRole;

    /// <summary>Ordinal rank of a relationship stage requirement ("" &lt; known &lt; trusted) — the same
    /// ladder the NPC dialogue and story-thread gates use.</summary>
    public static int StageRank(string? stage) => stage switch
    {
        StageTrusted => 2,
        StageKnown => 1,
        _ => 0,
    };
}
