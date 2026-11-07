// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The two small-and-giant floaters of Soaniri (#3359, #3366):
    /// <list type="bullet">
    /// <item><b>The Soundeling</b> — a small pastel fluff ball with a soft glow on two tiny feet: a round puff ringed with tufts,
    /// a curl on top and a tiny mouth that coos — no eyes (none of Soaniri's creatures has any). Every animal wears one
    /// of a few pastels (by its id), so a group
    /// reads like a bag of sweets. The ball is the rig's head, so it turns to you; asleep — or hiding in the dark by
    /// day — the animator squashes it into a curled-up ball. While it carries a light a player gave it
    /// (<c>NetCreature.CarriesLight</c>) it holds a little lamp up over its head (<see cref="RigDescription.CarriedLight"/>).</item>
    /// <item><b>A Quali</b> — a giant translucent glowing jellyfish from the other reality: the medusa plan at the giant scale of
    /// <see cref="QualiRules"/> (a bell ten blocks across), a see-through dome over a glowing veil and core, glowing beads round
    /// the rim, long see-through tentacles and four frilly arms, and a light of its own. The bell pulses and the arms trail it
    /// (the medusa's poses).</item>
    /// </list>
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The Soundelings' pastels: pink, baby blue, mint, lemon, lilac, peach (the species colour joins them).</summary>
        private static readonly int[] SoundelingPastels = { 0xF4C6E4, 0xC9E7FF, 0xD8F5C8, 0xFFF1B8, 0xE2D1FF, 0xFFD6C2 };

        /// <summary>Where the tufts sit round a Soundeling's ball (directions from its centre).</summary>
        private static readonly Vector3[] SoundelingTufts =
        {
            new Vector3(1f, 0.2f, 0.3f), new Vector3(-1f, 0.25f, 0.2f), new Vector3(0.3f, 0.9f, -0.2f), new Vector3(-0.4f, 0.8f, 0.3f),
            new Vector3(0.6f, -0.3f, -0.8f), new Vector3(-0.7f, -0.2f, -0.6f), new Vector3(0.2f, 0.4f, -1f), new Vector3(0.5f, 0.6f, 0.6f),
        };

        /// <summary>Builds a Soundeling: the tiny feet, the fluff ball (its head) with its mouth and curl, the soft glow and the
        /// hidden lamp it holds up while carrying a light.</summary>
        private void BuildSoundeling(GameObject root, NetCreature c)
        {
            float u = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            int pick = CreatureCoat.PlacementVariant(c.Id) % (SoundelingPastels.Length + 1);
            Color pastel = Rgb(pick < SoundelingPastels.Length ? SoundelingPastels[pick] : (c.ColorRgb != 0 ? c.ColorRgb : 0xF4C6E4));
            var mood = MoodOf(c, 0.1f, true, true);
            Color col = mood.Tint(pastel) * mood.Dim;
            col.a = 1f;
            string hide = string.IsNullOrEmpty(c.Hide) ? "down" : c.Hide;
            var fluff = Lit(col, HideOr(hide));
            var fluffLight = Lit(Color.Lerp(col, Color.white, 0.35f), HideOr(hide));
            var feet = Lit(Rgb(c.BellyRgb != 0 ? c.BellyRgb : 0xFFF4D6), null);
            _bodyMat = fluff;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float legLen = 0.35f * u, d = 1.8f * u;
            float ballY = legLen + (d * 0.45f);
            for (int side = 0; side < 2; side++)
            {
                _legs.Add(AddStubLeg(body.transform, side, new Vector3((side == 0 ? -1f : 1f) * 0.28f * u, legLen, 0f), legLen, 0.22f * u, feet, feet));
            }

            var neck = NewPivot(body.transform, "Head", new Vector3(0f, ballY, 0f));
            _headPivot = NewPivot(neck, "Ball", Vector3.zero);
            AddSoundelingFluff(d, fluff, fluffLight);
            AddSoundelingFace(d);
            _heads.Add(neck);
            _jaws.Add(_jawPivot);
            AddSoftLight(body.transform, new Vector3(0f, ballY, 0f), 2.6f, 0.55f, pastel);
            var lamp = AddCarriedLamp(body.transform, new Vector3(0f, ballY + (d * 0.85f), d * 0.1f), u);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var rig = Describe(c, body.transform, u, legLen, idh);
            rig.CarriedLight = lamp;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>The fluff ball: a round puff, eight tufts round it in two shades, and a little curl on top.</summary>
        private void AddSoundelingFluff(float d, Material fluff, Material fluffLight)
        {
            AddPartTo(_headPivot, "Puff", Vector3.zero, Vector3.one * d, fluff, PrimitiveType.Sphere);
            for (int i = 0; i < SoundelingTufts.Length; i++)
            {
                AddPartTo(_headPivot, "Tuft" + i, SoundelingTufts[i].normalized * (d * 0.44f), Vector3.one * (d * 0.36f),
                    i % 2 == 0 ? fluffLight : fluff, PrimitiveType.Sphere);
            }

            AddPartTo(_headPivot, "Curl", new Vector3(0f, d * 0.6f, -d * 0.04f), new Vector3(d * 0.07f, d * 0.2f, d * 0.07f), fluffLight);
            AddPartTo(_headPivot, "CurlTip", new Vector3(0f, d * 0.72f, d * 0.02f), Vector3.one * (d * 0.13f), fluffLight, PrimitiveType.Sphere);
        }

        /// <summary>A tiny mouth on the jaw pivot (it coos) — and no eyes: like every creature of Soaniri a Soundeling has none
        /// (canon from the school club's creature notes); it finds the light by feeling its warmth.</summary>
        private void AddSoundelingFace(float d)
        {
            _jawPivot = NewPivot(_headPivot, "Jaw", new Vector3(0f, -d * 0.12f, d * 0.42f));
            AddPartTo(_jawPivot, "Mouth", new Vector3(0f, -d * 0.02f, d * 0.04f), new Vector3(d * 0.1f, d * 0.04f, d * 0.04f),
                Unlit(new Color(0.55f, 0.25f, 0.4f)));
        }

        /// <summary>The little lamp a Soundeling holds up while carrying a light — a stalk, a warm glowing lamp and its light;
        /// hidden until the animator shows it (<c>SetCarrying</c>).</summary>
        private GameObject AddCarriedLamp(Transform parent, Vector3 at, float u)
        {
            var lamp = new GameObject("CarriedLight");
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = at;
            AddPartTo(lamp.transform, "Handle", new Vector3(0f, -u * 0.22f, 0f), new Vector3(u * 0.06f, u * 0.44f, u * 0.06f),
                Lit(new Color(0.45f, 0.32f, 0.2f), null));
            AddPartTo(lamp.transform, "Lamp", Vector3.zero, Vector3.one * (u * 0.32f), Unlit(new Color(1f, 0.86f, 0.45f)));
            var light = lamp.AddComponent<Light>();
            FxLightBridge.Mirror(light);
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.78f, 0.45f);
            light.shadows = LightShadows.None;
            lamp.SetActive(false);
            return lamp;
        }

        /// <summary>Builds a Quali: the giant see-through bell over a glowing veil and core, rim beads, long tentacles and frilly
        /// arms, and its own light — handed to the animator as a medusa (the bell pulses, the arms trail it).</summary>
        private void BuildQuali(GameObject root, NetCreature c)
        {
            float u = 0.5f * Mathf.Clamp(c.Size, 0.4f, QualiRules.MaxSize);
            Color bellCol = Rgb(c.ColorRgb != 0 ? c.ColorRgb : 0x8FE8FF);
            Color glowCol = Rgb(c.BellyRgb != 0 ? c.BellyRgb : 0xD8B4FF);
            var bellMat = Ghost(Color.Lerp(bellCol, Color.white, 0.3f), 0.34f);
            var veilMat = Ghost(glowCol, 0.45f);
            _bodyMat = bellMat;

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            float bellR = QualiRules.BellRadius(c.Size);
            var bell = new GameObject("Bell");
            bell.transform.SetParent(body.transform, false);
            bell.transform.localPosition = new Vector3(0f, 1.7f * u, 0f);
            AddPartTo(bell.transform, "BellDome", Vector3.zero, new Vector3(bellR * 2f, bellR * 1.4f, bellR * 2f), bellMat, PrimitiveType.Sphere);
            AddPartTo(bell.transform, "Veil", new Vector3(0f, bellR * 0.1f, 0f), new Vector3(bellR * 1.5f, bellR * 0.9f, bellR * 1.5f), veilMat, PrimitiveType.Sphere);
            AddPartTo(bell.transform, "Core", new Vector3(0f, -bellR * 0.1f, 0f), Vector3.one * (bellR * 0.45f),
                Unlit(Color.Lerp(glowCol, Color.white, 0.25f)), PrimitiveType.Sphere);
            AddQualiRimAndArms(bell.transform, bellR, u, Unlit(Color.Lerp(bellCol, Color.white, 0.5f)), veilMat);
            AddSoftLight(bell.transform, Vector3.zero, 3f * u, 1.2f, Color.Lerp(bellCol, glowCol, 0.5f));

            var rig = Describe(c, body.transform, u, u, CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed));
            rig.Aquatic = false;
            rig.Bell = bell.transform;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>A Quali's glowing beads round the rim, twelve long see-through tentacles from it and four frilly arms from the
        /// middle — every one a chain the animator waves with the bell's pulse.</summary>
        private void AddQualiRimAndArms(Transform bell, float bellR, float u, Material bead, Material veil)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                AddPartTo(bell, "Bead" + i, new Vector3(cx * bellR * 0.92f, -bellR * 0.32f, sz * bellR * 0.92f), Vector3.one * (bellR * 0.11f), bead, PrimitiveType.Sphere);
                _tentacleChains.Add(AddChain(bell, $"Tent{i}_", new Vector3(cx * bellR * 0.8f, -bellR * 0.4f, sz * bellR * 0.8f), 0f,
                    7, u * 0.07f, u * 0.55f, 0.08f, veil));
            }

            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
                _tentacleChains.Add(AddChain(bell, $"Arm{i}_", new Vector3(Mathf.Cos(a) * bellR * 0.2f, -bellR * 0.45f, Mathf.Sin(a) * bellR * 0.2f), 0f,
                    5, u * 0.22f, u * 0.5f, 0.12f, veil));
            }
        }
    }
}
