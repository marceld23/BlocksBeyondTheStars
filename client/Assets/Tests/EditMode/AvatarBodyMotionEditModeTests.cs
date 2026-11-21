// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.Feel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #3882–#3886 on the real rig: the parts hang on the body pivots, the walking body dips and sways while its feet keep
    /// to the floor and stay level in the stance, the joints are there, a seated figure's body is still, a machine moves
    /// stiffer. The curves themselves are tested headless (<c>HumanGaitTests</c>).
    /// </summary>
    public sealed class AvatarBodyMotionEditModeTests
    {
        [Test]
        public void ThePartsHangOnTheirPivots_AndTheJointsAreThere()
        {
            WithAvatar(avatar =>
            {
                Assert.That(Find(avatar, "Head").parent.name, Is.EqualTo("UpperBody"));
                Assert.That(Find(avatar, "Chest").parent.name, Is.EqualTo("UpperBody"));
                Assert.That(Find(avatar, "ArmLeft").parent.name, Is.EqualTo("UpperBody"));
                Assert.That(Find(avatar, "SuitPack").parent.name, Is.EqualTo("UpperBody"));
                Assert.That(Find(avatar, "Pelvis").parent.name, Is.EqualTo("Hips"));
                Assert.That(Find(avatar, "LegLeft").parent.name, Is.EqualTo("Hips"));
                Assert.That(Find(avatar, "UpperBody").parent.name, Is.EqualTo("Hips"));
                Assert.That(Find(avatar, "LegLeftFoot").parent.name, Is.EqualTo("LegLeftAnkle"));
                Assert.That(Find(avatar, "LegLeftKneeJoint").parent.name, Is.EqualTo("LegLeftKnee"));
                Assert.That(Find(avatar, "ArmRightElbowJoint").parent.name, Is.EqualTo("ArmRightElbow"));

                // At rest the origin pivots change nothing: every part is where it was built.
                avatar.AnimateForTest(0.05f);
                Assert.That(Vector3.Distance(Find(avatar, "Head").position, avatar.transform.TransformPoint(0f, 1.86f, 0f)), Is.LessThan(0.03f));
            });
        }

        [Test]
        public void Walking_TheBodyDipsAndSways_TheStandingFootStaysLevel()
        {
            WithAvatar(avatar =>
            {
                float lowest = 0f, widest = 0f, flattest = 90f, steepest = 0f;
                for (int step = 0; step < 120; step++)
                {
                    avatar.transform.position += new Vector3(0f, 0f, 0.12f); // ~2.4 blocks/s
                    avatar.AnimateForTest(0.05f);
                    if (step < 40)
                    {
                        continue; // the walk blends in first
                    }

                    // Measured on the pelvis, not on the origin pivot: a pivot's own position also carries the offset of
                    // turning about the hip point.
                    var pelvis = PelvisAt(avatar);
                    lowest = Mathf.Min(lowest, pelvis.y - PelvisY);
                    widest = Mathf.Max(widest, Mathf.Abs(pelvis.x));
                    float pitch = FootPitch(avatar, "LegLeft");
                    flattest = Mathf.Min(flattest, Mathf.Abs(pitch));
                    steepest = Mathf.Max(steepest, Mathf.Abs(pitch));
                }

                Assert.That(lowest, Is.LessThan(-0.01f), "the body dips at double support");
                Assert.That(widest, Is.GreaterThan(0.005f), "the body sways over the standing foot");
                Assert.That(flattest, Is.LessThan(3f), "the standing foot is held flat");
                // At this pace the shin tips up to ~70° at toe-off; a foot fixed to it tipped as far. On its ankle it stays
                // within the push-off and the hang of the swing.
                Assert.That(steepest, Is.LessThan(45f), "the foot rolls off instead of tipping with the shin");
            });
        }

        [Test]
        public void Seated_TheBodyIsStill()
        {
            WithAvatar(avatar =>
            {
                avatar.SetSeated(true);
                for (int i = 0; i < 40; i++)
                {
                    avatar.AnimateForTest(0.05f);
                }

                var hips = Find(avatar, "Hips");
                Assert.That(hips.localPosition.magnitude, Is.LessThan(0.002f));
                Assert.That(Quaternion.Angle(hips.localRotation, Quaternion.identity), Is.LessThan(0.5f));
            });
        }

        [Test]
        public void AMachine_MovesStifferThanAVillager()
        {
            float villager = WalkingSway(machine: false);
            float machine = WalkingSway(machine: true);
            Assert.That(machine, Is.LessThan(villager * 0.75f), "stiffer than a villager");
            Assert.That(machine, Is.GreaterThan(villager * 0.25f), "but no longer rigid");
        }

        private static float WalkingSway(bool machine)
        {
            float widest = 0f;
            WithAvatar(avatar =>
            {
                avatar.SetMachine(machine);
                Assert.That(avatar.Liveliness, Is.EqualTo(machine ? HumanGait.MachineLiveliness : 1f));
                for (int step = 0; step < 120; step++)
                {
                    avatar.transform.position += new Vector3(0f, 0f, 0.06f); // ~1.2 blocks/s
                    avatar.AnimateForTest(0.05f);
                    if (step >= 40)
                    {
                        widest = Mathf.Max(widest, Mathf.Abs(PelvisAt(avatar).x));
                    }
                }
            });
            return widest;
        }

        /// <summary>The pelvis box's centre at rest, root frame (<c>AvatarArmourFit.Pelvis</c>).</summary>
        private const float PelvisY = 0.97f;

        /// <summary>Where the pelvis is now, in the figure's root frame.</summary>
        private static Vector3 PelvisAt(PlayerAvatar avatar) => avatar.transform.InverseTransformPoint(Find(avatar, "Pelvis").position);

        /// <summary>The foot's pitch against the figure's root, degrees.</summary>
        private static float FootPitch(PlayerAvatar avatar, string leg)
        {
            var foot = Find(avatar, leg + "Foot");
            var forward = avatar.transform.InverseTransformDirection(foot.forward);
            return Mathf.Atan2(-forward.y, forward.z) * Mathf.Rad2Deg;
        }

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
            var go = new GameObject("AvatarBodyMotionTest");
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
