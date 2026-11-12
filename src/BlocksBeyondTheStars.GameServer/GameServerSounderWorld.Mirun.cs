// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: <b>Mirun</b>, the village of the last Sounders — a levelled moss plaza with a ring path of old brick, the bell post
/// in the middle (a carved-obsidian column with four chimes on its arms and a resonance crystal on top), six round-ish huts
/// of stone and reed planks on a ring, their doors toward the plaza and a chime on every roof, three paper lanterns, bell
/// blooms and an echo flower by the post, and a fringe of chime reeds around it all (the reed marsh's plant) — built by
/// <see cref="SoaniriBuilder"/> (#3522). The villagers are transient like every NPC: six Sounders (NPC look <c>sounder</c>) at
/// their huts and the Eldest (character id <c>eldest</c>) by the bell post, respawned on every load.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The settlement key of Mirun's villagers (their memory and greeting key, like the shrine keepers').</summary>
    internal const string MirunSettlement = "mirun";

    /// <summary>The authored story character the Eldest's NPC carries (the pack's dialogs key on it).</summary>
    internal const string EldestCharacterId = "eldest";

    /// <summary>Mirun's villagers (every load, idempotent): six Sounders outside their hut doors and the Eldest by the bell
    /// post. Deterministic per world; transient like every NPC.</summary>
    private void SpawnMirunVillagers(int cx, int floorY, int cz)
    {
        if (_npcs.Any(n => n.Settlement == MirunSettlement))
        {
            return;
        }

        var rng = new Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("mirun-villagers:" + _world.LocationId))));
        for (int i = 0; i < SoaniriBuilder.MirunHuts; i++)
        {
            var (hx, hz) = SoaniriBuilder.MirunHutCentre(cx, cz, i);
            double len = Math.Max(1.0, Math.Sqrt(((cx - hx) * (cx - hx)) + ((cz - hz) * (cz - hz))));
            var home = new Vector3f(hx + 0.5f + (float)((cx - hx) / len * 3.5), floorY + 1f, hz + 0.5f + (float)((cz - hz) / len * 3.5));
            _npcs.Add(MakeSounderNpc(home, rng, leash: 6f));
        }

        _npcs.Add(MakeSounderNpc(new Vector3f(cx + 0.5f, floorY + 1f, cz + 2.5f), rng, leash: 1.5f, EldestCharacterId));
        BroadcastNpcs();
    }

    /// <summary>A Sounder villager with Mirun as its home and a Sounder name, dressed like every Sounder (#3449,
    /// <see cref="DressAsSounder"/>: a light-blue or light-green skin, a sash, the Sounder's own scale) — the Eldest the
    /// tallest of them (<see cref="SounderRules.MaxSize"/>). The rng draws are the classic ones, so every villager keeps the
    /// name it had.</summary>
    private ServerNpc MakeSounderNpc(Vector3f home, Random rng, float leash, string characterId = "")
    {
        var npc = MakeNpc("settler", "sounders", robotic: false, home, rng);
        npc.Settlement = MirunSettlement;
        npc.Name = SounderName(rng);
        npc.CharacterId = characterId;
        float size = characterId == EldestCharacterId ? SounderRules.MaxSize : 0f;
        DressAsSounder(npc, echo: false, SounderSeed(npc.Name, characterId), size);
        npc.Leash = leash;
        return npc;
    }

    private static readonly string[] SounderSyllables =
    {
        "So", "Ni", "Ri", "Mi", "Ra", "Lu", "Ve", "Na", "Ta", "O", "Fe", "Su", "I", "La", "Me", "Ru", "A", "No",
    };

    private static readonly string[] SounderEndings = { "a", "i", "el", "in", "ar", "u", "is", "en" };

    /// <summary>A soft Sounder name — two syllables and an ending ("Sonira", "Luvel") — from the seeded rng.</summary>
    internal static string SounderName(Random rng)
    {
        string first = SounderSyllables[rng.Next(SounderSyllables.Length)];
        string second = SounderSyllables[rng.Next(SounderSyllables.Length)].ToLowerInvariant();
        return first + second + SounderEndings[rng.Next(SounderEndings.Length)];
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection: Mirun's villagers on the active world (look, character id, name, skin, sash, scale).</summary>
    internal (string Look, string CharacterId, string Name, uint Skin, uint Sash, float Size)[] MirunVillagersForTest()
        => _npcs.Where(n => n.Settlement == MirunSettlement).Select(n => (n.Look, n.CharacterId, n.Name, n.SkinRgb, n.OutfitRgb, n.Size)).ToArray();
}
