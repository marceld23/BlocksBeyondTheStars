// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The flight overlay (uGUI): the fade and hit flash, the hint and board prompts, the cargo line, the hull/shield
    /// gauges and their alarm, the instruments readout, the crosshair, the systems quick-bar, the lens flare, and the
    /// floating health bars and nameplates.
    /// </summary>
    public sealed partial class SpaceView
    {
        // Ship-systems quick-bar drawn as icon cells (shares UiKit.QuickSlot with the on-foot hotbar so the two
        // HUDs read alike): a bottom-centre row, one cell per fitted system, rebuilt when the system count changes.
        private UiKit.QuickSlot[] _systemSlots = System.Array.Empty<UiKit.QuickSlot>();
        private GameObject _systemsBarRoot;
        private int _builtSystemCount = -1;
        private readonly List<Image> _flare = new List<Image>();
        private Sprite _glowSprite;
        private static readonly float[] FlareT = { 0f, 0.35f, 0.62f, 1.0f, 1.32f };       // 0 = at sun … 1 = screen centre
        private static readonly float[] FlareSize = { 150f, 42f, 66f, 30f, 52f };
        private static readonly float[] FlareAlpha = { 0.30f, 0.10f, 0.08f, 0.10f, 0.06f };

        // ── Modern uGUI overlay (replaces the IMGUI fade + hint) ─────────────────────────────
        private Canvas _ui;
        private Canvas _fx;  // #2496: the launch/landing fade and the hit flash on their own canvas — they work while the HUD hides
        private Image _fade;
        private Image _hit;
        private Image _crosshair;
        private Text _hint;
        private Text _board;
        private Text _cargo;
        private RectTransform _shipStatus;  // repositioned each frame — the HUD panel above it changes height
        private StatusBar _hullBar, _shieldBar;
        private Text _instruments;         // flight readout: speed / throttle / heading (+ hull/shield)
        private Vector3 _instLastCamPos;
        private float _instSpeed;          // smoothed speed for a steady readout
        private const float ReadoutGap = 10f;        // breathing room between the vitals panel and the cargo readout

        private void EnsureUi()
        {
            if (_ui != null)
            {
                return;
            }

            // #2496: the launch/landing fade and the hit flash live on a small canvas of their own, so they keep working while
            // the flight HUD steps aside under a cutscene (GameBootstrap.HudHidden). Same sorting order as the HUD and created
            // first, so the HUD stays drawn over the fade as before (equal orders draw in hierarchy order).
            _fx = UiKit.CreateCanvas("Space View Fade", UiKit.HudRefW, UiKit.HudRefH);
            _fx.sortingOrder = 12;

            // Flat (not diegetic) but still a HUD, so it follows the player's UI-scale setting (#483).
            _ui = UiKit.CreateCanvas("Space View Overlay", UiKit.HudRefW, UiKit.HudRefH, userScalable: true); // ~1.25× bigger flight HUD
            _ui.sortingOrder = 12; // above the space HUD, below menus

            // Full-screen launch/landing fade.
            var fadeGo = new GameObject("Fade", typeof(RectTransform));
            fadeGo.transform.SetParent(_fx.transform, false);
            var frt = fadeGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _fade = fadeGo.AddComponent<Image>();
            _fade.sprite = UiKit.SolidSprite;
            _fade.color = new Color(0f, 0f, 0f, 0f);
            _fade.raycastTarget = false;

            // Red damage flash on a hit/collision.
            var hitGo = new GameObject("Hit", typeof(RectTransform));
            hitGo.transform.SetParent(_fx.transform, false);
            var hrtt = hitGo.GetComponent<RectTransform>();
            hrtt.anchorMin = Vector2.zero;
            hrtt.anchorMax = Vector2.one;
            hrtt.offsetMin = hrtt.offsetMax = Vector2.zero;
            _hit = hitGo.AddComponent<Image>();
            _hit.sprite = UiKit.SolidSprite;
            _hit.color = new Color(1f, 0.2f, 0.2f, 0f);
            _hit.raycastTarget = false;

            // Bottom-centre cruise controls hint.
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(_ui.transform, false);
            var hrt = hintGo.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(560f, 24f);
            hrt.anchoredPosition = new Vector2(0f, 20f);
            _hint = hintGo.AddComponent<Text>();
            _hint.font = UiKit.Font;
            _hint.fontSize = 18;
            _hint.color = UiKit.TextCol;
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            _hint.raycastTarget = false;

            // "Press E to board" prompt when near a station (centre, above the crosshair).
            var boardGo = new GameObject("BoardPrompt", typeof(RectTransform));
            boardGo.transform.SetParent(_ui.transform, false);
            var brt = boardGo.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(620f, 28f);
            brt.anchoredPosition = new Vector2(0f, -90f);
            _board = boardGo.AddComponent<Text>();
            _board.font = UiKit.Font;
            _board.fontSize = 22;
            _board.color = UiKit.Cyan;
            _board.alignment = TextAnchor.MiddleCenter;
            _board.fontStyle = FontStyle.Bold;
            _board.horizontalOverflow = HorizontalWrapMode.Overflow;
            _board.raycastTarget = false;
            _board.gameObject.SetActive(false);

            // Ship status block (top-left): the cargo readout, then the hull and shield bars. Its y is NOT
            // fixed — it trails the on-foot HUD's vitals panel, which lives on a lower canvas at the same
            // reference resolution and changes height with the rows it shows (PositionShipStatus, #915).
            var statusGo = new GameObject("ShipStatus", typeof(RectTransform));
            statusGo.transform.SetParent(_ui.transform, false);
            _shipStatus = statusGo.GetComponent<RectTransform>();
            _shipStatus.anchorMin = _shipStatus.anchorMax = new Vector2(0f, 1f);
            _shipStatus.pivot = new Vector2(0f, 1f);
            _shipStatus.sizeDelta = new Vector2(StatusWidth, 82f);
            PositionShipStatus();

            // Cargo readout, pulses when salvage is tractored in.
            var cargoGo = new GameObject("Cargo", typeof(RectTransform));
            cargoGo.transform.SetParent(_shipStatus, false);
            UiKit.Place(cargoGo, 0f, 0f, StatusWidth, 26f);
            _cargo = cargoGo.AddComponent<Text>();
            _cargo.font = UiKit.Font;
            _cargo.fontSize = 18;
            _cargo.color = UiKit.TextCol;
            _cargo.alignment = TextAnchor.MiddleLeft;
            _cargo.horizontalOverflow = HorizontalWrapMode.Overflow;
            _cargo.raycastTarget = false;

            // Hull + shield as BARS, directly under the suit's own bars and in the same column — a pilot
            // reads "how much is left" off a fill, not off a number. The flight view owns them while
            // piloting, because the vitals panel's hull/shield rows step aside then (#915). Hidden on an
            // EVA, where that panel shows them again.
            _hullBar = MakeStatusBar(_shipStatus, 30f, "HULL", HullBarCol);
            _shieldBar = MakeStatusBar(_shipStatus, 56f, "SHD", ShieldBarCol);

            // Flight instruments (bottom-left): smoothed speed, throttle, heading — and above them the altitude
            // over the flight plane (#1881), on a line of its own because the controls hint starts right after HDG.
            // Avionics-style abbreviations (SPD/THR/HDG/ALT) — identical in DE and EN cockpits.
            var instGo = new GameObject("Instruments", typeof(RectTransform));
            instGo.transform.SetParent(_ui.transform, false);
            var insRt = instGo.GetComponent<RectTransform>();
            insRt.anchorMin = insRt.anchorMax = new Vector2(0f, 0f);
            insRt.pivot = new Vector2(0f, 0f);
            insRt.sizeDelta = new Vector2(620f, 52f);
            insRt.anchoredPosition = new Vector2(20f, 20f);
            _instruments = instGo.AddComponent<Text>();
            _instruments.font = UiKit.Font;
            _instruments.fontSize = 19;
            _instruments.color = UiKit.Cyan;
            _instruments.alignment = TextAnchor.LowerLeft; // the SPD line stays where it always was
            _instruments.horizontalOverflow = HorizontalWrapMode.Overflow;
            _instruments.verticalOverflow = VerticalWrapMode.Overflow;
            _instruments.raycastTarget = false;

            // Aiming dot at screen centre — brightens cyan when the laser has a target locked.
            var chGo = new GameObject("Crosshair", typeof(RectTransform));
            chGo.transform.SetParent(_ui.transform, false);
            var chrt = chGo.GetComponent<RectTransform>();
            chrt.anchorMin = chrt.anchorMax = chrt.pivot = new Vector2(0.5f, 0.5f);
            chrt.sizeDelta = new Vector2(10f, 10f);
            chrt.anchoredPosition = Vector2.zero;
            _crosshair = chGo.AddComponent<Image>();
            _crosshair.sprite = UiKit.SolidSprite;
            _crosshair.color = new Color(0.6f, 0.7f, 0.8f, 0.35f);
            _crosshair.raycastTarget = false;

            // Ship-systems quick-bar: a bottom-centre container that holds the icon cells (1–9 select, LMB use).
            // The cells themselves are (re)built lazily in RefreshSystemSlots when the fitted-system count changes.
            _systemsBarRoot = new GameObject("SystemsBar", typeof(RectTransform));
            _systemsBarRoot.transform.SetParent(_ui.transform, false);
            var brt2 = _systemsBarRoot.GetComponent<RectTransform>();
            brt2.anchorMin = brt2.anchorMax = new Vector2(0.5f, 0f);
            brt2.pivot = new Vector2(0.5f, 0f);
            brt2.sizeDelta = new Vector2(66f, 66f);
            brt2.anchoredPosition = new Vector2(0f, 56f);
            _builtSystemCount = -1; // force a cell rebuild against the fresh container
        }

        /// <summary>Creates the chain of lens-flare sprites once (a big bloom at the sun + ghosts that march
        /// through the screen centre). Positioned + tinted each frame by <see cref="UpdateLensFlare"/>.</summary>
        private void EnsureFlare()
        {
            if (_flare.Count > 0 || _ui == null)
            {
                return;
            }

            if (_glowSprite == null)
            {
                var tex = GenerateGlowTexture();
                _glowSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < FlareT.Length; i++)
            {
                var go = new GameObject("Flare" + i, typeof(RectTransform));
                go.transform.SetParent(_ui.transform, false);
                var img = go.AddComponent<Image>();
                img.sprite = _glowSprite;
                img.raycastTarget = false;
                img.rectTransform.sizeDelta = new Vector2(FlareSize[i], FlareSize[i]);
                img.enabled = false;
                _flare.Add(img);
            }
        }

        /// <summary>Screen-space lens flare: a bloom on the sun plus ghost discs strung through the screen
        /// centre, brightening as you turn to look into the star. Hidden when the sun is off-screen/behind.</summary>
        private void UpdateLensFlare()
        {
            EnsureFlare();
            if (_sun == null || _flare.Count == 0)
            {
                return;
            }

            Vector3 sc = Camera.WorldToScreenPoint(_sun.position);
            bool onScreen = sc.z > 0f && sc.x >= 0f && sc.x <= Screen.width && sc.y >= 0f && sc.y <= Screen.height;
            if (!onScreen)
            {
                foreach (var g in _flare)
                {
                    g.enabled = false;
                }

                return;
            }

            var sun = new Vector2(sc.x, sc.y);
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float maxD = new Vector2(Screen.width, Screen.height).magnitude * 0.5f;
            float look = Mathf.Clamp01(1f - Vector2.Distance(sun, centre) / maxD); // 1 = looking straight at it

            for (int i = 0; i < _flare.Count; i++)
            {
                var g = _flare[i];
                Vector2 p = sun + (centre - sun) * FlareT[i]; // ghosts ride the sun→centre axis
                g.rectTransform.position = new Vector3(p.x, p.y, 0f);
                // The sun bloom (i==0) is always visible on-screen; the ghost chain swells as you look at it.
                float a = i == 0 ? FlareAlpha[0] * (0.5f + 0.5f * look) : FlareAlpha[i] * Mathf.Pow(look, 1.4f);
                // Desaturate toward white so the flare reads as glare, not a coloured screen wash.
                var col = Color.Lerp(_sunColor, Color.white, 0.5f);
                col.a = a;
                g.color = col;
                g.enabled = a > 0.004f;
            }
        }

        private void LateUpdate()
        {
            if (!_active)
            {
                if (_ui != null && _ui.enabled)
                {
                    _ui.enabled = false;
                }

                if (_fx != null && _fx.enabled)
                {
                    _fx.enabled = false; // a landing ends on full black — the fade must go with the view
                }

                return;
            }

            EnsureUi();
            // #2496: the flight HUD steps aside under a cutscene like the on-foot HUD (GameBootstrap.HudHidden — the T3 overlay
            // "Lift-off!" over the launch rise, the ending); the fade and the hit flash on their own canvas keep working.
            bool hud = !GameBootstrap.HudHidden;
            if (_ui.enabled != hud)
            {
                _ui.enabled = hud;
            }

            if (!_fx.enabled)
            {
                _fx.enabled = true;
            }

            // Entry-hatch beacon: a steady cyan glow normally, a strong pulse while on an EVA so you can find
            // your way back to the ship's hatch from a distance.
            if (_hatchMat != null)
            {
                float b = _eva ? 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f)) : 0.7f;
                _hatchMat.color = ShaderColor.Srgb(new Color(0.15f * b + 0.05f, 0.85f * b, 1f * b));
            }

            // Decay the hit feedback.
            _shake = Mathf.Max(0f, _shake - Time.deltaTime * 2.5f);
            _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime * 2f);
            _cargoFlash = Mathf.Max(0f, _cargoFlash - Time.deltaTime * 1.5f);
            _hit.color = new Color(1f, 0.2f, 0.2f, _hitFlash * 0.35f * (FxKit.ReduceFlashes ? 0.4f : 1f));

            // The vitals panel under us sheds its ship rows the moment we take the helm, so re-park the
            // status block every frame rather than only on entry (#915).
            PositionShipStatus();

            if (_phase != Phase.Cruise)
            {
                float dur = _phase == Phase.Boarding ? _boardDuration : (_phase == Phase.Landing ? LandDuration : SeqDuration);
                float t = Mathf.Clamp01(_seq / dur);
                float alpha;
                if (_phase == Phase.Landing)
                {
                    // Hold the view clear while the ship streaks away, then fade to black over the last stretch.
                    alpha = Mathf.Clamp01((t - 0.55f) / 0.45f);
                }
                else if (_phase == Phase.Boarding && _boardPath.Count >= 2)
                {
                    // #1917: watch the ship fly round to the hangar; fade only as it noses into the mouth.
                    alpha = Mathf.Clamp01((t - 0.75f) / 0.25f);
                }
                else if (_phase == Phase.Transit)
                {
                    alpha = 0f; // #1614: the warp's tunnel plays over the arrival — nothing to fade
                }
                else
                {
                    alpha = _phase == Phase.Boarding ? t : 1f - t;
                }

                _fade.color = new Color(0f, 0f, 0f, alpha);
                _hint.gameObject.SetActive(false);
                _board.gameObject.SetActive(false);
                _cargo.gameObject.SetActive(false);
            }
            else if (_confirmLand)
            {
                // Landing-pad chooser (item 38): the planet map of pads is its own clickable overlay (ShowLandMap);
                // here just clear the fade and show a one-line hint while the pad list streams in.
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                var pads = Game.LandingPadsBody == _choosePadBody ? Game.LandingPads : null;
                _hint.text = pads == null
                    ? (loc != null ? loc.Get("ui.space.pad_loading") : "Reading landing pads…")
                    : string.Empty; // the map shows the choices
                _hint.gameObject.SetActive(pads == null);
                _board.gameObject.SetActive(false);
                _cargo.gameObject.SetActive(false);
            }
            else if (_eva)
            {
                // Spacewalk HUD: float controls, a board prompt for the ship/station, and the oxygen lifeline.
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                // Device-aware controls hint: pad glyphs on a gamepad; blank on touch (the on-screen buttons
                // are self-labelling); the keyboard text otherwise — its keys are {key:Action} tokens since #2502,
                // expanded when the device, the language or a binding changes, not every frame. The pad line is a
                // template too since #2507: the player's buttons in their glyph set, an unbound one left out.
                var evaDevice = InputMap.ActiveDevice;
                var evaStamp = HudUi.GlyphStamp;
                if (_evaHint == null || evaDevice != _evaHintDevice || !ReferenceEquals(loc, _evaHintLoc) || !evaStamp.Equals(_evaHintStamp))
                {
                    _evaHintDevice = evaDevice;
                    _evaHintLoc = loc;
                    _evaHintStamp = evaStamp;
                    _evaHint = evaDevice switch
                    {
                        InputDeviceKind.Touch => string.Empty,
                        InputDeviceKind.Gamepad => loc != null ? HudUi.PadLine(loc, "ui.space.eva_controls_pad")
                            : "EVA — Left stick float · Right stick look · (A)/(B) up·down · (X) board",
                        _ => loc != null ? HudUi.ExpandKeyTokens(loc, loc.Get("ui.space.eva_controls"))
                            : "EVA — WASD/Mouse float · Space/Ctrl up/down · E board",
                    };
                }

                _hint.text = _evaHint;
                _hint.gameObject.SetActive(true);

                // The live binding (key or pad button) — and on a tablet the USE button, not the key letter (#2502).
                string useGlyph = HudUi.KeyTokenGlyph(loc, nameof(InputAction.Interact), prompt: true);
                var boardChoice = EvaBoardChoice(); // the same pick E makes (#2703)
                if (boardChoice == EvaBoardTarget.Station)
                {
                    string board = loc != null ? string.Format(loc.Get("ui.space.board_fmt"), useGlyph) : $"Press {useGlyph} to board";
                    _board.text = $"{board} {_nearStationName}";
                }
                else if (boardChoice == EvaBoardTarget.Ship)
                {
                    _board.text = loc != null
                        ? string.Format(loc.Get("ui.space.eva_board_ship_fmt"), useGlyph)
                        : $"Press {useGlyph} to board your ship";
                }
                else if (boardChoice == EvaBoardTarget.PartnerShip)
                {
                    _board.text = VisitPromptText(loc, "ui.visit.eva_board_fmt", "Press {key} to board {name}'s ship", useGlyph); // #2724
                }
                else
                {
                    // Always guide the player home: show how far the parked ship's hull is so they fly back and board it.
                    int dist = Mathf.RoundToInt(_evaShipDist);
                    string toShip = loc != null ? loc.Get("ui.space.eva_to_ship") : "Fly to your ship";
                    _board.text = $"{toShip} — {dist} m";
                }

                _board.gameObject.SetActive(true);

                int o2 = Mathf.CeilToInt(Mathf.Clamp(Game.Oxygen, 0f, 999f));
                string oxy = loc != null ? loc.Get("ui.space.eva_oxygen") : "O₂";
                _cargo.text = $"{oxy}: {o2}%";
                _cargo.color = Game.Oxygen <= 25f
                    ? Color.Lerp(new Color(1f, 0.3f, 0.2f), Color.white, Mathf.PingPong(Time.time * 2f, 1f) * 0.6f)
                    : UiKit.TextCol;
                _cargo.gameObject.SetActive(true);
            }
            else
            {
                _fade.color = new Color(0f, 0f, 0f, 0f);
                var loc = Game.Localizer;
                // Device-aware controls hint (pad glyphs on a gamepad — composed from the player's buttons and glyph
                // set since #2507; blank on touch — the on-screen buttons are self-labelling; the keyboard text otherwise).
                // #1516: the hint depends only on (device, autopilot tier, localizer) — build it when one of
                // those changes and assign once. Assigning the base and appending with `+=` every frame dirtied
                // the Text mesh each frame although the final string never changed.
                var device = InputMap.ActiveDevice;
                bool autopilot = Game.AiCoreTier >= 2;
                var stamp = HudUi.GlyphStamp; // #2502: a rebind or another pad glyph set re-expands the {key:Action} tokens
                if (_flightHint == null || device != _flightHintDevice || autopilot != _flightHintAutopilot || !ReferenceEquals(loc, _flightHintLoc)
                    || !stamp.Equals(_flightHintStamp))
                {
                    _flightHintDevice = device;
                    _flightHintAutopilot = autopilot;
                    _flightHintLoc = loc;
                    _flightHintStamp = stamp;
                    string hint = device switch
                    {
                        InputDeviceKind.Touch => string.Empty,
                        InputDeviceKind.Gamepad => loc != null ? HudUi.PadLine(loc, "ui.space.controls_pad")
                            : "Left stick fly · Right stick steer · RB fire · (X) land/dock · (Y) view",
                        _ => loc != null ? HudUi.ExpandKeyTokens(loc, loc.Get("ui.space.controls")).Replace("{feedback_key}", FeedbackUi.HotkeyName)
                            : "WASD/Mouse fly · V view · E land/dock · L return · G EVA",
                    };
                    // #2502: the autopilot and chart hints name the bound key / the pad's Actions list — the pad line used to
                    // read the keyboard's "[P] Autopilot · [M] Chart".
                    if (autopilot && device != InputDeviceKind.Touch)
                    {
                        hint += " · " + HudUi.ExpandKeyTokens(loc, Loc("ui.vega.autopilot.hint", "{key:FlightAutopilot} autopilot"));
                    }

                    if (device != InputDeviceKind.Touch)
                    {
                        hint += " · " + HudUi.ExpandKeyTokens(loc, Loc("ui.spacemap.key_hint", "{key:FlightMap} chart")); // system chart (#597)
                    }

                    _flightHint = hint;
                }

                _hint.text = _flightHint;
                _hint.gameObject.SetActive(true);

                // Prompt what E does (CruiseInteractChoice — the same pick the key makes, #2934): fly through the wormhole
                // (#2242: the rift wins), go over to the docked friend's ship or ask the friend at the helm (#2724), dock the
                // station or land on the body you've flown up to — whichever you're closest to — or just say that the friend
                // is flying. Labelled with the live Interact binding (keyboard key or pad glyph; a tablet's USE button, #2502).
                string useGlyph = HudUi.KeyTokenGlyph(loc, nameof(InputAction.Interact), prompt: true);
                var interact = CruiseInteractChoice();
                switch (interact)
                {
                    case CruiseInteractTarget.Wormhole:
                        _board.text = loc != null ? string.Format(loc.Get("ui.space.wormhole_prompt_fmt"), useGlyph) : $"Press {useGlyph} to fly through the wormhole";
                        break;
                    case CruiseInteractTarget.GoOver:
                        _board.text = VisitPromptText(loc, "ui.visit.go_over_fmt", "Press {key} to go over to {name}'s ship", useGlyph);
                        break;
                    case CruiseInteractTarget.AskPartner:
                        _board.text = VisitPromptText(loc, "ui.visit.flying_ask", "{name} is flying · {key}: ask to come over", useGlyph);
                        break;
                    case CruiseInteractTarget.Station:
                        string board = loc != null ? string.Format(loc.Get("ui.space.board_fmt"), useGlyph) : $"Press {useGlyph} to board";
                        _board.text = $"{board} {_nearStationName}";
                        break;
                    case CruiseInteractTarget.Body:
                        string land = loc != null ? string.Format(loc.Get("ui.space.land_prompt_fmt"), useGlyph) : $"Press {useGlyph} to land on";
                        _board.text = $"{land} {_landTargetName}";
                        break;
                    case CruiseInteractTarget.PartnerFlying:
                        _board.text = VisitPromptText(loc, "ui.visit.flying", "{name} is flying", useGlyph);
                        break;
                }

                _board.gameObject.SetActive(interact != CruiseInteractTarget.None);

                string cargoLabel = loc != null ? loc.Get("ui.space.cargo") : "Cargo";
                int cargoCount = Game.Cargo.Length;
                if (_cargoText == null || cargoCount != _cargoTextCount || !ReferenceEquals(cargoLabel, _cargoTextLabel))
                {
                    _cargoTextCount = cargoCount;
                    _cargoTextLabel = cargoLabel;
                    _cargoText = $"{cargoLabel}: {cargoCount}"; // #1516: formatted on change, not per frame
                }

                _cargo.text = _cargoText;
                _cargo.color = Color.Lerp(UiKit.TextCol, UiKit.Cyan, _cargoFlash);
                _cargo.gameObject.SetActive(true);
            }

            // The controls line and the "Press E to board/land" prompt advertise keys that are dead while a
            // menu-style dialog (feedback, chart, Tab menu) owns the screen (#1858) — drop them the way HudUi
            // drops its whole canvas under MenuOpen. The pad chooser is a cursor-only owner and keeps its line.
            if (Game.MenuOpen)
            {
                _hint.gameObject.SetActive(false);
                _board.gameObject.SetActive(false);
            }

            UpdateInstruments();
            UpdateShipBars();

            // Lens flare only during free flight (hidden behind the launch/landing/boarding fades + EVA).
            if (_phase == Phase.Cruise && !_eva)
            {
                UpdateLensFlare();
            }
            else
            {
                foreach (var g in _flare)
                {
                    g.enabled = false;
                }
            }

            // Aiming dot: shown in free flight, cyan when the laser has a target locked — red while that target is the
            // locked enemy (#2277: "shoot now").
            if (_crosshair != null)
            {
                bool show = _phase == Phase.Cruise && !_confirmLand && !_eva;
                _crosshair.enabled = show;
                if (show)
                {
                    _crosshair.color = LockedEnemyInSights()
                        ? new Color(1f, 0.35f, 0.35f, 0.95f)
                        : _fireTargetId != null
                        ? new Color(0.5f, 1f, 1f, 0.9f)
                        : new Color(0.6f, 0.7f, 0.8f, 0.35f);
                }
            }

            // Ship-systems quick-bar: icon cells, the selected one bright + scaled (same look as the hotbar).
            RefreshSystemSlots();

            // Floating nameplates over the other pilots so you can tell who is who out here (item 385).
            if (_phase == Phase.Cruise)
            {
                DrawRemoteNameplates();
                DrawEntityHealthBars();
            }

            DrawTargetHud(); // #2277/#2283: lock frame or edge arrow, threat ticks, waypoint arrow (free flight only)
        }

        /// <summary>Floating health bars over space hostiles (#692): drones, UFOs, cruisers and bandit
        /// ships — asteroids (mining targets) and stations are excluded. The shared policy in
        /// <see cref="EnemyHealthBars"/> shows a bar while an entity is in combat or is the current fire
        /// lock, so a quiet field stays clean. Fade band matches the pilot nameplates.</summary>
        private void DrawEntityHealthBars()
        {
            var space = Game.Space;
            if (Camera == null || space == null)
            {
                return;
            }

            foreach (var e in space.Entities)
            {
                // #2277: the locked target always shows its bar — a locked rock or wreck too (its hull is what the beam carves).
                bool locked = IsLockedEntity(e.Id);
                bool hostileShip = BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsHostileShipKind(e.Kind);
                if (!hostileShip && !(locked && e.HullMax > 0f && BlocksBeyondTheStars.Client.Core.SpaceTargeting.IsFireTargetKind(e.Kind)))
                {
                    continue;
                }

                Vector3 at;
                if (_entities.TryGetValue(e.Id, out var go) && go != null)
                {
                    at = go.transform.position + Vector3.up * (2f * Mathf.Max(1f, e.Scale));
                }
                else if (locked && _structs.TryGetValue(e.Id, out var body) && body != null && _root != null)
                {
                    at = _root.transform.TransformPoint(body.Pos) + Vector3.up * (TargetRadius(e) + 1.5f); // a voxel rock / wreck
                }
                else
                {
                    continue;
                }

                EnemyHealthBars.Push(Game, Camera, e.Id, at,
                    e.Hull, e.HullMax, friendly: false, fadeStart: 90f, fadeEnd: 140f,
                    targeted: e.Id == _fireTargetId || locked);
            }
        }

        /// <summary>Draws a floating name label over each other pilot's ship/suit — the flight-view counterpart to
        /// the ground nameplates (<see cref="RemotePlayers"/>). Names fade with distance (roughly radar range) so
        /// nearby mates stay identifiable without cluttering the far field. The synthetic NPC-trader poses that ride the
        /// same remote-ship path carry their pilot's name (the server's <c>AppendTraderPoses</c>), so traders are
        /// labelled too; only a pose with an empty name is skipped.</summary>
        private void DrawRemoteNameplates()
        {
            if (Camera == null || _remotePlayers.Count == 0)
            {
                return;
            }

            var labels = ScreenLabelLayer.Instance;
            foreach (var av in _remotePlayers.Values)
            {
                if (av.Root == null || string.IsNullOrEmpty(av.Name))
                {
                    continue;
                }

                // ~2 m above the hull clears both the half-scale flight ship and a floating EVA suit.
                string plate = av.Parked ? av.Name + " · " + Loc("ui.space.parked", "parked") : av.Name; // #2431: engines off, pilot inside
                labels.World(Camera, av.Root.transform.position + Vector3.up * 2f, plate, UiKit.TextCol, false, 90f, 140f);
            }
        }

        /// <summary>Draws the ship-systems quick-bar as icon cells (laser / tractor / …) matching the on-foot
        /// hotbar: the active system is bright + scaled with a selection ring, each cell shows its icon, hotkey
        /// number and name. Cells are rebuilt only when the fitted-system count changes (cheap each frame).</summary>
        private void RefreshSystemSlots()
        {
            if (_systemsBarRoot == null)
            {
                return;
            }

            bool show = _phase == Phase.Cruise && !_confirmLand && !_eva && _systems.Count > 0;
            if (_systemsBarRoot.activeSelf != show)
            {
                _systemsBarRoot.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            if (_systems.Count != _builtSystemCount)
            {
                BuildSystemSlots();
            }

            for (int i = 0; i < _systemSlots.Length; i++)
            {
                var s = _systemSlots[i];
                bool sel = i == _selectedSystem;
                UiKit.StyleQuickSlot(s, sel);
                s.Num.text = (i + 1).ToString();

                var sys = _systems[i];
                string iconKey = sys.Kind == "tractor" ? "tractor_beam" : sys.WeaponKey ?? FlightWeapon;
                var sprite = iconKey != null ? IconResolver.Resolve(iconKey, Game) : null;
                if (sprite != null && sprite.texture != null)
                {
                    var r = sprite.textureRect;
                    s.Icon.texture = sprite.texture;
                    s.Icon.uvRect = new Rect(r.x / sprite.texture.width, r.y / sprite.texture.height,
                        r.width / sprite.texture.width, r.height / sprite.texture.height);
                    s.Icon.color = Color.white;
                    s.Icon.enabled = true;
                }
                else
                {
                    s.Icon.enabled = false;
                }

                s.Name.text = sys.Label;
                s.Name.color = sel ? UiKit.Cyan : UiKit.TextCol;
            }
        }

        /// <summary>(Re)builds the quick-bar cells to match the fitted-system count, centred on a backplate.</summary>
        private void BuildSystemSlots()
        {
            for (int i = _systemsBarRoot.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_systemsBarRoot.transform.GetChild(i).gameObject);
            }

            int n = Mathf.Max(_systems.Count, 1);
            const float sw = 66f, pitch = 74f;
            float stripW = (n - 1) * pitch + sw;
            ((RectTransform)_systemsBarRoot.transform).sizeDelta = new Vector2(stripW, sw);
            UiKit.QuickBackplate(_systemsBarRoot.transform, -12f, -10f, stripW + 24f, sw + 20f);
            _systemSlots = new UiKit.QuickSlot[n];
            for (int i = 0; i < n; i++)
            {
                _systemSlots[i] = UiKit.MakeQuickSlot(_systemsBarRoot.transform, i * pitch, 0f, sw);
            }

            _builtSystemCount = _systems.Count;
        }

        /// <summary>Flight instruments: smoothed speed (camera delta — the camera rides the ship),
        /// throttle, heading, plus hull/shield while combat data is present. Shown in cruise only.</summary>
        /// <summary>Parks the ship-status block just below the on-foot HUD's vitals panel.
        ///
        /// That panel belongs to <see cref="HudUi"/> — a DIFFERENT canvas, drawn under this overlay
        /// (sortingOrder 10 vs 12) but built at the same reference resolution, so the two share one
        /// coordinate space and this overlay's text wins the depth test wherever they meet. The readout
        /// used to sit at a hard-coded y that cleared the panel as it looks ON FOOT; in flight the panel
        /// grows by its hull/shield rows and the label landed straight on the hull bar (#915). Reading the
        /// panel's live bottom edge instead means it follows whatever the panel is currently showing.</summary>
        private void PositionShipStatus()
        {
            if (_shipStatus == null)
            {
                return;
            }

            float y = -(HudUi.VitalsBottomY + ReadoutGap);
            if (!Mathf.Approximately(_shipStatus.anchoredPosition.y, y))
            {
                _shipStatus.anchoredPosition = new Vector2(20f, y);
            }
        }

        // A hull/shield gauge in the flight HUD: caption, track, fill and the raw value on top of it —
        // the vitals panel's row style, so the ship's bars read as a continuation of the suit's.
        private struct StatusBar
        {
            public GameObject Go;
            public Image Fill;
            public Text Value;
        }

        private const float StatusWidth = 320f;   // ship-status block width (the cargo line overflows freely)
        private const float StatusBarWidth = 178f; // matches the vitals rows above, so the columns line up

        private static readonly Color HullBarCol = new Color(0.6f, 0.66f, 0.74f);
        private static readonly Color ShieldBarCol = new Color(0.4f, 0.7f, 1f);
        private static readonly Color BarWarn = new Color(1f, 0.25f, 0.2f);

        private static StatusBar MakeStatusBar(Transform parent, float y, string caption, Color color)
        {
            var go = new GameObject("Bar_" + caption, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, 0f, y, StatusWidth, 20f);
            UiKit.AddText(go.transform, 0f, 2f, 44f, 16f, caption, 13, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddImage(go.transform, 46f, 2f, StatusBarWidth, 16f, UiKit.SolidSprite, new Color(0.03f, 0.07f, 0.13f, 0.9f));
            var fill = UiKit.AddImage(go.transform, 46f, 2f, StatusBarWidth, 16f, UiKit.SolidSprite, color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            var value = UiKit.AddText(go.transform, 52f, 2f, StatusBarWidth - 12f, 16f, string.Empty, 12,
                UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            return new StatusBar { Go = go, Fill = fill, Value = value };
        }

        /// <summary>Drives one gauge. A system in the warning band blinks toward red on the same curve the
        /// vitals rows use, so the cue is the one the player already learned on foot.</summary>
        private static void SetStatusBar(StatusBar bar, float frac, float value, Color baseColor, bool warn)
        {
            if (bar.Fill == null)
            {
                return;
            }

            bar.Fill.fillAmount = Mathf.Clamp01(frac);
            bar.Value.text = Mathf.CeilToInt(value).ToString();
            float k = UiKit.ReducedMotion ? 1f : Mathf.PingPong(Time.time * 2.4f, 1f);
            bar.Fill.color = warn ? Color.Lerp(baseColor, BarWarn, 0.35f + 0.65f * k) : baseColor;
        }

        /// <summary>Updates the hull/shield gauges and their alarm. Shown only while actually piloting: on
        /// an EVA (and during the launch/landing sequences) the on-foot vitals panel carries hull + shield
        /// itself, and two copies of the same bar is what this whole change is undoing (#915).</summary>
        private void UpdateShipBars()
        {
            var combat = Game?.ShipCombat;
            bool show = _phase == Phase.Cruise && !_eva && combat != null;
            if (_hullBar.Go != null && _hullBar.Go.activeSelf != show)
            {
                _hullBar.Go.SetActive(show);
                _shieldBar.Go.SetActive(show);
            }

            if (!show)
            {
                _hullWarn = _shieldWarn = false;
                _hullBeepTimer = 0f;
                return;
            }

            // Same thresholds as the vitals panel (#753): trips below 10 %, clears above 15 %, so a value
            // sitting on the edge can't flicker the alarm on and off. Unlike the panel these skip a system
            // the ship doesn't HAVE (Max 0) — a shieldless hull would otherwise alarm forever.
            bool alive = Game != null && Game.Health > 0f;
            float hullFrac = combat.HullMax > 0f ? combat.Hull / combat.HullMax : 0f;
            float shieldFrac = combat.ShieldMax > 0f ? combat.Shield / combat.ShieldMax : 0f;
            _hullWarn = alive && combat.HullMax > 0f && hullFrac < (_hullWarn ? 0.15f : 0.10f);
            _shieldWarn = alive && combat.ShieldMax > 0f && shieldFrac < (_shieldWarn ? 0.15f : 0.10f);

            SetStatusBar(_hullBar, hullFrac, combat.Hull, HullBarCol, _hullWarn);
            SetStatusBar(_shieldBar, shieldFrac, combat.Shield, ShieldBarCol, _shieldWarn);
            UpdateHullAlarm(Mathf.Max(Time.deltaTime, 1e-4f));
        }

        private void UpdateInstruments()
        {
            if (_instruments == null)
            {
                return;
            }

            bool show = _phase == Phase.Cruise && !_eva && Camera != null;
            if (_instruments.gameObject.activeSelf != show)
            {
                _instruments.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float raw = (Camera.transform.position - _instLastCamPos).magnitude / dt;
            _instLastCamPos = Camera.transform.position;
            _instSpeed = Mathf.Lerp(_instSpeed, Mathf.Min(raw, 999f), 0.15f);

            int thr = Mathf.RoundToInt(Mathf.Clamp01(InputMap.MoveY()) * 100f);
            int hdg = Mathf.RoundToInt(Mathf.Repeat(_yaw, 360f));
            int spd10 = Mathf.RoundToInt(_instSpeed * 10f);
            // #1881: height over the flight plane (y = 0 — where the system's planets, stations and wreck sit), in
            // the same instrument kilometres as every flight distance. Free pitch put pilots hundreds of units under
            // everything without a single number saying so.
            float altY = _ship != null ? _ship.transform.localPosition.y : 0f;
            int altKm = BlocksBeyondTheStars.Client.Core.SpaceRadarMath.SignedKm(altY);
            // Hull/shield are NOT repeated here — they're gauges in the ship-status block now (#915).
            // #1516: format only when a displayed digit changes (the readout shows one decimal of speed).
            if (spd10 != _instLastSpd10 || thr != _instLastThr || hdg != _instLastHdg || altKm != _instLastAltKm)
            {
                _instLastSpd10 = spd10;
                _instLastThr = thr;
                _instLastHdg = hdg;
                _instLastAltKm = altKm;
                string alt = BlocksBeyondTheStars.Client.Core.SpaceRadarMath.Altitude(altY, Loc("ui.space.km_fmt", null));
                _instruments.text = $"ALT {alt}\nSPD {_instSpeed:0.0}   THR {thr}%   HDG {hdg:000}°";
            }
        }

        // #1516: last-formatted flight overlay state (hint, cargo line, instruments).
        private string _flightHint;
        private InputDeviceKind _flightHintDevice;
        private bool _flightHintAutopilot;
        private object _flightHintLoc;
        private (ClientSettings, int, PadGlyphSet, bool) _flightHintStamp;

        // #2502: the EVA controls line, cached like the flight line (its keys are {key:Action} tokens now).
        private string _evaHint;
        private InputDeviceKind _evaHintDevice;
        private object _evaHintLoc;
        private (ClientSettings, int, PadGlyphSet, bool) _evaHintStamp;
        private string _cargoText;
        private int _cargoTextCount = -1;
        private string _cargoTextLabel;
        private int _instLastSpd10 = -1, _instLastThr = -1, _instLastHdg = -1, _instLastAltKm = int.MinValue;

        // #2724: the visit prompt ("go over to Lena's ship", "Lena is flying"), formatted only when it changes.
        private string _visitPrompt, _visitPromptKey, _visitPromptName, _visitPromptGlyph;
        private object _visitPromptLoc;

        /// <summary>#2724: the visit prompt <paramref name="key"/> with the use glyph (<c>{key}</c>) and the docked partner's
        /// name (<c>{name}</c>) filled in — re-formatted only when the line, the partner, the glyph or the language changes.</summary>
        private string VisitPromptText(BlocksBeyondTheStars.Shared.Localization.Localizer loc, string key, string fallback, string glyph)
        {
            string name = _visit.PartnerName;
            if (_visitPrompt == null || !ReferenceEquals(key, _visitPromptKey) || !ReferenceEquals(loc, _visitPromptLoc)
                || !string.Equals(name, _visitPromptName, System.StringComparison.Ordinal)
                || !string.Equals(glyph, _visitPromptGlyph, System.StringComparison.Ordinal))
            {
                _visitPromptKey = key;
                _visitPromptLoc = loc;
                _visitPromptName = name;
                _visitPromptGlyph = glyph;
                string template = loc != null ? loc.Get(key) : fallback;
                _visitPrompt = template.Replace("{key}", glyph ?? string.Empty).Replace("{name}", name);
            }

            return _visitPrompt;
        }

        private bool _hullWarn, _shieldWarn;
        private float _hullBeepTimer;

        /// <summary>Drives the low-systems beep at the vitals panel's cadence, so the ship gauges warn by
        /// ear exactly like the suit bars they replace while piloting.</summary>
        private void UpdateHullAlarm(float dt)
        {
            if (!_hullWarn && !_shieldWarn)
            {
                _hullBeepTimer = 0f; // first beep fires immediately when a system next runs low
                return;
            }

            _hullBeepTimer -= dt;
            if (_hullBeepTimer <= 0f)
            {
                _hullBeepTimer = 2.5f;
                ClientAudio.Instance?.Cue("vitals_warning", 0.5f);
            }
        }
    }
}
