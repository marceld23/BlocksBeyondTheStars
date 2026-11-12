// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: the <b>Choir Tower</b> — the finale stage of the Sounders pack. A paved base in a <b>barren ring</b> (the chime moss
/// around it turned to grey scree, its plants gone: where the old hunger shows, and where the Soundraks like to stand), a
/// round tower of carved obsidian <see cref="SounderSiteLayout.ChoirTowerHeight"/> blocks high with two bands of resonance
/// crystal, a doorway in the south and a spiral of steps around a central column inside, and on top a wide platform with a
/// low rim and the <b>eight pipes</b> in a ring: pipe <c>i</c> is tone <c>i</c>, clockwise from north, the low tones the tall
/// pipes, each crowned with a resonance crystal (the blocks are <see cref="SoaniriBuilder"/>'s, #3522). The pipes are the
/// melody site <see cref="ChoirTowerSiteKey"/> (the sound pack's
/// <see cref="RegisterMelodySite(string, System.Collections.Generic.IReadOnlyList{Vector3i})"/>, re-registered every load)
/// and a struck crystal feeds the running puzzle or sings its tone (<see cref="UseResonanceCrystal"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The floor of the top platform for a tower standing on <paramref name="floorY"/>.</summary>
    internal static int ChoirPlatformY(int floorY) => SoaniriBuilder.ChoirPlatformY(floorY);

    /// <summary>The pipes' base cells (the melody pillars), clockwise from north, for a tower on <paramref name="floorY"/>.</summary>
    internal static Vector3i[] ChoirPipes(int cx, int floorY, int cz) => SoaniriBuilder.ChoirPipes(cx, floorY, cz);

    /// <summary>Every load: the eight pipes as the melody site <see cref="ChoirTowerSiteKey"/>, and the pack site on the top
    /// platform (where the finale plays).</summary>
    private void RegisterChoirTowerSite(int cx, int floorY, int cz)
    {
        RegisterMelodySite(ChoirTowerSiteKey, ChoirPipes(cx, floorY, cz));
        RegisterPackSite(SoundersIds.PackId, ChoirTowerSiteKey, _world.LocationId, new Vector3i(cx, ChoirPlatformY(floorY) + 1, cz));
    }

    /// <summary>
    /// The use key on a resonance crystal (#3358): a Choir Tower pipe (or any crystal that is a melody pillar) strikes its
    /// tone into the running puzzle or starts one; with no puzzle it sings its pillar's tone, and any other crystal sings its
    /// own tone (a hash of its cell) — so every crystal on Soaniri is an instrument. The singing plants hear it too.
    /// </summary>
    private void UseResonanceCrystal(PlayerSession session, Vector3i cell)
    {
        if (TryStrikeMelodyPillar(session, cell))
        {
            return;
        }

        MelodySiteAt(cell, out int pillar);
        int tone = pillar >= 0 ? pillar : CrystalTone(WorldConstants.CanonicalBlock(cell, _world.Circumference));
        PlayNoteSequence(new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), new[] { tone }, SoundersIds.InstrumentVoice,
            0.6f, withFifth: false, source: "resonance_crystal");
        HearSingingPlants(session, tone, SoundersIds.InstrumentVoice);
    }

    /// <summary>The tone a lone resonance crystal sings (0–7): a stable hash of its canonical cell.</summary>
    internal static int CrystalTone(Vector3i cell)
    {
        unchecked
        {
            uint h = ((uint)cell.X * 73856093u) ^ ((uint)cell.Y * 19349663u) ^ ((uint)cell.Z * 83492791u);
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (int)(h % (uint)SounderAlphabet.ToneCount);
        }
    }
}
