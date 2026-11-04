// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3180 — the stories behind the board delivery jobs (Justus, school club 2026-11-04: "I want more story with the jobs
/// when I take them — why the deliveries, and who wants them"). Every delivery a settlement, station, base or ship board
/// coins (<see cref="BuildBoardMission"/>) used to read the same "Settlement supply request" — on a station too.
/// <list type="bullet">
/// <item>Each delivery gets one of the authored stories of its item and place (<c>data/board_stories.json</c>,
/// <see cref="BoardStory"/>): who needs it (the giver's name fills <c>{giver}</c>), why, and the thank-you that says what
/// came of it on hand-in. The story is picked by its own stable hash of the job id — the board rolls (item, amount,
/// reward) stay untouched, and a job keeps its story across reloads. Without a story the place's own plain texts are
/// used (<c>mission.&lt;place&gt;.gather.*</c>), so a station never says "settlement".</item>
/// <item>The story is sent as display text in the player's language with the giver filled in (like the AI texts and the
/// villagers' wishes), so a client that is older than the server still reads a sentence, never a key.</item>
/// <item>With the AI backend on, its posting text tells the same story in its own words (the brief travels along) and may
/// carry its own thank-you; the authored one is used whenever it does not.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The longest AI-written thank-you the server shows (characters).</summary>
    private const int MaxThanksLength = 240;

    /// <summary>The kind of board a board job id belongs to (<see cref="BoardStory.Places"/>): the own ship's board
    /// (<c>shipboard_…</c>), a base board (<c>home_…</c>), a station's (<c>station_…</c>), else a settlement's.</summary>
    internal static string BoardStoryPlaceOf(string missionId)
        => IsShipBoardMission(missionId) ? BoardStory.PlaceShip
            : IsBaseMission(missionId) ? BoardStory.PlaceBase
            : missionId.StartsWith("station_", System.StringComparison.Ordinal) ? BoardStory.PlaceStation
            : BoardStory.PlaceSettlement;

    /// <summary>The story a job tells, of the stories written for its item and place: a stable hash of the job id — its
    /// own stream beside the board rolls, so adding this changed no roll — or null when there are none.</summary>
    internal static BoardStory? PickBoardStory(IReadOnlyList<BoardStory> stories, string missionId)
        => stories.Count == 0
            ? null
            : stories[(int)(unchecked((ulong)WorldGenerator.StableHash("boardstory:" + missionId)) % (ulong)stories.Count)];

    /// <summary>Gives a freshly coined board delivery its texts: the story of its item and place (title, description,
    /// thank-you and the AI brief), or without one the place's plain texts.</summary>
    private MissionDefinition WithBoardStory(MissionDefinition def)
    {
        string place = BoardStoryPlaceOf(def.Id);
        string need = def.Objectives.FirstOrDefault()?.Target ?? string.Empty;
        if (PickBoardStory(_content.BoardStoriesFor(need, place), def.Id) is { } story)
        {
            def.NameKey = story.TitleKey;
            def.DescriptionKey = story.DescriptionKey;
            def.ThanksKey = story.ThanksKey;
            def.Brief = story.Brief;
        }
        else
        {
            def.NameKey = $"mission.{place}.gather.title";
            def.DescriptionKey = $"mission.{place}.gather.desc";
        }

        return def;
    }

    /// <summary>The posting a board job is shown with, as display text in the player's language: the AI's text when the
    /// backend wrote one (L3), else the job's authored story with the giver filled in. Null for any other mission and for a
    /// board job without a story — its locale keys are sent then.</summary>
    private MissionTextResult? BoardPostingText(MissionDefinition def, string locale)
    {
        if (!IsBoardMissionId(def.Id))
        {
            return null;
        }

        if (BoardMissionText(def.Id, locale) is { } generated)
        {
            return generated;
        }

        return BoardStory.IsStoryKey(def.DescriptionKey)
            ? new MissionTextResult
            {
                Title = Localize(locale, def.NameKey),
                Description = WithGiverName(Localize(locale, def.DescriptionKey), def.GiverName),
            }
            : null;
    }

    /// <summary>The thank-you a turned-in mission is answered with (it rides on the "complete" result): the AI's own words
    /// when the backend wrote them for this board job, else the mission's authored thank-you with the giver filled in;
    /// empty for a mission without one.</summary>
    private string MissionThanksText(MissionDefinition def, string locale)
    {
        if (IsBoardMissionId(def.Id) && BoardMissionText(def.Id, locale)?.Thanks is { } generated && !string.IsNullOrWhiteSpace(generated))
        {
            return Truncate(generated, MaxThanksLength).Trim();
        }

        return string.IsNullOrEmpty(def.ThanksKey) ? string.Empty : WithGiverName(Localize(locale, def.ThanksKey), def.GiverName);
    }

    private static string WithGiverName(string text, string? giverName)
        => text.Replace(BoardStory.GiverToken, giverName ?? string.Empty);

    /// <summary>Test seam (#3180): the item keys the board delivery templates ask for.</summary>
    internal static IReadOnlyList<string> BoardDeliveryNeedsForTest()
        => GiverMissionTemplates.Select(t => t.Need).Distinct(System.StringComparer.Ordinal).ToList();

    /// <summary>Test seam (#3180): the posting a board job is shown with in the player's language (null = its keys).</summary>
    public MissionTextResult? BoardPostingTextForTest(string playerId, string missionId)
        => FindSessionByPlayerId(playerId) is { } session && GetMissionDef(missionId) is { } def ? BoardPostingText(def, session.Locale) : null;

    /// <summary>Test seam (#3180): the thank-you a turn-in of this mission would be answered with, in the player's language.</summary>
    public string MissionThanksForTest(string playerId, string missionId)
        => FindSessionByPlayerId(playerId) is { } session && GetMissionDef(missionId) is { } def ? MissionThanksText(def, session.Locale) : string.Empty;
}
