// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3095 ("a robot that's left alone might look at its hands sometimes"): a Guardian robot (and the hunter) standing
    /// about alone fidgets now and then like the animals do (<c>CreatureAnimator</c>'s flourishes) — it brings its claws up
    /// and bends its head to look at them, rolls its shoulders, or scans far round. Picked and timed by
    /// <see cref="RobotIdle"/> (Client.Core): only after it has stood still a while, never while it walks, hunts, strikes
    /// or flinches, and a fidget that is caught by any of those fades out instead of snapping. Composed on top of the pose
    /// <see cref="Animate"/> just wrote; on the world clock, so the pause menu holds it like everything else here.
    /// </summary>
    public sealed partial class WorldEntities
    {
        private void ApplyRobotIdle(Entry en, bool idle)
        {
            if (en.Idle == null)
            {
                return;
            }

            var pose = en.Idle.Step(Game.WorldDeltaTime, idle);
            if (pose.IsRest)
            {
                return;
            }

            en.ArmL.localRotation *= Quaternion.Euler(pose.ArmPitchL, pose.ArmTwistL, pose.ArmRollL);
            en.ArmR.localRotation *= Quaternion.Euler(pose.ArmPitchR, pose.ArmTwistR, pose.ArmRollR);
            en.Body.localRotation *= Quaternion.Euler(0f, 0f, pose.BodyRoll);
            en.Head.localRotation *= Quaternion.Euler(pose.HeadPitch, pose.HeadYaw, 0f);
        }
    }
}
