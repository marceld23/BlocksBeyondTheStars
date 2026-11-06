// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The friendly hunter robot (#3295, Justus' idea: "the hunter, but good — where the evil one has red lights it has
    /// light-blue ones"). The look of the Guardian's hunter (<c>WorldEntities.Build(hunter: true)</c>: plated torso, shoulder
    /// plates, one wide visor under a crest, a reactor on the back) laid onto the biped's rig, so <see cref="CreatureAnimator"/>
    /// walks its jointed legs and swings its arms like any biped companion. Three things make it read as a friend at a glance:
    /// every light is light blue (<see cref="RobotLight"/>, #7FE8FF — clearly apart from the robbers' #4dadff and never the
    /// Guardian's red), the forearm blades are gone (rounded tool hands instead), and the horns are round sensor ears. The
    /// overall height follows <see cref="BipedRules.HeightFor"/>, the height the server measures its body by.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The robot's light colour — light blue, #7FE8FF.</summary>
        private static readonly Color RobotLight = new Color(0x7F / 255f, 0xE8 / 255f, 0xFF / 255f);

        /// <summary>The hunter model's own height in its build units (WorldEntities builds it about two units tall).</summary>
        private const float HunterModelHeight = 2f;

        private static Texture2D _robotPlateTex;
        private static bool _robotTexTried;

        /// <summary>The hunter's circuit-plating tile, loaded once (null when absent — the plates are then plain grey).</summary>
        private static Texture2D RobotPlateTexture()
        {
            if (!_robotTexTried)
            {
                _robotTexTried = true;
                _robotPlateTex = ModelKit.LoadTex("enemy_robot");
            }

            return _robotPlateTex;
        }

        /// <summary>Builds the friendly hunter robot: legs, torso, arms and head in the hunter's proportions scaled to the
        /// server's height, lit in light blue, handed to the animator as a two-legged, two-armed rig.</summary>
        private void BuildRobot(GameObject root, NetCreature c)
        {
            float s = Mathf.Clamp(c.Size, 0.3f, 2.5f);
            float headRatio = c.HeadRatio > 0f ? c.HeadRatio : 0.8f;
            float k = BipedRules.HeightFor(s, headRatio) / HunterModelHeight; // hunter units → blocks
            var plateTex = RobotPlateTexture();
            bool tiled = plateTex != null;
            var plate = Lit(tiled ? new Color(0.95f, 0.95f, 0.97f) : new Color(0.55f, 0.58f, 0.64f), plateTex);
            var dark = Lit(tiled ? new Color(0.55f, 0.55f, 0.58f) : new Color(0.24f, 0.25f, 0.29f), plateTex);
            var trim = Lit(new Color(0.62f, 0.66f, 0.72f), null);
            var light = Unlit(RobotLight);
            _bodyMat = plate;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // Legs from the hip (the hunter's hip sits at 0.95 of its two units): thigh, knee plate, shin, a broad foot.
            float legH = 0.95f * k;
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * 0.18f * k;
                _legs.Add(AddRobotLeg(body.transform, side, new Vector3(x, legH, 0f), legH, 0.2f * k, plate, dark, trim));
            }

            // Torso, pelvis and the dark chest plate with a light-blue "heart" — the friendly face of the front.
            float torsoY = legH + 0.32f * k;
            AddPartTo(body.transform, "Torso", new Vector3(0f, torsoY, 0f), new Vector3(0.62f, 0.7f, 0.42f) * k, plate);
            AddPartTo(body.transform, "Pelvis", new Vector3(0f, legH - 0.04f * k, 0f), new Vector3(0.5f, 0.22f, 0.36f) * k, dark);
            AddPartTo(body.transform, "ChestPlate", new Vector3(0f, torsoY + 0.06f * k, 0.215f * k), new Vector3(0.44f, 0.36f, 0.03f) * k, dark);
            AddPartTo(body.transform, "Heart", new Vector3(0f, torsoY + 0.08f * k, 0.235f * k), new Vector3(0.1f, 0.1f, 0.02f) * k, light);
            AddRobotArmour(body.transform, torsoY, k, dark, light);
            AddRobotArms(body.transform, torsoY, k, plate, dark, trim, light);
            AddRobotHead(body.transform, torsoY + 0.35f * k, k, plate, dark, trim, light);
            AddRobotLight(body.transform, torsoY, k);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var rig = Describe(c, body.transform, 0.5f * s, legH, idh);
            rig.Arms = _arms.ToArray();
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>One jointed robot leg — the same hip → knee → foot pivots as <see cref="AddLeg"/> (so the gait and the foot
        /// planner drive it), in plating instead of a coat; the shin folds back like a biped's.</summary>
        private LegRig AddRobotLeg(Transform parent, int side, Vector3 hipPos, float legLen, float thick, Material plate, Material dark, Material trim)
        {
            float upper = legLen * UpperLegShare;
            float lower = legLen - upper;
            var hip = NewPivot(parent, side == 0 ? "LegL0" : "LegR0", hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0.02f * thick), new Vector3(thick, upper, thick * 1.1f), plate);
            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "KneePlate", new Vector3(0f, 0f, thick * 0.5f), new Vector3(thick * 0.85f, thick * 0.6f, thick * 0.25f), dark);
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.8f, lower, thick * 0.8f), dark);
            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Sole", new Vector3(0f, -thick * 0.25f, thick * 0.35f), new Vector3(thick * 1.1f, thick * 0.5f, thick * 1.6f), trim);
            return new LegRig
            {
                Hip = hip,
                Knee = knee,
                Foot = foot,
                Side = side,
                Row = 0,
                Rows = 1,
                UpperLen = upper,
                LowerLen = lower,
                KneeSign = 1, // a biped's knee folds the shin back
                HipRest = hip.localRotation,
                HipRestPos = hip.localPosition,
            };
        }

        /// <summary>The hunter's armour, friendly: shoulder plates with a light-blue rim light and the reactor on the back with a
        /// light-blue core (the enemy's is red).</summary>
        private void AddRobotArmour(Transform body, float torsoY, float k, Material dark, Material light)
        {
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                AddPartTo(body, side == 0 ? "ShoulderL" : "ShoulderR", new Vector3(sx * 0.42f * k, torsoY + 0.34f * k, 0f),
                    new Vector3(0.3f, 0.14f, 0.36f) * k, dark);
                AddPartTo(body, side == 0 ? "ShoulderLightL" : "ShoulderLightR", new Vector3(sx * 0.42f * k, torsoY + 0.34f * k, 0.185f * k),
                    new Vector3(0.22f, 0.03f, 0.01f) * k, light);
            }

            AddPartTo(body, "Reactor", new Vector3(0f, torsoY + 0.08f * k, -0.27f * k), new Vector3(0.3f, 0.3f, 0.12f) * k, dark);
            AddPartTo(body, "ReactorCore", new Vector3(0f, torsoY + 0.08f * k, -0.34f * k), new Vector3(0.14f, 0.14f, 0.04f) * k, light);
        }

        /// <summary>Two arms from the shoulders — upper arm, elbow, forearm and a ROUND tool hand (the enemy's forearms carry
        /// blades); the right forearm wears a small light-blue tool lamp. Registered as <see cref="ArmRig"/>s so the animator
        /// swings them against the legs.</summary>
        private void AddRobotArms(Transform body, float torsoY, float k, Material plate, Material dark, Material trim, Material light)
        {
            float upper = 0.32f * k, lower = 0.3f * k, thick = 0.16f * k;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var shoulder = NewPivot(body, side == 0 ? "ArmL" : "ArmR", new Vector3(sx * 0.4f * k, torsoY + 0.23f * k, 0f));
                AddPartTo(shoulder, "UpperArm", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), plate);
                var elbow = NewPivot(shoulder, "Elbow", new Vector3(0f, -upper, 0f));
                AddPartTo(elbow, "Forearm", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.9f, lower, thick * 0.9f), dark);
                AddPartTo(elbow, "ToolHand", new Vector3(0f, -lower - thick * 0.45f, 0.02f * k), Vector3.one * (thick * 1.25f), trim, PrimitiveType.Sphere);
                if (side == 1)
                {
                    AddPartTo(elbow, "ToolLamp", new Vector3(0f, -lower * 0.55f, thick * 0.48f), new Vector3(thick * 0.5f, thick * 0.35f, 0.01f * k), light);
                }

                shoulder.localRotation = Quaternion.Euler(0f, 0f, -sx * 8f); // the arms hang a touch away from the body
                _arms.Add(new ArmRig
                {
                    Shoulder = shoulder,
                    Elbow = elbow,
                    Side = side,
                    ShoulderRest = shoulder.localRotation,
                    ElbowRest = elbow.localRotation,
                });
            }
        }

        /// <summary>The head on a neck pivot (the animator turns the neck — gaze, look-around): the hunter's skull with one wide
        /// light-blue visor under the crest, a hinged mouth grille as the jaw (the voice moves it), and round sensor ears where
        /// the enemy has horns.</summary>
        private void AddRobotHead(Transform body, float neckY, float k, Material plate, Material dark, Material trim, Material light)
        {
            var neck = NewPivot(body, "Head", new Vector3(0f, neckY, 0.04f * k));
            _headPivot = NewPivot(neck, "HeadCentre", new Vector3(0f, 0.2f * k, 0f));
            AddPartTo(_headPivot, "Skull", new Vector3(0f, 0.03f * k, 0f), new Vector3(0.4f, 0.3f, 0.4f) * k, plate);
            AddPartTo(_headPivot, "Visor", new Vector3(0f, 0.06f * k, 0.205f * k), new Vector3(0.34f, 0.07f, 0.03f) * k, light);
            AddPartTo(_headPivot, "Crest", new Vector3(0f, 0.21f * k, -0.02f * k), new Vector3(0.06f, 0.08f, 0.34f) * k, dark);
            AddPartTo(_headPivot, "EarL", new Vector3(-0.22f * k, 0.04f * k, 0f), Vector3.one * (0.1f * k), trim, PrimitiveType.Sphere);
            AddPartTo(_headPivot, "EarR", new Vector3(0.22f * k, 0.04f * k, 0f), Vector3.one * (0.1f * k), trim, PrimitiveType.Sphere);
            _jawPivot = NewPivot(_headPivot, "Jaw", new Vector3(0f, -0.12f * k, 0.08f * k));
            AddPartTo(_jawPivot, "Grille", new Vector3(0f, -0.02f * k, 0.06f * k), new Vector3(0.28f, 0.08f, 0.26f) * k, dark);
            _heads.Add(neck);   // the animator turns the neck, the face comes along (as on the biped)
            _jaws.Add(_jawPivot);
        }

        /// <summary>A soft light-blue point light on the chest, so the robot lights its surroundings at night like a glowing
        /// creature does (#2151).</summary>
        private void AddRobotLight(Transform body, float torsoY, float k)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(body, false);
            go.transform.localPosition = new Vector3(0f, torsoY, 0.3f * k);
            _glow = go.AddComponent<Light>();
            FxLightBridge.Mirror(_glow);
            _glow.type = LightType.Point;
            _glow.range = 3.5f * k;
            _glow.intensity = 0.8f;
            _glow.color = RobotLight;
            _glow.shadows = LightShadows.None;
        }
    }
}
