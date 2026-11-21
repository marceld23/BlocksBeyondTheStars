// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The cracks of the other side (#3909, epic #3891): "every wall we sang here cracked a wall over there". Every rune gate
/// (three or more rune stones close together, <see cref="IsGate"/>) carries a crack over there until somebody mends it
/// with a short counter-voice game. How it is built: <c>docs/developer/OTHER_SIDE.md</c> § "The cracks".
/// <list type="bullet">
/// <item><b>The state</b> is per world and saved: <c>WorldMetadata.MendedCracks</c>, one key per mended gate — its world,
/// the rounded middle of its stones and the reach of the mend. A gate whose middle lies within a mended crack's reach is
/// whole. The gate list's hook <see cref="OtherSideGateCracked"/> reads it.</item>
/// <item><b>Mending never blocks the way home</b>: the use key on a stone of a cracked gate still brings the player back.
/// The mend game starts from the melody bar instead — a tone played over there while standing in a cracked gate
/// (<see cref="RegisterMelodyFreePlayStarter"/>): the gate sings a three-tone motif made from its place, then the motif
/// turned round; the player answers each with the counter-voice, the key to press next glowing on the bar. A wrong tone
/// does not count (#3678). Players at the same gate play together (the puzzle framework's co-op rule).</item>
/// <item><b>Mended</b>: the key is saved, the cue <see cref="CrackMendSound"/> plays at the gate for the players over
/// there, every player over there within <see cref="CrackMendRange"/> blocks gets <c>crack:mended</c> (argument: the
/// key) and the milestone <c>snd:crack:&lt;location&gt;:&lt;n&gt;</c>, and the gate lists go out again.</item>
/// <item><b>The real side blooms</b> (<c>GameServerOtherSideCracks.Bloom.cs</c>) — only at a gate the world itself
/// carries (a monument, a rune shrine, a ruin, the Singing Shrine, the Choir Tower): three stones a player placed mend
/// like any gate, but give nothing, so nobody farms gifts with three stones.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A mended crack covers the gates whose middle lies this close to it (blocks) — the gate's own stones, and
    /// the same gate after a stone was added or mined.</summary>
    internal const float CrackGateReach = 12f;

    /// <summary>The great crack (the Sounders' chapter 11) covers every gate this close to its site (blocks).</summary>
    internal const float GreatCrackReach = 64f;

    /// <summary>A player over there this close to a gate that is mended took part in the mend (blocks).</summary>
    internal const float CrackMendRange = 16f;

    /// <summary>The sound cue of a mended crack (played at the gate, only for the players over there; an unknown cue is
    /// silent on the client).</summary>
    internal const string CrackMendSound = "crack_mend";

    /// <summary>Seconds a place that turned out to be no cracked gate is not searched again for one player's free tones.</summary>
    private const double CrackScanSeconds = 1.5;

    /// <summary>One entry of <c>WorldMetadata.MendedCracks</c>, parsed.</summary>
    private readonly record struct MendedCrack(string LocationId, Vector3f Centre, float Reach);

    private readonly List<MendedCrack> _mendedCracks = new();
    private int _mendedCracksParsed = -1;

    /// <summary>The gate each player's running mend game belongs to (its world and middle).</summary>
    private readonly Dictionary<string, (string LocationId, Vector3f Gate)> _crackRuns = new(StringComparer.Ordinal);

    private readonly Dictionary<string, double> _crackScanAt = new(StringComparer.Ordinal);

    /// <summary>Hooks the cracks into the melody puzzles — once, from the constructor (after the story's own starters, so
    /// the choir of chapter 11 is asked first at a gate where both would answer).</summary>
    private void WireOtherSideCracks()
    {
        RegisterMelodyFreePlayStarter(CrackMendStarter);
        RegisterMelodySolvedHandler(SoundersIds.PuzzleCrackMend, OnCrackMendSolved);
    }

    // ---------------- The state ----------------

    /// <summary>The gate list's hook (its defining declaration is in <c>GameServerOtherSide.GateList.cs</c>): a gate is
    /// cracked until it is mended.</summary>
    partial void OtherSideGateCracked(string locationId, Vector3f centre, ref bool cracked)
        => cracked = !IsCrackMended(locationId, centre);

    /// <summary>Whether the gate with its middle at <paramref name="centre"/> on <paramref name="locationId"/> is whole:
    /// a mended crack of that world reaches it.</summary>
    internal bool IsCrackMended(string locationId, Vector3f centre)
    {
        int circumference = _worlds.Find(locationId)?.World.Circumference ?? 0;
        foreach (var crack in MendedCracks())
        {
            if (!string.Equals(crack.LocationId, locationId, StringComparison.Ordinal))
            {
                continue;
            }

            double distSq = circumference > 0
                ? WorldConstants.WrapDistanceSquared(crack.Centre, centre, circumference)
                : crack.Centre.DistanceSquared(centre);
            if (distSq <= (double)crack.Reach * crack.Reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The save's mended cracks, parsed once per change of the list (it only ever grows).</summary>
    private List<MendedCrack> MendedCracks()
    {
        var saved = _meta.MendedCracks;
        if (_mendedCracksParsed == saved.Count)
        {
            return _mendedCracks;
        }

        _mendedCracks.Clear();
        foreach (string key in saved)
        {
            if (TryParseCrackKey(key, out var crack))
            {
                _mendedCracks.Add(crack);
            }
        }

        _mendedCracksParsed = saved.Count;
        return _mendedCracks;
    }

    /// <summary>The key of a mended crack: <c>&lt;location&gt;|&lt;x&gt;|&lt;y&gt;|&lt;z&gt;|&lt;reach&gt;</c>, the middle rounded to
    /// whole blocks. It is also the argument of <c>crack:mended</c>.</summary>
    internal static string CrackKey(string locationId, Vector3f centre, float reach)
        => string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", locationId,
            (long)Math.Round(centre.X), (long)Math.Round(centre.Y), (long)Math.Round(centre.Z), (long)Math.Round(reach));

    private static bool TryParseCrackKey(string key, out MendedCrack crack)
    {
        crack = default;
        string[] parts = (key ?? string.Empty).Split('|');
        if (parts.Length != 5 || parts[0].Length == 0
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            || !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float reach))
        {
            return false;
        }

        crack = new MendedCrack(parts[0], new Vector3f(x, y, z), Math.Max(1f, reach));
        return true;
    }

    // ---------------- The mend game ----------------

    /// <summary>
    /// The free-play starter: a tone of the melody bar played over there while the player stands in a cracked gate starts
    /// the mend game of that gate. Anything else passes (the tone is free play). A place that was no cracked gate is not
    /// searched again for <see cref="CrackScanSeconds"/>, so a child mashing the keys costs one scan.
    /// </summary>
    private MelodyPuzzleSpec? CrackMendStarter(PlayerSession s, int tone)
    {
        _ = tone;
        string id = s.State.PlayerId;
        if (!s.OtherSide || s.CurrentLocationId != _world.LocationId
            || (_crackScanAt.TryGetValue(id, out double until) && _uptime < until))
        {
            return null;
        }

        if (!IsGate(s.State.Position, out var gate) || IsCrackMended(_world.LocationId, gate))
        {
            _crackScanAt[id] = _uptime + CrackScanSeconds;
            return null;
        }

        _crackRuns[id] = (_world.LocationId, gate);
        return CrackMendSpec(_world.LocationId, gate);
    }

    /// <summary>
    /// The mend game of the gate at <paramref name="gate"/>: two calls — the gate's motif, then the motif turned round —
    /// each answered with its counter-voice a fifth up (the Bridge Song's voice, which breaks nothing), the key to press
    /// next glowing on the bar. Short on purpose: six tones in all.
    /// </summary>
    internal static MelodyPuzzleSpec CrackMendSpec(string locationId, Vector3f gate)
    {
        int[] motif = CrackMotif(locationId, gate);
        int[] turned = { motif[2], motif[1], motif[0] };
        const int Fifth = 4;
        return new MelodyPuzzleSpec
        {
            Id = SoundersIds.PuzzleCrackMend,
            Mode = MelodyPuzzleMode.CallAndResponse,
            Calls = new[] { motif, turned },
            Answers = new[] { MelodyPuzzle.Counter(motif, Fifth), MelodyPuzzle.Counter(turned, Fifth) },
            ShowNextTone = true,
            Instrument = SoundersIds.InstrumentChoir,
            NoteSeconds = 0.5f,
            IdleSeconds = 20f,
            LeaveDistance = CrackMendRange,
        };
    }

    /// <summary>The three tones a gate sings — from its world and the rounded middle of its stones, so the same gate
    /// always sings the same motif; no tone twice in a row.</summary>
    internal static int[] CrackMotif(string locationId, Vector3f gate)
    {
        ulong h = (ulong)WorldGenerator.StableHash("crack:" + CrackKey(locationId, gate, 0f));
        int tones = SounderAlphabet.ToneCount;
        int first = (int)(h % (ulong)tones);
        int second = (first + 1 + (int)((h >> 8) % (ulong)(tones - 1))) % tones;
        int third = (second + 1 + (int)((h >> 16) % (ulong)(tones - 1))) % tones;
        return new[] { first, second, third };
    }

    /// <summary>The mend game was solved (for each player who played it together): the crack of its gate is mended — by
    /// the first of them; the others took part through the range rule.</summary>
    private void OnCrackMendSolved(PlayerSession s)
    {
        if (_crackRuns.Remove(s.State.PlayerId, out var run) && run.LocationId == _world.LocationId && s.OtherSide)
        {
            MendCrack(run.Gate, CrackGateReach, s);
        }
    }

    // ---------------- Mending ----------------

    /// <summary>
    /// Mends the crack of the gate at <paramref name="gate"/> on the active world for good: saved, sounded, told — and, at
    /// a gate the world itself carries, answered by the real side (the bloom). <paramref name="reach"/> is how far the mend
    /// covers (<see cref="CrackGateReach"/>; <see cref="GreatCrackReach"/> for the great crack). <paramref name="solver"/>
    /// took part wherever they stand. False when the gate was whole already.
    /// </summary>
    internal bool MendCrack(Vector3f gate, float reach, PlayerSession? solver)
    {
        string location = _world.LocationId;
        bool whole = IsCrackMended(location, gate);
        string key = CrackKey(location, gate, reach);
        if ((whole && reach <= CrackGateReach) || _meta.MendedCracks.Contains(key))
        {
            return false;
        }

        // The real side answers once per gate: not again when a wider mend (the great crack) covers a gate that was whole.
        bool answers = !whole && IsWorldGate(gate);
        _meta.MendedCracks.Add(key);
        _repo.SaveMetadata(_meta);
        int number = MendedCracks().Count(c => string.Equals(c.LocationId, location, StringComparison.Ordinal));

        BroadcastToOtherSide(new SoundFx { SoundId = CrackMendSound, X = gate.X, Y = gate.Y + 1f, Z = gate.Z });
        var menders = CrackMenders(gate, solver);
        foreach (var mender in menders)
        {
            mender.State.Milestones.Add(SoundersIds.CrackMilestone(location, number));
            MarkJournalDirty(mender);
            _repo.SavePlayer(mender.State);
            Send(mender, new ServerMessage { Text = "@srv.sounders.crack_mended" });
        }

        foreach (var over in PlayersOverThere())
        {
            ResendOtherSideGates(over); // the crack is gone from every list
        }

        if (answers)
        {
            BloomRealSide(gate, key, menders);
        }

        foreach (var mender in menders)
        {
            RaiseGameEvent(mender, GameEventKeys.CrackMended, key); // last: a story step may answer with lines and scenes
        }

        _log.Info($"A crack was mended on {location} at ({gate.X:0},{gate.Y:0},{gate.Z:0}) — {menders.Count} singer(s), the real side answers: {answers}.");
        return true;
    }

    /// <summary>Who took part: the solver, and every player over there within <see cref="CrackMendRange"/> of the gate.</summary>
    private List<PlayerSession> CrackMenders(Vector3f gate, PlayerSession? solver)
    {
        var menders = new List<PlayerSession>();
        if (solver is not null)
        {
            menders.Add(solver);
        }

        foreach (var s in PlayersOverThere())
        {
            if (!ReferenceEquals(s, solver) && WrapDistSq(s.State.Position, gate) <= CrackMendRange * CrackMendRange)
            {
                menders.Add(s);
            }
        }

        return menders;
    }

    /// <summary>
    /// Whether the gate at <paramref name="gate"/> is one the world itself carries — at least
    /// <see cref="EchoRuneGroupSize"/> of its rune stones were not placed by a player (a monument's, a shrine's, a ruin's,
    /// a lone stone of the terrain, the Choir Tower's). Three stones a player placed are a gate too, but not this.
    /// </summary>
    internal bool IsWorldGate(Vector3f gate)
    {
        if (_content.GetBlock("rune_stone") is not { } rune)
        {
            return false;
        }

        int cx = (int)Math.Floor(gate.X), cy = (int)Math.Floor(gate.Y), cz = (int)Math.Floor(gate.Z);
        int own = 0;
        for (int dx = -EchoRuneScanRadius; dx <= EchoRuneScanRadius; dx++)
        {
            for (int dy = -EchoRuneScanRadius; dy <= EchoRuneScanRadius; dy++)
            {
                for (int dz = -EchoRuneScanRadius; dz <= EchoRuneScanRadius; dz++)
                {
                    var cell = new Vector3i(cx + dx, cy + dy, cz + dz);
                    if (_world.GetBlockIfLoaded(cell).Value == rune.NumericId.Value
                        && !string.Equals(RuneSiteKind(cell), RuneSitePlaced, StringComparison.Ordinal)
                        && ++own >= EchoRuneGroupSize)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: <see cref="IsCrackMended"/> for a gate of the active world.</summary>
    public bool IsCrackMendedForTest(Vector3f gate) => IsCrackMended(_world.LocationId, gate);

    /// <summary>Test seam: <see cref="MendCrack"/> with the player's world under the cursor.</summary>
    public bool MendCrackForTest(PlayerSession solver, Vector3f gate)
    {
        Serve(solver);
        return MendCrack(gate, CrackGateReach, solver);
    }

    /// <summary>Test seam: the answers of the mend game of a gate, round by round.</summary>
    public static int[][] CrackMendAnswersForTest(string locationId, Vector3f gate) => CrackMendSpec(locationId, gate).Answers;
}
