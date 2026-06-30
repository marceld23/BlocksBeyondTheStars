// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

        private static readonly Color Health = new Color(0.92f, 0.32f, 0.34f);
        private static readonly Color Oxygen = new Color(0.36f, 0.78f, 1f);
        private static readonly Color Energy = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Hunger = new Color(1f, 0.6f, 0.25f);
        private static readonly Color HullC = new Color(0.6f, 0.66f, 0.74f);
        private static readonly Color ShieldC = new Color(0.4f, 0.7f, 1f);

        private Canvas _canvas;
        private GameObject _crosshair, _locationPanel, _vitalsPanel, _shipRows;
        private Text _locTitle, _locPlace, _toast, _inSpace, _prompt, _loot, _hint, _todText, _compassDist;
        private GameObject _playtimePanel; // optional session/total playtime readout (top-right, under the clock)
        private Text _playtimeText;
        private RectTransform _todMarker;
        private RectTransform _compassShip, _compassWp;
        private Transform _compassParent; // parent for pooled beacon blips (item 37)
        private readonly System.Collections.Generic.List<RectTransform> _compassBeacons = new();

        private struct VitalRow { public Image Fill; public Text Label; public GameObject Go; }
        private VitalRow[] _vitals;

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

            bool show = !Game.MenuOpen;
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
            }

            if (show)
            {
                Refresh();
            }
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
            _vitalsPanel = Panel(root, 10, 64, 226, 196).gameObject;
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

            // Compass (round).
            var comp = new GameObject("Compass", typeof(RectTransform));
            comp.transform.SetParent(root, false);
            UiKit.Place(comp, W - 130f, 10, 120, 120);
            var craw = comp.AddComponent<RawImage>();
            craw.texture = UiKit.RadarCircle;
            UiKit.AddText(comp.transform, 0, 2, 120, 18, "▲", 14, UiKit.Cyan, TextAnchor.UpperCenter, FontStyle.Bold);
            _compassDist = UiKit.AddText(comp.transform, 0, 100, 120, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _compassShip = Blip(comp.transform, new Color(0.3f, 0.8f, 1f), 8f);
            _compassWp = Blip(comp.transform, new Color(1f, 0.85f, 0.3f), 7f);
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
            // threat, knowledge, and a highlighted "new discovery" line on a first-time scan.
            _scanPanel = Panel(root, 10, H - 150 - 48, 360, 150).gameObject;
            var scanIcon = UiKit.Icon("item_advanced_scanner") ?? UiKit.Icon("cat_target");
            float scanTextX = 10f;
            if (scanIcon != null)
            {
                UiKit.AddImage(_scanPanel.transform, 8, 6, 22, 22, scanIcon, UiKit.Cyan);
                scanTextX = 36f;
            }

            _scanSubject = UiKit.AddText(_scanPanel.transform, scanTextX, 6, 340 - scanTextX, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _scanInfo = UiKit.AddText(_scanPanel.transform, 10, 34, 340, 56, string.Empty, 14, UiKit.TextCol, TextAnchor.UpperLeft);
            _scanInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
            _scanThreat = UiKit.AddText(_scanPanel.transform, 10, 94, 340, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            _scanKnow = UiKit.AddText(_scanPanel.transform, 10, 116, 340, 22, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Wreck panel (right).
            _wreckPanel = Panel(root, W - 260f, 140, 250, 150).gameObject;
            _wreckName = UiKit.AddText(_wreckPanel.transform, 10, 26, 230, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddText(_wreckPanel.transform, 10, 6, 230, 18, "WRECK", 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
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
            string oxy = loc.Get("ui.hud.oxygen") + (Game.Environment != null && Game.Environment.Breathable ? " *" : string.Empty);
            SetVital(1, oxy, Game.Oxygen, Game.Oxygen / 100f, Oxygen, true);
            SetVital(2, loc.Get("ui.hud.energy"), Game.SuitEnergy, Game.SuitEnergy / 100f, Energy, true);
            SetVital(3, loc.Get("ui.hud.hunger"), Game.Hunger, Game.Hunger / 100f, Hunger, true);
            bool ship = Game.ShipCombat != null;
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

            _vitalsPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(226, ship ? 196 : 116);

            RefreshHotbar(loc);
            RefreshCompass();
            RefreshTimeOfDay(loc);
            RefreshPlaytime(loc);

            _toast.text = Game.LastMessage ?? string.Empty;
            _inSpace.text = Game.InSpace ? loc.Get("ui.hud.in_space") : string.Empty;
            _hint.text = loc.Get("ui.hud.hint");

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
            _loot.text = LootText(loc);

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

            string hint = $"F: {loc.Get("hud.speeder.exit")}  ·  R: {loc.Get("hud.speeder.refuel")}";
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
            if (!active) return;
            v.Fill.color = color;
            v.Fill.fillAmount = Mathf.Clamp01(frac);
            v.Label.text = $"{label}  {Mathf.RoundToInt(value)}";
        }

        // --- hotbar ---

        private void RefreshHotbar(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            // No on-foot hotbar while flying the ship — you're piloting, not holding hand tools. BUT on an EVA
            // the hotbar IS shown: you float in space and build/mine on structures from your inventory, so you
            // need to see + pick the held block/tool (B?).
            bool hide = ((Game.SpaceViewActive || Game.InSpace) && !Game.InEva) || !string.IsNullOrEmpty(Game.InSpeeder);
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
                    continue;
                }

                var blockDef = Game.Content?.GetBlock(item);
                if (blockDef == null && Game.Content?.GetItem(item)?.PlacesBlock is string pb && pb.Length > 0)
                {
                    blockDef = Game.Content?.GetBlock(pb); // a seed etc. shows the tile of the block it places
                }

                // A shaped block (sphere/pyramid/…) shows a form-specific icon instead of a plain cube tile (#125).
                int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(item);
                Texture2D shapeTex = (blockDef != null && Game.Atlas != null && shape > 0)
                    ? ShapeIconFactory.ForBlock(Game.Atlas, (ushort)blockDef.NumericId.Value, shape)
                    : null;

                Texture2D itemTex;
                if (shapeTex != null)
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

        // --- compass ---

        private RectTransform Blip(Transform parent, Color color, float size)
        {
            var img = UiKit.AddImage(parent, 0, 0, size, size, UiKit.SolidSprite, color);
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
            PlaceBlip(_compassWp, Game.Waypoint.HasValue, Game.Waypoint ?? Vector3.zero, radius);
            PlaceBlip(_compassShip, Game.ShipPosition.HasValue, Game.ShipPosition ?? Vector3.zero, radius, out float dist);
            _compassDist.text = Game.ShipPosition.HasValue ? $"{Mathf.RoundToInt(dist)} m" : string.Empty;

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
            float r = Mathf.Clamp(dist * 1.2f, 8f, radius);
            blip.gameObject.SetActive(true);
            blip.anchoredPosition = new Vector2(Mathf.Sin(ang) * r, Mathf.Cos(ang) * r); // +Y up = north
        }

        private void RefreshTimeOfDay(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var env = Game.Environment;
            // Day/night clock, temperature and gravity are planet-surface readings — meaningless in space, both
            // while piloting and on an EVA (no day/night cycle out there), so drop the whole panel there as well
            // as when there's no environment yet.
            if (env == null || Game.SpaceViewActive || Game.OnFootInSpace)
            {
                _todText.transform.parent.gameObject.SetActive(false);
                return;
            }

            _todText.transform.parent.gameObject.SetActive(true);
            float t = Game.LocalTimeOfDay; // the player's local time (longitude-shifted), already 0..1
            bool day = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f) > 0f;
            float nextEdge = day ? 0.75f : (t < 0.25f ? 0.25f : 1.25f);
            float frac = nextEdge - t; if (frac < 0f) frac += 1f;
            float secs = frac * Mathf.Max(1f, env.DayLengthSeconds);
            int mm = Mathf.FloorToInt(secs / 60f), ss = Mathf.FloorToInt(secs % 60f);
            string tempStr = env.Temperature <= -900f || Game.OnFootInSpace ? "—" : $"{Mathf.RoundToInt(env.Temperature)}°C";
            // Show this world's gravity (e.g. "0.6 g") only when it notably differs from Earth-like, so normal
            // worlds stay uncluttered. Hidden in space (on-foot zero-g) where gravity doesn't apply.
            string gravStr = env.GravityFactor > 0.01f && Mathf.Abs(env.GravityFactor - 1f) > 0.05f && !Game.OnFootInSpace
                ? "  " + string.Format(loc.Get("ui.hud.gravity"), env.GravityFactor.ToString("0.0"))
                : string.Empty;
            _todText.text = $"{(day ? loc.Get("ui.hud.day") : loc.Get("ui.hud.night")).ToUpperInvariant()}  {mm}:{ss:00}  {tempStr}{gravStr}";
            _todMarker.anchoredPosition = new Vector2(10 + 150 * t, _todMarker.anchoredPosition.y);
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
            bool show = scan != null && Time.time - Game.LastScanAt <= 12f;
            if (_scanPanel.activeSelf != show)
            {
                _scanPanel.SetActive(show);
                if (show) { UiKit.TransitionIn(_scanPanel); }
            }

            if (!show) return;
            _scanSubject.text = $"{loc.Get("ui.scan.title").ToUpperInvariant()}: {ScanSubjectName(loc, scan.Subject)}";
            _scanInfo.text = scan.Info;
            _scanThreat.text = $"{loc.Get("ui.scan.threat")}: {scan.Threat}";

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
            return loc.Has(creatureKey) ? loc.Get(creatureKey) : key;
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

        private static void MakeCrosshair(RectTransform parent)
        {
            var v = new GameObject("v", typeof(RectTransform)); v.transform.SetParent(parent, false);
            var vr = v.GetComponent<RectTransform>(); vr.anchorMin = vr.anchorMax = new Vector2(0.5f, 0.5f); vr.sizeDelta = new Vector2(2, 18);
            v.AddComponent<Image>().color = UiKit.Cyan;
            var hh = new GameObject("h", typeof(RectTransform)); hh.transform.SetParent(parent, false);
            var hr = hh.GetComponent<RectTransform>(); hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.5f); hr.sizeDelta = new Vector2(18, 2);
            hh.AddComponent<Image>().color = UiKit.Cyan;
        }
    }
}
