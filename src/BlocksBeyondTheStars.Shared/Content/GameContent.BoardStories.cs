// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>#3180: the stories behind the board delivery jobs, from <c>data/board_stories.json</c>.</summary>
public sealed partial class GameContent
{
    private IReadOnlyList<BoardStory> _boardStories = new List<BoardStory>();
    private Dictionary<string, List<BoardStory>> _boardStoriesByNeedAndPlace = new(StringComparer.Ordinal);

    /// <summary>Every board story in authored order — empty when <c>data/board_stories.json</c> is absent (the boards then
    /// show their plain texts).</summary>
    public IReadOnlyList<BoardStory> BoardStories => _boardStories;

    /// <summary>The stories of one delivery item at one kind of board (<see cref="BoardStory.Places"/>), in authored order
    /// — the server picks one per job id; empty when none is written for them.</summary>
    public IReadOnlyList<BoardStory> BoardStoriesFor(string need, string place)
        => _boardStoriesByNeedAndPlace.TryGetValue(BoardStoryIndexKey(need, place), out var list) ? list : (IReadOnlyList<BoardStory>)Array.Empty<BoardStory>();

    /// <summary>Installs the board stories (called by the content loader). Null rows are dropped and null strings of a
    /// hand-edited file become empty ones; everything else is checked by <see cref="Validate"/>.</summary>
    public void SetBoardStories(IEnumerable<BoardStory>? stories)
    {
        var list = new List<BoardStory>();
        var index = new Dictionary<string, List<BoardStory>>(StringComparer.Ordinal);
        foreach (var s in stories ?? Enumerable.Empty<BoardStory>())
        {
            if (s is null)
            {
                continue;
            }

            s.Need ??= string.Empty;
            s.Place ??= string.Empty;
            s.Key ??= string.Empty;
            s.Brief ??= string.Empty;
            list.Add(s);

            string key = BoardStoryIndexKey(s.Need, s.Place);
            if (!index.TryGetValue(key, out var bucket))
            {
                index[key] = bucket = new List<BoardStory>();
            }

            bucket.Add(s);
        }

        _boardStories = list;
        _boardStoriesByNeedAndPlace = index;
    }

    private static string BoardStoryIndexKey(string need, string place) => need + "|" + place;

    /// <summary>#3180: the board stories — a snake_case key unique per place, a known place, a real item and a brief for the
    /// AI wording. Whether every delivery template has its stories at every board, and whether their
    /// <c>mission.board.&lt;place&gt;.&lt;key&gt;.title|desc|thanks</c> texts exist in English and German, is checked by
    /// <c>BoardStoryTests</c> (the templates are server code, and the locale tables load lazily).</summary>
    private void ValidateBoardStories(List<string> problems, Action<string, string?> requireItem)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in _boardStories)
        {
            string ctx = $"Board story '{s.Place}.{s.Key}'";
            if (!IsSnakeCaseKey(s.Key))
            {
                problems.Add($"{ctx}: the key must be lower-case letters, digits and underscores.");
            }

            if (Array.IndexOf(BoardStory.Places, s.Place) < 0)
            {
                problems.Add($"{ctx} names an unknown place (settlement|station|base|ship).");
            }

            if (!keys.Add(s.Place + "." + s.Key))
            {
                problems.Add($"{ctx} is defined twice.");
            }

            if (string.IsNullOrEmpty(s.Need))
            {
                problems.Add($"{ctx} names no item.");
            }

            requireItem(ctx, s.Need);
            if (string.IsNullOrWhiteSpace(s.Brief))
            {
                problems.Add($"{ctx} has no brief for the AI wording.");
            }
        }
    }
}
