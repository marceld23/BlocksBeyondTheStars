// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-game HUD in the modern uGUI design (replaces the legacy IMGUI HUD): vitals + ship
    /// hull/shield bars, a hotbar with atlas/item icons, a round ship compass (with a waypoint pointer),
    /// the day/night indicator, scan/wreck/loot panels, toasts and prompts — all on a DPI-independent
    /// canvas (UiKit). Built once, refreshed each frame from the authoritative <see cref="GameBootstrap"/>.
    /// Hidden while a menu is open.
    /// Look pass (2026-09): SDF text (<see cref="UiText"/>), shader-drawn holo chrome (<see cref="UiHolo"/>),
    /// vitals icons, a motion layer (<see cref="UiTween"/>: eased bars with a ghost trail, rolling numbers,
    /// toast slide-in, a boot-up reveal on world entry / respawn, a visor glitch on damage) and nested
    /// sub-canvases so the per-frame movers (compass, vitals, hotbar, crosshair) no longer re-batch the
    /// static chrome every frame.
    /// </summary>
    public sealed partial class HudUi : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Local settings (wired by <see cref="WorldRig"/>) — read live for the optional playtime readout.</summary>
        public ClientSettings Settings;

        private const float W = UiKit.HudRefW, H = UiKit.HudRefH; // smaller reference → a ~1.25× bigger HUD

        /// <summary>Reach at which a teleporter pad answers E (mirrors PlayerController's beam press, #1560).</summary>
        private const float BeamUseRange = 2.2f;

        private Canvas _canvas;
        private GameObject _locationPanel;
        private TMP_Text _locTitle, _locPlace, _inSpace, _prompt, _loot, _hint;
        private TMP_Text _observer; // SPECTATOR badge while fleet-admin observer mode is active (issue #487)

        // Perf: the text-heavy HUD refresh runs at ~10 Hz, not every frame — rebuilding dozens of strings
        // per frame is pure GC churn and the readouts (vitals, clock, prompts) don't change faster than
        // that anyway. Motion-coupled elements (compass blips) still update per frame, and a hotbar
        // selection change forces an immediate refresh so input feedback never lags.
        private const float RefreshInterval = 0.1f;
        private float _refreshTimer;

        // Motion layer state.
        private CanvasGroup _rootGroup;   // whole-HUD fade for the boot-up
        private bool _bootPending = true; // play the boot reveal on the first visible frame (and after a respawn)
        private bool _wasDead;
        private WorldLoadingOverlay _veil;

        private void LateUpdate()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            EnsureBuilt();
            UpdateDamageFeedback(); // always, so the health baseline tracks even while a menu is open

            // Hidden while a menu is open — and during every cutscene (#760, #2461: the ending used to play over
            // the full HUD): vitals/hotbar/crosshair in the frame break the letterboxed shot. The VEGA speech panel
            // and the CinematicFrame chrome live on their own canvases, so the story text stays visible. #2496: also under a
            // scene that hides only the HUD (a T3 overlay's hideHud — GameBootstrap.HudHidden). #2530: the taming panel's own
            // (HUD-level) menu ownership does not count — the panel lives on this canvas.
            bool show = !Game.ScreenMenuOpen && !Game.VegaPrologueActive && !Game.CinematicCameraActive && !GameBootstrap.HudHidden;
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
            }

            UpdateTaming(show); // per frame: the cursor ownership + the [1]–[4] / Esc keys must not wait for the throttled Refresh

            if (!show && _gearCanvas != null && _gearCanvas.enabled)
            {
                _gearCanvas.enabled = false; // #2290: the gear strip goes with the HUD (Refresh brings it back)
            }

            // Even while a menu hides the canvas: rows must keep aging (a closed menu must not resurrect
            // stale pickups) and gains queued during the hidden-hotbar states must keep draining away.
            UpdatePickupFeed(Time.deltaTime);
            UpdateResearchToast(Time.deltaTime);
            UpdateToast(Time.deltaTime); // #1860: the status toast ages out even while a menu hides the canvas

            // While the binocular optic is raised its own reticle takes over — two crosshairs stacked on top of
            // each other read as a rendering bug (BinocularOptic owns the flag and always clears it).
            if (_crosshair != null && _crosshair.activeSelf == SuppressCrosshair)
            {
                _crosshair.SetActive(!SuppressCrosshair);
            }

            if (show)
            {
                if (_bootPending && (_veil == null || !_veil.VeilActive))
                {
                    _bootPending = false;
                    PlayBoot();
                }

                UpdateCrosshairState(Time.deltaTime); // per frame: aim tint must not lag the reticle
                RefreshCompass(); // per frame: blips counter-rotate with the camera, throttling would judder
                AnimateVitals(Time.deltaTime); // per frame: eased fills, ghost trail, rolling numbers
                UpdateLowVitalWarnings(Time.deltaTime); // per frame: the below-10 % blink (#753) must not step

                _refreshTimer -= Time.deltaTime;
                bool force = Game.SelectedHotbarSlot != _lastSelSlot;
                if (force || _refreshTimer <= 0f)
                {
                    _refreshTimer = RefreshInterval;
                    Refresh();
                }
            }
        }

        /// <summary>The founded base whose zone contains the player right now, or null. Mirrors the server's
        /// authoritative life-support check (same <see cref="WorldConstants.BaseZoneRadius"/> cube around the
        /// base_core) against the already-streamed <see cref="GameBootstrap.Bases"/> — HUD feedback only.</summary>
        private NetBase FindBaseZone()
        {
            var bases = Game.Bases;
            if (bases == null || bases.Length == 0)
            {
                return null;
            }

            int px = Mathf.FloorToInt(Game.PlayerPosition.x);
            int py = Mathf.FloorToInt(Game.PlayerPosition.y);
            int pz = Mathf.FloorToInt(Game.PlayerPosition.z);
            foreach (var b in bases)
            {
                // NetBase carries the core's block centre (X/Z at +0.5), canonical — floored back to the cell and measured
                // the short way round both seams (#2613: a base beside the home pad at X = 0 straddles the seam).
                if (InteractReach.InBaseZone(px, py, pz, b.X, b.Y, b.Z, Game.Circumference))
                {
                    return b;
                }
            }

            return null;
        }

        private void OnDestroy()
        {
            // The canvas is a top-level object (not under the game root), so destroy it explicitly
            // when the HUD is torn down — otherwise the last HUD lingers on the main menu.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }

            if (_flyCanvas != null)
            {
                Destroy(_flyCanvas.gameObject);
            }

            if (_gearCanvas != null)
            {
                Destroy(_gearCanvas.gameObject); // #2290: a scene-root canvas too
            }

            if (Game != null)
            {
                Game.SetHudMenuOwner(_tameMenuOwner, false); // #2530: a torn-down HUD never strands a free cursor
            }

            UiTween.Kill(_rootGroup);
            UiTween.Kill(_toastGroup);
        }

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateDiegeticCanvas("HudUI", W, H); // routed through the visor HUD camera when active
            _canvas.sortingOrder = 10;
            var root = _canvas.transform;
            _rootGroup = _canvas.gameObject.AddComponent<CanvasGroup>();
            _rootGroup.blocksRaycasts = true;
            _veil = FindAnyObjectByType<WorldLoadingOverlay>();

            BuildDamage(root);
            BuildCrosshair(root);

            // Location (top-left).
            _locationPanel = Panel(root, 10, 10, 280, 46).gameObject;
            _locTitle = UiText.Add(_locationPanel.transform, 10, 3, 260, 18, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _locPlace = UiText.Add(_locationPanel.transform, 10, 22, 260, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);

            BuildVitals(root);
            BuildHotbar(root);
            var compassGo = BuildCompass(root);
            BuildTimeOfDay(root);

            // Toast / indicators / prompts / hint.
            BuildToast(root);
            _inSpace = UiText.Add(root, W / 2f - 100, 8, 200, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Observer badge (issue #487). Top centre, always-on while the mode is active: an admin who forgets
            // they are invisible is how you end up "fixing" a world nobody can see you in.
            _observer = UiText.Add(root, W / 2f - 160, 30, 320, 24, string.Empty, 18, UiKit.Warn,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt = UiText.Add(root, W / 2f - 400, H / 2f + 24, 800, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _loot = UiText.Add(root, W / 2f - 160, H / 2f + 48, 320, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _hint = UiText.Add(root, (W - 1400) / 2f, H - 26, 1400, 20, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleCenter);

            BuildSpeeder(root);
            BuildScan(root);
            BuildWreck(root);
            BuildShipRepair(root);
            BuildTaming(root);

            // Typography: headline labels glow like the hologram, the rest keep the readable underlay.
            foreach (var glow in new TMP_Text[] { _locTitle, _inSpace, _prompt, _scanSubject, _tameName, _speederTitle, _shipRepairTitle, _observer })
            {
                UiText.Style(glow, UiText.Look.Glow);
            }

            foreach (var outline in new TMP_Text[] { _locPlace, _loot, _hint, _todText, _compassDist, _compassWpDist, _compassCoreDist, _compassFragDist, _compassStoryDist, _dmgCause, _playtimeText,
                         _wreckName, _wreckProg, _wreckHint, _shipRepairProg, _shipRepairHint, _tameMood, _tameNeed, _tameTrust,
                         _speederSpeed, _speederHullLabel, _speederFuelLabel, _speederHint })
            {
                UiText.Style(outline, UiText.Look.Outline);
            }

            // Per-frame movers get their own nested canvases, so their vertex updates re-batch a handful of
            // graphics instead of the whole HUD (perf analysis 2026-09-03, item 24).
            foreach (var mover in new[] { _vitalsPanel, compassGo, _hotbarRoot, _crosshair })
            {
                UiKit.AddSubCanvas(mover); // copies the parent's shader channels — a bare AddComponent<Canvas> drops UV1–UV3
            }

            CreateGearCanvas();
        }

        /// <summary>Boot-up: the whole HUD fades in while every holo panel wipes on with a short stagger —
        /// the suit's systems coming online. Instant under reduced motion.</summary>
        private void PlayBoot()
        {
            if (_canvas == null)
            {
                return;
            }

            if (_rootGroup != null)
            {
                if (!UiKit.ReducedMotion)
                {
                    _rootGroup.alpha = 0f;
                }

                UiTween.Alpha(_rootGroup, 1f, 0.45f, UiTween.Ease.OutQuad);
            }

            UiHolo.PlayReveal(_canvas.transform, 0.36f, 0.05f, 0.08f);
        }

        private void Refresh()
        {
            var loc = Game.Localizer;

            // Location — show the boarded station's name when on one, else the planet/system.
            string place;
            if (!string.IsNullOrEmpty(Game.StationName))
            {
                place = $"{Game.StationName}  ({loc.Get("ui.hud.station")})";
                if (Game.StationZeroG)
                {
                    place += $"  ·  {loc.Get("ui.hud.station_zero_g")}"; // #1842: the chosen float, not a drift
                }
            }
            else
            {
                place = string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.ShownLocationName;
                if (Game.ShownLocationOriginal.Length > 0)
                {
                    place += $" <size=80%><alpha=#99>({Game.ShownLocationOriginal})</alpha></size>"; // #2952: the real name, small
                }

                if (Game.Aboard) place += $"  ({loc.Get("ui.hud.aboard")})";
                else if (Game.OnFootAbovePlanet) place += $"  ·  {loc.Get("ui.hud.station_zero_g")}"; // #2276: floating above the planet's air
            }
            _locTitle.text = loc.Get("ui.hud.location").ToUpperInvariant();
            _locPlace.text = place;

            RefreshVitals(loc);

            RefreshHotbar(loc);
            RefreshGearStrip(loc); // #2290: after the hotbar — it hides with it
            RefreshTimeOfDay(loc);
            RefreshPlaytime(loc);

            string toastMsg = Game.LastMessage ?? string.Empty;
            int toastSeq = Game.LastMessageSeq;
            // #1860: the sequence tells a RE-SENT identical line from the one already shown — after the previous
            // toast expired and blanked the message, the same warning must slide in again.
            if (toastSeq != _lastToastSeq || !string.Equals(toastMsg, _lastToastMsg, System.StringComparison.Ordinal))
            {
                _lastToastSeq = toastSeq;
                _lastToastMsg = toastMsg;
                ShowToast(toastMsg);
            }
            _inSpace.text = Game.InSpace ? loc.Get("ui.hud.in_space") : string.Empty;
            _observer.text = Game.Spectating ? loc.Get("ui.hud.observer") : string.Empty;
            // #1516: compose the hint in a local and assign ONCE — assigning the base line first and then
            // appending with `+=` dirtied the Text (mesh regeneration) on every refresh even when the final
            // string was identical; the setter only early-outs on an equal string.
            // #2247: the space view prints its own controls line in the same spot (flight or EVA) — the on-foot one
            // ran underneath it.
            string hint = Game.SpaceViewActive ? string.Empty : InputMap.ActiveDevice switch
            {
                // On touch the on-screen buttons are self-labelling, so the text hint just adds clutter.
                InputDeviceKind.Touch => string.Empty,
                InputDeviceKind.Gamepad => PadHint(loc), // #2461 (N9): glyph set, bindings and triggers, not a fixed string
                _ => KeyboardHint(loc), // #2502: the bound keys ({key:Action} tokens), not a fixed letter list
            };

            // Creative/Sandbox worlds only: the one control the hint can't afford to omit, because flight has
            // no other tell. Appended rather than baked into ui.hud.hint so Explorer worlds stay uncluttered.
            // #2502: "{key:Jump}×2" — the pad line used to append the keyboard's "Space×2".
            if (Game.CanFly && hint.Length > 0)
            {
                hint += " · " + ExpandKeyTokens(loc, loc.Get("ui.hud.hint_fly"));
            }

            // Holding a rotatable block (a crafted shape or furniture): surface the rotate control (#863) —
            // nothing else in the game ever said the key exists. Appended only while it applies, like the
            // fly hint, so the always-on line stays short.
            if (Game.HoldingRotatableBlock && hint.Length > 0)
            {
                hint += " · " + loc.Get("ui.hud.hint_rotate")
                    .Replace("{key}", GlyphText(loc, InputAction.RotateShape));
            }

            // The middle-click slot-action ring (#924) has no other on-screen tell (#935): advertise it
            // while the hotbar is up and the selected slot holds anything to act on, like the hints above.
            // RefreshHotbar ran just before this, so the root's active state is current for this frame.
            if (hint.Length > 0 && _hotbarRoot != null && _hotbarRoot.activeSelf
                && !string.IsNullOrEmpty(Game.ItemInSlot(Game.SelectedHotbarSlot)))
            {
                hint += " · " + loc.Get("ui.hud.hint_slot_actions")
                    .Replace("{key}", GlyphText(loc, InputAction.HotbarAction));
            }

            _hint.text = hint;

            // Prompts — on-foot only. While piloting/EVA the flight view draws its own prompts, so don't leak
            // a stale on-foot "Use: Cockpit" into the centre of the space view (you reach the cockpit/helm on
            // foot inside the ship; from the flight view you press F to step inside).
            string prompt = string.Empty;
            if (!Game.SpaceViewActive)
            {
                if (Game.SettlingHint)
                {
                    // The spawn/respawn settle freeze is holding the body (#1462) — say so instead of leaving
                    // a fully drawn ship that ignores WASD.
                    prompt = loc.Get("ui.hud.settling");
                }
                else if (RestingInBed())
                {
                    // #2823: lying in a bed — how to get up, in the glyphs of the device in hand (the stations and the
                    // aimed blocks around the bed are not what the keys do now).
                    prompt = ExpandKeyTokens(loc, loc.Get("ui.rest.get_up"));
                }
                else if (!string.IsNullOrEmpty(Game.NearbyStation))
                {
                    // Inside the ship while it floats in space, the cockpit reads as the helm (take it to fly again).
                    string stationKey = (Game.NearbyStation == "cockpit" && Game.LoadingPlanetType == "ship_interior")
                        ? "ui.station.helm"
                        : $"ui.station.{Game.NearbyStation}";
                    prompt = $"{ExpandKeyTokens(loc, loc.Get("ui.hud.use"))}: {loc.Get(stationKey)}"; // "Press {key:Interact}" (#2463)
                    if (Game.NearbyStation == "cargo")
                    {
                        // #2502: the glyph names the pad's Actions list / the tablet's ACT button, not the key letter.
                        prompt += $"  ·  {loc.Get("ui.hud.stow_cargo")} {InParentheses(PromptGlyph(loc, InputAction.DepositToCrate))}";
                    }
                    if ((Game.NearbyStation == "cockpit" || Game.NearbyStation == "console")
                        && Game.LoadingPlanetType != "ship_interior" && OwnParkedVehicleCount() > 0)
                    {
                        // The ship can call a vehicle left out on this world back beside it (#1661).
                        prompt += $"  ·  {loc.Get("ui.key.recall_vehicle")} {InParentheses(PromptGlyph(loc, InputAction.RecallVehicle))}";
                    }
                    if ((Game.NearbyStation == "cockpit" || Game.NearbyStation == "console") && Game.ShipRepair != null
                        && InputMap.ActiveDevice != InputDeviceKind.Touch)
                    {
                        // Something to repair and this is the terminal for it: name the trigger right here (the static
                        // cockpit label no longer carries a hard-coded "(R)"; a pad reaches it through the Actions list).
                        string g = InputMap.ActiveDevice == InputDeviceKind.Gamepad
                            ? InputMap.Glyph(InputAction.ContextActions)
                            : InputMap.Glyph(InputAction.RepairWreck);
                        prompt += $"  ·  {loc.Get("ui.shiprepair.repair")} ({g})";
                    }
                }
                else if (Game.AimedOwnBase is { } ownBase)
                {
                    prompt = BaseAirPrompt(loc, ownBase);
                }
                else if (string.IsNullOrEmpty(Game.InSpeeder) && OwnParkedVehicleInBoardReach())
                {
                    // Beside an own parked speeder/boat (#1670): nothing on screen ever said how to board —
                    // "Board (E) · Pack up (X)", the same verbs PlayerController polls on foot. Glyphs follow
                    // the active device, and the pad/touch ACT list carries the pack-up too.
                    prompt = $"{loc.Get("hud.speeder.board")} {InParentheses(PromptGlyph(loc, InputAction.Interact))}"
                        + $"  ·  {loc.Get("hud.speeder.stow")} {InParentheses(PromptGlyph(loc, InputAction.StowVehicle))}";
                }
                else if (!string.IsNullOrEmpty(Game.AimedStationBlock))
                {
                    // #1073: "Workbench — crafting: menu (Tab) → Crafting" — the block names the tab it powers; the menu
                    // and use keys are {key:Action} tokens since #2463, so a pad or a tablet reads its own button.
                    // #2203: the bio lab has a panel of its own, so its prompt names the Interact glyph instead.
                    prompt = Game.AimedStationBlock == BlocksBeyondTheStars.Shared.Bio.BioItems.Lab
                        ? string.Format(loc.Get("ui.bio.prompt"), InteractGlyph(loc))
                        : ExpandKeyTokens(loc, loc.Get(StationBlockPromptKey(Game.AimedStationBlock)));
                }
                else if (Game.AimedCrystalDevice is { } crystalDev)
                {
                    // #2049: a Crystal Net device in the crosshair says what Interact does to it — the glyph follows
                    // the device in hand, and a tablet reads its USE button instead of a key name.
                    // #2256: someone else's device says who may use it instead of offering a key that would be refused.
                    prompt = string.Format(loc.Get(Game.CanOperateCrystal(crystalDev) ? CrystalPromptKey(crystalDev) : "ui.crystal.prompt.locked"), InteractGlyph(loc));
                }
                else if (Game.Views.Beams != null && Game.Views.Beams.NearestUsableBeam(Game.PlayerPosition, BeamUseRange) != 0)
                {
                    // Standing on / beside an own or allied teleporter pad: the same reach PlayerController
                    // uses for the E press. The pad never announced itself before (#1560).
                    prompt = ExpandKeyTokens(loc, loc.Get("ui.beam.use_hint")); // "Press {key:Interact}: Transporter" (#2463)
                }
                else if (HoldingScanner() && Game.ScanTargetInView)
                {
                    // Only when the scanner would actually read something — an unconditional hint over a
                    // target the scan can't acquire read as "titans can't be scanned" (#1458).
                    prompt = ExpandKeyTokens(loc, loc.Get("ui.scan.use_hint")); // "{key:Scan}: scan" (#2502)
                }
                else if (string.IsNullOrEmpty(Game.InSpeeder) && NearestTalkableNpc() is { } npcName)
                {
                    // #1869: E has talked to people since #1127, but nothing on screen ever said so. Same reach and
                    // the same "no station block in reach" rule as PlayerController's E.
                    string talkGlyph = PromptGlyph(loc, InputAction.Interact); // #2502: USE on a tablet, not "E"
                    prompt = BlocksBeyondTheStars.Shared.Localization.LineText.CollapseGlyphParentheses(
                        string.Format(loc.Get("ui.hud.talk"), npcName, talkGlyph), talkGlyph);
                }
                else if (string.IsNullOrEmpty(Game.InSpeeder) && OwnParkedVehicleOutOfReach(out string vehicleKind, out float vehicleDist))
                {
                    // An own vehicle a few metres off but beyond the pack-up reach (#1661): the X action used to
                    // stay silent exactly when the speeder had sunk or wedged just out of reach.
                    string stowGlyph = PromptGlyph(loc, InputAction.StowVehicle); // #2502: the Actions list / ACT, not "X"
                    prompt = BlocksBeyondTheStars.Shared.Localization.LineText.CollapseGlyphParentheses(
                        string.Format(loc.Get("hud.speeder.out_of_reach"),
                            loc.Get(vehicleKind == "boat" ? "item.boat.name" : "item.speeder.name"),
                            Mathf.RoundToInt(vehicleDist), stowGlyph),
                        stowGlyph);
                }
            }

            _prompt.text = prompt;
            // Only actionable on foot (#751): PlayerController never polls the loot key while the
            // flight view is up or a speeder is driven, so showing the prompt there was a dead key.
            _loot.text = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.InSpeeder) ? string.Empty : FeedOrLootText(loc);

            RefreshScan(loc);
            RefreshWreck(loc);
            RefreshShipRepair(loc);
            RefreshTaming(loc);
            RefreshSpeeder(loc);
        }

        /// <summary>#2823: the player lies in a bed (the rig's <see cref="PlayerController.Resting"/>).</summary>
        private bool RestingInBed()
        {
            if (_playerRig == null)
            {
                _playerRig = FindAnyObjectByType<PlayerController>(); // the rig WorldRig creates; null in the menu
            }

            return _playerRig != null && _playerRig.Resting;
        }

        /// <summary>The prompt key for an aimed station block (#1073). #2823: a bed or a crew bunk lays you down now — their
        /// prompts say so under keys of their own, so the translation pass picks the new wording up (the crew bunk had no
        /// prompt key at all and read as the raw key).</summary>
        private static string StationBlockPromptKey(string block) => block switch
        {
            "bed" => "ui.station.block.bed_rest",
            "crew_bunk" => "ui.station.block.crew_bunk",
            _ => "ui.station.block." + block,
        };

        /// <summary>The name of the NPC E would talk to right now (#1869), or null. #2613: the very picker PlayerController's
        /// E asks (<see cref="GameBootstrap.NearestTalkableNpc"/>, seam-aware) — the prompt used to measure on its own,
        /// straight across the seam, and stayed blank beside a settler on the far side of X = 0.</summary>
        private string NearestTalkableNpc()
        {
            var npc = Game.NearestTalkableNpc();
            if (npc == null)
            {
                return null;
            }

            return string.IsNullOrEmpty(npc.Name) ? Game.Localizer?.Get(npc.NameKey) ?? string.Empty : npc.Name;
        }

        /// <summary>Own vehicles parked (not driven) on this world — the cockpit recall prompt's gate (#1661).</summary>
        private int OwnParkedVehicleCount()
        {
            if (Game?.Speeders == null)
            {
                return 0;
            }

            int n = 0;
            foreach (var s in Game.Speeders)
            {
                if (s != null && s.OwnerId == Game.LocalPlayerId && string.IsNullOrEmpty(s.DriverId))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>An own parked (not driven) vehicle within the on-foot board reach — the "Board (E)" prompt's
        /// gate (#1670). The reach is the rig's own <see cref="PlayerController.SpeederBoardRange"/>, the value its E boards
        /// with — a copied 3.2 here could drift from it (#2926). No rig (the menu) means nothing on foot to board.</summary>
        private bool OwnParkedVehicleInBoardReach()
        {
            if (Game?.Speeders == null)
            {
                return false;
            }

            if (_playerRig == null)
            {
                _playerRig = FindAnyObjectByType<PlayerController>(); // the rig WorldRig creates; null in the menu
            }

            if (_playerRig == null)
            {
                return false;
            }

            float boardReach = _playerRig.SpeederBoardRange;
            foreach (var s in Game.Speeders)
            {
                if (s != null && s.OwnerId == Game.LocalPlayerId && string.IsNullOrEmpty(s.DriverId)
                    && (Game.ScenePos(s.X, s.Y, s.Z) - Game.PlayerPosition).sqrMagnitude < boardReach * boardReach)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The nearest own parked vehicle that is beyond the pack-up reach (5 m, the server's) but
        /// within a few metres — its kind and distance for the "walk up to it" hint (#1661). The range covers
        /// the recall's park-beside spots (up to 18 blocks from the pad centre, #1668), which the old 14 m
        /// missed from the cockpit.</summary>
        private bool OwnParkedVehicleOutOfReach(out string kind, out float distance)
        {
            const float reach = 5f, hintRange = 30f;
            kind = string.Empty;
            distance = 0f;
            if (Game?.Speeders == null)
            {
                return false;
            }

            float best = hintRange;
            foreach (var s in Game.Speeders)
            {
                if (s == null || s.OwnerId != Game.LocalPlayerId || !string.IsNullOrEmpty(s.DriverId))
                {
                    continue;
                }

                float d = (Game.ScenePos(s.X, s.Y, s.Z) - Game.PlayerPosition).magnitude;
                if (d > reach && d < best)
                {
                    best = d;
                    kind = s.Kind;
                }
            }

            distance = best;
            return best < hintRange;
        }

        /// <summary>#2049: the prompt key for a Crystal Net device in the crosshair: a switch toggles, a button
        /// presses, a configurable device opens its menu, everything else just names itself as linked.</summary>
        private static string CrystalPromptKey(NetCrystalDevice dev)
        {
            var kind = CrystalDeviceUi.KindOf(dev);
            if (kind == BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.Switch)
            {
                return "ui.crystal.prompt.toggle";
            }

            if (kind == BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.Button)
            {
                return "ui.crystal.prompt.press";
            }

            if (kind == BlocksBeyondTheStars.Shared.Definitions.CrystalDeviceKind.LiftStop)
            {
                return "ui.crystal.prompt.call"; // #2266
            }

            return BlocksBeyondTheStars.Shared.Definitions.CrystalNetRules.IsConfigurable(kind) ? "ui.crystal.prompt.menu" : "ui.crystal.prompt.linked";
        }

        /// <summary>Aiming at your own base core (#1267): the rename key, what the core's life-support field
        /// covers beyond its cube (sealed rooms, #794) and whether the spot you stand on has air — the sealed-room
        /// fill was invisible until now, so nobody knew the energy door is the one that seals.</summary>
        private string BaseAirPrompt(BlocksBeyondTheStars.Shared.Localization.Localizer loc, BlocksBeyondTheStars.Networking.Messages.NetBase b)
        {
            string air = b.SealedRooms > 0
                ? loc.Get("ui.base.air_status").Replace("{cells}", b.AirCells.ToString()).Replace("{rooms}", b.SealedRooms.ToString())
                : loc.Get("ui.base.air_none"); // #2649: the rules — any door, no size limit
            string here = Game.LifeSupportSource == 3 ? loc.Get("ui.base.air_here_ok") : loc.Get("ui.base.air_here_no");
            if (Game.LifeSupportSource != 3 && b.SealedRooms > 0)
            {
                // #2615: other rooms breathe, this spot does not — point at the command that names the reason (and the leak).
                here += "  ·  " + loc.Get("ui.base.air_here_why");
            }

            // #2679: how many people live here (a server that does not send it leaves the cap at 0 — nothing shown).
            string residents = b.ResidentCap > 0
                ? "  ·  " + loc.Get("ui.base.residents").Replace("{n}", b.Residents.ToString()).Replace("{max}", b.ResidentCap.ToString())
                : string.Empty;
            // #2717: E at the core also lists who lives here — said where the server knows residents at all.
            string key = b.ResidentCap > 0 ? "ui.base.core_hint" : "ui.base.rename_hint";
            return $"{ExpandKeyTokens(loc, loc.Get(key))}  ·  {air}  ·  {here}{residents}"; // #2502: {key:Interact}
        }

        /// <summary>#2018: the Feed prompt while food is held and a begging animal is near — it shares the loot line, and a herd
        /// begging at your feet is the rarer, more urgent thing to say than a crate three blocks off.</summary>
        private string FeedOrLootText(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            if (_playerRig == null)
            {
                _playerRig = FindAnyObjectByType<PlayerController>(); // the rig WorldRig creates; null in the menu
            }

            if (_playerRig != null && _playerRig.CanFeedCreature)
            {
                return ExpandKeyTokens(loc, loc.Get("ui.hud.feed")); // "{key:FeedCreature}: feed the herd" (#2502)
            }

            return LootText(loc);
        }

        private PlayerController _playerRig; // #2018: cached for the Feed prompt's probe

        private string LootText(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            // #2613: the container G takes from — the same seam-aware picker PlayerController's loot key uses. The line used to
            // measure straight across the seam and stayed blank beside a crate on the far side of X = 0.
            var here = Game.PlayerPosition;
            var nearest = InteractReach.NearestContainer(Game.Containers, here.x, here.y, here.z, Game.Circumference, crateOnly: false);
            if (nearest == null)
            {
                return string.Empty;
            }

            // #2726: a cache that belongs to a village's people is not offered — the line says whose it is (the server's own
            // refusal sentence) instead of "G: loot".
            if (!string.IsNullOrEmpty(nearest.OwnedBy))
            {
                return loc.Get("srv.loot.village_owned").Replace("{name}", nearest.OwnedBy);
            }

            // A storage crate (Task 5 Stage 3b) shows the take/store keys; salvage capsules just say "loot".
            // A dedicated crate (#1032) announces its filter, so "H did nothing" reads as "wrong crate".
            if (nearest.Kind == "crate")
            {
                string label = nearest.Filter is { Length: > 0 }
                    ? $"{loc.Get("ui.hud.stash")} ({nearest.ItemCount})  ·  {loc.Get("ui.hud.stash_filtered")}"
                    : $"{loc.Get("ui.hud.stash")} ({nearest.ItemCount})";
                // #2502: the take/store/open keys are {key:Action} tokens — the bound keys, the pad's Actions list, ACT.
                return $"{label}  ·  {ExpandKeyTokens(loc, loc.Get("ui.hud.stash_keys"))}  ·  {ExpandKeyTokens(loc, loc.Get("ui.hud.stash_filter_key"))}";
            }

            return $"{ExpandKeyTokens(loc, loc.Get("ui.hud.loot"))} ({nearest.ItemCount})";
        }

        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Scanner;
        }

        // --- helpers ---

        private static Image Panel(Transform parent, float x, float y, float w, float h)
            => UiHolo.AddPanel(parent, x, y, w, h, new Color(0.05f, 0.12f, 0.24f, 0.82f), 10f, 1.5f, 1f);
    }
}
