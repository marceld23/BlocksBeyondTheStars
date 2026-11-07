// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What an ACTIVE player pack brings besides its chapters (#3347, the Sounders story pack): before, only the save's world
/// story supplied lore texts, NPC dialogs, characters and readable texts. A player pack is active for a player when it has
/// started for them, they did not switch it off and the world did not switch it off (<see cref="GameRules.DisabledStoryPacks"/>).
/// Then its <c>lore_sites.json</c> texts can be found (gated by the player's chapter in the pack, <see cref="LoreSite.MinChapter"/>),
/// its dialogs are offered to that player, its characters may take an NPC slot (on its <see cref="StoryCharacter.PlanetTypes"/>),
/// and its <see cref="StoryDefinition.Texts"/> fill its Journal page once unlocked. Hidden blueprints (#3350) and the 1 Hz
/// per-player pass that pushes what changed live here too.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Condition texts parsed once (a dialog choice's <c>visibleIf</c>, a text's <c>unlockedBy</c>, a blueprint's
    /// <c>hiddenUntil</c>, a pack system's <c>visibleIf</c>) — null = broken (logged once, never holds).</summary>
    private readonly Dictionary<string, StoryCondition?> _conditionTextCache = new(StringComparer.Ordinal);

    /// <summary>What the 1 Hz pass last pushed to a session (dropped with the session).</summary>
    private readonly ConditionalWeakTable<PlayerSession, PackExtrasState> _packExtras = new();

    /// <summary>The per-session memory of <see cref="TickPackExtrasFor"/>.</summary>
    private sealed class PackExtrasState
    {
        /// <summary>The unlocked pack texts last counted (a change marks the Journal dirty).</summary>
        public int TextCount { get; set; } = -1;

        /// <summary>The hidden blueprints last sent as visible (null = nothing sent yet).</summary>
        public string? ShownBlueprints { get; set; }

        /// <summary>The pack systems this player could see at the last pass (a change re-sends their star map).</summary>
        public string PackSystems { get; set; } = string.Empty;
    }

    // ---------------------------------------------------------------------------------------------
    // Which packs are active.
    // ---------------------------------------------------------------------------------------------

    // IsStoryPackActive(PlayerSession, string) — started and enabled (not switched off in the Journal, not switched off for this
    // world: StoryPackEnabledFor) — lives in GameServerRunes.cs; what an active pack brings (lore, dialogs, texts, systems) is
    // offered to that player only.

    /// <summary>#3347: <c>IsStoryPackActive(PlayerSession, string)</c> for an online player by id (false when offline).</summary>
    internal bool IsStoryPackActive(string playerId, string packId)
        => FindSessionByPlayerId(playerId) is { } session && IsStoryPackActive(session, packId);

    /// <summary>#3347: whether the world switched the pack off (<see cref="GameRules.DisabledStoryPacks"/>, the world-creation
    /// Stories page) — it never starts here, and nothing of it is offered.</summary>
    internal bool StoryPackDisabledInWorld(string? packId)
        => !string.IsNullOrEmpty(packId) && (Rules.DisabledStoryPacks ?? new List<string>()).Contains(packId!, StringComparer.OrdinalIgnoreCase);

    /// <summary>The player packs active for this player (priority order; never the world story's missions).</summary>
    private IEnumerable<CompiledPack> ActivePlayerPacks(PlayerSession s)
    {
        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsStoryMissions && PackRunsFor(s, pack))
            {
                yield return pack;
            }
        }
    }

    /// <summary>#3347: the player's chapter in a pack — the 1-based number (manifest order) of the furthest chapter that ran,
    /// was done or skipped; 0 before the first. What <see cref="LoreSite.MinChapter"/> compares with.</summary>
    internal int PackChapterReached(PlayerSession s, CompiledPack pack)
    {
        var m = MilestonesOf(s);
        for (int i = pack.Chapters.Count - 1; i >= 0; i--)
        {
            var ch = pack.Chapters[i];
            if (m.Contains(StoryPackKeys.ChapterActive(pack.Id, ch.Id)) || ChapterDoneOrSkipped(s, ch) || ChapterRuns(s, ch))
            {
                return i + 1;
            }
        }

        return 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Conditions written as text (choices, texts, blueprints, systems).
    // ---------------------------------------------------------------------------------------------

    /// <summary>Whether every condition text holds for the player (an empty list holds); a condition that does not parse
    /// never holds (logged once — the validators report it to authors).</summary>
    internal bool ConditionTextsHold(IEnumerable<string>? texts, PlayerSession who)
    {
        foreach (string text in texts ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(text) && !ConditionTextHolds(text, who))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One condition text for the player (empty holds; broken never holds).</summary>
    internal bool ConditionTextHolds(string? text, PlayerSession who)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!_conditionTextCache.TryGetValue(text!, out var condition))
        {
            if (!StoryConditionParser.TryParse(text!, out condition, out string error, WorldPredicateNames))
            {
                _log.Warn($"Story condition is broken and never holds: {error}");
                condition = null;
            }

            _conditionTextCache[text!] = condition;
        }

        return condition is not null && EvaluateStoryCondition(condition, who);
    }

    // ---------------------------------------------------------------------------------------------
    // Lore (#3347: a side pack's lore_sites.json).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The lore texts of a site kind this player may find now: the world story's (by the world's knowledge level) and
    /// every active player pack's (by the player's chapter in it) — not read yet, with a key.</summary>
    private List<LoreSite> EligibleLoreSites(PlayerSession s, string site)
    {
        var eligible = new List<LoreSite>();
        var read = s.State.Milestones;
        bool Fits(LoreSite l) => string.Equals(l.Site, site, StringComparison.OrdinalIgnoreCase)
                                 && !string.IsNullOrEmpty(l.Key) && !read.Contains(LoreMilestonePrefix + l.Key);

        if (StoryActive && _story is not null)
        {
            int knowledge = WorldKnowledgeLevel();
            eligible.AddRange(_story.LoreSites.Where(l => Fits(l) && l.MinKnowledge <= knowledge));
        }

        foreach (var pack in ActivePlayerPacks(s))
        {
            if (pack.Def.LoreSites.Count == 0)
            {
                continue;
            }

            int chapter = PackChapterReached(s, pack);
            eligible.AddRange(pack.Def.LoreSites.Where(l => Fits(l) && l.MinChapter <= chapter && !eligible.Contains(l)));
        }

        return eligible;
    }

    // ---------------------------------------------------------------------------------------------
    // Dialogs and characters (#3347).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The dialogs of the player packs active for this player (priority order).</summary>
    private IEnumerable<DialogDefinition> ActivePackDialogs(PlayerSession s)
        => ActivePlayerPacks(s).SelectMany(p => p.Def.Dialogs);

    /// <summary>The pack a dialog belongs to (the world story or an installed player pack), or null for an engine dialog of
    /// <c>data/dialogs.json</c>.</summary>
    private StoryDefinition? PackOfDialog(DialogDefinition dialog)
    {
        if (_story is not null && _story.Dialogs.Contains(dialog))
        {
            return _story;
        }

        foreach (var pack in PlayerPacks)
        {
            if (pack.Def.Dialogs.Contains(dialog))
            {
                return pack.Def;
            }
        }

        return null;
    }

    /// <summary>The authored characters that may take an NPC slot in this world: the running world story's, and those of every
    /// installed player pack the world did not switch off (a player pack's NPC is the same person for everyone — only its
    /// dialog is per player).</summary>
    private IEnumerable<StoryCharacter> CastableCharacters()
    {
        if (StoryActive && _story is not null)
        {
            foreach (var ch in _story.Characters)
            {
                yield return ch;
            }
        }

        foreach (var pack in PlayerPacks)
        {
            if (pack.IsStoryMissions || StoryPackDisabledInWorld(pack.Id))
            {
                continue;
            }

            foreach (var ch in pack.Def.Characters)
            {
                yield return ch;
            }
        }
    }

    /// <summary>#3347: the authored character with this id in any installed pack (the world story first), or null.</summary>
    internal StoryCharacter? FindStoryCharacter(string? characterId)
    {
        if (string.IsNullOrEmpty(characterId))
        {
            return null;
        }

        var all = (_story?.Characters ?? new List<StoryCharacter>()).Concat(_content.Stories.Values.SelectMany(d => d.Characters));
        return all.FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// #3347: makes a code-spawned NPC an authored character of a pack (the Sounders' Echoes): it carries the character id —
    /// so <c>PickDialog</c> offers that character's pack dialog (<see cref="DialogDefinition.Character"/>) to every player
    /// for whom the pack is active — and the character's name and fixed look. Call it right after spawning the NPC. Returns
    /// false (the NPC unchanged) when no installed pack defines the character.
    /// </summary>
    internal bool ApplyStoryCharacter(ServerNpc npc, string characterId)
    {
        if (npc is null || FindStoryCharacter(characterId) is not { } ch || string.IsNullOrEmpty(ch.Name))
        {
            return false;
        }

        DressAuthoredCharacter(npc, ch);
        return true;
    }

    /// <summary>The fixed identity and look of an authored character (the same person everywhere).</summary>
    private static void DressAuthoredCharacter(ServerNpc npc, StoryCharacter ch)
    {
        var look = new Random(unchecked((int)BlocksBeyondTheStars.WorldGeneration.WorldGenerator.StableHash("charlook:" + ch.Id)));
        npc.CharacterId = ch.Id;
        npc.Name = ch.Name;
        npc.IsRobot = ch.Robot;
        npc.Size = 1.0f;
        npc.SkinRgb = ch.Robot
            ? new uint[] { 0xBFC7CF, 0xD5DBE1, 0xA8B2BC, 0xC9CCB8 }[look.Next(4)]
            : new uint[] { 0xFFE0C4, 0xD9A066, 0xA9713A, 0x8D5524 }[look.Next(4)];
        npc.OutfitRgb = new uint[] { 0x3D7EBF, 0x8A63BF, 0x2FA48E, 0xC24B5A, 0xB3A05C, 0x6B8FA3 }[look.Next(6)];
        npc.LegsRgb = new uint[] { 0x4A4E57, 0x5C5346, 0x3E4A5C, 0x6B5C4A }[look.Next(4)];
    }

    /// <summary>#3347: the <c>milestone:&lt;key&gt;</c> dialog consequence — sets the per-player milestone when the dialog's pack
    /// may write it (<see cref="StoryPackMilestones.MaySet"/>); anything else is ignored (and logged), like every malformed
    /// consequence.</summary>
    private void ApplyMilestoneConsequence(PlayerSession session, StoryDefinition? pack, string key)
    {
        if (!StoryPackMilestones.MaySet(pack, key))
        {
            _log.Warn($"Dialog consequence 'milestone:{key}' ignored: a pack dialog may only set keys under '<pack id>:' or its milestonePrefix.");
            return;
        }

        if (session.State.Milestones.Add(key))
        {
            MarkJournalDirty(session); // a text, a choice, a system may wait for it
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Readable pack texts (#3347: the Journal page's Song Archive).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The pack's texts this player has unlocked, in manifest order — none while the pack is not active for them.</summary>
    internal NetStoryText[] UnlockedPackTexts(PlayerSession s, CompiledPack pack)
    {
        if (pack.Def.Texts.Count == 0 || !PackRunsFor(s, pack))
        {
            return Array.Empty<NetStoryText>();
        }

        return pack.Def.Texts
            .Where(t => !string.IsNullOrEmpty(t.Id) && ConditionTextHolds(t.UnlockedBy, s))
            .Select(t => new NetStoryText { Id = t.Id, TitleKey = t.TitleKey ?? string.Empty, TextKey = t.TextKey ?? string.Empty })
            .ToArray();
    }

    // ---------------------------------------------------------------------------------------------
    // Hidden blueprints (#3350).
    // ---------------------------------------------------------------------------------------------

    /// <summary>#3350: whether the blueprint is hidden for this player — it has a <see cref="BlueprintDefinition.HiddenUntil"/>
    /// that does not hold for them and they do not own it.</summary>
    internal bool BlueprintHiddenFor(PlayerSession s, BlueprintDefinition bp)
        => !string.IsNullOrWhiteSpace(bp.HiddenUntil) && !s.State.UnlockedBlueprints.Contains(bp.Key) && !ConditionTextHolds(bp.HiddenUntil, s);

    /// <summary>#3350: <see cref="BlueprintHiddenFor(PlayerSession, BlueprintDefinition)"/> by player state — an offline player's
    /// conditional blueprint counts as hidden (VEGA's hints never name one).</summary>
    internal bool BlueprintHiddenFor(Shared.State.PlayerState p, BlueprintDefinition bp)
        => !string.IsNullOrWhiteSpace(bp.HiddenUntil) && !p.UnlockedBlueprints.Contains(bp.Key)
           && (FindSessionByPlayerId(p.PlayerId) is not { } s || BlueprintHiddenFor(s, bp));

    /// <summary>The hidden blueprints this player may see now (sorted keys).</summary>
    private List<string> ShownHiddenBlueprints(PlayerSession s)
        => _content.Blueprints.Values
            .Where(bp => !string.IsNullOrWhiteSpace(bp.HiddenUntil) && !BlueprintHiddenFor(s, bp))
            .Select(bp => bp.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    // ---------------------------------------------------------------------------------------------
    // The 1 Hz per-player pass.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 1 Hz per player (from <see cref="TickStoryPacksFor"/>): a newly unlocked pack text marks the Journal dirty, a change in
    /// the hidden blueprints this player may see is pushed (<see cref="BlueprintVisibility"/>, also the first pass after the
    /// join), and a pack star system is appended / its visibility pushed (<see cref="TickPackSystemsFor"/>).
    /// </summary>
    private void TickPackExtrasFor(PlayerSession s)
    {
        if (!s.Joined || s.Spectating)
        {
            return;
        }

        var state = _packExtras.GetValue(s, _ => new PackExtrasState());
        int texts = 0;
        foreach (var pack in PlayerPacks)
        {
            texts += pack.Def.Texts.Count == 0 ? 0 : UnlockedPackTexts(s, pack).Length;
        }

        if (texts != state.TextCount)
        {
            if (state.TextCount >= 0)
            {
                MarkJournalDirty(s);
            }

            state.TextCount = texts;
        }

        string shown = string.Join(",", ShownHiddenBlueprints(s));
        if (!string.Equals(shown, state.ShownBlueprints, StringComparison.Ordinal))
        {
            state.ShownBlueprints = shown;
            Send(s, new BlueprintVisibility { Shown = shown.Length == 0 ? Array.Empty<string>() : shown.Split(',') });
        }

        TickPackSystemsFor(s, state);
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: runs the 1 Hz pack pass for a player now.</summary>
    internal void TickPackExtrasForTest(PlayerSession s) => TickPackExtrasFor(s);

    /// <summary>Test seam: the unlocked texts of a pack for a player.</summary>
    internal NetStoryText[] UnlockedPackTextsForTest(PlayerSession s, string packId)
        => TryGetPlayerPack(packId, out var pack) ? UnlockedPackTexts(s, pack) : Array.Empty<NetStoryText>();

    /// <summary>Test seam: a villager at <paramref name="pos"/> made the pack character <paramref name="characterId"/> by
    /// <see cref="ApplyStoryCharacter"/> (a code-spawned NPC, like the Sounders' Echoes). Returns its id.</summary>
    internal int AddCharacterNpcForTest(Shared.Geometry.Vector3f pos, string characterId)
    {
        int id = AddNpcForTest(pos);
        ApplyStoryCharacter(_npcs.First(n => n.Id == id), characterId);
        return id;
    }
}
