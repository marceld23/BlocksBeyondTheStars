// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The moving frame of a monorail train (#2113): parenting the body to the wagon (<see cref="UpdateTrainFrame"/>), the
    /// seat (<see cref="HoldTrainSeat"/>), boarding with E (<see cref="TrainInteract"/>), walk-on boarding
    /// (<see cref="StepAboardTrain"/>), crossing to the neighbouring wagon and the framed move report
    /// (<see cref="TrySendFramedMove"/>). A sibling component of <see cref="PlayerController"/>, added in its <c>Awake</c>
    /// on the player's GameObject; it reads the controller's wiring (<c>Player.Game</c>, <c>Player.Avatar</c>) on every
    /// use, because <see cref="WorldRig"/> assigns those only after the component exists.
    /// </summary>
    public sealed class PlayerTrainRider : MonoBehaviour
    {
        /// <summary>The player this component carries (set by <see cref="PlayerController"/> in its <c>Awake</c>).</summary>
        public PlayerController Player;

        // ---------------------------------------------------------------------------------------------------------
        // #2113: the moving frame — riding a monorail train
        // ---------------------------------------------------------------------------------------------------------

        private Transform _trainFrame;
        private string _trainFrameId = string.Empty;
        private TrainView _trainView;
        private const float TrainBoardReach = 2.6f;

        /// <summary>The wagon the body is parented to while aboard, null on foot.</summary>
        internal Transform Frame => _trainFrame;

        /// <summary>The scene's train view, once a train lookup has found it (null before).</summary>
        internal TrainView View => _trainView;

        /// <summary>Keeps the body parented to the wagon named by the server (<see cref="GameBootstrap.InTrain"/>): the first
        /// time a frame appears the body is brought into the wagon's box and parented to it; when the server takes the
        /// frame away the body is released. Returns true while aboard.</summary>
        internal bool UpdateTrainFrame()
        {
            string frame = Player.Game != null ? Player.Game.InTrain : string.Empty;
            if (_leavingFrame.Length > 0)
            {
                if (frame == _leavingFrame)
                {
                    // #2129: we walked off on our side; until the server confirms, the frame it still names must not
                    // pull us back into the wagon.
                    if (_trainFrame != null)
                    {
                        LeaveTrainFrame();
                    }

                    return false;
                }

                _leavingFrame = string.Empty;
            }

            if (string.IsNullOrEmpty(frame))
            {
                if (_trainFrame != null)
                {
                    LeaveTrainFrame();
                }

                return false;
            }

            if (_trainView == null)
            {
                _trainView = Object.FindAnyObjectByType<TrainView>();
            }

            if (_trainView == null || !_trainView.TryGetWagon(frame, out var wagon))
            {
                return _trainFrame != null; // the list has not caught up: keep riding the last known wagon
            }

            if (_trainFrame != wagon || _trainFrameId != frame)
            {
                _trainFrame = wagon;
                _trainFrameId = frame;
                Player.ForgetSeat();
                transform.SetParent(wagon, true);
                var local = wagon.InverseTransformPoint(transform.position);
                var clamped = RailRules.ClampLocal(new Vector3f(local.x, local.y, local.z));
                bool wasEnabled = Player.Body.enabled;
                Player.Body.enabled = false;
                transform.position = wagon.TransformPoint(new Vector3(clamped.X, clamped.Y + 0.05f, clamped.Z));
                Player.Body.enabled = wasEnabled;
                Player.VerticalVelocity = 0f;
            }

            if (Player.Game.TrainSeat < 0 && !Player.Body.enabled)
            {
                Player.Body.enabled = true; // stood up from a seat
                Player.Avatar?.SetSeated(false);
            }

            return true;
        }

        private void LeaveTrainFrame()
        {
            transform.SetParent(null, true);
            _trainFrame = null;
            _trainFrameId = string.Empty;
            Player.Body.enabled = true;
            Player.Avatar?.SetSeated(false);
            Player.ApplyCameraMode();
        }

        /// <summary>A seated rider is parked on the seat of the wagon, controller off (the seat is the frame's fixed point).</summary>
        internal void HoldTrainSeat()
        {
            if (_trainFrame == null || !RailRules.TryParseFrame(Player.Game.InTrain, out _, out int wagonIndex))
            {
                return;
            }

            var train = Player.Game.RiddenTrain;
            string item = train != null && train.Wagons != null && wagonIndex < train.Wagons.Length ? train.Wagons[wagonIndex] : string.Empty;
            var seats = RailRules.SeatOffsets(item);
            if (Player.Game.TrainSeat >= seats.Count)
            {
                return;
            }

            var seat = seats[Player.Game.TrainSeat];
            Player.Body.enabled = false;
            Player.VerticalVelocity = 0f;
            transform.position = _trainFrame.TransformPoint(new Vector3(seat.X, seat.Y, seat.Z));
            Player.Avatar?.SetSeated(true);
        }

        /// <summary>E aboard: in the cab the panel opens; beside a seat you sit down. Returns true when it did something.</summary>
        internal bool TrainInteract()
        {
            if (_trainFrame == null || !RailRules.TryParseFrame(Player.Game.InTrain, out string trainId, out int wagonIndex))
            {
                return false;
            }

            var train = Player.Game.RiddenTrain;
            string item = train != null && train.Wagons != null && wagonIndex < train.Wagons.Length ? train.Wagons[wagonIndex] : string.Empty;
            var seats = RailRules.SeatOffsets(item);
            var local = _trainFrame.InverseTransformPoint(transform.position);
            int best = -1;
            float bestD = 1.3f;
            for (int i = 0; i < seats.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(local.x, local.z), new Vector2(seats[i].X, seats[i].Z));
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            if (best >= 0 && item != RailRules.CabItemKey)
            {
                Player.Game.Network?.SendEnterTrain(trainId, wagonIndex, best);
                return true;
            }

            if (wagonIndex == 0)
            {
                TrainCabUi.Instance?.Open(trainId);
                return true;
            }

            return false;
        }

        /// <summary>On foot: E beside a wagon boards it (your own train or an ally's — the server decides).</summary>
        internal bool TryBoardNearbyTrain()
        {
            if (Player.Game?.Network == null || Player.Game.Trains == null || Player.Game.Trains.Length == 0)
            {
                return false;
            }

            if (_trainView == null)
            {
                _trainView = Object.FindAnyObjectByType<TrainView>();
            }

            if (_trainView == null)
            {
                return false;
            }

            var t = _trainView.NearestWagon(transform.position, out int wagon, out float dist);
            if (t == null || dist > TrainBoardReach)
            {
                return false;
            }

            Player.Game.Network.SendEnterTrain(t.Id, wagon, -1);
            return true;
        }

        /// <summary>The wagon box (a frame id) the feet were in last frame, empty for none — the edge trigger of
        /// <see cref="StepAboardTrain"/>.</summary>
        private string _wagonBox = string.Empty;

        /// <summary>#2129: the frame we walked off on our side, until the server's confirmation clears it.</summary>
        private string _leavingFrame = string.Empty;

        /// <summary>#2129: the neighbouring wagon we asked the server to move our frame to, and when.</summary>
        private string _crossingTo = string.Empty;
        private float _crossingSentAt;

        /// <summary>#2129: stepping into a wagon — through a door, the open end, or a jump onto a moving train — boards it,
        /// no E needed; before, the floor simply slid away under a player who was never a rider. Edge-triggered per wagon:
        /// one request per entry into a wagon's box, so a refused one (someone else's train) is not repeated every frame,
        /// and a rider set down beside the train or walking out at a stop has left the box before the next entry counts.</summary>
        internal void StepAboardTrain()
        {
            if (_trainFrame != null || !string.IsNullOrEmpty(Player.Game?.InTrain))
            {
                _wagonBox = Player.Game?.InTrain ?? string.Empty; // aboard: leaving this wagon's box re-arms the trigger
                return;
            }

            if (Player.Game?.Network == null || Player.Game.Trains == null || Player.Game.Trains.Length == 0)
            {
                _wagonBox = string.Empty;
                return;
            }

            if (_trainView == null)
            {
                _trainView = Object.FindAnyObjectByType<TrainView>();
            }

            int wagon = 0;
            var t = _trainView != null ? _trainView.WagonAt(transform.position, out wagon) : null;
            string box = t == null ? string.Empty : RailRules.FrameId(t.Id, wagon);
            if (box.Length > 0 && box != _wagonBox)
            {
                Player.Game.Network.SendEnterTrain(t.Id, wagon, -1);
            }

            _wagonBox = box;
        }

        /// <summary>#2129: the rider has left their wagon's box through an open end. Still inside the train — in the next
        /// wagon, or in the gap between two coupled wagons — the server is asked to move the frame to the wagon the feet
        /// are in (re-sent at most twice a second) and the caller keeps its reports inside the old wagon; true. Outside the
        /// train: false.</summary>
        private bool CrossToNeighbourWagon()
        {
            if (_trainView == null || !RailRules.TryParseFrame(Player.Game.InTrain, out string trainId, out int current))
            {
                return false;
            }

            var t = _trainView.WagonAt(transform.position, out int wagon);
            if (t == null || t.Id != trainId || wagon == current)
            {
                // Not inside another wagon of this train: in the gap between two wagons we hold, beyond the train we leave.
                var gap = _trainView.WagonAt(transform.position, out _, RailRules.WagonGap);
                return gap != null && gap.Id == trainId;
            }

            string frame = RailRules.FrameId(trainId, wagon);
            if (frame != _crossingTo || Time.time - _crossingSentAt > 0.5f)
            {
                _crossingTo = frame;
                _crossingSentAt = Time.time;
                Player.Game.Network.SendEnterTrain(trainId, wagon, -1);
            }

            return true;
        }

        /// <summary>#2113: the position report while aboard — the wagon-local pose in the train's frame, the crossing into
        /// the neighbouring wagon (#2129) and the walk-off. Returns true when it sent the report (the caller sends nothing
        /// else this time), false when the body is not riding a wagon the server names.</summary>
        internal bool TrySendFramedMove(float yaw, float pitch)
        {
            if (_trainFrame != null && !string.IsNullOrEmpty(Player.Game.InTrain))
            {
                // #2113: aboard — the pose is the wagon-local offset in the train's frame. Out of the box = we walked off.
                var local = _trainFrame.InverseTransformPoint(transform.position);
                var l = new Vector3f(local.x, local.y, local.z);
                if (RailRules.OutsideWagon(l))
                {
                    // #2129: through the open end into the next wagon of the same train is not leaving — the frame moves
                    // there. Meanwhile the report stays inside the old wagon's box (the raw spot out here would read as
                    // walking off), and keeps flowing: the server drops a client silent for 90 s (#964).
                    if (CrossToNeighbourWagon())
                    {
                        Player.Game.Network.SendFramedMove(Player.Game.InTrain, RailRules.ClampLocal(l), yaw, pitch);
                        return true;
                    }

                    // Walked off: report the spot in the frame, so the server sets us down where we stepped off (an
                    // ExitTrain would put us beside the wagon instead), and stop riding on our side at once.
                    Player.Game.Network.SendFramedMove(Player.Game.InTrain, l, yaw, pitch);
                    _leavingFrame = Player.Game.InTrain;
                    LeaveTrainFrame();
                    return true;
                }

                Player.Game.Network.SendFramedMove(Player.Game.InTrain, l, yaw, pitch);
                return true;
            }

            return false;
        }
    }
}
