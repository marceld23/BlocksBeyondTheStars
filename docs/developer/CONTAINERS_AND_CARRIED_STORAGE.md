# Containers and carried storage

**Status:** shipped (crates since Task 5 Stage 3b, the crate screen #2436, the transport crate #3098 and the storage bag
#3099 on 2026-11-01). How world containers are stored and moved, and how an *item* can carry other items.

## World containers

Every world container is a `StoredContainer` row (`src/BlocksBeyondTheStars.Persistence/IWorldRepository.cs`): id, planet
(the location id), kind, cell, a list of `ItemStack`s and a filter. The container table is generic over its kind in all
three repositories (SQLite, PostgreSQL, the in-memory/browser one), so a new kind needs no schema change. A world loads
only the rows of its own location (`ContainerSystem.LoadContainers` → `ListContainers(LocationId)`).

| Kind | What it is | Where |
|---|---|---|
| `crate` | a placed storage block: `crate`, `wood_crate`, `station_container`, `transport_crate` (`ContainerSystem.IsContainerBlock`) | `ContainerSystem` |
| `salvage_capsule` | a death drop (`RespawnPlayer`), looted empty then removed | `ContainerSystem.LootContainer` |
| `drop` | a ground drop packet (#853) — overflow that found no room, collected by walking over it | `GameServerDropPackets.cs` |
| `reward_depot` | a mission's reward depot, pseudo-location `depot` | `GameServerMissions.cs` |
| `bag` / `transport_crate` | a carried-contents record, pseudo-location `carried` (below) | `CarriedContentsSystem` |

A placed crate block creates its `crate` row (`PlaceCrate`); mining it hands the stacks to the miner's material pool
(`RemoveCrateContainer`) and what does not fit spills as a drop packet. The block-kind decides capacity: a wood box
holds 8 kinds (`WoodCrateStackSlots`), every other crate is unbounded per kind. The crate screen (`CrateUi`,
`OpenContainerIntent` / `MoveContainerItemIntent` / `ContainerContents`) re-sends a crate's contents to everyone who has it
open after every change.

## Items that carry items (#3098, #3099)

An `ItemStack` is only a key and a count, so an item cannot hold data. Per-item data rides in the key suffix
(`ItemKey`: `#t…` tint, `g` glow, `s` shape, `p` paint design, `x` species, `u` lab change). A **carried-contents record**
follows the paint-design precedent — the item carries a reference, the save carries the data:

- **The key.** `CarriedContents` (`src/BlocksBeyondTheStars.Shared/State/CarriedContents.cs`): the base key, `#`, the tag
  `h` and a 16-digit lowercase hex record id — `storage_bag#h0f3a9c21b7d4e605`. An **empty** bag or crate has no record
  and is the plain base key (it stacks as usual: a transport crate up to 64, a bag never — `maxStack` 1). A key **with** a
  record never stacks: `GameContent.MaxStackOf` answers 1. The client names it *… · filled* (`ItemNames.Display`) and its
  icon resolves to the base key (`IconResolver.IconKey`).
- **The store.** `CarriedContentsSystem` (`src/BlocksBeyondTheStars.GameServer/`): record id → `StoredContainer` row with
  id `carried_<id>`, planet `carried`, kind = the base item key. Save-wide, not per location, so a packed crate carried to
  another planet, a station or the ship's hold still resolves. Loaded lazily on first use; written at once.
- **Transport crate** (`transport_crate`, a block — a crate like the workshop one when placed). Mining it with something
  inside (`GameServer.PackTransportCrate`, called by the break path before the contents would be handed out) moves the
  stacks into a new record, turns the block's one dropped `transport_crate` into the record key and removes the container
  — record, container and row in one transaction. Placing a record key (`PlaceCrateFrom`) creates the crate with the
  record's stacks and deletes the record, in one transaction with the player's save. A packed crate is **always paid for**,
  even where placing is free (Sandbox, instant build), or a dead copy of the key would stay behind. A space structure /
  ship hull edit refuses a record key (`@srv.carried.unpack_in_world`): a hull cell is no world crate.
- **Storage bag** (`storage_bag`, a component item, `maxStack` 1). The bag screen (`BagUi`, client) sends
  `OpenBagIntent` / `MoveBagItemIntent` (ids 315/316) and gets `BagContents` (317) back after every move — the server
  keeps nothing open, a bag only changes through those moves. The bag is addressed by its **backpack slot**; its key
  follows its contents (a new record on the first item, the plain key again when the last item leaves). Capacity:
  `CarriedContents.BagCapacity` = 1000 items in total, any number of kinds; a move puts in what fits. A bag never goes
  into a bag, and neither does a packed transport crate (`CarriedContents.FitsInBag`) — nesting would get around the
  limit. "Store all" takes loose materials, components and blocks, but not the stack in the player's hand. Each move writes
  the record and the player in one transaction (`RunInTransaction` + `SavePlayer`).

### One key, one record — the duplication rules

| Path | What happens to a record key | Why it is safe |
|---|---|---|
| Crate, hold, drop packet, salvage capsule, trade, crate filter machines | the key moves like any item | the record follows the key; the key is unique and never stacks |
| Sandbox catalog, admin `give_item` | the record tag is stripped — an empty bag/crate is handed out | no client can mint a second key for somebody's record |
| Discard (the bin) | refused while the record holds something (`@srv.carried.not_empty`); the client hides the button | the contents would be destroyed with it |
| Recipes, disassembly, vendors | never match: they compare exact base keys (`MaterialPool`, the lab's soft `u` tag only) | a filled bag is no plain bag |
| Placing a packed crate | the key is consumed (even when placing is free), the record deleted with the new crate written | the contents exist exactly once — in the world |
| A crash mid-move | bag moves and crate placement commit record + player together | the items are never in both places after a restart |

Orphans are possible in one direction only: a crash after a crate was **mined** but before the next player save leaves a
record no item points at (the contents are lost, as a mined plain crate's would be). Nothing is ever duplicated.

**Save compatibility.** The block raised `WorldMetadata.CurrentSaveVersion` to 11 (block ids shift). An older build never
loads the `carried` rows, so a packed crate or a filled bag opened there reads as empty — another reason the version
guard refuses it.
