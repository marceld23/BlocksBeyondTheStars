// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship scanner in the flight hotbar (#2237–#2240). Every ship has one (the cockpit is tier 1); the fitted
    /// Deep / Quantum scanner raises the tier — shorter hold, longer reach, the Quantum scanner's system ping. Select
    /// it like a weapon, point the nose at something and HOLD the fire button until the ring is full: four brackets
    /// lock onto the target, a light fan reaches from the nose, a hologram box and a scan plane sweep it, data motes
    /// flow back — then a ring pulse and the readout. Space objects go to the server as <c>ScanEntityIntent</c>, planets
    /// as <c>PlanetScanIntent</c> (the overview card opens). Everything here is cosmetic; the server checks range and
    /// decides what the scan reveals.
    /// </summary>
    public sealed partial class SpaceView
    {
        private struct ScanTarget
        {
            public string Key;        // "e:<entity id>", "b:<body id>" or null (nothing locked)
            public string Id;         // the entity id or the body id ("" = the body the flight is anchored on)
            public bool IsBody;
            public string Kind;       // the entity kind (NetCombatEntity.Kind), "Body" for a planet
            public string Name;       // the lock-on label
            public Vector3 Local;     // instance-frame position (_root local)
            public float Radius;      // instance-frame radius (bracket size, effect box)
            public bool Hostile;
        }

        private string _scanTargetKey;
        private float _scanProgress;
        private float _scanCooldown;
        private float _scanFxTimer;
        private float _scanPlaneTimer;
        private float _scanLockAge;
        private bool _scanCharging;
        private float _scannerHintTimer = 6f;
        private float _scanLetGoTimer; // #2247: seconds the "keep holding" line stays up after an early release
        private readonly HashSet<string> _scannedThisFlight = new HashSet<string>();
        private readonly HashSet<string> _anomalyHinted = new HashSet<string>();

        // Lock-on HUD: the shared bracket frame (#2283) with the scanner's charge ring, on the targeting layer.
        private SpaceTargetFrame _scanFrame;

        /// <summary>Within this, the autopilot / waypoint arrives at a life pod, an anomaly or a wormhole.</summary>
        private const float EncounterArriveRange = 22f;

        /// <summary>The scanner of the ship flown right now — the same rule the server applies (#2240).</summary>
        private ShipScannerSpec CurrentScanner()
            => ShipScannerRules.For(Game.ShipCombat?.Modules ?? System.Array.Empty<string>(), k => Game.Content?.GetShipModule(k));

        /// <summary>The encounter kinds that never move — rendered at their position, no snapshot buffer.</summary>
        private static bool IsStaticEncounter(string kind) => SpaceTargeting.IsStaticEncounterKind(kind);

        /// <summary>The encounter kinds a waypoint / the VEGA autopilot can fly to.</summary>
        private static bool IsEncounterWaypointKind(string kind) => IsStaticEncounter(kind);

        /// <summary>The space object kinds the scanner reads (mirrors the server's list; one list, #2277).</summary>
        private static bool IsScannableKind(string kind) => SpaceTargeting.IsScannableKind(kind);

        /// <summary>One frame of the selected scanner: lock, hold, read.</summary>
        private void UpdateScanner(float dt)
        {
            if (_ship == null || _root == null || Game == null)
            {
                return;
            }

            var scanner = CurrentScanner();
            var look = FxLook.ForModule(Game.Content, scanner.ModuleKey);
            _scanCooldown -= dt;
            _scanLetGoTimer -= dt;

            // #2247: a scan in progress keeps its target while the nose stays on it — an asteroid drifting through the
            // reticle used to snatch the lock and start the ring over.
            var target = BestScanTarget(scanner, _scanCharging ? _scanTargetKey : null);
            bool canPing = scanner.Tier >= 3 && target.Key == null; // #2240: the Quantum scanner reads the whole system
            string key = target.Key ?? (canPing ? "ping" : null);
            _fireTargetId = target.IsBody ? null : target.Id;

            if (key != _scanTargetKey)
            {
                _scanTargetKey = key;
                _scanProgress = 0f;
                _scanCharging = false;
                _scanLockAge = 0f;
                if (target.Key != null)
                {
                    ClientAudio.Instance?.Cue("ship_scan_lock", 0.45f);
                }
            }

            _scanLockAge += dt;
            bool held = InputMap.PrimaryHeld();
            if (key != null && held && _scanCooldown <= 0f)
            {
                if (!_scanCharging)
                {
                    _scanCharging = true;
                    _scanFxTimer = 0f;
                    _scanPlaneTimer = 0f;
                    ClientAudio.Instance?.Cue("ship_scan_charge", 0.6f);
                    if (target.Key != null)
                    {
                        var center = _root.transform.TransformPoint(target.Local);
                        FxGadgets.HoloBox(look.Color, center, Vector3.one * target.Radius * 2f, Mathf.Max(0.3f, scanner.ScanTime));
                    }
                }

                _scanProgress += dt / Mathf.Max(0.2f, scanner.ScanTime);
                ChargeFx(target, look, canPing, dt);
                if (_scanProgress >= 1f)
                {
                    CompleteScan(target, look, canPing);
                    _scanProgress = 0f;
                    _scanCharging = false;
                    _scanCooldown = canPing ? 8f : 0.45f;
                }
            }
            else if (!held)
            {
                if (_scanCharging && _scanProgress > 0.05f && _scanProgress < 1f)
                {
                    OnScanLetGoEarly(); // #2247: the ring was not full — say why nothing happened
                }

                _scanProgress = 0f;
                _scanCharging = false;
            }

            DrawScanLock(target, look, canPing);
        }

        /// <summary>#2247: the trigger came up before the ring was full. The scan never reached the server, so nothing
        /// appears — the one thing a player could not tell from the screen. The lock label says "keep holding" for a
        /// moment every time, and VEGA explains it once.</summary>
        private void OnScanLetGoEarly()
        {
            _scanLetGoTimer = 2.5f;
            var settings = Game.Settings;
            if (settings != null && !settings.ShipScanHoldHintShown)
            {
                settings.ShipScanHoldHintShown = true;
                settings.Save();
                VegaPanel.Instance?.SayLocal("vega.hint.ship_scan_hold");
            }
        }

        /// <summary>The fire control's name on the active device — what the "hold … to scan" line asks for.</summary>
        private string FireGlyph() => InputMap.ActiveDevice switch
        {
            InputDeviceKind.Gamepad => InputMap.PadGlyph(KeyCode.JoystickButton5) ?? "RB",
            InputDeviceKind.Touch => Loc("ui.touch.fire", "FIRE"),
            _ => Loc("ui.key.mouse_left", "LMB"),
        };

        /// <summary>Cancels a scan in progress and hides the lock-on HUD (another system selected, view closed).</summary>
        private void StopScanner()
        {
            _scanTargetKey = null;
            _scanProgress = 0f;
            _scanCharging = false;
            _scanFrame?.Hide();
        }

        /// <summary>The target ahead: a scannable space object within the scanner's range, else a body of the system the
        /// nose points at (any distance — planets are read from afar). A target whose apparent size covers the aim line
        /// counts too, so a big planet is easy to lock and a small rock needs the nose on it. <paramref name="keep"/> is
        /// the target of a scan in progress (#2247): it stays locked while it is still in reach and within a wider cone,
        /// whatever else drifts into the aim line meanwhile. The flight target lock (#2277) comes last: with nothing on the
        /// nose, a locked object the scanner can read, in its range, is scanned without precise aiming — the server only
        /// checks the range. The nose always wins over the lock, so an auto-lock on an attacker never takes the scanner
        /// away from what the player is pointing at.</summary>
        private ScanTarget BestScanTarget(ShipScannerSpec scanner, string keep)
        {
            var best = new ScanTarget();
            Vector3 shipPos = _ship.transform.localPosition;
            Vector3 fwd = _ship.transform.localRotation * Vector3.forward;
            float cone = Game.AutoAimOn ? 12f : 4f; // degrees of slack around the aim line
            float bestScore = float.MaxValue;
            if (keep != null && TryKeepScanTarget(scanner, keep, shipPos, fwd, cone * 1.5f + 4f, out var kept))
            {
                return kept;
            }

            var space = Game.Space;
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    if (!IsScannableKind(e.Kind))
                    {
                        continue;
                    }

                    var pos = new Vector3(e.X, e.Y, e.Z);
                    Vector3 to = pos - shipPos;
                    float dist = to.magnitude;
                    if (dist > scanner.Range || dist < 0.01f)
                    {
                        continue;
                    }

                    float radius = TargetRadius(e);
                    float angle = Vector3.Angle(fwd, to);
                    float apparent = Mathf.Atan2(radius, dist) * Mathf.Rad2Deg;
                    if (angle > cone + apparent)
                    {
                        continue;
                    }

                    float score = angle - apparent; // smaller = better aligned
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new ScanTarget
                        {
                            Key = "e:" + e.Id,
                            Id = e.Id,
                            Kind = e.Kind,
                            Name = TargetName(e),
                            Local = pos,
                            Radius = radius,
                            Hostile = e.Hostile,
                        };
                    }
                }
            }

            // A space object right on the nose wins; only with none locked do the planets get a chance.
            if (best.Key != null)
            {
                return best;
            }

            foreach (var body in _landables)
            {
                Vector3 to = body.Pos - shipPos;
                float dist = to.magnitude;
                if (dist < 0.01f)
                {
                    continue;
                }

                float angle = Vector3.Angle(fwd, to);
                float apparent = Mathf.Atan2(body.Radius, dist) * Mathf.Rad2Deg;
                if (angle > cone + apparent)
                {
                    continue;
                }

                float score = angle - apparent;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new ScanTarget
                    {
                        Key = "b:" + body.Id,
                        Id = body.Id ?? string.Empty,
                        IsBody = true,
                        Kind = "Body",
                        Name = body.Name,
                        Local = body.Pos,
                        Radius = body.Radius,
                    };
                }
            }

            // Nothing on the nose: the locked object, if the scanner can read it from here.
            if (best.Key == null && TryLockedScanTarget(scanner, shipPos, out var locked))
            {
                return locked;
            }

            return best;
        }

        /// <summary>#2247: the locked target of a scan in progress, if it still qualifies under <paramref name="cone"/>
        /// (wider than the pick cone, so a steady hand never loses it).</summary>
        private bool TryKeepScanTarget(ShipScannerSpec scanner, string key, Vector3 shipPos, Vector3 fwd, float cone, out ScanTarget kept)
        {
            kept = default;
            if (key.StartsWith("e:", System.StringComparison.Ordinal))
            {
                string id = key.Substring(2);
                var space = Game.Space;
                if (space == null)
                {
                    return false;
                }

                foreach (var e in space.Entities)
                {
                    if (e.Id != id || !IsScannableKind(e.Kind))
                    {
                        continue;
                    }

                    var pos = new Vector3(e.X, e.Y, e.Z);
                    Vector3 to = pos - shipPos;
                    float dist = to.magnitude;
                    float radius = TargetRadius(e);
                    if (dist > scanner.Range || dist < 0.01f
                        || Vector3.Angle(fwd, to) > cone + Mathf.Atan2(radius, dist) * Mathf.Rad2Deg)
                    {
                        return false;
                    }

                    kept = new ScanTarget
                    {
                        Key = key,
                        Id = e.Id,
                        Kind = e.Kind,
                        Name = TargetName(e),
                        Local = pos,
                        Radius = radius,
                        Hostile = e.Hostile,
                    };
                    return true;
                }

                return false;
            }

            if (key.StartsWith("b:", System.StringComparison.Ordinal))
            {
                string id = key.Substring(2);
                foreach (var body in _landables)
                {
                    if ((body.Id ?? string.Empty) != id)
                    {
                        continue;
                    }

                    Vector3 to = body.Pos - shipPos;
                    float dist = to.magnitude;
                    if (dist < 0.01f || Vector3.Angle(fwd, to) > cone + Mathf.Atan2(body.Radius, dist) * Mathf.Rad2Deg)
                    {
                        return false;
                    }

                    kept = new ScanTarget
                    {
                        Key = key,
                        Id = id,
                        IsBody = true,
                        Kind = "Body",
                        Name = body.Name,
                        Local = body.Pos,
                        Radius = body.Radius,
                    };
                    return true;
                }
            }

            return false;
        }

        /// <summary>An entity's rough size in the flight frame (brackets, effect box, apparent size). An instance method
        /// since #2357: a raider's radius follows the voxel design cached for it.</summary>
        private float TargetRadius(NetCombatEntity e)
        {
            float s = Mathf.Max(1f, e.Scale);
            switch (e.Kind)
            {
                case "Asteroid": return 4f * s;
                case "Wreck": return 7f * s;
                case "SpaceStation": return 12f * s;
                case "Wormhole": return 22f;
                case "Anomaly": return 3.5f;
                case "EscapePod": return 2.5f;
                case "Cruiser": return 4.5f;
                case "DebrisField": return 6f;    // #2353: the recorder's beacon, roomy brackets
                case "Debris": return 2f;         // #2353: a few cells of plating
                case "SalvageCapsule": return 2f;
                case "BanditShip": return RaiderDesignRadius(e); // #2357: the real hull's extent, else the wedge's
                default: return 2.5f * s;
            }
        }

        /// <summary>What the lock-on label calls an entity.</summary>
        private string TargetName(NetCombatEntity e)
        {
            switch (e.Kind)
            {
                case "Asteroid": return Loc("ui.scan.subject.asteroid", "Asteroid");
                case "Anomaly": return Loc("ui.scan.subject.anomaly", "Anomaly");
                case "EscapePod": return Loc("ui.scan.subject.escape_pod", "Life pod") + (string.IsNullOrEmpty(e.Name) ? string.Empty : ": " + e.Name);
                case "Drone": return Loc("ui.scan.subject.drone", "Guardian drone");
                case "Ufo": return Loc("ui.scan.subject.ufo", "Guardian saucer");
                case "Cruiser": return Loc("ui.scan.subject.cruiser", "Guardian cruiser");
                case "BanditShip": return string.IsNullOrEmpty(e.Name) ? Loc("ui.scan.subject.bandit_ship", "Raider ship") : e.Name;
                case "Wormhole": return WormholeLabel(e.Id);
                case "DebrisField": return Loc("ui.scan.subject.debris_field", "Debris field") + (string.IsNullOrEmpty(e.Name) ? string.Empty : ": " + e.Name); // #2353
                case "Debris": return Loc("ui.scan.subject.debris", "Wreckage");
                case "SalvageCapsule": return Loc("ui.scan.subject.salvage_capsule", "Salvage capsule");
                default: return string.IsNullOrEmpty(e.Name) ? e.Kind : e.Name;
            }
        }

        /// <summary>"Wormhole → Kessira" once the player knows where it leads, "Wormhole → ???" before.</summary>
        private string WormholeLabel(string id)
        {
            string dest = "???";
            var map = Game.StarMap;
            if (map?.Wormholes != null)
            {
                foreach (var w in map.Wormholes)
                {
                    if (w.Id == id && !string.IsNullOrEmpty(w.LinkedSystemId))
                    {
                        foreach (var s in map.Systems)
                        {
                            if (s.Id == w.LinkedSystemId)
                            {
                                dest = s.Name;
                                break;
                            }
                        }
                    }
                }
            }

            return Loc("ui.scan.subject.wormhole", "Wormhole") + " → " + dest;
        }

        private bool IsScanned(ScanTarget target)
        {
            if (target.Key == null)
            {
                return false;
            }

            if (_scannedThisFlight.Contains(target.Key))
            {
                return true;
            }

            var ids = Game.Space?.ScannedIds;
            return !target.IsBody && ids != null && System.Array.IndexOf(ids, target.Id) >= 0;
        }

        /// <summary>The charging look, a few times a second: the fan from the nose, the sweeping plane, motes flowing back.</summary>
        private void ChargeFx(ScanTarget target, FxLook look, bool ping, float dt)
        {
            _scanFxTimer -= dt;
            _scanPlaneTimer -= dt;
            Vector3 nose = NoseWorld();
            if (ping)
            {
                if (_scanFxTimer <= 0f)
                {
                    _scanFxTimer = 0.35f;
                    FxKit.Ring(nose, Vector3.zero, look.Color, 1f, 6f + 10f * _scanProgress, 0.4f, thickness: 0.06f, lines: 2f, intensity: 1.6f);
                }

                return;
            }

            Vector3 center = _root.transform.TransformPoint(target.Local);
            float r = target.Radius * _root.transform.lossyScale.x;
            if (_scanFxTimer <= 0f)
            {
                _scanFxTimer = 0.14f;
                var cam = Camera != null ? Camera.transform : _ship.transform;
                Vector3 right = cam.right * r;
                Vector3 up = cam.up * r;
                var fan = FxKit.BeamMaterial("shipscanfan", look.Color2, coreWidth: 0.6f, noiseScale: 2.5f, noiseSpeed: 20f, intensity: 1.6f);
                for (int i = 0; i < 4; i++)
                {
                    Vector3 corner = center + (i % 2 == 0 ? -right : right) + (i < 2 ? up : -up);
                    FxKit.Beam(nose, corner, look.Color, 0.05f, 0.18f, fan);
                }

                int motes = FxKit.Scaled(4);
                for (int i = 0; i < motes; i++)
                {
                    Vector3 from = center + Random.insideUnitSphere * r;
                    float life = Random.Range(0.4f, 0.7f);
                    FxKit.Emit(FxKit.Kind.Motes, from, (nose - from) / life, Random.Range(0.08f, 0.16f), life, Color.Lerp(look.Color, look.Color2, Random.value));
                }
            }

            if (_scanPlaneTimer <= 0f)
            {
                _scanPlaneTimer = 0.5f;
                FxGadgets.HoloPlane(look.Color2, center, Vector3.one * r * 2f, 0.45f);
            }
        }

        /// <summary>The ring is full: send the scan, and show it landed — a pulse, the scan wave over the target's
        /// surface, a flash and a light; the anomaly reacts, a planet opens its overview card.</summary>
        private void CompleteScan(ScanTarget target, FxLook look, bool ping)
        {
            Vector3 nose = NoseWorld();
            if (ping)
            {
                Game.SpaceSystemPingUntil = Time.time + 60f; // the radar pins every scannable object of the system
                SpaceFx.ScanPulse(nose, look.Color, 300f);
                FxKit.Flash(nose, look.Color2, 3f * FxKit.FlashScale, 0.3f);
                ClientAudio.Instance?.Cue("planet_scan_overview", 0.8f);
                return;
            }

            Vector3 center = _root.transform.TransformPoint(target.Local);
            float r = target.Radius * _root.transform.lossyScale.x;
            _scannedThisFlight.Add(target.Key);
            if (target.IsBody)
            {
                Game.OpenOverviewOnNextPlanetScan = true;
                Game.Network?.SendPlanetScan(target.Id);
                ClientAudio.Instance?.Cue("planet_scan_overview", 0.8f);
            }
            else
            {
                Game.Network?.SendScanEntity(target.Id);
                ClientAudio.Instance?.Cue(target.Hostile ? "ship_scan_hostile" : "ship_scan_complete", 0.8f);
                if (_entities.TryGetValue(target.Id, out var go) && go != null)
                {
                    go.GetComponent<AnomalyView>()?.React(); // the soap bubble ripples and calms
                }
            }

            SpaceFx.ScanPulse(center, look.Color, Mathf.Max(4f, r * 1.8f));
            FxScanWave.Start(center, Mathf.Max(8f, r * 3f), Mathf.Max(20f, r * 6f), look.Color, width: 2.5f, strength: 0.8f, lineDensity: 1.4f);
            FxKit.Flash(center, look.Color2, Mathf.Max(2f, r) * FxKit.FlashScale, 0.18f);
            FxLights.Flash(center, look.Color, 1.6f, Mathf.Max(10f, r * 3f), 0.4f);
            int burst = FxKit.Scaled(12);
            for (int i = 0; i < burst; i++)
            {
                Vector3 from = center + Random.insideUnitSphere * r;
                float life = Random.Range(0.5f, 0.9f);
                FxKit.Emit(FxKit.Kind.Motes, from, (nose - from) / life, Random.Range(0.1f, 0.2f), life, Color.Lerp(look.Color, look.Color2, Random.value));
            }

            // The other pilots out here see the fan too (cosmetic, instance-frame positions — like a shot).
            Game.Network?.SendFx(FxActionKinds.Scan, CurrentScanner().ModuleKey, _ship.transform.localPosition, target.Local, true);
        }

        private Vector3 NoseWorld()
        {
            var b = ShipLocalBounds();
            return _ship.transform.TransformPoint(new Vector3(b.center.x, b.center.y, b.max.z));
        }

        // ---------------- Lock-on HUD ----------------

        private void EnsureScanUi()
        {
            if (_scanFrame != null || _ui == null)
            {
                return;
            }

            // #2283: the same bracket frame as the target lock, plus the scanner's own charge ring and track.
            _scanFrame = new SpaceTargetFrame(TargetLayer(), "ScanLock", chargeRing: true);
        }

        /// <summary>The four corner brackets around the locked target (snapping in from wider), its name, distance and
        /// "✓ scanned" below, and the filling ring while the trigger is held. Red brackets for a hostile.</summary>
        private void DrawScanLock(ScanTarget target, FxLook look, bool ping)
        {
            EnsureScanUi();
            if (_scanFrame == null)
            {
                return;
            }

            bool show = (target.Key != null || ping) && !Game.MenuOpen && Camera != null;
            if (!show)
            {
                _scanFrame.Hide();
                return;
            }

            Color col = target.Hostile ? new Color(1f, 0.38f, 0.35f) : look.Color;
            var canvasRect = (RectTransform)_ui.transform;
            float scale = Mathf.Max(0.01f, _ui.scaleFactor);
            Vector2 anchored = Vector2.zero;
            float half = 60f;
            if (target.Key != null)
            {
                Vector3 world = _root.transform.TransformPoint(target.Local);
                Vector3 screen = Camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                {
                    _scanFrame.Hide(); // behind the camera: the target lock's edge arrow points there (#2277)
                    return;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out anchored);
                float r = target.Radius * _root.transform.lossyScale.x;
                Vector3 edge = Camera.WorldToScreenPoint(world + Camera.transform.right * r);
                half = Mathf.Clamp(Vector2.Distance(screen, edge) / scale * 1.25f, 26f, 240f);
            }

            float snap = Mathf.Clamp01(_scanLockAge / 0.16f);
            float squeeze = _scanCharging ? 1f - 0.12f * _scanProgress : 1f;
            float h = half * Mathf.Lerp(1.8f, 1f, 1f - (1f - snap) * (1f - snap)) * squeeze;
            _scanFrame.Place(anchored, h, TargetFrameShape.Corners, col, 3f, brackets: target.Key != null);
            float ringSize = target.Key != null ? Mathf.Min(h * 0.9f, 70f) : 90f;
            _scanFrame.SetCharge(_scanCharging && _scanProgress > 0f, _scanProgress, ringSize, col);

            // #2247: the lock says how to scan — "Hold LMB: scan" — until the ring runs, and "keep holding" for a moment
            // after an early release. Nothing on screen used to say the trigger must be HELD.
            string howTo = _scanLetGoTimer > 0f
                ? Loc("ui.space.scan_keep_holding", "Keep holding until the ring is full")
                : _scanCharging ? string.Empty
                : string.Format(Loc("ui.space.scan_hold_hint", "Hold {0}: scan"), FireGlyph());

            string text;
            if (target.Key == null)
            {
                text = Loc("ui.space.scan_ping_hint", "Hold: scan the whole system");
                if (_scanLetGoTimer > 0f)
                {
                    text += "\n" + howTo;
                }
            }
            else
            {
                // #2277: the instruments' kilometres (×10 per flight unit, like the radar) — the label used to print the raw
                // flight units as "m", so one wreck read "123 m" here and "1 230 km" on the radar.
                float dist = Vector3.Distance(_ship.transform.localPosition, target.Local);
                text = target.Name + "\n" + SpaceDistance.Label(Mathf.Round(dist), Loc("ui.space.km_fmt", SpaceDistance.FallbackFormat))
                       + (IsScanned(target) ? "  ✓ " + Loc("ui.space.scanned", "scanned") : string.Empty);
                if (howTo.Length > 0)
                {
                    text += "\n" + howTo;
                }
            }

            _scanFrame.SetLabel(text, col);
            _scanFrame.SetSubLabel(string.Empty, col);
        }

        // ---------------- VEGA tips ----------------

        /// <summary>Two one-shot lessons: the first flight ("your ship has a scanner") and an anomaly nearby that was
        /// never scanned ("select your scanner and hold it on the anomaly").</summary>
        private void MaybeSayScannerHints(float dt)
        {
            var settings = Game.Settings;
            if (settings == null)
            {
                return;
            }

            if (!settings.ShipScannerHintShown)
            {
                _scannerHintTimer -= dt;
                if (_scannerHintTimer <= 0f)
                {
                    settings.ShipScannerHintShown = true;
                    settings.Save();
                    VegaPanel.Instance?.SayLocal("vega.hint.ship_scanner");
                }
            }
        }

        /// <summary>Called every cruise frame whatever system is selected: an unscanned anomaly within 80 units gets the
        /// "use your scanner" tip once per anomaly and flight.</summary>
        private void MaybeSayAnomalyHint()
        {
            var space = Game?.Space;
            if (space == null || _ship == null)
            {
                return;
            }

            Vector3 shipPos = _ship.transform.localPosition;
            foreach (var e in space.Entities)
            {
                if (e.Kind != "Anomaly" || _anomalyHinted.Contains(e.Id) || _scannedThisFlight.Contains("e:" + e.Id)
                    || (space.ScannedIds != null && System.Array.IndexOf(space.ScannedIds, e.Id) >= 0))
                {
                    continue;
                }

                if ((new Vector3(e.X, e.Y, e.Z) - shipPos).sqrMagnitude <= 80f * 80f)
                {
                    _anomalyHinted.Add(e.Id);
                    VegaPanel.Instance?.SayLocal("vega.hint.anomaly_scan");
                }
            }

            // #2242: the first wormhole a player ever sees (within 500 units) — once, remembered in the settings.
            var settings = Game.Settings;
            if (settings != null && !settings.WormholeHintShown)
            {
                foreach (var e in space.Entities)
                {
                    if (e.Kind == "Wormhole" && (new Vector3(e.X, e.Y, e.Z) - shipPos).sqrMagnitude <= 500f * 500f)
                    {
                        settings.WormholeHintShown = true;
                        settings.Save();
                        VegaPanel.Instance?.SayLocal("vega.hint.wormhole");
                        break;
                    }
                }
            }
        }
    }
}
