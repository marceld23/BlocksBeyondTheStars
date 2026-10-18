# Held items — what the hand shows, how it holds it, how it changes

**Status:** current (2026-10-18). Covers #1033, #1427/#2755 (the painted hand), #1931/#1962 (tool models as data), #2278
(gloves), #2534 (miniatures), #2744 (block faces), and #2776–#2778 (the block's own look, a hand on every item, the
holster and draw).

The selected quick-bar item is shown in three places: the first-person view (`Viewmodel`, on the camera), the local
avatar's hand in third person and every other player's avatar (`PlayerAvatar`). All three build the same model through
`HeldItem.Build`; only the first-person view adds the suit hand and arm (an avatar has its own). It is all cosmetic and
client-side: the server only relays the held item key (`NetPlayer.Held`) and never reads any of it.

## 1. What is held (`HeldItem.For` → `HeldItem.Build`)

`HeldItem.For(content, itemKey)` maps the item to a `HeldItem.Kind` and a tint; `Build` turns that into a holder
pointing along +Z:

| Kind | Look | Where it comes from |
|---|---|---|
| `Drill`, `Gun`, `Blade`, `Scanner` | cube parts | `heldModel` in `data/items.json` (#1962) or the kind's model (`HeldItemShapes.Parts`); a player's tool look (#1963) wins |
| `Gadget`, `Tool` | cube parts | built in `HeldItem.Build` |
| `Gloves` | one glove per hand | `heldGrip: "gloves"` + `heldModel` (#2278) |
| `Block` | the block's cube (#2744) — or the world's own mesh of it (#2776, below) | the block atlas |
| `Vehicle`, `Plant`, `Icon` | miniatures (#2534) | `HeldMiniatures`: the hull, the placed plant, the extruded icon |
| `Hand` | the bare suit fist (#1033) | an empty slot, or an item without any picture |

### The block's own look (#2776)

A block's item key can carry a dye (`t`), a glow colour (`g`), a form (`s`) and a paint design (`p`) — see `ItemKey`.
`HeldBlockLook.For` (Client.Core) decides from the key and the block what the world would draw once it is placed:

- **dye / glow / design only** → a cube, meshed by the world's mesher with the cell's modifier and design;
- **a form** → that form, turned to its display orientation (`DisplayYaw` = 3: a ramp or staircase rises to the eye's
  right, the side view the hotbar icon draws) and upright; a player form over several blocks shows all its cells, the
  bed its head and foot;
- **a prop with a stamped form** (`PropShapes.DefaultPlaceShape`: bed, campfire, rug, flower pot, ladder, staircase,
  stretcher, trapdoor) → that form; the ladder shows its rungs to the eye (`LadderUpFace`);
- **nothing of that** → null, and the plain #2744 cube stays (cheaper, and unchanged).

It never promises more than the world draws: a form on a block that is not `Shapeable` is a cube (#1834), a player
form whose voxels have not arrived is a cube, a design that is not in the paint atlas yet is no design.

`HeldMiniatures.AttachBlock` meshes the cells in a one-chunk `ChunkData` (`SetShape` + `SetModifier`, the paint atlas's
`designUv`) with the chunk materials — opaque, transparent and the paint material (`HeldItem.PaintKitResolver`) — and
places it where the plain cube sits, at a block's scale (a slab is as wide as a block; anything longer than 1.6 blocks
is shrunk to fit). A form is turned a little (`FormTurn`) so its profile shows depth.

The mesh is cached under `HeldBlockLook.CacheKey` with the other miniatures (leased by the holders that show it). A
player form re-registered under a reused id drops the cached blocks (`HeldMiniatures.ForgetBlocks`; a mesh still in a
hand becomes an orphan and is freed when its last holder goes), and a new design or form sets
`GameBootstrap.HeldItemDirty`, so a block that waited for it is rebuilt.

## 2. The first-person hand (#2777)

`HeldItem.AttachHand` (called by `Viewmodel.BuildHeld`) builds the painted suit forearm and hand under the item. The
item stays exactly where it always sat; the hand is placed under it. The grip comes from `HeldHands.GripFor`: an item's
own `heldGrip` (`HeldGrips.Fist` / `Palm` / `Pinch` / `Stem`) wins, otherwise the kind decides:

| Grip | Kinds | Where the hand goes |
|---|---|---|
| `fist` | tools, weapons, scanner, gadgets | `HeldHands.HandlePoint`: on a pistol grip (a part hanging below the body, taller than long) just under the body, else the model's origin — where every held model already puts the palm |
| `palm` | `Block`, `Vehicle` | under the item's bounds, reaching in from its inner (−X) edge, the thumb against that edge |
| `pinch` | `Icon` | a fist under the card's lower edge (1.5 cm of it in the fist) |
| `stem` | `Plant` | a fist round the plant's foot |

The bare hand, the gloves and the climb hands (#2287) keep their own hands. The item's bounds are measured from its
cube parts and its miniature mesh (`HeldItem.HeldBounds`); an icon whose pixels are still on their way back from the
GPU (WebGPU) is gripped at its nominal card size and re-gripped when its mesh arrives (`HeldItem.RefreshHand`).

Things that look small but matter:

- **The forearm under an item is long** (`HeldHands.ItemForearm`, 0.60 m against the bare fist's 0.30): at the peak of a
  swing its far end would otherwise come into view — the flat end of an arm that stops in mid-air (the #1428 problem).
- **Swings turn round the forearm** (`HeldHands.SwingPivot`, 15 cm behind the hand toward the elbow), not round the
  item's origin: round the origin the forearm's far end swings up into the picture, round the elbow the item falls out
  of it. The jab and the blade's slash pitch a little less for it (34° and 40°). The gun's recoil and the bare fist's
  punch keep their own motion.
- **An item in the hand sits 2 cm higher** than the old rest pose (`Viewmodel.GripLift`), so the hand shows.
- **One shared material per (colour, painting)** for every hand part (`HeldItem.HandMaterial`); the hand is rebuilt on
  every quick-bar switch, and a material per part per rebuild (as the bare hand had before) would pile up.

## 3. Third person: carrying (#2777)

`PlayerAvatar` blends a player's right arm into a carry pose while it holds something (`CarryUpperArm`, `CarryElbow`,
a 30 % walk swing) and keeps the item level against the arm's pitch (`_held.localRotation`), so a tool points ahead
instead of at the sky. A swing, a blow, the bow, a gesture or the rope moves the arm and the item follows the hand
again. NPCs keep their tools at their side, and every pose with its own arms (asleep, climbing, seated, gliding) drops
the carry.

## 4. Putting it away and taking out the next one (#2778)

`HeldSwap` (Client.Core) is the one animation for every item, used by `Viewmodel` and `PlayerAvatar` alike:
holster (0.12 s, the item sinks and tips) → the waiting item is swapped in at the bottom → draw (0.2 s) → a small
settle (0.14 s). `Drop` is one smoothstep curve both ways, so a turn-round never jumps.

- `PlayerController.RefreshHeldItem` picks the kind of change: the first item and the same key rebuilt (a new arm
  painting) are `Instant`; a slot that just ran empty (the last block placed, the item dropped) is `DrawOnly`; any other
  switch is `Holster`. The `gear_equip` cue plays quietly on a holster switch, at most every 0.18 s; the gloves keep
  their `glove_charge` hum.
- Fast scrolling only replaces the item waiting at the bottom; a switch during the draw turns it round from where it is.
- It never delays anything: `Viewmodel.Swing` finishes the swap first, and a climb, a hidden hand (third person, the
  ship's view) or *Reduced effects* (`UiKit.ReducedMotion`) switch at once.
- In third person the right hand reaches down and back to the hip (`HolsterUpperArm`, `HolsterElbow`); other players
  see it, because a remote avatar animates every held-key change after the first one it sees.

## Tests

- `HeldBlockLookTests` — which look a key gets (dye, glow, forms, designs, player forms, props, the cache key).
- `HeldHandsTests` — grip choice and data values, the fist on every data tool model, the palm/pinch/stem contact, the
  forearm, the swing pivot, the bare fist of #1033 unchanged.
- `HeldSwapTests` — timing, one swap per switch, fast scrolling, turning round, draw-only, cutting it short.
- `HeldMiniatureTests`, EditMode `HeldItemEditModeTests` and `BlockCubeFacesEditModeTests` — the older halves.

The poses were checked by rendering the viewmodel and an avatar from an EditMode batch script (camera at FOV 80,
16:9, near plane 0.1 like `WorldRig`) — every kind at rest and at two swing phases, the switch frame by frame.
