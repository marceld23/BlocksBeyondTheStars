// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3358: <b>Mirun</b>, the village of the last Sounders — a levelled moss plaza with a ring path of old brick, the bell post
/// in the middle (a carved-obsidian column with four chimes on its arms and a resonance crystal on top), six round-ish huts
/// of stone and reed planks on a ring, their doors toward the plaza and a chime on every roof, three paper lanterns, bell
/// blooms and an echo flower by the post, and a fringe of chime reeds around it all (the reed marsh's plant). The villagers
/// are transient like every NPC: six Sounders (NPC look <c>sounder</c>) at their huts and the Eldest (character id
/// <c>eldest</c>) by the bell post, respawned on every load.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The settlement key of Mirun's villagers (their memory and greeting key, like the shrine keepers').</summary>
    internal const string MirunSettlement = "mirun";

    /// <summary>The authored story character the Eldest's NPC carries (the pack's dialogs key on it).</summary>
    internal const string EldestCharacterId = "eldest";

    private const int MirunHuts = 6;
    private const double MirunHutRing = 9.5;
    private const int MirunRadius = 15;

    private void WriteMirun(in SounderBlocks b, int cx, int floorY, int cz)
    {
        LevelSounderDisc(cx, cz, floorY, MirunRadius, b.Moss, b.Dirt);
        WriteMirunPlaza(b, cx, floorY, cz);
        for (int i = 0; i < MirunHuts; i++)
        {
            var (hx, hz) = MirunHutCentre(cx, cz, i);
            WriteMirunHut(b, hx, floorY, hz, cx, cz);
        }

        for (int i = 0; i < 3; i++)
        {
            double a = (i * Math.PI * 2.0 / 3.0) + 0.8;
            int lx = cx + (int)Math.Round(Math.Sin(a) * 6.0), lz = cz + (int)Math.Round(Math.Cos(a) * 6.0);
            SounderColumn(lx, lz, floorY + 1, floorY + 2, b.CarvedObsidian);
            SounderSet(lx, floorY + 3, lz, b.Lantern);
        }

        WriteMirunReeds(b, cx, floorY, cz);
    }

    /// <summary>The centre of hut <paramref name="i"/> on the ring around the plaza.</summary>
    internal static (int X, int Z) MirunHutCentre(int cx, int cz, int i)
    {
        double a = (i * Math.PI * 2.0 / MirunHuts) + 0.26;
        return (cx + (int)Math.Round(Math.Sin(a) * MirunHutRing), cz + (int)Math.Round(Math.Cos(a) * MirunHutRing));
    }

    /// <summary>The ring path, the bell post with its chimes and crystal, and the little garden of the post.</summary>
    private void WriteMirunPlaza(in SounderBlocks b, int cx, int floorY, int cz)
    {
        for (int dx = -5; dx <= 5; dx++)
        {
            for (int dz = -5; dz <= 5; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                if (d2 >= 9 && d2 <= 18)
                {
                    SounderSet(cx + dx, floorY, cz + dz, b.Brick); // the old ring path
                }
            }
        }

        SounderColumn(cx, cz, floorY + 1, floorY + 5, b.CarvedObsidian);
        foreach (var (ox, oz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            SounderSet(cx + ox, floorY + 5, cz + oz, b.CarvedObsidian); // the arms
            SounderSet(cx + ox, floorY + 4, cz + oz, b.Chime);          // a bell under each arm
        }

        SounderSet(cx, floorY + 6, cz, b.Crystal);
        SounderPlant(b, cx + 2, floorY, cz - 2, b.Bloom);
        SounderPlant(b, cx - 2, floorY, cz - 2, b.Bloom);
        SounderPlant(b, cx - 2, floorY, cz + 1, b.Bloom);
        SounderPlant(b, cx + 2, floorY, cz + 1, b.Echo);
    }

    /// <summary>A 5×5 hut: a course of brick under two of reed planks, a two-high door toward the plaza, a stepped plank roof
    /// with a chime on top and a plank floor inside.</summary>
    private void WriteMirunHut(in SounderBlocks b, int hx, int floorY, int hz, int cx, int cz)
    {
        int towardX = cx - hx, towardZ = cz - hz;
        (int doorX, int doorZ) = Math.Abs(towardX) >= Math.Abs(towardZ) ? (Math.Sign(towardX) * 2, 0) : (0, Math.Sign(towardZ) * 2);
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                int x = hx + dx, z = hz + dz;
                bool wall = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
                bool door = dx == doorX && dz == doorZ;
                if (wall)
                {
                    if (!door)
                    {
                        SounderSet(x, floorY + 1, z, b.Brick);
                        SounderSet(x, floorY + 2, z, b.LightPlanks);
                    }

                    SounderSet(x, floorY + 3, z, b.LightPlanks);
                }
                else
                {
                    SounderSet(x, floorY, z, b.LightPlanks);
                    SounderSet(x, floorY + 5, z, b.Planks);
                }

                SounderSet(x, floorY + 4, z, b.Planks);
            }
        }

        SounderSet(hx, floorY + 6, hz, b.Chime);
    }

    /// <summary>The reed fringe: about a third of the floor's outer ring grows chime reeds (never inside a hut wall).</summary>
    private void WriteMirunReeds(in SounderBlocks b, int cx, int floorY, int cz)
    {
        for (int dx = -MirunRadius; dx <= MirunRadius; dx++)
        {
            for (int dz = -MirunRadius; dz <= MirunRadius; dz++)
            {
                int d2 = (dx * dx) + (dz * dz);
                int x = cx + dx, z = cz + dz;
                if (d2 < 13 * 13 || d2 > MirunRadius * MirunRadius || SounderHash("mirun-reed", x, z) >= 0.35
                    || !_world.GetBlock(SounderCell(x, floorY + 1, z)).IsAir)
                {
                    continue;
                }

                SounderPlant(b, x, floorY, z, b.Reed);
            }
        }
    }

    /// <summary>Mirun's villagers (every load, idempotent): six Sounders outside their hut doors and the Eldest by the bell
    /// post. Deterministic per world; transient like every NPC.</summary>
    private void SpawnMirunVillagers(int cx, int floorY, int cz)
    {
        if (_npcs.Any(n => n.Settlement == MirunSettlement))
        {
            return;
        }

        var rng = new Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("mirun-villagers:" + _world.LocationId))));
        for (int i = 0; i < MirunHuts; i++)
        {
            var (hx, hz) = MirunHutCentre(cx, cz, i);
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
