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
    /// Flying the ship: the launch and landing sequences, a transit's arrival (#1614), the re-entry and launch-cloud
    /// effects, the VEGA autopilot, cruise control, the flight camera and the resolution of the space waypoint (#597).
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Snapshots the descent: the ship flies off toward the body it's landing on (the one ahead of
        /// the nose) and the camera tracks it from a fixed vantage, so it shrinks into the distance — centred —
        /// instead of sliding straight down out of frame.</summary>
        private void BeginLandingFlyAway()
        {
            _landShipStart = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            Vector3 fwd = _ship != null ? _ship.transform.forward : Vector3.forward;

            // The body we're heading for. First choice: the pad-chooser's body (_choosePadBody; empty id = the
            // home body we launched from). If that can't be resolved, pick whichever landable lies most directly
            // ahead of the nose — "the planet/asteroid in front of the ship" — so we never default to straight down.
            Vector3 dest = new Vector3(0f, -150f, -20f);
            string destId = _landDestBody ?? string.Empty;
            bool found = false;
            foreach (var b in _landables)
            {
                if (b.Id == destId)
                {
                    dest = b.Pos;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                float bestDot = -2f;
                foreach (var b in _landables)
                {
                    Vector3 to = b.Pos - _landShipStart;
                    if (to.sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    float dot = Vector3.Dot(to.normalized, fwd);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        dest = b.Pos;
                        found = true;
                    }
                }
            }

            // Aim a touch short of the centre so the hull dives at the surface rather than through the core.
            _landTargetPos = Vector3.Lerp(_landShipStart, dest, 0.92f);

            if (Camera != null)
            {
                _landCamPos = Camera.transform.localPosition;
                _landCamRot = Camera.transform.localRotation;
            }
        }

        private void UpdateSequence(bool rising)
        {
            _seq += Time.deltaTime;

            if (!rising)
            {
                // Landing: fly the ship away toward the body on an accelerating (ease-in) curve so it streaks off
                // and dwindles. The camera holds position but turns to follow it (PlaceCamera), keeping it centred.
                float lt = Mathf.Clamp01(_seq / LandDuration);
                float lease = lt * lt;
                if (_ship != null)
                {
                    Vector3 pos = Vector3.Lerp(_landShipStart, _landTargetPos, lease);
                    _ship.transform.localPosition = pos;
                    Vector3 dir = _landTargetPos - pos;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        // Point the nose at the body. A near-vertical dive would make Vector3.up (anti-)parallel to
                        // the heading and flip LookRotation, so fall back to Vector3.forward as the up hint then.
                        Vector3 nd = dir.normalized;
                        Vector3 up = Mathf.Abs(nd.y) > 0.95f ? Vector3.forward : Vector3.up;
                        _ship.transform.localRotation = Quaternion.LookRotation(nd, up);
                    }
                }

                UpdateReentry(lt);
                if (_seq >= LandDuration)
                {
                    Exit();
                }

                return;
            }

            LaunchClouds(Mathf.Clamp01(_seq / SeqDuration));

            // Launch: rise off the pad with an ease-out, level and centred.
            float t = Mathf.Clamp01(_seq / SeqDuration);
            float ease = 1f - (1f - t) * (1f - t);
            float y = Mathf.Lerp(-40f, 0f, ease);
            if (_ship != null)
            {
                _ship.transform.localPosition = new Vector3(_launchOffset.x, y, _launchOffset.z); // #2440: beside the others
                _ship.transform.localRotation = Quaternion.identity;
            }

            if (_seq >= SeqDuration)
            {
                if (Game.SpaceAutomaticTransit)
                {
                    // #1614: hold at the top of the climb, hands-off, until the server answers with the next stage —
                    // the landing (the descent starts when the flight closes) or the warp into the destination's system.
                    HoldForTransit();
                    return;
                }

                _phase = Phase.Cruise;
            }
        }

        /// <summary>#1614: the arrival of a transit's warp in the destination's system. The ship holds, hands-off, until
        /// the warp has played, then reports it; the server lands the ship and the usual descent takes over.</summary>
        private void UpdateTransitArrival()
        {
            _seq += Time.deltaTime;
            if (!Game.SpaceAutomaticTransit)
            {
                _phase = Phase.Cruise; // the transit ended in flight (a refused landing) — the pilot takes over
                return;
            }

            if (_seq >= TransitWarpHold)
            {
                HoldForTransit();
            }
        }

        /// <summary>#1614: reports the played stage once, then waits for the server's next stage. Should no answer ever
        /// come (a lost packet), the pilot gets the controls back after <see cref="TransitFallback"/> instead of hanging.</summary>
        private void HoldForTransit()
        {
            if (!_transitLaunchDone)
            {
                _transitLaunchDone = true;
                _transitWait = 0f;
                Game.Network?.SendTransitLaunchDone();
            }

            _transitWait += Time.deltaTime;
            if (_transitWait > TransitFallback)
            {
                _phase = Phase.Cruise;
            }
        }

        /// <summary>#1614: whether the transit's next stage is the warp rather than the landing — its destination is not
        /// a body of this flight's system, or it has none ("Hyperjump to this system" ends in flight).</summary>
        private bool TransitWarpsNext()
        {
            if (!Game.SpaceAutomaticTransit)
            {
                return false;
            }

            if (_landDestBody == null)
            {
                return true;
            }

            foreach (var b in _landables)
            {
                if (b.Id == _landDestBody)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Atmosphere entry on the landing descent (#2157): a plasma sheath builds around the ship, hottest on its
        /// leading face, streaks of glow stream back off it, a heat light and a gentle rattle.</summary>
        private void UpdateReentry(float progress)
        {
            if (_ship == null)
            {
                return;
            }

            if (_reentry == null)
            {
                var mat = FxKit.Cached("BlocksBeyondTheStars/FxShell", "reentry", m =>
                {
                    m.SetFloat("_Plasma", 1f);
                    m.SetFloat("_RimPower", 1.6f);
                    m.SetFloat("_Hex", 0f);
                    m.SetFloat("_Fill", 0.05f);
                });
                if (mat == null)
                {
                    return;
                }

                var go = new GameObject("Reentry");
                go.transform.SetParent(_ship.transform, false);
                var bounds = ShipLocalBounds();
                go.transform.localPosition = bounds.center + Vector3.forward * bounds.extents.z * 0.2f;
                go.transform.localScale = Vector3.Max(bounds.size * 1.4f, Vector3.one * 2f);
                go.AddComponent<MeshFilter>().sharedMesh = FxKit.SphereMesh;
                _reentry = go.AddComponent<MeshRenderer>();
                _reentry.sharedMaterial = mat;
                _reentry.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            float air = Game.Environment != null ? Mathf.Clamp01(Game.Environment.AtmosphereDensity * 1.5f + 0.2f) : 0.6f;
            float heat = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.15f) / 0.5f)) * air;
            var block = FxKit.Block;
            block.Clear();
            block.SetColor("_Color", FxKit.Lin(Color.Lerp(new Color(1f, 0.55f, 0.25f), new Color(1f, 0.45f, 0.7f), progress)) * new Color(1f, 1f, 1f, heat));
            block.SetFloat("_Intensity", 1.8f);
            _reentry.SetPropertyBlock(block);
            if (heat > 0.05f)
            {
                var b = ShipLocalBounds();
                var front = _ship.transform.TransformPoint(b.center + Vector3.forward * b.extents.z);
                FxKit.Emit(FxKit.Kind.Motes, front + Random.insideUnitSphere * 0.8f, -_ship.transform.forward * 18f + Random.insideUnitSphere * 2f,
                    Random.Range(0.15f, 0.3f), 0.35f, new Color(1f, 0.7f, 0.4f));
                FxCamera.AddTrauma(0.02f * heat);
                if (_engineLight != 0)
                {
                    FxLights.Set(_engineLight, new Color(1f, 0.55f, 0.3f), 2.5f * heat);
                }
            }
        }

        /// <summary>The launch rise (#2157): cloud wisps rush down past the camera while the ship climbs out of the air.</summary>
        private void LaunchClouds(float progress)
        {
            if (Camera == null || progress > 0.75f || Random.value > 0.6f * FxKit.Density)
            {
                return;
            }

            float air = Game.Environment != null ? Game.Environment.AtmosphereDensity : 0.4f;
            if (air < 0.05f)
            {
                return; // an airless body has no clouds to break through
            }

            var cam = Camera.transform;
            var at = cam.position + cam.forward * Random.Range(6f, 20f) + cam.right * Random.Range(-12f, 12f) + Vector3.up * Random.Range(4f, 10f);
            FxKit.Emit(FxKit.Kind.Smoke, at, Vector3.down * Random.Range(22f, 34f), Random.Range(2.5f, 5f), 0.9f, new Color(0.92f, 0.94f, 0.98f));
        }

        // VEGA autopilot (Mk2+ AI core): hands-off cruise toward the nearest station / landable body.
        private bool _autopilot;

        private void SetAutopilot(bool on)
        {
            if (_autopilot == on)
            {
                return;
            }

            _autopilot = on;
            Game.ShowMessage(Loc(on ? "ui.vega.autopilot.on" : "ui.vega.autopilot.off",
                on ? "Autopilot engaged" : "Autopilot disengaged"));
        }

        /// <summary>The autopilot's destination: the nearest space station, else the nearest landable body.
        /// Returns the point to fly at plus the squared arrival distance (just outside dock/land range).</summary>
        private bool TryAutopilotTarget(out Vector3 target, out float arriveSq)
        {
            // A waypoint the player set on the system chart (#597) outranks the default pick — that is
            // the whole point of setting one. Falls through to nearest-station/nearest-body without one.
            if (TryResolveSpaceWaypoint(out target, out arriveSq))
            {
                return true;
            }

            Vector3 pos = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            target = Vector3.zero;
            arriveSq = 0f;
            float bestSq = float.MaxValue;

            var space = Game.Space;
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    var sp = new Vector3(e.X, e.Y, e.Z);
                    float sq = (sp - pos).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        // #1917: a station with a known hangar is flown to the point in front of its mouth.
                        if (StationApproachPoint(e.Id, DockApproachStandOff) is { } approach)
                        {
                            target = approach;
                            arriveSq = DockApproachArrive * DockApproachArrive;
                        }
                        else
                        {
                            target = sp;
                            arriveSq = BoardRange * BoardRange * 0.64f; // well inside dock range before handing over
                        }
                    }
                }
            }

            if (bestSq == float.MaxValue)
            {
                foreach (var body in _landables)
                {
                    float sq = (body.Pos - pos).sqrMagnitude;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        target = body.Pos;
                        float approach = body.Radius + KeepOutMargin + LandBand * 0.5f;
                        arriveSq = approach * approach;
                    }
                }
            }

            return bestSq != float.MaxValue;
        }

        private void UpdateCruise()
        {
            if (_ship == null)
            {
                return;
            }

            // Report our position so the server runs authoritative collisions against asteroids/entities and
            // the other players in this instance can see our ship. Sent BEFORE the menu/prompt early-outs
            // below: behind the star map, the pad chooser or the destruction prompt this is the client's only
            // payload, and the server drops sessions silent for 90 s (#964/#1008). After the ship was
            // destroyed the server has already removed us from the instance and ignores the intent — but
            // receiving it still stamps the heartbeat.
            _moveSendTimer -= Time.deltaTime;
            if (_moveSendTimer <= 0f)
            {
                _moveSendTimer = 0.08f; // ~12 Hz
                Game.Network?.SendShipMove(_ship.transform.localPosition, _yaw);
            }

            // #2724: whom K docks with and what the visit prompt says — kept current behind a menu too, so the docked line
            // and the Actions list never read a stale frame. E's share of it is decided below, beside the station and body.
            UpdateVisit(_ship.transform.localPosition);
            _interactGoesOver = false;
            _interactAsksPartner = false;

            // #2277: the target lock follows the instance every cruise frame (destroyed, gone, out of range, a new
            // attacker); its keys are read only while the helm takes input — not behind a menu, the chat or the pad map.
            UpdateTargetLock(Time.deltaTime, FlightInputAllowed && !_confirmLand);

            // Hold position while a menu is open (e.g. the Tab star map, used to hyperspace-jump to another
            // system), while the ship-destruction "Weiter" prompt is up, or while the player is typing (the
            // chat box, any focused field — #1858: "E" typed into the chat used to dock the ship), so flight
            // input doesn't fight the UI / swing the camera behind the modal. #2724: and while a visit to the docked
            // friend's ship is on its way behind the veil.
            if (!FlightInputAllowed || _visitWait >= 0f)
            {
                return;
            }

            // Landing opens a pad chooser (item 38): pick a free landing pad with a number key, Esc cancels.
            // No accidental drop to the surface — you choose where you touch down.
            if (_confirmLand)
            {
                // Esc — or pad B (#1043): the land map is stick-navigable, so the pad needs a back-out too.
                if (InputMap.Down(InputAction.UiCancel))
                {
                    Game.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                    CancelLandChooser();
                    return;
                }

                var pads = Game.LandingPadsBody == _choosePadBody ? Game.LandingPads : null;
                if (pads == null && Time.time - _padRequestAt > 5f)
                {
                    // No reply — one silent re-request, then give control back instead of freezing
                    // the flight on "Reading landing pads…" forever (#956).
                    if (!_padRequestRetried)
                    {
                        _padRequestRetried = true;
                        _padRequestAt = Time.time;
                        Game.Network?.SendRequestLandingPads(_choosePadBody);
                    }
                    else
                    {
                        Game.ShowMessage(Loc("ui.space.pad_timeout", "No landing data from the server - please try again."));
                        CancelLandChooser();
                        return;
                    }
                }

                if (pads != null)
                {
                    // Show the planet map with the landing pads on it; the player clicks a free pad to touch down
                    // there. Number keys 1–9 mirror clicking, for keyboard players.
                    ShowLandMap(pads);
                    RefreshLandWeather(force: false); // #2176: the weather layer follows the server's snapshots
                    for (int i = 0; i < pads.Length && i < 9; i++)
                    {
                        // #999: key N selects the pad LABELLED N (Index + 1), not array slot N — today the
                        // server sends pads in index order so both agree, but any reordered/filtered list
                        // would silently land the ship on the wrong pad.
                        var pad = System.Array.Find(pads, p => p.Index == i);
                        if (pad != null && (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) && !pad.Occupied)
                        {
                            LandOnPad(pad.Index);
                            break;
                        }
                    }
                }

                return; // hold position while choosing
            }

            // L opens the pad chooser for the body you launched from (landing on a nearby body is E).
            if (InputMap.Down(InputAction.FlightPadChooser))
            {
                OpenPadChooser(Game.StarMap != null ? Game.StarMap.ActiveLocationId : string.Empty);
                return;
            }

            // M: the system chart (#597) — a clickable top-down map of this system to set a nav waypoint.
            // Opening it takes menu ownership, so the ship holds position until it closes (like Tab).
            if (Map != null && InputMap.Down(InputAction.FlightMap))
            {
                Map.Open();
                return;
            }

            // F: leave the helm and step inside the ship — walk around its interior while it floats here.
            // (From inside, the airlock starts an EVA — there is no direct cockpit→EVA any more.)
            if (InputMap.Down(InputAction.FlightEnterInterior))
            {
                _enteringInterior = true;
                Game.BeginWorldTransition(); // veil immediately — stepping inside has no descent to mask (B34)
                Game.Network?.SendEnterShip();
                return;
            }

            // P: VEGA autopilot (needs an AI Core Mk2 aboard) — flies toward the nearest station, else the
            // nearest landable body, and hands back control on arrival or any manual input.
            if (InputMap.Down(InputAction.FlightAutopilot))
            {
                if (_autopilot)
                {
                    SetAutopilot(false);
                }
                else if (Game.AiCoreTier >= 2)
                {
                    SetAutopilot(true);
                }
                else
                {
                    Game.ShowMessage(Loc("ui.vega.autopilot.none", "Autopilot requires an AI Core Mk2"));
                }
            }

            float fwd, strafe;
            if (_autopilot && TryAutopilotTarget(out var apTarget, out float apArriveSq))
            {
                // Manual input takes the helm back instantly.
                if (Mathf.Abs(InputMap.MoveY()) > 0.25f || Mathf.Abs(InputMap.MoveX()) > 0.25f)
                {
                    SetAutopilot(false);
                }
                else if ((apTarget - _ship.transform.localPosition).sqrMagnitude <= apArriveSq)
                {
                    SetAutopilot(false); // arrived — the E dock/land prompt takes over
                }
                else
                {
                    // Steer the nose onto the target and cruise at full throttle.
                    Vector3 to = (apTarget - _ship.transform.localPosition).normalized;
                    float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float wantPitch = -Mathf.Asin(Mathf.Clamp(to.y, -1f, 1f)) * Mathf.Rad2Deg;
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wantYaw, 60f * Time.deltaTime);
                    _pitch = Mathf.MoveTowards(_pitch, Mathf.Clamp(wantPitch, -80f, 80f), 45f * Time.deltaTime);
                }
            }

            if (_autopilot)
            {
                fwd = 1f;
                strafe = 0f;
            }
            else
            {
                _yaw += InputMap.LookX() * LookSpeed * _shipTurnMul;
                _pitch = Mathf.Clamp(_pitch - InputMap.LookY() * LookSpeed * _shipTurnMul, -80f, 80f);
                fwd = CaptureForward();
                strafe = InputMap.MoveX();
            }

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _ship.transform.localRotation = rot;
            var move = rot * (Vector3.forward * fwd + Vector3.right * strafe) * (MoveSpeed * _shipSpeedMul * Time.deltaTime);
            var pos = _ship.transform.localPosition + move;
            if (pos.magnitude > FlightClamp)
            {
                pos = pos.normalized * FlightClamp;
            }

            // Keep-out: never let the ship penetrate a body — push it back out to the sphere surface, so it
            // slides around the planet instead of flying in. This is the "auto-adjust the flight direction"
            // that stops you from getting too close / accidentally dropping onto the planet.
            foreach (var ob in _keepOut)
            {
                Vector3 d = pos - ob.Pos;
                float dist = d.magnitude;
                if (dist < ob.Radius && dist > 0.0001f)
                {
                    pos = ob.Pos + d / dist * ob.Radius;
                }
            }

            // Stations are solid too. A station whose voxel hull is here (#1917) blocks block by block — the ship
            // slides along the hull and can fly between its modules right up to the hangar mouth; one still drawn
            // as the placeholder model keeps its sphere shell (the board range is far larger than either).
            pos = ResolveStationHullMove(_ship.transform.localPosition, pos);
            var keepSpace = Game.Space;
            if (keepSpace != null)
            {
                foreach (var e in keepSpace.Entities)
                {
                    if (e.Kind != "SpaceStation" || _structs.ContainsKey(e.Id))
                    {
                        continue;
                    }

                    Vector3 sp = new Vector3(e.X, e.Y, e.Z);
                    Vector3 d = pos - sp;
                    float dist = d.magnitude;
                    float keep = StationKeepOut * Mathf.Max(1f, e.Scale); // bigger tiers have a bigger hull shell
                    if (dist < keep && dist > 0.0001f)
                    {
                        pos = sp + d / dist * keep;
                    }
                }
            }

            _ship.transform.localPosition = pos;

            // System-scale flight: the nearest other body within approach range is the land target; press E
            // to land on it. With none in range, L returns you to the body you launched from.
            _landTargetId = null;
            _landTargetName = null;
            _landTargetSq = float.MaxValue;
            foreach (var body in _landables)
            {
                float approach = body.Radius + KeepOutMargin + LandBand; // just outside its keep-out shell
                float sq = (body.Pos - pos).sqrMagnitude;
                if (sq <= approach * approach && sq < _landTargetSq)
                {
                    _landTargetId = body.Id;
                    _landTargetName = body.Name;
                    _landTargetSq = sq;
                }
            }

            // Boarding: find the nearest station in range — measured to its hull (#1917); E boards it (the server
            // validates the range the same way).
            _nearStationId = null;
            _nearStationSq = float.MaxValue;
            var space = Game.Space;
            if (space != null)
            {
                float best = BoardRange;
                foreach (var e in space.Entities)
                {
                    if (e.Kind != "SpaceStation")
                    {
                        continue;
                    }

                    float dist = StationDistance(e, _ship.transform.localPosition);
                    if (dist < best)
                    {
                        best = dist;
                        _nearStationId = e.Id;
                        _nearStationName = e.Name;
                        _boardTargetPos = new Vector3(e.X, e.Y, e.Z);
                        _nearStationSq = dist * dist;
                    }
                }
            }

            // #2242: a wormhole within reach takes E before anything else (rifts open far from planets and stations).
            UpdateNearWormhole(pos);
            UpdateNearAnomaly(pos); // #2588: flying up to an anomaly says what it is (it is not a wormhole)

            // #2724: docked with a friend whose ship floats parked within reach, E goes over to it — unless a station or a
            // body is closer (the same "whichever you're closest to" rule). A friend at the helm can only be asked: E then
            // tells them (the server's toast) while nothing else wants the key.
            if (_nearWormholeId == null)
            {
                float visitSq = _visit.Distance * _visit.Distance;
                bool visitCloser = (_nearStationId == null || visitSq <= _nearStationSq) && (_landTargetId == null || visitSq <= _landTargetSq);
                _interactGoesOver = _visit.Prompt == BlocksBeyondTheStars.Client.Core.ShipVisitPrompt.GoOver && visitCloser;
                _interactAsksPartner = _visit.Prompt == BlocksBeyondTheStars.Client.Core.ShipVisitPrompt.PartnerFlying
                    && _visit.Distance <= BlocksBeyondTheStars.Client.Core.ShipVisitRules.VisitRange
                    && _nearStationId == null && _landTargetId == null;
            }

            // E is the context action while flying: fly through a wormhole, go over to the docked friend's ship, dock with a
            // station you're next to, or — like docking — land on a planet/moon you've flown up close to (the server flies
            // the descent). CruiseInteractChoice is the one rule; the board prompt asks it too (#2934). L stays as the
            // "return to the body you launched from" shortcut.
            if (InputMap.Down(InputAction.Interact))
            {
                switch (CruiseInteractChoice())
                {
                    case CruiseInteractTarget.Wormhole:
                        BeginWormholeTransit();
                        break;
                    case CruiseInteractTarget.GoOver:
                        BeginVisit(); // #2724: over to the docked friend's ship
                        break;
                    case CruiseInteractTarget.AskPartner:
                        // #2724: the friend is flying — ask anyway; the server says "step inside your ship" to them and why to us.
                        // Behind the veil like any visit: if they stepped inside since the last flight snapshot the server takes
                        // us over at once (no landing descent); its "is flying" refusal lifts the veil again straight away.
                        BeginVisit();
                        break;
                    case CruiseInteractTarget.Station:
                        // Whichever you're closest to wins: dock the station or land on the body.
                        _phase = Phase.Boarding; // dock-approach animation (into the hangar, #1917); board intent sent on completion
                        _seq = 0f;
                        _boardSent = false;
                        _boardWait = 0f;
                        _boardTargetId = _nearStationId;
                        _boardStartPos = _ship.transform.localPosition;
                        PlanDockApproach();
                        break;
                    case CruiseInteractTarget.Body:
                        // One press opens the planet map; you pick the pad to land on there (item 38 / B?).
                        OpenPadChooser(_landTargetId);
                        break;
                    default: // nothing in reach, or the friend is flying out of reach (the prompt only says so)
                        SayAnomalyHintOnInteract(); // #2588: E at an anomaly said nothing — now it says how to read it
                        break;
                }
            }

            // Ship-systems quick-bar: 1–9 pick the active system, LMB uses it. The laser auto-locks the best
            // target ahead (mines asteroids + fights hostiles); the tractor beam sweeps in nearby salvage.
            RebuildSystems();
            MaybeSayAnomalyHint(); // #2238: an unscanned anomaly nearby — VEGA points at the scanner
            MaybeSayScannerHints(Time.deltaTime); // #2247: "every ship has a scanner" on an early flight, whatever is selected
            for (int n = 0; n < _systems.Count && n < 9; n++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + n))
                {
                    _selectedSystem = n;
                }
            }

            // Device-neutral cycle (#1044): the hotbar scroll — mouse wheel, pad d-pad ◄►, touch ◄► — steps
            // through the systems, wrapping. Neither pad nor touch has number keys, and the wheel is
            // otherwise idle while piloting; the EVA hotbar already accepts the same scroll.
            if (_systems.Count > 1)
            {
                float step = InputMap.HotbarScroll();
                if (step > 0f)
                {
                    _selectedSystem = (_selectedSystem + _systems.Count - 1) % _systems.Count;
                }
                else if (step < 0f)
                {
                    _selectedSystem = (_selectedSystem + 1) % _systems.Count;
                }
            }

            _fireCd -= Time.deltaTime;
            var sys = _systems[_selectedSystem];
            if (sys.Kind != "scanner")
            {
                StopScanner(); // #2237: switching away cancels a scan in progress and hides the lock brackets
            }

            if (sys.Kind == "laser")
            {
                var target = BestFireTarget(sys.WeaponKey);
                _fireTargetId = target?.Id;
                if (target != null && _fireCd <= 0f && InputMap.PrimaryHeld())
                {
                    _fireCd = WeaponCooldownFor(sys.WeaponKey);
                    FireAt(target, sys.WeaponKey);
                }
            }
            else if (sys.Kind == "scanner")
            {
                UpdateScanner(Time.deltaTime); // #2237: aim, hold until the ring is full, read the target
            }
            else // tractor
            {
                // Auto-lock the nearest salvage drop (like the laser) so the reticle confirms what you'll pull —
                // a blind radius sweep was impossible to aim in 3D, where depth is hard to judge.
                var drop = BestTractorTarget();
                _fireTargetId = drop?.Id;
                if (_fireCd <= 0f && InputMap.PrimaryDown())
                {
                    _fireCd = 0.6f;
                    ActivateTractor(drop);
                }
            }
        }

        /// <summary>Places the flight camera, then adds the shared camera feel (#2152): trauma shake, kick and FOV punch.</summary>
        private void PlaceCamera()
        {
            PlaceCameraBase();
            FxCamera.Sample(Time.deltaTime, out var feel, out float fovPunch);
            if (Camera != null && _ship != null)
            {
                Camera.transform.localRotation *= Quaternion.Euler(feel);
                if (_flightFov > 1f)
                {
                    Camera.fieldOfView = _flightFov + fovPunch;
                }
            }
        }

        private void PlaceCameraBase()
        {
            if (_ship == null)
            {
                return;
            }

            // EVA: first-person from the suit, looking where you steer.
            if (_eva)
            {
                Camera.transform.localPosition = _evaPos;
                Camera.transform.localRotation = Quaternion.Euler(_evaPitch, _evaYaw, 0f);
                if (_shake > 0.001f)
                {
                    Camera.transform.localPosition += Random.insideUnitSphere * (_shake * 0.5f);
                }

                return;
            }

            // Landing: keep the camera at the descent's start vantage but turn it to follow the receding ship, so
            // the hull stays centred while it flies off toward the body and shrinks (instead of the camera chasing
            // it down at a fixed offset, which read as "sinking"). Cockpit view is forced to this too.
            if (_phase == Phase.Landing)
            {
                Camera.transform.localPosition = _landCamPos;
                Vector3 toShip = _ship.transform.localPosition - _landCamPos;
                if (toShip.sqrMagnitude > 0.0001f)
                {
                    Vector3 nts = toShip.normalized;
                    Vector3 camUp = Mathf.Abs(nts.y) > 0.95f ? Vector3.forward : Vector3.up;
                    Camera.transform.localRotation = Quaternion.LookRotation(nts, camUp);
                }
                else
                {
                    Camera.transform.localRotation = _landCamRot;
                }
                if (_shake > 0.001f)
                {
                    Camera.transform.localPosition += Random.insideUnitSphere * (_shake * 0.5f);
                }

                return;
            }

            var sp = _ship.transform.localPosition;
            var rot = _ship.transform.localRotation;
            if (_viewMode == 1)
            {
                // Cockpit: just ahead of the ship, facing its heading.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 0.7f, 1.9f);
                Camera.transform.localRotation = rot;
            }
            else
            {
                // Third-person: behind and above, looking at the ship.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 4.5f, -13f);
                Camera.transform.localRotation = Quaternion.LookRotation((sp + Vector3.up * 0.5f) - Camera.transform.localPosition, Vector3.up);
            }

            if (_shake > 0.001f)
            {
                Camera.transform.localPosition += Random.insideUnitSphere * (_shake * 0.5f);
            }
        }

        /// <summary>Arrival distance for a wreck waypoint (#1664): inside the mining beam's reach (40) and the
        /// server's manifest-reading approach range (45), so arriving IS visiting.</summary>
        private const float WreckArriveRange = 30f;

        /// <summary>Resolves the player's space waypoint (#597) to a scene position + squared arrival
        /// distance: a landable body id (arrive just outside land range), a station entity id (arrive in
        /// dock range), or a free point (arrive within a small fixed radius). False when no waypoint is
        /// set or an id no longer resolves in this space instance (a stale target counts as cleared).</summary>
        public bool TryResolveSpaceWaypoint(out Vector3 target, out float arriveSq)
        {
            target = Vector3.zero;
            arriveSq = 100f; // free-point arrival: within 10 units
            if (Game == null)
            {
                return false;
            }

            string id = Game.SpaceWaypointId;
            if (!string.IsNullOrEmpty(id))
            {
                foreach (var b in _landables)
                {
                    bool isHome = id == HomeWaypointId && string.IsNullOrEmpty(b.Id);
                    if (isHome || b.Id == id)
                    {
                        target = b.Pos;
                        float approach = b.Radius + KeepOutMargin + LandBand * 0.5f;
                        arriveSq = approach * approach;
                        return true;
                    }
                }

                var space = Game.Space;
                if (space != null)
                {
                    foreach (var e in space.Entities)
                    {
                        if (e.Kind == "SpaceStation" && e.Id == id)
                        {
                            if (StationApproachPoint(e.Id, DockApproachStandOff) is { } approach)
                            {
                                target = approach; // #1917: in front of the hangar mouth
                                arriveSq = DockApproachArrive * DockApproachArrive;
                            }
                            else
                            {
                                target = new Vector3(e.X, e.Y, e.Z);
                                arriveSq = BoardRange * BoardRange * 0.64f;
                            }

                            return true;
                        }

                        if (e.Kind == "Wreck" && e.Id == id)
                        {
                            // The system's derelict (#1664): a chart target like a station, arrive in salvage range.
                            target = new Vector3(e.X, e.Y, e.Z);
                            arriveSq = WreckArriveRange * WreckArriveRange;
                            return true;
                        }

                        if (IsEncounterWaypointKind(e.Kind) && e.Id == id)
                        {
                            // #2241/#2242: a life pod, an anomaly or a wormhole — fly up to it (the rift: into its prompt range).
                            target = new Vector3(e.X, e.Y, e.Z);
                            arriveSq = EncounterArriveRange * EncounterArriveRange;
                            return true;
                        }
                    }
                }

                return false;
            }

            if (Game.SpaceWaypointPos.HasValue)
            {
                target = Game.SpaceWaypointPos.Value;
                return true;
            }

            return false;
        }
    }
}
