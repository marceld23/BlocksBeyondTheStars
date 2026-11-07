// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The names the Sounders story pack ("The Sounders' Song", epic #3368) shares between data, server and client: one place,
/// so a renamed key cannot drift between the pack's <c>story.json</c>, the gameplay handlers and the views.
/// </summary>
public static class SoundersIds
{
    /// <summary>The story pack id (<c>data/stories/sounders/</c>).</summary>
    public const string PackId = "sounders";

    /// <summary>The Sounders' detuned home system (#3357), appended to a galaxy on its first reveal.</summary>
    public const string SystemId = "ornemis";

    /// <summary>The Sounder home world's planet type (#3358): the world Soaniri.</summary>
    public const string PlanetType = "sounder_world";

    /// <summary>The cat-like, eyeless singer with huge ears (#3359) — and the story companion (#3361).</summary>
    public const string Soundi = "soundi";

    /// <summary>The ten-metre plant-like giant with the loud scream (#3359).</summary>
    public const string Soundrak = "soundrak";

    /// <summary>One of the giant translucent jellyfish from the other reality (#3359); plural "Qualis".</summary>
    public const string Quali = "quali";

    /// <summary>The small light-loving fluff ball (#3359, #3366).</summary>
    public const string Soundeling = "soundeling";

    /// <summary>The look of a living Sounder NPC (#3355): four arms, no eyes, long ears.</summary>
    public const string Sounder = "sounder";

    /// <summary>The look of an Echo (#3356): a Sounder body, translucent and glowing.</summary>
    public const string Echo = "sounder_echo";

    /// <summary>The placeable artifact that plays Song Discs (#3352) — not a "record player".</summary>
    public const string SongSpindle = "song_spindle";

    /// <summary>Story disc 1, from the Singing Shrine: the first letters of the note alphabet.</summary>
    public const string DiscBeginning = "song_disc_beginning";

    /// <summary>Story disc 2: carries the Resonance Blaster blueprint.</summary>
    public const string DiscBuilders = "song_disc_builders";

    /// <summary>Story disc 3: carries the obsidian armour blueprint and is itself the hearing disc for the socket.</summary>
    public const string DiscHearing = "song_disc_hearing";

    /// <summary>Story disc 4: the hunger (the humming "recorder" of the alien debris field).</summary>
    public const string DiscHunger = "song_disc_hunger";

    /// <summary>Story disc 5: the Great Song (the first sound chamber).</summary>
    public const string DiscGreatSong = "song_disc_great_song";

    /// <summary>Story disc 6: the Sounder instruments for the melody block (the second sound chamber).</summary>
    public const string DiscVoices = "song_disc_voices";

    /// <summary>Socket disc: Soundraks stay calm.</summary>
    public const string DiscCalm = "song_disc_calm";

    /// <summary>Socket disc: a soft glow.</summary>
    public const string DiscLight = "song_disc_light";

    /// <summary>A collectible song disc: a seeded Sounder song, no story content.</summary>
    public const string DiscSong = "song_disc";

    /// <summary>The story discs in the order the pack hands them out.</summary>
    public static readonly string[] StoryDiscs =
    {
        DiscBeginning, DiscBuilders, DiscHearing, DiscHunger, DiscGreatSong, DiscVoices,
    };

    /// <summary>The Resonance Blaster (<i>Musik-Dröhner</i>, #3353).</summary>
    public const string ResonanceBlaster = "resonance_blaster";

    /// <summary>The obsidian armour (#3354).</summary>
    public const string ObsidianHelmet = "obsidian_helmet";

    /// <summary>The obsidian armour's chest — the piece with the disc socket.</summary>
    public const string ObsidianChest = "obsidian_chest";

    /// <summary>The obsidian armour's legs.</summary>
    public const string ObsidianLegs = "obsidian_legs";

    /// <summary>Hidden blueprint (#3350): the Resonance Blaster.</summary>
    public const string BlueprintResonance = "sounder_resonance";

    /// <summary>Hidden blueprint: the obsidian armour.</summary>
    public const string BlueprintObsidian = "sounder_obsidian";

    /// <summary>Hidden blueprint: more Song Spindles.</summary>
    public const string BlueprintSpindle = "sounder_spindle";

    /// <summary>Hidden blueprint: the Sounder instruments on the melody block.</summary>
    public const string BlueprintVoice = "sounder_voice";

    /// <summary>Melody puzzle (#3346): repeat the Singing Shrine's phrases (chapter 2).</summary>
    public const string PuzzleShrineEcho = "shrine_echo";

    /// <summary>Melody puzzle: play the Song of Return, ORNEMIS in the note alphabet (chapter 6).</summary>
    public const string PuzzleSongOfReturn = "song_of_return";

    /// <summary>Melody puzzle: the counter-voice at the Choir Tower (chapter 8).</summary>
    public const string PuzzleBridgeSong = "bridge_song";

    /// <summary>Melody puzzle: the story Soundi's call and response (#3361).</summary>
    public const string PuzzleSingDuel = "sing_duel";

    /// <summary>Melody puzzle: the Bell Bloom trail in Ornasa (#3367).</summary>
    public const string PuzzleBloomTrail = "bloom_trail";

    /// <summary>The four Echoes (#3356), in the order of the Song of Return's parts.</summary>
    public static readonly string[] Echoes = { "elora", "kamo", "feya", "tini" };

    /// <summary>The word the Song of Return spells in the note alphabet: the system's name.</summary>
    public const string SongOfReturnWord = "ORNEMIS";

    /// <summary>The Song of Return split into the parts the four Echoes give (same order as <see cref="Echoes"/>).</summary>
    public static readonly string[] SongOfReturnParts = { "OR", "NE", "MI", "S" };

    /// <summary>Procedural instrument index of the Sounder voice (#3345); 0–3 are the melody block's originals.</summary>
    public const int InstrumentVoice = 4;

    /// <summary>Procedural instrument index of the Sounder choir (two voices and a fifth).</summary>
    public const int InstrumentChoir = 5;

    /// <summary>Prefix of the pack's own per-player milestones (the runtime's <c>story:sounders:…</c> family stays the
    /// runtime's): <c>snd:word:&lt;word&gt;</c>, <c>snd:disc:&lt;item&gt;</c>, <c>snd:echo:&lt;id&gt;</c> ….</summary>
    public const string MilestonePrefix = "snd:";

    /// <summary>The player learned the Song of Return — Ornemis shows on their star map and can be a jump target.</summary>
    public const string MilestoneSongLearned = "snd:song_learned";

    /// <summary>A heard rune word, collected in the Song Archive.</summary>
    public static string WordMilestone(string word) => MilestonePrefix + "word:" + word;

    /// <summary>A decoded story disc.</summary>
    public static string DiscMilestone(string discItem) => MilestonePrefix + "disc:" + discItem;

    /// <summary>A questioned Echo.</summary>
    public static string EchoMilestone(string echoId) => MilestonePrefix + "echo:" + echoId;
}
