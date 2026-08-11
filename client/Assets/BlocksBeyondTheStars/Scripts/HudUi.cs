// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
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
    /// </summary>
    public sealed class HudUi : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Local settings (wired by <see cref="WorldRig"/>) — read live for the optional playtime readout.</summary>
        public ClientSettings Settings;

        private const int Slots = 9;
        private const float W = UiKit.HudRefW, H = UiKit.HudRefH; // smaller reference → a ~1.25× bigger HUD

        // Scan panel (bottom-left), in HUD reference units. Width is CAPPED at 390: the hotbar backplate
        // starts at x 400, so a wider panel clips it. The left column above is VEGA's (speech 396…586,
        // objective chip 594…642 — see VegaPanel), so this panel starts at 650 (#482).
        private const float ScanPanelY = 650f, ScanPanelW = 390f, ScanPanelH = 182f;

        // Vitals panel (top-left, under the location panel), in HUD reference units. Its HEIGHT VARIES:
        // the ship rows (hull/shield) are appended only in the states that have them, so anything parked
        // below it must read VitalsBottomY rather than assume a fixed edge (#915).
        private const float VitalsPanelY = 64f;

        /// <summary>Bottom edge of the vitals panel in HUD reference coordinates (y down from the top),
        /// republished on every refresh. The space-flight overlay is a SEPARATE canvas drawn above this
        /// one, but at the same reference resolution — it parks its cargo/oxygen readout below this edge
        /// so the two can't overprint each other whichever rows the panel is currently showing (#915).
        /// The initial value is the tallest the panel ever gets, so a reader that runs before the first
        /// refresh still clears it.</summary>
        public static float VitalsBottomY { get; private set; } = VitalsPanelY + 196f;

        /// <summary>How long a scan readout lingers once the scanner is put away. Was 12 s — long enough to
        /// read the old one-liner, not the fuller readout (#482). While the scanner is still the held item
        /// the panel stays pinned regardless (see <see cref="RefreshScan"/>).</summary>
        private const float ScanHoldSeconds = 20f;

        private static readonly Color Health = new Color(0.92f, 0.32f, 0.34f);
        private static readonly Color Oxygen = new Color(0.36f, 0.78f, 1f);
        private static readonly Color Energy = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Hunger = new Color(1f, 0.6f, 0.25f);

        /// <summary>Energy-bar tint while the suit's climate control is actively draining it (#666) —
        /// a hot orange-red, so "why is my energy falling?" answers itself at a glance.</summary>
        private static readonly Color EnergyStressed = new Color(1f, 0.45f, 0.2f);
        private static readonly Color HullC = new Color(0.6f, 0.66f, 0.74f);
        private static readonly Color ShieldC = new Color(0.4f, 0.7f, 1f);

        private Canvas _canvas;
        private GameObject _crosshair, _locationPanel, _vitalsPanel, _shipRows;

        // Crosshair state (#693): hostile tint while an enemy is under the reticle + the hit-marker flash.
        private static readonly Color HostileAim = new Color(1f, 0.4f, 0.35f, 0.95f);
        private static readonly Color HitMarkerCol = new Color(1f, 0.85f, 0.4f, 0.95f);
        private Image _crossV, _crossH;
        private GameObject _hitMarker;
        private float _hitMarkerTimer;

        /// <summary>Set while a scope draws its own reticle (see <see cref="BinocularOptic"/>); hides the HUD
        /// crosshair for as long as it is up.</summary>
        public static bool SuppressCrosshair;
        private Text _locTitle, _locPlace, _toast, _inSpace, _prompt, _loot, _hint, _todText, _compassDist, _compassWpDist;
        private Text _observer; // SPECTATOR badge while fleet-admin observer mode is active (issue #487)
        private GameObject _playtimePanel; // optional session/total playtime readout (top-right, under the clock)
        private Text _playtimeText;
        private RectTransform _todMarker;
        private RectTransform _compassShip, _compassWp;
        private Transform _compassParent; // parent for pooled beacon blips (item 37)
        private readonly System.Collections.Generic.List<RectTransform> _compassBeacons = new();

        private struct VitalRow { public Image Fill; public Text Label; public GameObject Go; public bool Warn; public Color BaseColor; }
        private VitalRow[] _vitals;
        private float _lowVitalBeepTimer; // shared low-vitals alarm cadence (#753)

        private UiKit.QuickSlot[] _hotbar;
        private GameObject _hotbarRoot; // backplate + cells + rings, toggled together when flying

        // Scan / wreck panels.
        private GameObject _scanPanel, _wreckPanel, _shipRepairPanel;

        // Creature taming prompt: decoded mood + what the creature wants now, with the four response buttons.
        private GameObject _tamePanel;
        private Text _tameName, _tameMood, _tameNeed, _tameTrust;
        private Button _tameFeed, _tameCalm, _tameApproach, _tameSpace, _tameStop;
        private Text _scanSubject, _scanInfo, _scanThreat, _scanKnow, _wreckName, _wreckProg, _wreckHint;
        private Text _shipRepairTitle, _shipRepairProg, _shipRepairHint;

        // Hover-speeder vehicle HUD: integrity + energy gauges, speed and the drive prompt (shown while driving).
        private GameObject _speederPanel;
        private Image _speederHull, _speederFuel;
        private Text _speederTitle, _speederSpeed, _speederHullLabel, _speederFuelLabel, _speederHint;
        private Image _wreckBar, _shipRepairBar;
        private Button _wreckClaim, _shipRepairBtn;

        // Damage feedback (B21): a red screen flash + a cause label when health drops.
        private Image _dmgFlash;
        private Text _dmgCause;
        private float _prevHealth = 100f, _flashTimer, _causeTimer;
        private string _causeKey = string.Empty;
        private float _o2BeepTimer; // periodic low-oxygen warning tone (interval shrinks as O₂ drops)

        private int _lastSelSlot = -1; // hotbar selection tick state

        // Pickup feed (#745): a short right-aligned column just above the hotbar's right end, one row per
        // collected item ("icon  +n name"). Repeat pickups of the same item merge and count up instead of
        // stacking rows; each row fades out after a couple of seconds. Rows live under the hotbar root so
        // flying/driving hides the feed together with the bar.
        private sealed class PickupRow
        {
            public string Item;
            public int Count;
            public float Ttl;
            public GameObject Go;
            public CanvasGroup Fade;
            public Text Label;
        }

        private readonly System.Collections.Generic.List<PickupRow> _pickupRows = new System.Collections.Generic.List<PickupRow>();
        private const int PickupMaxRows = 4;
        private const float PickupRowH = 26f, PickupRowW = 300f, PickupLife = 2.5f, PickupFadeTime = 0.5f;
        private float _pickupRightX, _pickupAnchorY; // right edge + top of the hotbar backplate

        // Research toast (#763): "New research available!" with the blueprint's icon, top-centre under
        // the IN SPACE/observer lines. One toast at a time; further keys wait in Game.ResearchAvailable.
        private GameObject _researchGo;
        private RectTransform _researchRect;
        private CanvasGroup _researchFade;
        private RawImage _researchIcon;
        private Image _researchGlow;
        private RectTransform _researchShine;
        private Text _researchHead, _researchName;
        private float _researchAge = -1f; // <0 = idle, no toast up
        private const float ResearchW = 480f, ResearchH = 86f, ResearchY = 64f;
        private const float ResearchPop = 0.25f, ResearchHold = 3.0f, ResearchFade = 0.5f;

        // Perf: the text-heavy HUD refresh runs at ~10 Hz, not every frame — rebuilding dozens of strings
        // per frame is pure GC churn and the readouts (vitals, clock, prompts) don't change faster than
        // that anyway. Motion-coupled elements (compass blips) still update per frame, and a hotbar
        // selection change forces an immediate refresh so input feedback never lags.
        private const float RefreshInterval = 0.1f;
        private float _refreshTimer;
        private int _lastCompassDist = int.MinValue;
        private int _lastCompassWpDist = int.MinValue;

        /// <summary>Edge detector for the base life-support field (#782): true while the last HUD refresh saw
        /// the player inside some founded base's zone, so the "Life support: …" toast fires once on entry.</summary>
        private bool _wasInBaseZone;

        /// <summary>Set while a HUD exists so world-side FX (MiningFx) can hand off pickup fly-ins.</summary>
        public static HudUi Instance { get; private set; }
        private Canvas _flyCanvas; // own overlay canvas so the visor distortion can't bend the fly-ins

        private void Awake() => Instance = this;

        private void LateUpdate()
        {
            if (Game?.Localizer == null)
            {
                return;
            }

            EnsureBuilt();
            UpdateDamageFeedback(); // always, so the health baseline tracks even while a menu is open

            // Hidden while a menu is open — and during the prologue cinematic (#760): vitals/hotbar/
            // crosshair in the frame break the letterboxed shot. The VEGA speech panel and the
            // CinematicFrame chrome live on their own canvases, so the story text stays visible.
            bool show = !Game.MenuOpen && !Game.VegaPrologueActive && !Game.CinematicCameraActive;
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
            }

            // Even while a menu hides the canvas: rows must keep aging (a closed menu must not resurrect
            // stale pickups) and gains queued during the hidden-hotbar states must keep draining away.
            UpdatePickupFeed(Time.deltaTime);
            UpdateResearchToast(Time.deltaTime);

            // While the binocular optic is raised its own reticle takes over — two crosshairs stacked on top of
            // each other read as a rendering bug (BinocularOptic owns the flag and always clears it).
            if (_crosshair != null && _crosshair.activeSelf == SuppressCrosshair)
            {
                _crosshair.SetActive(!SuppressCrosshair);
            }

            if (show)
            {
                UpdateCrosshairState(Time.deltaTime); // per frame: aim tint must not lag the reticle
                RefreshCompass(); // per frame: blips counter-rotate with the camera, throttling would judder
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
                // NetBase carries the core's block centre (X/Z at +0.5) — floor back to the cell.
                if (Mathf.Abs(px - Mathf.FloorToInt(b.X)) <= WorldConstants.BaseZoneRadius
                    && Mathf.Abs(py - Mathf.FloorToInt(b.Y)) <= WorldConstants.BaseZoneRadius
                    && Mathf.Abs(pz - Mathf.FloorToInt(b.Z)) <= WorldConstants.BaseZoneRadius)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>Flashes the screen red + names the cause whenever the player's health drops (B21), so
        /// environmental damage (lava, suffocation, starvation) or a hit never kills you "out of nowhere".</summary>
        private void UpdateDamageFeedback()
        {
            float dt = Time.deltaTime;
            float h = Game.Health;
            if (h < _prevHealth - 0.05f)
            {
                float drop = _prevHealth - h;
                _flashTimer = Mathf.Min(0.6f, 0.22f + drop * 0.03f); // a bigger hit flashes longer
                _causeTimer = 2.2f;
                _causeKey = InferDamageCause();
                UrpScenePost.Instance?.PulseVignette(0.4f + Mathf.Clamp01(drop / 25f) * 0.6f); // vignette kick
            }

            UpdateOxygenAlarm(dt);

            _prevHealth = h;

            if (_flashTimer > 0f) { _flashTimer = Mathf.Max(0f, _flashTimer - dt); }
            if (_dmgFlash != null)
            {
                var c = _dmgFlash.color;
                c.a = Mathf.Clamp01(_flashTimer / 0.6f) * 0.38f; // peak ~0.38 — clear but not blinding
                _dmgFlash.color = c;
            }

            if (_causeTimer > 0f) { _causeTimer -= dt; }
            if (_dmgCause != null)
            {
                bool showCause = _causeTimer > 0f && Game.Health > 0f && !Game.MenuOpen;
                _dmgCause.text = showCause && Game.Localizer != null ? Game.Localizer.Get(_causeKey) : string.Empty;
            }
        }

        /// <summary>Low-oxygen warning: under 25% O₂ a pulsing blue vignette ramps in (UrpScenePost) and a
        /// periodic two-beep alarm plays, its interval shrinking as oxygen runs out. Silent when healthy,
        /// breathable air keeps O₂ at 100 so it never fires there.</summary>
        private void UpdateOxygenAlarm(float dt)
        {
            float o2 = Game.Oxygen;
            float alarm = o2 <= 25f && Game.Health > 0f ? Mathf.Clamp01((25f - o2) / 25f) : 0f;
            UrpScenePost.Instance?.SetOxygenAlarm(alarm);

            if (alarm <= 0f)
            {
                _o2BeepTimer = 0f; // first beep fires immediately when the alarm next trips
                return;
            }

            _o2BeepTimer -= dt;
            if (_o2BeepTimer <= 0f)
            {
                _o2BeepTimer = Mathf.Lerp(4f, 1.2f, alarm); // urgent = faster
                ClientAudio.Instance?.Cue("o2_warning", 0.45f + alarm * 0.35f);
            }
        }

        /// <summary>Best-effort cause of the latest health loss from local state — lava (most acute) first,
        /// then suffocation / starvation, else a generic hit (creature/fall).</summary>
        private string InferDamageCause()
        {
            var p = Game.PlayerPosition;
            var id = Game.World != null
                ? Game.World.GetBlock(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z))
                : default;
            if (Game.Content?.BlockById(id)?.Key == "lava") { return "ui.hud.dmg_lava"; }
            if (Game.Oxygen <= 0.5f) { return "ui.hud.dmg_suffocate"; }
            if (Game.Hunger <= 0.5f) { return "ui.hud.dmg_starve"; }
            // Exposure damage (#666): the suit is out of energy and climate control lost the fight —
            // the environment temperature says which extreme is doing the damage.
            if (Game.SuitClimateActive && Game.SuitEnergy <= 0.5f)
            {
                return (Game.Environment?.Temperature ?? 15f) < 15f ? "ui.hud.dmg_freeze" : "ui.hud.dmg_overheat";
            }

            return "ui.hud.dmg_hit";
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

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>A small block-tile icon flying from the mined block toward the hotbar — the
        /// "it went into your inventory" read (mining-loop juice). No-op if the block is unknown.</summary>
        public void FlyPickup(Vector3 worldPos, BlocksBeyondTheStars.Shared.Primitives.BlockId block)
        {
            var cam = Camera.main;
            var def = Game?.Content?.BlockById(block);
            if (cam == null || def == null || Game.Atlas == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z <= 0f)
            {
                return; // behind the camera
            }

            if (_flyCanvas == null)
            {
                var go = new GameObject("PickupFlyCanvas");
                _flyCanvas = go.AddComponent<Canvas>();
                _flyCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _flyCanvas.sortingOrder = 240;
            }

            var iconGo = new GameObject("fly_icon");
            iconGo.transform.SetParent(_flyCanvas.transform, false);
            var raw = iconGo.AddComponent<RawImage>();
            raw.texture = Game.Atlas.Texture;
            raw.uvRect = Game.Atlas.TileUv(def.NumericId.Value);
            raw.raycastTarget = false;
            var rt = raw.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(34f, 34f);

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var fly = iconGo.AddComponent<FlyIcon>();
            fly.From = new Vector2(sp.x, sp.y) - center;
            fly.To = new Vector2(0f, -Screen.height * 0.5f + 70f); // the hotbar zone (bottom-centre)
        }

        /// <summary>A quick scale tick (1 → 1.1 → 1 over 0.12 s) on the newly selected hotbar slot.</summary>
        private sealed class SlotTick : MonoBehaviour
        {
            private const float Life = 0.12f;
            private float _t;

            public void Restart()
            {
                _t = 0f;
                enabled = !UiKit.ReducedMotion;
                if (!enabled)
                {
                    transform.localScale = Vector3.one;
                }
            }

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_t / Life);
                transform.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.1f);
                if (k >= 1f)
                {
                    transform.localScale = Vector3.one;
                    enabled = false;
                }
            }
        }

        /// <summary>Eases a pickup icon from its spawn point into the hotbar zone, shrinking, then dies.</summary>
        private sealed class FlyIcon : MonoBehaviour
        {
            public Vector2 From, To;

            private const float Life = 0.45f;
            private float _t;
            private RectTransform _rt;

            private void Awake() => _rt = (RectTransform)transform;

            private void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / Life);
                _rt.anchoredPosition = Vector2.Lerp(From, To, k * k); // ease-in: accelerates toward the bar
                transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.45f, k);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
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

            // Damage feedback (B21): full-screen red flash (behind the HUD so bars stay readable) + a cause label.
            var flashGo = new GameObject("DamageFlash", typeof(RectTransform));
            flashGo.transform.SetParent(root, false);
            var frt = flashGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            _dmgFlash = flashGo.AddComponent<Image>();
            _dmgFlash.sprite = UiKit.SolidSprite;
            _dmgFlash.color = new Color(0.85f, 0.06f, 0.05f, 0f);
            _dmgFlash.raycastTarget = false;
            _dmgCause = UiKit.AddText(root, W / 2f - 220, H / 2f - 90, 440, 28, string.Empty, 20, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleCenter, FontStyle.Bold);

            // Crosshair.
            _crosshair = new GameObject("Crosshair", typeof(RectTransform));
            _crosshair.transform.SetParent(root, false);
            var ch = _crosshair.GetComponent<RectTransform>();
            ch.anchorMin = ch.anchorMax = new Vector2(0.5f, 0.5f);
            ch.sizeDelta = Vector2.zero;
            MakeCrosshair(ch);

            // Location (top-left).
            _locationPanel = Panel(root, 10, 10, 280, 46).gameObject;
            _locTitle = UiKit.AddText(_locationPanel.transform, 10, 3, 260, 18, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _locPlace = UiKit.AddText(_locationPanel.transform, 10, 22, 260, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);

            // Vitals panel (6 rows; ship rows toggled).
            _vitalsPanel = Panel(root, 10, VitalsPanelY, 226, 196).gameObject;
            _vitals = new VitalRow[6];
            string[] order = { "health", "oxygen", "energy", "hunger", "hull", "shield" };
            for (int i = 0; i < 6; i++)
            {
                _vitals[i] = MakeVital(_vitalsPanel.transform, 10, 8 + i * 24, order[i]);
            }

            // Hotbar: a centred row of large icon cells on a backplate, raised a touch off the bottom edge so the
            // held tool reads clearly. Shares its cell style with the flight ship-systems bar (UiKit.QuickSlot).
            // All cells + backplate + selection rings live under one stretched container so flying hides them in one.
            _hotbarRoot = new GameObject("Hotbar", typeof(RectTransform));
            _hotbarRoot.transform.SetParent(root, false);
            var hbRt = _hotbarRoot.GetComponent<RectTransform>();
            hbRt.anchorMin = Vector2.zero; hbRt.anchorMax = Vector2.one; hbRt.offsetMin = hbRt.offsetMax = Vector2.zero;
            var hbParent = _hotbarRoot.transform;
            _hotbar = new UiKit.QuickSlot[Slots];
            const float sw = 72f, pitch = 80f;
            float total = (Slots - 1) * pitch + sw, x0 = (W - total) / 2f, hy = H - sw - 40f;
            UiKit.QuickBackplate(hbParent, x0 - 12f, hy - 10f, total + 24f, sw + 20f);
            for (int i = 0; i < Slots; i++)
            {
                _hotbar[i] = UiKit.MakeQuickSlot(hbParent, x0 + i * pitch, hy, sw);
            }

            // Pickup feed anchors (#745): rows sit flush with the backplate's right edge, stacking upward
            // from just above it.
            _pickupRightX = x0 + total + 12f;
            _pickupAnchorY = hy - 14f;

            // Compass (round).
            var comp = new GameObject("Compass", typeof(RectTransform));
            comp.transform.SetParent(root, false);
            UiKit.Place(comp, W - 130f, 10, 120, 120);
            var craw = comp.AddComponent<RawImage>();
            craw.texture = UiKit.RadarCircle;
            UiKit.AddText(comp.transform, 0, 2, 120, 18, "▲", 14, UiKit.Cyan, TextAnchor.UpperCenter, FontStyle.Bold);
            _compassDist = UiKit.AddText(comp.transform, 0, 100, 120, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Waypoint distance on its own line under the ship distance — before #592 the compass number
            // was the SHIP only and the waypoint's distance existed nowhere outside the map panel.
            _compassWpDist = UiKit.AddText(comp.transform, 0, 118, 120, 18, string.Empty, 14, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassShip = Blip(comp.transform, new Color(0.3f, 0.8f, 1f), 8f);
            // The waypoint blip is the map_waypoint ICON, not another plain square — at 7 px amber it was
            // nearly indistinguishable from the 6 px amber beacon blips (#592).
            _compassWp = Blip(comp.transform, new Color(1f, 0.85f, 0.3f), 16f, "map_waypoint");
            _compassParent = comp.transform;

            // Time of day + temperature.
            var tod = Panel(root, W - 210f, 140, 200, 56);
            _todText = UiKit.AddText(tod.transform, 10, 5, 184, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(tod.transform, 10, 32, 150, 12, UiKit.SolidSprite, new Color(0.05f, 0.08f, 0.16f));
            UiKit.AddImage(tod.transform, 10 + 150 * 0.25f, 32, 150 * 0.5f, 12, UiKit.SolidSprite, new Color(0.30f, 0.55f, 0.85f, 0.85f));
            _todMarker = UiKit.AddImage(tod.transform, 10, 30, 2, 16, UiKit.SolidSprite, UiKit.Cyan).rectTransform;

            // Optional playtime readout, tucked just under the clock (top-right). Hidden unless the comfort
            // setting is on; refreshed each frame in RefreshPlaytime.
            _playtimePanel = Panel(root, W - 210f, 200, 200, 40).gameObject;
            _playtimeText = UiKit.AddText(_playtimePanel.transform, 10, 4, 184, 30, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _playtimePanel.SetActive(false);

            // Toast / indicators / prompts / hint.
            _toast = UiKit.AddText(root, 14, 268, W - 28, 22, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _inSpace = UiKit.AddText(root, W / 2f - 100, 8, 200, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Observer badge (issue #487). Top centre, always-on while the mode is active: an admin who forgets
            // they are invisible is how you end up "fixing" a world nobody can see you in.
            _observer = UiKit.AddText(root, W / 2f - 160, 30, 320, 24, string.Empty, 18, UiKit.Warn,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt = UiKit.AddText(root, W / 2f - 160, H / 2f + 24, 320, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _loot = UiKit.AddText(root, W / 2f - 160, H / 2f + 48, 320, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _hint = UiKit.AddText(root, (W - 1400) / 2f, H - 26, 1400, 20, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleCenter);

            // Vehicle HUD (hover speeder): integrity + energy gauges, speed + drive prompt, where the hotbar sits
            // (the hotbar is hidden while driving). Hidden until the player boards a speeder.
            _speederPanel = Panel(root, W / 2f - 170, H - 136, 340, 108).gameObject;
            _speederTitle = UiKit.AddText(_speederPanel.transform, 12, 6, 220, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speederSpeed = UiKit.AddText(_speederPanel.transform, 108, 6, 220, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Bold);
            _speederHull = MakeBar(_speederPanel.transform, 12, 30, 316, 16);
            _speederHullLabel = UiKit.AddText(_speederPanel.transform, 18, 30, 304, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speederFuel = MakeBar(_speederPanel.transform, 12, 52, 316, 16);
            _speederFuelLabel = UiKit.AddText(_speederPanel.transform, 18, 52, 304, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _speederHint = UiKit.AddText(_speederPanel.transform, 12, 80, 316, 18, string.Empty, 12, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _speederPanel.SetActive(false);

            // Scan result panel (bottom-left): the scanner's detail readout — subject, description,
            // threat, knowledge, and a highlighted "new discovery" line on a first-time scan. Enlarged in
            // #482 (it carried the densest text in the game at the smallest size). WIDTH IS CAPPED: the
            // hotbar backplate owns x 400…1136, so this panel must not reach x 400.
            _scanPanel = Panel(root, 10, ScanPanelY, ScanPanelW, ScanPanelH).gameObject;
            var scanIcon = UiKit.Icon("item_advanced_scanner") ?? UiKit.Icon("cat_target");
            float scanTextX = 12f;
            if (scanIcon != null)
            {
                UiKit.AddImage(_scanPanel.transform, 10, 8, 26, 26, scanIcon, UiKit.Cyan);
                scanTextX = 42f;
            }

            const float scanTextW = ScanPanelW - 24f;
            _scanSubject = UiKit.AddText(_scanPanel.transform, scanTextX, 8, ScanPanelW - 12f - scanTextX, 26, string.Empty, 19, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _scanInfo = UiKit.AddText(_scanPanel.transform, 12, 40, scanTextW, 78, string.Empty, 17, UiKit.TextCol, TextAnchor.UpperLeft);
            _scanInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Truncate, NOT the AddText default Overflow: a long yield list (asteroids report every
            // distinct resource) used to run straight over the threat + knowledge lines (#482).
            _scanInfo.verticalOverflow = VerticalWrapMode.Truncate;
            _scanThreat = UiKit.AddText(_scanPanel.transform, 12, 122, scanTextW, 22, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            _scanKnow = UiKit.AddText(_scanPanel.transform, 12, 148, scanTextW, 26, string.Empty, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(_scanSubject);
            UiKit.AddOutline(_scanInfo);
            UiKit.AddOutline(_scanThreat);
            UiKit.AddOutline(_scanKnow);

            // Wreck panel (right).
            _wreckPanel = Panel(root, W - 260f, 140, 250, 150).gameObject;
            _wreckName = UiKit.AddText(_wreckPanel.transform, 10, 26, 230, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddText(_wreckPanel.transform, 10, 6, 230, 18, Game?.Localizer?.Get("ui.hud.wreck") ?? "WRECK", 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(_wreckPanel.transform, 10, 48, 230, 14, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f));
            _wreckBar = UiKit.AddImage(_wreckPanel.transform, 10, 48, 230, 14, UiKit.SolidSprite, UiKit.Cyan);
            _wreckBar.type = Image.Type.Filled;
            _wreckBar.fillMethod = Image.FillMethod.Horizontal;
            _wreckProg = UiKit.AddText(_wreckPanel.transform, 12, 47, 226, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft);
            _wreckHint = UiKit.AddText(_wreckPanel.transform, 10, 68, 230, 50, string.Empty, 12, UiKit.CyanDim, TextAnchor.UpperLeft);
            _wreckHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            _wreckClaim = UiKit.AddButton(_wreckPanel.transform, 10, 120, 230, 24, string.Empty, () => Game.Network?.SendClaimWreck());

            // Ship-repair panel (right, below the wreck panel) — the cockpit "Repair ship" action: buy hull
            // back + refill EVA-carved hull cells with one click, paid in metal (docs/developer/SHIP_REPAIR.md).
            _shipRepairPanel = Panel(root, W - 260f, 300, 250, 120).gameObject;
            _shipRepairTitle = UiKit.AddText(_shipRepairPanel.transform, 10, 6, 230, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(_shipRepairPanel.transform, 10, 30, 230, 14, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f));
            _shipRepairBar = UiKit.AddImage(_shipRepairPanel.transform, 10, 30, 230, 14, UiKit.SolidSprite, UiKit.Cyan);
            _shipRepairBar.type = Image.Type.Filled;
            _shipRepairBar.fillMethod = Image.FillMethod.Horizontal;
            _shipRepairProg = UiKit.AddText(_shipRepairPanel.transform, 12, 29, 226, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft);
            _shipRepairHint = UiKit.AddText(_shipRepairPanel.transform, 10, 50, 230, 36, string.Empty, 12, UiKit.CyanDim, TextAnchor.UpperLeft);
            _shipRepairHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            _shipRepairBtn = UiKit.AddButton(_shipRepairPanel.transform, 10, 90, 230, 24, string.Empty, () => Game.Network?.SendRepairShip("all"));

            // Creature-taming prompt (bottom-centre, above the hotbar): the translator's decoded mood + need,
            // a trust bar of correct responses, and the four response actions. Captions are set in RefreshTaming.
            _tamePanel = Panel(root, W / 2f - 250f, 150f, 500f, 172f).gameObject;
            _tameName = UiKit.AddText(_tamePanel.transform, 14, 8, 410, 22, string.Empty, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _tameStop = UiKit.AddButton(_tamePanel.transform, 432, 8, 56, 24, string.Empty, () => Respond("cancel"));
            _tameMood = UiKit.AddText(_tamePanel.transform, 14, 34, 472, 20, string.Empty, 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _tameNeed = UiKit.AddText(_tamePanel.transform, 14, 56, 472, 38, string.Empty, 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            _tameNeed.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tameTrust = UiKit.AddText(_tamePanel.transform, 14, 96, 472, 18, string.Empty, 13, UiKit.TextCol, TextAnchor.MiddleLeft);
            const float tby = 122f, tbw = 112f, tbh = 40f, tgap = 6f;
            _tameFeed = UiKit.AddButton(_tamePanel.transform, 14 + 0 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("feed"));
            _tameCalm = UiKit.AddButton(_tamePanel.transform, 14 + 1 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("calm"));
            _tameApproach = UiKit.AddButton(_tamePanel.transform, 14 + 2 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("approach"));
            _tameSpace = UiKit.AddButton(_tamePanel.transform, 14 + 3 * (tbw + tgap), tby, tbw, tbh, string.Empty, () => Respond("space"));
            _tamePanel.SetActive(false);
        }

        /// <summary>Sends the player's chosen response in the current taming ritual (read from the live state).</summary>
        private void Respond(string response)
        {
            var t = Game?.TameState;
            if (t != null && Game?.Network != null)
            {
                Game.Network.SendTameRespond(t.CreatureId, response);
            }
        }

        private void Refresh()
        {
            var loc = Game.Localizer;

            // Location — show the boarded station's name when on one, else the planet/system.
            string place;
            if (!string.IsNullOrEmpty(Game.StationName))
            {
                place = $"{Game.StationName}  ({loc.Get("ui.hud.station")})";
            }
            else
            {
                place = string.IsNullOrEmpty(Game.LocationName) ? "—" : Game.LocationName;
                if (Game.Aboard) place += $"  ({loc.Get("ui.hud.aboard")})";
            }
            _locTitle.text = loc.Get("ui.hud.location").ToUpperInvariant();
            _locPlace.text = place;

            // Vitals.
            SetVital(0, loc.Get("ui.hud.health"), Game.Health, Game.Health / 100f, Health, true);
            // Base life-support field (#782): a founded base's zone (the shared radius cube around its
            // base_core) always breathes. Client-side mirror of the server's check, HUD feedback only —
            // announced once on entry and spelled out on the O2 bar, but only where it matters (worlds
            // whose own air is NOT breathable; under a breathable sky the base adds nothing).
            bool breathable = Game.Environment != null && Game.Environment.Breathable;
            // Base air (#782/#794): the local cube mirror still names the base; SEALED rooms beyond the
            // cube only the server can judge (the fill needs the airtight table), so the server-sent
            // LifeSupportSource (3 = base) extends the same feedback into them — and doubles as a "life
            // support lost" cue the moment a mined wall drops the source while the sky stays unbreathable.
            var zoneBase = FindBaseZone();
            bool baseAir = zoneBase != null || Game.LifeSupportSource == 3;
            if (baseAir && !_wasInBaseZone && !breathable)
            {
                string baseName = zoneBase == null || string.IsNullOrEmpty(zoneBase.Name)
                    ? loc.Get("ui.base.default") : zoneBase.Name;
                Game.ShowMessage(loc.Get("ui.base.life_support").Replace("{name}", baseName));
            }
            else if (!baseAir && _wasInBaseZone && !breathable && Game.LifeSupportSource == 0 && !Game.Aboard)
            {
                Game.ShowMessage(loc.Get("ui.base.air_left")); // stepped (or un-sealed) out of base air
            }
            _wasInBaseZone = baseAir;

            // Spell out "(breathable)" rather than a bare "*", so new players understand the full O2 bar isn't
            // draining because the air here is breathable — and that it will drain elsewhere (space, toxic worlds).
            string oxySuffix = breathable ? "  (" + loc.Get("ui.hud.breathable") + ")"
                : baseAir ? "  (" + loc.Get("ui.hud.base_air") + ")"
                : string.Empty;
            string oxy = loc.Get("ui.hud.oxygen") + oxySuffix;
            SetVital(1, oxy, Game.Oxygen, Game.Oxygen / 100f, Oxygen, true);
            // While climate control fights heat/cold/vacuum (#666) the energy bar turns stress-orange.
            SetVital(2, loc.Get("ui.hud.energy"), Game.SuitEnergy, Game.SuitEnergy / 100f,
                Game.SuitClimateActive ? EnergyStressed : Energy, true);
            SetVital(3, loc.Get("ui.hud.hunger"), Game.Hunger, Game.Hunger / 100f, Hunger, true);
            // Ship rows (hull/shield) exist whenever the player owns a ship in combat range — but while
            // PILOTING they repeat the flight instrument line (SPD/THR/HDG + HULL/SHD, bottom-left), so
            // hide them there, exactly as the compass and the time-of-day panel already do (#915). On an
            // EVA the instrument line is hidden (it needs !_eva), so these bars are the only hull readout
            // and must stay.
            bool piloting = Game.SpaceViewActive && !Game.InEva;
            bool ship = Game.ShipCombat != null && !piloting;
            if (ship)
            {
                var c = Game.ShipCombat;
                SetVital(4, loc.Get("ui.hud.hull"), c.Hull, c.HullMax > 0 ? c.Hull / c.HullMax : 0f, HullC, true);
                SetVital(5, loc.Get("ui.hud.shield"), c.Shield, c.ShieldMax > 0 ? c.Shield / c.ShieldMax : 0f, ShieldC, true);
            }
            else
            {
                SetVital(4, null, 0, 0, HullC, false);
                SetVital(5, null, 0, 0, ShieldC, false);
            }

            float vitalsHeight = ship ? 196f : 116f;
            _vitalsPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(226, vitalsHeight);
            VitalsBottomY = VitalsPanelY + vitalsHeight;

            RefreshHotbar(loc);
            RefreshTimeOfDay(loc);
            RefreshPlaytime(loc);

            _toast.text = Game.LastMessage ?? string.Empty;
            _inSpace.text = Game.InSpace ? loc.Get("ui.hud.in_space") : string.Empty;
            _observer.text = Game.Spectating ? loc.Get("ui.hud.observer") : string.Empty;
            _hint.text = InputMap.ActiveDevice switch
            {
                // On touch the on-screen buttons are self-labelling, so the text hint just adds clutter.
                InputDeviceKind.Touch => string.Empty,
                InputDeviceKind.Gamepad => loc.Get("ui.hud.hint_pad"),
                _ => loc.Get("ui.hud.hint").Replace("{feedback_key}", FeedbackUi.HotkeyName),
            };

            // Creative/Sandbox worlds only: the one control the hint can't afford to omit, because flight has
            // no other tell. Appended rather than baked into ui.hud.hint so Explorer worlds stay uncluttered.
            if (Game.CanFly && _hint.text.Length > 0)
            {
                _hint.text += " · " + loc.Get("ui.hud.hint_fly");
            }

            // Holding a rotatable block (a crafted shape or furniture): surface the rotate control (#863) —
            // nothing else in the game ever said the key exists. Appended only while it applies, like the
            // fly hint, so the always-on line stays short.
            if (Game.HoldingRotatableBlock && _hint.text.Length > 0)
            {
                _hint.text += " · " + loc.Get("ui.hud.hint_rotate")
                    .Replace("{key}", InputMap.Glyph(InputAction.RotateShape));
            }

            // Prompts — on-foot only. While piloting/EVA the flight view draws its own prompts, so don't leak
            // a stale on-foot "Use: Cockpit" into the centre of the space view (you reach the cockpit/helm on
            // foot inside the ship; from the flight view you press F to step inside).
            string prompt = string.Empty;
            if (!Game.SpaceViewActive)
            {
                if (!string.IsNullOrEmpty(Game.NearbyStation))
                {
                    // Inside the ship while it floats in space, the cockpit reads as the helm (take it to fly again).
                    string stationKey = (Game.NearbyStation == "cockpit" && Game.LoadingPlanetType == "ship_interior")
                        ? "ui.station.helm"
                        : $"ui.station.{Game.NearbyStation}";
                    prompt = $"{loc.Get("ui.hud.use")}: {loc.Get(stationKey)}";
                }
                else if (HoldingScanner())
                {
                    prompt = loc.Get("ui.scan.use_hint");
                }
            }

            _prompt.text = prompt;
            // Only actionable on foot (#751): PlayerController never polls the loot key while the
            // flight view is up or a speeder is driven, so showing the prompt there was a dead key.
            _loot.text = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.InSpeeder) ? string.Empty : LootText(loc);

            RefreshScan(loc);
            RefreshWreck(loc);
            RefreshShipRepair(loc);
            RefreshTaming(loc);
            RefreshSpeeder(loc);
        }

        /// <summary>Vehicle HUD while driving a hover speeder: integrity + energy gauges (colour-graded), the
        /// current speed and the F/R drive prompt. Hidden on foot.</summary>
        private void RefreshSpeeder(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var s = Game.DrivenSpeeder;
            bool active = !string.IsNullOrEmpty(Game.InSpeeder) && s != null;
            if (_speederPanel.activeSelf != active)
            {
                _speederPanel.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            _speederTitle.text = loc.Get("item.speeder.name");
            _speederSpeed.text = $"{Mathf.RoundToInt(Mathf.Abs(Game.SpeederSpeed))} m/s";

            float hullFrac = s.HullMax > 0 ? s.Hull / s.HullMax : 0f;
            _speederHull.fillAmount = Mathf.Clamp01(hullFrac);
            _speederHull.color = hullFrac > 0.5f ? new Color(0.4f, 0.85f, 0.5f)
                : (hullFrac > 0.25f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.95f, 0.35f, 0.3f));
            _speederHullLabel.text = $"{loc.Get("hud.speeder.integrity")}  {Mathf.RoundToInt(s.Hull)}";

            float fuelFrac = s.FuelMax > 0 ? s.Fuel / s.FuelMax : 0f;
            _speederFuel.fillAmount = Mathf.Clamp01(fuelFrac);
            _speederFuel.color = fuelFrac > 0.2f ? new Color(0.4f, 0.8f, 1f) : new Color(0.95f, 0.5f, 0.2f);
            _speederFuelLabel.text = $"{loc.Get("hud.speeder.fuel")}  {Mathf.RoundToInt(s.Fuel)}";

            // Key letters come from the live bindings (keyboard key or pad glyph), not hardcoded F/R.
            string hint = $"{InputMap.Glyph(InputAction.SpeederExit)}: {loc.Get("hud.speeder.exit")}"
                        + $"  ·  {InputMap.Glyph(InputAction.SpeederRefuel)}: {loc.Get("hud.speeder.refuel")}";
            if (s.Fuel <= 0.01f)
            {
                hint = loc.Get("hud.speeder.nofuel");
            }

            _speederHint.text = hint;
        }

        /// <summary>A horizontal fill bar (dark track + a coloured fill) for the vehicle gauges.</summary>
        private Image MakeBar(Transform parent, float x, float y, float w, float h)
        {
            UiKit.AddImage(parent, x, y, w, h, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f, 0.9f));
            var fill = UiKit.AddImage(parent, x, y, w, h, UiKit.SolidSprite, Color.white);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            return fill;
        }

        // --- vitals ---

        private VitalRow MakeVital(Transform parent, float x, float y, string key)
        {
            var go = new GameObject("Vital_" + key, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, 200, 16);
            UiKit.AddImage(go.transform, 22, 0, 178, 16, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f, 0.9f));
            var fill = UiKit.AddImage(go.transform, 22, 0, 178, 16, UiKit.SolidSprite, Color.white);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            var label = UiKit.AddText(go.transform, 28, 0, 172, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft);
            return new VitalRow { Fill = fill, Label = label, Go = go };
        }

        private void SetVital(int i, string label, float value, float frac, Color color, bool active)
        {
            var v = _vitals[i];
            if (v.Go.activeSelf != active) v.Go.SetActive(active);
            // Low-value warning state with hysteresis (#753): trips below 10 %, clears above 15 %, so a
            // value hovering at the threshold doesn't flicker the alarm on and off. Suppressed while dead
            // (every bar bottoms out on the respawn screen — blinking them all helps nobody).
            v.Warn = active && Game.Health > 0f && (frac < 0.10f || (v.Warn && frac < 0.15f));
            v.BaseColor = color;
            _vitals[i] = v;
            if (!active) return;
            v.Fill.color = color;
            v.Fill.fillAmount = Mathf.Clamp01(frac);
            v.Label.text = $"{label}  {Mathf.RoundToInt(value)}";
        }

        private static readonly Color VitalWarnRed = new(1f, 0.25f, 0.2f);

        /// <summary>Blinks any vital row below its warning threshold toward red and drives the shared
        /// low-vitals beep (#753). Runs per frame — the 10 Hz Refresh would step the blink. Oxygen keeps
        /// its own dedicated 25 % alarm (vignette + beeps), so its row blinks but doesn't double-beep.</summary>
        private void UpdateLowVitalWarnings(float dt)
        {
            bool beepWorthy = false;
            for (int i = 0; i < _vitals.Length; i++)
            {
                var v = _vitals[i];
                if (v.Fill == null || !v.Warn || !v.Go.activeSelf) continue;
                float k = UiKit.ReducedMotion ? 1f : Mathf.PingPong(Time.time * 2.4f, 1f);
                v.Fill.color = Color.Lerp(v.BaseColor, VitalWarnRed, 0.35f + 0.65f * k);
                if (i != 1) beepWorthy = true; // 1 = oxygen — covered by UpdateOxygenAlarm
            }

            if (!beepWorthy)
            {
                _lowVitalBeepTimer = 0f; // first beep fires immediately when a bar next runs low
                return;
            }

            _lowVitalBeepTimer -= dt;
            if (_lowVitalBeepTimer <= 0f)
            {
                _lowVitalBeepTimer = 2.5f;
                ClientAudio.Instance?.Cue("vitals_warning", 0.5f);
            }
        }

        // --- hotbar ---

        private void RefreshHotbar(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            // No on-foot hotbar while flying the ship — you're piloting, not holding hand tools. BUT on an EVA
            // the hotbar IS shown: you float in space and build/mine on structures from your inventory, so you
            // need to see + pick the held block/tool (B?).
            // Observer mode has no hotbar either (issue #487): an observer carries nothing and may not build,
            // so showing slots would advertise actions the server drops.
            bool hide = ((Game.SpaceViewActive || Game.InSpace) && !Game.InEva)
                        || !string.IsNullOrEmpty(Game.InSpeeder)
                        || Game.Spectating;
            if (_hotbarRoot != null && _hotbarRoot.activeSelf == hide)
            {
                _hotbarRoot.SetActive(!hide);
            }

            if (hide)
            {
                return;
            }

            // A quick pop on the newly selected slot's icon (skipped for the initial selection); the persistent
            // scale-up + bright ring (UiKit.StyleQuickSlot) is the steady "this is selected" cue.
            int selNow = Game.SelectedHotbarSlot;
            if (selNow != _lastSelSlot && _lastSelSlot >= 0 && selNow >= 0 && selNow < Slots && _hotbar[selNow].Icon != null)
            {
                var ico = _hotbar[selNow].Icon.gameObject;
                (ico.GetComponent<SlotTick>() ?? ico.AddComponent<SlotTick>()).Restart();
            }

            _lastSelSlot = selNow;

            for (int i = 0; i < Slots; i++)
            {
                var s = _hotbar[i];
                bool sel = i == Game.SelectedHotbarSlot;
                UiKit.StyleQuickSlot(s, sel);
                s.Num.text = (i + 1).ToString();

                string item = Game.ItemInSlot(i);
                if (string.IsNullOrEmpty(item))
                {
                    s.Icon.enabled = false;
                    s.Name.text = string.Empty;
                    s.Count.text = string.Empty;
                    continue;
                }

                // Stack size top-right (#744): only for real stacks, so tools (max stack 1) stay clean.
                int count = Game.CountInSlot(i);
                s.Count.text = count > 1 ? count.ToString() : string.Empty;

                var blockDef = Game.Content?.GetBlock(item);
                if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
                {
                    blockDef = Game.Content?.GetBlock(pb); // a seed etc. shows the tile of the block it places
                }

                // A shaped block (sphere/pyramid/…) shows a form-specific icon instead of a plain cube tile (#125).
                int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(item);
                Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                    ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape, Game.CustomShapes)
                    : null;

                // A painted item (own texture, p-field in the key) shows its design — the texture IS the
                // point of that stack, so it beats the form silhouette. Unresolved/wiped ids fall through.
                int design = BlocksBeyondTheStars.Shared.State.ItemKey.Design(item);
                Texture2D itemTex;
                if (design != 0 && Game.PaintAtlas != null && Game.PaintAtlas.TryGetUv(design, out var designUv))
                {
                    s.Icon.texture = Game.PaintAtlas.Texture;
                    s.Icon.uvRect = designUv;
                }
                else if (shapeTex != null)
                {
                    s.Icon.texture = shapeTex;
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }
                else if (blockDef != null && Game.Atlas != null)
                {
                    s.Icon.texture = Game.Atlas.Texture;
                    s.Icon.uvRect = Game.Atlas.TileUv(blockDef.NumericId.Value);
                }
                else if ((itemTex = IconResolver.ItemTexture(item)) != null)
                {
                    s.Icon.texture = itemTex; // a generated content-styled icon
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }
                else
                {
                    var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                    s.Icon.texture = IconFactory.ForItem(item, kind);
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }

                s.Icon.color = IconResolver.Tint(item, Game); // toxic consumables read green
                s.Icon.enabled = true;
                // The held slot shows its full name (brighter); the rest stay short so the row reads at a glance.
                string name = loc.Get($"item.{item}.name");
                s.Name.text = sel ? name : (name.Length > 10 ? name.Substring(0, 9) + "…" : name);
                s.Name.color = sel ? UiKit.Cyan : UiKit.TextCol;
            }
        }

        // --- pickup feed (#745) ---

        /// <summary>Drains the queued inventory gains into the feed rows and ages/fades them. While the
        /// hotbar is hidden (piloting/driving/observer) gains are dropped, not saved up — announcing a
        /// pile of stale pickups after landing would be noise, not feedback.</summary>
        private void UpdatePickupFeed(float dt)
        {
            if (_hotbarRoot == null || !_hotbarRoot.activeSelf)
            {
                Game.PickupGains.Clear();
                if (_pickupRows.Count > 0)
                {
                    foreach (var row in _pickupRows)
                    {
                        Destroy(row.Go);
                    }

                    _pickupRows.Clear();
                }

                return;
            }

            bool changed = false;
            while (Game.PickupGains.Count > 0)
            {
                var (item, gained) = Game.PickupGains.Dequeue();
                var row = _pickupRows.Find(r => r.Item == item);
                if (row == null)
                {
                    if (_pickupRows.Count >= PickupMaxRows)
                    {
                        Destroy(_pickupRows[0].Go); // full: the oldest row yields
                        _pickupRows.RemoveAt(0);
                    }

                    row = MakePickupRow(item);
                    _pickupRows.Add(row);
                }

                row.Count += gained;
                row.Ttl = PickupLife;
                row.Label.text = $"+{row.Count} {Game.Localizer.Get($"item.{item}.name")}";
                changed = true;
            }

            for (int i = _pickupRows.Count - 1; i >= 0; i--)
            {
                var row = _pickupRows[i];
                row.Ttl -= dt;
                if (row.Ttl <= 0f)
                {
                    Destroy(row.Go);
                    _pickupRows.RemoveAt(i);
                    changed = true;
                    continue;
                }

                // Steady, then a short fade-out at the end; reduced motion holds full alpha until removal.
                row.Fade.alpha = UiKit.ReducedMotion ? 1f : Mathf.Clamp01(row.Ttl / PickupFadeTime);
            }

            if (changed)
            {
                // Stack upward from the backplate: newest row sits closest to the hotbar.
                for (int i = 0; i < _pickupRows.Count; i++)
                {
                    float y = _pickupAnchorY - (_pickupRows.Count - i) * PickupRowH;
                    UiKit.Place(_pickupRows[i].Go, _pickupRightX - PickupRowW, y, PickupRowW, PickupRowH);
                }
            }
        }

        /// <summary>One feed row: right-aligned "+n name" text with the item's icon at the far right.</summary>
        private PickupRow MakePickupRow(string item)
        {
            var go = new GameObject("pickup_row", typeof(RectTransform));
            go.transform.SetParent(_hotbarRoot.transform, false);
            var fade = go.AddComponent<CanvasGroup>();
            fade.blocksRaycasts = false;
            fade.interactable = false;

            var iconGo = new GameObject("icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            UiKit.Place(iconGo, PickupRowW - 24f, 3f, 20f, 20f);
            var raw = iconGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            SetItemIcon(raw, item);

            var label = UiKit.AddText(go.transform, 0f, 3f, PickupRowW - 30f, 20f, string.Empty, 15,
                UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.AddOutline(label);

            return new PickupRow { Item = item, Go = go, Fade = fade, Label = label };
        }

        /// <summary>Resolves an item key to its icon the same way the hotbar does (atlas tile for blocks
        /// and seeds, generated PNG or procedural fallback for pure items) — minus the shaped-block special
        /// case, since drops in the feed are plain base items.</summary>
        private void SetItemIcon(RawImage img, string item)
        {
            var blockDef = Game.Content?.GetBlock(item);
            if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
            {
                blockDef = Game.Content?.GetBlock(pb);
            }

            Texture2D itemTex;
            if (blockDef != null && Game.Atlas != null)
            {
                img.texture = Game.Atlas.Texture;
                img.uvRect = Game.Atlas.TileUv(blockDef.NumericId.Value);
            }
            else if ((itemTex = IconResolver.ItemTexture(item)) != null)
            {
                img.texture = itemTex;
                img.uvRect = new Rect(0, 0, 1, 1);
            }
            else
            {
                var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
                img.texture = IconFactory.ForItem(item, kind);
                img.uvRect = new Rect(0, 0, 1, 1);
            }

            img.color = IconResolver.Tint(item, Game);
        }

        // --- research toast (#763) ---

        /// <summary>Drives the "new research available" toast: pops the next queued blueprint when idle
        /// and the HUD is visible, animates the pop/glow/shine, then fades out. Queued keys survive
        /// menus and flight — the announcement is rare enough to arrive late rather than be dropped.</summary>
        private void UpdateResearchToast(float dt)
        {
            if (_researchAge >= 0f && _researchGo == null)
            {
                _researchAge = -1f; // the canvas (and toast) got torn down mid-toast, e.g. on a world change
            }

            if (_researchAge < 0f)
            {
                if (Game.ResearchAvailable.Count == 0 || _canvas == null || !_canvas.enabled)
                {
                    return;
                }

                StartResearchToast(Game.ResearchAvailable.Dequeue());
            }

            _researchAge += dt;
            if (_researchAge >= ResearchHold + ResearchFade)
            {
                _researchAge = -1f;
                _researchGo.SetActive(false);
                return;
            }

            float alpha = _researchAge >= ResearchHold
                ? 1f - (_researchAge - ResearchHold) / ResearchFade
                : (UiKit.ReducedMotion ? Mathf.Clamp01(_researchAge / 0.2f) : 1f);
            _researchFade.alpha = alpha;

            if (UiKit.ReducedMotion)
            {
                _researchRect.localScale = Vector3.one;
                _researchGlow.color = new Color(0.4f, 0.82f, 1f, 0.22f * alpha);
                _researchShine.gameObject.SetActive(false);
                return;
            }

            // Pop-in: overshoot past 1.0 and settle by the end of the pop window (sin term ends at 0).
            float p = Mathf.Clamp01(_researchAge / ResearchPop);
            _researchRect.localScale = Vector3.one * (Mathf.Lerp(0.75f, 1f, p) + Mathf.Sin(p * Mathf.PI) * 0.08f);

            // Soft glow pulsing behind the icon for the toast's whole life.
            float pulse = Mathf.Sin(_researchAge * 4.2f) * 0.5f + 0.5f;
            _researchGlow.color = new Color(0.4f, 0.82f, 1f, (0.14f + pulse * 0.24f) * alpha);
            float gs = 1f + pulse * 0.18f;
            _researchGlow.rectTransform.localScale = new Vector3(gs, gs, 1f);

            // One diagonal shine sweep across the panel just after the pop (clipped by the RectMask2D).
            float sweep = (_researchAge - 0.15f) / 0.6f;
            bool sweeping = sweep >= 0f && sweep <= 1f;
            _researchShine.gameObject.SetActive(sweeping);
            if (sweeping)
            {
                var ap = _researchShine.anchoredPosition;
                ap.x = Mathf.Lerp(-80f, ResearchW + 40f, sweep);
                _researchShine.anchoredPosition = ap;
            }
        }

        /// <summary>Fills and shows the toast for one blueprint key and plays the discovery chime.</summary>
        private void StartResearchToast(string bpKey)
        {
            EnsureResearchToast();
            var loc = Game.Localizer;
            _researchHead.text = loc.Get("ui.tech.research_available").ToUpperInvariant();
            _researchName.text = loc.Get($"blueprint.{bpKey}.name");
            SetBlueprintIcon(_researchIcon, bpKey);
            _researchGo.SetActive(true);
            _researchAge = 0f;
            _researchFade.alpha = UiKit.ReducedMotion ? 0f : 1f;
            _researchRect.localScale = UiKit.ReducedMotion ? Vector3.one : Vector3.one * 0.75f;
            ClientAudio.Instance?.Cue("research_available", 0.9f);
        }

        /// <summary>Builds the toast hierarchy once: panel + glow disc + icon + two text lines + the
        /// shine bar. Center-pivoted (unlike the usual top-left Place) so the pop scales in place.</summary>
        private void EnsureResearchToast()
        {
            if (_researchGo != null)
            {
                return;
            }

            _researchGo = new GameObject("research_toast", typeof(RectTransform));
            _researchGo.transform.SetParent(_canvas.transform, false);
            _researchRect = UiKit.Place(_researchGo, (W - ResearchW) / 2f, ResearchY, ResearchW, ResearchH);
            _researchRect.pivot = new Vector2(0.5f, 0.5f);
            _researchRect.anchoredPosition += new Vector2(ResearchW / 2f, -ResearchH / 2f);
            _researchFade = _researchGo.AddComponent<CanvasGroup>();
            _researchFade.blocksRaycasts = false;
            _researchFade.interactable = false;

            var bg = _researchGo.AddComponent<Image>();
            bg.sprite = UiKit.PanelSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.05f, 0.12f, 0.24f, 0.9f);
            bg.raycastTarget = false;
            _researchGo.AddComponent<RectMask2D>(); // clips the shine sweep to the panel

            _researchGlow = UiKit.AddImage(_researchGo.transform, 8f, 8f, 70f, 70f, UiKit.DiscSprite, new Color(0.4f, 0.82f, 1f, 0.3f));
            var glowRt = _researchGlow.rectTransform; // centre-pivot so the pulse grows around the icon
            glowRt.pivot = new Vector2(0.5f, 0.5f);
            glowRt.anchoredPosition += new Vector2(35f, -35f);
            var iconGo = new GameObject("icon", typeof(RectTransform));
            iconGo.transform.SetParent(_researchGo.transform, false);
            UiKit.Place(iconGo, 15f, 15f, 56f, 56f);
            _researchIcon = iconGo.AddComponent<RawImage>();
            _researchIcon.raycastTarget = false;

            _researchHead = UiKit.AddText(_researchGo.transform, 92f, 16f, ResearchW - 104f, 22f, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _researchName = UiKit.AddText(_researchGo.transform, 92f, 40f, ResearchW - 104f, 30f, string.Empty, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(_researchHead);
            UiKit.AddOutline(_researchName);

            var shine = UiKit.AddImage(_researchGo.transform, -80f, -20f, 46f, ResearchH + 40f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.16f));
            shine.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            _researchShine = shine.rectTransform;
            _researchShine.gameObject.SetActive(false);

            _researchGo.SetActive(false);
        }

        /// <summary>Icon for a blueprint key: most blueprint keys ARE item keys (heal_tank, binoculars…)
        /// and reuse the item resolution; module/expansion blueprints fall back to their tech-category
        /// sprite, tinted cyan like the tech tree's own nodes.</summary>
        private void SetBlueprintIcon(RawImage img, string bpKey)
        {
            if (Game.Content?.GetItem(bpKey) != null || Game.Content?.GetBlock(bpKey) != null)
            {
                SetItemIcon(img, bpKey);
                return;
            }

            string cat = Game.Content != null && Game.Content.Blueprints.TryGetValue(bpKey, out var bp) && bp.Category == "ShipExpansion"
                ? "cat_modules"
                : "cat_tech";
            var sprite = UiKit.Icon(cat);
            if (sprite != null)
            {
                img.texture = sprite.texture;
                img.uvRect = new Rect(0, 0, 1, 1);
                img.color = UiKit.Cyan;
            }
            else
            {
                img.texture = IconFactory.ForItem(bpKey, BlocksBeyondTheStars.Shared.Definitions.ToolKind.None);
                img.uvRect = new Rect(0, 0, 1, 1);
                img.color = Color.white;
            }
        }

        // --- compass ---

        /// <summary>A compass blip: a plain square, or a white-ink map icon when <paramref name="icon"/>
        /// is given (the waypoint — a shaped, larger blip so it never reads as just another beacon, #592).</summary>
        private RectTransform Blip(Transform parent, Color color, float size, string icon = null)
        {
            var sprite = icon != null ? UiKit.Icon(icon) : null;
            var img = UiKit.AddImage(parent, 0, 0, size, size, sprite != null ? sprite : UiKit.SolidSprite, color);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private void RefreshCompass()
        {
            // The round compass is on-foot/EVA navigation (north arrow, ship + waypoint + beacon blips). While
            // piloting the ship you ARE the ship and the flight view draws its own radar, so hide it then; on an
            // EVA it stays — the ship blip + distance is how you float your way back to the hull.
            bool piloting = Game.SpaceViewActive && !Game.InEva;
            var compassGo = _compassParent != null ? _compassParent.gameObject : null;
            if (compassGo != null && compassGo.activeSelf == piloting)
            {
                compassGo.SetActive(!piloting);
            }

            if (piloting)
            {
                return;
            }

            const float radius = 44f;
            PlaceBlip(_compassWp, Game.Waypoint.HasValue, Game.Waypoint ?? Vector3.zero, radius, out float wpDist);
            PlaceBlip(_compassShip, Game.ShipPosition.HasValue, Game.ShipPosition ?? Vector3.zero, radius, out float dist);

            // This runs per frame, so only rebuild the distance strings when the rounded value changed.
            int distNow = Game.ShipPosition.HasValue ? Mathf.RoundToInt(dist) : -1;
            if (distNow != _lastCompassDist)
            {
                _lastCompassDist = distNow;
                _compassDist.text = distNow >= 0 ? $"{distNow} m" : string.Empty;
            }

            // Amber (matching the waypoint blip) vs the cyan ship line above — colour is the label,
            // no glyph prefix (the UI font has no guaranteed ✛).
            int wpDistNow = Game.Waypoint.HasValue ? Mathf.RoundToInt(wpDist) : -1;
            if (wpDistNow != _lastCompassWpDist)
            {
                _lastCompassWpDist = wpDistNow;
                _compassWpDist.text = wpDistNow >= 0 ? $"{wpDistNow} m" : string.Empty;
            }

            // Player-placed beacons (item 37): amber blips, pooled since their count varies.
            var beacons = Game.Beacons;
            int bn = beacons?.Length ?? 0;
            for (int i = 0; i < bn; i++)
            {
                if (i >= _compassBeacons.Count)
                {
                    _compassBeacons.Add(Blip(_compassParent, new Color(1f, 0.72f, 0.2f), 6f));
                }

                PlaceBlip(_compassBeacons[i], true, new Vector3(beacons[i].X, beacons[i].Y, beacons[i].Z), radius);
            }

            for (int i = bn; i < _compassBeacons.Count; i++)
            {
                _compassBeacons[i].gameObject.SetActive(false);
            }
        }

        private void PlaceBlip(RectTransform blip, bool active, Vector3 target, float radius)
            => PlaceBlip(blip, active, target, radius, out _);

        private void PlaceBlip(RectTransform blip, bool active, Vector3 target, float radius, out float dist)
        {
            dist = 0f;
            if (blip == null) return;
            if (!active) { blip.gameObject.SetActive(false); return; }
            float dx = target.x - Game.PlayerPosition.x, dz = target.z - Game.PlayerPosition.z;
            dist = Mathf.Sqrt(dx * dx + dz * dz);
            float ang = (Mathf.Atan2(dx, dz) * Mathf.Rad2Deg - Game.PlayerYaw) * Mathf.Deg2Rad;
            // Log-scaled radius (#592): the old linear dist*1.2 pinned everything past ~37 m to the rim,
            // so approach progress was unreadable. Log keeps direction AND lets "getting closer" show:
            // ~20 px at 40 m, ~34 px at 400 m, rim only past ~2 km.
            float r = 8f + (radius - 8f) * Mathf.Clamp01(Mathf.Log10(1f + dist / 8f) / Mathf.Log10(1f + 2000f / 8f));
            blip.gameObject.SetActive(true);
            blip.anchoredPosition = new Vector2(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r); // +Y up = north
        }

        private void RefreshTimeOfDay(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var env = Game.Environment;
            // Day/night clock and gravity are planet-surface readings — meaningless in space, so the panel
            // stays hidden while piloting (the cabin is climate-controlled anyway). On an EVA the suit's
            // hull-temperature reading IS meaningful (#668: sun-dependent vacuum value from the server), so
            // show a temperature-only readout there instead of dropping the panel.
            if (env == null || Game.SpaceViewActive)
            {
                _todText.transform.parent.gameObject.SetActive(false);
                return;
            }

            if (Game.OnFootInSpace)
            {
                _todText.transform.parent.gameObject.SetActive(true);
                _todMarker.gameObject.SetActive(false);
                _todText.text = ColoredTemp(env.Temperature);
                return;
            }

            _todText.transform.parent.gameObject.SetActive(true);
            _todMarker.gameObject.SetActive(true);
            float t = Game.LocalTimeOfDay; // the player's local time (longitude-shifted), already 0..1
            bool day = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) > 0f;
            float nextEdge = day ? 0.75f : (t < 0.25f ? 0.25f : 1.25f);
            float frac = nextEdge - t; if (frac < 0f) frac += 1f;
            float secs = frac * Mathf.Max(1f, env.DayLengthSeconds);
            int mm = Mathf.FloorToInt(secs / 60f), ss = Mathf.FloorToInt(secs % 60f);
            string tempStr = env.Temperature <= -900f ? "—" : ColoredTemp(env.Temperature);
            // Show this world's gravity (e.g. "0.6 g") only when it notably differs from Earth-like, so normal
            // worlds stay uncluttered. Hidden in space (on-foot zero-g) where gravity doesn't apply.
            string gravStr = env.GravityFactor > 0.01f && Mathf.Abs(env.GravityFactor - 1f) > 0.05f && !Game.OnFootInSpace
                ? "  " + string.Format(loc.Get("ui.hud.gravity"), env.GravityFactor.ToString("0.0"))
                : string.Empty;
            _todText.text = $"{(day ? loc.Get("ui.hud.day") : loc.Get("ui.hud.night")).ToUpperInvariant()}  {mm}:{ss:00}  {tempStr}{WeatherChip(loc, env)}{gravStr}";
            _todMarker.anchoredPosition = new Vector2(10 + 150 * t, _todMarker.anchoredPosition.y);
        }

        /// <summary>
        /// The weather chip (#900): a small icon + the localized state name next to the temperature, shown
        /// only when there is actually weather to name — a clear sky says nothing, so a calm world stays as
        /// uncluttered as it was. Before this the game never named its own weather anywhere.
        /// </summary>
        private string WeatherChip(BlocksBeyondTheStars.Shared.Localization.Localizer loc,
            BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env)
        {
            string state = env.Weather;
            if (string.IsNullOrEmpty(state) || state == "clear" || env.SpaceSky)
            {
                return string.Empty;
            }

            string icon = state switch
            {
                "clouds" => "☁",
                "rain" or "drizzle" => "☂",
                "storm" => "⚡",
                "blizzard" or "gale" => "❄",
                "fog" or "ground_fog" => "≈",
                "heatwave" => "☀",
                "acid_rain" => "☣",
                "ion_storm" => "⚡",
                "meteor_shower" => "★",
                "ember_fall" => "▲",
                "spore_bloom" => "❋",
                _ => "•",
            };

            // Violent and exotic weather is worth a warning colour; the mild states stay plain.
            string colour = env.WeatherFamily switch
            {
                "violent" => "#ff7439",
                "exotic" => "#c58bff",
                "obscuring" => "#9fb4c8",
                _ => null,
            };

            string label = $"{icon} {loc.Get("weather." + state)}";
            return "  " + (colour is null ? label : $"<color={colour}>{label}</color>");
        }

        /// <summary>Temperature readout tinted by comfort (#666): icy blue below the band, hot orange above
        /// it, plain inside — matching where the suit actually starts spending energy (−5…40 °C ±5 grace).</summary>
        private static string ColoredTemp(float tempC)
        {
            string s = $"{Mathf.RoundToInt(tempC)}°C";
            if (tempC < -10f) { return $"<color=#6fd8ff>{s}</color>"; }
            if (tempC > 45f) { return $"<color=#ff7439>{s}</color>"; }
            return s;
        }

        /// <summary>Optional comfort readout: the current session's real-world playtime, plus this world's
        /// saved total. Hidden entirely unless the player enabled it in settings. Counts real wall-clock so it
        /// keeps ticking while paused in menus.</summary>
        private void RefreshPlaytime(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            if (_playtimePanel == null)
            {
                return;
            }

            bool show = Settings != null && Settings.ShowSessionTime;
            if (_playtimePanel.activeSelf != show)
            {
                _playtimePanel.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            _playtimeText.text = $"{loc.Get("ui.hud.playtime")}  {FormatDuration((long)Game.SessionSeconds)}"
                                 + $"  ·  {loc.Get("ui.hud.playtime_total")}  {FormatDuration(Game.TotalPlaytimeSeconds)}";
        }

        /// <summary>Formats a span of seconds as <c>H:MM:SS</c> (or <c>M:SS</c> under an hour) for the HUD.</summary>
        private static string FormatDuration(long totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            long h = totalSeconds / 3600;
            long m = (totalSeconds % 3600) / 60;
            long s = totalSeconds % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        private void RefreshScan(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var scan = Game.LastScan;
            // Pinned while the scanner is still in hand (you're actively surveying — the readout is the
            // point), otherwise it lingers ScanHoldSeconds after the scan and fades out (#482).
            bool show = scan != null && (HoldingScanner() || Time.time - Game.LastScanAt <= ScanHoldSeconds);
            if (_scanPanel.activeSelf != show)
            {
                _scanPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_scanPanel); }
            }

            if (!show) return;
            _scanSubject.text = $"{loc.Get("ui.scan.title").ToUpperInvariant()}: {ScanSubjectName(loc, scan.Subject)}";
            _scanInfo.text = ScanInfoText(loc, scan);
            // The threat WORD comes from a locale key now; `scan.Threat` is the legacy English fallback (#484).
            string threat = !string.IsNullOrEmpty(scan.ThreatKey) ? loc.Get(scan.ThreatKey) : scan.Threat;
            _scanThreat.gameObject.SetActive(!string.IsNullOrEmpty(threat) && threat != "—");
            _scanThreat.text = $"{loc.Get("ui.scan.threat")}: {threat}";

            // A first-time discovery shows its knowledge GAIN highlighted; re-scans just show the total.
            if (scan.FirstTime && scan.KnowledgeGained > 0)
            {
                _scanKnow.color = new Color(0.45f, 1f, 0.6f);
                _scanKnow.text = $"{loc.Get("ui.scan.first_time")}  +{scan.KnowledgeGained}  ({loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal})";
            }
            else
            {
                _scanKnow.color = UiKit.TextCol;
                _scanKnow.text = $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}";
            }
        }

        /// <summary>Builds the scan panel's description line from the STRUCTURED payload (#484): a creature's
        /// habitat/activity/temperament traits, or a yield/resource list with localized item names, or a
        /// single remark key. Falls back to the legacy English <see cref="ScanResult.Info"/> only when the
        /// server is older than the structured fields.</summary>
        private string ScanInfoText(BlocksBeyondTheStars.Shared.Localization.Localizer loc, BlocksBeyondTheStars.Networking.Messages.ScanResult scan)
        {
            var traits = scan.TraitKeys;
            if (traits != null && traits.Length > 0)
            {
                var parts = new string[traits.Length];
                for (int i = 0; i < traits.Length; i++)
                {
                    parts[i] = loc.Get(traits[i]);
                }

                return string.Join("  ·  ", parts);
            }

            var drops = scan.Drops;
            if (drops != null && drops.Length > 0)
            {
                var parts = new string[drops.Length];
                for (int i = 0; i < drops.Length; i++)
                {
                    // Count 0 = a resource TYPE with no quantity (asteroid scan) — no "×n" suffix then.
                    string name = ItemOrBlockName(loc, drops[i].Item);
                    parts[i] = drops[i].Count > 0 ? $"{name} ×{drops[i].Count}" : name;
                }

                string label = loc.Get(scan.Kind == "asteroid" ? "ui.scan.resources" : "ui.scan.yield");
                return $"{label}: {string.Join(", ", parts)}";
            }

            if (!string.IsNullOrEmpty(scan.InfoKey))
            {
                return loc.Get(scan.InfoKey);
            }

            return scan.Info; // pre-#484 server
        }

        /// <summary>Localized name for an item key, falling back to the block table (drop lists mix both).</summary>
        private string ItemOrBlockName(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key)
        {
            if (Game.Content?.GetItem(key) is { } item)
            {
                return loc.Get(item.NameKey);
            }

            return Game.Content?.GetBlock(key) is { } block ? loc.Get(block.NameKey) : key;
        }

        /// <summary>Resolves a scan subject key to a readable, localized name (block / item / creature)
        /// so the readout says what it is ("Stone") rather than the raw key ("stone").</summary>
        private string ScanSubjectName(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return key;
            }

            if (Game.Content?.GetBlock(key) is { } b)
            {
                return loc.Get(b.NameKey);
            }

            if (Game.Content?.GetItem(key) is { } it)
            {
                return loc.Get(it.NameKey);
            }

            // Not a block/item key: the server already resolved creatures/flora/trees to their coined,
            // language-neutral display name (CreatureSpecies.Name etc.) and asteroids to a plain label, so
            // the subject IS the display name — show it as-is. (A real per-species localization key is
            // honoured if one exists; Localizer.Get returns "[key]" for a missing key, so we must probe
            // with Has() rather than inspect the returned string.)
            string creatureKey = $"creature.{key}.name";
            if (loc.Has(creatureKey))
            {
                return loc.Get(creatureKey);
            }

            // Generic scan subjects that are neither content nor a species — currently just "asteroid" (#484).
            string subjectKey = $"ui.scan.subject.{key}";
            return loc.Has(subjectKey) ? loc.Get(subjectKey) : key;
        }

        private void RefreshWreck(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var wreck = Game.Wreck;
            bool show = wreck != null;
            if (_wreckPanel.activeSelf != show)
            {
                _wreckPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_wreckPanel); }
            }

            if (!show) return;
            _wreckName.text = wreck.WreckName;
            int done = wreck.Total - wreck.Remaining;
            _wreckBar.fillAmount = wreck.Total > 0 ? done / (float)wreck.Total : 0f;
            _wreckProg.text = $"{loc.Get("ui.wreck.progress")}  {done}/{wreck.Total}";
            bool claim = wreck.Claimable;
            _wreckClaim.gameObject.SetActive(claim);
            if (claim)
            {
                var t = _wreckClaim.GetComponentInChildren<Text>();
                if (t != null) t.text = loc.Get("ui.action.claim");
            }

            // While there are breaches left, tell the player how to repair + which blocks are still needed.
            _wreckHint.gameObject.SetActive(!claim);
            if (!claim)
            {
                string needs = string.Empty;
                if (!string.IsNullOrEmpty(wreck.Needs))
                {
                    var keys = wreck.Needs.Split(',');
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i] = loc.Get($"block.{keys[i]}.name");
                    }

                    needs = "  " + string.Join(", ", keys);
                }

                _wreckHint.text = loc.Get("ui.wreck.repair_hint") + needs;
            }
        }

        private void RefreshShipRepair(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var sr = Game.ShipRepair;
            bool show = sr != null && sr.NeedsRepair;
            if (_shipRepairPanel.activeSelf != show)
            {
                _shipRepairPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_shipRepairPanel); }
            }

            if (!show) return;

            _shipRepairTitle.text = loc.Get("ui.shiprepair.title");
            _shipRepairBar.fillAmount = sr.HullMax > 0f ? Mathf.Clamp01(sr.Hull / sr.HullMax) : 1f;
            _shipRepairProg.text = $"{loc.Get("ui.shiprepair.hull")}  {(int)sr.Hull}/{(int)sr.HullMax}";

            // List the materials the full repair needs (item:count pairs from the server), localized.
            string needs = string.Empty;
            if (!string.IsNullOrEmpty(sr.Needs))
            {
                var parts = sr.Needs.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    var kv = parts[i].Split(':');
                    string name = loc.Get($"item.{kv[0]}.name");
                    if (name.StartsWith("item.")) { name = loc.Get($"block.{kv[0]}.name"); } // fall back for raw block keys
                    parts[i] = kv.Length > 1 ? $"{name} ×{kv[1]}" : name;
                }

                needs = "  " + string.Join(", ", parts);
            }

            string cells = sr.MissingCells > 0 ? $"  ({sr.MissingCells} {loc.Get("ui.shiprepair.cells")})" : string.Empty;
            _shipRepairHint.text = loc.Get("ui.shiprepair.hint") + needs + cells;

            var t = _shipRepairBtn.GetComponentInChildren<Text>();
            if (t != null) { t.text = loc.Get("ui.shiprepair.repair"); }
        }

        private void RefreshTaming(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var st = Game.TameState;
            bool show = st != null && st.Active && !Game.MenuOpen;
            if (_tamePanel.activeSelf != show)
            {
                _tamePanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_tamePanel); }
            }

            if (!show) return;

            string name = string.IsNullOrEmpty(st.CreatureName) ? loc.Get("creature.generic.name") : st.CreatureName;
            _tameName.text = $"{loc.Get("ui.tame.title")} — {name}";
            _tameMood.text = string.IsNullOrEmpty(st.MoodKey) ? string.Empty : loc.Get(st.MoodKey);

            string need = string.IsNullOrEmpty(st.NeedKey) ? string.Empty : loc.Get(st.NeedKey);
            if (!string.IsNullOrEmpty(st.BaitKey)) { need += "  (" + loc.Get(st.BaitKey) + ")"; }
            if (!string.IsNullOrEmpty(st.MessageKey)) { need = loc.Get(st.MessageKey) + "\n" + need; }
            _tameNeed.text = need;
            _tameTrust.text = $"{loc.Get("ui.tame.trust")}: {st.Trust}/{st.Required}";

            SetButtonText(_tameFeed, loc.Get("ui.tame.feed"));
            SetButtonText(_tameCalm, loc.Get("ui.tame.calm"));
            SetButtonText(_tameApproach, loc.Get("ui.tame.approach"));
            SetButtonText(_tameSpace, loc.Get("ui.tame.space"));
            SetButtonText(_tameStop, loc.Get("ui.tame.cancel"));
        }

        private static void SetButtonText(Button button, string text)
        {
            var t = button.GetComponentInChildren<Text>();
            if (t != null) { t.text = text; }
        }

        private string LootText(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            BlocksBeyondTheStars.Networking.Messages.NetContainer nearest = null;
            float bestSq = 36f;
            foreach (var c in Game.Containers)
            {
                float dx = c.X + 0.5f - Game.PlayerPosition.x, dy = c.Y + 0.5f - Game.PlayerPosition.y, dz = c.Z + 0.5f - Game.PlayerPosition.z;
                float d = dx * dx + dy * dy + dz * dz;
                if (d < bestSq) { bestSq = d; nearest = c; }
            }

            if (nearest == null)
            {
                return string.Empty;
            }

            // A storage crate (Task 5 Stage 3b) shows the take/store keys; salvage capsules just say "loot".
            return nearest.Kind == "crate"
                ? $"{loc.Get("ui.hud.stash")} ({nearest.ItemCount})  ·  {loc.Get("ui.hud.stash_keys")}"
                : $"{loc.Get("ui.hud.loot")} ({nearest.ItemCount})";
        }

        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Scanner;
        }

        // --- helpers ---

        private static Image Panel(Transform parent, float x, float y, float w, float h)
            => UiKit.AddPanel(parent, x, y, w, h, new Color(0.05f, 0.12f, 0.24f, 0.82f));

        private void MakeCrosshair(RectTransform parent)
        {
            var v = new GameObject("v", typeof(RectTransform)); v.transform.SetParent(parent, false);
            var vr = v.GetComponent<RectTransform>(); vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 0.5f); vr.sizeDelta = new Vector2(2, 18);
            _crossV = v.AddComponent<Image>(); _crossV.color = UiKit.Cyan;
            var hh = new GameObject("h", typeof(RectTransform)); hh.transform.SetParent(parent, false);
            var hr = hh.GetComponent<RectTransform>(); hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f); hr.sizeDelta = new Vector2(18, 2);
            _crossH = hh.AddComponent<Image>(); _crossH.color = UiKit.Cyan;

            // Hit marker (#693): four diagonal ticks around the reticle, flashed briefly when one of the
            // local player's shots visibly lands (the entity views attribute the hull drop and call
            // ShowHitMarker). Inactive by default.
            _hitMarker = new GameObject("hits", typeof(RectTransform));
            _hitMarker.transform.SetParent(parent, false);
            var hm = _hitMarker.GetComponent<RectTransform>();
            hm.anchorMin = hm.anchorMax = new Vector2(0.5f, 0.5f);
            hm.sizeDelta = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var tick = new GameObject("t" + i, typeof(RectTransform));
                tick.transform.SetParent(_hitMarker.transform, false);
                var tr = tick.GetComponent<RectTransform>();
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.sizeDelta = new Vector2(2.5f, 9f);
                float ang = 45f + i * 90f;
                tr.localRotation = Quaternion.Euler(0f, 0f, ang);
                tr.anchoredPosition = Quaternion.Euler(0f, 0f, ang) * new Vector2(0f, 13f);
                tick.AddComponent<Image>().color = HitMarkerCol;
            }

            _hitMarker.SetActive(false);
        }

        /// <summary>Tints the reticle hostile-red while an enemy sits under it, and runs the hit-marker
        /// flash timer. Called every frame from <see cref="LateUpdate"/>.</summary>
        private void UpdateCrosshairState(float dt)
        {
            if (_crossV == null)
            {
                return;
            }

            var col = Game.AimedEnemyId != null ? HostileAim : UiKit.Cyan;
            if (_crossV.color != col)
            {
                _crossV.color = col;
                _crossH.color = col;
            }

            if (_hitMarkerTimer > 0f)
            {
                _hitMarkerTimer -= dt;
                if (_hitMarkerTimer <= 0f && _hitMarker != null)
                {
                    _hitMarker.SetActive(false);
                }
            }
        }

        /// <summary>Flashes the crosshair hit marker (#693) — called by the entity views when a hull drop is
        /// attributable to the local player's latest shot.</summary>
        public void ShowHitMarker()
        {
            if (_hitMarker == null)
            {
                return;
            }

            _hitMarkerTimer = 0.25f;
            _hitMarker.SetActive(true);
        }
    }
}
