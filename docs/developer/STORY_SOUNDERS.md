# The Sounders' Song — concept and how it is built

Status: implemented (epic #3368, issues #3344–#3367). Live Done/Open status: [../../TODO.md](../../TODO.md).

**The Sounders' Song** (*Das Lied der Sounders*) is the second story of the game, next to VEGA's. It is a **side pack per
player** (`data/stories/sounders/`, `kind: side`, `scope: player`): it runs in the same world as VEGA's story, every
player has their own progress, a world can switch it off at creation and every player can opt out in the Journal.
VEGA narrates; the Echoes, the Eldest and Roll speak in the scenes.

Lore, creatures and names come from the maintainer, Justus and the school club "Building Games with AI" (the Sounder
sketches by Theo and a classmate; the names by Marcel and Justus, 2026-11-07).

## 1. Canon

- **The Sounders** — organic, tall and thin (3–4 blocks), two legs, **four arms**, a small head **without eyes**, long
  ears, a mouth; a slot on the chest where their obsidian armour holds a Song Disc. They speak and write in music.
- **Their old age** — with song and technology they shaped reality ("we sang, and it was"). Unknowingly, every song
  broke something in another reality, the world of the **Qualis** (giant translucent jellyfish when they appear in ours).
  The Qualis defended their home by taking the Sounders' food. Nobody understood why.
- **The Great Song** — all Sounders sang together at the Choir Tower to save their world. The song carried every singer
  into the Qualis' world. **They are not dead**: as **Echoes** they still sing "on the other side", and where rune stones
  stand the wall between the realities is thin. Only the few who did not sing stayed behind.
- **The Final Song** — the survivors' myth of the Great Song: their people "rose to a higher place". The Echoes tell the
  truth.
- **The living Sounders** — a few hum (they no longer build by singing) in the village **Mirun** on **Soaniri**, in the
  **Ornemis** system. The system is **detuned**: navigation finds no jump point unless the ship knows its song.
- **The Young Voices**, led by **Roll**, believe the myth and want to sing the Final Song again to follow their people.
- **Soundis** (cat-like, eyeless, huge ears; tamed only by a duel — a Soundi in its pack is defended by the pack),
  **Soundelings** (light-loving fluff balls), **Soundraks** (ten-metre plant-like giants with a loud scream) and the
  singing plants (**Chime Reed**, **Bell Bloom**, **Harp Vine**, **Echo Flower**) live on Soaniri.
- **The twist inside the pack:** VEGA's old monument "translations" (her own pack's rune inscriptions) were the
  archive's guesses — the runes were never letters. VEGA's texts stay as they are; she says so herself in chapter 3.

## 2. The story (8 chapters)

| Chapter | What the player does | Ends with |
|---|---|---|
| s1 The stones sing | plays rune stones (use key) — every word goes into the **Song Archive** | three runes played |
| s2 The shrine of eight voices | answers the **Singing Shrine** (8 pillars = 8 tones, the repeat puzzle); old saves: a ring of eight rune stones around an ancient brick | the Song Spindle + disc 1 |
| s3 Song Discs | places the **Song Spindle**, decodes discs with the **note-key** minigame | three discs decoded |
| s4 Alien blueprints | researches the hidden blueprints, builds the **Resonance Blaster** and the **obsidian armour**, opens a **sound chamber** under a monument, inserts the hearing disc | the first voice is heard |
| s5 The Echoes | questions **Elora**, **Kamo**, **Feya** and **Tini** with melodies | the truth about the Great Song |
| s6 The Song of Return | plays **ORNEMIS** in the note alphabet at the shrine (or the ring) | Ornemis on the player's star map |
| s7 Homecoming | jumps to Ornemis, lands on Soaniri, meets **the Eldest** in Mirun | the Young Voices are at the Choir Tower |
| s8 The Bridge Song | persuades **Roll**, plays the counter-voice at the **Choir Tower**'s eight pipes, then wins the **sing duel** with a Soundi | the Soundi travels with the player |

Nobody dies, nobody is a villain, wrong notes never punish, every place-bound step has a fallback (no shrine → the ring;
no monuments here → another world; the Echoes come to any group of three or more rune stones, even placed ones).

## 3. Mechanics and where they live

| Mechanic | Code | Docs |
|---|---|---|
| Note alphabet, rune words | `Shared/Sounders/SounderAlphabet.cs` (`SounderAlphabet`, `SounderLexicon`), `SoundersIds` | this doc §4 |
| Use key on blocks | `UseBlockIntent` (323) → `GameServerBlockUse.cs` (per-block-key handlers) | — |
| Melodies at a place | `NoteSequence` (324), `GameServer.PlayNoteSequence` | [SOUND_DESIGN.md](SOUND_DESIGN.md) |
| Sounder instruments 4/5, rune stones | `ProceduralAudio`, `GameServerRunes.cs` | [SOUND_DESIGN.md](SOUND_DESIGN.md), [CRYSTAL_NET.md](CRYSTAL_NET.md) |
| Melody puzzles, melody sites, the melody bar | `MelodyPuzzle*`, `GameServerMelodyPuzzles*.cs`, `HudUi.MelodyBar*` | [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) |
| Side packs with lore, dialogs, texts, systems | story engine | [STORY_PACK_AUTHORING.md](STORY_PACK_AUTHORING.md) |
| Hidden blueprints (`hiddenUntil`) | `BlueprintDefinition`, `HandleUnlock` | [CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md) |
| Song Spindle, discs, note-key minigame, story loot | `GameServerSongSpindle.cs`, `GameServerStructureLoot.cs` | [MINIGAMES_AND_WIKI.md](MINIGAMES_AND_WIKI.md) |
| Resonance Blaster, sound chambers | `GameServerGadgets.cs`, `GameServerSoundChambers.cs` | [HELD_ITEMS.md](HELD_ITEMS.md) |
| Obsidian armour, disc socket | equipment, `GearLook` | [CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md) |
| Echoes, the Eldest, Roll, story glue | `GameServerSoundersStory*.cs` | this doc §5 |
| Ornemis (pack star systems) | story engine | [MULTIWORLD_AND_SYSTEM_FLIGHT.md](MULTIWORLD_AND_SYSTEM_FLIGHT.md) |
| Soaniri, Mirun, Ornasa, the Choir Tower, singing plants | `sounder_world`, `GameServerSounderWorld*.cs` | [WORLD_GENERATION.md](WORLD_GENERATION.md) |
| Sounder, Soundi, Soundrak, Quali, Soundeling | body plans, `data/creatures.json` | [CREATURE_RIG.md](CREATURE_RIG.md) |
| Companion selection, story Soundi, duel taming, pack defence | taming code | [CREATURE_TAMING.md](CREATURE_TAMING.md) |

## 4. The note alphabet (canon — never reorder)

Eight tones, C4 to C5 (indices 0–7, the melody block's scale). A letter is **two tones**: the first picks the row, the
second the column. A short pause (`Rest`) separates letters, a long one (two rests) words.

```
            col C   D   E   F
  row C:        A   B   C   D
  row D:        E   F   G   H
  row E:        I   J   K   L
  row F:        M   N   O   P
  row G:        Q   R   S   T
  row A:        U   V   W   X
  row B/H:      Y   Z
```

The rune stones sing invented Sounder words (`SounderLexicon.Words`: SOA song, NIRI home, ORN star, EMIS still, LUM light,
VEL water, TAR stone, KEI hunger, ARU sky, MIRA listen, ESA together, OLU build, FENA field, RUSO wave, ILA child, DUNA far
away) — the same tune in every language and for every player of a world. The word of a stone is fixed by the world seed
and its position. The **Song of Return** spells the system's name, **ORNEMIS**; the four Echoes give its parts OR, NE, MI, S.

## 5. Per-player milestones

The runtime owns `story:sounders:…`. The pack's own flags live under `snd:`:

| Milestone | Written when |
|---|---|
| `snd:first_rune` | any rune stone was played by the player (pack-independent; the pack's start rule waits for it) |
| `snd:word:<WORD>` | a rune word was heard (the Song Archive shows it while the pack runs) |
| `snd:disc:<item>` | a story disc was decoded |
| `snd:echo:<id>` | an Echo was questioned to the end |
| `snd:song_learned` | the Song of Return was played — Ornemis shows on this player's star map |

## 6. Assets

Sounds, textures, icons and music were generated with the repository's asset scripts (maintainer approval 2026-11-07)
and are listed in [NOTICES.md](../../NOTICES.md); the Soundi's calls are the animated series' approved calls. The notes
themselves are procedural (`note_<instrument>_<tone>`), never files.
