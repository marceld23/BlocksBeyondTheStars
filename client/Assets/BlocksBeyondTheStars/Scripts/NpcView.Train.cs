// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3445: NPC passengers on the monorail. A snapshot with a train frame (<see cref="NetNpc.FrameId"/>) is drawn in the
    /// wagon as <see cref="TrainView"/> draws it this frame — <c>wagon.TransformPoint(local)</c> with the wagon's rotation
    /// and the passenger's facing in it — so it sits still on its seat however the wagon moves (the interpolation buffer
    /// would trail the wagon by its delay). The rules are Unity-free in <see cref="NpcTrainRide"/>. An NPC whose wagon this
    /// client does not draw (yet) falls back to its world position.
    /// </summary>
    public sealed partial class NpcView
    {
        /// <summary>The trains this client draws (set by <c>WorldRig</c>, like <see cref="RemotePlayers.Trains"/>).</summary>
        public TrainView Trains;

        /// <summary>Takes a snapshot's ride: a changed frame (boarded, stepped off, moved wagons) snaps the drawing to the
        /// snapshot instead of easing toward it.</summary>
        private void ApplyRide(Npc n, NetNpc nd, double at, Vector3f snapPos, Vector3f snapVel, bool stamped)
        {
            string frame = nd.FrameId ?? string.Empty;
            if (NpcTrainRide.FrameChanged(n.Frame, frame))
            {
                n.Interp.Reset(at, snapPos, snapVel, stamped, nd.Facing, 0);
                n.SettledWorld = n.Target;
                n.Yaw = n.TargetYaw;
            }

            n.Frame = frame;
            n.RideLocal = new Vector3(nd.LocalX, nd.LocalY, nd.LocalZ);
            n.RideYaw = nd.LocalYaw;
        }

        /// <summary>Draws a passenger in its wagon this frame. False when it is on foot or its wagon is not drawn here.</summary>
        private bool PlaceAboard(Npc n)
        {
            if (!NpcTrainRide.Aboard(n.Frame) || Trains == null || !Trains.TryGetWagon(n.Frame, out var wagon))
            {
                return false;
            }

            var at = wagon.TransformPoint(n.RideLocal);
            n.Yaw = NpcTrainRide.DrawnYawDegrees(wagon.rotation.eulerAngles.y, n.RideYaw);
            if (n.Pose == 2)
            {
                // #3473: asleep in a sleeper's berth, laid out like a villager in bed — the berth's middle is the ride point and
                // the facing points from its foot to its head, so the feet go half a body toward the foot, the root lies on
                // its back on the mattress.
                var headward = Quaternion.Euler(0f, n.Yaw, 0f) * Vector3.forward;
                n.Go.transform.position = at - (headward * (0.9f * n.Go.transform.localScale.y)) + (Vector3.up * RailWagonLayout.BerthTop);
                n.Go.transform.rotation = Quaternion.Euler(-90f, n.Yaw + 180f, 0f);
                n.SettledWorld = n.Target;
                return true;
            }

            if (n.Pose == 1)
            {
                at.y -= NpcTrainRide.SeatDrop; // the pelvis on the seat, like a seated remote player
            }

            n.Go.transform.position = at;
            n.Go.transform.rotation = Quaternion.Euler(0f, n.Yaw, 0f);
            n.SettledWorld = n.Target; // the server's world position (the bubble and the voice read it)
            return true;
        }
    }
}
