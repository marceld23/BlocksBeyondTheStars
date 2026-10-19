# Held items — what the hand shows, how it holds it, how it changes

**Status:** current (2026-10-19). Covers #1033, #1427/#2755 (the painted hand), #1931/#1962 (tool models as data), #2278
(gloves), #2534 (miniatures), #2744 (block faces), #2776–#2778 (the block's own look, a hand on every item, the
holster and draw), #2798–#2800 (no shared planes, the scanners' handle, gun or blade by range) and #2836–#2838
(materials on every part, a model for every gadget, ores held as a lump).

The selected quick-bar item is shown in three places: the first-person view (`Viewmodel`, on the camera), the local
avatar's hand in third person and every other player's avatar (`PlayerAvatar`). All three build the same model through
`HeldItem.Build`; only the first-person view adds the suit hand and arm (an avatar has its own). It is all cosmetic and
client-side: the server only relays the held item key (`NetPlayer.Held`) and never reads any of it.

## 1. What is held (`HeldItem.For` → `HeldItem.Build`)

`HeldItem.For(content, itemKey)` maps the item to a `HeldItem.Kind` and a tint; `Build` turns that into a holder
pointing along +Z:

| Kind | Look | Where it comes from |
|---|---|---|
| `Drill`, `Gun`, `Blade`, `Scanner`, `Gadget` | cube parts | `heldModel` in `data/items.json` (#1962; every gadget has one since #2837) or the kind's model (`HeldItemShapes.Parts`; a gadget's is the compact emitter); a player's tool look (#1963) wins |
| `Tool`, `Hoe`, `Hammer`, `Axe`, `Bow` | cube parts | the NPCs' tools: `HeldItem.Build` and `HeldItemShapes.NpcToolParts` (#1869, #2647) |
| `Gloves` | one glove per hand | `heldGrip: "gloves"` + `heldModel` (#2278) |
| `Block` | the block's cube (#2744) — or the world's own mesh of it (#2776, below) | the block atlas |
| `Lump` | a small irregular lump of the block (#2838, below) | `HeldItemShapes.Lump` + the block's cube faces |
| `Vehicle`, `Plant`, `Icon` | miniatures (#2534) | `HeldMiniatures`: the hull, the placed plant, the extruded icon |
| `Hand` | the bare suit fist (#1033) | an empty slot, or an item without any picture |

Every cube part — of a data model, a kind's model, an NPC tool, a glove, the climbing gear or a player's look — is made
of a **material** with its own surface texture (#2836, below).

### Materials (#2836)

A `heldModel` part may name its material: `"m"` (C# `HeldModelPart.M`), one of the twelve in `HeldMaterials.All` —
`metal`, `darkmetal`, `wood`, `plastic`, `rubber`, `glass`, `energy`, `crystal`, `leather`, `stone`, `bone`, `carbon`.
A part without one (or with a name the client does not know — a content pack is not trusted) gets a guess from its
colour and glow, `HeldMaterialRules.Infer` (Client.Core), so nothing in the data had to be tagged:

1. it glows → `energy`;
2. a brown (hue 15–50°, saturation ≥ 0.3, value ≤ 0.75): saturation over 0.8 → `metal` (copper), value under 0.32 →
   `leather`, otherwise `wood`;
3. very dark (luminance under 0.18) → `rubber`;
4. a grey (chroma — max − min channel — under 0.25, so the blue-tinted steel greys count): luminance under 0.40 →
   `darkmetal`, otherwise `metal`;
5. anything else → `plastic`.

`glass`, `crystal`, `stone`, `bone` and `carbon` are never guessed; the data names them (the diamond drill's bit is
`crystal`, the wooden tools' heads `stone`, their lashings `leather`, the plasma blades `energy`). `HeldItemShapes.Part`
always carries a known `Material`; `Mirror` keeps it.

On the client, `HeldTextures` (modelled on `PropTextures`, #1956) gives each material a texture key `held_<material>`
(`HeldMaterials.TextureKey`), read through `GameTextures` like every tile (64×64 RGBA32, `Resources/textures/held_*.bytes`
for the build, then the local pack and the world's textures), point-filtered with `wrapMode = Repeat`:

- **The tile is multiplied over the part's colour.** It is meant to be light and mostly grey; `HeldMaterialRules.LiftTile`
  pulls a darker tile toward white until its mean is 0.88 (v′ = 1 − (1 − v)·d, the pattern only compressed), so a part
  does not come out much darker than its flat colour did. Without a tile in any layer the part keeps its flat colour.
- **One shared LitColor material per (colour, glow, material)** — the held models are rebuilt on every quick-bar switch,
  for every player in sight. A texture change (`GameTextures.Changed`: a pack, a world texture, the texture editor)
  updates those materials in place. A glowing part keeps `_Floor = 1`.
- **The tile repeats in metres** (`HeldMaterialRules.TileMetres` = 0.2 m per tile): each part is a unit cube scaled to
  its size, like the Unity primitive it replaces, but its UVs run as far as the part is long
  (`HeldMaterialRules.BuildCube`). A 52 cm blade shows 2.6 tiles, not one stretched one. Sizes are bucketed in eighths of
  a tile (`UvBucket`), so parts of about the same size share one mesh. The part GameObject is still named `Part` with its
  size as `localScale`, so `HeldBounds` (the hand) and `MuzzleOf` read it as before.
- The texture editor lists the twelve keys under their own group (`TextureCatalog.GroupHeld`, `ui.cat.tex_held`,
  labels `ui.tex.held.<material>`), whether the build ships their tiles or not; without one it starts from a light grey
  (`HeldTextures.BaseColor`).

### A model for every gadget (#2837)

A `Gadget` is a shaped kind (`HeldItemShapes.IsShapedKind`): it draws its `heldModel` like a drill or a gun. Before, all
21 gadgets were one four-cube emitter in another tint, and the remote control's own model was never drawn. Now each has a
model of 5–13 parts in `data/items.json`: two rubber tubes for the binoculars (orange glowing lenses and an IR sensor
on the thermal ones), a camera with lens, flash and a screen toward the eye, a medkit case with a glowing cross, the
stasis projector's ring, a chunky barrel for the terrain blaster, a pump with tank and hose, a dish on the terrain
scanner, a handset with screen and antenna for the creature translator, the weather scanner's cup anemometer, a paint
brush, a caliper for the shaping tool, a stencil frame, a tablet with a rolled plan, a wrench for the rail linker, a
toy cab or wagon on its rail beam for the monorail items, a syringe for the sampler. The compact emitter stays the
model of a gadget without one (a content pack's, the NPCs' medkit and camera). Conventions every gadget model keeps:

- **The fist closes round the origin, or round a pistol grip** (a part hanging below −0.06 that is taller than long,
  `HeldHands.HandlePoint`). A model without a grip is lifted so its screen, lenses or wagon stand above the fist
  (top at about 0.06) or reach out in front of it — inside the fist they would be hidden.
- **No two parts share a face plane facing the same way** (they z-fight, visibly so with two materials) —
  `HeldGadgetModelTests.NoTwoPartsOfAGadget_ShareAFacePlane` checks it; offset a face by a millimetre or two.
- `"tint"` is kept for the identity glow where `HeldItem.GadgetTint` gives one (medkit green, stasis cyan, blaster
  orange).

Because a gadget is a shaped kind, the tool-look editor (`ToolLookEditor`, #1963) offers the gadgets too; the server
already stored a look for any item with a tool.

### The ore lump (#2838)

The 24 mined materials that share their key with the block they come from — the ores, silicate, carbon, the tainted
ores — have no icon and showed the bare fist, while the quick bar draws them as that block's cube (a block of the item's
key wins there). `HeldItemShapes.MiniatureFor(item, placed, own)` now returns `Miniature.Lump` for them
(`IsLumpOf`: a material without tool, vehicle or block to place whose key is a block's), and `HeldItem.For` hands out
`Kind.Lump` with that block. `HeldItemShapes.Lump(key)` builds the lump: a 10–13 cm core with 2–5 smaller knobs stuck
into its sides, top and ends (never under it), the shape seeded by an FNV-1a hash of the key — the same piece of iron ore
for every player, a different one for every ore. `HeldItem.BuildLump` puts the block's own cube mesh and the shared atlas
material (`TryBlockLook`, shared with the #2744 block cube) on every knob; without the atlas the knobs keep the block's
map colour. The lump rests on the open hand (`palm` grip).

A weapon is a `Gun` or a `Blade` by its range, the server's rule (`MeleeRules.IsRanged`: longer than the 6-block
`MeleeReach`, #2800) — never by its key: the key matcher before it held the plasma sword ("plasma") like a pistol. A
gadget whose key ends in `_gun` (the energy rope gun) is held as a `Gun` too.

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
| `fist` | tools, weapons, scanner, gadgets | `HeldHands.HandlePoint`: on a pistol grip (a part hanging below the body, taller than long) just under the body, else the model's origin — where every held model already puts the palm. Both scanners have such a handle (#2799); before it the fist sat inside the scanner's body |
| `palm` | `Block`, `Vehicle`, `Lump` | under the item's bounds, reaching in from its inner (−X) edge, the thumb against that edge |
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
- **The hand never shares a plane with the item** (`HeldHands.ClearOf`, #2798). A hand face within 1 mm of a face of the
  item's cubes that faces the same way (and overlaps it) is pulled 2 mm back into the hand. Hand and item are both
  opaque `LitColor` under different transforms, so two faces in one plane z-fight: the hand scanner's 12 cm body round
  its origin and the 12 cm fist there shared their top and bottom, and the hand flickered through the scanner with every
  bob of the view. The item stays where it sat; the hand ends a hair under its surface. This covers the data models, the
  kinds' models, the gadgets' fixed cubes (the hand is measured from every `Part` child) and players' own looks. Faces
  that only touch the item from outside — the palm under a block, the thumb at its side — face the other way, one of
  the two is culled, and they stay. `HeldHandsTests` checks every model in the data and a grip-less look in all four
  grips.
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
  forearm, the swing pivot, the bare fist of #1033 unchanged; no hand face in the plane of an item face (#2798), the
  scanners held by their handle (#2799).
- `WeaponTests.IsRanged_GoesByTheWeaponsRange` — gun or blade by range (#2800), and no weapon in the data sits at the
  melee reach.
- `HeldSwapTests` — timing, one swap per switch, fast scrolling, turning round, draw-only, cutting it short.
- `HeldMaterialRulesTests` (#2836) — the colour rule, a named material winning, every part of every model the game draws
  having a known material (the player palette included), the UV buckets and the outward-wound cube, the tile lift.
- `HeldGadgetModelTests` (#2837) — the list of gadgets, each drawing its own 4–14 parts with named materials, the remote
  control's model, the emitter fallback, every gadget different, no shared face planes, nothing hidden in the fist.
- `HeldLumpTests` (#2838) — the 24 lump items and their blocks, the palm grip, the lump the same per key and different per
  ore, knobs stuck to the core, no shared planes with each other or the hand.
- Server suite `HeldModelDataTests` — every gadget has a model, every `"m"` in the data is known and bound from the short
  key.
- `HeldMiniatureTests`, EditMode `HeldItemEditModeTests` and `BlockCubeFacesEditModeTests` — the older halves.

The poses were checked by rendering the viewmodel and an avatar from an EditMode batch script (camera at FOV 80,
16:9, near plane 0.1 like `WorldRig`) — every kind at rest and at two swing phases, the switch frame by frame.
