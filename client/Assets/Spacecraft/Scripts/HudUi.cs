using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
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
        private RectTransform _todMarker;
        private RectTransform _compassShip, _compassWp;
        private Transform _compassParent; // parent for pooled beacon blips (item 37)
        private readonly System.Collections.Generic.List<RectTransform> _compassBeacons = new();

        private struct VitalRow { public Image Fill; public Text Label; public GameObject Go; }
        private VitalRow[] _vitals;

        private struct Slot { public Image Border; public RawImage Icon; public Text Num, Name; }
        private Slot[] _hotbar;

        // Scan / wreck panels.
        private GameObject _scanPanel, _wreckPanel;
        private Text _scanSubject, _scanInfo, _scanThreat, _scanKnow, _wreckName, _wreckProg, _wreckHint;
        private Image _wreckBar;
        private Button _wreckClaim;

        // Damage feedback (B21): a red screen flash + a cause label when health drops.
        private Image _dmgFlash;
        private Text _dmgCause;
        private float _prevHealth = 100f, _flashTimer, _causeTimer;
        private string _causeKey = string.Empty;

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
            }

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

            // Hotbar.
            _hotbar = new Slot[Slots];
            float sw = 60f, total = Slots * 64f, x0 = (W - total) / 2f, hy = H - 64f - 28f;
            for (int i = 0; i < Slots; i++)
            {
                _hotbar[i] = MakeSlot(root, x0 + i * 64f, hy, sw);
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

            // Toast / indicators / prompts / hint.
            _toast = UiKit.AddText(root, 14, 268, W - 28, 22, string.Empty, 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _inSpace = UiKit.AddText(root, W / 2f - 100, 8, 200, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt = UiKit.AddText(root, W / 2f - 160, H / 2f + 24, 320, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _loot = UiKit.AddText(root, W / 2f - 160, H / 2f + 48, 320, 22, string.Empty, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _hint = UiKit.AddText(root, 12, H - 26, 1400, 20, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);

            // Scan panel (bottom-left).
            _scanPanel = Panel(root, 10, H - 96 - 48, 290, 96).gameObject;
            _scanSubject = UiKit.AddText(_scanPanel.transform, 10, 6, 270, 18, string.Empty, 14, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _scanInfo = UiKit.AddText(_scanPanel.transform, 10, 28, 270, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            _scanThreat = UiKit.AddText(_scanPanel.transform, 10, 48, 270, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
            _scanKnow = UiKit.AddText(_scanPanel.transform, 10, 68, 270, 18, string.Empty, 14, UiKit.TextCol, TextAnchor.MiddleLeft);

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

        private Slot MakeSlot(Transform parent, float x, float y, float size)
        {
            var panel = Panel(parent, x, y, size, size);
            var border = panel;
            border.color = new Color(0.05f, 0.12f, 0.24f, 0.82f);
            var num = UiKit.AddText(panel.transform, 4, 2, size - 10, 18, string.Empty, 11, UiKit.TextCol, TextAnchor.UpperLeft);
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(panel.transform, false);
            UiKit.Place(iconGo, 14, 12, size - 30, size - 30);
            var icon = iconGo.AddComponent<RawImage>();
            icon.enabled = false;
            var name = UiKit.AddText(panel.transform, 2, size - 16, size - 4, 14, string.Empty, 10, UiKit.TextCol, TextAnchor.MiddleCenter);
            return new Slot { Border = border, Icon = icon, Num = num, Name = name };
        }

        private void RefreshHotbar(Spacecraft.Shared.Localization.Localizer loc)
        {
            // No on-foot hotbar in space — you're flying the ship, not holding hand tools.
            bool hide = Game.SpaceViewActive || Game.InSpace;
            for (int i = 0; i < Slots; i++)
            {
                if (_hotbar[i].Border != null)
                {
                    _hotbar[i].Border.gameObject.SetActive(!hide);
                }
            }

            if (hide)
            {
                return;
            }

            for (int i = 0; i < Slots; i++)
            {
                var s = _hotbar[i];
                bool sel = i == Game.SelectedHotbarSlot;
                s.Border.color = sel ? new Color(0.12f, 0.4f, 0.6f, 0.95f) : new Color(0.05f, 0.12f, 0.24f, 0.82f);
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

                Texture2D itemTex;
                if (blockDef != null && Game.Atlas != null)
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
                    var kind = Game.Content?.GetItem(item)?.Tool?.Kind ?? Spacecraft.Shared.Definitions.ToolKind.None;
                    s.Icon.texture = IconFactory.ForItem(item, kind);
                    s.Icon.uvRect = new Rect(0, 0, 1, 1);
                }

                s.Icon.color = IconResolver.Tint(item, Game); // toxic consumables read green
                s.Icon.enabled = true;
                string name = loc.Get($"item.{item}.name");
                s.Name.text = name.Length > 9 ? name.Substring(0, 8) + "…" : name;
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

        private void RefreshTimeOfDay(Spacecraft.Shared.Localization.Localizer loc)
        {
            var env = Game.Environment;
            if (env == null)
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
            _todText.text = $"{(day ? loc.Get("ui.hud.day") : loc.Get("ui.hud.night")).ToUpperInvariant()}  {mm}:{ss:00}  {tempStr}";
            _todMarker.anchoredPosition = new Vector2(10 + 150 * t, _todMarker.anchoredPosition.y);
        }

        private void RefreshScan(Spacecraft.Shared.Localization.Localizer loc)
        {
            var scan = Game.LastScan;
            bool show = scan != null && Time.time - Game.LastScanAt <= 8f;
            if (_scanPanel.activeSelf != show) _scanPanel.SetActive(show);
            if (!show) return;
            _scanSubject.text = $"{loc.Get("ui.scan.title").ToUpperInvariant()}: {ScanSubjectName(loc, scan.Subject)}";
            _scanInfo.text = scan.Info;
            _scanThreat.text = $"{loc.Get("ui.scan.threat")}: {scan.Threat}";
            _scanKnow.text = $"{loc.Get("ui.scan.knowledge")}: {scan.KnowledgeTotal}";
        }

        /// <summary>Resolves a scan subject key to a readable, localized name (block / item / creature)
        /// so the readout says what it is ("Stone") rather than the raw key ("stone").</summary>
        private string ScanSubjectName(Spacecraft.Shared.Localization.Localizer loc, string key)
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

            string creatureName = loc.Get($"creature.{key}.name");
            return creatureName.StartsWith("creature.") ? key : creatureName; // fall back to the raw key
        }

        private void RefreshWreck(Spacecraft.Shared.Localization.Localizer loc)
        {
            var wreck = Game.Wreck;
            bool show = wreck != null;
            if (_wreckPanel.activeSelf != show) _wreckPanel.SetActive(show);
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

        private string LootText(Spacecraft.Shared.Localization.Localizer loc)
        {
            Spacecraft.Networking.Messages.NetContainer nearest = null;
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
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Scanner;
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
