// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Chapter 11 of the Sounders' Song (#3910), "The Song from Over There": the choir of the Sounders who crossed over, the
/// song the player sings with them, and the great crack.
/// <list type="bullet">
/// <item><b>The site</b> is the foot of the Choir Tower on Soaniri, and the gate the player came through on any other
/// world (the chapter's stand-in). The tower was built with crystal pipes and no rune stones, so it is no gate by
/// itself: while a player in chapter 11 is near it, three rune stones stand at its foot (<see cref="EnsureChoirTowerGate"/>
/// — ordinary blocks nobody owns, written once, never where a gate stands already).</item>
/// <item><b>The choir</b>: while a player in chapter 11 is over there within <see cref="OverChoirGatherRange"/> of the
/// site, Elora, Kamo, Feya, Tini and two to four more stand in a ring around it — Echo figures only players over there
/// see, with the small talk of the folk at the gates. They go when nobody of the chapter is near.</item>
/// <item><b>The song</b>: a tone on the melody bar within <see cref="OverChoirSingRange"/> of the site, while the song's
/// step runs, starts <see cref="SoundersIds.PuzzleSongFromOverThere"/> — SOANIRI ("song home"), one letter a round as
/// in the Bridge Song: the choir sings the letter, the player answers the glowing keys. A wrong tone does not count.</item>
/// <item><b>Solved</b>: <c>snd:great_crack</c>, the crack of the site and of every gate within
/// <see cref="GreatCrackReach"/> is mended, and the Lights gather over the site.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The choir stands while a player of chapter 11 is over there this close to the site (blocks) …</summary>
    internal const float OverChoirGatherRange = 64f;

    /// <summary>… and leaves when none is within this.</summary>
    internal const float OverChoirLeaveRange = 110f;

    /// <summary>The song starts from the melody bar this close to the site (blocks, measured on the ground: the tower is tall).</summary>
    internal const float OverChoirSingRange = 24f;

    /// <summary>How many singers stand in the choir at least (the four of the story and two more) …</summary>
    internal const int OverChoirMin = 6;

    /// <summary>… and at most.</summary>
    internal const int OverChoirMax = 8;

    /// <summary>How many Lights gather over the site when the great crack closes.</summary>
    internal const int GreatCrackQualis = 5;

    /// <summary>Where the three rune stones may stand at the tower's foot, relative to its axis: east, west, north — never
    /// in front of the doorway in the south.</summary>
    private static readonly (int X, int Z)[][] ChoirTowerGateSides =
    {
        new[] { (7, -2), (7, 0), (7, 2) },
        new[] { (-7, -2), (-7, 0), (-7, 2) },
        new[] { (-2, 7), (0, 7), (2, 7) },
    };

    private static readonly float[] OverChoirRadii = { 5.6f, 6.6f, 7.6f, 4.4f, 3.2f };

    /// <summary>The choir of one world.</summary>
    private sealed class OverChoir
    {
        public Vector3f Site;
        public readonly List<ServerNpc> Singers = new();
    }

    private readonly Dictionary<string, OverChoir> _overChoirs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _choirTowerGateCheckAt = new(StringComparer.Ordinal);

    // ---------------- The site ----------------

    /// <summary>The foot of the Choir Tower of the active world (standing height on its paved base), or null without one.</summary>
    private Vector3f? ChoirTowerFoot()
        => IsSounderWorld && FindPlacementRecord(SounderSiteRecordPrefix + ChoirTowerSiteKey, 0) is { Placed: true } tower
            ? new Vector3f(tower.X + 0.5f, tower.GroundY + 1f, tower.Z + 0.5f)
            : null;

    /// <summary>Where chapter 11's song is sung for <paramref name="s"/> on the active world: the tower's foot, else the
    /// gate they came through. Null on our side of a world without a tower.</summary>
    private Vector3f? OverChoirSiteFor(PlayerSession s) => ChoirTowerFoot() ?? EntryGateOf(s);

    /// <summary>
    /// Three rune stones at the foot of the Choir Tower, so the song's site is a gate: checked every few seconds while a
    /// player of chapter 11 is near. Nothing is written where a gate stands already (stones raised before, or a player's),
    /// where the paving is taken, or where somebody stands.
    /// </summary>
    private void EnsureChoirTowerGate(Vector3f foot)
    {
        string location = _world.LocationId;
        if ((_choirTowerGateCheckAt.TryGetValue(location, out double at) && _uptime < at)
            || _content.GetBlock("rune_stone") is not { } rune)
        {
            return;
        }

        _choirTowerGateCheckAt[location] = _uptime + 5.0;
        if (IsGate(foot, out _))
        {
            return;
        }

        int cx = (int)Math.Floor(foot.X), y = (int)Math.Floor(foot.Y), cz = (int)Math.Floor(foot.Z);
        foreach (var side in ChoirTowerGateSides)
        {
            var cells = side.Select(o => new Vector3i(cx + o.X, y, cz + o.Z)).ToArray();
            if (!cells.All(ChoirGateCellFree))
            {
                continue;
            }

            foreach (var cell in cells)
            {
                _world.SetBlock(cell, rune.NumericId); // nobody's: the world carries this gate
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = rune.NumericId.Value });
            }

            _log.Info($"Three rune stones stand at the Choir Tower's foot on {location} now (chapter 11's gate).");
            return;
        }
    }

    /// <summary>A stone may stand here: the cell and the one above are open, it has paving under it, and nobody is in it.
    /// Reads the world itself (not only what is loaded): a player 64 blocks away need not have the tower's chunks yet.</summary>
    private bool ChoirGateCellFree(Vector3i cell)
    {
        if (!_world.GetBlock(cell).IsAir || !_world.GetBlock(new Vector3i(cell.X, cell.Y + 1, cell.Z)).IsAir
            || !IsCollidingBlock(_world.GetBlock(new Vector3i(cell.X, cell.Y - 1, cell.Z)), fluidsPass: true, foliagePasses: false))
        {
            return false;
        }

        var middle = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
        foreach (var s in JoinedInActiveWorld())
        {
            if (WrapDistSq(s.State.Position, middle) < 1.5 * 1.5)
            {
                return false;
            }
        }

        return true;
    }

    // ---------------- The choir ----------------

    /// <summary>One pass a second per world (from <see cref="TickSoundersStory"/>): the tower's gate and the choir follow
    /// the players of chapter 11; without one near, the choir of this world goes.</summary>
    private void TickOverChoir()
    {
        string location = _world.LocationId;
        var foot = ChoirTowerFoot();
        Vector3f? wantedAt = null;
        bool keep = false;
        _overChoirs.TryGetValue(location, out var choir);
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Spectating || InSpace(s.State.PlayerId) || !InSoundersChapter(s, "s11"))
            {
                continue;
            }

            var pos = s.State.Position;
            if (foot is { } f && WrapDistSq(pos, f) <= OverChoirGatherRange * OverChoirGatherRange)
            {
                EnsureChoirTowerGate(f);
            }

            if (!s.OtherSide || OverChoirSiteFor(s) is not { } site)
            {
                continue;
            }

            double distSq = WrapDistSq(pos, site);
            keep |= choir is not null && WrapDistSq(pos, choir.Site) <= OverChoirLeaveRange * OverChoirLeaveRange;
            if (distSq <= OverChoirGatherRange * OverChoirGatherRange)
            {
                wantedAt ??= site;
            }
        }

        if (choir is not null && (!keep || choir.Singers.Count == 0 || !_npcs.Contains(choir.Singers[0])))
        {
            DismissOverChoir(location, choir);
            choir = null;
        }

        if (choir is null && wantedAt is { } at)
        {
            GatherOverChoir(location, at);
        }
    }

    /// <summary>The four of the story and two to four more, in a ring around the site — who and how many comes from the
    /// world seed and the site, so the same site has the same choir every time.</summary>
    private void GatherOverChoir(string location, Vector3f site)
    {
        ulong hash = (ulong)WorldGenerator.StableHash(
            FormattableString.Invariant($"overchoir:{_meta.Seed}:{location}:{Math.Floor(site.X / 4f)}:{Math.Floor(site.Z / 4f)}"));
        int count = OverChoirMin + (int)(hash % (ulong)(OverChoirMax - OverChoirMin + 1));
        int firstName = (int)((hash >> 8) % (ulong)OtherSideFolkNames.Length);
        var choir = new OverChoir { Site = site };
        for (int i = 0; i < count; i++)
        {
            double angle = (i * Math.PI * 2.0 / count) + 0.4;
            if (OverChoirSpot(site, angle) is not { } spot)
            {
                continue;
            }

            bool story = i < SoundersIds.Echoes.Length;
            string id = story ? SoundersIds.Echoes[i] : string.Empty;
            string name = story
                ? char.ToUpperInvariant(id[0]) + id.Substring(1)
                : OtherSideFolkNames[(firstName + (i * 3)) % OtherSideFolkNames.Length];
            var npc = SpawnOverThere(spot, name, OtherSideFolkCharacter); // the folk's small talk: the choir is no dialog step
            DressAsSounder(npc, echo: true, SounderSeed(name, id)); // the four look as they did as Echoes
            choir.Singers.Add(npc);
        }

        if (choir.Singers.Count > 0)
        {
            _overChoirs[location] = choir;
            BroadcastNpcs();
        }
    }

    private void DismissOverChoir(string location, OverChoir choir)
    {
        bool any = false;
        foreach (var npc in choir.Singers)
        {
            any |= _npcs.Remove(npc);
        }

        _overChoirs.Remove(location);
        if (any)
        {
            BroadcastNpcs();
        }
    }

    /// <summary>A place to stand in the ring around the site: solid ground with room for a tall figure, never on a rune stone.</summary>
    private Vector3f? OverChoirSpot(Vector3f site, double angle)
    {
        foreach (float radius in OverChoirRadii)
        {
            int x = (int)Math.Floor(site.X + (Math.Cos(angle) * radius));
            int z = (int)Math.Floor(site.Z + (Math.Sin(angle) * radius));
            for (int y = (int)Math.Floor(site.Y) + 3; y >= (int)Math.Floor(site.Y) - 5; y--)
            {
                if (IsCollidingCellIfLoaded(x, y - 1, z, foliagePasses: false) && FreeForFolk(x, y, z)
                    && !IsRuneStoneCell(new Vector3i(x, y - 1, z)))
                {
                    return new Vector3f(x + 0.5f, y, z + 0.5f);
                }
            }
        }

        return null;
    }

    // ---------------- The song ----------------

    /// <summary>The free-play starter of chapter 11: a tone of the melody bar played over there at the song's site, while
    /// the song's step runs, starts the Song from Over There.</summary>
    private MelodyPuzzleSpec? OverChoirStarter(PlayerSession s, int tone)
    {
        _ = tone;
        if (!s.OtherSide || s.CurrentLocationId != _world.LocationId || !SoundersStepRuns(s, SoundersChoirStep)
            || s.State.Milestones.Contains(SoundersIds.MilestoneGreatCrack) || OverChoirSiteFor(s) is not { } site)
        {
            return null;
        }

        var pos = s.State.Position;
        var level = new Vector3f(site.X, pos.Y, site.Z);
        return WrapDistSq(pos, level) <= OverChoirSingRange * OverChoirSingRange ? SongFromOverThereSpec() : null;
    }

    /// <summary>
    /// Chapter 11's song: SOANIRI ("song home") sung by the choir one letter per round, the player answering each letter with
    /// its counter-voice a fifth up, the key to press next glowing on the bar — the Bridge Song's game (#3677), played from
    /// the other side. A wrong tone simply does not count (#3678).
    /// </summary>
    internal static MelodyPuzzleSpec SongFromOverThereSpec() => new()
    {
        Id = SoundersIds.PuzzleSongFromOverThere,
        Mode = MelodyPuzzleMode.Harmony,
        Target = SounderAlphabet.Encode(SoundersIds.SongFromOverThereWord),
        Word = SoundersIds.SongFromOverThereWord,
        RoundPerLetter = true,
        ShowNextTone = true,
        Instrument = SoundersIds.InstrumentChoir,
        IdleSeconds = 30f,
        LeaveDistance = OverChoirSingRange + 8f,
    };

    /// <summary>The song was sung: the great crack closes — for this player the milestone, for the world the mend of the
    /// site and of every gate around it, and the Lights gather.</summary>
    private void OnSongFromOverThereSolved(PlayerSession s)
    {
        if (!IsStoryPackActive(s, SoundersIds.PackId) || !SetSoundersMilestone(s, SoundersIds.MilestoneGreatCrack)
            || !s.OtherSide || s.CurrentLocationId != _world.LocationId || OverChoirSiteFor(s) is not { } site)
        {
            return;
        }

        var gate = IsGate(site, out var stones) ? stones : site;
        MendCrack(gate, GreatCrackReach, s);
        GatherLights(site);
    }

    /// <summary>Up to <see cref="GreatCrackQualis"/> Lights over the site — the Qualis of the other side (the folk code's
    /// species), only for the players over there. They never speak.</summary>
    private void GatherLights(Vector3f site)
    {
        if (OtherSideQualiSpecies() is not { } species)
        {
            return;
        }

        var world = OtherSideWorldOf(_world.LocationId);
        double nearSq = (double)QualiLeaveRange * QualiLeaveRange;
        int near = _creatures.Count(c => c.OtherSideOnly && WrapDistSq(c.Position, site) <= nearSq);
        for (int i = near; i < GreatCrackQualis; i++)
        {
            SpawnOtherSideQuali(world, species, site);
        }

        if (near < GreatCrackQualis)
        {
            BroadcastCreatures();
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the names of the choir standing on the active world.</summary>
    internal IReadOnlyList<string> OverChoirNamesForTest()
        => _overChoirs.TryGetValue(_world.LocationId, out var choir) ? choir.Singers.Select(n => n.Name).ToList() : new List<string>();

    /// <summary>Test seam: one pass of the choir's tick on the active world.</summary>
    internal void TickOverChoirForTest() => TickOverChoir();
}
