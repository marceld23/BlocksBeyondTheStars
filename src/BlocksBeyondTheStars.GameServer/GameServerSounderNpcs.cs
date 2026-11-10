// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Sounders as NPCs (#3355, #3356): the villagers of Mirun, the Eldest, Roll and the Young Voices (look
/// <see cref="SoundersIds.Sounder"/>) and the Echoes Elora, Kamo, Feya and Tini (look <see cref="SoundersIds.Echo"/>). A Sounder
/// NPC is an ordinary <see cref="ServerNpc"/> — it strolls on its leash, turns to a visitor, talks through the NPC dialogs — whose
/// <see cref="ServerNpc.Look"/> travels on the existing <c>NetNpc.Look</c> field (no new message): the client draws the Sounder
/// rig instead of the human avatar (an Echo translucent teal, glowing, floating a hand above the ground) and lets it hum
/// (<c>elder_hum_*</c> / <c>echo_hum_*</c>) instead of speaking. Its size is the Sounder's scale (<see cref="SounderRules"/>,
/// 3–4 blocks tall); its skin and its sash colour ride on <c>SkinRgb</c> and <c>OutfitRgb</c>.
/// <para>NPCs are runtime state: a world's list is rebuilt when it loads, so the caller (the pack's world and story code)
/// places its Sounders again on every load of the world that holds them.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The nameplate key of a living Sounder ("Sounder").</summary>
    internal const string SounderNameKey = "npc.role.sounder";

    /// <summary>The nameplate key of an Echo ("Echo").</summary>
    internal const string EchoNameKey = "npc.role.sounder_echo";

    /// <summary>
    /// Puts a Sounder NPC on the active world and returns its NPC id.
    /// </summary>
    /// <param name="look"><see cref="SoundersIds.Sounder"/> (a living Sounder) or <see cref="SoundersIds.Echo"/> (an Echo);
    /// anything else reads as a living Sounder.</param>
    /// <param name="pos">Where it stands (its feet; an Echo's float is drawn by the client) — also the home of its leash.</param>
    /// <param name="name">The nameplate's name ("Elora", "Roll" …); empty = a coined name.</param>
    /// <param name="characterId">The story character it is (for the pack's dialogs), or empty.</param>
    /// <param name="leash">How far it strolls round <paramref name="pos"/> (0 = it stands still).</param>
    /// <param name="size">Its scale (<see cref="SounderRules.MinSize"/>..<see cref="SounderRules.MaxSize"/>, ≈ 3–4 blocks); 0 =
    /// one picked from the name.</param>
    internal int SpawnSounderNpc(string look, Vector3f pos, string name, string characterId = "", float leash = 1.6f, float size = 0f)
    {
        bool echo = SounderRules.IsEcho(look);
        int seed = SounderSeed(name, characterId);
        var npc = MakeNpc("settler", "sounders", robotic: false, pos, new System.Random(seed));
        npc.NameKey = echo ? EchoNameKey : SounderNameKey;
        if (name.Length > 0)
        {
            npc.Name = name;
        }

        npc.CharacterId = characterId;
        DressAsSounder(npc, echo, seed, size);
        npc.Leash = System.Math.Max(0f, leash);
        _npcs.Add(npc);
        BroadcastNpcs();
        return npc.Id;
    }

    /// <summary>The seed a Sounder's look comes from: its story character id, else its name — the same Sounder looks the same on
    /// every load.</summary>
    internal static int SounderSeed(string name, string characterId)
        => unchecked((int)WorldGenerator.StableHash("sounder:" + (characterId.Length > 0 ? characterId : name)));

    /// <summary>Dresses an NPC as a Sounder (#3449: shared by <see cref="SpawnSounderNpc"/> and Mirun's villagers, who used to
    /// keep the settler's human skin, outfit and size): the Sounder look, a skin and a sash from <see cref="SounderRules.LookFor"/>
    /// (an Echo's translucent teal and glow instead), its scale — <paramref name="size"/> clamped, or 0 = one picked from the seed
    /// (<see cref="SounderRules.SizeFor"/>) — and the face seed the client draws the hum's pitch and the body's small differences
    /// from.</summary>
    private static void DressAsSounder(ServerNpc npc, bool echo, int seed, float size = 0f)
    {
        var (skin, sash) = SounderRules.LookFor(seed);
        npc.Look = echo ? SoundersIds.Echo : SoundersIds.Sounder;
        npc.Size = size > 0f ? SounderRules.ClampSize(size) : SounderRules.SizeFor(seed);
        npc.SkinRgb = echo ? SounderRules.EchoTeal : skin;
        npc.OutfitRgb = echo ? SounderRules.EchoGlow : sash;
        npc.LegsRgb = npc.SkinRgb;
        npc.FaceSeed = seed != 0 ? seed : 1;
    }

    /// <summary>Removes a Sounder NPC (an Echo that goes back "to the other side", a villager who leaves). False when there is
    /// no Sounder with that id.</summary>
    internal bool RemoveSounderNpc(int npcId)
    {
        int removed = _npcs.RemoveAll(n => n.Id == npcId && SounderRules.IsSounderLook(n.Look));
        if (removed > 0)
        {
            BroadcastNpcs();
        }

        return removed > 0;
    }

    /// <summary>The ids of the Sounder NPCs on the active world (living and Echoes).</summary>
    internal IReadOnlyList<int> SounderNpcIds() => _npcs.Where(n => SounderRules.IsSounderLook(n.Look)).Select(n => n.Id).ToList();

    // --- Test seams (#3355) ----------------------------------------------------------------------------------------------

    /// <summary>Test seam: <see cref="SpawnSounderNpc"/>.</summary>
    public int SpawnSounderNpcForTest(string look, Vector3f pos, string name, string characterId = "")
        => SpawnSounderNpc(look, pos, name, characterId);

    /// <summary>Test seam: the NPC <paramref name="id"/> exactly as the wire carries it.</summary>
    public Networking.Messages.NetNpc NetNpcForTest(int id) => ToNetNpc(_npcs.First(n => n.Id == id));

    /// <summary>Test seam: <see cref="RemoveSounderNpc"/>.</summary>
    public bool RemoveSounderNpcForTest(int id) => RemoveSounderNpc(id);
}
