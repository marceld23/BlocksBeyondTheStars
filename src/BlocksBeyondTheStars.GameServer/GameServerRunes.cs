// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Playable rune stones (#3345): the use key on a <c>rune_stone</c> plays the stone's word — a Sounder word fixed per world
/// seed and cell (<see cref="SounderLexicon.WordAt"/>), spelled in the note alphabet (<see cref="SounderAlphabet.Encode"/>)
/// and sung with the Sounder voice (instrument 4) for everyone nearby. Runes ALWAYS sound, with or without the story pack
/// (maintainer decision 2026-11-07). Every use writes the player's <see cref="FirstRuneMilestone"/> once (the pack's start rule
/// waits for it) and collects the word (<see cref="SoundersIds.WordMilestone"/>, the Song Archive — so the stone that starts
/// the story is archived too); only for a player whose Sounders pack is running (<see cref="IsStoryPackActive"/>) does it
/// raise <see cref="GameEventKeys.RunePlayed"/> with the stone's site kind (<see cref="RuneSiteKind"/>). A stone that is a
/// pillar of a melody site strikes that site's tone instead
/// while the player plays a puzzle there (<see cref="TryStrikeMelodyPillar"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two rune words of one player — a held key or a frantic tapper does not stack melodies.</summary>
    internal const double RuneCooldownSeconds = 1.5;

    /// <summary>The per-player milestone every rune use writes once (pack-independent): the Sounders pack starts on it.</summary>
    internal const string FirstRuneMilestone = SoundersIds.MilestonePrefix + "first_rune";

    /// <summary>Site kinds of <see cref="GameEventKeys.RunePlayed"/>'s argument.</summary>
    internal const string RuneSiteMonument = "monument";
    internal const string RuneSiteShrine = "shrine";
    internal const string RuneSiteRuin = "ruin";
    internal const string RuneSiteLone = "lone";
    internal const string RuneSitePlaced = "placed";

    /// <summary>How far (blocks, horizontally) from the Singing Shrine's centre a stone still belongs to the shrine.</summary>
    private const int ShrineRuneReach = 7;

    private readonly Dictionary<string, double> _runeReadyAt = new(System.StringComparer.Ordinal);

    /// <summary>
    /// Whether the story pack <paramref name="packId"/> runs for this player right now: started (<c>story:&lt;pack&gt;:started</c>)
    /// and enabled — installed, player-scoped (or the world story) and not switched off (<see cref="StoryPackEnabledFor"/>).
    /// The same test the pack runtime's own <c>PackRunsFor</c> makes, by id.
    /// </summary>
    internal bool IsStoryPackActive(PlayerSession session, string packId)
        => session.State.Milestones.Contains(StoryPackKeys.Started(packId)) && StoryPackEnabledFor(session, packId);

    /// <summary>The word the rune stone at <paramref name="cell"/> sings (canonical cell, so both sides of the seam agree).</summary>
    internal string RuneWordAt(Vector3i cell)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        return SounderLexicon.WordAt(_meta.Seed, c.X, c.Y, c.Z);
    }

    /// <summary>The use key on a rune stone (registered in <see cref="BuildBlockUseHandlers"/>).</summary>
    private void UseRuneStone(PlayerSession session, Vector3i cell)
    {
        if (TryStrikeMelodyPillar(session, cell))
        {
            return; // a pillar of the melody site the player plays at: its tone, not the word
        }

        string id = session.State.PlayerId;
        if (_runeReadyAt.TryGetValue(id, out double readyAt) && _melodyClock < readyAt)
        {
            return;
        }

        _runeReadyAt[id] = _melodyClock + RuneCooldownSeconds;
        string word = RuneWordAt(cell);
        var at = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        PlayNoteSequence(at, SounderAlphabet.Encode(word), SoundersIds.InstrumentVoice, source: "rune_stone");

        var milestones = session.State.Milestones;
        bool changed = milestones.Add(FirstRuneMilestone);
        if (milestones.Add(SoundersIds.WordMilestone(word)))
        {
            // Kept even before the pack runs: the very stone that starts the story is in the Song Archive from the start.
            changed = true;
            MarkJournalDirty(session);
        }

        if (IsStoryPackActive(session, SoundersIds.PackId))
        {
            RaiseGameEvent(session, GameEventKeys.RunePlayed, RuneSiteKind(cell));
        }

        if (changed)
        {
            _repo.SavePlayer(session.State); // once per player and word — the pack's start rule reads the first one
        }
    }

    /// <summary>
    /// Where the rune stone at <paramref name="cell"/> stands, as <see cref="GameEventKeys.RunePlayed"/> names it: one of the
    /// Singing Shrine's (<c>shrine</c>), part of a monument (<c>monument</c>), one a player placed (<c>placed</c> — the
    /// cell's last editor is a player), one a structure stamp wrote (<c>ruin</c> — the ruined settlements are the only other
    /// stamp with rune stones), else a lone stone of the natural terrain (<c>lone</c>).
    /// </summary>
    internal string RuneSiteKind(Vector3i cell)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (ShrineMelodySite() is { } shrine && NearShrine(shrine, c))
        {
            return RuneSiteShrine;
        }

        if (InMonument(c))
        {
            return RuneSiteMonument;
        }

        if (_repo.GetBlockAttribution(_world.LocationId, c) is { } edit)
        {
            return string.IsNullOrEmpty(edit.Owner) ? RuneSiteRuin : RuneSitePlaced;
        }

        return RuneSiteLone;
    }

    private bool NearShrine(MelodySite shrine, Vector3i c)
    {
        int dx = WorldConstants.WrapDeltaX(c.X - shrine.Centre.X, _world.Circumference);
        int dz = c.Z - shrine.Centre.Z;
        return (dx * dx) + (dz * dz) <= ShrineRuneReach * ShrineRuneReach
            && c.Y >= shrine.Centre.Y - 1 && c.Y <= shrine.Centre.Y + 6;
    }

    /// <summary>Whether a canonical cell lies in (or right at the edge of) a monument's box.</summary>
    private bool InMonument(Vector3i c)
    {
        foreach (var m in _monuments)
        {
            int relX = WorldConstants.WrapDeltaX(c.X - m.Min.X, _world.Circumference);
            if (relX >= -1 && relX <= m.Max.X - m.Min.X + 1
                && c.Z >= m.Min.Z - 1 && c.Z <= m.Max.Z + 1
                && c.Y >= m.Min.Y - 1 && c.Y <= m.Max.Y + 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam: the rune cooldown of a player is over.</summary>
    internal void ResetRuneCooldownForTest(PlayerSession session) => _runeReadyAt.Remove(session.State.PlayerId);
}
