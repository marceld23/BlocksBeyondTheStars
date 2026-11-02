// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3094 ("the robbers don't look like all the other people in the game — keep their mask, but generate the hair like
    /// the villagers', just no beard"): a robber (both kinds, <c>Bandit</c> and <c>BanditGunner</c>) is built on the human rig
    /// every villager and player uses (<see cref="PlayerAvatar"/>) — the same head, face and jointed limbs, its hair by the
    /// villagers' rule (<see cref="NpcLooks.HairToneFor"/>: seven tones, now and then bald), seeded by its id and name so it
    /// looks the same every time it comes into view. No beard (<see cref="LowerFace.Bare"/>); the cloth mask over nose and
    /// mouth (<see cref="PlayerAvatar.SetRobberMask"/>) is still what tells a robber from a villager. It keeps its scruffy
    /// jacket, skin and mask tones, its dark trousers and its cold-blue energy weapon — never red on the head, never the
    /// Guardians' glow (#601). It walks on its own (the rig reads its movement), aims or brandishes while it fights
    /// (<see cref="PlayerAvatar.SetStance"/>), swings its blade or kicks with the shot (<see cref="PlayerAvatar.Swing"/>,
    /// <see cref="PlayerAvatar.Recoil"/>), and a lean pivot under it carries the swipe's lunge and the flinch of a hit.
    /// </summary>
    public sealed partial class WorldEntities
    {
        /// <summary>Height above a robber's feet of the top of its head (hair included) — the daze stars and the health bar
        /// sit just above it.</summary>
        private const float BanditHeadTop = 2.2f;

        /// <summary>The bandits' weapon/tracer glow: cold blue human tech, kept clearly apart from the Guardian
        /// machines' red sensor glow (<see cref="_eyeMat"/>).</summary>
        private static readonly Color BanditEnergyColor = new(0.30f, 0.68f, 1f);

        /// <summary>A robber's dark trousers (and the belt line of the pelvis).</summary>
        private static readonly Color BanditTrousers = new(0.14f, 0.13f, 0.14f);

        /// <summary>The robber's human figure: the rig and the lean pivot it stands on.</summary>
        private sealed class BanditRig
        {
            public BanditRig(PlayerAvatar avatar, Transform lean)
            {
                Avatar = avatar;
                Lean = lean;
            }

            public PlayerAvatar Avatar { get; }

            public Transform Lean { get; }

            /// <summary>The attack window last played (a new <c>AttackUntil</c> = a new blow or shot).</summary>
            public float AttackSeen { get; set; } = -1f;
        }

        // The gunner's snub blaster and the blade robber's energy blade, as held models (#3094: held in the rig's hand like any
        // tool). Both glow cold blue (#4dadff = BanditEnergyColor); the blaster's foremost box is its muzzle.
        private static readonly List<HeldModelPart> BanditGunLook = new List<HeldModelPart>
        {
            Box(0f, 0f, 0.10f, 0.09f, 0.10f, 0.30f, "#242124", HeldMaterials.DarkMetal),      // body
            Box(0f, 0.01f, 0.27f, 0.05f, 0.05f, 0.06f, "#3a3d44", HeldMaterials.Metal),       // barrel
            Box(0f, 0.01f, 0.32f, 0.06f, 0.06f, 0.05f, "#4dadff", HeldMaterials.Energy, true), // the glowing muzzle
            Box(0.05f, 0.02f, 0.08f, 0.012f, 0.03f, 0.14f, "#4dadff", HeldMaterials.Energy, true), // the power cell's glow
            Box(0f, -0.13f, -0.04f, 0.08f, 0.18f, 0.10f, "#242124", HeldMaterials.Rubber),    // grip
        };

        private static readonly List<HeldModelPart> BanditBladeLook = new List<HeldModelPart>
        {
            Box(0f, -0.04f, 0f, 0.06f, 0.06f, 0.16f, "#242124", HeldMaterials.Leather),       // hilt
            Box(0f, -0.01f, 0.095f, 0.05f, 0.12f, 0.03f, "#5a5e66", HeldMaterials.Metal),     // guard
            Box(0f, 0f, 0.30f, 0.03f, 0.07f, 0.38f, "#4dadff", HeldMaterials.Energy, true),    // the energy blade
        };

        private static HeldModelPart Box(float x, float y, float z, float w, float h, float d, string color, string material,
            bool glow = false)
            => new HeldModelPart { P = new[] { x, y, z }, S = new[] { w, h, d }, C = color, G = glow, M = material };

        /// <summary>Builds a robber on the human rig (see the type summary). <paramref name="name"/> joins the id in the face
        /// seed (null for the Codex portrait, whose fixed id keeps its look).</summary>
        private static Entry BuildBandit(string id, string name, Transform parent, bool gunner)
        {
            int h = Hash(id);
            float size = 0.95f + ((h & 0xFF) / 255f) * 0.1f; // people vary a little, not like fauna
            var en = new Entry
            {
                Seed = (h & 0x3ff) * 0.137f,
                Pitch = 0.9f + ((h >> 5) % 17) / 17f * 0.25f,
                IsBandit = true,
                IsGunner = gunner,
            };

            var root = new GameObject(gunner ? "BanditGunner" : "Bandit");
            root.transform.SetParent(parent, true);
            root.transform.localScale = Vector3.one * size;
            en.Root = root;

            // The lean pivot at the feet tips the whole figure for the lunge and the flinch; the rig poses its own limbs.
            var lean = Pivot(root.transform, Vector3.zero);
            var figure = new GameObject("Robber");
            figure.transform.SetParent(lean, false);
            var avatar = figure.AddComponent<PlayerAvatar>();

            // Jacket, skin and mask tones vary per robber as before (muted, scruffy — these are people, not a species); the
            // hair and the face's little differences come from the villagers' rule, seeded by the id and the name.
            int seed = NpcLooks.RobberFaceSeed(id, name);
            int tone = NpcLooks.HairToneFor(seed, robot: false);
            Color? hair = null;
            if (tone >= 0)
            {
                var (r, g, b) = NpcLooks.HairTone(tone);
                hair = new Color(r, g, b);
            }

            var jacket = BanditColor(BanditPalette.Jacket, (h >> 12) % 4);
            avatar.Build(BanditColor(BanditPalette.Skin, (h >> 16) % 5), jacket, jacket, BanditTrousers, spacesuit: false,
                variantSeed: seed, hair: hair, lowerFace: LowerFace.Bare);
            avatar.SetRobberMask(BanditColor(BanditPalette.Mask, (h >> 8) % 4));
            avatar.NeverAirborne = true; // a robber never jumps — a step up eased over a moment is not a leap (#2667)
            avatar.SetHeldItem(gunner ? HeldItem.Kind.Gun : HeldItem.Kind.Blade, BanditEnergyColor,
                look: gunner ? BanditGunLook : BanditBladeLook);

            en.Bandit = new BanditRig(avatar, lean);
            // The machines' limb fields are not used for a robber; they point at the lean pivot so nothing can trip on null.
            en.Body = en.Head = en.ArmL = en.ArmR = en.LegL = en.LegR = lean;
            return en;
        }

        /// <summary>A robber's pose for this frame: the stance while it fights (the gunner aims, the blade robber brandishes),
        /// the eyes on the player, a swing or a recoil when <see cref="Update"/> opens an attack window, and the lean of the
        /// swipe's lunge and of a hit's flinch. Its walk and idle are the rig's own.</summary>
        private void AnimateBandit(Entry en, bool hostile)
        {
            var rig = en.Bandit;
            var avatar = rig.Avatar;
            if (avatar == null)
            {
                return;
            }

            bool fighting = hostile && !Game.Aboard; // a player safe in the ship is not stalked (see Update)
            avatar.SetStance(!fighting ? PlayerAvatar.Stance.None
                : en.IsGunner ? PlayerAvatar.Stance.Aim : PlayerAvatar.Stance.Brandish);
            avatar.SetLookYaw(fighting ? 0f : float.NaN);

            if (Game.WorldTime < en.AttackUntil && !Mathf.Approximately(rig.AttackSeen, en.AttackUntil))
            {
                rig.AttackSeen = en.AttackUntil;
                if (en.IsGunner)
                {
                    avatar.Recoil();
                }
                else
                {
                    avatar.Swing();
                }
            }

            float lean = 0f;
            if (!en.IsGunner && Game.WorldTime < en.AttackUntil)
            {
                float k = 1f - ((en.AttackUntil - Game.WorldTime) / 0.35f);
                lean += 8f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // lunge into the swing
            }

            if (Game.WorldTime < en.FlinchUntil)
            {
                lean -= 14f; // recoil back when hit
            }

            rig.Lean.localRotation = Quaternion.Euler(lean, 0f, 0f);
        }

        /// <summary>Where a robber gunner's tracer starts: the muzzle of the blaster in its hand.</summary>
        private static Vector3 BanditMuzzle(Entry en)
        {
            var avatar = en.Bandit?.Avatar;
            if (avatar != null && avatar.TryMuzzle(out var tip))
            {
                return tip;
            }

            return en.Root.transform.position + (Vector3.up * 1.4f);
        }

        /// <summary>Which per-bandit palette a tint comes from (see <see cref="BanditColor"/>).</summary>
        private enum BanditPalette
        {
            Jacket,
            Skin,
            Mask,
        }

        /// <summary>One entry of a per-bandit palette. Muted, scruffy, and — for anything on the head —
        /// deliberately never red: glowing red belongs to the Guardian machines (#601). The hair has the villagers'
        /// palette since #3094 (<see cref="NpcLooks.HairTone"/>).</summary>
        private static Color BanditColor(BanditPalette palette, int variant) => palette switch
        {
            BanditPalette.Jacket => variant switch
            {
                0 => new Color(0.32f, 0.24f, 0.16f), // worn leather brown
                1 => new Color(0.22f, 0.26f, 0.22f), // faded olive
                2 => new Color(0.26f, 0.22f, 0.28f), // dusty plum
                _ => new Color(0.24f, 0.24f, 0.27f), // grey slate
            },
            // Skin: a spread of human tones so a camp isn't one cloned face.
            BanditPalette.Skin => variant switch
            {
                0 => new Color(0.86f, 0.71f, 0.58f), // light
                1 => new Color(0.78f, 0.60f, 0.45f), // tan
                2 => new Color(0.64f, 0.47f, 0.34f), // brown
                3 => new Color(0.48f, 0.34f, 0.24f), // deep brown
                _ => new Color(0.35f, 0.24f, 0.18f), // dark
            },
            // The face mask's cloth — muted scavenged fabric.
            _ => variant switch
            {
                0 => new Color(0.30f, 0.28f, 0.25f), // dusty canvas
                1 => new Color(0.22f, 0.27f, 0.26f), // faded teal-grey
                2 => new Color(0.26f, 0.24f, 0.30f), // washed indigo
                _ => new Color(0.19f, 0.19f, 0.20f), // charcoal
            },
        };
    }
}
