// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The singing plants as puzzle helpers (#3367, the Sounders story pack), on a Sounder world. One entry point,
/// <see cref="HearSingingPlants"/>, takes every tone a player plays there (free play on the melody bar — the sound pack's
/// free-play branch calls it — and every struck resonance crystal):
/// <list type="bullet">
/// <item>an <b>echo flower</b> within <see cref="SingingPlantHearing"/> blocks answers: when the player has been quiet for
/// <see cref="EchoFlowerDelay"/> seconds it sings back the last ≤ <see cref="EchoFlowerMaxNotes"/> tones from the flower, in the
/// Sounder voice (a <c>NoteSequence</c>, source <c>echo_flower</c>);</item>
/// <item>a <b>phrase of the Song of Return</b> (<see cref="SoundersIds.SongOfReturnParts"/> in the note alphabet) opens the bell
/// blooms around the player — the wild ones close again after <see cref="WildBloomOpenSeconds"/> seconds;</item>
/// <item>the <b>bell-bloom trail</b> of Ornasa opens one bloom per phrase, in order (the next closed one, when the player stands
/// near it), and stays open; when its last bloom opens, the trail's sound chamber opens and <c>melody:solved</c> is raised with
/// <see cref="SoundersIds.PuzzleBloomTrail"/>. The trail's state is its blocks, so nothing else is saved.</item>
/// </list>
/// Wrong notes never punish: a phrase that does not fit simply does nothing. The plants' hum and their silence near a
/// Soundrak are the client's (<c>SingingPlantChorus</c>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A singing plant hears the tones played this close (blocks).</summary>
    internal const float SingingPlantHearing = 8f;

    /// <summary>How long the player must be quiet before an echo flower answers (seconds).</summary>
    internal const double EchoFlowerDelay = 1.2;

    /// <summary>An echo flower repeats at most this many tones.</summary>
    internal const int EchoFlowerMaxNotes = 8;

    /// <summary>A wild bell bloom closes again after this long (seconds); the trail's blooms stay open.</summary>
    internal const double WildBloomOpenSeconds = 45.0;

    /// <summary>The last tones remembered per player for the Song of Return's phrases.</summary>
    private const int SingingEarMemory = 16;

    /// <summary>What the plants heard from one player.</summary>
    private sealed class SingingEar
    {
        public readonly List<int> Heard = new();
        public readonly List<int> Pending = new();
        public string LocationId = string.Empty;
        public double EchoAt = double.MaxValue;
        public Vector3i EchoFlower;
    }

    private readonly Dictionary<string, SingingEar> _singingEars = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Location, Vector3i Cell), double> _wildBloomsCloseAt = new();
    private readonly Dictionary<string, Vector3i[]> _bloomTrails = new(StringComparer.Ordinal);
    private double _singingClock;

    /// <summary>The Song of Return's phrases as the tones a player plays (rests are timing, not input).</summary>
    private static readonly int[][] SongOfReturnPhrases = Array.ConvertAll(SoundersIds.SongOfReturnParts,
        part => SounderAlphabet.TonesOnly(SounderAlphabet.Encode(part)));

    /// <summary>
    /// A player played <paramref name="tone"/> (0–7) on <paramref name="instrument"/> on a Sounder world: the singing plants
    /// near them listen (see the class summary). The sound pack's free-play branch of the melody bar and the resonance crystal's
    /// use call it; anything else that plays a tone for a player may too. A no-op anywhere but a Sounder world.
    /// </summary>
    internal void HearSingingPlants(PlayerSession session, int tone, int instrument)
    {
        if (!IsSounderWorld || tone < 0 || tone >= SounderAlphabet.ToneCount || session.Spectating || InSpace(session.State.PlayerId))
        {
            return;
        }

        var ear = SingingEarOf(session);
        Remember(ear.Heard, tone, SingingEarMemory);
        Remember(ear.Pending, tone, EchoFlowerMaxNotes);
        if (EndsWithSongPhrase(ear.Heard))
        {
            ear.Heard.Clear();
            OpenBellBloomsNear(session);
        }

        if (NearestSingingPlant(session.State.Position, FloraCatalog.EchoFlowerKey) is { } flower)
        {
            ear.EchoFlower = flower;
            ear.EchoAt = _singingClock + EchoFlowerDelay;
        }
        else
        {
            ear.Pending.Clear();
            ear.EchoAt = double.MaxValue;
        }
    }

    private SingingEar SingingEarOf(PlayerSession session)
    {
        string id = session.State.PlayerId;
        if (!_singingEars.TryGetValue(id, out var ear))
        {
            _singingEars[id] = ear = new SingingEar();
        }

        if (ear.LocationId != _world.LocationId)
        {
            ear.Heard.Clear();
            ear.Pending.Clear();
            ear.EchoAt = double.MaxValue;
            ear.LocationId = _world.LocationId;
        }

        return ear;
    }

    private static void Remember(List<int> tones, int tone, int cap)
    {
        tones.Add(tone);
        if (tones.Count > cap)
        {
            tones.RemoveAt(0);
        }
    }

    /// <summary>Whether the remembered tones end with one of the Song of Return's phrases.</summary>
    internal static bool EndsWithSongPhrase(IReadOnlyList<int> heard)
    {
        foreach (var phrase in SongOfReturnPhrases)
        {
            if (phrase.Length == 0 || heard.Count < phrase.Length)
            {
                continue;
            }

            bool match = true;
            for (int i = 0; i < phrase.Length && match; i++)
            {
                match = heard[heard.Count - phrase.Length + i] == phrase[i];
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Per occupied world: the echoes that are due (the player was quiet long enough) and the wild blooms whose time
    /// is up.</summary>
    private void TickSingingPlants(double dt)
    {
        _singingClock += dt;
        if (_singingEars.Count == 0 && _wildBloomsCloseAt.Count == 0)
        {
            return;
        }

        string loc = _world.LocationId;
        foreach (var ear in _singingEars.Values)
        {
            if (ear.LocationId != loc || ear.EchoAt > _singingClock)
            {
                continue;
            }

            ear.EchoAt = double.MaxValue;
            if (ear.Pending.Count > 0 && _content.BlockById(_world.GetBlockIfLoaded(ear.EchoFlower))?.Key == FloraCatalog.EchoFlowerKey)
            {
                var f = ear.EchoFlower;
                PlayNoteSequence(new Vector3f(f.X + 0.5f, f.Y + 0.8f, f.Z + 0.5f), ear.Pending.ToArray(), SoundersIds.InstrumentVoice,
                    0.42f, withFifth: false, source: "echo_flower");
            }

            ear.Pending.Clear();
        }

        CloseWildBlooms(loc);
    }

    private void CloseWildBlooms(string loc)
    {
        if (_wildBloomsCloseAt.Count == 0)
        {
            return;
        }

        var due = new List<(string Location, Vector3i Cell)>();
        foreach (var (key, at) in _wildBloomsCloseAt)
        {
            if (key.Location == loc && at <= _singingClock)
            {
                due.Add(key);
            }
        }

        var closed = SounderBlock(FloraCatalog.BellBloomKey);
        foreach (var key in due)
        {
            _wildBloomsCloseAt.Remove(key);
            if (_content.BlockById(_world.GetBlockIfLoaded(key.Cell))?.Key == FloraCatalog.BellBloomOpenKey && !closed.IsAir)
            {
                SetSounderBlockLive(key.Cell, closed);
            }
        }
    }

    // ---------------- Bell blooms and the trail ----------------

    /// <summary>A phrase of the Song of Return near <paramref name="session"/>'s player: the trail's next bloom (when near)
    /// opens and stays open; every wild bell bloom in hearing opens for a while.</summary>
    private void OpenBellBloomsNear(PlayerSession session)
    {
        var open = SounderBlock(FloraCatalog.BellBloomOpenKey);
        if (open.IsAir)
        {
            return;
        }

        var pos = session.State.Position;
        AdvanceBloomTrail(session, pos, open);
        var trail = _bloomTrails.TryGetValue(_world.LocationId, out var t) ? t : Array.Empty<Vector3i>();
        foreach (var cell in SingingPlantsNear(pos, FloraCatalog.BellBloomKey))
        {
            if (Array.IndexOf(trail, cell) >= 0)
            {
                continue; // the trail opens in order, above
            }

            SetSounderBlockLive(cell, open);
            _wildBloomsCloseAt[(_world.LocationId, cell)] = _singingClock + WildBloomOpenSeconds;
        }
    }

    /// <summary>Registers Ornasa's trail (every load) — and if every bloom of it is open but its chamber is not (a crash between
    /// the two writes), opens the chamber.</summary>
    private void RegisterBloomTrail(int cx, int floorY, int cz)
    {
        var cells = new Vector3i[SounderSiteLayout.BloomTrailLength];
        bool allOpen = true;
        for (int k = 0; k < cells.Length; k++)
        {
            cells[k] = WorldConstants.CanonicalBlock(SounderSiteLayout.BloomTrailCell(cx, floorY, cz, k), _world.Circumference);
            allOpen &= _content.BlockById(_world.GetBlock(cells[k]))?.Key == FloraCatalog.BellBloomOpenKey;
        }

        _bloomTrails[_world.LocationId] = cells;
        if (allOpen)
        {
            OpenOrnasaChamber(null, SounderSiteLayout.BloomTrailChamber, byBlaster: false);
        }
    }

    /// <summary>A runtime change of a Soaniri block (a bloom opening or closing, a chamber's seal falling): written and sent
    /// to the world's players, so a client that already streamed the chunk sees it.</summary>
    private void SetSounderBlockLive(Vector3i cell, BlockId id)
    {
        _world.SetBlock(cell, id);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = id.Value });
    }

    /// <summary>The index of the trail's first still-closed bloom (a bloom someone picked counts as open — the trail never
    /// gets stuck), or -1 when every one is open.</summary>
    private int NextClosedTrailBloom(Vector3i[] trail)
    {
        for (int k = 0; k < trail.Length; k++)
        {
            if (_content.BlockById(_world.GetBlock(trail[k]))?.Key == FloraCatalog.BellBloomKey)
            {
                return k;
            }
        }

        return -1;
    }

    private void AdvanceBloomTrail(PlayerSession session, Vector3f pos, BlockId open)
    {
        if (!_bloomTrails.TryGetValue(_world.LocationId, out var trail))
        {
            return;
        }

        int next = NextClosedTrailBloom(trail);
        if (next < 0)
        {
            return;
        }

        var cell = trail[next];
        if (WrapDistSq(pos, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f)) > (double)SingingPlantHearing * SingingPlantHearing)
        {
            return; // sing it where the next bloom can hear you
        }

        SetSounderBlockLive(cell, open);
        if (NextClosedTrailBloom(trail) < 0)
        {
            OpenOrnasaChamber(session, SounderSiteLayout.BloomTrailChamber, byBlaster: false);
            RaiseGameEvent(session, GameEventKeys.MelodySolved, SoundersIds.PuzzleBloomTrail);
            SendVegaLine(session, "vega.sys.bloom_trail", 3);
        }
    }

    // ---------------- Finding plants ----------------

    /// <summary>The cells of plant <paramref name="key"/> within hearing of <paramref name="pos"/> (loaded chunks only).</summary>
    private List<Vector3i> SingingPlantsNear(Vector3f pos, string key)
    {
        var found = new List<Vector3i>();
        var id = SounderBlock(key);
        if (id.IsAir)
        {
            return found;
        }

        int r = (int)SingingPlantHearing;
        int px = (int)Math.Floor(pos.X), py = (int)Math.Floor(pos.Y), pz = (int)Math.Floor(pos.Z);
        double r2 = (double)SingingPlantHearing * SingingPlantHearing;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if ((dx * dx) + (dz * dz) > r2)
                {
                    continue;
                }

                for (int dy = -3; dy <= 3; dy++)
                {
                    var cell = WorldConstants.CanonicalBlock(new Vector3i(px + dx, py + dy, pz + dz), _world.Circumference);
                    if (_world.GetBlockIfLoaded(cell) == id)
                    {
                        found.Add(cell);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The nearest plant <paramref name="key"/> within hearing, or null.</summary>
    private Vector3i? NearestSingingPlant(Vector3f pos, string key)
    {
        Vector3i? best = null;
        double bestSq = double.MaxValue;
        foreach (var cell in SingingPlantsNear(pos, key))
        {
            double d = WrapDistSq(pos, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
            if (d < bestSq)
            {
                bestSq = d;
                best = cell;
            }
        }

        return best;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: advances the singing plants' clock and runs their tick on the active world.</summary>
    internal void TickSingingPlantsForTest(double dt) => TickSingingPlants(dt);

    /// <summary>Test/inspection: the active world's bloom trail cells (empty without one).</summary>
    internal IReadOnlyList<Vector3i> BloomTrailForTest
        => _bloomTrails.TryGetValue(_world.LocationId, out var t) ? t : Array.Empty<Vector3i>();
}
