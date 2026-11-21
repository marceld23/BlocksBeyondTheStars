# The other side

> **Status: how-it-works note** (epic #3891). This file is shared by two areas: the other-side core (the state, the
> gates, what a player may do, who sees whom — `GameServerOtherSide*.cs`) and the sound layer below. The core's sections
> belong above this one.

The other side is the Qualis' world. The Sounders who sang the Great Song live there (the Echoes); the Echoes call the
Qualis "the Lights". Canon: [STORY_SOUNDERS.md](STORY_SOUNDERS.md) §1. The grey look, the transition and the HUD mark
are the client's part (#3906); the story chapters that use the other side are s9–s11 of the Sounders' Song (#3909,
#3910). Live Done/Open status: [../../TODO.md](../../TODO.md).

## The core

*Issues #3904 (the state, the gates, what a player may do) and #3905 (who sees whom, who lives there). Code:
`GameServerOtherSide.cs` (state, API, join and leave, the watch, the admin verb), `GameServerOtherSide.Gates.cs` (what a
player may do), `GameServerOtherSide.Visibility.cs` (who sees whom, the two creature lists),
`GameServerOtherSide.Folk.cs` (the Qualis and the Sounders at the gates), `Networking/Messages/OtherSideMessages.cs`
(the state message).*

### A state, not a place

The other side is **the same world, everywhere**: the same blocks, the same coordinates, in white, grey and black for
whoever is over there. So a player who crossed is in a **state** — `PlayerSession.OtherSide` — and never in another
world, location or instance. No second world is loaded, generated or rendered.

| What | Where |
|---|---|
| The flag | `PlayerSession.OtherSide` (session state). `GameServerOtherSide*.cs` is its **only writer**; everybody else reads it. |
| The same fact on the player record | the milestone `snd:over` (`GameServer.OtherSideMarker`), written and removed together with the flag. The damage gate reads it (`OverThere(PlayerState)`), a story condition can (`milestone:snd:over`), and a save written meanwhile is recognised at the next join. |
| The first crossing | the milestone `snd:crossed`, kept for good. |
| The entry gate | `PlayerState.OtherSideGateLocation` (the world), `OtherSideGate` (the centre of its rune stones — the marker) and `OtherSideEntry` (where the player stood — the spot a forced return and the next join use). Additive, saved (`PlayerSnapshot`), empty on our side. |
| The capability | the join word `otherside1` (`PlayerSession.SupportsOtherSide`). A client without it can never cross. |

### Gates, crossing, coming back

A **gate** is a group of three or more rune stones within 8 blocks (`IsGate`, the detection the Echoes of chapter 5
use — `TryFindRuneGroup`). Monuments, rune shrines, the Singing Shrine and the Choir Tower are gates; three placed rune
stones make one.

The use key on a rune stone runs `UseRuneStone` as always — the stone sings its word — and then `OtherSideOnRuneStone`:

- **On our side**, with `song_disc_crossing` in the worn obsidian chest's socket (`HasActiveSocketDisc`), a stone of a
  gate takes the player over (`TryCross`).
- **Over there**, a stone of a gate brings the player back where they stand (`TryReturn`). The disc is not needed.
- **A lone stone only sings**, on both sides, and nothing is said.
- A player who carries the disc but does not wear it in the socket is told so at a gate; every other refusal of a
  crossing is told too. **On our side**, a stone that is a pillar of a melody puzzle the player is playing strikes its
  tone instead and never reaches this code. **Over there** a stone is never struck as a pillar (`UseRuneStone` skips
  `TryStrikeMelodyPillar`): the use key is the way home, also at the Singing Shrine or a ring, also while a mend game
  runs — puzzles played over there take their tones from the melody bar.
- Two flips of one player are at least 2.5 s apart, so a held key does not cross and come straight back.

A crossing is refused with one of these locale keys (they are also the `out` of `TryCross`):

| Key | When |
|---|---|
| `srv.otherside.no_client` | the client did not announce `otherside1` |
| `srv.otherside.not_now` | the player watches (an observer, a fallen player), waits for the respawn choice or is dead |
| `srv.otherside.not_here` | in flight, on a spacewalk, in a ship interior or aboard the ship — planets and stations only, on foot |
| `srv.otherside.not_seated` | seated, in a vehicle or on a train |
| `srv.otherside.no_gate` | no gate at this stone |
| `srv.otherside.no_disc` | the crossing disc is not in the worn chest's socket |

Both flips raise a game event (`otherside:crossed`, `otherside:returned`; argument: the body id, so a story step can
ask for a crossing on one world), save the player — once: the save of the stone's word is folded into it — and send the
state message.

### The API (for the story and the sound layer)

```csharp
bool TryCross(PlayerSession s, Vector3i runeStone, out string refusalKey);   // every rule above
bool TryReturn(PlayerSession s, Vector3i runeStone, out string refusalKey);  // only a gate at the stone
void ForceReturn(PlayerSession s, string reason);          // back on our side AND at the entry spot
bool IsGate(Vector3f at, out Vector3f centre);             // on the active world
IEnumerable<PlayerSession> PlayersOverThere();             // of the active world
Vector3f? EntryGateOf(PlayerSession s);                    // the gate's centre, null on our side
int SpawnOtherSideEcho(string characterId, Vector3f at);   // a story Echo only players over there see
void ResendOtherSideState(PlayerSession s);                // the state message again (the sound-block count moved)
static bool OverThere(PlayerState p);                      // for code that has a record and no session
static bool HiddenFrom(PlayerSession viewer, PlayerSession subject); // the visibility rule
```

All of them are `internal` to the server assembly and expect the player's world under the cursor (`_worlds.Active`),
as every handler has it.

### Leaving, crashing, being moved

- **Leaving the game or losing the connection over there**: the leave step `other side` (`LeaveTeardownSteps`) puts the
  record at `OtherSideEntry` and clears the marker before the save.
- **A crash, or an autosave written over there**: the record still carries `snd:over`. `LoadJoiningPlayer` undoes it
  (`UndoOtherSideInSave`) — the player stands at the entry gate on our side — and the join says so
  (`OtherSideReasons.Rejoined`, `srv.otherside.rejoined`). Nobody ever joins over there.
- **Something else moves the player**: an admin teleport (`SnapPlayerTo`, `/tp`, `/tpp`), a world change
  (`HandleTravel`) and a forced respawn (`RespawnPlayer`) call `OtherSideBeforeMove` first — the player is back on our
  side, silently, where the mover then puts them (`OtherSideReasons.Moved`). Behind those hooks a watch runs once a
  second (`TickOtherSide`): a player over there who is in flight, aboard, seated, riding, watching, dead or on another
  world than the gate's is brought back the same way, so a mover nobody thought of cannot leave a player over there in
  a place the rules do not allow.

### What a player may do over there

`Dispatch` applies `OtherSideAllows` while the flag is set — an **allow-list** beside the observer's, the fallen's, the
pause's and the dead body's. An intent that is not named — every future one — is refused.

| Group | Intents |
|---|---|
| Moving | `MoveIntent`, `FarTerrainTileRequest`, `SelectHotbarIntent`, `SetJetpackIntent`, `SetGlidingIntent`, `ReleaseRopeIntent`, `SetLampIntent`, `SetSeatedIntent` (standing up only) |
| Talking | `ChatIntent`, `DirectChatIntent`, `VoiceFrame`, `PlayerEmoteIntent`, `FxIntent` (the last two are relayed only to who is over there too) |
| Control plane | `PauseIntent`, `SaveGameIntent`, `BumpReport`, `CallAdminIntent`, `PlayerReportIntent`, `AdminCommandIntent`, `AdminModeIntent`, `AdminPanelIntent`, `AdminJumpIntent`, `AdminReportAction`, `TradeCancelIntent`, `RespawnChoiceIntent` |
| The look | `SetAppearanceIntent`, `SetFaceIntent`, `SetBodyPaintIntent`, `SetToolLookIntent` |
| Reads | `RequestStarMap`, `RequestMissions`, `RequestCompanionsIntent`, `RequestAllianceListIntent`, `RequestLandingPadsIntent`, `RequestKnownNpcsIntent`, `RequestBaseResidentsIntent`, `LocateStationIntent`, `CrewActionIntent` and `MarkerActionIntent` (`list` only), `YieldIntent` (status only) |
| The Journal and the stories | `StoryPackIntent`, `StorySelectIntent`, `CutsceneSeenIntent`, `RequestStoryResolutionIntent`, `UiTutorialEvent`, `PlaytestWatchIntent`, `SetNpcCallsIntent`, `SetFestivalPrefsIntent` |
| The own backpack and equipment | `MoveItemIntent`, `EquipItemIntent`, `UnequipItemIntent` (the disc may be taken out) |
| The Sounders' things | `UseBlockIntent` **only on a `rune_stone`**; `TalkToNpcIntent` **only with an NPC that lives over there**, `NpcDialogChoiceIntent` in such a talk (leaving a talk always); `UseGadgetIntent` **only for the Resonance Blaster in the hand**; `MelodyNoteIntent` |

Everything else is dropped and answered with **one** system line, `srv.otherside.refused` ("Over here you can only touch
the rune stones."), at most once every four seconds per player. What a client sends by itself — `FallDamageIntent`,
`SpeederImpactIntent`, `TransitLaunchDoneIntent`, `NetFragmentFoundIntent` — is dropped without the line.

A feature built on the other side opens what it needs through the hook
`partial void OtherSideAllowsMore(PlayerSession s, object message, ref bool allowed)`; it is asked only for what the list
refused. The list itself stays the place where "allowed over there" is decided — `OtherSideTests` pins how many of the
handled intents get through.

### No needs, no harm

- The per-player survival tick leaves through `HoldVitalsOverThere` right after the god-mode check: oxygen, hunger,
  health and the exposure meter stand still — no air, no hunger, no cold or heat, no lava, no weather. The jetpack and
  the cloak were ticked before that point, so flying still costs suit energy.
- The one damage gate, `PlayerTakesDamage`, asks `OverThere`. With it go every hit, a fall, PvP, a crash — and
  `HostilesIgnore`, the question every hostile's choice of target asks: machines, bandits, giants and aggressive or
  provoked animals look through a player who is over there.

### Who sees whom

A player over there is **withheld** from everything our side is told about them — where the data is produced, like an
observer; a cloaked player is only flagged, and this is not that. Players over there see each other and everything of
our side. Chat and voice stay visible to everybody.

The rule is one predicate, `HiddenFrom(viewer, subject)` (the subject is over there and the viewer is not). The sites:

| Site | File |
|---|---|
| The 10 Hz presence stream (per viewer; the viewer signature counts a crossing, so the next beat is a full resend) | `GameServerPresence.cs` `TickPresence` |
| The presences a newcomer is sent | `SendExistingPresences` |
| The face, every body painting and every tool look — live changes and what a newcomer is sent | `BroadcastFace`, `BroadcastBodyPaint`, `BroadcastToolLook`, `SendExistingFaces` |
| A gesture, a flinch | `GameServerEmotes.cs` `BroadcastPlayerEmote` |
| The effect of an action (a swing, a shot, a gadget's outcome); people nearby are not startled | `GameServerActionFx.cs` `SendFxNearby`, `HandleFxIntent` |
| A tone of the melody bar and a puzzle's notes | `GameServerMelodyPuzzles.cs` `SendNotesAround` |
| The star map's player list. A flip only marks it stale: the message has no players-only part, so the 1 Hz watch sends the map once per second at most (`FlushOtherSideStarMaps`), and only to those whose list changed — everybody on our side and whoever flipped; a player who stayed over there sees everybody before and after | `StarMapPlayersFor` (from `SendStarMap`) |
| An ally's teleport to the player ("not here") | `GameServerTeleport.cs` `TeleportToPlayer` |
| A trade request ("too far") and a duel challenge ("gone") | `GameServerTrade.cs` `RequestTrade`, `GameServerPvp.cs` `ChallengeDuel` |
| The bump snapshot's nearby players (it can reach the report inbox) | `GameServerBump.cs` |
| VEGA's "a pilot nearby" and the story condition "a player is near" | `GameServerShipAiContext.cs`, `GameServerStoryConditions.cs` |

The world's own senses skip a player who is over there at their gathering points:

| Sense | File |
|---|---|
| Slide doors | `GameServerDoors.cs` `GatherDoorTargets` |
| Crystal Net presence sensors | `GameServerCrystalNet.cs` `GatherCrystalPresence` |
| Animals (nothing flees, hunts or begs; fauna still lives around the player) | `GameServerCreatures.cs` `FillCreatureTargets` |
| Giants listening for steps | `GameServer.cs` `HandleMove` (`GiantsOnPlayerMoved`) |
| Villagers greeting a passer-by | `GameServerNpcPassing.cs` |
| The people of our side: a village's chatter audience and deeds, a festival's hero, the market and festival gathering on a plaza, a keeper turning to a customer, the streamer's question, a wanderer or a pioneer walking up, visitors to a base, a trader landing nearby | `GameServerVillageLife.cs`, `GameServerVillageFestival.cs`, `GameServerPlaceEvents.cs`, `GameServerVendorTrade.cs`, `GameServerProfessions.cs`, `GameServerWanderers.cs`, `GameServerPioneers.cs`, `GameServerBaseVisitors.cs`, `GameServerSpaceTraders.cs` |
| What hunts or wakes by presence: robbers picking a mark, a hunter camp, a sleeping guardian giant, Ressaw blocks cracking beside a lingering player | `GameServerBandits.cs`, `GameServerHunterSites.cs`, `GameServerGuardianGiants.cs`, `GameServerRessawBlocks.cs` |
| Drops on the ground | `GameServerDropPackets.cs` |
| Weather (no storm drains or charges the suit) | `GameServerWeatherEvents.cs` |
| Companions (a pet with no owner in sight would give the place away: it waits, as for an observer, and is back with the return) | `GameServerTaming.cs` `ReconcileCompanions` |
| Soaniri's singing plants (no echo flower sings a tone from over there back to our side, no bell bloom opens, Ornasa's bloom trail does not move). A mended crack's bloom is the story's own answer and not this | `GameServerSingingPlants.cs` `HearSingingPlants` |

The handshake of a flip (`HideFromOurSide`, `ShowOnOurSide`): at a crossing our side gets a `PlayerLeft` for the avatar
and the players already over there show the newcomer their looks; at a return the one who came back gets a `PlayerLeft`
for everybody still over there and our side gets their looks again. The presence itself follows with the next beat.

A rune stone a player over there uses is heard on both sides: the stones sing, and where they stand the wall is thin.

### Who lives there

Animals and figures that exist only over there carry `OtherSideOnly` (`CombatEntity`, `ServerNpc`) and are sent only to
the players who are over there. The players over there still get everything of our side, unchanged.

- **The creature list** is one broadcast per world. While an animal of the other side lives on it, the flush builds two
  lists — our side's, and our side's plus the other side's — each encoded once (`SendCreatureListBySide`, `SendBySide`),
  never per player. The 7.5 Hz motion stream and the per-player list (`SendCreatures`) apply the same rule. Such animals
  are outside the world's fauna cap.
- **The NPC list** is built per receiver anyway (`NpcsInReachOf`). A player on our side who names such an NPC in a talk is
  told nobody is there, its gestures go only to players over there (`BroadcastNpcEmote`), and it greets no passer-by.
- **The Qualis**: while a player is over there on a planet, 2–4 drift near them (a group shares them, never more than 8
  in a world). They are the Quali record as a species of its own (`au_quali_over`: hover band 14 blocks, awake at any
  hour), known to the creature tick but never in the world's roster, so the spawner cannot bring one to our side. The
  creatures' own drifter code moves them; they are untouchable like every Quali; one farther than 56 blocks from every
  player over there is gone. The Qualis never speak. A station has none (a void world has no ground life).
- **The Sounders who crossed over**: at a gate near a player over there stand one to three Echo figures (look
  `sounder_echo`), gone when nobody over there is within 110 blocks. Their number and names
  (`OtherSideFolkNames` — invented, language-neutral, none of the story's) come from the world seed and the gate, so a
  gate has the same folk every time. They carry the character id `snd_over_folk`, and the engine dialog of that key
  (`data/dialogs.json`, first in the file so it wins over a settler's small talk) answers the use key.
  A gate is found where the server already knows one may stand — the entry gate, a monument, a melody site, the nearest
  rune shrine within 96 blocks — and around the player (the 17³ scan the Echoes use), one player and at most three scans
  every two seconds. Stones a player placed far from any known site therefore get their folk when somebody over there
  walks up to them.
- **The four Echoes of the story** are not placed by this code. The story calls `SpawnOtherSideEcho(characterId, at)`
  (the figure is named after the id, `elora` → "Elora") and takes it away with `RemoveSounderNpc`. NPCs are runtime
  state: place it again when the world was loaded anew.

### The message

`OtherSideState` (NetCodec id 362, server → client, only to the player it is about):

| Field | Meaning |
|---|---|
| `bool Over` | on the other side |
| `bool HasGate`, `float GateX, GateY, GateZ` | the entry gate's centre, for the marker on compass and map — set exactly while `Over` |
| `string Reason` | `OtherSideReasons`: `sync` (the join, a count), `crossed`, `returned`, `forced` (`ForceReturn` — the client was also snapped with a `RespawnNotice`), `moved` (something else moved the player), `rejoined` (left the game over there), `admin` |
| `int SoundBlocksUsed`, `int SoundBlockCap` | the sound layer's meter |

It is sent at every join (to a client with `otherside1`) and on every change. No protocol bump: an older client drops
it. `NetworkClient.OtherSideStateReceived` hands it to the client.

### The gate list

*Issue #3906 (server part). Code: `GameServerOtherSide.GateList.cs`; the hook's implementation is the story's
(`GameServerOtherSideCracks.cs`).*

Over there everything is grey, so a door home must be recognisable from far away — and only the server knows where
gates are. `OtherSideGates` (NetCodec id 364, server → client) lists them to a player **who is over there** and to
nobody else; the client stands a tall light over each (see "The client").

| Field | Meaning |
|---|---|
| `NetOtherSideGate[] Gates` | nearest first, at most 24; empty at the return |
| `float X, Y, Z` | the gate's centre (the middle of its rune stones) |
| `bool Cracked` | a crack runs through it (the story's hook) |
| `bool Entry` | the gate this player came through |

**The set** is what the server knows without searching the world, within 400 blocks (horizontally, across the seams):

- the **entry gate** — always, however far;
- the **rune groups the folk pass found** in the loaded chunks around the players (`OtherSideWorld.Gates`), checked
  against the blocks;
- **monuments** whose generated relic carries a rune group (`MonumentInstance.RuneGate`, counted once from the
  structure when the instance is registered — `LargestRuneGroup`, the rule of `TryFindRuneGroup` on a list);
- **complete rune shrines** no stamp was built over (seed arithmetic, `WorldGenerator.RuneShrinesNear`);
- the **melody sites** of the world. The Singing Shrine is listed from afar (its pillars are rune stones); any other
  site — a ring of other stone, the Choir Tower's crystal pipes — is a gate only when the blocks around it say so.

A known site whose surroundings are loaded is checked with `IsGate` (stones a player mined are no gate, and the real
centre replaces the nominal one); a site far away is taken at its word. Two candidates closer than 14 blocks are one
gate. No chunk is loaded and nothing is scanned beyond the 17³ box the Echoes' detection reads.

**When**: at the crossing; then from the 1 Hz watch (`TickOtherSideGateLists`, after the folk pass) when the set may
have changed — the folk pass found or lost a gate, or the player moved 64 blocks — at most once every 3 s, and never a
list equal to the last one; once more, empty, at the return (`OtherSideGatesOnReturn`).

**Cracks**: `partial void OtherSideGateCracked(string locationId, Vector3f centre, ref bool cracked)` is asked for
every listed gate (its defining declaration is in the gate list's file; without an implementation nothing is cracked).
`ResendOtherSideGates(PlayerSession s)` sends the list again at once — the story calls it after a crack was mended.

`tests/BlocksBeyondTheStars.Tests/OtherSideGateListTests.cs` covers the crossing and the return, a gate the player
walks up to and one whose stones are mined, the pace, the explicit resend and the rune-group rule.

### The hooks between the core and the sound layer

- `partial void SoundLayerSideChanged(PlayerSession s)` — the core calls it right after the flag flipped, in either
  direction.
- `partial void FillSoundBlockCount(PlayerSession s, ref int used, ref int cap)` — the core calls it for every state
  message.
- `partial void SoundBlockCountChanged(PlayerSession builder)` — the sound layer calls it when a count moved; the core
  sends the state again.

The defining declarations of the first two are in `GameServerSoundLayer.Hooks.cs`, the third's in
`GameServerSoundLayer.cs` (C# allows one defining declaration per partial method).

### QA and tests

`/otherside` (behind the cheat gate) flips the state for the admin without disc and gate; `/otherside on` and
`/otherside off` set it. The place the admin stands at is the entry point. The capability word still holds.

`tests/BlocksBeyondTheStars.Tests/OtherSideTests.cs` covers the rules of a crossing, the allow-list at the type level
and through the real dispatch, needs and harm, the entry gate, leaving and a save written over there, the movers, the
admin verb, the events and the message; `OtherSideVisibilityTests.cs` covers presence, looks, gestures, the star map,
the world's senses, the Qualis and the Sounders at the gates and a story Echo.

## The sound layer

*Issues #3907 (the layer) and #3908 (shaping with the Resonance Blaster, server half). Code:
`GameServerSoundLayer.cs` (store, rules, helpers), `GameServerSoundLayer.Chunks.cs` (the second chunk variant, who is
told), `GameServerSoundLayer.Blaster.cs` (shaping, the palette), `GameServerSoundLayer.Hooks.cs` (the two hooks the core
calls), `Shared/Sounders/OtherSideRules.cs` (the numbers both sides share), `Persistence/*SoundLayer.cs` (the table).*

A **sound block** is sound, not matter: a song that stays where it was sung. Players shape them over there with the
Resonance Blaster; nobody on the real side may ever receive one.

### Where they live

Sound blocks are **not in the world's block grid** and never a row of `block_edit`. Each resident world carries a sparse
second layer (`LoadedWorld.SoundLayer`: chunk coordinate → local cell index → glow colour and builder), read from the save
the first time anything asks for it and dropped with the world. Its table is `sound_edit (planet, x, y, z, glow, owner)`,
keyed by the cell, in all three repositories (SQLite `WITHOUT ROWID`, PostgreSQL, and the in-memory save's snapshot list
`SoundCells`). `planet` is the location id, so a station world has its own layer like a planet.

Because the layer is not the grid, **sound blocks are rigid by construction**: fluids, fire, falling blocks, flora
regrowth, snow, NPC paths, creatures, machines, the base air fill and the far view all read the real grid and never meet
one. Real-side creatures and machines pass through them.

### Two variants of a chunk

All players over there see the same thing, so a chunk has exactly two variants: the real one and *real + sound blocks*.
On-foot movement belongs to the client and the server trusts the reported position, so over there a sound block is an
**ordinary block inside the chunk message** (`sound_block` with a per-voxel glow modifier) — meshing, collision and light
need no client work.

- `StreamChunkNow` (the one place chunk data leaves the server — the streaming pass, the arrival pre-load
  `StreamFootingNow` and every re-stream go through it) asks `session.OtherSide` first. A real-side player never reaches
  the sound layer: no lookup, no allocation.
- Over there, a chunk **with** sound cells is composed — the real chunk's cells with `sound_block` written into the sound
  cells that are air on the real side, plus the glow per cell — and cached beside the real chunk's entry, keyed by
  `(coord, chunk.Version, SoundVersion)` with the same cap (1024). `SoundVersion` is a per-chunk counter the layer bumps
  on every cell set or removed.
- A chunk **without** sound cells has no second variant: both sides are served from the one real cache entry
  (`SoundLayerTests.AChunkWithoutSoundCells_IsServedFromTheRealCache_ForBothSides` pins the counters).
- A sound cell that a real block stands in right now (snow, water, a stamp) is **hidden**: the composed chunk carries the
  real block, the cell stays in the layer and the save. See "Hidden, not dissolved" below.

### Who is told

- A sound cell set, removed or shown again is a normal `BlockChanged` (`sound_block` + glow, or what the cell holds on
  the real side) sent with `BroadcastToOtherSide` — to the players of that world who are over there, encoded once, and
  to nobody else.
- The cues `sound_shape` (pitched to the tone, `SoundFx.Pitch`) and `sound_dissolve` play at the cell, again only over
  there. An unknown cue id is silent on the client (`ClientAudio.At` returns when the clip is missing).
- **A change of side** (`SoundLayerSideChanged`, called by the core right after the flag flipped) re-sends exactly the
  chunks of the player's sent set that carry sound cells, in the variant of the new side — in both directions.
- `sound:shaped` is raised for the builder (argument: the tone 0–7).

### The rules

| Rule | Where |
|---|---|
| Only in a cell that is air on the real side and holds no sound block; inside the build band | `SoundShapeRefusal` |
| Not in the base zone (or base pad square) of a player who is no ally — the check real edits use, `IsBaseProtected`. Protected structures only guard real edits: sound may stand on or beside them | `SoundShapeRefusal` |
| Not in a body: nobody's head cell, and nobody's feet cell but the builder's own (who is lifted, as with a real block) | `SoundCellHoldsABody` |
| **Nobody is walled in.** Not within one cell of the body of ANOTHER player over there (the 3×4×3 box around their feet and head, `SoundBodyMargin`) unless that player is the builder's ally — nine blocks of a stranger would be a cage: its prisoner cannot dissolve them, cannot mine and cannot reach a rune stone | `SoundCellHoldsABody` |
| **A gate's stones stay free.** Not in a cell that touches a `rune_stone` (its six neighbours, the cell above it among them) | `SoundCellAtRuneStone` |
| `OtherSideRules.SoundBlockCap` (2000) per player and location; one more is refused with `srv.otherside.sound_cap` | `SoundShapeRefusal` |
| Only the builder, their allies (`AreAllied`: alliance or crew) and admins dissolve one — `srv.otherside.not_yours`. **The exception:** anybody dissolves a sound block that stands in or within one cell of their own body (the same 3×4×3 box), whoever built it — so a player who walked into a build, or was caged in spite of the rule above, sings their way out | `TryDissolveSound` |
| **The real side wins**: a real block a **player** writes into the cell (a write that names an owner) dissolves the sound block for good — layer, save, the `sound_dissolve` cue | `OnPlayerBlockSetOverSound` (`ServerWorld.PlayerBlockSet`) |
| Any other real block in the cell — snow, flowing water, falling sand, a regrown plant, fire, a worldgen stamp — only **hides** the sound block | `OnRealBlockSetOverSound` (`ServerWorld.BlockSet`) |

Everything else that cannot be done is told as `srv.otherside.sound_blocked`.

### Hidden, not dissolved

Weather and nature must not eat what a player sang. A real block nobody built that lands in a sound block's cell hides
it and nothing else:

- The cell stays in the layer and in `sound_edit`, and it still counts toward its builder's cap. `IsSoundBlock` answers
  true for it and `SoundCellsNear` lists it; the cell cannot be shaped again while it is taken.
- While the real cell is not air, players over there see the real block there — in the composed chunk and through the
  ordinary `BlockChanged` every side gets.
- When the real cell is **air again** (the snow melted, the water ran off, a player dug the snow away), the hook queues
  the cell (`SoundLayerState.Reappear`). The real `BlockChanged` of air goes out after `SetBlock` returns, so the sound
  block is told afterwards: the per-world tick step `FlushSoundLayer` (right before `StreamChunks`) sends the sound
  `BlockChanged` to the players over there and moves the chunk's `SoundVersion`. With nothing queued the step is one
  field read. The same path re-tells a sound block after a tool wrote air into a cell that was air all along.
- `TryDissolveSound` still dissolves a hidden sound block (the server accepts `Alt` aimed at the real block that hides
  it); a client that only sends `Alt` on sound blocks it can see will not get there.

### Shaping with the Resonance Blaster

Over there the gadget table hands the blaster's pulse to `ShapeSoundWithBlaster` **before** its energy check: shaping
costs no suit energy, has its own cadence (`OtherSideRules.ShapeCooldownSeconds`, 0.2 s; a refused pulse holds the blaster
for 1 s so the refusal is one line), sends no gadget outcome and no pulse sound — nothing a real-side player could
notice. It does not push and opens no chamber. On the real side the blaster is unchanged.

**The cadence is a schedule, with one tick of tolerance.** Intents are handled once per server tick (1/15 s), and the
client pulses every 0.22 s while the trigger is held — so two pulses arrive three or four ticks apart, and a three-tick
gap can fall just short of 0.2 s. A check of "now ≥ ready" swallowed about every third pulse of a held trigger without
a word. So over there the gadget table takes a pulse up to `OtherSideRules.ShapeJitterToleranceSeconds` (0.07 s, one
tick) early, and a shaped block moves the next-ready time on by one cooldown **from when the pulse was due**, not from
now (the held beam's pattern, `BeamWeaponRules.JitterToleranceSeconds`): a client that pulses every tick still gets
five blocks a second on average and never more (`SoundShapingTests.TheCadence_…`).

The client sends the aim point, as for every gadget, and one additive field: **`UseGadgetIntent.Alt`** (bool, default
false; an older client never sets it, an older server drops it). It picks one of two actions — shaping and dissolving
are never decided by what is aimed at. The server looks at two cells on the eye's ray: the point nudged 0.05 **into**
the surface and the point nudged 0.05 **toward the eyes**.

- **`Alt == false` — shape (the use key).** Always shapes. If the *into* cell is a surface within the blaster's reach —
  a real block or a sound block — the *near* cell gets a sound block, if the rules allow: walls, stairs and bridges grow
  block by block from either kind of face. With no surface there, the point lies in the open air and the cell it lies
  in gets the block (how a bridge leaves the cliff); a point farther out than the blaster's reach (the item's `range`,
  8 blocks) is pulled back along the ray to that reach first, and the world is not read beyond it.
- **`Alt == true` — dissolve (the mine/primary key on a sound block, blaster in hand).** The sound block in the *into*
  cell dissolves, if it is the player's to dissolve (`srv.otherside.not_yours` otherwise). No sound block there: nothing
  happens — no line, no cadence spent. A real block is never touched.

Either action must be within reach (`WithinReach`, 8 blocks plus the usual slack; `out_of_reach` otherwise). On the real
side `Alt` means nothing: the pulse is the sonic push.

**The palette.** The tone a player played last on the melody bar (0–7, remembered per session from `MelodyNoteIntent`,
0 before any) sets the next block's glow: `OtherSideRules.ToneGlow`, eight brightness steps of one cool white (the look
over there is greyscale). `OtherSideRules.ToneOfGlow` reads the tone back — the client uses it to sound a block that is
walked on — and `PitchOfTone` gives the tone's pitch factor (C major, C4 to C5).

### For other systems

`IsSoundBlock(cell)`, `SoundGlowAt(cell)`, `SoundBlocksOf(playerId)`, `SoundCellsNear(at, radius)`,
`TryShapeSound(session, cell, tone, out refusalKey)`, `TryDissolveSound(session, cell, out refusalKey)` — all on the
active world. `FillSoundBlockCount` feeds the core's state message; `SoundBlockCountChanged` tells the core when a
builder's count moved.

### What the cap costs (measured 2026-11-21, `SoundLayerStoreTests.Measurement_…`)

| | Bytes on the wire | Compose + encode |
|---|---|---|
| A chunk with 0 sound cells (the real chunk, mostly air) | 123 | 25–46 µs |
| … with 100 cells, scattered (worst case for the run-length encoding) | 781 | 54–105 µs |
| … with 1000 cells, scattered | 3671 | 195–471 µs |
| … with 1000 cells in one solid body (a wall, a floor) | 3042 | — |

(Two runs on a developer machine that was building other things at the time; the sizes are exact, the times a range.)
The glow per cell is what weighs — about 3 bytes a cell after LZ4 — not the blocks. The composed variant is built once
per change and cached; every further player over there gets the same bytes. In the save, 2000 cells take 86 KB (43 bytes
each) of `sound_edit`. A full cap of one player is therefore a handful of chunks of a few kilobytes and under 100 KB of
save — the cap bounds clutter and griefing, it is not a technical limit.

### Known edges

- A real block that reaches a sound block's cell without passing `ServerWorld.SetBlock` (a structure overlay applied
  when a chunk loads) hides it like any other, but nothing queues its return: if such a cell ever becomes air again
  outside `SetBlock`, the sound block is back with the chunk's next full send, not at once.
- A write that names a player always counts as "built": a player dyeing or re-placing the snow that hides a sound block
  dissolves the hidden block.
- A per-voxel glow is a light source for every other block; the block data deliberately gives `sound_block` no
  `lightColor`, and the client registers it as "glow is a look" (`ClientWorld.SetGlowLookBlocks`, see "The client"): it
  shines by emission and lights nothing, so 2000 of them cost no flood fill.

## The cracks

*Issue #3909. Code: `GameServerOtherSideCracks.cs` (the state, the mend game, the mend), `GameServerOtherSideCracks.Bloom.cs`
(what the real side answers), `GameServerMelodyPuzzles.FreePlay.cs` (a puzzle that starts from the melody bar). The
story that uses them — chapters 10 and 11 of the Sounders' Song — is in [STORY_SOUNDERS.md](STORY_SOUNDERS.md) §2.*

"Every wall we sang here cracked a wall over there." Every rune gate carries a crack on the other side until somebody
mends it. What a crack **looks** like is the client's (the gate list's `Cracked` flag); the server only knows whether a
gate is whole.

### The state

A mended crack is one string in `WorldMetadata.MendedCracks` (additive, saved with the metadata the moment it is mended):

```
<location>|<x>|<y>|<z>|<reach>[|<site>]
```

— the gate's world, the middle of its rune stones rounded to whole blocks, how far the mend reaches
(`CrackGateReach`, 12 blocks; the great crack of the Sounders' chapter 11 reaches `GreatCrackReach`, 64), and — for a
gate the world itself carries — the **site** its stones belong to. A gate is whole (`IsCrackMended`) when

- its middle lies within a mended crack's reach (so a gate stays mended when a stone is added or mined and its middle
  moves a little, and one mend never covers the next monument), **or**
- its site was mended, wherever the middle was measured then.

The list only grows; it is parsed once per change. The gate list asks through its hook `OtherSideGateCracked` (declared
in `GameServerOtherSide.GateList.cs`, implemented here); the site is looked up only on the active world, and only when
the reach says "cracked" and a site of that world was mended at all.

**Why the site.** The middle of a gate is the mean of the rune stones within 8 blocks of whoever looks. Stones a player
places beside a monument, and another place to stand, move it further than 12 blocks while the monument's stones are
still part of the gate — keyed by the middle alone, the same monument was mended, counted (chapter 10's "three different
gates") and paid again. `WorldGateSite(gate)` (`GameServerOtherSideCracks.Sites.cs`) names the site instead: of the
rune stones around the gate that no player placed (at least three, else there is no site), the structure most of them
belong to —

| Site id | What |
|---|---|
| `shrine` | the Singing Shrine |
| `<kind>@<x>:<z>` | a stamped structure: the box it registered in the footprint registry (`monument@…`, `ruin@…`, `unique_site@…`, `sounder_site@…`, `settlement@…` …; the pad and crash-site reservations are no structures) |
| `rune_shrine@<x>:<z>` | a rune shrine of the terrain (seed arithmetic, `RuneShrinesNear`) |
| `stones@<x>:<y>:<z>` | world stones in none of these (terrain stones of an old world): the first cell of the whole group — the stones found are grown by every world stone within 8 blocks, twice at most, so the id does not depend on which part of the group the look began at |

A site id never holds a `|`. Stones a player placed never count toward a site.

Gates a player built count: three placed rune stones are a gate, and its crack can be mended like any other — it has no
site, keeps the position key and gets no answer from the real side.

### The mend game

Mending **never blocks the way home**: the use key on a stone of a cracked gate still brings the player back — also
where the stones are the pillars of a melody site, also while the game runs (over there a stone is never struck as a
pillar, see "Gates, crossing, coming back"). The game
therefore starts from the melody bar — the melody puzzles' free-play starters (`RegisterMelodyFreePlayStarter`, the twin
of the site starters): a tone played with no puzzle running asks the starters, and `CrackMendStarter` answers when the
player is over there and stands in a cracked gate (`IsGate` at their position; a place that was none is not searched
again for 1.5 s, so a child mashing the keys costs one scan).

The puzzle `crack_mend` is call and response in two rounds: the gate sings a three-tone motif made from its world and its
place (`CrackMotif` — the same gate always sings the same three tones, never one twice in a row), then the motif turned
round; the player answers each with the counter-voice a fifth up, the key to press next glowing on the bar
(`ShowNextTone`). Six tones in all. A wrong tone does not count (#3678); walking 16 blocks away ends it. Players who
play it at the same gate solve it together (the puzzle framework's co-op rule). The gate's tones and the answers are
heard by everybody over there near the gate and by nobody on our side: a prompt or a chorus of a puzzle played over
there follows the rule of the tones (`SendNotesAround`'s `from`, `HiddenFrom`).

### Mended

`MendCrack(gate, reach, solver)`, once per gate and world — for a gate of a world site: once per SITE and world,
whatever stones stand around it and wherever the mender stood (a second mend of the same site returns false: no event,
no milestone, nothing from the real side):

- the key is saved (`_repo.SaveMetadata`);
- the cue `crack_mend` plays at the gate for the players over there (`BroadcastToOtherSide`; an unknown cue is silent);
- every player over there within `CrackMendRange` (16 blocks) of the gate — and the solver wherever they stand — gets
  the per-player milestone `snd:crack:<location>:<n>` (`n`: the crack's number among that world's mended cracks), the
  line `srv.sounders.crack_mended` and the game event `crack:mended` (argument: the key);
- every player over there on that world gets the gate list again (`ResendOtherSideGates`).

### The real side answers

Only at a gate **the world itself carries** (`WorldGateSite` names a site: at least three of its rune stones were not
placed by a player — a monument's, a rune shrine's, a ruin's, the Singing Shrine's, the Choir Tower's): three stones a
player placed mend, but give nothing, so nobody farms gifts with three stones. Once per site and world, since a site is
mended once:

- **The singing plants bloom.** Three to five chime reeds, bell blooms and echo flowers (the harp vine hangs from
  obsidian and has no place on open ground) appear 3–11 blocks around the gate, as ordinary block changes nobody owns.
  A plant takes the topmost open cell of its column near the gate's height, on natural ground (a solid block of the
  category `terrain`) — never in a base zone, a protected structure cell, a settlement (`IsSettlementBlock`) or on a
  cell or ground a player touched last. Fewer, or none, when there is no room; the line `srv.sounders.crack_bloom`
  comes only when something grew.
- **A relic cache refills.** The nearest relic cache of a monument within 40 blocks of the gate that was emptied (an
  emptied loot container is removed; the loot ledger `GeneratedLoot` still knows it) stands there again with ordinary
  relic finds rolled from the crack's key (`BuildStructureLoot("relic_cache")`; the rare Sounder finds of a first fill
  are not rolled again — the story-loot rule still applies to it as to every relic cache). A cache that still stands
  is left alone.
- **The Lights leave a gift** for every player who took part: two crystals and an obsidian, into the backpack only.
  It is due as the milestone `snd:crack_gift:<key>` and packed by the 1 Hz story pass (`HandCrackGifts`, from
  `CatchUpSoundersSkips`); without room it waits like everything the Sounders' story hands over — VEGA asks for room
  (`srv.sounders.catch_up_room`), nothing is lost, nothing lies on the ground.

### QA and tests

`/sounders crack` (behind the cheat gate) mends the crack of the gate the admin stands in; `/sounders cross` hands over
what a crossing needs and takes the admin over. `tests/BlocksBeyondTheStars.Tests/OtherSideCracksTests.cs` covers the
state and the gate list, where the game starts, a solved game (the save, the event, the cue, the list), the way home
through a cracked gate (also at a gate of melody pillars), the bloom and the gift (also with a full backpack), a
player-built gate, a world site that is mended once whatever stones a player adds, the site's id from wherever it is
looked at, several players at one gate, a reload and the motif.

### Known edges of the site rule

- A group of world stones in no registered structure (`stones@…`) is named by its first cell. Mining that very stone
  gives the rest of the group another id; the position rule still holds it whole as long as its middle stays within
  the mend's reach, so a second payout would need both — the first stone mined AND the middle moved by more than 12
  blocks with placed stones. Sites the server knows by structure (monuments, shrines, ruins, the Sounders' sites) have
  no such edge.
- A stamped structure is one site, however many rune groups it carries: mending one group of a ruin makes the ruin's
  other groups whole.

## The client

*Issues #3906 (the look, the gates' lights, the HUD) and #3908 (shaping, client half). Unity-free model and rules:
`Client.Core/OtherSide/` (`OtherSideView`, `OtherSideGateBook`, `OtherSideLook`, `SoundShaper`, `SoundAim`,
`SoundPalette`, `SoundStepTones`, `RuneGateFinder`). Unity: `GameBootstrap.OtherSide.cs` (wiring), `OtherSideFx.cs`
(the look and the lights), `UrpScenePost.cs` (`SetOtherSide`, `PulseOtherSide`), `HudUi.OtherSide.cs`,
`PlayerController.OtherSide.cs`.*

The client draws what the server says and does not start what the server would refuse. It announces `otherside1`
(`NetworkClient.ShowsOtherSide`, set by `GameBootstrap`).

### The model

`OtherSideView` takes every `OtherSideState`: the flag, the entry gate, the meter, and a 0..1 **blend** that eases over
1.2 s (`Look` is its smoothstep). The blend eases for the changes of side that happen while playing (`crossed`,
`returned`, `admin`, `forced`, `moved`) and is set at once for the join (`sync`, `rejoined`) and for a reason the client
does not know. `Apply` returns what to stage: a veil and a sound for the player's own step through a gate (`crossed`,
`returned`, `admin`), nothing for `forced`, `moved`, the join or a count. `OtherSideGateBook` keeps the last
`OtherSideGates` list (it is cleared with the return, whether or not the empty list has arrived).

### White, grey and black

`UrpScenePost.SetOtherSide(level)` is one more level of the grade the memory flashback uses: saturation ends at −100
(`OtherSideLook.Saturation`), the biome's colour filter, the white balance's warmth, the cool shadow split, the bloom
tint and the mood LUT's contribution fade out, contrast rises by 14. All of it is baked into URP's grading LUT — no
second world render, no new shader, nothing per frame but the write when the level changes; it runs in the browser and
on the lowest preset. Order matters: the saturation is applied after the filter, the shadow/highlight split and the
lift, so everything the cosiness slider, the rest look and a flashback shape before it comes out grey; the mood LUT is
applied *after* the grade and is therefore switched off by contribution.

What is drawn by the world camera is grey: terrain, sky, fog, water, creatures (the Lights too), players, effects, the
gates' lights. What is drawn after post-processing keeps its colours: the HUD (overlay, or the visor composite, which
runs after post), menus, chat, and the labels of `ScreenLabelLayer` (nameplates, door and cube labels).

The step through a gate: `PulseOtherSide` tints the vignette white for about a second (a soft veil from the frame's
edge; nothing with reduced effects) and `otherside_cross` / `otherside_return` play in 2D when the files exist.

### The gates' lights

`OtherSideFx` draws, for every gate of the book: a pillar 150 blocks tall that fades toward the sky and is drawn wider
with distance (`OtherSideLook.PillarWidthAt`, so a far gate stays a line; visible to the camera's far plane, which the
far view extends), a short wide glow between the stones, the entry gate brighter and breathing
(`OtherSideLook.PillarStrength`, steady with reduced motion), and — while `Cracked` — a dark jagged line between the
stones. Pooled `LineRenderer`s with the existing `FxBeam` material (additive, no haze term) and `Unlit/Color` for the
crack; no lights. Their strength is the look's blend, so they fade in with the grey and are gone on our side.

On **our side**, while the crossing disc sits in the worn obsidian chest's socket, the same glow stands between the
stones of the gates near the player. The server lists nothing to a player on our side, so the client looks itself:
`RuneGateFinder` reads a 41 × 21 × 41 box around the player from the chunks it has, one slab per frame, every three
seconds, and groups the rune stones by the server's rule. It is a hint; whether a stone takes the player across is the
server's decision.

### The HUD

- A chip under the top toast lane: `ui.otherside.chip`, plus the meter (`ui.otherside.count`, how many sound blocks
  are left) while the Resonance Blaster is held.
- The entry gate on the compass: a white blip, and "`ui.otherside.gate_marker` 34 m" on the shared caption row while
  the core, the story marker and the fragment signal leave it free.
- The need warnings stay quiet (`OtherSideView.ShowsNeedWarnings`): no low-vital blink or beep, no oxygen alarm.

### What the client does not start

`PlayerController` asks `OverThere` (the view) at the entry points, so nothing is predicted that the server takes back:
no mining click or hold (`HandleInteract`, `HandleDrillAudio`), no placing and no placement ghost, no attack swing, no
held beam, no loot, deposit, feed or lifted block, no item use. The use key's pick is filtered
(`OtherSideView.FilterInteraction`): only a rune stone and a talk with an Echo remain; the aim readouts that feed the
HUD's prompts (station, device, base, scan, door label) are cleared. The server still refuses everything else with its
one line — the guard is against flicker, not a rule.

### Shaping

With the Resonance Blaster in hand over there, `SoundShaper` reads the two hand keys: the use key (the one that fires
the blaster) shapes, at most every 0.22 s while held; the mine key dissolves when a sound block is under the crosshair
and does nothing otherwise. Both send `UseGadgetIntent` (`Alt` false / true) through
`NetworkClient.SendUseGadget(key, target, alt)`. After an `ActionRejected` for the gadget the trigger rests a second,
like the server.

The aim point is the **middle of the face** the crosshair rests on (or the point at reach in the open air):
`SoundAim` mirrors the server's reading of a pulse — the cells 0.05 behind and in front of the point along the ray from
the eyes — so a third-person camera names the same cells, and the selection outline (`AimOutlineCell`) shows the very
cell that gets the block.

**The palette**: `SoundPalette` mirrors the server's rule (the tone played last on the melody bar this session, 0 at
the start). Over there the bar draws its eight keys in the brightness of `OtherSideRules.ToneGlow`, rings the palette's
tone, shows the meter and the hint `ui.otherside.bar_hint`.

**Walking on sound**: `SoundStepTones` plays a block's tone (`ToneOfGlow` of its glow, the Sounder voice's procedural
note) when the cell under the feet becomes a sound block, with a short rest so a run does not machine-gun. Local only.

**Glow, not light**: `ClientWorld.SetGlowLookBlocks` keeps `sound_block` out of the light-source index; the mesher
still draws its glow colour as emission.

### Tests

`tests/BlocksBeyondTheStars.Client.Tests`: `OtherSideViewTests` (the model, the gate book, the filter, the look's
numbers), `OtherSideShapingTests` (the trigger, the aim, the palette, the step tones, the gate finder, glow-not-light)
and `OtherSideClientServerTests` — the real client against the real server: cross with `/otherside`, receive the state
and the gate list, shape in the palette's tone, see the `sound_block` in the client's world, dissolve it, come back and
see it gone.
