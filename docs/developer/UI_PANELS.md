# Modal in-world panels: the `UiPanel` base + `ClientContext` (#2913)

The modal panels WorldRig puts next to the player (beam pad, train cab, crate filter, …) all hand-rolled the same
plumbing. `client/Assets/BlocksBeyondTheStars/Scripts/UiPanel.cs` now owns it, and the panels' `static Instance`
singletons are replaced by a per-rig registry, `ClientContext` (`ClientContext.cs`), hung off `GameBootstrap.Context`.

## What `UiPanel` owns

- **`Game`** — a property, not a field: setting it (WorldRig's `x.Game = boot;` right after `AddComponent`) registers
  the panel in `Game.Context`; `OnDestroy` unregisters it. Readers ask `Game?.Context.Panel<BeamPadUi>()?.Open(…)`
  instead of `BeamPadUi.Instance?.Open(…)` and get the panel of *their* rig, or null.
- **The canvas** — `CanvasName` + `CanvasSortingOrder` (abstract), created by `EnsureCanvas()` (once) or
  `CreateCanvasNow()` (per open) — both call `UiKit.CreateCanvas`, set the sort order and `UiNav.Enable` right away,
  so every panel is pad-navigable (#1198). `OnDestroy` destroys the canvas (a top-level object that would otherwise
  outlive the rig, #2303) unless `KeepCanvasOnDestroy` says otherwise.
- **State** — `Shown`, `OpenFrame`, `IsOpen` (virtual; `Shown` by default), `IsBuilt` (the canvas exists).
- **The modal** — `AddModal(x, y, w, h)` (the standard `UiKit.AddModalOverlay`, remembered as `Overlay`),
  `DestroyOverlay()`, `PlayOpenEffect()` (`UiKit.OpenModal` on the overlay).
- **Show/hide** — `ShowCanvas()` (shown in this frame + active), `HideCanvas()`, `DestroyCanvas()`.
- **The arbiter and input** — `SetMenuOwned(bool)` = `Game?.SetMenuOwner(this, …)`, `ConsumeMenuInput()` =
  `Game?.MarkMenuInputHandled()`, `CancelPressed(skipOpenFrame = true)` = the menu verb (`InputAction.UiCancel`:
  Escape or pad B), by default not in the frame the panel opened in.
- **`L(key)`** — `Game?.Localizer?.Get(key) ?? key`.

The base has no Awake, Update or Start: each panel keeps its own Update. Overrides of `OnDestroy` call
`base.OnDestroy()` last.

### Two rules carried over

- **#413 arbiter:** an open panel is a menu owner (`SetMenuOwned(true)` on open, `false` on close). The arbiter frees
  the cursor and pauses control while *any* owner is open and re-locks only when the last one closes — no panel
  writes `Cursor.lockState` itself. A panel that consumes Esc calls `ConsumeMenuInput()` so the shell does not also
  open the quit prompt.
- **#2302 open effect:** `PlayOpenEffect()` only when the panel *opens*, never from a rebuild of an open panel, or
  every click would replay the fade.

## The three shapes

**B — build once, toggle active** (`BeamPadUi`): the static chrome goes into `BuildChrome(Transform root)`, which
`EnsureCanvas()` runs once before hiding the canvas.

```csharp
public void Open(int sourceId) { EnsureCanvas(); _sourceId = sourceId; ShowCanvas(); RebuildList(); SetMenuOwned(true); }
private void Update() { if (!Shown) return; if (CancelPressed()) { ConsumeMenuInput(); Close(); } }
private void Close() { HideCanvas(); SetMenuOwned(false); }
```

**B2 — canvas once, overlay per open** (`TrainCabUi`): an empty canvas; every open (and every refresh) builds a
fresh `AddModal` overlay.

```csharp
public void Open(string id) { if (Shown) Close(); EnsureCanvas(); _id = id; ShowCanvas(); Build(); PlayOpenEffect(); SetMenuOwned(true); }
private void Update() { if (!Shown) return; if (CancelPressed()) { ConsumeMenuInput(); Close(); return; } /* refresh */ }
private void Close() { DestroyOverlay(); HideCanvas(); SetMenuOwned(false); }
private void Rebuild() { DestroyOverlay(); Build(); }   // no PlayOpenEffect here (#2302)
private void Build() { var panel = AddModal(x, y, w, h); /* content */ }
```

**A — build on open, destroy on close** (`ContainerFilterUi`): `public override bool IsOpen => IsBuilt;`.

```csharp
public void Open(…) { if (IsOpen || Game == null) return; /* state */ CreateCanvasNow(); SetMenuOwned(true); Build(); PlayOpenEffect(); }
private void Update() { if (IsOpen && CancelPressed(skipOpenFrame: false)) { ConsumeMenuInput(); Close(); } }
private void Close() { if (IsBuilt) { DestroyCanvas(); SetMenuOwned(false); } /* reset state */ }
private void Rebuild() { foreach (Transform child in Canvas.transform) Destroy(child.gameObject); Build(); }
protected override void OnDestroy() { Close(); base.OnDestroy(); }
```

`CancelPressed()` keeps each panel's old Esc rule: panels that ignored Esc in their opening frame use the default,
the others pass `skipOpenFrame: false` (`BeaconLabelUi` and `BlueprintToolUi` take Esc at once but still keep Enter out
of the opening frame with `Time.frameCount != OpenFrame`).

**B2 variant** (`InterviewUi`): `EnsureCanvas(hidden: false)` — the canvas stays active and its single overlay is built
once in `BuildChrome` (`AddModal`, then `Overlay.SetActive(false)`); Open/Close only toggle the overlay.

## Tests

`UiPanel.CanvasForTest` (read-only) is the seam for EditMode tests: `client/Assets/Tests/EditMode/UiPanelEditModeTests.cs`
opens the three pilots without a `Game`, checks sort order, pad focus and overlay count, pins `ClientContext`'s
register/unregister rules, and removes the canvases with `DestroyImmediate` (Awake/OnDestroy do not run in edit mode).

## Inventory and follow-up queue

**Migrated:** shape B — `BeamPadUi`, `BeaconLabelUi`, `BlueprintToolUi`, `StoryReaderUi`, `TeleporterUi`; shape B2 —
`TrainCabUi`, `BioLabUi`, `CrystalDeviceUi`, `InterviewUi` (keeps its canvas on destroy: `KeepCanvasOnDestroy => true`);
shape A — `ContainerFilterUi`.

**Queued panels** (static `Instance` reads outside the file): shape A — `CrateUi` 1, `HotbarActionUi` 1,
`ContextActionsUi` 1.

**Not panels of this kind** (stay as they are for now):

| Group | Types (static `Instance` reads) |
|---|---|
| Prompts / tool hosts | `VendorChoicePrompt` 3, `LaunchPrompt` 2 (static `IsOpen`, `SetCursorOwner`), `ShapeToolUi` 3, `PaintToolUi` 1 (no canvas; host an editor) |
| Update-driven HUD-tier panels | `BanditDemandUi`, `NpcDialogUi` (order 24), `MaintenanceUi` (110), `DisconnectScreen` (112) — no `Instance`, owner toggled from server state |
| HUD singletons | `VegaPanel` 21, `HudUi` 2, `VisorHud` 0 |
| Views / FX / audio | `ClientAudio` 161, `UrpScenePost` 33, `CutsceneDirector` 18, `DoorView` 10, `ResolutionCinematic` 8, `MiningFx` 6, `BeamView` 4, `FactoryView` 4, `MicroFaunaView` 4, `NpcView` 4, `ThermalVision` 4, `DataCubeView` 2, `FinaleView` 2, `NetFragmentView` 2, `FxKit` 1, `OreScanView` 1, `MinigameCatalog` 0 |
| Other static access | `ScreenLabelLayer.Instance` (lazy getter, 15 reads), `OnScreenKeyboardUi` (private `_instance`) |

**Scene searches:** 68 `FindAnyObjectByType` / `FindObjectsByType` calls in `Scripts/`, by target: `PlayerController`
16, `AppShell` 12, `WorldLoadingOverlay` 6, `WeaponFx` 4, `AudioListener` 5 (3 `FindObjectsByType`), `VegaPanel` 3,
`TrainView` 3, `GameMenu` 3, `GameBootstrap` 3, `SpaceView` 2, `EventSystem` 2, and one each for `Text`, `Light`,
`VisorHud`, `Sky`, `ShootingStars`, `RemotePlayers`, `LightShafts`, `AtmosphereProbe`, `WeatherFx3D`. Half of them
(34) sit in shell-level code without a `Game` reference (`ClipDirector` 14, `ScreenshotDirector` 9, `UiSettings` 8,
`PerfProbe` 3); replacing those needs an AppShell-level handle and is a package of its own.
