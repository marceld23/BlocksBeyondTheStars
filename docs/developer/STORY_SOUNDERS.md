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
  **Soundelings** (light-loving fluff balls), **Soundraks** (ten-metre plant-like giants with a loud scream — **hostile from
  the start**, #3424: their scream is the warning, then they attack; the calm disc and the Resonance Blaster calm them) and the
  singing plants (**Chime Reed**, **Bell Bloom**, **Harp Vine**, **Echo Flower**) live on Soaniri. Justus calls the Soundraks
  "Soundkras".
- **Soaniri is light blue and light green** (#3423, Justus 2026-11-08): the Sounders, Soundis, Soundelings and Soundraks, the
  chime moss (light green with aqua clumps), the singing plants (light-green stalks and leaves; light-blue bells, tips, strands
  and throats), the wild plants (the planet's `floraPalette` of light greens) — and the plants keep their colours through
  autumn and winter (`evergreenFlora`). #3448/#3449 (Marcel 2026-11-09): the rolled animals (the planet's `faunaPalette` of
  light blues, aquas and mints, in fur and skin), the micro-fauna and Mirun's villagers and the Eldest (who wore a settler's
  human skin until then) too. The carved obsidian stays black, the sky and its star are untouched. Colours and
  where they live: [CREATURE_RIG.md](CREATURE_RIG.md) § "The bodies of Soaniri", [WORLD_GENERATION.md](WORLD_GENERATION.md) §51.
- **The twist inside the pack:** VEGA's old monument "translations" (her own pack's rune inscriptions) were the
  archive's guesses — the runes were never letters. VEGA's texts stay as they are; she says so herself in chapter 3.

## 2. The story (8 chapters)

| Chapter | What the player does | Ends with |
|---|---|---|
| s1 The stones sing | plays rune stones (use key) — every word goes into the **Song Archive** | three runes played |
| s2 The shrine of eight voices | answers a **singing shrine** (8 pillars = 8 tones, the repeat puzzle) — the oldest and biggest is the one-of-a-kind Singing Shrine ("the Shrine of the Eight Voices"); any ring of eight rune stones around an ancient brick works too | the Song Spindle + disc 1 |
| s3 Song Discs | places the **Song Spindle**, decodes discs with the **note-key** minigame | three discs decoded |
| s4 Alien blueprints | researches the hidden blueprints, builds the **Resonance Blaster** and the **obsidian armour**, opens a **sound chamber** under a monument, inserts the hearing disc | the first voice is heard |
| s5 The Echoes | questions **Elora**, **Kamo**, **Feya** and **Tini** with melodies | the truth about the Great Song |
| s6 The Song of Return | plays **ORNEMIS** in the note alphabet at a singing shrine (the Singing Shrine or a ring) | Ornemis on the player's star map |
| s7 Homecoming | jumps to Ornemis, lands on Soaniri, meets **the Eldest** in Mirun | the Young Voices are at the Choir Tower |
| s8 The Bridge Song | persuades **Roll**, plays the counter-voice at the **Choir Tower**'s eight pipes, then wins the **sing duel** with a Soundi | the Soundi travels with the player |

Nobody dies, nobody in the story is a villain, wrong notes never punish, every place-bound step has a fallback (no shrine → the
ring; no monuments here → another world; the Echoes come to any group of three or more rune stones, even placed ones). The
Soundraks are the one danger on Soaniri (#3424) — wild animals, not characters: no story step needs one or waits for one, a
scream always warns before they attack, the usual flee and respawn rules apply, and from chapter 4 on the calm disc and the
Resonance Blaster calm them.

**Finding the places (#3415).** Every "where" step (`onStart.reveal`) marks its place 5 s after its start line (the pack's
`escalate.markerAfter`), because the hints say "I marked it". The shrine steps (s2.1, s5.1, s6.2, reveal
`unique_site:shrine`) first mark the nearest complete **rune shrine** of the player's world (#3416, `NearestRuneShrine`;
on the unique shrine's own world the nearer of the two). Without one — an older world, a planet type without them — they
show the way to the Singing Shrine, which exists once per galaxy, on one world: they name its world and system from another
system, make its world the flight target inside its system and mark the shrine on its world. They run only while the player can reach the shrine
(`world:shrine_reachable` — same system, a jump generator, a landing there before or instant travel); otherwise s2.1a, s5.1a
and s6.2a stand in (the ring, any rune stones); they mark the nearest complete rune shrine of the player's world
(`nearest_rune_shrine`) and say nothing when there is none — no far place. The texts speak of "a singing shrine": the Shrine of the Eight Voices is the
oldest and biggest, a ring of eight rune stones works the same.

**Skips never strand the player (#3425).** A skipped step (or chapter) hands over, once, what playing it would have left
with the player — `GameServerSoundersStory.CatchUp.cs`, a table in story order, run by the 1 Hz story-pack pass
(`TickStoryPacksFor`), so an old save that is already stuck heals on its next join too. Chapter 2 through without the
shrine's reward: the Song Spindle (unless one is carried) and disc 1 (`snd:shrine_reward`). A skipped decode step (s3.2,
s3.4, s4.5) counts as decoded — `snd:disc:<disc>` with everything a decode brings: the letters in the note table
(`KnownSounderLetters`), the disc's Song Archive page, the hidden blueprint it carries in research, and the story loot moving
on to the next disc. This is deliberate: a skipper gets what the skipped steps would have given, and without it chapter 4
(research the blueprints the decodes show) and the note-key game of later discs would stand on nothing. s3.4 also hands over
the builders and the hearing disc (the chest socket's disc chapters 4 and 5 need), s4.4 the Great Song disc — each only when
not decoded and not at hand (`NextMissingDisc`), and once (`snd:catchup:<disc>` — a disc in a spindle is not at hand). s4.2: the two blueprints are unlocked. s4.3 / s4.6: one Resonance Blaster /
obsidian chest piece unless one is carried (`snd:catchup:<item>` makes it once). Chapter 6: `snd:song_learned` (Ornemis on the
star map). s8.1: `snd:roll_convinced`; s8.2: `snd:bridge_done` (the sing duel can start). A player who played a step has its
milestones and never gets a second item. The catch-up runs only while a chapter of the story is still ahead, so "Skip all"
hands out nothing and the sing duel's Soundi (the last step) stays the prize of playing it. VEGA says what came
(`srv.sounders.catch_up_items`, the item and blueprint names in the player's language) or that the gaps were filled
(`srv.sounders.catch_up`). A disc that is no story disc (a socket disc, a collectible) sung on a spindle while a decode step
runs only sings its tune — the server names the disc the story waits for: `srv.sounders.wrong_disc` when the player carries
it, `srv.sounders.wrong_disc_find` (the relic caches) when not (`HintStoryDisc`, `WantedStoryDisc`).

## 3. Mechanics and where they live

| Mechanic | Code | Docs |
|---|---|---|
| Note alphabet, rune words | `Shared/Sounders/SounderAlphabet.cs` (`SounderAlphabet`, `SounderLexicon`), `SoundersIds` | this doc §4 |
| Use key on blocks | `UseBlockIntent` (323) → `GameServerBlockUse.cs` (per-block-key handlers) | — |
| Melodies at a place | `NoteSequence` (324), `GameServer.PlayNoteSequence` | [SOUND_DESIGN.md](SOUND_DESIGN.md) |
| Sounder instruments 4/5, rune stones | `ProceduralAudio`, `GameServerRunes.cs` | [SOUND_DESIGN.md](SOUND_DESIGN.md), [CRYSTAL_NET.md](CRYSTAL_NET.md) |
| Melody puzzles, melody sites, the melody bar | `MelodyPuzzle*`, `GameServerMelodyPuzzles*.cs`, `HudUi.MelodyBar*` | [STORY_IMPLEMENTATION.md](STORY_IMPLEMENTATION.md) |
| Rune shrines (#3416, worlds from terrain generation 36): the generated ruins the rune stones stand in — a complete one is a ring of eight around an ancient-brick altar, i.e. a melody ring the s2/s6 steps accept; `NearestRuneShrine(from)` gives the nearest complete one's altar | `WorldGenerator.RuneShrinesGen36.cs`, `GameServerRuneShrines.cs` | [WORLD_GENERATION.md](WORLD_GENERATION.md) §52 |
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

**Spelled on the melody bar (#3426).** A kid read "play ORNEMIS" as the note names O-R-N-E-M-I-S — only the E is a key.
A melody puzzle whose melody spells a word carries it (`MelodyPuzzleSpec.Word`; the Song of Return and the Bridge Song set
ORNEMIS), and the bar's state sends it with the two tones the player plays per letter (`MelodyPuzzleState.Word` /
`WordTones`, new map keys — an older client skips them, an older server sends none and the bar shows its usual key hint).
The bar shows `O = F E · R = G D · N = F D · E = D C · M = F C · I = E C · S = G E` in the hint row, named with the
locale's `ui.note.names` (German H for B), the letter the player is at (right notes ÷ 2) in the chip yellow; a result
line then flashes in the title's place. The Bridge Song's groups are the counter-voice the player plays (a fifth up: O =
C' B). `Shared/Sounders/MelodySpelling.cs` builds both the tones (the server) and the line (the client,
`MelodyBarModel.SpellingLine`), so they always equal the puzzle's expected tones. The s6 texts say every letter is TWO notes
and the bar shows them.

## 5. Per-player milestones

The runtime owns `story:sounders:…`. The pack's own flags live under `snd:`:

| Milestone | Written when |
|---|---|
| `snd:first_rune` | any rune stone was played by the player (pack-independent; the pack's start rule waits for it) |
| `snd:word:<WORD>` | a rune word was heard (the Song Archive shows it while the pack runs) |
| `snd:disc:<item>` | a story disc was decoded (or its decode step skipped, #3425) |
| `snd:got:<item>` | a story disc was handed out (loot, a chamber, the recorder, the shrine, the skip catch-up) |
| `snd:shrine_reward` | the shrine (or a ring) gave the Song Spindle and disc 1 — or chapter 2 was skipped and the catch-up did |
| `snd:catchup:<item>` | the skip catch-up handed over a disc a skipped step would have found or a piece it would have made (once) |
| `snd:echo:<id>` | an Echo was questioned to the end |
| `snd:song_learned` | the Song of Return was played (or chapter 6 skipped) — Ornemis shows on this player's star map |
| `snd:roll_convinced` / `snd:bridge_done` | Roll was convinced / the Bridge Song was sung (or the step skipped) |

## 6. Assets

Sounds, textures, icons and music were generated with the repository's asset scripts (maintainer approval 2026-11-07)
and are listed in [NOTICES.md](../../NOTICES.md); the Soundi's calls are the animated series' approved calls. The notes
themselves are procedural (`note_<instrument>_<tone>`), never files. The moss and the five singing plants were recoloured into
light green and light blue (#3423) by the deterministic `tools/ai-assets/recolour_soaniri.py` (no model; it reads the generated
tiles from the commit that bundled them, so it gives the same tiles however often it runs).
