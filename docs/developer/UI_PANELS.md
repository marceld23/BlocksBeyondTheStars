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
  so every panel is pad-navigable (#1198). `OnDestroy` destroys the canvas of every panel (a top-level object that
  would otherwise outlive the rig, one more hidden canvas per world join — #2303, #2925).
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
The canvas cleanup on destroy is therefore pinned in PlayMode: `client/Assets/Tests/PlayMode/UiPanelPlayModeTests.cs`
builds the interview's canvas, destroys the panel and checks that the canvas is gone a frame later (#2925).
The rig components that are not panels (the prompts below, `DeathFx`, `TouchControlsUi`, `VendorTradeUI`, `SpaceMap`,
the Tab menu's dialogs) destroy their scene-root canvases in their own `OnDestroy`, after the unsubscribes (#2936):
`client/Assets/Tests/PlayMode/RigCanvasCleanupPlayModeTests.cs` pins it per type, and the Client.Tests source guard
`RigCanvasCleanupGuardTests` fails in PR CI when a rig component keeps a `UiKit.Create…Canvas` canvas without that.
`client/Assets/Tests/EditMode/RigViewsEditModeTests.cs` pins `RigViews`: null while unset, the registered view, and a
true null once that view is destroyed.

## Inventory and follow-up queue

**Migrated (all 13):** shape B — `BeamPadUi`, `BeaconLabelUi`, `BlueprintToolUi`, `StoryReaderUi`, `TeleporterUi`;
shape B2 — `TrainCabUi`, `BioLabUi`, `CrystalDeviceUi`, `InterviewUi`; shape A — `ContainerFilterUi`, `CrateUi` (`Close(bool notifyServer)`), `HotbarActionUi`
and `ContextActionsUi` (their first level is a `UiKit.AddModalDim` ring/list, not an `AddModal` dialog;
`ContextActionsUi` keeps its toggle key out of the opening frame with `Time.frameCount != OpenFrame`). No panel of the
13 has a `static Instance` any more.

**Not panels of this kind** — the rig's other views no longer have a `static Instance` either (#2914): they sit in
`GameBootstrap.Views` (`RigViews.cs`), a plain-C# registry with one typed property per view. `WorldRig.Build` registers
each right after its `AddComponent` (the moment its old `Instance = this` ran in Awake); `VegaPanel` registers itself
in its `Start`, as before. A getter hands back null until the view exists and again once it is destroyed (UnityEngine's
overloaded `!=`), so `Game.Views.Doors?.SetInteractTarget(…)` behaves like the old `DoorView.Instance?.SetInteractTarget(…)`.

| Group | Types (`Game.Views.<entry>`) |
|---|---|
| Prompts / tool hosts | `VendorChoicePrompt` (`VendorChoice`), `LaunchPrompt` (`LaunchPrompt`; both free the cursor through `SetCursorOwner` and have an instance `IsOpen`), `ShapeToolUi` (`ShapeTool`), `PaintToolUi` (`PaintTool`) (no canvas; host an editor). They stay off `UiPanel` by design (#2929): the prompts are cursor-only owners (play keeps running, where `SetMenuOwned` would pause it), ignore keys for a 0.25 s grace after opening, the vendor prompt takes a raw Escape (pad B is crouch while it is up) and both read `UiKit.Tr` with an English fallback; the tool hosts would need a dummy canvas. The prompts' own `OnDestroy` destroys their canvas (#2936) |
| Update-driven HUD-tier panels | `BanditDemandUi`, `NpcDialogUi` (order 24), `MaintenanceUi` (110), `DisconnectScreen` (112) — no registry entry, owner toggled from server state |
| HUD | `VegaPanel` (`Vega`), `HudUi` (`Hud`) |
| Views / FX | `CutsceneDirector` (`Director`; `HoldsScreen` / `ShowsCard` are instance properties), `DoorView` (`Doors`), `ResolutionCinematic` (`Resolution`), `MiningFx` (`Mining`), `BeamView` (`Beams`), `FactoryView` (`Factories`), `MicroFaunaView` (`MicroFauna`), `NpcView` (`Npcs`), `ThermalVision` (`Thermal`), `DataCubeView` (`DataCubes`), `FinaleView` (`Finale`), `NetFragmentView` (`NetFragments`), `OreScanView` (`OreScan`) |
| Static services (stay static by design) | `ClientAudio.Instance` (161 reads — the shell-level music, the settings screen and the static FX helpers have no `Game`), `UrpScenePost.Instance` (33 — the menu shell creates one too), `ScreenLabelLayer.Instance` (lazy getter, 15 reads — its canvas is not parented under the rig), `FxKit.Instance` (the static FX helpers' lazy `Ensure()`); private statics with no outside reader: `VisorHud` (only its own `Kick`), `MinigameCatalog` (the `Load()` cache), `OnScreenKeyboardUi` (`_instance`) |

**Scene searches:** 69 `FindAnyObjectByType` / `FindObjectsByType` calls in 25 files of `Scripts/`, by target:
`PlayerController` 17, `AppShell` 12, `WorldLoadingOverlay` 6, `WeaponFx` 4, `AudioListener` 5 (3 `FindObjectsByType`),
`VegaPanel` 3, `TrainView` 3, `GameMenu` 3, `GameBootstrap` 3, `SpaceView` 2, `EventSystem` 2, and one each for `Text`,
`Light`, `VisorHud`, `Sky`, `ShootingStars`, `RemotePlayers`, `LightShafts`, `AtmosphereProbe`, `WeatherFx3D`. Half of
them (34) sit in shell-level code without a `Game` reference (`ClipDirector` 14, `ScreenshotDirector` 9, `UiSettings` 8,
`PerfProbe` 3). They stay by decision (#2929): 26 of those 34 are in the flag-launched capture/perf tools, `UiSettings`
searches only on a settings click, and the rig-level calls run once, on an event, or cache what they find, while a typed
handle for all of them would mean about 14 new `RigViews` entries or a new static singleton. Swap a search for a handle
only when its file is edited anyway.
