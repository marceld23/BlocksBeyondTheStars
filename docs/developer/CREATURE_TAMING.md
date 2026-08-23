# Creature taming & companions — how it works

Status: implemented (see [../../TODO.md](../../TODO.md) for live Done/Open status). Last updated 2026-06-19.

## Overview

A player can **tame wild creatures** with a handheld **Creature Translator**. A tamed creature becomes a
named **companion** that lives on the planet it was tamed on, follows the player while they are on that world,
is saved per player, and is listed in a dedicated Companions menu tab. Taming difficulty scales with species
temperament and a hidden per-individual personality, so it's trial-and-error rather than a fixed recipe. A
first tame of a species pays research knowledge. Companions are passive, invulnerable followers.

## How it works

**Why a translator (fiction → mechanic).** "You can't tame what you can't understand." The translator decodes
the creature's current signal (mood + need) into readable text and lets the player pick a response — making
the device central rather than a re-skinned capture ball.

**The bonding ritual** (`GameServerTaming.cs`):
1. **Decode** — equip the `creature_translator` gadget, aim at a wild creature, use it. The server replies
   with the creature's **mood** + a fuzzy **need hint** (hungry/wary/curious/territorial/hostile) and the
   **bait category** it craves.
2. **Respond** — offer the bait it wants / calm it / approach slowly / give space. Correct responses raise a
   hidden **trust** meter; wrong ones lower it — skittish bolt, territorial/aggressive provoke, passive forgive.
3. **Repeat** until trust ≥ threshold → tamed → name prompt → the companion spawns.

**Difficulty + randomization.** Required trust and patience scale with **temperament** (Passive → Skittish →
Territorial → Aggressive → PackHunter), exotic habitat (Cave/Lava/Air), glow and size. A per-individual seed
(the entity-id hash already used for size/colour) randomises preferred bait *within* the category plus shyness,
so two same-species animals tame differently.

**Companions are new player-state, not saved wild creatures.** Wild fauna is transient (regenerated per visit,
seeded per world) and a species' roster slot (`sp0`) differs per world. So a companion stores a full
`SpeciesSnapshot` (traits + appearance) and is self-contained — it renders and behaves without any world's
roster. Stored as `PlayerState.TamedCreatures : List<TamedCreature>` in the player JSON blob (no DB schema
change — like `LandedBodies`/`KnownSystems`).

**Residence & lifecycle.** A companion is **home-planet resident**: present only when the owner is on its home
body, otherwise stored. On arrival/world-load it re-spawns near the player (`ReconcileCompanions`) with a
`Follow` behaviour (`CreatureBehaviour.FollowStep` — approach owner if far, idle-wander when close, snap if
stuck, never path into ships/bases); it despawns when the player leaves. Companions are invulnerable + harmless,
excluded from the wild cap/prune, and capped at 6 per world.

**Knowledge reward.** A **first** tame of a species pays research `KnowledgePoints` (difficulty-scaled),
gated once per species by a `TamedSpecies` set (mirroring how scanning only pays new subjects via `Scanned`);
re-taming a known species gives only a small trickle, so progression can't be farmed.

**Recognition.** `NetCreature` carries `OwnerId`/`CustomName` (implicit `Tamed`); the client renders a
friendly green-cyan tint + a floating nameplate so companions are unmistakable vs. wild fauna.

**Naming collision note.** The ship-AI VEGA already owns "companion panel" in the HUD, so creatures use the
"Begleiter / Companions" wording for the tab to avoid confusion.

## Networking

New messages (all `Register()`'d in `NetCodec`): `TameRespondIntent`, `TameProgress` (mood/need/trust),
`TameResult`, `CompanionList`, `SetCompanionNameIntent`, `ReleaseCompanionIntent`, `RequestCompanionsIntent`;
plus `OwnerId`/`CustomName` added to `NetCreature`.

## Design decisions (locked)

1. **Residence → home-planet resident** (no cross-world creature streaming).
2. **Taming mechanic → translator-led bonding ritual** (decode → respond → hidden trust), not feed-with-chance
   or stasis-capture.
3. **Menu placement → dedicated "Begleiter/Companions" tab** (parallel to Alliances).
4. **Companion role → passive, invulnerable followers** (charm/decoration, zero combat risk).

## Key files / classes

- Server: `GameServerTaming.cs` (decode/respond, trust, difficulty+personality, success → create/persist +
  knowledge, spawn/despawn, follow tick, rename, release, `CompanionList`); hooks in `GameServerCreatures.cs`
  (exclude from wild cap, broadcast `OwnerId`) and `GameServerGadgets.cs` (route the translator).
- Shared: `Shared/State/TamedCreature.cs` (+ `SpeciesSnapshot`), `CreatureBehaviour.FollowStep`,
  `PlayerState.TamedCreatures` + `PlayerState.TamedSpecies`.
- Persistence: `PlayerSnapshot.TamedCreatures` / `TamedCreatureDto` + `StateMapper` (player JSON blob).
- Data: `creature_translator` gadget + 3 bait consumables (`forage_bait`/`meat_bait`/`nectar_lure`) +
  recipes + a knowledge-gated blueprint + bilingual locales + OpenAI-generated icons.
- Client: HudUi taming prompt (mood/need/trust + four response buttons); `CraftingTechShipUI` Companions tab
  (roster/rename/release + "new companion" badge); `CreatureView`/`CreatureBuilder` friendly tint + nameplate.
- Tests: `CreatureTamingTests.cs` (9) — ritual by temperament, seed variance, persistence round-trip,
  home-body binding, per-world cap, first-tame-knowledge gate. Full suite 598/598.

## Companion payoff (#1210, 2026-08-22)

`GameServerCompanionPayoff.cs` — three roles on top of follow + machine ward, no new persistence, one additive
wire field (`NetCreature.Alerting`):
- **Fetch:** `TickDropPackets` pours a packet into a player's pool when it lies within their own reach OR within
  `CompanionFetchRadius` (3× `DropPickupRadius`) of one of THEIR present companions while the owner is within
  `CompanionLeashRange` of that pet (owner-only; no teleporting loot across the world). First fetch → VEGA
  `vega.hint.companion_fetch` once.
- **Alert + distract:** 1 Hz `TickCompanionPayoff` (Guard after `TickDropPackets`): a hostile (planet machine,
  non-leaving bandit, aggressive awake fauna) with `HasLineOfSight` to a companion within 20 → `ServerMessage
  "@srv.companion.alert:{name}"` to the owner (per-owner cooldown 30 s) + `CombatEntity.AlertUntil` (client amber
  "!" nameplate for 4 s + the `creature_companion_growl` take through the species timbre). Robbers in Approach/Demanding within
  6 of the mark's companion stall 8 s (`StallUntil`, repeat 30 s) — `MoveBandit` returns early. Bond ≥ 70 +
  companion within `CompanionWardRange`: `BanditWardedByCompanion` keeps the player off the ambush roll and sends
  an approaching robber away.
- **Produce:** every 600 s a present companion spills its species' `DropItem` at its feet (fetch picks it up; a
  penned pet stockpiles).
- Tests: `CompanionPayoffTests`.

**Feed & bond (#1225, `GameServerCompanionBond.cs`):** `FeedCompanionIntent` (NetCodec 228) spends any of the
three bait items for +5 Bond (cap 100), 60 s per-companion cooldown (the bait is the cost; the cooldown only
stops a double-click). Decay 1 per real day since `LastFedUtc`, floor `TamedCreature.BondFloor` = 40, applied
on join and before a feed — keyed on `LastFedUtc`, which the decay advances by the same whole days, so a day is
never charged twice. Tiers: 50 fetch radius ×1.5 (`CompanionFetchRadiusFor`), 70 the bandit ward (#1210), 90 a
scouting hint every 5 min through `TryEmitHint` — which for a companion can only ever reveal the wreck (it has
no NPC relationship memory, so the chest/legend branches never fire; bounded on purpose). `NetCompanion.CanFeed`
drives the Companions-tab button. Tests: `CompanionBondTests`.

## Known remaining gaps / deferred (P4)

- Needs a Unity client build.
- Riding (B4), companion mortality, a portable companion that travels with you, a slow "study" knowledge
  trickle while accompanied, dye / accessories. (Feed/bond — B3 — shipped with #1225.)
