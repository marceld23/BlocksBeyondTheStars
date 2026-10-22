// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Vehicles: the arcade hover speeder and the boat (#1215) — driving (<see cref="DriveSpeeder"/>), the fluid probes
    /// under and ahead of the hull, boarding, stowing and recalling (<see cref="TryBoardNearbySpeeder"/>,
    /// <see cref="TryStowNearbySpeeder"/>, <see cref="TryRecallVehiclesAtConsole"/>) and the parked-vehicle probes the
    /// HUD and the Actions list read.
    /// </summary>
    public sealed partial class PlayerController
    {
        // Hover speeder (arcade, car-style): W gas / S brake-reverse / A,D steer / Space hop / Shift boost.
        public float SpeederMaxSpeed = 13f;
        public float SpeederBoostSpeed = 20f;
        public float SpeederAccel = 16f;
        public float SpeederTurnSpeed = 95f;     // degrees/sec, scaled by current speed
        public float SpeederHoverHeight = 1.3f;  // metres held above the ground below
        public float SpeederHopSpeed = 6f;       // Space gives a quick lift over a low obstacle
        public float SpeederImpactThreshold = 9f; // a hard stop above this speed reports a collision
        public float SpeederBoardRange = 3.2f;
        public float SpeederStowRange = 5f;       // = the server's pack-up reach (was 3.5: a sunk speeder was silently out of reach, #1661)
        public float SpeederHintRange = 14f;      // own parked vehicle within this but beyond stow reach → the HUD says so

        // Boat (#1215) — the water kind of the same vehicle system: slower, lazier steering, it drifts, and it
        // floats on the water column instead of hovering over a ground raycast. Speeds come from the item's
        // "vehicle" block in items.json when present; these are the fallbacks.
        public float BoatMaxSpeed = 9f;
        public float BoatBoostSpeed = 13f;
        public float BoatAccel = 7f;
        public float BoatTurnSpeed = 60f;        // degrees/sec, scaled by current speed
        public float BoatFloatHeight = 0.35f;    // driver feet above the waterline
        private const float BoatGrip = 2.5f;     // how fast the hull velocity follows the bow (lower = more drift)

        /// <summary>A parked speeder of ours is within stow range (StowVehicle applies).</summary>
        public bool NearOwnParkedSpeeder
        {
            get
            {
                if (Game?.Speeders == null)
                {
                    return false;
                }

                float rangeSq = SpeederStowRange * SpeederStowRange;
                foreach (var s in Game.Speeders)
                {
                    if (s != null && s.OwnerId == Game.LocalPlayerId && string.IsNullOrEmpty(s.DriverId)
                        && (Game.ScenePos(s.X, s.Y, s.Z) - transform.position).sqrMagnitude < rangeSq)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Standing at the own landed ship's cockpit or console with a deployed vehicle out on this world
        /// (RecallVehicle applies, #1661). Never aboard the floating interior — the ship is not landed there.</summary>
        public bool CanRecallVehicle
            => Game != null && (Game.NearbyStation == "cockpit" || Game.NearbyStation == "console")
               && Game.LoadingPlanetType != "ship_interior" && OwnParkedVehicleCount > 0;

        /// <summary>How many of our own vehicles stand parked (not driven) on this world.</summary>
        public int OwnParkedVehicleCount
        {
            get
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
        }

        // Speeder drive state.
        private float _speederSpeed;
        private bool _wasDriving;
        private float _speederCamPitch = 10f;

        // Boat drive state (#1215).
        private bool _drivingBoat;        // the vehicle we boarded is a boat (remembered for the dismount cue)
        private float _speederBlockedSeconds; // the speeder wants to move but the capsule does not: wedged against a lip (#1660)
        private bool _speederAutoHop;         // next frame's hover gets a hop to clear that lip
        private Vector3 _speederLastDryPos;   // last driven pose over nothing the speeder may not cross (shore stop, #1660/#2535)
        private bool _speederHasDryPos;
        private Vector3 _boatVel;         // planar hull velocity — lags the bow, so the boat drifts through turns
        private float _boatBobPhase;      // sine phase of the idle bob
        private float _boatDrySeconds;    // how long the hull has had no water under it (aground)
        private Vector3 _boatLastWetPos;  // last pose with water under the hull — a beached boat eases back to it
        private bool _boatHasWetPos;

        /// <summary>Arcade hover driving (car-style): W/S throttle, A/D steer, Space hop, Shift boost. Holds a
        /// fixed height over the ground, can't climb steep walls (a hard stop reports a collision), and runs on
        /// the speeder's own energy cell (empty = no propulsion). F dismounts, R refuels.
        /// A <b>boat</b> (#1215) is the same loop with a different vertical model and lazier handling: it floats
        /// on the water column (<see cref="DriveBoatVertical"/>), drifts through turns, has no hop and no fuel,
        /// and runs aground instead of crashing when it leaves the water.</summary>
        private void DriveSpeeder()
        {
            var driven = Game.DrivenSpeeder;
            bool boat = driven != null && driven.Kind == "boat";
            if (!_wasDriving)
            {
                _wasDriving = true;
                _drivingBoat = boat;
                _speederSpeed = 0f;
                _boatVel = Vector3.zero;
                _boatDrySeconds = 0f;
                _boatHasWetPos = false;
                _speederBlockedSeconds = 0f;
                _speederAutoHop = false;
                _speederHasDryPos = false;
                Avatar?.SetVisible(true);   // sit visibly in the vehicle
                _viewmodel?.SetVisible(false);
                ClientAudio.Instance?.SpeederStart(boat);
            }

            // Chase camera behind + above the vehicle; mouse Y tilts it.
            float my = InputMap.LookY() * MouseSensitivity * (InvertY ? -1f : 1f);
            _speederCamPitch = Mathf.Clamp(_speederCamPitch - my, -8f, 35f);
            if (Camera != null)
            {
                // Same spring arm as the third-person view (#1460) — the chase boom clipped into cliffs and walls.
                Camera.transform.position = ResolveCameraBoom(
                    transform.TransformPoint(new Vector3(0f, 1.2f, 0f)), transform.TransformPoint(new Vector3(0f, 2.4f, -5.5f)),
                    Time.deltaTime);
                Camera.transform.localEulerAngles = new Vector3(_speederCamPitch, 0f, 0f);
            }

            // Speeds: the item's "vehicle" block (items.json) is the source of truth when present, the fields
            // above are the fallback. Accel/turn stay client tuning.
            var profile = Game.Content?.GetItem(boat ? "boat" : "speeder")?.Vehicle;
            float cruise = profile?.MaxSpeed ?? (boat ? BoatMaxSpeed : SpeederMaxSpeed);
            float boost = profile?.BoostSpeed ?? (boat ? BoatBoostSpeed : SpeederBoostSpeed);
            float accel = boat ? BoatAccel : SpeederAccel;
            float turn = boat ? BoatTurnSpeed : SpeederTurnSpeed;

            // FuelMax 0 (the boat) means "never runs dry" — not "empty".
            bool outOfFuel = driven != null && driven.FuelMax > 0.01f && driven.Fuel <= 0.01f;

            float throttle = InputMap.MoveY();   // W = +1, S = -1 (brake / reverse)
            float steer = InputMap.MoveX();    // A = -1, D = +1
            bool boosting = InputMap.Held(InputAction.SpeederBoost) && !outOfFuel && throttle > 0.1f;

            // Steering scales with speed (no pirouetting while parked).
            float speedFrac = Mathf.Clamp01(Mathf.Abs(_speederSpeed) / cruise);
            transform.Rotate(0f, steer * turn * (0.35f + 0.65f * speedFrac) * Time.deltaTime, 0f);

            float maxSpeed = boosting ? boost : cruise;
            float targetSpeed = outOfFuel ? 0f : (throttle >= 0f ? throttle * maxSpeed : throttle * cruise * 0.45f);

            float vSpeed;
            Vector3 planar;
            if (boat)
            {
                bool aground = !DriveBoatVertical(out vSpeed);
                if (aground)
                {
                    // Beached: no way forward, only a slow reverse off the bank, and the speed bleeds off fast.
                    // Running aground is a nudge, not a crash — no damage, no impact report.
                    targetSpeed = Mathf.Min(0f, throttle) * cruise * 0.3f;
                    _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, accel * 2.5f * Time.deltaTime);
                }
                else
                {
                    _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, accel * Time.deltaTime);
                }

                // Drift: the hull keeps sliding the way it was going while the bow comes round.
                _boatVel = Vector3.Lerp(_boatVel, transform.forward * _speederSpeed, 1f - Mathf.Exp(-BoatGrip * Time.deltaTime));
                planar = _boatVel;
                if (aground && _boatHasWetPos && _boatDrySeconds > 0.6f)
                {
                    // Ease back toward the last wet spot so a boat nosed onto sand slides off again by itself.
                    Vector3 back = _boatLastWetPos - transform.position;
                    back.y = 0f;
                    if (back.sqrMagnitude > 0.01f)
                    {
                        planar += back.normalized * 1.2f;
                    }
                }

                Game.VehicleAground = aground;
            }
            else
            {
                // Shore stop (#1660): the hover speeder is a LAND vehicle (the boat exists for the water). Water in
                // the cells it is about to enter caps the throttle at zero — reverse stays free — and if it is
                // over water anyway (a slope pushed it in, or it was deployed into the sea before the deploy
                // check existed) it floats on the surface, can only back out, and eases toward the last dry
                // pose the way a beached boat eases back to the water.
                // #2535: a speeder whose item says "hoverOverWater" (the hover speeder does) takes the water surface
                // for ground — no shore stop, no reverse-only cap. Lava, oil and gas stay a stop for every speeder: they
                // are checked explicitly here, never as ground (the block column skips them as it always did).
                bool waterOk = profile?.HoverOverWater ?? false;
                var under = SpeederFluidUnder(transform.position, out float fluidSurface);
                bool stranded = under == SpeederFluid.Hazard || (under == SpeederFluid.Water && !waterOk);
                if (stranded)
                {
                    targetSpeed = Mathf.Min(0f, throttle) * cruise * 0.3f;
                    _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, accel * 2.5f * Time.deltaTime);
                }
                else
                {
                    _speederLastDryPos = transform.position; // a safe pose: dry ground, or water this speeder hovers over
                    _speederHasDryPos = true;
                    if (_speederSpeed > 0f && FluidAhead(transform.position, transform.forward, 1.6f + _speederSpeed * 0.3f, waterOk))
                    {
                        targetSpeed = Mathf.Min(targetSpeed, 0f);
                        _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, accel * 2.5f * Time.deltaTime);
                    }
                    else
                    {
                        _speederSpeed = Mathf.MoveTowards(_speederSpeed, targetSpeed, accel * Time.deltaTime);
                    }
                }

                // Hover: hold a fixed height above whatever ground is below; sink gently over a void/edge. The
                // ray starts INSIDE the driver's capsule — from 2.5 m up it met the capsule's own top first
                // (1.8 m), failed the self-hit guard and fell through to the "sink" branch every frame, so
                // hover never really engaged and nothing stopped a speeder from following the seabed (#1660).
                // The block column backs the physics ray up: it knows a chunk whose collider is still baking,
                // and it is what lifts a hull that got wedged into a block back out.
                float groundY = float.NaN;
                if (Physics.Raycast(transform.position + Vector3.up * 1.0f, Vector3.down, out var hit, 12f, WorldRayMask, QueryTriggerInteraction.Ignore)
                    && hit.collider != _controller)
                {
                    groundY = hit.point.y;
                }

                if (BlockColumnGround(transform.position, out float columnY) && (float.IsNaN(groundY) || columnY > groundY))
                {
                    groundY = columnY;
                }

                bool overFluid = under != SpeederFluid.None;
                bool hovering = !float.IsNaN(groundY) || overFluid;
                if (hovering)
                {
                    // Over water it may cross (#2535) the surface is the ground: full hover height above it. Stranded
                    // over a fluid it may not cross, it only floats on top while it backs out, as before.
                    float overSurface = fluidSurface + (stranded ? 0.4f : SpeederHoverHeight);
                    float targetY = float.IsNaN(groundY) ? overSurface : groundY + SpeederHoverHeight;
                    if (overFluid)
                    {
                        targetY = Mathf.Max(targetY, overSurface); // never below the surface
                    }

                    vSpeed = Mathf.Clamp((targetY - transform.position.y) * 6f, -10f, 8f);
                }
                else
                {
                    vSpeed = -Gravity * 0.2f;
                }

                if ((InputMap.JumpDown() || _speederAutoHop) && !outOfFuel)
                {
                    vSpeed = SpeederHopSpeed; // a quick hover-hop over a low obstacle
                    _speederAutoHop = false;
                }

                planar = transform.forward * _speederSpeed;
                if (stranded && _speederHasDryPos)
                {
                    Vector3 back = _speederLastDryPos - transform.position;
                    back.y = 0f;
                    if (back.sqrMagnitude > 0.01f)
                    {
                        planar += back.normalized * 1.2f;
                    }
                }

                Game.VehicleAground = stranded;
            }

            Vector3 before = transform.position;
            _controller.Move((planar + Vector3.up * vSpeed) * Time.deltaTime);

            if (!boat)
            {
                // Wedged (#1660): the speeder wants to move but the capsule does not — a lip it cannot slide
                // over, a block it drove into. After a moment it hops by itself, and the on-foot rescue keeps
                // watching for a capsule that ended up inside geometry.
                Vector3 step = transform.position - before;
                step.y = 0f;
                bool blocked = Mathf.Abs(_speederSpeed) > 2f && step.magnitude / Mathf.Max(1e-4f, Time.deltaTime) < 0.3f;
                _speederBlockedSeconds = blocked ? _speederBlockedSeconds + Time.deltaTime : 0f;
                if (_speederBlockedSeconds > 0.8f)
                {
                    _speederBlockedSeconds = 0f;
                    _speederAutoHop = true;
                }

                _verticalVelocity = 0f;
                GuardAgainstFallingOut(grounded: Mathf.Abs(vSpeed) < 2f, inWater: false, onLadder: false);
            }

            // A hard horizontal stop at speed = ran into a wall/cliff → report the impact (server scales the hull
            // damage from the speed and jolts the driver). For the boat the intended speed is the drifting hull's,
            // not the bow's, so a sharp turn never reads as a crash.
            float wanted = boat ? planar.magnitude : Mathf.Abs(_speederSpeed);
            Vector3 moved = transform.position - before;
            moved.y = 0f;
            float actual = moved.magnitude / Mathf.Max(1e-4f, Time.deltaTime);
            if (wanted > SpeederImpactThreshold && actual < wanted * 0.45f)
            {
                Game.Network?.SendSpeederImpact(Game.InSpeeder, wanted);
                _speederSpeed *= 0.15f;
                _boatVel *= 0.15f;
                ClientAudio.Instance?.Cue("vehicle_impact", 0.85f);
            }

            ClientAudio.Instance?.SpeederTick(speedFrac, boosting, boat);
            Game.SpeederSpeed = boat ? planar.magnitude * Mathf.Sign(_speederSpeed) : _speederSpeed; // vehicle HUD readout

            if (InputMap.Down(InputAction.SpeederExit))
            {
                Game.Network?.SendExitSpeeder();
            }
            else if (InputMap.Down(InputAction.SpeederRefuel) && !boat)
            {
                Game.Network?.SendRefuelSpeeder(Game.InSpeeder);
            }
        }

        /// <summary>Boat float model (#1215): samples the water column under the hull (one below the feet up to two
        /// above, so a swell or a step in flowing water still counts), eases the hull onto the surface +
        /// <see cref="BoatFloatHeight"/> with a slow sine bob, and remembers the pose as the last wet one. Returns
        /// false when there is no water within a block under the hull — aground — in which case the hull settles
        /// onto the ground like a very low hover, so a boat can nose onto a beach and the driver can step off.
        /// Only streamed chunks are judged: a cell the client has not received yet (#987 far-water LOD) holds the
        /// current height and never reads as land.</summary>
        private bool DriveBoatVertical(out float vSpeed)
        {
            Vector3 pos = transform.position;
            int fx = Mathf.FloorToInt(pos.x), fz = Mathf.FloorToInt(pos.z);
            int feet = Mathf.FloorToInt(pos.y);
            float surface = float.NaN;
            if (Game?.World == null || !Game.World.TryGetBlock(fx, feet, fz, out _))
            {
                surface = pos.y - BoatFloatHeight; // unknown terrain: hold height, don't judge
            }
            else
            {
                for (int y = feet + 2; y >= feet - 1; y--)
                {
                    if (BlockKeyAt(new Vector3(pos.x, y + 0.5f, pos.z)) == "water")
                    {
                        surface = y + 1f;
                        break;
                    }
                }
            }

            if (float.IsNaN(surface))
            {
                _boatDrySeconds += Time.deltaTime;
                if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 6f, WorldRayMask, QueryTriggerInteraction.Ignore)
                    && hit.collider != _controller)
                {
                    vSpeed = Mathf.Clamp((hit.point.y + 0.4f - pos.y) * 6f, -6f, 4f);
                }
                else
                {
                    vSpeed = -Gravity * 0.2f;
                }

                return false;
            }

            _boatDrySeconds = 0f;
            _boatLastWetPos = pos;
            _boatHasWetPos = true;
            _boatBobPhase += Time.deltaTime * 1.6f;
            float targetY = surface + BoatFloatHeight + Mathf.Sin(_boatBobPhase) * 0.06f;
            vSpeed = Mathf.Clamp((targetY - pos.y) * 5f, -6f, 6f);
            return true;
        }

        /// <summary>What a hover speeder's hull is over (#2535): nothing to speak of, water, or a fluid no speeder may
        /// cross (lava, or a still liquid such as oil).</summary>
        private enum SpeederFluid
        {
            None,
            Water,
            Hazard,
        }

        /// <summary>The fluid under the hover speeder at <paramref name="pos"/>: the first cell from the feet down (two
        /// below at most) that is neither air nor a walk-through prop, and the top of that fluid. Real ground first means
        /// no fluid — a bridge over a lake is ground. The client twin of the server's <c>FluidUnderHull</c>. Unknown
        /// terrain (a chunk not streamed yet) is not judged.</summary>
        private SpeederFluid SpeederFluidUnder(Vector3 pos, out float surface)
        {
            surface = float.NaN;
            int fx = Mathf.FloorToInt(pos.x), fz = Mathf.FloorToInt(pos.z), feet = Mathf.FloorToInt(pos.y);
            if (Game?.World == null || !Game.World.TryGetBlock(fx, feet, fz, out _))
            {
                return SpeederFluid.None;
            }

            for (int y = feet; y >= feet - 2; y--)
            {
                var kind = FluidKindAt(new Vector3(pos.x, y + 0.5f, pos.z), out bool ground);
                if (kind != SpeederFluid.None)
                {
                    surface = y + 1f;
                    return kind;
                }

                if (ground)
                {
                    return SpeederFluid.None;
                }
            }

            return SpeederFluid.None;
        }

        /// <summary>The speeder's fluid kind of one cell; <paramref name="ground"/> is set for a cell that is real ground
        /// (neither air, a fluid nor a walk-through prop such as grass on the bank).</summary>
        private SpeederFluid FluidKindAt(Vector3 at, out bool ground)
        {
            ground = false;
            if (Game?.World == null || Game.Content == null)
            {
                return SpeederFluid.None;
            }

            var id = Game.World.GetBlock(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y), Mathf.FloorToInt(at.z));
            if (id.IsAir)
            {
                return SpeederFluid.None;
            }

            // The same classification as the server's FluidUnderHull: water is the one fluid a flagged speeder crosses,
            // VehicleProperties.IsHoverHazard (lava, oil, a gas giant's gas sea) is a stop for every speeder.
            var def = Game.Content.BlockById(id);
            if (def?.Key == BlocksBeyondTheStars.Shared.Definitions.VehicleProperties.HoverWaterKey)
            {
                return SpeederFluid.Water;
            }

            if (BlocksBeyondTheStars.Shared.Definitions.VehicleProperties.IsHoverHazard(def))
            {
                return SpeederFluid.Hazard;
            }

            if (def != null && BlocksBeyondTheStars.Shared.Definitions.WalkThroughBlocks.IsWalkThrough(def.Key))
            {
                return SpeederFluid.None;
            }

            ground = true;
            return SpeederFluid.None;
        }

        /// <summary>A fluid the speeder may not cross within <paramref name="dist"/> metres ahead at hull height (feet
        /// down to two below) — the shore stop's look-ahead, scaled by speed by the caller so the speeder brakes before
        /// the bank. Lava, oil and gas always count; water only when <paramref name="waterOk"/> is off (#2535).</summary>
        private bool FluidAhead(Vector3 pos, Vector3 forward, float dist, bool waterOk)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            forward.Normalize();
            int feet = Mathf.FloorToInt(pos.y);
            for (float d = 1f; d <= dist; d += 1f)
            {
                Vector3 probe = pos + forward * d;
                for (int y = feet; y >= feet - 2; y--)
                {
                    var kind = FluidKindAt(new Vector3(probe.x, y + 0.5f, probe.z), out bool ground);
                    if (kind == SpeederFluid.Hazard || (kind == SpeederFluid.Water && !waterOk))
                    {
                        return true;
                    }

                    if (kind != SpeederFluid.None || ground)
                    {
                        break; // crossable water or real ground at this probe — look further ahead
                    }
                }
            }

            return false;
        }

        /// <summary>The top face of the first colliding block in the column from the feet cell down (eight cells),
        /// from the streamed block data rather than the physics scene. A colliding FEET cell means the hull is
        /// inside a block — the surface reported is then the top of that block, which lifts it out.</summary>
        private bool BlockColumnGround(Vector3 pos, out float surfaceY)
        {
            surfaceY = float.NaN;
            if (Game?.World == null)
            {
                return false;
            }

            int feet = Mathf.FloorToInt(pos.y);
            for (int y = feet; y >= feet - 8; y--)
            {
                if (IsCollidingKey(BlockKeyAt(new Vector3(pos.x, y + 0.5f, pos.z))))
                {
                    surfaceY = y + 1f;
                    return true;
                }
            }

            return false;
        }

        /// <summary>X at the own cockpit/console: asks the ship to bring every vehicle we left out on this world
        /// back beside it (#1661). Returns true if at least one request went out.</summary>
        private bool TryRecallVehiclesAtConsole()
        {
            if (Game?.Network == null || Game.Speeders == null || !CanRecallVehicle)
            {
                return false;
            }

            bool any = false;
            foreach (var s in Game.Speeders)
            {
                if (s != null && s.OwnerId == Game.LocalPlayerId && string.IsNullOrEmpty(s.DriverId))
                {
                    Game.Network.SendRecallVehicle(s.Id);
                    any = true;
                }
            }

            return any;
        }

        /// <summary>Boards the nearest parked speeder the player owns within reach (on-foot E). Returns true if one
        /// was found and a board intent sent.</summary>
        private bool TryBoardNearbySpeeder()
        {
            if (Game?.Network == null || Game.Speeders == null)
            {
                return false;
            }

            string best = null;
            float bestSq = SpeederBoardRange * SpeederBoardRange;
            foreach (var s in Game.Speeders)
            {
                if (s == null || s.OwnerId != Game.LocalPlayerId || !string.IsNullOrEmpty(s.DriverId))
                {
                    continue;
                }

                float d = (Game.ScenePos(s.X, s.Y, s.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Id;
                }
            }

            if (best == null)
            {
                return false;
            }

            Game.Network.SendEnterSpeeder(best);
            return true;
        }

        /// <summary>Packs the nearest parked speeder the player owns back into the item (on-foot X).</summary>
        private bool TryStowNearbySpeeder()
        {
            if (Game?.Network == null || Game.Speeders == null)
            {
                return false;
            }

            string best = null;
            float bestSq = SpeederStowRange * SpeederStowRange;
            foreach (var s in Game.Speeders)
            {
                if (s == null || s.OwnerId != Game.LocalPlayerId || !string.IsNullOrEmpty(s.DriverId))
                {
                    continue;
                }

                float d = (Game.ScenePos(s.X, s.Y, s.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = s.Id;
                }
            }

            if (best == null)
            {
                return false;
            }

            Game.Network.SendStowSpeeder(best);
            return true;
        }
    }
}
