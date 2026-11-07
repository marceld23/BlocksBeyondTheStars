// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3398/#3399 — the hunter robot's gear on its body (<see cref="CreatureBuilder"/>'s robot rig): the weapon it was given in
    /// its right tool hand (the same held model the player sees, <see cref="HeldItem.Build"/>; a pair of fight gloves on both
    /// hands), a small jetpack on its back behind the reactor, and the jetpack's flame while it flies (the wire's
    /// <see cref="NetCreature.GearFlags"/>). Attached to the built rig and kept in step per frame — cheap: only a robot with gear
    /// does anything, and only when what it carries changes. A rebuilt body (the rig's children go) is noticed and re-dressed.
    /// </summary>
    public sealed partial class CreatureView
    {
        /// <summary>What a robot's body shows of its gear right now.</summary>
        private sealed class RobotGearLook
        {
            public string Weapon = string.Empty; // the item key drawn in its hand
            public GameObject Held;              // that held model (under the right elbow)
            public GameObject HeldLeft;          // the left glove of a pair of fight gloves
            public GameObject Pack;              // the jetpack on its back
            public ParticleSystem Flame;         // the jetpack's flame
        }

        /// <summary>The flame's colour — the warm orange of a player's jetpack.</summary>
        private static readonly Color RobotFlameColor = new Color(1f, 0.65f, 0.25f);

        /// <summary>Keeps a robot's gear on its body in step with the wire.</summary>
        private void UpdateRobotGear(Entry entry, NetCreature c)
        {
            if (c.BodyPlan != "Robot" || entry.Root == null)
            {
                return;
            }

            if (entry.Gear == null && string.IsNullOrEmpty(c.GearWeapon) && c.GearFlags == 0)
            {
                return; // a robot without gear: nothing to dress
            }

            var look = entry.Gear ??= new RobotGearLook();
            UpdateRobotWeapon(entry, look, c.GearWeapon ?? string.Empty);
            UpdateRobotJetpack(entry, look, RobotGearView.WearsJetpack(c));
            bool flying = RobotGearView.Flying(c);
            WeaponFx.SetThrust(look.Flame, flying);
            if (flying)
            {
                entry.Animator?.SetMotion(c.Motion, true, false); // legs tucked like mid-jump while it flies
            }
        }

        /// <summary>The weapon in the right hand: rebuilt when it changes or when the body was rebuilt under it.</summary>
        private void UpdateRobotWeapon(Entry entry, RobotGearLook look, string weapon)
        {
            bool built = look.Held != null || look.HeldLeft != null;
            if (weapon == look.Weapon && (built || weapon.Length == 0))
            {
                return;
            }

            if (look.Held != null)
            {
                Destroy(look.Held);
            }

            if (look.HeldLeft != null)
            {
                Destroy(look.HeldLeft);
            }

            look.Held = look.HeldLeft = null;
            look.Weapon = weapon;
            if (weapon.Length == 0 || Game?.Content == null)
            {
                return;
            }

            var right = RobotHand(entry.Root, "ArmR");
            if (right == null)
            {
                return;
            }

            var (kind, tint, blockKey) = HeldItem.For(Game.Content, weapon);
            if (kind == HeldItem.Kind.Gloves)
            {
                look.Held = HeldItem.BuildAvatarGlove(right, weapon, null, left: false);
                var left = RobotHand(entry.Root, "ArmL");
                look.HeldLeft = left != null ? HeldItem.BuildAvatarGlove(left, weapon, null, left: true) : null;
                return;
            }

            look.Held = HeldItem.Build(right, kind, tint, blockKey, weapon);
            if (look.Held != null)
            {
                look.Held.transform.localPosition += new Vector3(0f, -0.06f, 0.06f); // in the round hand, pointing forward
            }
        }

        /// <summary>An anchor at the robot's tool hand on one arm ("ArmL" / "ArmR"): under the elbow pivot, so the held model
        /// swings with the forearm, at the hand's place and unscaled (the hand itself is a scaled sphere).</summary>
        private static Transform RobotHand(GameObject root, string arm)
        {
            var elbow = root.transform.Find("BodyRig/" + arm + "/Elbow");
            var hand = elbow != null ? elbow.Find("ToolHand") : null;
            if (hand == null)
            {
                return null;
            }

            var anchor = elbow.Find("GearHand");
            if (anchor == null)
            {
                anchor = new GameObject("GearHand").transform;
                anchor.SetParent(elbow, false);
                anchor.localPosition = hand.localPosition;
            }

            return anchor;
        }

        /// <summary>The jetpack on the back, behind the reactor, sized to the rig (the reactor is 0.3 hunter units wide), with
        /// its flame under the nozzles.</summary>
        private void UpdateRobotJetpack(Entry entry, RobotGearLook look, bool wears)
        {
            if (!wears)
            {
                if (look.Pack != null)
                {
                    Destroy(look.Pack); // the flame is a child of it
                }

                look.Pack = null;
                look.Flame = null;
                return;
            }

            if (look.Pack != null)
            {
                return;
            }

            var reactor = entry.Root.transform.Find("BodyRig/Reactor");
            if (reactor == null)
            {
                return;
            }

            float k = reactor.localScale.x / 0.3f;
            var pack = new GameObject("GearJetpack").transform;
            pack.SetParent(reactor.parent, false);
            pack.localPosition = reactor.localPosition + new Vector3(0f, 0f, -0.14f * k);
            HeldItem.SolidPart(pack, Vector3.zero, new Vector3(0.42f, 0.5f, 0.16f) * k, new Color(0.33f, 0.35f, 0.4f));
            HeldItem.SolidPart(pack, new Vector3(-0.12f * k, -0.3f * k, 0f), new Vector3(0.1f, 0.12f, 0.1f) * k, new Color(0.2f, 0.21f, 0.24f));
            HeldItem.SolidPart(pack, new Vector3(0.12f * k, -0.3f * k, 0f), new Vector3(0.1f, 0.12f, 0.1f) * k, new Color(0.2f, 0.21f, 0.24f));
            HeldItem.GlowPart(pack, new Vector3(0f, 0.12f * k, -0.085f * k), new Vector3(0.24f, 0.04f, 0.01f) * k, new Color(0.5f, 0.91f, 1f));
            look.Pack = pack.gameObject;
            look.Flame = Fx != null ? Fx.CreateThrustFlame(pack, new Vector3(0f, -0.4f * k, 0f), RobotFlameColor) : null;
        }
    }
}
