# Creator suite — textures, forms and tool looks players make themselves

Epic #1950. Three menu editors whose results are **the player's at once, on any install** — no developer, no
rebuild, none of the original source assets: the **Texture Editor** (#1955), the **Form Editor** (#1960) and
**My Tools** (#1963). Around them: a texture source with three layers, world textures for multiplayer, forms
that span several blocks, tool models as data, and a way to send a texture to the maintainers.

In the WORLD nothing changed for the player: the shaping tool, its small editor and dyeing work as before.

```
                 Texture Editor ──┬─ "use for me"  ─► texture_overrides/  (local pack, PNG)
                                  ├─ publish (admin) ─► server registry ─► every client of that world
                                  ├─ export ─► texture_exports/<key>/ ─► tools/merge_texture.py ─► the game
                                  └─ submit ─► report inbox (category "texture") ─► pull tool ─► merge_texture.py
                 Form Editor ─────── custom_shapes/*.json  (the library the shaping tool reads)
                 My Tools ────────── settings (ToolLooks) ─► SetToolLookIntent ─► PlayerToolLook
```

## 1. One texture source, three layers (#1952)

`GameTextures` (client) answers "which pixels does key X have right now":

| Layer | Comes from | Wins over |
|---|---|---|
| Official | `Resources/textures/<key>.bytes` (+ `<key>__anim.bytes`) | — |
| Local | the player's pack, `AppPaths.Root/texture_overrides/` (`TexturePackFolder`: PNG or a PNG strip per key, `textures.json` for the speed, `icons/` for item icons) | official |
| World | what an admin of the running world published | local **and** official — *the world wins* (decision 2026-09-20) |

Two per-player switches (`ClientSettings.UseTexturePack`, `ShowWorldTextures`) turn the upper layers off; the
second one is the safety valve against an unsuitable world texture. Every loader goes through
`GameTextures.TileBytes` / `LoadTileTexture`; a change raises `GameTextures.Changed` with the affected keys,
and the **block atlas repaints those slots in place** (`BlockTextureAtlas.OnTexturesChanged`, normals rebuilt,
icon caches dropped) — no chunk has to re-mesh.

**The alpha rule** lives once, in `Shared/Textures/TextureTiles.cs`, and is applied by the loader, the editor's
canvas model, the server and the client's world-texture inbox: a block tile is **opaque** (nobody paints
themselves an X-ray view), plants/leaves/fire/torch are **cutout**, creature/avatar/microfauna tiles are
**free**. Tiles are 64×64 RGBA32, rows bottom-up, up to 8 frames at 2/4/8/12 fps.

**Atlas bands** (`Client.Core/Textures/AtlasBands.cs`): slots 0–399 = blocks by numeric id
(`GameContent.AtlasTileCapacity`), 400–511 extras, 512–989 a dynamic band handed out in row-contiguous runs
by `AtlasSlotAllocator`, 990+ derived tiles.

## 2. Texture Editor (#1955, icon mode #1962)

`TextureEditor` (MonoBehaviour) over `PixelCanvasModel` (Client.Core, unit-tested: tools, frames, snapshot
undo, alpha rule built in). `TextureCatalog` lists what the RUNNING game has — the content's blocks, the
non-block tiles under `Resources/textures`, and the item icons — never a developer folder.
`TexturePreviewRig` shows the result on a cube, on the real shaped mesh (both halves of the bed) or on crossed
cards. Hosts: `AppShell.OpenTextureEditor` (menu) and `GameMenu.OpenTextureEditor` (in a world, with
`ITextureEditorWorldHost` for publish / take back).

**Icon mode:** an item icon opens on the same 64×64 canvas — read back through the GPU
(`Graphics.Blit` → `ReadPixels`), because the build's icons are not CPU-readable and come in every size — with
free alpha and exactly one frame; saved to the pack's `icons/` folder, exported / submitted as kind `icon`.

**Pipeline safety (#1953):** `tools/ai-assets/texture_provenance.json` records who made a tile (`hand:<nick>`);
the generator scripts refuse to overwrite a hand-painted key and need `--only` for bulk modes.
`tools/merge_texture.py` adopts an export bundle by **copying bytes** (no image library, no `out/` folder, no
API key), `tools/merge_material.py` adds a new block material.

**Animated tiles in the world (#1957).** An animated block keeps frame 0 in its own atlas slot (icons, the held
block, far terrain and the UI read that one) and gets all frames as a row-contiguous **strip** in the dynamic
band. Mesh UVs stay on the own slot; faces that show the block's own tile add
`16·frames + 256·speedIndex + 1024·stripStart` to their tint-mode float (`BlockTextureAtlas.AnimationCode`,
every term an exact float, the tint mode stays in the low four bits), and the **vertex stage** of
`BlockAtlas` / `BlockAtlasTransparent` moves the UV onto the strip cell of the current frame. A log's cap,
a furniture part dressed with another block's tile, a painted design and a variant tile do not carry the code.
A tile that becomes (or stops being) animated bumps `AnimationVersion` and the loaded chunks re-mesh once;
a plain repaint never needs a re-mesh. `BlockDefinition.Anim { fps }` is the speed of officially bundled
frames (`<key>__anim.bytes`). The build ships no animated tile yet — that is an asset decision.

**Props (#1956).** Doors, factory machines and the station terminal are primitives with a hard-coded colour.
`PropTextures` gives each part a texture key (`prop_door_{slide,hinge,wood}_{panel,trim}`,
`prop_factory_{metal,dark,accent}`, `prop_decor_housing`) and ONE shared material per key, updated in
place when a layer gets a texture under it. No tile is shipped for these keys — the part keeps its colour and
the editor starts from it. Parts whose colour is animated every frame stay per-object and untextured.

## 3. World textures (#1958 server, #1959 client)

Per-save registry `world_texture(key PK, frames, fps, data, owner, owner_name, created_unix)` in all three
repositories. Payload = raw-deflated frames as base64 (`Shared/Textures/WorldTextureCodec`, hard stop on
inflate). Limits: 256 textures, 320 frame slots in total. Only **admins** publish
(`GameRules.WorldTextures`, world option *World textures: Admins / Off*; off also takes them away from every
client — they stay stored). Wire: extended tags 256–259 (`PublishWorldTextureIntent`,
`RemoveWorldTextureIntent`, `WorldTextureData`, `WorldTextureList`). The join list is **paged** (≤ 200 000
chars per page, one page per tick — a whole list can exceed the 1 MiB message cap and the browser's JSON
envelope has no compression). `/reporttexture <key>` (any player, travels as chat), `/texturewipe <key |
player | all>` (admin).

Client: `WorldTextureInbox` (Client.Core, tested) turns pages into ONE batch (one atlas repaint), lets a
publish/wipe that arrives while the list streams beat the older state in a later page, and checks key,
animation, size and the alpha rule again — a modified server cannot make terrain see-through. The world layer
is cleared with the world (the atlas is shared with the menu).

## 4. Extended message tags (#1951)

The one-byte tag space was full (243 used). Tag **254** now means "a two-byte id follows" (`NetCodec.ExtendedTag`,
ids from 256). An older peer reads tag 254 as an unknown tag, `NetCodec.Decode` returns null and the message
is dropped — exactly what happens to any message a peer does not know — so there is **no protocol bump**: an
old client on a new server simply sees no world textures and no tool looks. The golden list in `NetCodecTests` covers both ranges. 256–259 world
textures, 260–261 tool looks.

## 5. Forms: the menu editor (#1960) and forms over several blocks (#1961)

`FormEditor` over `FormCanvasModel` (Client.Core, tested): the form as one voxel volume, layers, strokes as
single undo steps, mirror / copy below / fill / shift, footprint steppers, coarse/fine grid for one-block
forms, and `Problem()` — the same rules the server applies. It writes `CustomShapeLibrary`
(`AppPaths.Root/custom_shapes/`), the library the shaping tool reads.

**Multi-cell format** (`Shared/World/CustomShape.cs`): `"m1:WHL:"` + one 8³ bitmap per cell, footprint ≤ 3×3×3,
≤ 8 cells, every cell non-empty, box budget (48) **per cell**. It rides the existing `Voxels` string — no new
message, no new column; an older peer sees an unknown form → plain cube. **One registry slot per form.** A
placed block says which cell it is in **descriptor bits 27–30** (`ShapeCode.CellOf/WithCell/WithoutCell`);
everything placed before reads cell 0. `CustomShape.CellOffset(voxels, cell, yaw)` turns cell positions exactly
like the mesher turns geometry inside a cell ((x, z) → (−z, x) per quarter turn) — server placement, sibling
removal, the ghost and the previews all go through it.

Server (`GameServerMultiCellForms.cs`): every footprint cell passes `IsFreePartnerCell` (shared with the bed's
foot half) **before** the item is consumed; the form turns but never tips. `ServerWorld.ShapedBlockReplaced`
(raised from `SetBlock` with what WAS in the cell) lets one subscriber clear the other cells — so the form falls
as one piece for the mining beam, fire, fluids, sand, bombs and stamps alike; repainting or dyeing does not
trigger it; a neighbour's cells are never touched. An N-cell form is made from N blocks and gives them back
("make one" from the crafting menu takes N; a hotbar stack becomes as many forms as it holds; form → form goes
via plain blocks; stencils stay 1:1). Not on ships/stations (a player form becomes a cube there, as before).
Client: mesher + geometry cache keyed by cell, ghost draws every block upright, the icon is the silhouette of
the whole form, the in-world `ShapeEditor` stays one-block and points to the menu editor.

## 6. Tool models as data (#1962) and player tool looks (#1963)

`ItemDefinition.HeldModel` (`data/items.json`, `heldModel`): boxes `{ p:[x,y,z], s:[w,h,d], c:"#rrggbb"|"tint",
g?:true }`, ≤ 32, validated (`HeldModelPart.IsValid`). The eleven tools that had a look of their own in code
carry it as data; `HeldItemShapes` keeps only the model every item of a KIND falls back to. `HeldItem` resolves
through `ModelResolver`, draws glowing parts fully lit and shares one material per (colour, glow).

A **tool look** (`Shared/State/ToolLook.cs`) is an 8×8×16 voxel model from a **fixed** palette of fifteen
colours with a glow mask (`"t1:GGGG:"` + 1024 hex digits), merged per colour into ≤ 32 `HeldModelPart`s. It
follows the body-paint pattern: bound to the **player** and a base item key (never the item instance — some
twenty server rules compare exact item keys), `SetToolLookIntent` / `PlayerToolLook` (tags 260/261), shared
2 s appearance throttle, ≤ 16 per player, persisted in the player snapshot, synced both ways on entering a
world. Client: `ToolLookEditor` over `ToolLookCanvasModel` (tested); looks ride `GameMenu`'s paced appearance
queue after the join; `RemotePlayers` validates arriving looks again.

## 7. Reports and submissions (#1964–#1966)

Every F1 and client crash report carries `reportJson.texturePack` (replaced keys, capped — never pixels;
`GameTextures.ReportInfo` is an immutable snapshot because the crash path runs off the main thread).
"Submit to the developers" and the inbox side are described in [PLAYER_FEEDBACK.md](PLAYER_FEEDBACK.md) and
[REPORT_HOST.md](REPORT_HOST.md). **The consent wording** (`ui.tex.submit.check_1..3`) was approved as it
stands; changing what a sentence MEANS requires bumping `TextureSubmission.ConsentTextVersion`.

**Adopting a submission:** `tools/pull_texture_submissions.py --fetch` → `tools/merge_texture.py <bundle>` →
credit the painter in `NOTICES.md` under the **nickname** they gave (never a real name) → answer the report
with *fixed in version* set (that also exempts it from the twelve-month deletion). School-club textures keep
their own rule: each one is released individually by the maintainer.

## 8. Gamepad

All three editors use `PadCanvasFocus` / `PadCanvasLogic` (#1954): Start toggles canvas mode, the stick walks a
cell cursor with repeat, A/X/Y act, the d-pad's up/down is a gated layer/frame step, RB undoes.
`PadCanvasFocus.OwnsCancel` keeps the B that leaves canvas mode from also closing the editor.

## 9. Testing

Unity-free models carry the rules: `PixelCanvasModelTests`, `FormCanvasModelTests`, `ToolLookCanvasModelTests`,
`WorldTextureInboxTests`, `TextureSubmissionTests`, `PadCanvasLogicTests`, `AtlasSlotAllocatorTests`,
`HeldItemShapesTests` (Client.Tests); `TextureTilesTests`, `WorldTextureTests`, `MultiCellFormTests`,
`ToolLookTests`, `ReportHostTextureSubmissionTests`, `NetCodecTests` (Tests). PR CI never compiles
`client/Assets` — a **local Unity build** is part of every change there.

## 10. Known limits

- Icons are painted at 64×64 (the editor's one canvas size).
- Forms over several blocks: planets and bases only; a blueprint copy that cuts through one leaves partial cells
  (they render, nothing crashes); player forms stay decorative (no sitting, no storage).
- A tool look is announced when a world is entered — looks are edited in the main menu only.
