# The village lives — villagers, wishes, guests, the festival (epic #2589)

"The village lives" makes settlements feel inhabited: villagers greet and talk to each other, remember what players
did, ask small favours, visit, and celebrate the player who helped them. Everything follows the same three rules:

- **Server authoritative, data-driven.** The content lives in `data/village_life.json` (`VillageLifeDefinition`,
  Shared; validated by `GameContent.Validate`) and the locale tables; the logic in `GameServer*` partials.
- **Two modes, one flow.** Every line has an authored, localized version (AI off is the default in singleplayer and the
  browser). With AI personalisation on (hosted worlds), a cached re-wording from the shared `/npc-lines` path
  (`GameServerAiLines.cs`, see [AI_MISSION_BACKEND.md](AI_MISSION_BACKEND.md)) is used when one exists — names and
  numbers travel as placeholder tokens, filled per receiver; a cache key never holds a player.
- **No sound jumble, no FOMO.** One voice at a time in a crowd (the client's `NpcVoiceGate` and the server's pacing), and
  nothing expires: wishes and festivals wait for the player.

The routine-level details (phases, chatter, the deed log and news) are in [NPC_ROUTINES.md](NPC_ROUTINES.md) §12–§15.

## Place keys and board rolls (#2688, #2693)

A settlement's **place key** `settle_<n>` names everything that belongs to it: its board's mission ids
(`settle_<n>_3`, `…_b0`, `…_s0`, `…_c0`, `…_bounty_<camp>`), the memory keys of its people (`settle_<n>:quartermaster`,
the villagers' person keys `settle_<n>#bed@x.y.z:settler`), the wish ledger and wish ids (`wish_<n>_…`) and the place a
chain step was taken at (`MissionProgress.AcceptedFrom`). Since #2693 `<n>` is a nine-digit hash of **body and name salted
with the world seed** (`SettlementLocationKeyFor`, `GameServerSettlementKeys.cs`) — names repeat (about 45 per tier, unique
per body only), so the old name-only key (at most five digits) gave two "Karth Village" on two moons one board, one set of
mission ids and one memory. Station keys (`station_<n>`, from the station id) were unique already and stay. The key is
save data — known-answer tests pin it (`MissionBoardVarietyTests`). A place's interview articles are filed under
`s:<place key>` in `WorldMetadata.News` (one table for every body; a lodge's under `l:<body>|<lodge>`); the per-world
`NewsPlaceKey` (`s:<name>`) only scopes the chatter state, and old articles under it move over on first use.

**Board rolls** (#2688) seed `System.Random` from `StableHashing.Seed32(key, worldSeed)` (`BoardRng`) instead of the low
bits of the plain FNV hash: consecutive slots used to draw almost the same first number (a board echoed itself after four
jobs in most cases) and a board was the same in every world. Board jobs are never stored — a held job is coined again
from its id after every load — so the new rolls run on **new ids**: a settlement board under its new place key, a station
or base board on its own `m` stream (`station_<n>_m3`, `…_ms0`, `…_mc0`, `home_<n>_m0`). The old ids keep the old rolls
(`legacy: true`): `EnsureLegacyBoardStreams` re-coins what a player still holds there and finishes a big order they
started, but offers nothing new. On world entry `RebuildHeldBoardJobs` coins every job the player holds on a settlement or
base board of this world (old and new ids) wherever they stand, so a held survey or build job keeps counting after a
restart (a base board waits for its quartermaster, whose name the jobs carry; `RefreshBaseMarkers` catches up). Bounty
rewards roll per world too (their definition is stored when taken).

**Migration** (`MigrateLegacySettlementKeys`, on world entry before the #2598 one): what the save files under an old key
moves to the key of the settlement of that name on the body the player enters — memory entries (person keys included;
the stray `settle_<n>_bounty…:quartermaster` entries the old mission-id parsing wrote are added onto the quartermaster),
the `npcmig:` marker, the wish ledger (its villagers' keys) with the held wish renamed onto the new place (its stored
definition too), and `AcceptedFrom`. Should the new place already have a ledger, the two merge lane by lane (a held old
wish comes over onto the higher counter; where both lanes hold one, the new one stays). An old key cannot say which body
it meant: the first namesake the player enters after the update inherits it (moved, not copied — a copy would list a
person twice and cannot split one held wish or one running chain). A name that is not on this body waits for its own.
Quartermaster names (`CoinGiverName`), a chain giver nobody has named yet (coined from the old place key,
`ChainGiverFallbackName`) and a settlement's trade (`SettlementTradeFor`) still come from the name — they name living NPCs
players know.

## Festival (#2604)

Finishing a settlement's four-step **"settlement needs"** chain (`data/missions.json`) plans a festival for the player
who did it — once per settlement, ever; a later helper gets the thanks and the present only.

| Step | What happens |
|---|---|
| Planned (last step turned in) | `settlement_helped` deed; `WorldMetadata.Festivals["<locationId>\|<settlement>"]` = pending; the quartermaster's `village.festival.planned` (a direct reply, Kind Addressed) and VEGA's `vega.festival.planned`; with AI on the `cheer` and `festival_thanks` lines are asked for in the hero's language |
| Waiting | no deadline; starts when the hero is within 48 m of the square at local day 0.60–0.95; the villagers' `festival_pending` chatter names the hero |
| The party (180 s) | ≤ 24 residents within 64 m form a ring (routine phase `Festival`, "celebrating"), facing the square; one cheer every 5 s (Kind Cheer, cheer gesture); a bell tune of `SoundFx` notes at the start and every 45 s; the quartermaster's present (`festival.gift`; full bag → the next talk); +6 standing per attendee; `festival` deed; VEGA's `vega.festival.started` |
| Afterwards | the routine takes everyone back at its next check; the deed feeds the news and the chatter |

The square is the layout centre (the generator's plaza) when a ring fits there, else the villagers' marker nearest it,
else the mission board. No block is ever changed. Implementation: `GameServerVillageFestival.cs`; the full description
is [NPC_ROUTINES.md](NPC_ROUTINES.md) §15; tests: `VillageFestivalTests`.

## Visitors (#2602)

About once per 1.5 in-game days a friendly guest walks up to a base whose owner is at home. Implementation:
`GameServerBaseGuests.cs` (guests are ordinary `ServerNpc`s, never `CombatEntity`s; RAM only, nothing is saved); tests:
`BaseGuestTests`.

| | |
|---|---|
| **Switch** | world option `GameRules.FriendlyVisitors` — default **on** in every preset, live-editable (`SetWorldRulesIntent.FriendlyVisitors`, the world-rules row *Friendly visitors*, `--friendly-visitors true\|false`); an old save reads on (no launch lift needed); independent of the bandit scouts' `BaseVisitors` and of every enemy setting |
| **When** (1 Hz per world, `TickBaseGuests`, before the routine) | the owner is on the body, on foot (not aboard, in space or observing), ≤ 24 blocks from the core; local day fraction 0.25–0.60 at the core; no machine, robber (not leaving) or hostile animal within 24 m; no scouts at the base; one guest per base. Per base: quiet spell 1.5 × day length × 0.8–1.2, a first sighting arms 0.4–0.8 of it, a due window brings a guest with 70 % |
| **Who** (weights) | **neighbour** 50 — the real villager of the nearest settlement with an unwatched candidate (a settler with job "", gardener or craftsman, awake, standing, not chatting, not in a dialogue, no player within 24 m; one with an open wish for the owner first); **travelling merchant** 30 — none while a trader ship is parked on the body; **lost traveller** 20 |
| **Neighbour = the villager** | the guest is a clone with the villager's name, look, settlement, `PersonKey` and face seed (`ServerNpc.FaceSeed` → `NetNpc.FaceVariant`, so face and voice match); the source gets `VisitingAway` (not sent in `NpcsInReachOf`, not moved, routine paused, no passing hello) and is restored when the guest ends |
| **Spawn** | rings 30/36/42 × 8 bearings (the neighbour's from its village's bearing); real standable ground, not `BanditColumnRejected` (walls, sealed rooms, fluids, wall tops), ≤ 44 blocks (Chebyshev) from the owner so the A* box (48) reaches, ≥ 12 m from them. Nothing fits → no visit |
| **Script** | *arriving*: A* to a standable spot ≤ √10 from the owner, re-targeted ≤ 3× (≥ 5 s apart) when the owner moves > 6 m; two failed routes, the owner in a sealed room or 90 s → it walks to the first open cell outside zone/yard/room on the owner's side and the owner gets `@srv.guest.waiting:<name>` · *greeting* ≤ 4.5 m with a line of sight: `NpcGreeting` Kind `Guest`, Gesture `Wave`, a new `GreetingId`, to every player ≤ 8 m in their language (a neighbour also counts as today's hello) · *waiting* 120 s (merchant 180 s), +15 s while a player is ≤ 6 m (never past twice the wait), 30 s after its present · *leaving* on its bearing (village / destination / where it came from), despawned at the end of the walk when no player ≤ 32 m, else 8 m further; after 120 s whenever no player ≤ 24 m. The owner leaving the world or going aboard (or the world not ticking for 20 s) ends the visit at once |
| **Talk** | `PickDialog` treats a `guest_*` job like a profession: only the `job: guest_neighbour` (+ `guest_neighbour_friend`, min stage trusted), `guest_merchant`, `guest_traveler` dialogues match (no friend gift for a guest). Consequences: `guest:gift:<item>:<n>` (validated in `GameContent.Validate`) once per guest and player, a full bag keeps it; `guest:wish` grants the borrowed villager's open wish (#2601, `GrantWish` with `answerWhenReady`) and tells it — with everything already in the bag it adds "you already have everything with you" and the guest stays for the hand-over, otherwise it heads home 30 s later; none → "I'm fine". **#2617: the neighbour stands for its villager in wish matters** (`WishPersonOf` resolves the guest to its villager): a held wish — ready or not — is handed over to the guest (thanks, present, +5) or reminded before its own dialogue; the "?" / "!" markers sit on the guest, never on the hidden villager (`WishFaceOf`; the standings are re-sent when the visit starts and ends); the tab's "next to the giver" check and its thank-you bubble use the visible one (`VisiblePersonByKey`). The merchant is role `vendor`: `MarketAvailable` accepts barter beside it (`NearBaseGuestMerchant`, and any vendor within 6 since #2626), the client's "Trade or talk?" works as at a stall; #2630: its theme is `mixed` — one offer from each of 4 trades, new every in-game day (`MarketStock.MixedOffers`, the same pick on the client's screen and in the server's check) |
| **Memory** | neighbour: its person key (the dialogue's interaction keeps the village as its place); merchant/traveller: `guest:<name>` (`ServerNpc.GuestKey`), never written (`RecordNpcInteraction` skips the prefix) |
| **AI** | at spawn: `guest_greet` (tokens `{player}`, `{place}`; key `guest_greet\|<kind>\|p<persona>\|<locale>\|<stage>`) and the dialogue opening via `WarmDialogOpening` — purpose `guest_offer` for guests (`dialog_open` path, the guest dialogues set `aiPrompt`); used when cached, else the authored `guest.greet.<kind>` / `dlg.guest_*.prompt` |

## Traders landing near your base (#2603)

Trader ships also land on a body while nobody flies (the space instance is no longer required): a planet-side scheduler
per occupied body picks the free dry pad nearest the player's base, announces "look up!", sets the ship down after a
20 s approach and hails the base owner (radio, or VEGA without one). Details: [NPC_TRADER_SHIPS.md](NPC_TRADER_SHIPS.md)
§ "Planet-side arrivals"; tests: `PlanetTraderArrivalTests`.

## Wishes (#2601)

A named villager asks **this** player for a small favour — *bring me* five berries, *show me* a crystal (you keep it),
*check on the animals* (scan one). A wish is an ordinary system mission with its own id prefix, so the mission log, the
objectives, the progress hooks and the turn-in are the existing ones. Implementation: `GameServerWishes.cs`; content:
`data/wishes.json` (`WishTemplate`, Shared/Missions); tests: `VillagerWishTests`, `VillagerWishRealVillageTests`,
`VillagerWishAiTests`, `WishContentTests`.

| | |
|---|---|
| **Mission** | id `wish_<place hash>_<player hash>_<lane>_<n>` — deliberately not a board prefix (`settle_/station_/home_` would make it a board job, and big orders count turn-ins by prefix); `GiverName` = the villager; `CountsForStory = false`; not repeatable; title "A wish from {npc}" + the villager's request as free text in the Missions tab. Kinds → objectives: `deliver` → Deliver (used up), `show` → Collect (only "have it" — the first real use of Collect), `scan` → Scan (`ScanTargets`, e.g. `creature:any`) |
| **Who** | settlement villagers with a person key (`ServerNpc.PersonKey`, #2598) and role settler — plain settlers, gardeners, craftsmen, innkeepers (`WishTemplate.EligibleJobs`); never traders, quartermasters, professions, guardians, authored characters or base residents (later) |
| **Lanes** | a hamlet one, every larger place two; one wish per lane at a time; per player (`PlayerState.Wishes[place] = WishLedger { Lanes[{N, Giver, LastGiver, LastTemplate}], Done }`, persisted additively in the player snapshot). A held wish is a mission row + its definition in the mission store (`_repo.SaveMission`, the bounty pattern — it survives a restart; an orphaned row is coined again for the same villager) |
| **The coin** | deterministic from (world seed, place, player, lane, the lane's counter): the villagers are split between the lanes by a hash ("shares"); the lane takes the best-ranked villager of its share who is not its last giver (borrowing from the rest only when its share has nobody left, the last giver again only as the very last resort); the template by hash among those this world can grant (`requires`: `flora_drop` — a growing plant drops the item, a poisonous species not when the item has a poisonous twin; `trees`; `mycelium`; `creatures`), open to the giver's job and within the lane's tier (tier T after T−1 fulfilled wishes of the lane), preferably not the lane's last template; count = template ± jitter. An open offer depends only on its own lane, so fulfilling the other lane's wish never changes it |
| **Talk** (`HandleTalkToNpc`, after the sleepy mumble and the owed festival present, before `PickDialog`; the welcome-back bubble is sent first and "seen" stamped after, as on the dialogue path) | a held wish that is complete → `HandleTurnInMission` (returns bool) → thanks + the always-authored present line "Tomas gives you: 3× Berry Seed" + **+5** standing (`AddStanding` on the person key); a full bag → "make a little room", the wish stays · held, incomplete → "You have 3 of 5 Berries so far — no hurry!" once per progress value (`PlayerSession.WishReminded`; the same progress again → the ordinary dialogue, e.g. `innkeeper_news`) · an open offer → the synthetic dialogue `wish` (request + the authored fact line "You need: 5× Berries"; "Sure, I'll help!" → `wish:accept` → `GrantWish` — already complete at the yes (a new player's starter berries), the answer becomes `dlg.wish.r_yes_ready` ("Oh, you already have everything with you! Just talk to me again."), the "?" goes out with the dialogue's standings and the 1 Hz toast stays quiet — the same for a neighbour's wish told at the base, which is then handed over to the guest right there (#2617); "Maybe later." → `wish:later` → `PlayerSession.DeclinedWishGivers`, the ordinary dialogue for the rest of the session, the "!" stays). No offer while the villager celebrates at the festival or is away visiting a base (their lane waits for them — at the base the guest's "Can I help you with anything?" takes it). The panel's conversation follows [NPC_ROUTINES.md](NPC_ROUTINES.md) §16 (#2616) |
| **The tab** | `AvailableMissionsFor` never lists a wish (nor another player's); `HandleAcceptMission` refuses `@srv.wish.talk`; `HandleTurnInMission` refuses `@srv.wish.return_giver:<name>` unless the villager is within greeting reach — then the villager thanks the player in a bubble (#2617: a neighbour visiting the base counts and thanks; the villager hidden at home meanwhile never does) |
| **Markers** | additive `NpcStandingList.WishNpcIds` ("!", offers) / `WishReadyNpcIds` ("?", a held wish ready to hand over), filled in every `SendNpcStandings`; the client keeps them in `GameBootstrap.NpcWishMarks` and `NpcView` draws a gold glyph over the head (opaque to 30 m, gone at 45 m, not through walls; hidden while the NPC speaks or sleeps). While the villager visits a base as a neighbour both sit on the guest (#2617) — a "?" always means "talk to me and it is handed over". An older client shows none — talking still works |
| **1 Hz check** (`TickWishesFor`, from the VEGA advisor tick — no tick of its own) | a held wish turning ready → markers re-sent + `@srv.wish.ready:<name>` once; a festival starting or ending in the settlement the player stands in re-sends them (no "!" while everybody celebrates); with AI on, walking into another settlement re-sends them (which warms that village's requests). Cost: a box test per settlement, one pass over the NPCs and the mission list |
| **AI** (`/npc-lines`, purposes `wish_ask` / `wish_thanks`; key `<purpose>\|<template>\|p<persona>\|<locale>\|` — never a player) | `wish_ask` is asked for (priority Personal) when the standings go out while the player stands in the village with an offer, in the villager's persona; tokens `{count}`, `{item}` required (`{player}` allowed); `wish_thanks` (`{item}` required) when a wish turns ready. A text shows a line only if it was cached BEFORE (read first, then asked) — an open panel is never re-worded; the variant is picked by the wish id, so the panel and the Missions tab agree. The fact line and the present line are always authored. v2 (AI proposes the item, Suggest/Auto only) is not built |
| **Texts** | `wish.title`, `wish.target.creature`, `dlg.wish.*`, `srv.wish.*`, `wish.<key>.ask` / `.thanks` per template — English + German mandatory (`WishContentTests`) |
