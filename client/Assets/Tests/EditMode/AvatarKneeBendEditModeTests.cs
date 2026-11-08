// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #3412 (found with Justus' "the forearms still bend backwards", #3401): the knees had the mirror bug — the poses give a
    /// positive bend, the joint turned it into a forward fold, so seated shins pointed up and the squat knelt. Measured on
    /// the real rig: in the thigh's own frame a bent knee puts the foot BEHIND the knee (−z), never in front of it.
    /// </summary>
    public sealed class AvatarKneeBendEditModeTests
    {
        /// <summary>The knee pivot sits at z 0 in the thigh frame; the foot box's centre is 0.06 forward at rest (the toes).</summary>
        private const float RestToe = 0.06f;

        [Test]
        public void Seated_TheShinsHangDown_TheFeetBelowTheKnees()
        {
            WithAvatar(avatar =>
            {
                avatar.SetSeated(true);
                Pose(avatar);
                var (knee, foot) = KneeAndFoot(avatar, "LegLeft");
                Assert.That(foot.y, Is.LessThan(knee.y - 0.3f), "a seated foot hangs below its knee, not up in the air");
                AssertFootBehindKnee(avatar, "LegLeft");
                AssertFootBehindKnee(avatar, "LegRight");
            });
        }

        [Test]
        public void Crouched_TheKneesBendBackward_TheFeetStayUnderTheBody()
        {
            WithAvatar(avatar =>
            {
                avatar.SetCrouched(true);
                Pose(avatar);
                AssertFootBehindKnee(avatar, "LegLeft");
                AssertFootBehindKnee(avatar, "LegRight");

                // The caller lowers the root by CrouchDrop: the lowest foot then rests near the standing feet's level.
                var root = avatar.transform;
                float lowest = Mathf.Min(KneeAndFoot(avatar, "LegLeft").Foot.y, KneeAndFoot(avatar, "LegRight").Foot.y)
                               - root.position.y - PlayerAvatar.CrouchDrop;
                // Standing, the foot boxes' centres sit ~0.02 above the root; squatting they should stay close to that.
                Assert.That(lowest, Is.InRange(-0.1f, 0.12f), "squatting feet neither float nor sink into the floor");
            });
        }

        [Test]
        public void Walking_NoKneeEverFoldsForward()
        {
            WithAvatar(avatar =>
            {
                for (int step = 0; step < 40; step++)
                {
                    avatar.transform.position += new Vector3(0f, 0f, 0.12f); // ~2.4 blocks/s: a walk
                    avatar.AnimateForTest(0.05f);
                    AssertFootBehindKnee(avatar, "LegLeft");
                    AssertFootBehindKnee(avatar, "LegRight");
                }
            });
        }

        private static void Pose(PlayerAvatar avatar)
        {
            for (int i = 0; i < 30; i++)
            {
                avatar.AnimateForTest(0.05f); // eases the crouch blend fully in
            }
        }

        /// <summary>In the thigh's frame the foot's centre is never ahead of the knee by more than the toes at rest.</summary>
        private static void AssertFootBehindKnee(PlayerAvatar avatar, string leg)
        {
            var hip = Find(avatar, leg);
            var foot = Find(avatar, leg + "Foot");
            var knee = Find(avatar, leg + "Knee");
            float footZ = hip.InverseTransformPoint(foot.position).z;
            float kneeZ = hip.InverseTransformPoint(knee.position).z;
            Assert.That(footZ, Is.LessThanOrEqualTo(kneeZ + RestToe + 0.01f), $"{leg}: the shin folds forward past the knee");
        }

        private static (Vector3 Knee, Vector3 Foot) KneeAndFoot(PlayerAvatar avatar, string leg)
            => (Find(avatar, leg + "Knee").position, Find(avatar, leg + "Foot").position);

        private static Transform Find(PlayerAvatar avatar, string name)
        {
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            Assert.Fail($"the avatar has no part named {name}");
            return null;
        }

        private static void WithAvatar(System.Action<PlayerAvatar> body)
        {
            LogAssert.ignoreFailingMessages = true; // the part builder calls Object.Destroy, which logs outside play mode
            var go = new GameObject("AvatarKneeTest");
            try
            {
                var avatar = go.AddComponent<PlayerAvatar>();
                avatar.Build(new Color(0.8f, 0.6f, 0.5f), Color.blue, Color.blue, Color.gray, spacesuit: true);
                body(avatar);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
