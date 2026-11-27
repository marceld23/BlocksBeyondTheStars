# UI layout: the menu template and the HUD zones

**Status:** implemented (package `ui layout 2026-11`, issues #3662–#3673, epic #3674). This doc is the reference for
*where* a window goes; how a screen is built in code (UiKit, uGUI, no prefabs) is in
[CLIENT_SHELL_AND_ASSETS.md](CLIENT_SHELL_AND_ASSETS.md), the console itself in
[CRAFTING_TECH_SHIP_UI.md](CRAFTING_TECH_SHIP_UI.md).

## Why

The UI's look (holo cyan, Rajdhani, glass panels) was consistent; its layout grammar was not. Every screen and every
HUD element computed its own position. The result: the Back button in five places (and with the power icon of
*Quit*), titles in four styles, a playtime tile lying on the planet card, a chat window that jumped sideways whenever
VEGA spoke, VEGA's 640 px speech box at eye level, eight equally weighted main-menu buttons and no *Continue*. The fix
is two rules that every screen follows.

## 1. The menu template (#3662)

Menus live in the 1920 × 1080 reference space (`UiKit.CreateCanvas`). `Scripts/UiKit.Screen.cs` holds the parts:

| Part | API | Rule |
|---|---|---|
| Header | `UiKit.AddScreenHeader(root, title, subtitle, onBack, backLabel)` | Back top left (arrow icon; Esc and pad B do the same through `AppShell`), title + subtitle left-aligned at `UiKit.MenuLeft` (90). Content starts at `UiKit.MenuContentTop` (236), a footer row sits at `UiKit.MenuFooterY` (990). |
| Button tiers | `UiKit.AddTierButton(…, ButtonTier, icon)`, `UiKit.StyleTier` | **Exactly one `Primary` per screen** (the filled "next step"); `Secondary` (outline, opaque fill), `Quiet` (side actions: Back, Settings), `Danger` (destructive, red, set apart). |
| Tertiary | `UiKit.AddLinkButton` | A text link ("Forgot password?", "Read more", "Reset this category"). |
| Content | `UiKit.AddCard`, `UiKit.AddKicker` | Panels that carry text are opaque (`UiKit.CardFill`) — the animated menu background must not shine through. |
| Tabs | `UiKit.AddSegmented` | The selected tab is drawn `Primary`. |
| Controls | `UiKit.AddCycle` (`‹ value ›`), `UiKit.AddSwitch` | A cycle value can no longer be mistaken for a text field. |
| Text helpers | `UiKit.TimeAgo`, `UiKit.Ellipsize` | "5 min ago"; a clamped text ends in "…" instead of stopping mid-sentence. |

**Icons have fixed meanings** (`client/Assets/Resources/icons`): `btn_back` = back · `btn_exit` (power) = quit the
game / leave the world only · `btn_delete` = delete · `btn_new` = create new · `btn_close` = close an in-game screen ·
`btn_pause` = pause menu · `btn_continue` = continue / play · editors (#3688): `btn_save` = save · `btn_load` = load /
open · `btn_undo` / `btn_redo` = undo / redo · `btn_submit` = submit to the developers. Every menu button, tab and settings
category carries an icon. The new icons were generated with `tools/ai-assets/gen_icons.py --only …` and recoloured to the set's cyan.

Screens on the template: main menu (`UiMainMenu`), *My worlds* (`UiSaveSelect` — singleplayer and LAN hosting in one
master-detail screen), *Multiplayer* (`UiMultiplayer`, `ShellPhase.Multiplayer`: official worlds · join a server ·
host), *Settings* (`UiSettings`, category navigation + description column), *Editors* (`UiEditors`, card grid), and
in game the pause menu (`AppShell.BuildQuitDialog`), the Codex (`WikiUI`), the admin menu (`AdminPanelUi`) and the
console (`CraftingTechShipUI`: 8 main tabs with sub-tabs, opaque backdrop).

**My worlds: the "last played" picture.** The selected world's detail pane opens with a 16:9 picture (480 × 270 at the
pane's top left); the facts (mode, last played, playtime) sit beside it, the name, the mode line and Play under it — Play
ends 30 px above the hosting row's divider. A world without a picture shows a planet icon on the card fill. The main
menu's Continue card shows the same picture in its icon's place (149 × 84, the title and line move right of it), and only
when it exists — without one the card and the menu's hierarchy are unchanged. The picture is `preview.jpg` (640 × 360,
JPG quality 80) in the world's save folder, so deleting the world deletes it; it is never in the server's database.
`WorldPreviewFrame` reads it for the selected world only (never one per list row), keeps the last four and frees them
when the screen closes. `WorldPreviewCapture` takes it while the player is in a world the bundled server runs
(singleplayer, or a save opened to friends): a clone of the view camera renders into a render texture — never the
screen, so no HUD, menu, chat or overlay is in it — `GpuReadback` reads it, the JPG is written atomically off the main
thread. When (`Client.Core/WorldPreviewSchedule`): after 30 s of clear view, then every 3 min of it, right before the
pause menu opens (`AppShell.OpenPauseMenu` → `WorldPreviewCapture.PauseMenuOpening`, before the menu and its blur — the
picture of leaving) and when the application quits. Never during a cutscene, the death prompt, the loading veil, a
landing or docking transition or the 10 s after a hyperjump, nor within 5 s after one of those (the world streams in);
never while a panel, the console, the pause menu or the chat is open, nor within 1 s after (the menu blur fades)
(`WorldPreviewRules`). Multiplayer and official worlds get none; nor does the browser's in-tab world (one IndexedDB
blob, no save folder, and the browser menu has neither My worlds nor Continue).

### Gamepad and touch rules of the template

- **First focus** is never Back / Leave / Close: those are moved last in the hierarchy (`back.transform.SetAsLastSibling()`,
  UiNav focuses the first control in hierarchy order), and a screen whose content arrives later picks its real start
  with `UiNav.Prefer(root, control, takeOver)` (My worlds → Play, Multiplayer → the open tab, Settings → the shown
  category, Codex → the open chapter, admin menu → the open tab).
- **Dialogs own the focus.** `UiKit.AddModalDim` (and with it `AddModalOverlay`, `OpenModal`) adds a `UiModalScope`: while
  a scrim with controls is up, only its controls (and anything drawn after it) can be reached, a selection under it is
  pulled back, and closing it selects the opener again (`UiNav.Modal.cs`). The first focus never lands on a `Danger`
  button (`UiKit.IsDangerButton`) — a delete question opens on Cancel. A scrim without controls restricts nothing.
- **B / Esc closes one layer at a time**: a portal dialog (`UiOfficialWorlds.CloseTopDialog`), the layers of *My worlds*
  (`UiSaveSelect.CloseTopLayer`: confirmation → world options → New-world form) and of *Settings*
  (`UiSettings.CloseTopLayer`: reset question → festival list) close before the screen; never while a key is being
  captured for rebinding (`UiSettings.CapturingKey`) or when the Esc only left a text field.
- **A `‹ value ›` cycle and a −/+ stepper are one focus stop**: Left/Right step the value (`StepOnMove`,
  `UiNav.Controls.cs`; `UiKit.AddStepperValue`), the arrows are no stops.
- **Tab bars** (`UiKit.AddSegmented`) step with **LB/RB** where the screen asks for it (`Segmented.StepWithShoulders`,
  the console's `ui.pad.tabs` hint) — Multiplayer does; the console keeps LB/RB for its own main tabs.
- **Touch**: every hit area is at least 44 reference px (`UiKit.MinTapSize`) — a short link gets a taller transparent hit
  box (`AddLinkButton`), a switch a 60 × 44 one (`UiKit.AddHitArea`), and a switch's label toggles it
  (`UiKit.AddLabelHit`).

## 2. The HUD zones (#3668)

The HUD lives in the 1536 × 864 reference space (`UiKit.HudRefW/HudRefH`, top-left origin). `Scripts/HudZones.cs` is
the plan:

```
┌───────────────┬────────────────────────────┬────────────────┐
│ A  status     │ B  toasts (one lane)       │ C  map / radar │
│  location,    │                            │    + clock     │
│  vitals, ship │                            ├────────────────┤
├───────────────┤                            │ D  objectives  │
│               │      centre — FREE         ├────────────────┤
│               │  crosshair, prompts,       │ E  context card│
│               │  world labels              │  (scan, planet,│
├───────────────┤                            │   wreck)       │
│ F  comms      ├────────────────────────────┼────────────────┤
│  chat, VEGA   │ H  hotbar / flight strip   │ G  ≤ 4 keys    │
└───────────────┴────────────────────────────┴────────────────┘
```

- **A** — location and vitals; aboard also hull and shield as rows of the same panel, in flight too (#3670: the
  flight view's own `HULL/SHD` bars in hard-coded English are gone), the cargo line under it.
- **B** — `HudUi` toasts, top centre (they used to sit in the status column).
- **C** — on foot the compass (captions below the dial) and the clock with the playtime right under it; in space the
  radar (`SpaceRadar`, moved here from top centre; rim labels that would overlap stay off).
- **D** — VEGA's objective chip and its "all objectives" rows (`VegaPanel.PlaceObjectives`), 330 wide.
- **E** — the hand scanner's readout, the wreck / ship repair cards, the planet card in space. They stack: each card
  that shows takes the next free place (`HudUi.StackContextCard`), and the planet card starts under the HUD's own cards
  (`HudZones.FreeContextTop`) and never runs past the screen's bottom edge.
- **F** — VEGA's speech panel at the bottom (`VegaPanel.SpeechX/Y/W/H`: 16, 546, 440 × 190 — the column ends left of the
  melody bar), her folded tab on its
  last row (`VegaPanel.ChipY`), the chat lane and the opened chat window above it (`ChatWindowLayout`, #3669 — only
  the gamepad's on-screen keyboard and a touch keyboard centre the window, above the keyboard; the docked window starts
  under the status panel, however tall the exposure meter and effect rows make it).
- **H** — the hotbar and the gear strip; in space the flight data strip above the systems bar.
- **G** — the short controls line (`ClientSettings.ControlsHint`: short = four keys for the situation, full = the old
  one-row list across the bottom, off).

**The right column is a stack.** Its map part writes `HudZones.RightStackBottom` every frame (`HudUi.PlaceRightStack`
on foot and in space), VEGA's chip stacks under it and writes `HudZones.ObjectivesBottom`, and a context card starts at
`HudZones.ContextTop`. An element knows its zone, not fixed pixels — so it cannot slide under a neighbour the way the
planet card slid under the playtime tile. The values are at most one frame old. Every element of the column (the radar
and the planet card included) is placed in the HUD's 1536-wide frame, so a window wider than 16:9 shows one column, not
a staircase.

**On a touch device** the touch layer (`TouchControlsUi`, its own 1920 × 1080 canvas above the HUD) owns parts of the
screen, so the zones make room for it. Every touch control comes from one table (`Client.Core/TouchLayout`);
`TouchControlsUi` publishes the controls of the current context (on foot / flight + EVA / speeder) as
`HudZones.Touch` (a `TouchReservation` in HUD units — 1 touch unit = 0.8 / UI scale HUD units at any aspect; `None`
with mouse, keyboard or pad, so the desktop layout never changes). From it:
- zone C starts under the touch top row (`HudZones.MapShift` / `MapTop`: 118 instead of 10 at 16:9; nothing moves on
  20:9 and 21:9 phones, where the row sits right of the HUD frame);
- the right-column stack stops a thumb's margin above the first action button (`HudZones.StackLimit`; ≈ 560 at 16:9) — a
  card that does not fit moves into a second column left of the first (`Client.Core/ContextCardStack`), a third waits
  until one goes; the planet card is capped and scrolls its content;
- VEGA's speech panel takes the free spot nearest its desktop one, measured every frame against the touch controls,
  the gear strip / melody bar / quick bar, the flight instruments and the chat lane (`Client.Core/TouchCommsLayout`,
  `VegaPanel.Layout.cs`; 464–904 × 472–662 on foot at 16:9), and the touch layer's NEXT button sits right beside it;
- the toast lane narrows past the MAP button, the chat lane ends above RUN and the stick, the opened chat window stays
  under the top row, and the look pad lets taps through to the chat window and to the wreck / repair buttons.

The browser's fullscreen button (bottom right of the page) pulls both short controls lines in
(`TouchHudLayout.BrowserButtonInset`). `TouchHudLayoutTests` (Client.Tests) checks five aspects × three UI scales ×
three contexts for overlaps.

**While the console (Tab) is open** the objective chip and VEGA's folded tab step aside; a line VEGA says meanwhile (a
lesson about that very screen) draws above the opaque console instead of disappearing behind it — and above its Codex
and DataQubes screens, which have their own canvases at order 55 (#4071; under them she shone through as a ghost)
(`VegaPanel.RiseAboveMenu` — only for the console; trade, dialogues and the taming panel keep VEGA under them).

## 3. The editor shell (#3688–#3690, epic #3696)

The creative editors get the menu template as one frame: `Scripts/EditorShell*.cs` (uGUI) over Unity-free parts in
`Client.Core/Editors/` (`EditorShellLayout`, `EditorDraftStore`, `UndoHistory<T>`, `EditorStatusText`). The maintainer's
decisions (2026-11-15) are built in: unsaved work is kept as an automatic **draft** (no "Save changes?" question), the main
action is **Save** everywhere (primary, save icon) with a **subline** saying what it does, the actions sit in a **header
bar at the top**, the editors are **desktop-only** and target **mouse + Full HD** (the school club's PCs; no touch).

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ ‹ Back │ SHIP EDITOR            │ ↶ Undo  ↷ Redo │ Load  New │ Submit │ ■ SAVE           │ header 88
│        │ My cruiser ● Not saved │                │           │        │  For your new worlds │
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│  side card (opaque)  │               work area — the editor's own content          │ side card │ 940
├──────────────────────────────────────────────────────────────────────────────────────────────┤
│ ● Saved.  /  ● Give it a name first.  /  ✕ Could not save.            ≤ 4 keys (hint) │ status 52
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

**Adopting it** (an editor keeps its content, tools and save logic; the shell owns header, status, drafts, dialogs):

```csharp
Ui = UiKit.CreateCanvas("ShipEditorUi");                       // 1920×1080, not UI-scalable (see Sizes)
_shell = EditorShell.Build(Ui, this, new EditorShellOptions     // this : IEditorDocument
{
    EditorKey = "ship", TitleKey = "ui.menu.ship_editor", SaveSublineKey = "ui.editor.save_sub.ship",
    OnBack = _host.CloseShipEditor, OnSave = SaveDesign,        // Func<bool>: true = saved → the draft is removed
    OnLoad = OpenLoadPicker, OnNew = ClearBuild, OnSubmit = OpenSubmit, HasUndo = true,
});
var palette = _shell.AddSideCard(right: false, 300f);           // content goes under _shell.WorkArea
_shell.PreferFocus(firstTool);                                  // the pad's first focus (never Back)
_shell.OfferDraft();                                            // last: "Continue your draft?" when one is left
// on every edit: _shell.NotifyChanged();   messages: _shell.SetStatus(text, EditorStatusSeverity.Error)
```

**The contract** (`IEditorDocument`): `DocumentName`, `IsDirty`, `CanUndo` / `CanRedo` / `Undo()` / `Redo()`,
`SaveDraft()` (the open work as text — the editor's export JSON is fine; null/empty = nothing worth keeping) and
`LoadDraft(string)` (false = unreadable; leaves the document dirty), and `CloseTopLayer()` — closes the editor's topmost
picker / dialog / panel / pad canvas mode and returns true, false when nothing of its own is open. Optional header buttons
exist only when their action is given (`OnLoad`, `OnNew`, `OnSubmit`, `HasUndo`); `SetSubmitAvailable(false)` hides Submit
where the work cannot be submitted, `SetTitle` / `SetSaveSubline` follow an editor's mode.

**Drafts** (#3689, `EditorDraftStore`): one draft per editor key in `AppPaths.Root/drafts/<key>.draft`, never inside
the user content. While the work is dirty, a draft is written after it rested 2 s (at the latest 15 s into an unbroken
session; an editor that never calls `NotifyChanged` is re-checked every 15 s while dirty), written atomically (temp file + replace),
skipped when unchanged, capped at 8 MB. Back writes it once more and leaves — it asks ("Leave anyway?") only when the draft
could not be written. Save removes it; work undone back to its saved state removes it too. On opening, `OfferDraft` asks
**Continue your draft** (primary, the pad's first focus, also what Esc / B choose) or **Start fresh** (the file stays until
new work replaces it — one mis-click destroys nothing). A broken draft is renamed `<key>.draft.broken` and never blocks the
editor. In the browser `DesktopFeatures` is false: the in-game editors hide "open folder", file exports and clipboard paste.

**Undo** (`UndoHistory<T>`): one snapshot history for every editor — what `PixelCanvasModel`, `FormCanvasModel` and
`ToolLookCanvasModel` each wrote for themselves (48 steps, a stroke = one step via `Begin` / `Commit`, a no-op is no step,
a new step forgets the redo tail) plus `PixelEditHistory`'s colour-drag merging (`Merge(before, after, key)` /
`EndMerge`). `T` is any immutable snapshot (pass `UndoSnapshots.Bytes` for byte arrays; the ship and structure editors can
snapshot the same text they write as a draft). `IsDirty` / `MarkSaved` give the right "unsaved" answer across undo. The
shell's Undo / Redo buttons grey out with `CanUndo` / `CanRedo`; Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z and Ctrl+S work unless a
text field has the keyboard (`EditorShellOptions.Shortcuts`; an adopting editor drops its own Ctrl+Z handler).

**Esc / pad B** (#3690): the shell reads no cancel key itself. The ONE reader (`AppShell` for the menu editors, `GameMenu`
in a world) calls `EditorShell.Current.HandleCancel(fieldTypingRecent)` instead of closing the editor. Order: a text field
just took the key → nothing; the submit dialog / pad canvas mode own it (`EditorShell.CancelOwnedElsewhere`) → nothing;
the shell's own dialog → closed (Cancel; Continue on the draft question); the editor's `CloseTopLayer()`; otherwise the
editor stays, the status bar says "use Back", and a pad's focus moves to Back. **An editor closes only through Back.**

**Confirmations**: `UiKit.Confirm(root, ConfirmSpec)` / `UiKit.ConfirmDanger` (any screen) and on the shell `Confirm`,
`ConfirmDelete(name, …)` ("Delete “…” for good?") and `ConfirmDiscard(proceed)` (asks only over unsaved work — Load, New,
Generate, Assemble). Danger button first, Cancel (`btn_close`) second: the pad opens on Cancel, Esc / B cancel; the dialog is
the canvas's last child, so it draws and takes clicks above every widget.

**Sizes**: header buttons 48 high (Back 170, Undo / Redo 170, Load 160, New 150, Submit 200, Save 230; `EditorShellLayout`),
every control ≥ 44 px, every text ≥ 16 px at the 1920 × 1080 reference (button labels are re-fitted to 16–22 on one line).
Editor canvases do **not** follow the UI-scale setting: their content is laid out in absolute 1920 coordinates like every
menu, and a smaller reference would push Save off the screen. At the school club's Full HD 1 reference px = 1 screen px.

**Status bar**: `SetStatus(text, Info | Success | Warning | Error)` — light, green (only success), amber, red with a cross
(an error reads as one without colour too); file paths are cut to their last part (`EditorStatusText.WithoutPaths`), line
breaks become spaces, a message stays until the next. `SetHint` fills the right side (at most four keys).

**Icons and words**: `btn_save` (floppy), `btn_load` (folder), `btn_undo` / `btn_redo` (mirrored pair), `btn_submit` (paper
plane), plus `btn_back` and `btn_new` — generated with `tools/ai-assets/gen_icons.py` and recoloured to the set's cyan.
Keys: `ui.menu.back`, `ui.editor.save` / `load` / `new` / `undo` / `redo`, `ui.submit.button`, `ui.action.cancel`,
`ui.editor.save_sub.<editor>` (the Save subline), `ui.editor.draft.*`, `ui.editor.confirm_*`, `ui.editor.saved` /
`save_failed` / `unsaved` / `leave_hint`.

## Tests that pin it

- `tests/BlocksBeyondTheStars.Client.Tests/ChatWindowLayoutTests.cs` — the chat window's dock and the keyboard cases.
- `tests/BlocksBeyondTheStars.Client.Tests/EditorShellLayoutTests.cs`, `EditorDraftStoreTests.cs`, `UndoHistoryTests.cs` — the
  editor shell's geometry and path-free status text, the drafts, the shared undo history.
- EditMode `EditorShellEditModeTests` — the shell built on a test canvas: header order, one primary Save, optional buttons,
  sizes, pad first focus, status colours, the draft question, the Esc order and the confirmations.
- EditMode: `ChatBigWindowEditModeTests`, `ChatLaneEditModeTests` (the comms zone constants match `VegaPanel` and
  `HudZones`), `HudRightColumnEditModeTests`, `MainMenuHierarchyEditModeTests` (the main menu's hierarchy golden).
- `WorldPreviewFilesTests`, `WorldPreviewScheduleTests` (Client.Tests) — the picture's path, atomic write and the "good
  moment" rules and timer; EditMode `WorldPreviewEditModeTests` — My worlds shows the selected world's picture or the
  placeholder, the Continue card shows it only when it exists (both on a temp saves folder,
  `LocalServerLauncher.SavesRootOverrideForTest`).
