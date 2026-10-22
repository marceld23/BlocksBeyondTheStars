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
    /// The vitals panel of <see cref="HudUi"/>: the suit, ship and exposure bars with their motion layer and the
    /// low-value warnings (#753), and the running effects of the bio lab's preparations (#2202).
    /// </summary>
    public sealed partial class HudUi
    {
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

        private static readonly Color Health = new Color(0.92f, 0.32f, 0.34f);
        private static readonly Color Oxygen = new Color(0.36f, 0.78f, 1f);
        private static readonly Color Energy = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Hunger = new Color(1f, 0.6f, 0.25f);

        /// <summary>Energy-bar tint while the suit's climate control is actively draining it (#666) —
        /// a hot orange-red, so "why is my energy falling?" answers itself at a glance.</summary>
        private static readonly Color EnergyStressed = new Color(1f, 0.45f, 0.2f);
        private static readonly Color ExposureColdC = new Color(0.55f, 0.85f, 1f);  // 2026-09 exposure meter (Titas)
        private static readonly Color ExposureHotC = new Color(1f, 0.5f, 0.25f);
        private static readonly Color HullC = new Color(0.6f, 0.66f, 0.74f);
        private static readonly Color ShieldC = new Color(0.4f, 0.7f, 1f);

        private GameObject _vitalsPanel, _shipRows;

        private struct VitalRow
        {
            public Image Fill, Ghost;      // holo bar fill + the dim "what you just lost" trail behind it
            public TMP_Text Label;
            public GameObject Go;
            public bool Warn, Init;
            public Color BaseColor;
            public string LastLabel;
            public string Suffix;          // shown after the value (#2202: the shield cushion on the health row)
            public int LastValue, LastMax;
            public bool LabelDirty;
            public float TargetFrac, ShownFrac, GhostFrac, TargetValue, ShownValue; // motion state (AnimateVitals)
        }

        private const float VitalBarW = 178f;
        private VitalRow[] _vitals;
        private float _lowVitalBeepTimer; // shared low-vitals alarm cadence (#753)
        private int _homeBeeped;          // #2824: the vitals (one bit per row) that already beeped their one beep at home
        private bool _vitalsWarnedAtHome; // #2824: whether the last warning pass ran at home (to spot coming home)

        // Running effects of the bio lab's preparations (#2202): one small row per effect inside the vitals panel,
        // under its last bar — a dot in the effect's colour, "Speed III", the time left as m:ss, and the catch in
        // amber to the right of the panel. Pooled like the pickup feed; the panel grows by the rows it shows.
        private sealed class EffectRow
        {
            public GameObject Go;
            public Image Dot, SideMark;
            public TMP_Text Label, Left, Side;
            public int Effect = -1, Level = -1, Seconds = -1, SideEffect = -1; // what the row shows (written on change only)
            public float Y = -1f;
            public object Loc;
        }

        private readonly System.Collections.Generic.List<EffectRow> _effectRows = new System.Collections.Generic.List<EffectRow>();
        private const float EffectRowH = 20f;
        private int _shieldShown;      // the shield cushion the health row shows (rounded up)
        private string _shieldSuffix;
        private object _shieldLoc;

        /// <summary>Edge detector for the base life-support field (#782): true while the last HUD refresh saw
        /// the player inside some founded base's zone, so the "Life support: …" toast fires once on entry.</summary>
        private bool _wasInBaseZone;
        private int _exposureRow = -1; // the panel row the exposure bar sits in (4 below the suit, 6 below the ship rows)

        private void BuildVitals(Transform root)
        {
            // Vitals panel (6 rows; ship rows toggled) + the exposure row of the timed-exposure worlds (2026-09).
            _vitalsPanel = Panel(root, 10, VitalsPanelY, 226, 196).gameObject;
            _vitals = new VitalRow[7];
            string[] order = { "health", "oxygen", "energy", "hunger", "hull", "shield", "exposure" };
            for (int i = 0; i < 7; i++)
            {
                _vitals[i] = MakeVital(_vitalsPanel.transform, 10, 8 + i * 24, order[i]);
            }
        }

        private void RefreshVitals(BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            // Vitals. #2202: a shield preparation's cushion rides on top of the 100 — "+12 Shield" after the health value.
            int shield = Mathf.CeilToInt(Game.Bio.Shield);
            if (shield != _shieldShown || !ReferenceEquals(loc, _shieldLoc))
            {
                _shieldShown = shield;
                _shieldLoc = loc;
                _shieldSuffix = shield > 0 ? "  +" + shield + " " + loc.Get("ui.bio.shield") : null;
            }

            SetVital(0, loc.Get("ui.hud.health"), Game.Health, Game.Health / 100f, Health, true, 0f, _shieldSuffix);
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
                : Game.LifeSupportSource == 2 ? "  (" + loc.Get("ui.hud.station_air") + ")" // #1473: sealed station pocket
                : baseAir ? "  (" + loc.Get("ui.hud.base_air") + ")"
                : Game.LifeSupportSource == 4 ? "  (" + loc.Get("ui.hud.factory_air") + ")" // #2070: a factory hall breathes
                : Game.LifeSupportSource == 5 ? "  (" + loc.Get("ui.hud.city_air") + ")" // #2112: a sky city's air pocket
                : string.Empty;
            string oxy = loc.Get("ui.hud.oxygen") + oxySuffix;
            SetVital(1, oxy, Game.Oxygen, Game.Oxygen / Mathf.Max(1f, Game.SuitOxygenMax), Oxygen, true);
            // While climate control fights heat/cold/vacuum (#666) the energy bar turns stress-orange. Its full mark is the
            // worn gear's maximum (#2297: 150 with the suit battery), not a flat 100.
            SetVital(2, loc.Get("ui.hud.energy"), Game.SuitEnergy, Game.SuitEnergy / Mathf.Max(1f, Game.SuitEnergyMax),
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
                // #1585: hull and shield spell out "value / max". The shield only charges in flight, so a
                // freshly built generator on the ground reads "Shield 55" against a 41 % bar — which looks like
                // a dead module unless the max (135) stands next to it. The suit rows keep their bare value.
                SetVital(4, loc.Get("ui.hud.hull"), c.Hull, c.HullMax > 0 ? c.Hull / c.HullMax : 0f, HullC, true, c.HullMax);
                SetVital(5, loc.Get("ui.hud.shield"), c.Shield, c.ShieldMax > 0 ? c.Shield / c.ShieldMax : 0f, ShieldC, true, c.ShieldMax);
            }
            else
            {
                SetVital(4, null, 0, 0, HullC, false);
                SetVital(5, null, 0, 0, ShieldC, false);
            }

            // Exposure meter (2026-09, Titas): the cold or heat protection left, under the last visible row.
            bool exposure = Game.ExposureActive || Game.Exposure > 0.001f;
            if (exposure)
            {
                int row = ship ? 6 : 4;
                if (_exposureRow != row)
                {
                    _exposureRow = row;
                    UiKit.Place(_vitals[6].Go, 10, 8 + row * 24, 200, 16);
                }

                float left = 1f - Mathf.Clamp01(Game.Exposure);
                SetVital(6, loc.Get(Game.ExposureHot ? "ui.hud.exposure_hot" : "ui.hud.exposure_cold"), left * 100f, left,
                    Game.ExposureHot ? ExposureHotC : ExposureColdC, true);
            }
            else
            {
                SetVital(6, null, 0, 0, ExposureColdC, false);
            }

            float vitalsHeight = (ship ? 196f : 116f) + (exposure ? 24f : 0f);

            // #2202: the running effects, as rows under the last bar; the panel grows by what they need, and
            // VitalsBottomY with it (the flight overlay parks its readout below that edge).
            float effectsTop = 8f + (4 + (ship ? 2 : 0) + (exposure ? 1 : 0)) * 24f;
            int effectRows = RefreshEffects(loc, effectsTop);
            if (effectRows > 0)
            {
                vitalsHeight = Mathf.Max(vitalsHeight, effectsTop + effectRows * EffectRowH + 10f);
            }

            _vitalsPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(226, vitalsHeight);
            VitalsBottomY = VitalsPanelY + vitalsHeight;
        }

        // --- vitals ---

        private VitalRow MakeVital(Transform parent, float x, float y, string key)
        {
            var go = new GameObject("Vital_" + key, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, 200, 16);
            // Line icon in the 22-unit gutter the bar always left free (vital_* from tools/ai-assets/gen_hud_icons.py).
            var icon = UiKit.Icon("vital_" + key);
            if (icon != null)
            {
                UiKit.AddImage(go.transform, 1, -1, 18, 18, icon, new Color(0.86f, 0.93f, 1f, 0.92f));
            }

            var trackCol = new Color(0.03f, 0.07f, 0.13f, 0.9f);
            var track = UiKit.AddImage(go.transform, 22, 0, VitalBarW, 16, UiKit.SolidSprite, trackCol);
            var ghost = UiKit.AddImage(go.transform, 22, 0, VitalBarW, 16, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.4f));
            var fill = UiKit.AddImage(go.transform, 22, 0, VitalBarW, 16, UiKit.SolidSprite, Color.white);
            if (UiHolo.Available)
            {
                track.color = new Color(trackCol.r, trackCol.g, trackCol.b, 1f);
                UiHolo.Apply(track, UiHolo.Style.Panel, 8f, 1f, 0.3f).FillOpacity = trackCol.a;
                UiHolo.Apply(ghost, UiHolo.Style.Panel, 8f, 0f, 0f);
                UiHolo.Apply(fill, UiHolo.Style.Panel, 8f, 0.5f, 0.9f);
            }
            else
            {
                foreach (var img in new[] { ghost, fill })
                {
                    img.type = Image.Type.Filled;
                    img.fillMethod = Image.FillMethod.Horizontal;
                    img.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }

            var label = UiText.Add(go.transform, 28, 0, 172, 16, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);
            return new VitalRow { Fill = fill, Ghost = ghost, Label = label, Go = go, ShownFrac = -1f };
        }

        private void SetVital(int i, string label, float value, float frac, Color color, bool active, float max = 0f, string suffix = null)
        {
            var v = _vitals[i];
            if (v.Go.activeSelf != active) v.Go.SetActive(active);
            // Low-value warning state with hysteresis (#753): trips below 10 %, clears above 15 %, so a
            // value hovering at the threshold doesn't flicker the alarm on and off. Suppressed while dead
            // (every bar bottoms out on the respawn screen — blinking them all helps nobody).
            v.Warn = active && Game.Health > 0f && (frac < 0.10f || (v.Warn && frac < 0.15f));
            v.BaseColor = color;
            if (!active)
            {
                _vitals[i] = v;
                return;
            }

            v.Fill.color = color;
            v.Ghost.color = new Color(color.r, color.g, color.b, 0.38f);
            v.TargetFrac = Mathf.Clamp01(frac);
            v.TargetValue = value;
            if (!v.Init || UiKit.ReducedMotion)
            {
                // First reading (or reduced motion): no easing, the bar simply IS the value.
                v.Init = true;
                v.ShownFrac = v.GhostFrac = v.TargetFrac;
                v.ShownValue = value;
                UiHolo.SetBar(v.Fill, v.ShownFrac, VitalBarW);
                UiHolo.SetBar(v.Ghost, v.GhostFrac, VitalBarW);
            }

            int shownMax = max > 0f ? Mathf.RoundToInt(max) : 0; // 0 = no "/ max" suffix (#1585)
            if (shownMax != v.LastMax || !ReferenceEquals(label, v.LastLabel) || !ReferenceEquals(suffix, v.Suffix))
            {
                v.LabelDirty = true;
            }

            v.LastMax = shownMax;
            v.LastLabel = label;
            v.Suffix = suffix;
            UpdateVitalText(ref v);
            _vitals[i] = v;
        }

        /// <summary>Writes the row label only when the rounded (rolling) value, the max or the label changed
        /// (#1554: 10 Hz × 6 rows of interpolated strings otherwise).</summary>
        private static void UpdateVitalText(ref VitalRow v)
        {
            int shown = Mathf.RoundToInt(v.ShownValue);
            if (shown == v.LastValue && !v.LabelDirty && v.LastLabel != null)
            {
                return;
            }

            v.LastValue = shown;
            v.LabelDirty = false;
            v.Label.text = v.LastMax > 0 ? $"{v.LastLabel}  {shown} / {v.LastMax}" : $"{v.LastLabel}  {shown}{v.Suffix}";
        }

        // --- running effects (#2202) ---

        /// <summary>Shows the running effects as rows inside the vitals panel, the first at <paramref name="top"/>;
        /// returns how many rows are up. A label is only written when what it shows changed — the seconds tick once
        /// a second, this runs at the refresh's 10 Hz.</summary>
        private int RefreshEffects(BlocksBeyondTheStars.Shared.Localization.Localizer loc, float top)
        {
            var effects = Game.Bio.Effects;
            int count = effects.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= _effectRows.Count)
                {
                    _effectRows.Add(MakeEffectRow());
                }

                var row = _effectRows[i];
                var e = effects[i];
                if (!row.Go.activeSelf)
                {
                    row.Go.SetActive(true);
                }

                float y = top + i * EffectRowH;
                if (!Mathf.Approximately(y, row.Y))
                {
                    row.Y = y;
                    UiKit.Place(row.Go, 10f, y, 206f, EffectRowH);
                }

                bool relabel = !ReferenceEquals(loc, row.Loc); // the language changed under the HUD
                row.Loc = loc;
                if (relabel || (int)e.Effect != row.Effect || e.Level != row.Level)
                {
                    row.Effect = (int)e.Effect;
                    row.Level = e.Level;
                    row.Dot.color = BioLabUi.EffectColor(e.Effect);
                    row.Label.text = BlocksBeyondTheStars.Shared.Bio.BioItems.EffectLabel(loc, e.Effect, e.Level);
                }

                int seconds = Mathf.Max(0, Mathf.CeilToInt(e.SecondsLeft));
                if (seconds != row.Seconds)
                {
                    row.Seconds = seconds;
                    row.Left.text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
                }

                int side = e.SideLevel > 0 ? (int)e.Side : 0;
                if (relabel || side != row.SideEffect)
                {
                    row.SideEffect = side;
                    row.SideMark.enabled = side != 0;
                    row.Side.text = side != 0 ? loc.Get("bio.side." + e.Side.ToString().ToLowerInvariant()) : string.Empty;
                }
            }

            for (int i = count; i < _effectRows.Count; i++)
            {
                if (_effectRows[i].Go.activeSelf)
                {
                    _effectRows[i].Go.SetActive(false);
                }
            }

            return count;
        }

        /// <summary>One effect row: the colour dot, the name with its level, the time left — and, to the right of the
        /// panel, the catch behind a down triangle (drawn: the HUD's SDF font carries no ▼ glyph).</summary>
        private EffectRow MakeEffectRow()
        {
            var go = new GameObject("effect_row", typeof(RectTransform));
            go.transform.SetParent(_vitalsPanel.transform, false);
            UiKit.Place(go, 10f, 0f, 206f, EffectRowH);

            var dot = UiKit.AddImage(go.transform, 5f, 5f, 10f, 10f, UiKit.DiscSprite, Color.white);
            var label = UiText.Add(go.transform, 22f, 1f, 134f, 18f, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);
            var left = UiText.Add(go.transform, 158f, 1f, 42f, 18f, string.Empty, 12, UiKit.TextCol, TextAnchor.MiddleRight, FontStyle.Normal, UiText.Look.Outline);

            var mark = UiKit.AddImage(go.transform, 224f, 6f, 9f, 9f, UiKit.TriangleSprite, UiKit.Warn);
            var markRt = mark.rectTransform; // turned upside down around its own centre
            markRt.pivot = new Vector2(0.5f, 0.5f);
            markRt.anchoredPosition = new Vector2(228.5f, -10.5f);
            markRt.localRotation = Quaternion.Euler(0f, 0f, 180f);
            var side = UiText.Add(go.transform, 238f, 1f, 170f, 18f, string.Empty, 12, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);

            return new EffectRow { Go = go, Dot = dot, SideMark = mark, Label = label, Left = left, Side = side };
        }

        /// <summary>Motion layer for the vitals (per frame): the fill eases to its target, a dim ghost trails
        /// behind on a loss so the size of the hit stays readable for a moment, and the number rolls instead
        /// of jumping. Cheap: four vertices per bar move, text only when the rounded value ticks.</summary>
        private void AnimateVitals(float dt)
        {
            if (_vitals == null || UiKit.ReducedMotion)
            {
                return;
            }

            float kFill = 1f - Mathf.Exp(-dt * 11f);
            float kGhost = 1f - Mathf.Exp(-dt * 2.6f);
            float kNum = 1f - Mathf.Exp(-dt * 9f);
            for (int i = 0; i < _vitals.Length; i++)
            {
                var v = _vitals[i];
                if (!v.Init || v.Go == null || !v.Go.activeSelf)
                {
                    continue;
                }

                bool changed = false;
                if (!Mathf.Approximately(v.ShownFrac, v.TargetFrac))
                {
                    v.ShownFrac = Mathf.Abs(v.TargetFrac - v.ShownFrac) < 0.002f ? v.TargetFrac : Mathf.Lerp(v.ShownFrac, v.TargetFrac, kFill);
                    UiHolo.SetBar(v.Fill, v.ShownFrac, VitalBarW);
                    changed = true;
                }

                // Ghost: on a gain it rides with the fill; on a loss it lags behind and slowly drains down.
                float ghostTarget = v.ShownFrac;
                if (v.GhostFrac < ghostTarget)
                {
                    v.GhostFrac = ghostTarget;
                    changed = true;
                }
                else if (v.GhostFrac > ghostTarget)
                {
                    v.GhostFrac = v.GhostFrac - ghostTarget < 0.002f ? ghostTarget : Mathf.Lerp(v.GhostFrac, ghostTarget, kGhost);
                    changed = true;
                }

                if (changed)
                {
                    UiHolo.SetBar(v.Ghost, v.GhostFrac, VitalBarW);
                }

                if (!Mathf.Approximately(v.ShownValue, v.TargetValue))
                {
                    v.ShownValue = Mathf.Abs(v.TargetValue - v.ShownValue) < 0.5f ? v.TargetValue : Mathf.Lerp(v.ShownValue, v.TargetValue, kNum);
                    UpdateVitalText(ref v);
                }

                _vitals[i] = v;
            }
        }

        private static readonly Color VitalWarnRed = new(1f, 0.25f, 0.2f);

        /// <summary>Blinks any vital row below its warning threshold toward red and drives the shared
        /// low-vitals beep (#753). Runs per frame — the 10 Hz Refresh would step the blink. Oxygen keeps
        /// its own dedicated 25 % alarm (vignette + beeps), so its row blinks but doesn't double-beep.
        /// At home (#2824: aboard your ship, or under your own base's roof) a vital that drops below its line beeps
        /// ONCE — home is where you go to recover, not to be nagged every 2.5 s; the row still blinks. It re-arms
        /// when that vital recovers above its line or when you leave home.</summary>
        private void UpdateLowVitalWarnings(float dt)
        {
            int warnMask = 0; // the beep-worthy vitals below their line, one bit per row
            for (int i = 0; i < _vitals.Length; i++)
            {
                var v = _vitals[i];
                if (v.Fill == null || !v.Warn || !v.Go.activeSelf) continue;
                float k = UiKit.ReducedMotion ? 1f : Mathf.PingPong(Time.time * 2.4f, 1f);
                v.Fill.color = Color.Lerp(v.BaseColor, VitalWarnRed, 0.35f + 0.65f * k);
                if (i != 1) warnMask |= 1 << i; // 1 = oxygen — covered by UpdateOxygenAlarm
            }

            bool atHome = Game != null && Game.AtHome;
            if (atHome && !_vitalsWarnedAtHome)
            {
                // Just came home: what was already low has been beeping outside — it does not announce itself again.
                _homeBeeped = warnMask;
            }

            _vitalsWarnedAtHome = atHome;
            _homeBeeped = atHome ? _homeBeeped & warnMask : 0; // a vital back above its line re-arms; leaving home re-arms all
            if (warnMask == 0)
            {
                _lowVitalBeepTimer = 0f; // first beep fires immediately when a bar next runs low
                return;
            }

            _lowVitalBeepTimer -= dt;
            if (atHome)
            {
                // The outside cadence keeps counting down meanwhile, so a step out of the door with a bar still low beeps
                // no sooner than it would have — and then every 2.5 s as ever.
                if ((warnMask & ~_homeBeeped) != 0)
                {
                    _homeBeeped |= warnMask;
                    _lowVitalBeepTimer = LowVitalBeepSeconds;
                    ClientAudio.Instance?.Cue("vitals_warning", 0.5f);
                }

                return;
            }

            if (_lowVitalBeepTimer <= 0f)
            {
                _lowVitalBeepTimer = LowVitalBeepSeconds;
                ClientAudio.Instance?.Cue("vitals_warning", 0.5f);
            }
        }

        private const float LowVitalBeepSeconds = 2.5f; // the low-vitals beep's cadence outside (#753)
    }
}
