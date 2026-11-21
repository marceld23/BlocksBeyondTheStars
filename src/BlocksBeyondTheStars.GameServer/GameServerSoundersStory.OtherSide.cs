// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounders' Song after the Bridge Song (#3910) — the glue of chapters 9 to 11, which take the player to the other
/// side (<c>GameServerOtherSide*.cs</c>). The steps are data (<c>data/stories/sounders/story.json</c>); this code does
/// what data cannot:
/// <list type="bullet">
/// <item><b>The threshold</b> (s9.1): hearing that the stones are thin — from the Eldest, or from a rune stone VEGA
/// translates — writes <c>snd:threshold</c>, which shows the crossing disc's blueprint (<see cref="DeriveSoundersThreshold"/>).</item>
/// <item><b>Elora and Kamo over there</b> (s9.6, s10.1): while the step runs and the player is over there at a gate, the
/// figure stands there for them (an Echo figure only players over there see, <see cref="SpawnOtherSideEcho"/>), carrying
/// the character id of the pack's dialog (<c>snd_elora_over</c>, <c>snd_kamo_over</c>). It goes a few seconds after the
/// talk, and when the player leaves or returns. Players at the same gate share one figure.</item>
/// <item><b>The choir</b> (chapter 11): while a player in chapter 11 is over there near the song's site — the Choir
/// Tower's foot on Soaniri, the gate they came through anywhere else — six to eight Sounders who crossed over stand
/// around it (<c>GameServerSoundersStory.Choir.cs</c>). A tone on the melody bar there starts the Song from Over There;
/// solved, the great crack closes (<c>snd:great_crack</c>, a mend with <see cref="GreatCrackReach"/>) and the Lights
/// gather.</item>
/// </list>
/// Everything is per player and only while the pack runs for them. Nothing here can fail a player: no figure is needed
/// for longer than its step, a wrong tone never counts against anybody, and every step can be skipped.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Chapter 9's first step and its stand-in: hearing that the stones are thin.</summary>
    private const string SoundersThinStep = "s9.1";

    /// <summary>See <see cref="SoundersThinStep"/>: the rune stone VEGA translates.</summary>
    private const string SoundersThinStandIn = "s9.1a";

    /// <summary>Chapter 9's last step: Elora at the gate, over there.</summary>
    private const string SoundersEloraStep = "s9.6";

    /// <summary>Chapter 10's first step: Kamo at a gate, over there.</summary>
    private const string SoundersKamoStep = "s10.1";

    /// <summary>Chapter 11's first step: cross on Soaniri, at the Choir Tower.</summary>
    private const string SoundersCrossAtTowerStep = "s11.1";

    /// <summary>Its stand-in when Soaniri is out of reach: cross at any gate.</summary>
    private const string SoundersCrossAnywhereStep = "s11.1a";

    /// <summary>Chapter 11's song with the choir.</summary>
    private const string SoundersChoirStep = "s11.2";

    /// <summary>The pack's dialogs of the two figures (their end lets the figure go).</summary>
    private const string EloraOverDialog = "snd_elora_over";

    /// <summary>See <see cref="EloraOverDialog"/>.</summary>
    private const string KamoOverDialog = "snd_kamo_over";

    /// <summary>The entry gate counts as "the gate the player stands in" this close to it (blocks) — no scan needed.</summary>
    private const float OverEchoEntryRange = 12f;

    /// <summary>Players this close to a standing figure of the same character share it (blocks).</summary>
    private const float OverEchoShareRange = 16f;

    /// <summary>A figure of the other side that stands for one player's dialog step.</summary>
    private sealed class OverEcho
    {
        public string Character = string.Empty;
        public ServerNpc Npc = null!;
        public string LocationId = string.Empty;
        public double FadeAt;
    }

    private readonly Dictionary<string, OverEcho> _overEchoes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _overEchoScanAt = new(StringComparer.Ordinal);

    /// <summary>Hooks chapters 9–11 into the melody puzzles and the event bus — once, from <see cref="WireSoundersStory"/>.</summary>
    private void WireSoundersOtherSide()
    {
        RegisterMelodyFreePlayStarter(OverChoirStarter); // before the cracks: at the song's site the choir answers first
        RegisterMelodySolvedHandler(SoundersIds.PuzzleSongFromOverThere, OnSongFromOverThereSolved);
        WireOtherSideCracks();
        AddGameEventListener(GameEventKeys.RunePlayed, _soundersListener!);
    }

    // ---------------- Conditions ----------------

    /// <summary><c>world:in_ornemis</c>: the player is in the Sounders' home system.</summary>
    private bool InOrnemis(PlayerSession s)
        => string.Equals(ResolveLocationHostBody(s.CurrentLocationId)?.SystemId, SoundersIds.SystemId, StringComparison.Ordinal);

    /// <summary><c>world:soaniri_reachable</c>: Soaniri is in the galaxy for this player and they can get there.</summary>
    private bool SoaniriReachable(PlayerSession s)
        => !PackSystemHiddenFor(s, SoundersIds.SystemId) && CanReachBody(s, StoryPackSystem.BodyIdOf(SoundersIds.SystemId));

    /// <summary>Whether step <paramref name="stepId"/> of the Sounders pack runs for <paramref name="s"/> right now.</summary>
    private bool SoundersStepRuns(PlayerSession s, string stepId)
        => SoundersPack is { } pack && pack.StepsById.TryGetValue(stepId, out var step)
           && IsStoryPackActive(s, SoundersIds.PackId) && StepActive(s, step) && !StepComplete(s, step);

    // ---------------- The threshold (s9.1) ----------------

    /// <summary>
    /// Writes <c>snd:threshold</c> once the player heard that the stones are thin — however: the Eldest's dialog writes it
    /// itself (a dialog consequence); the stand-in (a rune stone VEGA translates) is done; either step was skipped; or
    /// chapter 9 is through. From the 1 Hz story pass, so a skip and an old save heal as well. Silent: the step's own
    /// line said it.
    /// </summary>
    private void DeriveSoundersThreshold(PlayerSession s)
    {
        var m = s.State.Milestones;
        if (m.Contains(SoundersIds.MilestoneThreshold) || !IsStoryPackActive(s, SoundersIds.PackId) || SoundersPack is not { } pack)
        {
            return;
        }

        string id = SoundersIds.PackId;
        bool standInWaits = pack.StepsById.TryGetValue(SoundersThinStandIn, out var standIn) && StepActive(s, standIn);
        bool due = m.Contains(StoryPackKeys.StepDone(id, SoundersThinStep))
                   || m.Contains(StoryPackKeys.StepDone(id, SoundersThinStandIn))
                   || m.Contains(StoryPackKeys.StepSkipped(id, SoundersThinStandIn))
                   || (m.Contains(StoryPackKeys.StepSkipped(id, SoundersThinStep)) && !standInWaits)
                   || SoundersChapterThrough(m, "s9");
        if (due)
        {
            SetSoundersMilestone(s, SoundersIds.MilestoneThreshold);
        }
    }

    /// <summary>A rune stone was played: while the stand-in of s9.1 runs (or was just completed by this very stone — the
    /// order of the listeners is not ours), VEGA's translation shows the blueprint at once.</summary>
    private void OnSoundersRunePlayed(PlayerSession s)
    {
        if (!IsStoryPackActive(s, SoundersIds.PackId) || SoundersPack is not { } pack
            || !pack.StepsById.TryGetValue(SoundersThinStandIn, out var standIn))
        {
            return;
        }

        if (StepActive(s, standIn) || s.State.Milestones.Contains(StoryPackKeys.StepDone(SoundersIds.PackId, SoundersThinStandIn)))
        {
            SetSoundersMilestone(s, SoundersIds.MilestoneThreshold);
        }
    }

    // ---------------- Steps the world's state completes ----------------

    /// <summary>
    /// One pass a second per player (from <see cref="TickSoundersStory"/>): two steps whose event may lie behind the player
    /// when the step starts. The stand-in of s9.1 (a rune stone) is through, silently, for a player who heard it from the
    /// Eldest after all; and chapter 11's first step ("cross at the tower" — or, as its stand-in, anywhere) is through for
    /// a player who is over there already, so nobody has to go back and cross again.
    /// </summary>
    private void TickSoundersStepEvidence(PlayerSession s)
    {
        if (s.State.Milestones.Contains(SoundersIds.MilestoneThreshold) && SoundersStepRuns(s, SoundersThinStandIn))
        {
            CompleteSoundersStep(s, SoundersThinStandIn, silent: true);
        }

        if (!s.OtherSide)
        {
            return;
        }

        bool onSoaniri = string.Equals(_world.LocationId, StoryPackSystem.BodyIdOf(SoundersIds.SystemId), StringComparison.Ordinal);
        if (onSoaniri && SoundersStepRuns(s, SoundersCrossAtTowerStep))
        {
            CompleteSoundersStep(s, SoundersCrossAtTowerStep, silent: false);
        }
        else if (SoundersStepRuns(s, SoundersCrossAnywhereStep))
        {
            CompleteSoundersStep(s, SoundersCrossAnywhereStep, silent: false);
        }
    }

    /// <summary>Completes a running step of the pack on the glue's own evidence and walks the pack on, as an event would.</summary>
    private void CompleteSoundersStep(PlayerSession s, string stepId, bool silent)
    {
        if (SoundersPack is not { } pack || !pack.StepsById.TryGetValue(stepId, out var step) || !StepActive(s, step) || StepComplete(s, step))
        {
            return;
        }

        CompleteStep(s, step, silent);
        AdvancePack(s, pack);
        SyncStepListeners();
        SendVegaObjective(s);
    }

    // ---------------- Elora and Kamo over there (s9.6, s10.1) ----------------

    /// <summary>One pass a second per player of the active world (from <see cref="TickSoundersStory"/>): the figure of the
    /// player's dialog step stands at their gate while they are over there, and goes when it is no longer theirs.</summary>
    private void TickSoundersOverEcho(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        string? wanted = !s.OtherSide ? null
            : SoundersStepRuns(s, SoundersEloraStep) ? SoundersIds.EloraOver
            : SoundersStepRuns(s, SoundersKamoStep) ? SoundersIds.KamoOver
            : null;
        if (_overEchoes.TryGetValue(pid, out var echo))
        {
            bool here = string.Equals(echo.LocationId, _world.LocationId, StringComparison.Ordinal);
            bool gone = !here || !_npcs.Contains(echo.Npc)
                        || (echo.FadeAt > 0 ? _uptime >= echo.FadeAt : !string.Equals(wanted, echo.Character, StringComparison.Ordinal))
                        || WrapDistSq(s.State.Position, echo.Npc.Pos) > EchoLeaveDistance * EchoLeaveDistance;
            if (gone)
            {
                ReleaseOverEcho(pid, echo);
            }

            return;
        }

        if (wanted is null || (_overEchoScanAt.TryGetValue(pid, out double at) && _uptime < at))
        {
            return;
        }

        _overEchoScanAt[pid] = _uptime + EchoScanSeconds;
        var pos = s.State.Position;
        Vector3f gate;
        if (EntryGateOf(s) is { } entry && WrapDistSq(entry, pos) <= OverEchoEntryRange * OverEchoEntryRange)
        {
            gate = entry;
        }
        else if (!IsGate(pos, out gate))
        {
            return; // not at a gate: the figure waits at the stones
        }

        PlaceOverEcho(s, wanted, gate);
    }

    /// <summary>Lets the player's figure stand at <paramref name="gate"/> — the one another player already has there, or a
    /// new one beside the stones, turned to the player.</summary>
    private void PlaceOverEcho(PlayerSession s, string character, Vector3f gate)
    {
        string location = _world.LocationId;
        foreach (var other in _overEchoes.Values)
        {
            if (other.Character == character && other.LocationId == location && other.FadeAt <= 0 && _npcs.Contains(other.Npc)
                && WrapDistSq(other.Npc.Pos, gate) <= OverEchoShareRange * OverEchoShareRange)
            {
                _overEchoes[s.State.PlayerId] = new OverEcho { Character = character, Npc = other.Npc, LocationId = location };
                return;
            }
        }

        var p = s.State.Position;
        double toPlayer = Math.Atan2(p.Z - gate.Z, p.X - gate.X);
        var spot = FolkSpot(gate, toPlayer + 0.7) ?? new Vector3f(p.X + 2.5f, p.Y, p.Z);
        string name = character == SoundersIds.EloraOver ? "elora" : "kamo";
        int id = SpawnOtherSideEcho(name, spot); // named and dressed as the Echo of chapter 5
        var npc = _npcs.First(n => n.Id == id);
        npc.CharacterId = character; // the dialog of the other side, not the Echo's of chapter 5
        _overEchoes[s.State.PlayerId] = new OverEcho { Character = character, Npc = npc, LocationId = location };
    }

    /// <summary>The figure is no longer this player's; it leaves ITS world when it is nobody's — whichever world is under
    /// the cursor: the player may have changed worlds, or be leaving the game, while others keep that world resident. A
    /// world that was unloaded took its NPCs with it.</summary>
    private void ReleaseOverEcho(string playerId, OverEcho echo)
    {
        _overEchoes.Remove(playerId);
        if (_overEchoes.Values.Any(e => ReferenceEquals(e.Npc, echo.Npc)))
        {
            return; // somebody else still talks to it
        }

        if (_worlds.Find(echo.LocationId) is { } world && world.Npcs.Remove(echo.Npc))
        {
            world.NpcListDirty = true; // goes out with that world's next flush
        }
    }

    /// <summary>The player leaves the game (from the leave step of the other side, whichever side they are on): their
    /// figure does not wait at the gate for good. The per-second pass only sees joined players.</summary>
    private void ReleaseOverEchoOnLeave(PlayerSession s)
    {
        string pid = s.State.PlayerId;
        _overEchoScanAt.Remove(pid);
        if (_overEchoes.TryGetValue(pid, out var echo))
        {
            ReleaseOverEcho(pid, echo);
        }
    }

    /// <summary>Whether the per-second story pass looks at this player (<see cref="TickSoundersStory"/>).</summary>
    private bool SoundersPassReaches(PlayerSession s)
        => !s.Spectating && !InSpace(s.State.PlayerId) && IsStoryPackActive(s, SoundersIds.PackId);

    /// <summary>
    /// Once a second per world, after the players' own pass: a figure of this world whose player that pass no longer
    /// reaches — they left the game, changed worlds, fly, watch, or switched the pack off — is released, and the entries
    /// of a world that left memory are dropped (its NPCs went with it). Without it such a figure stood at its gate until
    /// the world was unloaded. One count read while nobody has a figure.
    /// </summary>
    private void SweepOverEchoes()
    {
        if (_overEchoes.Count == 0)
        {
            return;
        }

        string here = _world.LocationId;
        foreach (var (pid, echo) in _overEchoes.ToList())
        {
            bool orphan = string.Equals(echo.LocationId, here, StringComparison.Ordinal)
                ? !(FindSessionByPlayerId(pid) is { Joined: true } s && s.CurrentLocationId == here && SoundersPassReaches(s))
                : _worlds.Find(echo.LocationId) is null;
            if (orphan)
            {
                ReleaseOverEcho(pid, echo);
            }
        }
    }

    /// <summary>The end of a dialog of the other side: the figure stays a moment (the last answer is still being read).</summary>
    private void OnSoundersOverDialogDone(PlayerSession s, string dialogKey)
    {
        string? character = dialogKey switch
        {
            EloraOverDialog => SoundersIds.EloraOver,
            KamoOverDialog => SoundersIds.KamoOver,
            _ => null,
        };
        if (character is not null && _overEchoes.TryGetValue(s.State.PlayerId, out var echo) && echo.Character == character)
        {
            // Its own entry fades; a figure shared with another player stays for them.
            _overEchoes[s.State.PlayerId] = new OverEcho
            {
                Character = echo.Character,
                Npc = echo.Npc,
                LocationId = echo.LocationId,
                FadeAt = _uptime + EchoFadeSeconds,
            };
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the character id of the figure standing for <paramref name="playerId"/> over there, or null.</summary>
    internal string? SoundersOverEchoForTest(string playerId)
        => _overEchoes.TryGetValue(playerId, out var e) ? e.Character : null;

    /// <summary>Test seam: the NPC id of that figure, or -1.</summary>
    internal int SoundersOverEchoNpcForTest(string playerId)
        => _overEchoes.TryGetValue(playerId, out var e) ? e.Npc.Id : -1;
}
