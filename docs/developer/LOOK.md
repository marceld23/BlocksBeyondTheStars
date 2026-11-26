# The game's look — how it works and where the code is

Status: part of the game since 2026-11-22, not yet in a release (one package; the old look is gone, there is no switch
and no setting for it). The decision behind the graphics tiers is
[ADR 0021](adr/0021-graphics-tiers-desktop-high-potato-browser.md); the live status and what is still open (above all:
**not measured, never run in a browser**, see the last section) are in [TODO.md](../../TODO.md). The player-facing
side is in [USER_MANUAL.md](../user/USER_MANUAL.md) (§4 *The look of the
world*, §6 Editors). The normative style rules are [ART_BIBLE.md](ART_BIBLE.md).

The look is presentation only. The server, the save format and the network protocol are untouched, with three exceptions
that belong to world generation 48 (§2): the colour harmony, the grouped flora and the promised red and blue suns. Those
are Shared/WorldGeneration code that server and client compute alike; older worlds keep their colours, plants and stars
bit for bit.

## How to read this doc

- **Fixed values.** Every number of the look lives in `client/Assets/BlocksBeyondTheStars/Scripts/GameLook.cs` as a
  constant (the ranges per preset, the shares, the budgets). There is no command line and no setting for any of them;
  change a value there and rebuild. What each preset meshes and builds at all is decided in `Scripts/GameLook.Tiers.cs`
  (#3946), one property per part: whether the mesher records crown cells, grass edges and plant cells for the near
  drawers (`RecordCrownCells`, `RecordGrassClumps`, `RecordPlantCells`), which crown far form a preset gets
  (`CrownFarFormsTier`), the emitter reach (`LongEmitterReach`), plain figures (`PlainFigures`), the plant far pictures
  (`PlantFarPictures`), the terrain forms (`TerrainFormsNow`), and `MeshTierKey`, which tells when the loaded chunks must be meshed again. `Sky.Look.cs`
  publishes the values the shaders read as globals once a frame; the
  shared shader parts are in `Shaders/AtmosphereCommon.hlsl`. Shader branches are driven by those values, not by
  keywords, so no shader variant was added for the look.
- **"Desktop class"** below means `HighDetails.DesktopClass` (`ClientSettings.FarViewLightDevice` is false): the Windows
  and Linux clients. The browser (WebGL / WebGPU) and phones are light devices.
- **Tiers.** Every part lists what High, Medium, Low, Potato and the browser get, following ADR 0021. "Every tier" means
  the code path does not look at the preset; whether it runs well there is in the last section. **The browser offers
  only Potato and Low** (`src/BlocksBeyondTheStars.Client.Core/PresetLimit.cs`, `Scripts/ClientSettings.Platform.cs`,
  #3946): a stored higher preset is lowered on load, the settings row toggles between the two, and the automatic
  calibration never steps above Low. So the browser gets what its preset gets, minus what needs a desktop-class client.
- **Changing the preset in a loaded world.** When the change alters what the mesher emits (`MeshTierKey` — every change
  that crosses Low; Medium to High does not), every loaded chunk is meshed again (`Scripts/GameBootstrap.LookTier.cs`).
  The emitter reach is fixed per loaded world (the light index keeps each source's reach from the moment its chunk
  arrived), and the rounding of figures and tools is fixed for the run: a change to or from Potato shows at the next
  start.
- Files are under `client/Assets/BlocksBeyondTheStars/` unless they start with `src/`.

At a glance:

| Part | High (desktop) | Medium (desktop) | Low | Potato | Browser (Low or Potato) |
|---|---|---|---|---|---|
| Light, sky, calm tiles, soft blocks, grain, sod, clouds, debris, regrow marker, critter bodies | yes | yes | yes | yes | as its preset (never run) |
| Longer reach of lava, crystal and glow-plant light | yes | yes | classic reach | classic reach | classic reach |
| Bloom | stronger outdoors | stronger outdoors | LDR buffer: only near-white blooms | as Low | as Low |
| Depth of field in play | yes | yes | no | no | no |
| Grass clumps, ground cover, cover under plants | 24 m | 12 m | no (not recorded) | no (not recorded) | no |
| Plant bodies, vines | 32 m | 16 m | no (not recorded); baked far picture | no; the crossed tile picture | no; the crossed tile picture |
| Crown clusters, crown forms, leaf picture | 48 m | 24 m | no (not recorded) | no | no |
| Needle and palm crowns beyond the clusters | far forms on every cell | far forms on every cell | far forms on the outer cells + spire | leaf-block shell | as its preset (never run) |
| Fur, feathers, toes, rounded creature boxes | yes | half the felt | no | no | no |
| Rounded and formed avatar and tool parts | yes | yes | yes | plain boxes | as its preset (never run) |
| Device forms | soft edges | soft edges | hard edges | hard edges | hard edges (never run) |
| Soft edges of player forms | yes | yes | no | no | no |
| Clear shallow water | yes | yes | no | no | no |
| Terrain forms (§17) | yes | yes | no (not looked for) | no | no |

## 1. Light and sky (#3927)

**What the player sees.** The sun no longer passes through the zenith: it stands lower at noon, so the side faces of
blocks get sun all day and the world has more than "top and side" brightness. Shadows take the sky's colour, faces that
look down get a ground bounce, the sky is a gradient from the horizon colour to a darker, more saturated zenith with a
soft halo around the sun, the golden hour is wider, the sun and moons in the sky are larger. Creatures, avatars, doors
and other models stand in the real sun of the world (warm side, rim light against the sun) instead of a fixed studio
light. Lava, crystals and glowing plants light a cliff, not only the blocks beside them, and outdoors the bloom is
stronger and wider; on the ship it stays as before. The haze near lava, crystals and glowing plants takes their colour.
With depth of field, what sits in the middle of the picture is sharp and the background well behind it goes soft.

**How.** The sun rotation in `GameLook.cs` tilts the sun's arc about the line from sunrise to sunset, so sunrise and
sunset keep their moments and only the light's direction changes (the time of day is not touched). The ambient term of every
world-space shader is split into a sky colour above and a ground colour below, mixed by the face's direction, with the
sun's share raised; the sky gradient is drawn by the existing sky dome (`Shaders/Atmosphere.shader`) over the
background colour, which stays the horizon colour, so the distance haze still meets the far terrain without a seam.
`LitColor.shader` reads the sun direction and hue globals in the world and keeps the studio light in menus and
previews; the first-person hand and tool take only a small share of the world's light, so they stay readable.
`GameLook.cs` also lengthens the reach of the quiet natural emitters in the block light index — on Medium and High
only, fixed when a world is entered (`GameLook.Tiers.cs`, `LongEmitterReach`). Since a crystal world's whole ground is
the `crystal` block, that reach nearly burnt such worlds out; on 2026-11-23 the crystal's own glow went 0.50 → 0.42 and its
light colour to 90 % (0x97B4E6). Bloom:
`GameLook.cs`, called from `UrpScenePost`, lowers the threshold and raises intensity and scatter
outdoors and fades back to the classic values aboard (the "home" share). The glowing haze: `Scripts/Sky.GlowHaze.cs`
reads the natural lights within a few dozen blocks from the world's light index about twice a second, forms one mean
colour, amount and height, and `BbtsHazeMix` (`AtmosphereCommon.hlsl`) mixes the haze toward it — one colour for the
whole picture, not per light. Depth of field: `UrpScenePost.DriveFocusBlur` drives URP's Gaussian depth of field (the
menu blur shares the profile and wins); `PlayerController.Focus.cs` focuses on the first block in the middle of the
picture (plants and leaves count, water does not), fast toward near things and slowly back. Blur starts only well behind
the focus, so the middle distance stays sharp while you build at arm's length, and a look into the distance stays sharp
everywhere.

**Files.** `Scripts/GameLook.cs`, `Scripts/GameLook.Tiers.cs`, `Scripts/Sky.cs`, `Scripts/Sky.Look.cs`, `Scripts/Sky.GlowHaze.cs`,
`Scripts/SkyBodiesView.cs`, `Scripts/UrpScenePost.cs`, `Scripts/PlayerController.Focus.cs`,
`Shaders/AtmosphereCommon.hlsl`, `Shaders/Atmosphere.shader`, `Shaders/BlockAtlas.shader`, `Shaders/MeadowLight.hlsl`,
`Shaders/FarTerrain.shader`, `Shaders/LitColor.shader`.

**Tiers.** Sun arc, ambient, sky gradient, golden hour, larger sky bodies and the glowing haze: every tier and the
browser. The longer emitter reach: High and Medium; Low, Potato and the browser keep the classic reach. Bloom runs on
every preset, but Low and Potato render to an LDR buffer, where only near-white
pixels cross the threshold. Depth of field: desktop class on Medium and High, never with *Reduced effects*, so never
in the browser.

**Known limits.** The light formula still exists in several shaders (block, transparent, meadow, far terrain, scatter,
`LitColor`); a change must reach all of them or a seam shows between near and far ground. The glowing haze is one colour
for the whole view. The brightness of the play was never compared on capture pairs after the look was complete.

## 2. Colour harmony per world — world generation 48 (#3928, suns #3942)

**What the player sees.** On a world created with generation 48 or later the plants, clouds, valley mist and water
answer the sky instead of being five independent rolls: tree crowns take one hue in a fixed relation to the sky, the
other plant species take hues from the world's scheme, clouds and mist sit beside the sky's hue, clean water follows the
sky inside a sea-green-to-indigo arc. Worlds differ in mood — balanced, vivid, pastel, earthy or gloomy — and the moods
and schemes a world can roll depend on its planet kind. Plants grow in groups with clearings between them. Every galaxy
holds exactly one promised red sun and one promised blue sun, never in the start system, and rarely a further one; all
other suns are ordinary white to orange ones. Older worlds keep all of this exactly as before.

**How.**

- **The harmony** (`src/BlocksBeyondTheStars.Shared/World/WorldHarmony.cs`): the sky is the anchor. Crowns get one hue
  per world (a neighbour, a third of the hue circle or the opposite of the sky's hue); the other species take a hue from
  the world's scheme (`Triad`, `Monochrome`, `Rainbow`); the mood (`WorldMood`) sets saturation and brightness bands, and
  no plant gets so dark that its trunk could not stay darker. `WorldPalettes.cs` holds the planet kinds (derived from the type's
  data through `MistProfiles.FamilyOf`, never from a key list), each with its own sky anchors and its own chances for the
  five moods and three schemes. A type may pin its mood in `data/planets.json` (`colourMood`, today only `arena_nigra`:
  `gloomy`). Fixed skies, fixed mists, fixed water, poison water, a fixed flora palette and white clouds or mist stay as
  authored; ship cabins and station decks (void types) get no harmony.
- **Where it is wired.** `AtmosphereTints.SkyRgb` / `CloudRgb`, `MistProfiles.For` (takes the terrain generation),
  `FluidTints.ForWorld`, `FloraTints.For` / `ForWorld`, `PlantGlowLight.PackedFor` (a glowing plant's light follows its
  leaves). Server: `GameServerWeather.cs` (mist), `GameServerBio.cs` (colour of cloned plants). Client:
  `GameBootstrap.Environment.cs`, `GameBootstrap.Meadow.cs`, `FarTerrainView.cs`, `SpaceView.Descent.cs` (the landing
  cover takes the target world's cloud and sky colour), `PlanetOrbitLook.GroundColor` and `Client.Core/PlanetMapBaker.cs`
  (the planet seen from space). `JoinAccepted.LocationId` and `WorldReset.LocationId` (appended fields, no protocol bump)
  give the client the location id the plant colours are built from.
- **Grouped flora** (`src/BlocksBeyondTheStars.WorldGeneration/WorldGenerator.FloraGroupsGen48.cs`, called from
  `WorldGenerator.ChunkFill.cs`): fields of a few blocks hold at most one group, its chance following a clearing pattern;
  inside a group the density rises, around it a ring takes what the group's cap cuts off, the free ground gets less, so
  the amount of plants per world stays about the same. Most columns of a group grow the group's species. No trigonometry,
  no allocation per column. The grass tufts on landing-pad islands are deliberately not grouped.
- **The suns** (`src/BlocksBeyondTheStars.Shared/World/StarColors.cs`, the generation-aware `ForSystem` overload,
  `PromisedSuns`): two
  of the systems the galaxy starts with, other than the start system, are picked from the seed — one red, one blue;
  every other system except the start system has a small chance (`RareShare`) of a further red or blue one; the start
  system always has an ordinary sun. The red is the existing red-dwarf anchor, the blue the existing blue anchor, each
  blended only a little toward its own neighbour. A star still depends only on the seed, its index, the start count and
  the start system, so a growing galaxy keeps its colours.
- **The gates.** `WorldDescription.ColourHarmonyGeneration`, `GroupedFloraGeneration` and `RareStarsGeneration` are all
  48, and `CurrentTerrainGeneration` is 48. The save version is unchanged (18). The full generation entry is in
  [WORLD_GENERATION.md](WORLD_GENERATION.md) §63.

**Tiers.** World data: identical on every tier and in the browser.

**Known limits.** How dark a gloomy world reads in play is only computed (the sky colour also feeds ambient light and
distance fog). Very densely planted types (flower fields) show hardly any groups: their groups sit at the density cap.

## 3. Calm tiles (#3929)

**What the player sees.** The bundled tiles are redrawn in a calm style: soft forms instead of busy pixel noise, with a
deliberately weak pattern. The light draws the grain (relief, fine grain, soft edges, §4), the tile gives
the colour. A tile a player painted (their own pack or a world texture) shows exactly as painted. Rock and ore are the
exception since #4000: they read as natural stone of up to three materials with their own grain (granite as pink
feldspar, pale quartz and dark mica; an ore as deposits seated in the rock with a darker rim and a lighter glint),
because the calm one- and two-colour rocks read as flat paint.

**How.**

- **The tile set.** The calm tiles are the game's tiles: `client/Assets/Resources/textures/<key>.bytes`, loaded by
  `GameTextures` like before; there is no second tile folder. 583 tiles: 487 redrawn with an image model from the
  previous tile as the edit reference, 80 generated greyscale forms coloured by script with the previous tile's colours
  (one or two materials), 3 generated device tiles and 13 drawn by script (the grass sides, the mushroom cap and its
  gills, the geyser hole, the device raster tiles, the burger patty). Only the 16 coat pattern masks (`creature_pattern_*`)
  and the 5 horn ramps (`creature_horn_*`) were not redrawn: the code distributes colours by them. Cut-out tiles
  (plants, leaves, critter sprites, fire) keep their exact old alpha; surfaces (hides, the suit, the held materials,
  liquids) wrap and keep their mean colour. The scripts, their order and which steps cost money are in
  [tools/ai-assets/calm_tiles/README.md](../../tools/ai-assets/calm_tiles/README.md) — a record of how the set was made,
  not a build step: the committed `.bytes` are the source of truth. Round 6 (#4000, `rock_tiles.py`) redrew the 39
  rock and ore tiles from the tiles before the calm set: the 14 rocks, the 23 ores (carbon and the tainted ores
  included) and two new bundled keys, `bedrock` and `obsidian` (painted in code before; the code painter stays as the
  fallback). Rocks keep 0.6 × the old tile's contrast, ores 0.85 × and never less than the calm tile's, and every tile
  keeps the old mean colour, so the far terrain and the map do not change. Mars's ground (#4018, `make_mars_tiles.py`, no
  model): `red_sand` is the calm `sand` tile and `red_rock` the natural `stone` tile, recoloured pixel by pixel to the
  blocks' data colours (form and seams kept). Per-key origin in
  `tools/ai-assets/texture_provenance.json` (it also keeps the other bundlers off these keys), licence notes in
  `NOTICES.md`. The art rule for tiles used on built objects:
  a tile that becomes a building piece of a larger generated object (a giant mushroom's cap, a trunk, a burger) shows
  no whole object and no seam per block.
- **The calming mask for tiles painted in code.** The rule which tile counts as calm is
  `src/BlocksBeyondTheStars.Client.Core/Textures/CalmTileRule.cs`, applied by `Scripts/BlockTextureAtlas.CalmTiles.cs`:
  a bundled still tile that wins for its key is a calm tile (the two mask families above are not, and never enter the
  block atlas anyway); a tile a player painted is exempt as well; a tile painted in code (no bundled file) and an
  animated tile are not calm tiles. The block shader fades the slots that are not exempt toward their tile's mean colour
  on natural faces at any distance (`BbtsLookCalm`, the same mix the distance softening uses), so code-painted tiles sit
  with the calm set. The atlas keeps one texel per slot (`_Sc_LookTileMask`): 1 = calm this slot, 0 = leave it alone.
  The relief and normal map keep their detail either
  way. Creature hides are calmed separately when they load (`CreatureBuilder.CalmHide`, a fixed share toward the mean).
- **Ground variants.** The three variants of a ground block (stone, dirt, sand, snow, basalt, deepslate, granite, mud)
  are, for a redrawn tile, the tile mirrored (left-right, top-bottom) with a small brightness step instead of the old
  cracks and speckles. A painted tile gets only the small brightness step, never mirrored, so a painted arrow or letter
  stays readable; a painted plant tile keeps its outline (no eroded edge).
- **The grass sod and two face tiles** (`Scripts/LookSod.cs`): grass, alien grass and mycelium show earth with the
  grass hanging over the upper edge on their sides and earth below; a giant mushroom's cap shows gills on its underside.
  The face tiles are dealt into the client's block definitions before `ShapeFaceTextures` builds its table, so
  `data/blocks.json`, the server and content validation are untouched. Face tiles taken from the redrawn set are marked
  calm like block tiles, so the calming never mixes the sod's earth and green into one colour.
- **The geyser vent** (`Scripts/ChunkMesher.Vent.cs`) is drawn as the ground block beside it (the most common natural
  full ground block among its four neighbours, else one row lower, else the block below), with its sinter ring and hole
  (`geyser_hole`, cut out) laid as a quad a few millimetres over the top. No grass carpet grows on it. For the server, the
  steam and collision it stays a geyser.

**Files.** `Scripts/GameTextures.cs`, `Scripts/BlockTextureAtlas.cs`, `Scripts/BlockTextureAtlas.CalmTiles.cs`,
`src/BlocksBeyondTheStars.Client.Core/Textures/CalmTileRule.cs`, `Scripts/LookSod.cs`, `Scripts/ShapeFaceTextures.cs`,
`Scripts/ChunkMesher.Vent.cs`,
`Shaders/BlockAtlas.shader`.

**Tiers.** Every tier and the browser (it is the tile set).

**Known limits.** `held_darkmetal` and `held_plastic` still show a faint seam. The crystal ore, ice and alien grass are
calmer but less distinctive than before. The mesher still turns the top face of the eight ground kinds in quarter turns,
so a painted arrow on a stone top can point another way. A painted log's end grain is still calmed. A closed crown
(§6) shows leaves as their mean colour, so a painted leaf picture is lost on it.

## 4. Soft block edges, joints, grain

**What the player sees.** Every opaque block face reads as a stone of its own: its border rounds off toward the
neighbour and a dark joint runs along it; about a third of the joints all but vanish, so a flat floor reads as slabs of
several sizes, not a grid. Natural faces carry a fine grain.

**How.** All in the block shader, nothing in the mesh or the data: `BbtsSoftBlock` in `Shaders/BlockAtlas.shader` tilts
the normal within the outer part of the tile and darkens a joint; each edge's strength comes from a hash of the two cells
it lies between, so both faces sharing an edge agree. `BbtsLookGrain` multiplies a fine grain onto natural faces. The
mesher's geometric bevel of convex edges (`ChunkMesher.BevelAmount`) is unchanged; the soft edge is light, not geometry.
Closed crowns (§6) take their puffs' tilt instead.

**Files.** `Shaders/BlockAtlas.shader`, `Scripts/Sky.Look.cs` (the globals), `Scripts/GameLook.cs`.

**Tiers.** Every tier and the browser.

**Known limits.** The joint is a shading effect: it does not follow a player form's micro boxes (those get real soft
edges, §13).

## 5. Grass as clumps; plants as bodies; ground cover (#3930, #3931)

**What the player sees.** Near the camera grass tops grow dense clumps of blades with a fringe at open block edges, in
the world's grass colours. Small plants are small bodies — stems, leaves, blossoms, caps — that sway in the wind; beyond
their range they are the crossed picture again, now with the body's own outline. Bare sand, earth, ash and salt carry
patches of dry tufts and a few round pebbles. Grass and its clumps also grow under a plant on a narrow foot, lower under
a plant with leaves near the ground. Strands hang from the underside of tree crowns.

**How.**

- **Grass clumps** (`Scripts/GrassClumps.cs`, `Shaders/GrassClump.shader`, `Shaders/MeadowLight.hlsl`): the mesher
  records each grass top with its open edges; `GrassClumps` draws the clumps instanced per chunk. Grass takes the
  softened sun shadow of the ground beneath it, so a blade does not flip between sun and shade under a swaying crown. The
  High preset's grass carpet is calmed under the clumps and kept out of the SSAO (`GameLook.cs`: the carpet
  leaves the depth prepass and draws right after the SSAO): the SSAO's per-frame noise on the carpet's fine strands made
  it flicker. Most of the flicker is gone; a little remains.
- **Plant bodies** (`Scripts/PlantBodies.cs`, `Scripts/PlantForms.cs`, `PlantForms.More.cs`, `PlantForms.Solid.cs`,
  `Shaders/PlantBody.shader`): the mesher records every plant (`PlantCell`, at the crossed plant, the solid plant, the
  bred plant and the flower pot) and keeps its crossed picture or simple solid form in the chunk as the far stage, with
  a mark in the leaf code. Near the camera `PlantBodies` draws the bodies as instanced copies per chunk, and the block
  shader dithers the far picture out over the cross-fade, as for crowns. Eighteen build types cover every plant block. A
  species' form is rolled per world and species from the seed (four individuals per species); crops and authored plants
  have the same form on every world. Colours come from the world's species colour, the cell's colour (dyed and bred
  plants, tree fruit) or the plant's own tile (crops, authored plants); the main colour takes its brightness from the
  leaves of the plant's tile. Hanging plants hang from the cell's ceiling, stacked strands join into one strand, water
  plants sway like in a current, glowing plants glow. Solid plants keep their old collision and are scaled like it.
  Crystals are cut stones: a narrow foot, a wide girdle, a crown and a point, lit deep where a facet turns away and
  bright where one looks into it, with a light of their own. A budget on the bodies a frame submits shrinks the range
  slowly (down to a third) in a kelp forest or a dense jungle floor and lets it grow back.
- **The baked far picture** (`Scripts/PlantBodies.FarPictures.cs`, `Scripts/PlantForms.Far.cs`,
  `src/BlocksBeyondTheStars.Client.Core/PlantFarBake.cs`, `Shaders/PlantFar.hlsl`): once per world each species with a
  body is rasterised from two sides into a cell of a small far atlas (`_Sc_LookPlantFar`, form and brightness only — the
  world colour is applied by the shader as before); the atlas' last cell maps a tile slot to its picture, and the block
  shader samples the far atlas for a crossed picture whose slot has an entry. Baked lazily over the frames after a world
  loads, about a millisecond a frame, on a desktop-class client on Low and up (`GameLook.PlantFarPictures`). This
  includes the school club's authored plants: near the camera they are bodies,
  so their far picture follows the body. Left out on purpose: the solid plants (their far stage is the opaque collider
  form) and the water lily (seen from the side a pad is a line).
- **Ground cover on bare ground** (`ChunkMesher.ScrubKindOf`, drawn by `GrassClumps`): tufts in patches (none between
  them) on sand, earth, mud, peat, ash and salt; pebbles alone on stone, basalt, granite, sandstone and moss stone, and on
  Mars's red sand and red rock (nothing grows there, #4018). In the ground's own colour.
- **Cover under plants** (`ChunkMesher.cs`, `BlockTraits.CoverUnderOf` in `ChunkMesher.HighDetails.cs`): a ground cell
  counts as open under a plant with a narrow foot. Full cover under a stem flower, a bush, a sapling, an ember bloom; low
  cover under a leaf tuft, a blade tuft, a strand, a pitcher plant; none under cushions, mosses, mushrooms, crystals and
  solid plants. Only the carpet and its clumps grow there, no loose tuft or pebble. Under water, leaves and solid blocks
  nothing changes.
- **Vines under crowns** (`ChunkMesher.cs`, right after a crown cell is recorded): under about one crown cell in six
  with air below, the mesher adds imagined plant cells (the world's hanging kelp form and colour, one to three cells
  long) that `PlantBodies` draws. Never under leaves a player placed. Display only: no block, no collision, nothing in
  the world data.

**Files.** As named above, plus `Scripts/ChunkMesher.cs`, `Scripts/ChunkMesher.HighDetails.cs`, `Scripts/GameLook.cs`.

**Tiers.** Grass clumps and ground cover: desktop class, High 24 m, Medium 12 m; on Low, Potato and in the browser the
mesher does not even record their cells (`RecordGrassClumps`), and the ground keeps the flat tile (and the existing tufts
of *Ground details*). Plant bodies and vines: desktop class, High 32 m, Medium 16 m, then the baked far picture; on Low,
Potato and in the browser no plant cells are recorded (`RecordPlantCells`) and no picture is hidden near the eye. The far
picture is baked on a desktop client on Low too, so Low shows it at every distance; Potato and the browser bake nothing
and keep the crossed tile picture. Cover under plants: where the clumps and carpet run.

**Known limits.** Thin blades (reeds, grass tufts, salt grass) lose a few pixels of width in the far picture. Pots,
hanging plants, stacked strands, the water lily's height and the solid plants' collision were never confirmed in play.
In ships and stations plants are still the old pictures and forms. The vines have no far stage. Pebbles were removed
once before (#3120, "they read as boxes"); these are round.

## 6. Tree crowns (#3932)

**What the player sees.** Near the camera a crown is a cluster of small cubes with real gaps between them that rock in
the wind, with the leaf picture running across the cubes; very close to the eye the cubes shrink away so they do not
fill the view. Farther out the crown is a closed block with a painted cube pattern, and dense forests get a closed
canopy. Conifers are cones of sloping boughs under a spire; palms carry long feathered fronds and a rosette over the
trunk. Their far stage keeps that outline.

**How.**

- **Staged clusters** (`Scripts/CrownClusters.cs`, `Shaders/BlockAtlas.shader` instanced variant): the mesher records
  every crown cell and meshes the closed far picture; `CrownClusters` draws the clusters near the camera instanced, with
  a cross-fade at the range's end and their own shadows over the nearer part of the range (beyond it the closed far cube
  casts). A budget on the clusters a frame submits shrinks the range slowly (down to a third). The leaf picture on the
  cubes is a share of the leaf tile, running across all cubes of a cell and on into the next cell in world axes; it fades
  to one colour per cube between 10 and 20 m, where a leaf would be smaller than a pixel.
- **Crown forms** (`Scripts/ChunkMesher.CrownForms.cs`, `BlockShapeGeometry.CrownForm`): for each needle and frond cell
  the mesher looks for the tree's axis (the nearest trunk block within three cells, level or up to two below). A needle
  cell becomes a bough that slopes outward in three flat steps, its length cut to a cone under the tree's top; a bare
  trunk block at the top of a generated conifer gets a spire (display only, one cell above the trunk). A frond cell
  becomes a long frond pointing away from the trunk, level while the frond continues in the next cell and drooping at its
  end; over the trunk sits a rosette of six short fronds. Without a trunk nearby the cell stays a cluster. Broadleaf,
  giant leaf and blossom crowns are unchanged.
- **Far forms** (`Scripts/ChunkMesher.CrownFar.cs`, `src/BlocksBeyondTheStars.Client.Core/CrownFarForms.cs`): beyond the
  near range the chunk draws, instead of the block shell, a simple form inside the near form's outline: a needle bough as
  one closed wedge with the same slope, the spire as a frustum, fronds as two-sided cards, the palm heart as six spokes.
  Drawn through the existing far path, so the cross-fade to the near clusters is the same.

**Files.** As above, plus `Scripts/ChunkMesher.cs`, `Scripts/Sky.Look.cs` (`_Sc_LookLeaf`), `Scripts/GameLook.cs`.

**Tiers.** Clusters, crown forms and the leaf picture: desktop class, High 48 m, Medium 24 m; on Low, Potato and in the
browser the mesher records no crown cells (`RecordCrownCells`) and every crown is the closed block. Needle and palm crowns
beyond the clusters (`CrownFarFormsTier`): on High and Medium every cell draws its far form; on Low only the crown's
outer cells do, plus the spire (the cells inside are covered by them); on Potato they are the plain leaf-block shell, no
spire. The browser follows its preset (never run).

**Known limits.** A closed crown lets no sun flecks through. In dense forests a cell may take the neighbouring tree's
trunk as its axis. Far forms reach up to 0.4 of a block over a chunk border; the chunk visibility check was not
reviewed for that. A far conifer costs about 2.6 times the triangles of its block shell on Medium and High (computed, not
measured); Low draws only the outer cells, Potato the shell. A measurement taken while the crowns were built put dense
forest worlds at about a third more frame time; nothing was measured after the look was complete.

## 7. Creatures (#3933)

**What the player sees.** Furry animals have felt, bristles, shag or strands; feathered ones have wings of real
feathers, a tail fan and a crest. Every box of every animal has rounded edges, standard animals have three blunt toes in
front of each sole, and hides are calmer. The smallest creatures — butterflies, beetles, fish, snails — are small bodies
with beating wings and wagging tails that point where they move; the ones that are lights (fireflies, glow-worms, wisps,
spore floaters, ember mites, glowing plankton) stay glowing sprites.

**How.**

- **Fur and feathers** (`Scripts/CoatForms.cs`, `Scripts/CoatForms.Feathers.cs`, `Scripts/CreatureBuilder.Coat3d.cs`,
  `Scripts/CoatMotion.cs`, `Shaders/CreatureCoat.shader`): only the standard build. Fur where a part's hide is a fur kind
  (`fur`, `velvet`, `down`, `bristles`, `shaggy`, `wool`), feathers on wings with a feather hide. `CreatureBuilder` hangs a
  coat mesh under each box and the feathers at shoulder and wrist; `CoatMotion` switches the short felt off beyond its
  range and feeds each mesh the blades' lean from the part's motion and the wing's fold. The shader takes the colour from
  the part's hide under the root, lights like `LitColor`, lets the tips shine through against the sun, and draws after
  the opaques without a depth pass, so the SSAO does not flicker on it. No shadow. A felt budget per frame shrinks the
  felt's range when many furry animals stand close.
- **Rounded boxes** (`Scripts/RoundedCube.cs`, used by `CreatureBuilder.TileCube` for all creature builds): the radius is
  a share of the box's thinnest side (`GameLook.Round`, 0.12 — 0.2 until 2026-11-23, when Justus found the animals "too
  round"), with the cube's texture coordinates; touching boxes show a joint.
- **The far stage of rounded boxes** (`Scripts/RoundLod.cs`): every rounded part of an avatar, a tool or an animal is
  registered with its plain twin (`RoundLod.Assign`); a pass over a few hundred parts a frame swaps by distance — a part
  turns plain where its rounding is under half a pixel (on Low, Potato and the browser a whole pixel), measured to the
  nearest enabled camera.
- **The smallest creatures** (`Scripts/CritterBodies.cs`, `Shaders/CritterBody.shader`, `Scripts/MicroFaunaView.cs`):
  22 of the 28 micro-fauna kinds as bodies of boxes and plates without a tile, in three colour slots from the kind's tone;
  eleven build types (moth, buzzer, dragonfly, beetle, ant, spider, crawler, snail, fish, hopper, balloon). Wings and tails
  move in the shader from the time and a value per animal, one material for all. The shader is in the always-included
  list (`Editor/BuildScript.cs`).

**Tiers.** Fur, feathers, toes and rounded creature boxes: desktop class on Medium and High (Medium: half the felt range
and density). Low, Potato and the browser keep the plain boxes. Calmer hides and the critter bodies: every tier and the
browser. The far stage's swap distance is the whole-pixel one on Low and in the browser; on Potato figures and tools are
plain boxes anyway (§8).

**Known limits.** Coat meshes are built per species and never freed (long sessions over many worlds collect memory);
after a preset change, animals already built keep their old felt density. Triangle counts per animal were counted in an
offline preview, not measured in play. The critters are lit and go dark at night like the ground (the old sprites were
unlit); in caves they are lit by the sun term, not the cave light.

## 8. The avatar and held tools (#3934)

**What the player sees.** The player figure, the NPCs and every held tool have rounded edges. Barrels, shafts, antennas,
handles and rods have flattened (chamfered) long edges, flanges, lens rings and caps too; a drill's bit is a cone. The
NPCs' hoe and hammer handles are flattened like every other handle.

**How.**

- **Avatar** (`Scripts/PlayerAvatar.Round.cs`, hooks in `PlayerAvatar.cs`, `PlayerAvatar.PaintMesh.cs`, `HeldItem.cs` for
  the first-person arm): rounded boxes (`GameLook.AvatarRound`, 0.10 of the thinnest side — 0.16 until 2026-11-23, with
  the animals' rounding); a painted part gets at most one texel of its paint tile as radius, so the paint
  stays where it was painted, and the head under a painted face likewise; very thin parts stay sharp.
- **Tools** (`src/BlocksBeyondTheStars.Client.Core/HeldPartForms.cs` — the rule, `HeldFormMeshes.cs` — the meshes,
  `HeldItemShapes.cs`, `HeldWorkTools.cs` — the NPCs' tools; client `HeldTextures.ToolPartMesh` / `FormMesh`,
  `HeldItem.PartCube`): a part with an exactly square cross-section becomes a chamfered bar when it is long against its
  thickness, a chamfered plate when it is thin against its width; the frontmost bar along the axis of a drill becomes a
  cone; wide housings and screens stay rounded boxes. A part may name its form in the data — `"f": "box" | "chamfer" |
  "cone"` on a `heldModel` part (`HeldModelPart.F`, [HELD_ITEMS.md](HELD_ITEMS.md)) — which overrides the rule. A
  player's own tool look keeps its boxes (`ToolLook.ToParts` writes `box`); the wire format is unchanged.

**Tiers.** High, Medium and Low (desktop and browser): rounded avatar parts, rounded, chamfered and cone tool parts;
the far stage (§7) turns parts plain with distance. Potato: plain boxes for the avatar and every tool part — no rounding,
chamfer or cone (`GameLook.PlainFigures`). This is fixed for the run from the preset at start-up, so a figure never mixes
round and plain parts; a change to or from Potato shows at the next start.

**Known limits.** A plain avatar has several times the vertices it had (no measurement in play). Parts whose size
changes after building (jump boots) keep their mesh. A figure in a preview with a hand-triggered camera (poster or clip
capture) may show plain parts. Held blocks, miniatures and icons are untouched.

## 9. Debris and the regrow marker (#3935)

**What the player sees.** A broken block splits into pieces that show its own texture; drill chips show a small cut of it.
Plants, crystals, torches, lanterns, ladders and flower pots break into much smaller pieces that start close to them,
leaves into medium ones; a full block keeps the old size. Pieces of a glowing block glow. Where a harvested plant regrows,
a small version of that very plant stands and grows, instead of two green quads.

**How.**

- **Debris** (`Scripts/FxKit.Debris.cs`, `Scripts/ChunkMesher.Debris.cs`, `Shaders/FxDebris.shader`): each particle
  carries two vectors through `SetCustomParticleData` — its tile rectangle in the atlas and its tint (world tint mode,
  glow) — and the two debris systems pass them as vertex streams to `FxDebris.shader`, which reads the block atlas as the
  global `_Sc_FxAtlas`. Still one particle system and one draw per debris kind. Size and start offset per block kind come
  from `ChunkMesher.DebrisFit`; a smaller piece shows a smaller cut so the texel size stays about the block's. Where plants
  are bodies, their pieces take the body's colours (`PlantBodies.TryBodyColours`) instead of the old crossed picture.
  In space: mined cells and chips show the cell's tile, breaking asteroids one of their cells' tiles, hull hits and
  destroyed ships `metal_panel`, robot bolts `scrap_metal`. Callers: `MiningFx.cs`, `SpaceView.Structures.cs`, `SpaceFx.cs`,
  `SpaceView.Combat.cs`, `FxDefeat.cs`.
- **The regrow marker** (`Scripts/FloraGrowthFx.cs`): the plant that regrows there, small, growing from about a sixth to
  most of its size, then the real plant takes over. Where plants are bodies it is the species' body
  (`PlantBodies.CreateStandIn`, same mesh, turn and individual as the cell's plant); elsewhere the species' crossed picture
  on the chunk material, or a slim column with the species' tile for solid forms. Hanging plants grow down.

**Tiers.** Every tier and the browser; body colours and body markers where plant bodies run (§5).

**Known limits.** Gotcha for anyone touching it: the particle system's *Custom Data* module must stay off — switched on
it scales the values written by script, and the cuts land in the wrong place; every new debris particle gets both
vectors written, even a plain one, or a dead particle's tile can linger in the slot. Not reproduced on debris: dyed cells,
painted hulls, the biome looks, ground variants. The regrow marker does not copy the per-plant size, lean and offset the
mesher rolls (a small jump at the end) and is invisible beyond the bodies' range on High and Medium.

## 10. Clouds (#3935)

**What the player sees.** Cumulus and layer clouds are heaps of round billows on a flat underside, every billow lit for
itself: warm on the sun's side, the sky's colour in its shade, darker undersides, a bright rim at thin edges toward the
sun. High cirrus stays as before.

**How.** `Scripts/Clouds.cs` (`GenerateBillowTexture`: the card's texture carries the surface direction in red and green,
thickness in blue, coverage in alpha; `BillowPair` takes the planet's cloud colour and only the hue of the sun light and
the sky ambient), `Shaders/Cloud.shader` (`_Billow` branch). Gotcha: `Material.SetColor` converts a property declared as a
colour from sRGB to linear once more, so the pair is passed sRGB-encoded. Unchanged: cloud shadows, the sea of clouds, the
cloud shell seen from space, weather and drift (`CloudDeck`).

**Tiers.** Every tier and the browser (the same number of cards as before); the desktop client on High gets ten more low
clouds.

**Known limits.** Clouds in weather and at night were never confirmed in play.

## 11. Water (#3935)

**What the player sees.** In shallow water the bed shows through with a turquoise cast (or the world's water colour) and
the sun's moving web of light on it while it is lit; fine ripples break the reflection and the sun's path into glitter.

**How.** In the screen-space part of `Shaders/BlockAtlasTransparent.shader`: in the shallows the surface covers only a
fraction of what the tile says (foam stays), the bed gets the cast and the caustic web, and the water's own colour is a
light turquoise there. Only while the camera is above water. On generation-48 worlds clean water follows the sky inside
its hue arc (§2).

**Tiers.** Medium and High (the only presets with the water's screen-space part); Low, Potato and therefore the browser
keep the plain water.

## 12. Devices as forms inside their cube (#3936)

**What the player sees.** Workbench, forge, tanks, desks, terminals, machine housings, crates and other devices have a
form inside their block — a worktop on legs, a forge with a fire niche and a chimney, tanks with a base, a glowing column
and a lid, desks with a key deck and a screen — while the block still takes the whole cell: collision, mining, aiming and
the placement ghost are the cube's. A device with a front keeps the direction it was placed in.

**How.**

- **Data model** (`src/BlocksBeyondTheStars.Client.Core/DeviceForms.cs`): a form is up to 48 boxes on an eighth-of-a-block
  raster (the custom-shape format's 8³ grid), each with a colour slot. Each slot names a tile for top, bottom, side and
  front, and each tile a mapping mode: *by position* (the face shows the part of the tile that lies at its place in the
  cell — for raster tiles and flat front pictures) or *whole picture* (the tile fills the face — for a picture of a whole
  thing). `DeviceForms.FromVoxels` reads a form drawn in the game's form editor (the cell value is the slot), so a
  device form can later be drawn in the editor instead of in code.
- **The form table** (`DeviceFormTable.cs`): forms for 25 devices and 3 crates. The workbench has no back wall: it is the
  workshop station's marker, and a marker block keeps its full top, because the station decor (§14) stands on it.
  Devices without a sensible form stay cubes — the Crystal Net parts, stalls, station and ship core blocks, drills, the
  matter sender. `data_cache` and the cockpit marker `ship_helm` get a plinth with a glowing band.
- **Faces and turning** (`DeviceFormFaces.cs`, client `Scripts/DeviceFormGeometry.cs`, `Scripts/ChunkMesher.DeviceForms.cs`,
  `Scripts/LookDeviceForms.cs` — registers the forms' tiles in the atlas): faces covered by another box of the same form
  are left out; the form turns so its front lies where the cube shows its front picture — the stored front
  (`CubeFacing`, [CUSTOM_SHAPES.md](CUSTOM_SHAPES.md) "The front"). Devices that had a front already (the forge, the
  terminals and desks …) keep storing it; five formed devices store it now too: the workbench, the three crates
  (`crate`, `wood_crate`, `transport_crate`) and the thumper (`"facing": "toward"` in `data/blocks.json`), so building
  beside them never turns them, and their top picture turns with the front (`CubeFacing.TopUvTurns`). The other formed
  devices look the same from all four sides and store nothing. Blocks placed before, and blocks placed by ship layouts,
  settlements and kits (descriptor 0), keep the front derived from their open neighbours.
- **Neighbours.** A neighbour's face toward a device is drawn (the device no longer seals it); the decision depends on the
  block id alone, so it is the same in every chunk.
- **Soft edges** (`DeviceFormSoft.cs`, `Scripts/ChunkMesher.DeviceSoft.cs`): the same 45° chamfer as player forms
  (`ShapeChamfer`, §13); edges on the cell border stay hard, a strip takes the tile, mapping and light of one of its two
  faces (vertical before horizontal, the front first). A triangle cap (`ChunkMesher.DeviceSoft.cs`) would keep a form
  hard if its chamfer grew too large; at its value all 28 forms are soft.
- **Material tiles and the micro-cube raster.** Many device tiles are a picture of the whole device on a background — fine
  on a cube's face, wrong on a small part, which would show half a button. The forms therefore use material tiles
  (`devform_*` in `Resources/textures`: dark and light housing metal, deck with keys, screen, embers, two tank liquids,
  vent louvres, signal lamp). Their pattern sits on the micro-cube raster — one eighth of a block is eight texels: a key
  per micro cube, a louvre per eight texels, screen rows of eight texels, housing plates of 32 texels — so any box that is
  a whole number of micro cubes shows whole keys and louvres. The rule for any tile on a formed thing: its pattern lies
  on the micro-cube raster or it has no motif; a picture of a whole object belongs only on a whole face.
- **What keeps its look.** A device a player reshaped or painted keeps that. Icons, held blocks and the far ship hull stay
  cubes; ships, trains, lifts and miniatures run through the same mesher and get the forms. `StationScreenView.cs` hangs
  the station screens onto the `data_cache` plinth's column.

**Tiers.** The forms are chunk geometry: every tier and the browser (never run there). Soft edges: desktop class on
Medium and High (`BlockShapeGeometry.SoftTierAllows`); Low, Potato and the browser keep hard edges.

**Known limits.** A form has about 32 to 74 triangles instead of a cube's 12, the most expensive one with soft edges
(the workbench) 228 — counted, not measured in play. Light and shadow still treat the device as a full cube. Bullet
holes and marks on a device's side float in
front of recessed parts. Standing on a low device (a stretcher) you stand visibly above the form.

## 13. Player forms: colour slots and soft edges (the world side) (#3941)

**What the player sees.** A self-made form can use five colour slots — main, second, accent, dark, glow — and its outer
edges are softly chamfered on High and Medium.

**How.**

- **Colour slots** (`src/BlocksBeyondTheStars.Client.Core/Forms/ShapeColourSlots.cs`, `ShapeColourSlotRule.cs`): the
  cell value of a form (`Shared/World/CustomShape.cs`) is the slot — 1 main, 2 second, 3 accent, 4 dark, 5 glow, higher
  values read as main. Format, server, saves and wire are unchanged; to them every non-zero value is "filled". The client
  merges the cells per slot; collision keeps the slot-blind boxes (`AddShapedBlock` takes separate visual and collision
  faces for player forms). The rule acts on the per-vertex values: second is a lighter step of the block's colour, dark a
  darker one (less light and, on a dyed block, a darker hue), glow shines at least at a set strength, accent is the
  opposite colour of the dye on a dyed block and a shiny metal inlay on an undyed one (the mesher cannot read a tile's
  mean colour off the main thread). A form whose slot version needs more than 48 boxes is drawn in one colour.
- **Soft edges** (`src/BlocksBeyondTheStars.Client.Core/Forms/ShapeChamfer.cs`, `Scripts/BlockShapeGeometry.ShapeLook.cs`,
  `Scripts/ChunkMesher.ShapeLook.cs`): a 45° chamfer on the form's outer edges, about half a micro cell wide and never
  wider than the cube bevel; edges on the cell border stay hard; above a face cap the form keeps hard edges. Painted and
  see-through forms stay as before. Collision is unchanged.

**Tiers.** Colour slots: every tier and the browser. Soft edges: desktop class on Medium and High
(`BlockShapeGeometry.SoftTierAllows`).

**Known limits.** Under the calm tiles, undyed second, accent and all glow cells keep the full tile pattern beside calmed
main cells. The placement ghost and the icons show the chamfer in one colour. An older client shows a slotted form in
one colour. Round primitives inside player forms are not part of this.

## 14. Ship station decor (#3937)

**What the player sees.** The cockpit desk is a body of its own — a base, a body, a top, two cheeks, two key decks
sloping toward the pilot and a tilted frame with the flickering screen — facing the pilot; lab and console terminals are
the same in small (green and teal screens) and face the side you stand on; the medbay's capsule stands on a base with a
collar and a cap. The housing of desk and terminals is light, not dark. The holographic system map and the workshop
sparks are unchanged.

**How.** `Scripts/StationDecorView.cs`, `StationDecorView.Forms.cs`, `StationDecorView.FormMesh.cs`,
`StationDecorView.Facing.cs`, `src/BlocksBeyondTheStars.Client.Core/StationDecorForms.cs` (boxes and mapping),
`StationDecorFacing.cs` (the side). The decor still sits in the cell over the station's marker block, without collision,
and the server knows nothing of it. Boxes are multiples of an eighth of a block, metal and keys are mapped by size (an
eighth of a block = eight texels), the screen tile only on the screen's front, in grey shown in the station's colour.
Drawn with `LitColor`, so it stands in the look's light; the housing uses the light housing tile
(`devform_metal_light`) and the figures' base light. There is no other decor path. **How a console finds its side:**
the cockpit desk's front faces away from the ship's nose — the nose runs from the
engine cells to the helm (`TransitionCockpit.Nose`, as the transition camera uses it); without a ship model it assumes
+Z, like the standard ships. A terminal faces the side a player can stand on (the neighbouring cell free at marker and
decor height, floor below); with several, the one toward the ship's centre. The side is found once per build; while the
marker block is not loaded yet the view asks again twice a second and turns with what it knows after half a minute. The
form hangs under its own child object (`DecorFormTurn`), so turning never touches the screen's animation.

**Tiers.** Every tier and the browser (never run there). One draw call more per station.

**Known limits.** The heal tank's cap, like its capsule, reaches out of the one cell.

## 15. The cabin light limit (#3938)

**What the player sees.** Ceiling lamps aboard are separate bright lamps with a lens and a rim, not one white sheet.

**How.** One soft knee in the block shader (`BbtsLookCabinLimit` in `Shaders/BlockAtlas.shader`), only for strongly
glowing faces under a roof: it caps how bright a white texel of such a face can get, so a lamp no longer pours most of
itself into the bloom over its neighbours; the lens and the rim keep their difference. `Scripts/LookCabinLight.cs` sets
the value, `UrpScenePost.ApplyGradeNow` applies it. Under the open sky nothing changes. Aboard, the bloom keeps its classic
values anyway (§1).

**Tiers.** Every tier and the browser.

**Known limits.** It also dims light strips, torches, lanterns, fire and lava under a roof, force fields, `data_cache`
and glowing plants in caves — anything strongly glowing under a roof.

## 16. The editors (#3939, #3940, #3941)

**What the player sees.** The three editors show a work as the world will show it.

- **Texture editor.** A block tile is shown as a wall of 3×3 blocks built by the real chunk mesher on a tiny chunk and
  drawn with the block shader — the eight ground kinds turned and in their variants, with relief and soft edges — and
  follows the canvas while you paint. Plant tiles show as three crossed pictures; tiles the world tints get a "World
  colour n of 3" button. A toggle switches every editor's preview between *As in the world* and *Pixels only*. Soft
  tools: a soft brush, a smoothing brush, *Smooth all*, *Calm all*, *Make seamless*, *Brightness only* (for tiles the
  world tints — plants, leaves, bark — the canvas shows grey), and a *Calm when saving* stepper.
- **Avatar designer.** The figure stands in the look's light (switchable) with the world's rounded edges.
- **Form editors** (the small in-world editor and the main-menu Form Editor): the form with its block's tile and the block
  shader; the in-world editor shows the material of the crafting window, stone without one. A colour-slot palette
  (main, second, accent, dark, glow) paints slots into the cells.

**How.**

- **Shared preview** (`Scripts/LookPreview.cs` — the toggle, one setting for all editors for the session;
  `Scripts/LookPreviewLight.cs` — on every preview camera it sets the look's globals before that camera renders and
  restores them after, so a work looks the same in the main menu and in a world — before, `Sky.OnBeginCamera` did not
  reset the model-light global per camera). The wall: `Scripts/LookBlockWall.cs`, its own small preview atlas
  `Scripts/LookPreviewAtlas.cs`, `Scripts/TexturePreviewRig.Look.cs`, cells from
  `src/BlocksBeyondTheStars.Client.Core/Textures/LookPreviewCells.cs` (a painted ground tile shows as after saving: not
  calmed, not mirrored, only the brightness step). Shapes: `Scripts/LookShapePreview.cs`, `ShapeEditor.LookPreview.cs`,
  `FormEditor.LookPreview.cs`. Avatar: `Scripts/AvatarPreviewRig.cs`.
- **Painted tiles and "calm when saving"** (`Scripts/TextureEditor.Calm.cs`): the canvas model always holds what was
  painted; saved, published, exported and submitted is the calmed version (0–100 % in steps of ten). The draft carries the
  value (older drafts read 0). Because saving clears the draft, the original is kept locally in
  `AppPaths.Root/texture_originals/<key>.json` (never sent to a server) and comes back when the editor reopens a tile the game
  still shows in exactly that saved version. In the world a painted tile is exempt from the calming (§3).
- **Soft tools** (`Scripts/TextureEditor.Soft.cs`; Unity-free `src/BlocksBeyondTheStars.Client.Core/Textures/TileFilters.cs`,
  `SeamFft.cs`, `PixelCanvasModel.Soft.cs`): every action is one undo step. *Make seamless* bends the brightness near the
  edges with the method of `tools/ai-assets/heal_seams.py` (without its grid alignment; alpha untouched) and reports the
  seam before and after.
- **Colour slots** (`Scripts/ShapeSlotPalette.cs`, `ShapeEditor.Slots.cs`, `FormEditor.Slots.cs`, the model
  `src/BlocksBeyondTheStars.Client.Core/Forms/FormCanvasModel.cs` keeps the values through paint, mirror, shift, load, save
  and undo). The budget line counts the larger of the two box counts (slot version and collision).

**Tiers.** Preview cells: High and Medium 64-texel cells with relief, Low and Potato 32-texel cells, the browser 32-texel
cells without heights. Everything else: every tier.

**Known limits.** The preview shows leaves as the cut-out leaf picture, not as a cube crown, and plants as crossed
pictures, not bodies; the three sample world colours are fixed tones; the new buttons cannot be reached with a gamepad;
*Pixels only* shows a form in one colour. It was never checked on screen that the per-camera values stay with the preview
camera (URP is assumed to submit a camera's draws before `endCameraRendering`).

## 17. Terrain forms (#3964)

**What the player sees.** Natural ground is no longer a field of smooth cubes. Part of it is drawn as a form made of micro
cubes (eight per block edge):

- **Edges** (top open, one or two sides open): broken-off edges, a small step, a double step, a missing corner; on grass
  the earth under the sod falls back so the grass hangs over as a lip, or the grass edge breaks off.
- **Walls** (top covered, a side open): strata grooves that line up across neighbouring blocks (their height follows the
  world height and a coarse field), niches, a ledge that stands out while the face steps back, chipped faces.
- **Ceilings** (only the bottom open): a breakout of rock.
- **Flat ground** (only the top open): a surface pattern that suits the material, laid over whole regions so it reads as
  a surface — flagstone joints (stones of two to three and a half blocks, in running bond) or cracks on rock and ice, wind
  ripples (every nine to twelve eighths, along X or Z) or small dips on sand, snow, ash and earth, shallow dips in grass.
  Only the top eighth is touched.

Forms come in rough and calm regions (a smooth field 24 blocks wide; about a fifth of the ground is calm), and each world
has its own strength from its seed. Walking, mining, aiming and building are unchanged: **the collision is the cube**.

**Which cells.** `TerrainForms.FamilyOf` (`src/BlocksBeyondTheStars.Client.Core/TerrainForms/TerrainForms.cs`): rock (stone,
basalt, granite, sandstone, moss stone, deepslate, dark and blackened sandstone, coral rock, sulfur stone, tainted stone,
ice, red rock), soft ground (dirt, mud, peat, ash, salt, sand, black sand, red sand, snow) and grass (grass, alien grass, mycelium). Never
formed: a cell with an edited mark (placed, stamped or weather snow, #3118), a shape, front or paint descriptor or a dye;
ores, devices, plants, wood, leaves, glass; a cell beside water or lava (shores and sea floors stay cubes); and a cell with an
open side into a **dug cell** (#4011) — natural rock, soil or ore that a stored edit replaced (`ChunkData.IsDugLocal`,
the rule in `Shared/World/DugCells.cs`), so the walls of a tunnel or a room a player cut out of a mountain stay flat. The
server rebuilds the dug marks from the edit rows when it loads a chunk (an edit whose block differs from the generated one
over counting terrain) and on every live mine (`ServerWorld.SetBlock`), and streams them as `ChunkDataMessage.DugRuns`; the
client marks a live mine itself with the same rule (`ClientWorld.ApplyBlockChange`). Picked plants, felled trees, drained
water and melted weather snow leave the cell natural. Existing worlds are covered — every mined cell already has its row.
The choice is a
hash of the world cell and the world seed (`GameLook.TerrainFormsSalt`, set on join), so every client draws the same.

**How.**

- **Rules** (`TerrainForms.TrySelect`, Client.Core, no Unity): the cell's spot from its open sides, a chance per family and
  spot (`BaseChance`) times the region field, the world's strength and `GameLook.TerrainFormsDensity`; then a recipe carves
  a full 8³ grid. Integer geometry and plain float products only, identical on every platform.
- **Faces** (`TerrainForms.BuildFaces`): a face points from a filled micro cell to an empty one. The cell draws it when the
  filled one is its own (toward its own space, open air, or a formed neighbour's empty micro cell), or when the filled one
  lies in a full, unformed neighbour and the empty one is its own — the **back wall**: that neighbour takes the formed
  cell for a full cube and culls its face, so this cell closes the gap itself. Faces are merged into rectangles per plane.
  A neighbour in the same chunk is read micro cell by micro cell (`TerrainFormContext.FormAt`), so a groove or a step runs
  on into it; a neighbour in another chunk counts as full, because the build cannot see that chunk's edited marks — a back
  wall too many, never a hole.
- **Mesher** (`Scripts/ChunkMesher.TerrainForms.cs`, the branch after the device forms in `ChunkMesher.BuildGeometry`): only
  the planet streamer passes `terrainForms: true`; ships, stations, editors, miniatures and the tests keep the cube (the
  mesher golden hashes do not move). The formed cell records its collider faces with the cube rule (as a device form) and
  emits its faces into the opaque submesh with the device-form face streams: each face shows the tile the cube shows on
  the side it looks to, sliced by position (the grass sod's side tile on the sides); faces on the cell's surface take that
  cube face's light and corner AO, inner faces the light of the cell's first open side and the inner shade of the device
  forms. A form that breaks the top edge (`topCarved`) keeps the grass carpet and tufts off its cell.
- **Tier** (`GameLook.TerrainFormsNow`, part of `MeshTierKey`): a preset change across Medium remeshes the loaded chunks.

**Files.** `src/BlocksBeyondTheStars.Client.Core/TerrainForms/TerrainForms.cs`, `Scripts/ChunkMesher.TerrainForms.cs`,
`Scripts/ChunkMesher.cs`, `Scripts/GameLook.TerrainForms.cs`, `Scripts/GameLook.Tiers.cs`, `Scripts/GameBootstrap.cs`,
`Scripts/GameBootstrap.ChunkStreaming.cs`. Tests: `tests/BlocksBeyondTheStars.Client.Tests/TerrainFormsTests.cs` (the same
cell gives the same form, the merged faces cover exactly the micro faces of the rule, two formed neighbours draw their
border once, flat forms touch only the top layer and edge forms keep the inner half of the top, the face budget).

**Tiers.** Desktop class on Medium and High. Low, Potato and the browser keep the cube; the mesher does not look for forms.

**Known limits.** Tuned by eye in a playtest on 2026-11-23, not measured: an edge or wall form has about 15 faces, a
patterned flat cell 8 to 9, a cube 1 or 2. The forms have hard edges (no chamfer); shadows, sky light and corner AO still
treat the cell as a cube. On a step edge you stand up to half a block above the drawn form. A groove or joint that meets a
chunk border ends in a small cross wall. Across the world's X seam the hash sees other coordinates, so the forms may jump
there.

## Frame cost

Measured 2026-11-23 (#3947) with PerfProbe on the release builds v2026.11.26 (before the look) and v2026.11.27 (the look),
one run each, seed 424242, 30 s idle + 30 s walk, quiet machine. ⚠ Both builds predate #3954, so the probe sampled the
prologue's camera flight, not the player's view: the comparison is fair (the same scene on both sides), the absolute
numbers are not the cost in play.

**Desktop** — reference laptop (Ryzen 9 PRO 7940HS, RTX 2000 Ada Laptop GPU, Direct3D 12), view distance 8, VSync on;
idle phase, GPU p90 and main-thread p50:

| Preset | v2026.11.26 | v2026.11.27 |
|---|---|---|
| Potato | 60 fps · GPU 3.1 ms · main 5.4 ms | 60 fps · GPU 3.9 ms · main 5.5 ms |
| Low | 60 fps · GPU 5.5 ms · main 5.5 ms | 60 fps · GPU 8.9 ms · main 5.7 ms |
| Medium | 60 fps · GPU 13.7 ms · main 6.4 ms | 40 fps · GPU 19.6 ms · main 10.6 ms |
| High | 37 fps · GPU 24.4 ms · main 7.3 ms | 28 fps · GPU 34.2 ms · main 15.9 ms |

Walking: Medium 55 → 43 fps, High 35 → 30 fps; Potato and Low stay at 60. So Potato and Low keep their frame rate; on
Medium and High the look costs about 40 % more GPU time and nearly twice the main-thread time — Medium and High are where
the near drawers (crown clusters, grass clumps, plant bodies) run. This laptop calibrates itself to Medium, so it drops
from 60 to about 40 fps there. The extra cost was accepted (2026-11-23); no optimisation is planned for it.

**Browser** — headless Chromium on the same laptop (it renders on the Radeon 780M integrated GPU through ANGLE/D3D11),
view distance 3, the browser's 60 fps cap, no GPU timer: Potato and Low, WebGPU and WebGL 2, 58–60 fps idle and 53–57 fps
walking on both builds, differences within run-to-run noise. No new console error; WebGL 2 logs the same 187 shader
warm-up warnings as v2026.11.26 (all before the join). The preset stays at Low (#3946), where v2026.11.26 still rose to
Medium. At the cap these numbers show that the frame fits, not how much room is left.

The block shader reads one texture more than before (the plants' far atlas), and the browser limits textures per shader;
the browser runs above logged no shader or texture error on WebGPU or WebGL 2. #3947 was closed on 2026-11-23.

### In the play view, and where the processor's time goes (2026-11-26, epic #4051)

Measured again with PerfProbe after #3954 — the player's view, not the prologue — on the same laptop: 16 logical
processors, Direct3D 12, view distance 8, VSync at 60 Hz, seed 424242, a quiet machine. The game fills the laptop's own
panel: **3840 × 2400**, with MSAA 4× on High, 2× on Medium, and render scale 0.75 without MSAA on Potato. A 1080p
display has less than a quarter of those pixels, so the GPU times below are this panel's, not a typical player's.
"Idle" stands where the player spawns, aboard the starter ship, after the view has settled; one run each unless a range
is given.

| Preset | Before (main `a061a77f`) | After this package |
|---|---|---|
| Potato | 60 fps · GPU 4.0 ms · main 4.2 ms · render 1.7 ms | 60 fps · GPU 4.0 ms · main 4.1 ms · render 2.0 ms |
| Medium | 40 fps · GPU 19.6 ms · main 9.9 ms · render 3.1 ms | 40 fps · GPU 18.8 ms · main 7.5 ms · render 3.1 ms |
| High | 27–28 fps · GPU 33.6–34.0 ms · main 13.4–13.8 ms · render 5.8 ms (two runs) | 28–30 fps · GPU 30.6–32.9 ms · main 10.4–13.1 ms, median 11.2 · render 5.0–6.1 ms (five runs) |

(GPU and main thread are medians; the main thread's time excludes its wait for the picture to be shown.) On Medium and
High the graphics card is the limit on this panel, so the frame rate barely moves; the GPU time itself swings by about
1.5 ms between runs. The main thread lost about a quarter of its work on Medium and a sixth on High. What the package
bought shows where the processor is the limit — in how fast the world appears:

| High, view distance 8 | Rule before (four builds a frame) | This package |
|---|---|---|
| The loading veil lifts after | 13.1 s | 11.3 s |
| The view is fully meshed after | 17.6 s | 9.8 s |
| Walking: a chunk waits for its first mesh — median / 90th percentile / longest | 0.9 s / 2.9–4.4 s / 9.5 s | 0.2 s / 0.8 s / 1.2 s |
| Cores the mesher keeps busy while the view fills | 1.4 | 3.3 |
| The far view (1024 blocks, 104 patches) is complete after | 10.0–10.4 s (one worker) | 7.3–8.4 s (three workers) |

Both columns come from the same build: PerfProbe's `-perfFeature meshbudget=frame,loadbudget=off` runs the rule before
(#4045). The view's last chunk arrives after about 8 s — that is the server's stream, the floor for any client. Medium:
veil 11.3 s, meshed 9.9 s, a chunk waits 0.15 s (median) and 0.55 s (90th percentile) while walking. Potato: veil 9.5 s,
meshed 8.0 s (10.1 s with the rule before), 0.05 s and 0.08 s (0.22 s and 0.55 s before).

Where the main thread's time went on High, from profiler captures of a Development player (their absolute times are
higher than a release player's):

- **The near drawers.** Before, each of the five drawers ran an `Update()` on every loaded chunk that had data for it:
  2150 calls a frame, 4.6 ms. Now one pass walks the chunks in reach (#4046): 2.2–3.1 ms with 86–130 chunks in reach,
  and nothing on Potato and Low. What is left is handing the instanced calls to the engine.
- **The far view's `Update()`**: 2.6 ms → 0.1 ms. Its column mask asked the world generator for every drawn chunk's
  surface four times a second, and the generator's calibration lookup allocated on every call (#4052).
- **Allocations on the main thread**: 38 300 a second → 6 300 (3.0 → 1.6 MB a second); the far view's share fell from
  32 700 a second to 130.
- **The chunk pass** (uploads and the copies a dispatch makes): about 1 ms per frame while walking, unchanged per chunk;
  an upload takes 0.3–0.6 ms, a large one 3 ms.
- **Receiving chunks**: 0.13–0.26 ms per frame while walking, of which the envelope's decode is 0.02 ms — measured and
  left on the main thread (#4047).

A chunk's geometry takes the mesher about 1 ms on its thread when the chunk is air or rock (the median) and 30–45 ms
when it is full of surface detail (the 90th percentile; the longest took 130 ms).

**Graphics Jobs** (#4049), the same code built with and without them, main-thread and render-thread medians, idle:

| | Direct3D 12 (native jobs, split threading) | Direct3D 11 (legacy jobs) |
|---|---|---|
| High | main 10.8 → 7.8 ms · render 5.6 → 1.7 ms | main 12.0 → 8.1 ms · render 4.2 → 4.4 ms |
| Medium | main 7.5 → 5.9 ms · render 3.1 → 1.4 ms | — |
| Potato | main 4.1 → 2.9 ms · render 2.0 → 0.7 ms | main 3.9 → 3.3 ms · render 1.0 → 1.1 ms |
| Potato held to four logical processors | main 4.3 → 4.5 ms; walking 55 → 52 fps; the world ready after 14.1 → 15.2 s | — |

No extra long frames in any run. They win on a large machine on both graphics APIs — the measurement of 2026-09-04 that
switched them off no longer holds — but the one run on four logical processors showed no gain, and Potato exists for
such machines; Linux is unmeasured. They stay off until a real small machine has been checked (`BuildScript.cs` carries
the numbers; `BBS_GRAPHICS_JOBS=1` builds a player with them).

Not measured here: the far view's 2048-block range, the browser, Linux, and any machine but this one.

### What High's GPU time is made of (2026-11-26, #4050)

A sweep with PerfProbe's `-perfFeature` switches: desktop High on the same laptop and panel (3840 × 2400, MSAA 4×),
standing still on open ground beside a wood (the probe's outdoor phase, seed 424242, at 28 / 90 / 80), one cost-bearing
part switched off per run, 15 s each. The two runs without a switch, first and last of the series, read 27.4 and
27.6 ms, so a difference of half a millisecond is real here.

| Switched off (or changed) | GPU time, median | Saved | Frame rate |
|---|---|---|---|
| nothing | 27.5 ms | — | 33 fps |
| render scale 0.5 — a quarter of the pixels | 12.2 ms | 15.3 ms (56 %) | 59 fps |
| every shadow (`shadowdist=0`) | 19.2 ms | 8.3 ms (30 %) | 40 fps |
| the near crown clusters (`crowns`) | 23.6 ms | 3.9 ms (14 %), and 1.2 ms of the main thread | 37 fps |
| ambient occlusion (`ssao`) | 23.9 ms | 3.6 ms (13 %) | 36 fps |
| volumetric light (`volumetric`) | 24.0 ms | 3.5 ms (13 %) | 36 fps |
| MSAA 4× → none (`msaa=1`) | 24.7 ms | 2.8 ms (10 %) | 36 fps |
| every post effect (`post`) | 25.4 ms | 2.1 ms (8 %) | 35 fps |
| Medium's shadow tier: 70 m, two cascades (`shadowdist=70,cascades=2`) | 25.6 ms | 1.9 ms (7 %) | 35 fps |
| crowns and plants cast no shadow of their own (`nearshadows`) | 26.4 ms | 1.1 ms | 34 fps |
| the sun's soft shadow with 9 taps instead of 16 (`sunshadow=pipeline`) | 26.9 ms | 0.6 ms | 34 fps |
| the visor HUD's camera and composite (`visor`) | 27.2 ms | 0.3 ms | — |
| relief, plant bodies, grass clumps, the grass carpet | 27.3–27.6 ms | under 0.3 ms each at this spot | — |

The savings overlap (the crowns' share includes their shadows), so they do not add up to the frame.

What the table says:

- **The pixel count is the largest single item.** From the two resolutions, about 20 ms of the 27.5 scale with the
  pixels and about 7 ms do not (an estimate from two points: `27.5 = a + b`, `12.2 = a + b / 4`). The same picture on a
  1080p display would then need about 12 ms, on 2560 × 1440 about 15 ms — an estimate, not a measurement. High is limited
  by the graphics card on this laptop because its panel has 9.2 million pixels, more than four 1080p screens.
- **Shadows are the largest effect**, 8.3 ms. Neither their reach nor their softness is the bulk of it: Medium's reach
  and cascade count save 1.9 ms, the smaller soft-shadow kernel 0.6 ms. The rest is drawing the casters into the shadow
  map and sampling it; the shadow map's size on its own was not swept.
- **Ambient occlusion, volumetric light and the near crowns** cost about 3.5–4 ms each; MSAA about 3 ms; all post
  effects together 2 ms.
- The spot is a wood's edge: crowns in reach, little meadow. The carpet and the clumps therefore read as free here; on a
  meadow they are not, and that picture was not measured.
- The visor's second camera is cheap (0.3 ms). A reading of the code had suggested it runs the full ambient-occlusion
  pass; the measurement does not support that.

The options that follow, each a decision about the look and none built here, are listed on issue #4050.
