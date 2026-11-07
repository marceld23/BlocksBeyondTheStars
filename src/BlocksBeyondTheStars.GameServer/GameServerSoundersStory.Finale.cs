// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The finale of the Sounders' Song (#3360, #3361): while chapter 8 runs and a player comes near the Choir Tower, Roll and
/// the Young Voices gather on its top platform (Sounder NPCs; Roll carries the character id <c>roll</c>, so the pack's
/// persuasion dialog <c>snd_roll</c> answers) and the tower's scene plays once. The Bridge Song (the harmony puzzle the
/// site starter hands out once Roll was convinced) calls the Qualis: a few drift in above the tower. On Soaniri, from
/// chapter 7 on, a curious Soundi keeps near the player; once the Bridge Song is sung, walking up to it starts the sing duel
/// (call and response) — won, it becomes the player's travelling companion, named by the player ("Muffin" suggested).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The Young Voices gather when a player in chapter 8 is this close to the tower (blocks).</summary>
    private const float ChoirGatherDistance = 64f;

    /// <summary>The story Soundi starts its sing duel when the player walks this close (blocks).</summary>
    private const float SingDuelDistance = 4.5f;

    /// <summary>The story Soundi is put back near the player when it strays farther than this (blocks).</summary>
    private const float StorySoundiLeash = 18f;

    /// <summary>How many Young Voices besides Roll.</summary>
    private const int YoungVoiceCount = 4;

    private readonly List<int> _youngVoices = new();
    private string _youngVoicesLocation = string.Empty;
    private readonly Dictionary<string, string> _storySoundis = new(StringComparer.Ordinal);
    private double _soundersStoryTickAt;

    /// <summary>One pass a second over the active world's players: Echoes, the tower, the story Soundi.</summary>
    private void TickSoundersStory()
    {
        if (_uptime < _soundersStoryTickAt)
        {
            return;
        }

        _soundersStoryTickAt = _uptime + 1.0;
        if (!string.Equals(_youngVoicesLocation, _world.LocationId, StringComparison.Ordinal))
        {
            _youngVoices.Clear(); // NPCs are per world: another world is active now
            _youngVoicesLocation = _world.LocationId;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Spectating || InSpace(s.State.PlayerId) || !IsStoryPackActive(s, SoundersIds.PackId))
            {
                continue;
            }

            TickSoundersEchoes(s);
            TickChoirTower(s);
            TickStorySoundi(s);
        }
    }

    /// <summary>Chapter 8 near the tower: the Young Voices gather and the tower's scene plays once.</summary>
    private void TickChoirTower(PlayerSession s)
    {
        if (!InSoundersChapter(s, "s8") || FindMelodySite(_world.LocationId, ChoirTowerSiteKey) is not { } tower
            || WrapDistSq(s.State.Position, tower.CentreF) > ChoirGatherDistance * ChoirGatherDistance)
        {
            return;
        }

        EnsureYoungVoices(tower);
        if (SetSoundersMilestone(s, "snd:tower_seen"))
        {
            PlayCutscene(s, SoundersIds.PackId, "snd.choir_tower", anchor: tower.CentreF);
        }
    }

    /// <summary>Roll in the middle of the platform, the others in a ring inside the pipes — once per world load.</summary>
    private void EnsureYoungVoices(MelodySite tower)
    {
        if (_youngVoices.Count > 0)
        {
            return;
        }

        var c = tower.CentreF;
        _youngVoices.Add(SpawnSounderNpc(SoundersIds.Sounder, new Vector3f(c.X, c.Y, c.Z), "Roll", "roll", leash: 1.0f));
        for (int i = 0; i < YoungVoiceCount; i++)
        {
            double angle = (i + 0.5) * Math.PI * 2 / YoungVoiceCount;
            var pos = new Vector3f(c.X + (float)(Math.Sin(angle) * 2.2), c.Y, c.Z + (float)(Math.Cos(angle) * 2.2));
            _youngVoices.Add(SpawnSounderNpc(SoundersIds.Sounder, pos, string.Empty, string.Empty, leash: 0.8f, size: 0.9f));
        }
    }

    /// <summary>The Bridge Song was played: the Qualis come, calm and glowing, and the milestone holds the moment.</summary>
    private void OnBridgeSongSolved(PlayerSession s)
    {
        if (!IsStoryPackActive(s, SoundersIds.PackId) || !SetSoundersMilestone(s, SoundersBridgeDoneMilestone))
        {
            return;
        }

        if (FindMelodySite(_world.LocationId, ChoirTowerSiteKey) is { } tower)
        {
            var c = tower.CentreF;
            SpawnAuthoredCreature(SoundersIds.Quali, new Vector3f(c.X + 12f, c.Y + 18f, c.Z));
            SpawnAuthoredCreature(SoundersIds.Quali, new Vector3f(c.X - 9f, c.Y + 22f, c.Z + 8f));
        }
    }

    /// <summary>On Soaniri in chapters 7 and 8 a curious Soundi keeps near the player; after the Bridge Song, walking up to it
    /// starts the sing duel.</summary>
    private void TickStorySoundi(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        bool wanted = string.Equals(_worlds.Active.PlanetType, SoundersIds.PlanetType, StringComparison.Ordinal)
                      && (InSoundersChapter(s, "s7") || InSoundersChapter(s, "s8"))
                      && !s.State.Milestones.Contains(SoundersSoundiWonMilestone);
        var soundi = _storySoundis.TryGetValue(pid, out string? id) ? _creatures.Find(c => c.Id == id) : null;
        if (!wanted)
        {
            return;
        }

        if (soundi is null)
        {
            var p = s.State.Position;
            if (SpawnAuthoredCreature(SoundersIds.Soundi, new Vector3f(p.X + 7f, p.Y + 1f, p.Z + 5f)) is { } spawned)
            {
                _storySoundis[pid] = spawned;
            }

            return;
        }

        double d2 = WrapDistSq(s.State.Position, soundi.Position);
        if (d2 > StorySoundiLeash * StorySoundiLeash)
        {
            var p = s.State.Position;
            soundi.Position = new Vector3f(p.X + 6f, p.Y + 1f, p.Z + 4f); // it was following all along
            return;
        }

        if (d2 <= SingDuelDistance * SingDuelDistance && s.State.Milestones.Contains(SoundersBridgeDoneMilestone)
            && InSoundersChapter(s, "s8") && RunningMelodyPuzzle(s) is null)
        {
            StartSingDuel(s, soundi.Position);
        }
    }

    /// <summary>The Soundi sings first; the player answers each call with the same notes.</summary>
    private void StartSingDuel(PlayerSession s, Vector3f at)
    {
        var spec = new MelodyPuzzleSpec
        {
            Id = SoundersIds.PuzzleSingDuel,
            Mode = MelodyPuzzleMode.CallAndResponse,
            Calls = SingDuelCalls,
            Instrument = SoundersIds.InstrumentVoice,
            NoteSeconds = 0.4f,
            IdleSeconds = 25f,
        };
        if (StartMelodyPuzzle(s, spec))
        {
            int call = 1 + ((int)(_uptime * 7.0) % SoundiCallCount);
            Send(s, new SoundFx
            {
                SoundId = SoundiCallPrefix + call.ToString("00", CultureInfo.InvariantCulture),
                X = at.X,
                Y = at.Y + 0.5f,
                Z = at.Z,
            });
        }
    }

    /// <summary>The sing duel was won: the story Soundi joins the player (named by them, "Muffin" suggested) and the wild one
    /// that followed them leaves the world — it is the same animal, now a companion.</summary>
    private void OnSingDuelSolved(PlayerSession s)
    {
        if (!IsStoryPackActive(s, SoundersIds.PackId) || !SetSoundersMilestone(s, SoundersSoundiWonMilestone))
        {
            return;
        }

        if (_storySoundis.TryGetValue(s.State.PlayerId, out string? id))
        {
            _creatures.RemoveAll(c => c.Id == id);
            _storySoundis.Remove(s.State.PlayerId);
            BroadcastCreatures();
        }

        GrantStoryCompanion(s, TamedCreature.SoundiKind, SoundersIds.Soundi, "Muffin");
    }

    // --- Test seams --------------------------------------------------------------------------------------------------------

    /// <summary>Test seam: one pass of the story tick now (skips the 1 s throttle).</summary>
    internal void TickSoundersStoryForTest()
    {
        _soundersStoryTickAt = 0;
        TickSoundersStory();
    }

    /// <summary>Test seam: the id of the story Soundi near <paramref name="playerId"/>, or null.</summary>
    internal string? StorySoundiForTest(string playerId) => _storySoundis.TryGetValue(playerId, out string? id) ? id : null;

    /// <summary>Test seam: how many Young Voices (Roll included) stand on the tower.</summary>
    internal int YoungVoiceCountForTest => _youngVoices.Count;
}
