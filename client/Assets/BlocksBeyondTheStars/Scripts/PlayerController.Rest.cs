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
    /// Sitting and resting: E on a chair seats the player (#806, one sitter per seat #2122 — <see cref="SitDown"/>), E on a
    /// bed or a bunk lays the player down (#2823 — <see cref="TryLieDown"/>, <see cref="TickRest"/>) with the pillow view,
    /// the warm dim and VEGA's good night, and any move gets the player up again (<see cref="GetUp"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        // Sit on a chair-shaped cell (#806): E on a chair seats the player — control freezes (the look
        // stays free), the eye eases to seat height, and the Seated flag rides the presence broadcast so
        // other players see the pose. The CharacterController is disabled while seated: the capsule sits
        // INSIDE the chair cell and must not fight the seat's own collider boxes.
        private static readonly Vector3 SeatedEye = new Vector3(0f, 1.05f, 0f);
        private Vector3Int? _seatCell;

        /// <summary>Forgets the chair seat (<see cref="PlayerTrainRider"/>: boarding a wagon frame leaves any seat behind).</summary>
        internal void ForgetSeat() => _seatCell = null;

        private int _satFrame; // debounce: the E that sat us down must not also stand us up

        // One seat, one sitter (#2122): the server refuses a seat somebody already sits on; every refusal bumps
        // GameBootstrap.SeatRejections, and a count that moved while we sit stands us back up.
        private int _seatRejectionsSeen;

        // Resting in a bed (#2823): E on a bed or a crew bunk still sets the home spawn and now also lays the player down.
        // Client-side only — other players need not see the pose, so nothing new goes on the wire. Control freezes (the
        // look stays free), the view settles onto the pillow and looks up and out over the foot of the bed, the screen
        // dims warm (UrpScenePost.SetRest), the music turns quiet and VEGA says good night. Any move, jump, crouch or E
        // gets up and eases it all back. No time skip, no bonus. The body stays where it stood (the bed's own collider
        // boxes never fight the capsule, and getting up needs no free cell) — only the camera lies down.
        private const float RestEaseSeconds = 1.5f;    // the warm dim and the quiet music fall (and rise again) over this long
        private const float RestSettleRate = 2.2f;     // how fast the view glides onto the pillow (exponential, 1/s)
        private const float RestSteerSeconds = 2.5f;   // how long the gentle look towards the foot of the bed is steered
        private const float RestRiseSeconds = 0.6f;    // getting up: the view rises from the pillow back into the body
        private const float RestPitch = -22f;          // the pillow view: gently up and out (negative pitch looks up)
        private const float RestPillowEye = 0.8f;      // eye height above the bed cell's floor (the pillow tops out at 0.62)
        private const float RestPillowInset = 0.18f;   // from the head cell's centre towards the headboard
        private const float RestQuartersReach = 3.5f;  // E at the ship's quarters lays you on its bunk from this close
        private const float RestLineCooldown = 120f;   // VEGA's good night at most this often (s) — a nap is no speech cue
        private const int RestLineVariants = 4;        // vega.rest.1 … vega.rest.4
        private Vector3Int? _restCell;   // the bed cell lain on — the world grid's, or a parked ship's hull cell (_restOnShip)
        private bool _restOnShip;
        private int _restFrame;          // debounce: the E that laid us down must not also get us up
        private bool _restMoveArmed;     // a held move/crouch gets us up only once it was let go after lying down
        private int _restEpoch;          // the world we lay down in — a world change gets us up
        private float _restSince;
        private bool _restLookFree;      // the player took the look over: stop steering it towards the pillow view
        private Vector3 _restCamPos;     // the camera's eased world position while lying, and where the rise starts from
        private float _restRise;         // 1 → 0 after getting up: the view's blend from the pillow back into the body
        private float _restLevel;        // the rest look 0 … 1 (dim + music), eased by TickRest
        private string _restLineKey = string.Empty; // VEGA's good-night line, taken back when we get up
        private int _lastRestLine;
        private float _lastRestLineAt = -RestLineCooldown;

        /// <summary>#2823: lying in a bed — the HUD shows how to get up instead of the usual prompts.</summary>
        public bool Resting => _restCell.HasValue;

        /// <summary>Seats the player on a chair-shaped cell (#806): parks the capsule centred on the cell
        /// (controller off — the seat's own collider boxes must not fight the snap) and reports the pose.</summary>
        private void SitDown(Vector3Int cell)
        {
            _seatCell = cell;
            _satFrame = Time.frameCount;
            _verticalVelocity = 0f;
            UpdateJetpack(false);
            UpdateGlide(false);
            _controller.enabled = false;
            transform.position = Game != null
                ? Game.ScenePos(cell.x + 0.5f, cell.y, cell.z + 0.5f)
                : new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);
            Avatar?.SetSeated(true);
            if (Game != null)
            {
                _seatRejectionsSeen = Game.SeatRejections; // only a refusal that arrives from now on stands us up
            }

            Game?.Network?.SendSitDown(cell.x, cell.y, cell.z); // the server checks the seat is free (#2122)
        }

        /// <summary>Whether a seated NPC or a seated remote player is on the seat cell as far as this client knows
        /// (#2122) — see <see cref="SeatCells"/>.</summary>
        private bool SeatLooksTaken(Vector3Int cell)
        {
            if (Game == null || Game.Npcs == null)
            {
                return false;
            }

            int circ = Game.Circumference;
            foreach (var n in Game.Npcs)
            {
                if (n != null && n.Pose == 1 && SeatCells.Covers(n.X, n.Y, n.Z, cell.x, cell.y, cell.z, circ))
                {
                    return true;
                }
            }

            return Remotes != null && Remotes.AnySeatedOn(cell.x, cell.y, cell.z);
        }

        /// <summary>#2985: whether a villager sleeps in the bed at <paramref name="cell"/> as far as this client knows — the bed
        /// twin of <see cref="SeatLooksTaken"/>. A sleeper lies across the seam between the head and the foot half, so a lying
        /// NPC in either half counts; a one-cell bed has only its own cell.</summary>
        private bool BedLooksTaken(Vector3Int cell)
        {
            if (Game == null || Game.Npcs == null || RestBedKey(cell, false, out int descriptor) != "bed")
            {
                return false;
            }

            var other = cell;
            if (FurnitureShapes.TryBedPartnerOffset(descriptor, out int dx, out int dz))
            {
                other = new Vector3Int(cell.x + dx, cell.y, cell.z + dz);
            }

            int circ = Game.Circumference;
            foreach (var n in Game.Npcs)
            {
                if (n != null && n.Pose == 2
                    && (SeatCells.Covers(n.X, n.Y, n.Z, cell.x, cell.y, cell.z, circ)
                        || SeatCells.Covers(n.X, n.Y, n.Z, other.x, other.y, other.z, circ)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stands the player back up from a chair (#806) and re-enables normal movement.</summary>
        private void StandUp()
        {
            _seatCell = null;
            _controller.enabled = true;
            Avatar?.SetSeated(false);
            Game?.Network?.SendSetSeated(false);
        }

        /// <summary>#2823: lays the player down on a bed or a crew bunk (see the rest fields) — only from solid footing on foot:
        /// not flying, climbing, seated, riding or in mid-air, where the body would be left hanging. The caller has already
        /// asked for the home spawn; a refusal there (someone else's bed, a derelict) still lets you rest.</summary>
        private void TryLieDown(Vector3Int cell, bool onShip)
        {
            if (Game == null || Camera == null || _restCell.HasValue || _seatCell.HasValue || _train.Frame != null
                || _flying || _climbing || _pullingUp || !_controller.enabled || !_controller.isGrounded
                || !RestPose(cell, onShip, out _, out _))
            {
                return;
            }

            _restCell = cell;
            _restOnShip = onShip;
            _restFrame = Time.frameCount;
            _restEpoch = Game.WorldEpoch;
            _restSince = Time.time;
            _restLookFree = false;
            _restRise = 0f;
            _restCamPos = Camera.transform.position;
            _restMoveArmed = !RestMoveHeld(); // still walking or crouching into the bed: that key must be let go first
            _verticalVelocity = 0f;
            UpdateJetpack(false);
            UpdateGlide(false);
            _optic?.Lower();

            // The aim readouts are not refreshed while lying (HandleStations does not run): drop the bed's prompt and any
            // network outline now instead of leaving them on screen until we get up.
            Game.AimedStationBlock = null;
            Game.AimedCrystalDevice = null;
            Game.AimedCell = null;

            // The pillow view is a first-person one in either camera mode: no body left standing beside the bed in the
            // chase view, no tool held in front of the face. GetUp hands both back to the camera mode.
            Avatar?.SetVisible(false);
            _viewmodel?.SetVisible(false);
            SayGoodNight();
        }

        /// <summary>#2823: a move stick/key or crouch is held — the "get up" inputs that are held rather than pressed.</summary>
        private static bool RestMoveHeld()
            => InputMap.CrouchHeld() || Mathf.Abs(InputMap.MoveX()) > 0.3f || Mathf.Abs(InputMap.MoveY()) > 0.3f;

        /// <summary>#2823: E or jump gets up at once; a move or crouch only once it was let go after lying down — a player
        /// still holding forward from walking up to the bed would otherwise be up again the very next frame.</summary>
        private bool RestWantsUp()
        {
            if (Time.frameCount == _restFrame)
            {
                return false; // the E that laid us down
            }

            bool held = RestMoveHeld();
            if (!held)
            {
                _restMoveArmed = true;
            }

            return InputMap.JumpDown() || InputMap.Down(InputAction.Interact) || (_restMoveArmed && held);
        }

        /// <summary>#2823: gets up from the bed — control is back at once, and the view rises from the pillow into the body over
        /// <see cref="RestRiseSeconds"/> (<see cref="ApplyRestRise"/>). <paramref name="instant"/> (death, a teleport, a world
        /// change, the flight view) and "Camera motion" off skip the rise: a glide across that distance would be a swoop. The
        /// warm dim and the music ease back in <see cref="TickRest"/>.</summary>
        private void GetUp(bool instant)
        {
            if (!_restCell.HasValue)
            {
                return;
            }

            _restCell = null;
            _restRise = instant || !CameraMotion || UiKit.ReducedMotion ? 0f : 1f;
            if (Game != null && Game.SpaceViewActive)
            {
                // The flight view has re-parented the camera and puts it back itself — hand back only the body and the item.
                Avatar?.SetVisible(ThirdPerson);
                _viewmodel?.SetVisible(!ThirdPerson);
            }
            else
            {
                ApplyCameraMode(); // the eye back in the body, the avatar or the held item shown per the camera mode
                if (_restRise > 0f && Camera != null)
                {
                    Camera.transform.position = _restCamPos; // the rise starts on the pillow; UpdateCameraFeel carries it on
                }
            }

            if (_restLineKey.Length > 0)
            {
                Game?.Views.Vega?.WithdrawLine(_restLineKey); // a good night still on screen after getting up reads stale
                _restLineKey = string.Empty;
            }
        }

        /// <summary>#2823, per frame before any early return: ends the rest the moment something else takes the player — death,
        /// a respawn or teleport snap, a world change, the flight view, a vehicle or a train, a cutscene — or the bed is gone
        /// (mined, its chunk unloaded, the parked ship took off); then eases the rest look (the warm dim, the quiet music)
        /// towards lying or awake over <see cref="RestEaseSeconds"/>. An open menu leaves both as they are.</summary>
        private void TickRest()
        {
            if (_restCell is { } bed && RestInterrupted(bed))
            {
                GetUp(instant: true);
            }

            float target = _restCell.HasValue ? 1f : 0f;
            if (_restLevel != target)
            {
                _restLevel = Mathf.MoveTowards(_restLevel, target, Time.deltaTime / RestEaseSeconds);
                ApplyRestLook(Mathf.SmoothStep(0f, 1f, _restLevel));
            }
        }

        private bool RestInterrupted(Vector3Int bed)
            => Game == null || Game.WorldEpoch != _restEpoch || !_spawned || _settling
               || Game.Health <= 0f || Game.AwaitingRespawnConfirm
               || Game.SpaceViewActive || !string.IsNullOrEmpty(Game.InSpeeder) || !string.IsNullOrEmpty(Game.InTrain)
               || Game.CinematicCameraActive || GameBootstrap.CutsceneActive
               || !RestPose(bed, _restOnShip, out _, out _);

        /// <summary>#2823: the rest look at <paramref name="level"/> (0 awake … 1 lying): the warm, gently dimmed grade and
        /// the quieter music.</summary>
        private static void ApplyRestLook(float level)
        {
            var post = UrpScenePost.Instance;
            if (post != null)
            {
                post.SetRest(level);
            }

            ClientMusic.BedRestQuiet = level;
        }

        /// <summary>#2823: the pillow view, per frame while lying. The camera glides onto the pillow (one cut with camera motion
        /// off) and, for the first moments, the look is steered gently up and out over the foot of the bed — until the player
        /// looks around on their own. The body never moves; the camera is placed in world space.</summary>
        private void HoldRestView(Vector3Int bed)
        {
            if (Camera == null || !RestPose(bed, _restOnShip, out var eye, out float? lookYaw))
            {
                return;
            }

            bool calm = !CameraMotion || UiKit.ReducedMotion;
            float k = calm ? 1f : 1f - Mathf.Exp(-RestSettleRate * Time.deltaTime);
            if (!_restLookFree)
            {
                if (Mathf.Abs(InputMap.LookX()) > 0.05f || Mathf.Abs(InputMap.LookY()) > 0.05f
                    || Time.time - _restSince > RestSteerSeconds)
                {
                    _restLookFree = true;
                }
                else
                {
                    if (lookYaw is { } yaw)
                    {
                        transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(transform.eulerAngles.y, yaw, k), 0f);
                    }

                    _pitch = Mathf.Lerp(_pitch, RestPitch, k);
                    Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
                    _restLookFree = calm; // without camera motion the view cuts there once, then the look is the player's
                }
            }

            _restCamPos = calm ? eye : Vector3.Lerp(_restCamPos, eye, k);
            Camera.transform.position = _restCamPos;
        }

        /// <summary>#2823: just up from a bed — the view rises from the pillow into the body over <see cref="RestRiseSeconds"/>
        /// instead of jumping there. Runs after the camera feel placed the eye (first person) or the boom (third person).</summary>
        private void ApplyRestRise(float dt)
        {
            if (_restRise <= 0f)
            {
                return;
            }

            Camera.transform.position = Vector3.Lerp(Camera.transform.position, _restCamPos, Mathf.SmoothStep(0f, 1f, _restRise));
            _restRise = Mathf.Max(0f, _restRise - (dt / RestRiseSeconds));
        }

        /// <summary>#2823: the pillow view of a bed cell — the eye just above the pillow end, and the heading along the bed
        /// towards its foot. A two-cell bed knows its ends (the head half's foot lies along its yaw,
        /// <see cref="FurnitureShapes.TryBedPartnerOffset"/>), so lying down from either half puts the head on the pillow. A
        /// one-cell bed or a crew bunk has no ends: the eye rests above its middle and keeps the player's heading
        /// (<paramref name="lookYaw"/> null). False when the cell holds no bed (any more).</summary>
        private bool RestPose(Vector3Int cell, bool onShip, out Vector3 eye, out float? lookYaw)
        {
            eye = default;
            lookYaw = null;
            if (Game == null || RestBedKey(cell, onShip, out int descriptor) is not ("bed" or "crew_bunk"))
            {
                return false;
            }

            if (FurnitureShapes.TryBedPartnerOffset(descriptor, out int dx, out int dz))
            {
                bool head = ShapeCode.ShapeOf(descriptor) == (int)BlockShape.BedHead;
                int fx = head ? dx : -dx, fz = head ? dz : -dz; // head → foot
                float hx = (head ? cell.x : cell.x + dx) + 0.5f - (fx * RestPillowInset);
                float hz = (head ? cell.z : cell.z + dz) + 0.5f - (fz * RestPillowInset);
                eye = Game.ScenePos(hx, cell.y + RestPillowEye, hz);
                lookYaw = Mathf.Atan2(fx, fz) * Mathf.Rad2Deg;
                return true;
            }

            // A crew bunk fills its cell; the one-cell bed is a slab (PropShapes.BedSingleCell) — lie on top of either.
            float top = ShapeCode.IsCube(descriptor) ? 1f : 0.5f;
            eye = Game.ScenePos(cell.x + 0.5f, cell.y + top + 0.3f, cell.z + 0.5f);
            return true;
        }

        /// <summary>#2823: the block key of a bed cell — the world grid's, or a parked ship's hull cell — and its shape
        /// descriptor (which half of the bed, which way it lies).</summary>
        private string RestBedKey(Vector3Int cell, bool onShip, out int descriptor)
        {
            descriptor = 0;
            if (Game?.World == null || Game.Content == null)
            {
                return null;
            }

            if (!onShip)
            {
                descriptor = Game.World.GetShape(cell.x, cell.y, cell.z);
                return Game.Content.BlockById(Game.World.GetBlock(cell.x, cell.y, cell.z))?.Key;
            }

            var id = Game.LandedShipBlockAt(cell.x, cell.y, cell.z, out var ship, out var local);
            if (ship == null)
            {
                return null;
            }

            descriptor = ship.Shapes.TryGetValue(local, out int shape) ? shape : 0;
            return Game.Content.BlockById(id)?.Key;
        }

        /// <summary>#2823: the bed of the ship's quarters within <see cref="RestQuartersReach"/> — the cabin's crew bunk or the
        /// head of its bed, in the world grid (the floating interior) or in a parked ship's hull. False when there is none.</summary>
        private bool QuartersBedCell(out Vector3Int cell, out bool onShip)
        {
            cell = default;
            onShip = false;
            if (Game?.Stations == null)
            {
                return false;
            }

            var p = transform.position;
            float bestSq = RestQuartersReach * RestQuartersReach;
            bool found = false;
            foreach (var s in Game.Stations)
            {
                if (s == null || s.Type != "quarters")
                {
                    continue;
                }

                // Both ground axes wrap, as in GameBootstrap.NearestStationType; the station sits at its cell's floor centre.
                float dx = (float)WorldConstants.WrapDeltaX(s.X - p.x, Game.Circumference), dy = s.Y - p.y;
                float dz = (float)WorldConstants.WrapDeltaZ(s.Z - p.z, Game.Circumference);
                float d = (dx * dx) + (dy * dy) + (dz * dz);
                if (d < bestSq)
                {
                    bestSq = d;
                    cell = new Vector3Int(Mathf.FloorToInt(s.X), Mathf.FloorToInt(s.Y), Mathf.FloorToInt(s.Z));
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            if (RestBedKey(cell, false, out _) is "bed" or "crew_bunk")
            {
                return true;
            }

            onShip = true;
            return RestBedKey(cell, true, out _) is "bed" or "crew_bunk";
        }

        /// <summary>#2823: VEGA's short good night — one of <see cref="RestLineVariants"/> lines, never the same twice in a row,
        /// at most every <see cref="RestLineCooldown"/> seconds. Spoken like a client lesson (the hints switch mutes it, it is
        /// dropped once the moment has passed) but kept out of the tips log: it is a remark, not a tip.</summary>
        private void SayGoodNight()
        {
            var vega = Game?.Views.Vega;
            if (vega == null || Time.time - _lastRestLineAt < RestLineCooldown)
            {
                return;
            }

            int n = UnityEngine.Random.Range(1, _lastRestLine == 0 ? RestLineVariants + 1 : RestLineVariants);
            if (_lastRestLine != 0 && n >= _lastRestLine)
            {
                n++; // skip the line said last time
            }

            _lastRestLine = n;
            _lastRestLineAt = Time.time;
            _restLineKey = "vega.rest." + n;
            vega.SayLocal(_restLineKey, keepInLog: false);
        }
    }
}
