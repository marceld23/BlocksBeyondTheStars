// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The micro-fauna as small bodies (#3933). A critter used to be one flat sprite that always faced the camera; now it is a handful
    /// of boxes and plates in plain colours — form without colour: the colours are the kind's palette pick on this world,
    /// as before — that points where it flies, crawls or swims, with wings that really beat and a tail that wags
    /// (<c>CritterBody.shader</c> moves them from the time and a phase per critter; nothing is animated on the CPU).
    /// <para>
    /// Eleven builds cover the kinds that are bodies. The kinds that are LIGHTS — firefly, glow-worm, wisp, spore
    /// drifter, ember mite, glow plankton — stay the glowing sprites they are: a point of light is not a body.
    /// </para>
    /// The mesh is in the critter's own space: +Z ahead, +Y up, about one unit long; the view scales it to the kind.
    /// </summary>
    public static class CritterBodies
    {
        private enum Build { None, Moth, Buzzer, Dragonfly, Beetle, Ant, Spider, Crawler, Snail, Fish, Hopper, Balloon }

        private static Material _material;

        // Scratch lists, reused for every critter built (main thread only).
        private static readonly List<Vector3> Verts = new List<Vector3>(256);
        private static readonly List<Vector3> Normals = new List<Vector3>(256);
        private static readonly List<Color> Colours = new List<Color>(256);
        private static readonly List<Vector4> Parts = new List<Vector4>(256);
        private static readonly List<Vector4> Beats = new List<Vector4>(256);
        private static readonly List<int> Tris = new List<int>(512);

        /// <summary>How large a body is drawn against its kind's sprite (the sprites keep a margin round the animal).</summary>
        public const float Scale = 0.7f;

        private static Build BuildOf(string key) => key switch
        {
            "butterfly" or "moth" or "cavemoth" or "prismwing" => Build.Moth,
            "bee" or "fly" or "sandskimmer" => Build.Buzzer,
            "dragonfly" => Build.Dragonfly,
            "beetle" or "crystalbeetle" or "frostmite" or "waterbeetle" => Build.Beetle,
            "ant" => Build.Ant,
            "spider" or "strider" => Build.Spider,
            "caterpillar" or "worm" => Build.Crawler,
            "snail" => Build.Snail,
            "fish" or "tadpole" => Build.Fish,
            "ashhopper" => Build.Hopper,
            "gasbag" => Build.Balloon,
            _ => Build.None,
        };

        /// <summary>True for a kind that is drawn as a body (false: it stays a sprite — the lights, an unknown kind).</summary>
        public static bool Has(CritterKind kind) => kind != null && BuildOf(kind.Key) != Build.None;

        /// <summary>The one material every body shares (null when the shader is missing from the build).</summary>
        public static Material Material()
        {
            if (_material == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/CritterBody");
                if (shader != null)
                {
                    _material = new Material(shader) { name = "CritterBody" };
                }
            }

            return _material;
        }

        /// <summary>Fills <paramref name="mesh"/> with the body of <paramref name="kind"/> in <paramref name="tint"/>
        /// (sRGB-authored, the critter's own colour) and with its own <paramref name="phase"/> for the beat.</summary>
        public static void Fill(Mesh mesh, CritterKind kind, Color tint, float phase)
        {
            Verts.Clear();
            Normals.Clear();
            Colours.Clear();
            Parts.Clear();
            Beats.Clear();
            Tris.Clear();

            // Three colour places from the one tint: the main colour, a light one and a dark one. A glowing kind (the
            // prism wing) carries its glow in the colour's alpha.
            float glow = kind.Glow ? 0.55f : 0f;
            Color main = Linear(tint, 1f, glow);
            Color light = Linear(Color.Lerp(tint, Color.white, 0.45f), 1f, glow);
            Color dark = Linear(tint, 0.32f, 0f);
            Color pale = Linear(Color.Lerp(tint, new Color(0.92f, 0.94f, 0.96f), 0.8f), 1f, 0f);

            switch (BuildOf(kind.Key))
            {
                case Build.Moth: Moth(kind.Key, main, light, dark, phase); break;
                case Build.Buzzer: Buzzer(main, dark, pale, phase); break;
                case Build.Dragonfly: Dragonfly(main, light, dark, pale, phase); break;
                case Build.Beetle: Beetle(main, dark); break;
                case Build.Ant: Ant(main, dark); break;
                case Build.Spider: Spider(main, dark, kind.Key == "strider"); break;
                case Build.Crawler: Crawler(main, light, phase, kind.Key == "worm"); break;
                case Build.Snail: Snail(main, light, dark); break;
                case Build.Fish: Fish(main, light, dark, phase, kind.Key == "tadpole"); break;
                case Build.Hopper: Hopper(main, dark); break;
                case Build.Balloon: Balloon(main, light, dark, phase); break;
            }

            mesh.Clear();
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colours);
            mesh.SetUVs(0, Parts);
            mesh.SetUVs(1, Beats);
            mesh.SetTriangles(Tris, 0);
            // The wings and tails move in the shader: bounds that hold every pose.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.5f, 1.5f, 1.5f));
        }

        private static Color Linear(Color srgb, float gain, float glow)
        {
            Color c = ShaderColor.Srgb(new Color(srgb.r * gain, srgb.g * gain, srgb.b * gain, 1f));
            c.a = glow;
            return c;
        }

        // ---------------------------------------------------------------- the builds

        /// <summary>Butterfly, moth, prism wing: a slim body and two pairs of large wings that beat up and down.</summary>
        private static void Moth(string key, Color main, Color light, Color dark, float phase)
        {
            Box(new Vector3(0f, 0f, 0f), new Vector3(0.09f, 0.09f, 0.52f), dark);
            Box(new Vector3(0f, 0.01f, 0.29f), new Vector3(0.12f, 0.11f, 0.1f), dark);
            bool moth = key != "butterfly" && key != "prismwing";
            // A butterfly beats slowly and high, a moth flutters faster and flatter.
            var beat = new Vector4(phase, moth ? 26f : 18f, moth ? 0.95f : 1.2f, moth ? -0.25f : -0.4f);
            for (int side = -1; side <= 1; side += 2)
            {
                float s = side;
                var hinge = new Vector4(s, 0f, s * 0.045f, 0.03f);
                // fore wing: broad, swept a little forward; hind wing: smaller, rounded back
                Plate(new Vector3(s * 0.045f, 0.03f, 0.02f), new Vector3(s * 0.52f, 0.03f, 0.12f), new Vector3(s * 0.5f, 0.03f, 0.42f),
                    new Vector3(s * 0.045f, 0.03f, 0.25f), main, light, hinge, beat);
                Plate(new Vector3(s * 0.045f, 0.03f, -0.24f), new Vector3(s * 0.36f, 0.03f, -0.36f), new Vector3(s * 0.42f, 0.03f, -0.04f),
                    new Vector3(s * 0.045f, 0.03f, 0f), main, light, hinge, beat);
            }
        }

        /// <summary>Bee, fly, sand skimmer: a stout ringed body and small pale wings that whirr.</summary>
        private static void Buzzer(Color main, Color dark, Color pale, float phase)
        {
            Box(new Vector3(0f, 0f, -0.22f), new Vector3(0.3f, 0.28f, 0.2f), main);
            Box(new Vector3(0f, 0f, -0.03f), new Vector3(0.34f, 0.31f, 0.18f), dark);
            Box(new Vector3(0f, 0f, 0.15f), new Vector3(0.3f, 0.28f, 0.18f), main);
            Box(new Vector3(0f, -0.01f, 0.32f), new Vector3(0.2f, 0.19f, 0.16f), dark);
            var beat = new Vector4(phase, 70f, 0.9f, -0.15f);
            for (int side = -1; side <= 1; side += 2)
            {
                float s = side;
                var hinge = new Vector4(s, 0f, s * 0.1f, 0.15f);
                Plate(new Vector3(s * 0.1f, 0.15f, -0.06f), new Vector3(s * 0.5f, 0.15f, -0.14f), new Vector3(s * 0.52f, 0.15f, 0.1f),
                    new Vector3(s * 0.1f, 0.15f, 0.16f), pale, pale, hinge, beat);
            }
        }

        /// <summary>Dragonfly: a long thin body and four narrow wings.</summary>
        private static void Dragonfly(Color main, Color light, Color dark, Color pale, float phase)
        {
            Box(new Vector3(0f, 0f, -0.2f), new Vector3(0.06f, 0.06f, 0.62f), main);
            Box(new Vector3(0f, 0.01f, 0.19f), new Vector3(0.13f, 0.12f, 0.2f), light);
            Box(new Vector3(0f, 0.01f, 0.35f), new Vector3(0.15f, 0.12f, 0.1f), dark);
            for (int pair = 0; pair < 2; pair++)
            {
                float z0 = pair == 0 ? 0.13f : -0.04f;
                var beat = new Vector4(phase + (pair * 1.6f), 58f, 0.5f, -0.3f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float s = side;
                    var hinge = new Vector4(s, 0f, s * 0.06f, 0.07f);
                    Plate(new Vector3(s * 0.06f, 0.07f, z0), new Vector3(s * 0.52f, 0.07f, z0 - 0.02f), new Vector3(s * 0.52f, 0.07f, z0 + 0.11f),
                        new Vector3(s * 0.06f, 0.07f, z0 + 0.13f), pale, pale, hinge, beat);
                }
            }
        }

        /// <summary>Beetle and its kin: a domed shell, a head, three legs a side.</summary>
        private static void Beetle(Color main, Color dark)
        {
            Box(new Vector3(0f, 0.04f, -0.08f), new Vector3(0.5f, 0.28f, 0.58f), main);
            Box(new Vector3(0f, 0.12f, -0.08f), new Vector3(0.36f, 0.2f, 0.44f), main);
            Box(new Vector3(0f, 0f, 0.3f), new Vector3(0.3f, 0.2f, 0.2f), dark);
            Legs(dark, 0.26f, 0.2f, -0.1f, new[] { -0.26f, -0.04f, 0.18f });
        }

        /// <summary>Ant: three small segments in a row, three legs a side.</summary>
        private static void Ant(Color main, Color dark)
        {
            Box(new Vector3(0f, 0.02f, -0.3f), new Vector3(0.24f, 0.22f, 0.3f), main);
            Box(new Vector3(0f, 0f, -0.03f), new Vector3(0.16f, 0.15f, 0.2f), dark);
            Box(new Vector3(0f, 0.03f, 0.24f), new Vector3(0.22f, 0.2f, 0.22f), main);
            Legs(dark, 0.1f, 0.24f, -0.08f, new[] { -0.12f, -0.03f, 0.06f });
        }

        /// <summary>Spider (and the pond skater): a round body, a head, four long legs a side.</summary>
        private static void Spider(Color main, Color dark, bool skater)
        {
            Box(new Vector3(0f, 0.04f, -0.1f), new Vector3(skater ? 0.16f : 0.36f, skater ? 0.12f : 0.28f, skater ? 0.5f : 0.4f), main);
            Box(new Vector3(0f, 0.02f, 0.18f), new Vector3(skater ? 0.12f : 0.22f, skater ? 0.1f : 0.18f, 0.2f), dark);
            Legs(dark, skater ? 0.08f : 0.18f, skater ? 0.42f : 0.34f, skater ? -0.05f : -0.08f, new[] { -0.22f, -0.07f, 0.08f, 0.22f });
        }

        /// <summary>Caterpillar and worm: a row of small segments; a wave runs down the body.</summary>
        private static void Crawler(Color main, Color light, float phase, bool worm)
        {
            var beat = new Vector4(phase, worm ? 3.5f : 5f, worm ? 0.07f : 0.045f, 0f);
            var wag = new Vector4(0f, 1f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float z = -0.42f + (i * 0.168f);
                float thick = worm ? 0.12f : (i == 5 ? 0.2f : 0.18f);
                Box(new Vector3(0f, 0f, z), new Vector3(thick, thick * 0.9f, 0.16f), worm || (i % 2) == 0 ? main : light, wag, beat);
            }
        }

        /// <summary>Snail: a foot, a house on its back, two eye stalks.</summary>
        private static void Snail(Color main, Color light, Color dark)
        {
            Box(new Vector3(0f, -0.1f, 0.02f), new Vector3(0.2f, 0.1f, 0.72f), light);
            Box(new Vector3(0f, 0.12f, -0.1f), new Vector3(0.3f, 0.36f, 0.4f), main);
            Box(new Vector3(0f, 0.12f, -0.1f), new Vector3(0.34f, 0.2f, 0.22f), dark);
            Box(new Vector3(-0.05f, 0.02f, 0.34f), new Vector3(0.03f, 0.16f, 0.03f), light);
            Box(new Vector3(0.05f, 0.02f, 0.34f), new Vector3(0.03f, 0.16f, 0.03f), light);
        }

        /// <summary>Fish and tadpole: a body that tapers and a tail fin that wags.</summary>
        private static void Fish(Color main, Color light, Color dark, float phase, bool tadpole)
        {
            var beat = new Vector4(phase, tadpole ? 14f : 9f, tadpole ? 0.16f : 0.12f, 0f);
            if (tadpole)
            {
                Box(new Vector3(0f, 0f, 0.18f), new Vector3(0.34f, 0.3f, 0.36f), main);
                Box(new Vector3(0f, 0f, -0.08f), new Vector3(0.14f, 0.14f, 0.2f), main, new Vector4(0f, 0.4f, 0f, 0f), beat);
                Plate(new Vector3(0f, -0.09f, -0.16f), new Vector3(0f, -0.04f, -0.5f), new Vector3(0f, 0.04f, -0.5f), new Vector3(0f, 0.09f, -0.16f),
                    dark, dark, new Vector4(0f, 1f, 0f, 0f), beat);
                return;
            }

            Box(new Vector3(0f, 0f, 0.14f), new Vector3(0.18f, 0.3f, 0.38f), main);
            Box(new Vector3(0f, 0.01f, 0.36f), new Vector3(0.14f, 0.2f, 0.12f), main);
            Box(new Vector3(0f, 0f, -0.16f), new Vector3(0.13f, 0.22f, 0.28f), light, new Vector4(0f, 0.45f, 0f, 0f), beat);
            Plate(new Vector3(0f, -0.2f, -0.52f), new Vector3(0f, 0.2f, -0.52f), new Vector3(0f, 0.07f, -0.28f), new Vector3(0f, -0.07f, -0.28f),
                dark, light, new Vector4(0f, 1f, 0f, 0f), beat);
            Plate(new Vector3(0f, 0.15f, 0.02f), new Vector3(0f, 0.28f, -0.06f), new Vector3(0f, 0.26f, 0.16f), new Vector3(0f, 0.15f, 0.26f),
                dark, dark, Vector4.zero, beat);
        }

        /// <summary>Ash hopper: a body with large folded hind legs.</summary>
        private static void Hopper(Color main, Color dark)
        {
            Box(new Vector3(0f, 0.02f, -0.02f), new Vector3(0.2f, 0.2f, 0.62f), main);
            Box(new Vector3(0f, 0.04f, 0.34f), new Vector3(0.18f, 0.2f, 0.16f), dark);
            for (int side = -1; side <= 1; side += 2)
            {
                Box(new Vector3(side * 0.15f, 0.12f, -0.14f), new Vector3(0.06f, 0.3f, 0.12f), main);  // the thigh, raised
                Box(new Vector3(side * 0.17f, -0.06f, -0.26f), new Vector3(0.05f, 0.07f, 0.4f), dark);  // the shin, along the ground
                Box(new Vector3(side * 0.16f, -0.07f, 0.16f), new Vector3(0.14f, 0.04f, 0.04f), dark);  // a fore leg
            }
        }

        /// <summary>Gas bag: a balloon of crossed boxes with threads hanging under it.</summary>
        private static void Balloon(Color main, Color light, Color dark, float phase)
        {
            Box(new Vector3(0f, 0.16f, 0f), new Vector3(0.56f, 0.34f, 0.4f), main);
            Box(new Vector3(0f, 0.16f, 0f), new Vector3(0.4f, 0.34f, 0.56f), main);
            Box(new Vector3(0f, 0.18f, 0f), new Vector3(0.38f, 0.5f, 0.38f), light);
            var beat = new Vector4(phase, 2.2f, 0.05f, 0f);
            var wag = new Vector4(0f, 1f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -0.12f : 0.12f, z = (i & 2) == 0 ? -0.12f : 0.12f;
                Box(new Vector3(x, -0.26f, z), new Vector3(0.03f, 0.4f, 0.03f), dark, wag, new Vector4(phase + i, beat.y, beat.z, 0f));
            }
        }

        /// <summary>Thin legs to either side of the body at the given places along it.</summary>
        private static void Legs(Color colour, float bodyHalf, float reach, float y, float[] places)
        {
            foreach (float z in places)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Box(new Vector3(side * (bodyHalf + (reach * 0.5f)), y, z), new Vector3(reach, 0.04f, 0.04f), colour);
                }
            }
        }

        // ---------------------------------------------------------------- the pieces

        private static readonly Vector3[] BoxNormals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        private static void Box(Vector3 centre, Vector3 size, Color colour) => Box(centre, size, colour, Vector4.zero, Vector4.zero);

        /// <summary>A box about <paramref name="centre"/>; <paramref name="part"/> and <paramref name="beat"/> as the shader
        /// reads them (a wagging part: part.y = its weight).</summary>
        private static void Box(Vector3 centre, Vector3 size, Color colour, Vector4 part, Vector4 beat)
        {
            Vector3 h = size * 0.5f;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = BoxNormals[f];
                // two axes across the face, u × v = n (so the quad winds as a front face seen from outside)
                Vector3 u = f < 2 ? new Vector3(0f, 0f, -n.x) : f < 4 ? Vector3.forward : new Vector3(n.z, 0f, 0f);
                Vector3 v = f < 2 ? Vector3.up : f < 4 ? new Vector3(n.y, 0f, 0f) : Vector3.up;
                Vector3 o = centre + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                Quad(o - du - dv, o + du - dv, o + du + dv, o - du + dv, n, colour, colour, part, beat);
            }
        }

        /// <summary>A flat plate through four corners (a wing, a fin): the first and last corner are its root and take
        /// <paramref name="inner"/>, the two in between its tip and take <paramref name="outer"/>.</summary>
        private static void Plate(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color inner, Color outer, Vector4 part, Vector4 beat)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            Colours.Add(inner); Colours.Add(outer); Colours.Add(outer); Colours.Add(inner);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(n);
                Parts.Add(part);
                Beats.Add(beat);
            }

            Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
            Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
        }

        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color first, Color second, Vector4 part, Vector4 beat)
        {
            int i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
            Colours.Add(first); Colours.Add(second); Colours.Add(second); Colours.Add(first);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(n);
                Parts.Add(part);
                Beats.Add(beat);
            }

            Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
            Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
        }
    }
}
