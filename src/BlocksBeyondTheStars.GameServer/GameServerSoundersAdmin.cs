// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Story QA for the Sounders' Song (#3368), like VEGA's <c>/advance</c>, <c>/revealfinale</c> and <c>/gotocore</c>: the
/// cheat-gated admin verb <c>sounders</c> (chat <c>/sounders &lt;what&gt;</c>) jumps around the story for the asking admin.
/// <c>status</c> reports the pack's state; <c>start</c> starts it now; <c>chapter &lt;1-8&gt;</c> makes a chapter run
/// (the ones before it are skipped); <c>items</c> hands over every Sounder item and the four hidden blueprints;
/// <c>echo</c> calls the next Echo right here; <c>song</c> learns the Song of Return (Ornemis shows on the star map);
/// <c>soaniri</c> lands straight on Soaniri; <c>tower</c> drops the admin onto the Choir Tower's platform; <c>soundi</c>
/// grants the story Soundi. Every use is in the cheat log.
/// </summary>
public sealed partial class GameServer
{
    private const string SoundersAdminUsage =
        "Usage: /sounders status | start | chapter <1-8> | items | echo | song | soaniri | tower | soundi";

    private void AdminSounders(PlayerSession session, string? args)
        => Send(session, new ServerMessage { Text = SoundersAdminReply(session, args) });

    private string SoundersAdminReply(PlayerSession session, string? args)
    {
        string[] words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string verb = words.Length > 0 ? words[0].ToLowerInvariant() : "status";
        string result = verb switch
        {
            "status" => SoundersStatus(session),
            "start" => AdminSoundersStart(session),
            "chapter" => AdminSoundersChapter(session, words.Length > 1 ? words[1] : string.Empty),
            "items" => AdminSoundersItems(session),
            "echo" => AdminSoundersEcho(session),
            "song" => AdminSoundersSong(session),
            "soaniri" => AdminSoundersSoaniri(session),
            "tower" => AdminSoundersTower(session),
            "soundi" => AdminSoundersSoundi(session),
            _ => SoundersAdminUsage,
        };
        if (!string.Equals(verb, "status", StringComparison.Ordinal) && !ReferenceEquals(result, SoundersAdminUsage))
        {
            CheatLog(session.State, "sounders " + string.Join(" ", words));
        }

        return result;
    }

    private CompiledPack? SoundersPack => PlayerPacks.FirstOrDefault(p => p.Id == SoundersIds.PackId);

    /// <summary>The pack's state for the admin, with the pack's own milestones counted.</summary>
    private string SoundersStatus(PlayerSession s)
    {
        var m = s.State.Milestones;
        string chapter = SoundersChapters.FirstOrDefault(c => InSoundersChapter(s, c)) ?? "-";
        int words = m.Count(k => k.StartsWith(SoundersIds.MilestonePrefix + "word:", StringComparison.Ordinal));
        int discs = m.Count(k => k.StartsWith(SoundersIds.MilestonePrefix + "disc:", StringComparison.Ordinal));
        int echoes = SoundersIds.Echoes.Count(e => m.Contains(SoundersIds.EchoMilestone(e)));
        return string.Format(CultureInfo.InvariantCulture,
            "Sounders: active={0}, chapter={1}, words={2}/{3}, discs={4}/{5}, echoes={6}/4, song={7}, roll={8}, bridge={9}, soundi={10}",
            IsStoryPackActive(s, SoundersIds.PackId), chapter, words, SounderLexicon.Words.Length, discs,
            SoundersIds.StoryDiscs.Length, echoes, m.Contains(SoundersIds.MilestoneSongLearned),
            m.Contains(SoundersRollConvincedMilestone), m.Contains(SoundersBridgeDoneMilestone), m.Contains(SoundersSoundiWonMilestone));
    }

    private string AdminSoundersStart(PlayerSession s)
    {
        if (SoundersPack is not { } pack)
        {
            return "The Sounders pack is not installed.";
        }

        s.State.Milestones.Add(FirstRuneMilestone);
        return StartPackOnRequest(s, pack) || IsStoryPackActive(s, SoundersIds.PackId)
            ? "The Sounders' Song runs for you."
            : "The pack cannot start here (switched off for this world?).";
    }

    private string AdminSoundersChapter(PlayerSession s, string number)
    {
        if (SoundersPack is not { } pack
            || !int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > SoundersChapters.Length)
        {
            return SoundersAdminUsage;
        }

        if (!IsStoryPackActive(s, SoundersIds.PackId))
        {
            AdminSoundersStart(s);
        }

        var chapter = pack.Chapters.FirstOrDefault(c => c.Id == SoundersChapters[n - 1]);
        return chapter is not null && JumpToChapter(s, chapter)
            ? $"Chapter {n} ({chapter.Id}) runs now. Use /sounders items, song or echo for what it needs."
            : $"Chapter {n} could not be started.";
    }

    /// <summary>Every Sounder item, the decoded-disc milestones (so the hidden blueprints show) and the four blueprints.</summary>
    private string AdminSoundersItems(PlayerSession s)
    {
        foreach (string disc in SoundersIds.StoryDiscs)
        {
            s.State.Milestones.Add(SoundersIds.DiscMilestone(disc));
        }

        foreach (string bp in new[] { SoundersIds.BlueprintResonance, SoundersIds.BlueprintObsidian, SoundersIds.BlueprintSpindle, SoundersIds.BlueprintVoice })
        {
            s.State.UnlockedBlueprints.Add(bp);
        }

        foreach (string item in new[]
                 {
                     SoundersIds.SongSpindle, SoundersIds.DiscHearing, SoundersIds.DiscCalm, SoundersIds.DiscLight, SoundersIds.DiscSong,
                     SoundersIds.ResonanceBlaster, SoundersIds.ObsidianHelmet, SoundersIds.ObsidianChest, SoundersIds.ObsidianLegs,
                 })
        {
            GiveSoundersItem(s, item, 1);
        }

        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        SendPlayerState(s);
        return "Sounder items, decoded discs and the four Sounder blueprints are yours.";
    }

    /// <summary>The next Echo, right next to the admin — no rune stones, no spacing rule.</summary>
    private string AdminSoundersEcho(PlayerSession s)
    {
        if (NextSoundersEcho(s.State.Milestones) is not { } next)
        {
            return "Every Echo was questioned already.";
        }

        if (_soundersEchoes.TryGetValue(s.State.PlayerId, out var standing))
        {
            RemoveSounderNpc(standing.NpcId);
            _soundersEchoes.Remove(s.State.PlayerId);
        }

        var p = s.State.Position;
        SpawnSoundersEcho(s, next, new Vector3f(p.X + 3f, p.Y, p.Z + 3f));
        return $"Echo '{next}' is here. Talk to it with the use key (it stays while chapter 5 runs and the hearing disc is in).";
    }

    private string AdminSoundersSong(PlayerSession s)
    {
        SetSoundersMilestone(s, SoundersIds.MilestoneSongLearned);
        return "The Song of Return is learned — Ornemis appears on your star map within a second.";
    }

    /// <summary>Lands the admin on Soaniri: the song learned (so Ornemis is theirs to see), the system appended, then the
    /// normal landing path with the jump gate bypassed.</summary>
    private string AdminSoundersSoaniri(PlayerSession s)
    {
        SetSoundersMilestone(s, SoundersIds.MilestoneSongLearned);
        foreach (var (pack, system) in DeclaredPackSystems())
        {
            if (string.Equals(system.Id, SoundersIds.SystemId, StringComparison.Ordinal))
            {
                RevealPackSystem(pack, system);
            }
        }

        HandleTravel(s, new TravelIntent { DestinationBodyId = StoryPackSystem.BodyIdOf(SoundersIds.SystemId) }, quickTravel: false, adminBypass: true);
        return "Off to Soaniri.";
    }

    private string AdminSoundersTower(PlayerSession s)
    {
        if (FindMelodySite(_world.LocationId, ChoirTowerSiteKey) is not { } tower
            || !string.Equals(s.CurrentLocationId, _world.LocationId, StringComparison.Ordinal))
        {
            return "No Choir Tower on this world — /sounders soaniri first.";
        }

        if (InSpace(s.State.PlayerId))
        {
            return "Land on Soaniri first — /sounders tower works on foot.";
        }

        // #3393: through the snap channel — a bare state update is dropped by the client, so the admin never moved.
        var c = tower.CentreF;
        s.Streaming.SentChunks.Clear();
        SnapPlayerTo(s, new Vector3f(c.X + 1.5f, c.Y + 0.5f, c.Z + 1.5f), "@srv.tp.done");
        return "On the Choir Tower.";
    }

    private string AdminSoundersSoundi(PlayerSession s)
    {
        SetSoundersMilestone(s, SoundersSoundiWonMilestone);
        return GrantStoryCompanion(s, TamedCreature.SoundiKind, SoundersIds.Soundi, "Muffin") is not null
            ? "A Soundi travels with you now."
            : "You already have a Soundi.";
    }

    /// <summary>Test seam: runs <c>/sounders &lt;args&gt;</c> for <paramref name="s"/> without the admin gates and returns the reply.</summary>
    internal string SoundersAdminForTest(PlayerSession s, string args) => SoundersAdminReply(s, args);
}
