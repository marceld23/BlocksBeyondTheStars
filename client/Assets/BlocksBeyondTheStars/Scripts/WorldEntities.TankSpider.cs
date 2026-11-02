// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The tank spider (#3096, Justus: "a mechanical spider with a tank instead of a hump"): the look and the warning of the
    /// server's <c>TankSpider</c>. The body follows the arachnid rig's layout (a cephalothorax with the head in front, eight
    /// jointed legs in four rows splayed from its flanks — <c>CreatureBuilder.BuildArachnid</c>) in the Guardian robots'
    /// plating and red eyes, and where a spider carries its abdomen it carries a round brass pressure tank with glowing
    /// bands and a valve. It skitters on a tetrapod gait (two alternating groups of four legs). When the server says its
    /// tank swells (<c>NetCombatEntity.Inflating</c>) it crouches, trembles, the tank grows by half, its bands blink from
    /// amber to white ever faster, steam hisses from the valve and the hiss plays — the warning to run, for
    /// <see cref="TankSpiderRules.InflateSeconds"/>. The burst itself comes as the <c>WorldFx</c>
    /// <see cref="TankSpiderRules.BurstFx"/>: a flash, sparks and a puff of smoke, the bang, a shake, and the push that throws
    /// the local player clear (mirroring the server's reach and its "a wall is cover" rule; the damage is the server's). No
    /// gore, no block ever changes. Shot before it bursts, it only pops.
    /// </summary>
    public sealed partial class WorldEntities
    {
        /// <summary>The tank spider's own parts on its <see cref="Entry"/> (null for every other machine).</summary>
        private sealed class TankSpiderParts
        {
            public readonly Transform[] Legs = new Transform[8]; // hip pivots: row-major, left then right
            public readonly float[] LegYaw = new float[8];       // each leg's resting splay (degrees about Y)
            public Transform Tank;                               // the tank pivot, scaled as it swells
            public Transform Valve;                              // where the steam comes out
            public Renderer[] Glow;                              // the tank's bands and valve light (tinted per frame)
            public float InflateStart = -1f;                     // Game.WorldTime the swelling began (-1 = not swelling)
            public float NextPuff;                               // paces the steam puffs while it swells
        }

        private const float SpiderHipHeight = 0.55f;
        private static Material _tankMat, _tankGlowMat;
        private static MaterialPropertyBlock _tankGlowBlock;
        private static readonly int TankGlowColorId = Shader.PropertyToID("_Color");
        private static readonly Color TankGlowIdle = new Color(1f, 0.55f, 0.15f); // dull amber
        private static readonly Color TankGlowHot = new Color(1f, 0.95f, 0.8f);   // near-white, about to burst
        private static readonly Color TankBurstColor = new Color(1f, 0.68f, 0.32f);
        private PlayerController _tankKnockPlayer;

        /// <summary>Builds the tank spider (see the class summary). The legs and the tank sit on pivots so
        /// <see cref="AnimateTankSpider"/> can walk and swell it; the arm/leg pivots of the shared <see cref="Entry"/> are
        /// dummies, like the drone's, so nothing else ever poses them.</summary>
        private static Entry BuildTankSpider(string id, Transform parent)
        {
            EnsureMaterials();
            EnsureTankMaterials();
            int h = Hash(id);
            var en = new Entry
            {
                Seed = (h & 0x3ff) * 0.137f,
                Pitch = 1.25f + (((h >> 5) % 17) / 17f * 0.25f), // a smaller machine: a higher growl
                Spider = new TankSpiderParts(),
            };

            var root = new GameObject("TankSpider");
            root.transform.SetParent(parent, true);
            root.transform.localScale = Vector3.one * (0.95f + ((h & 0xFF) / 255f * 0.12f));
            en.Root = root;

            en.Body = Pivot(root.transform, new Vector3(0f, SpiderHipHeight, 0f));
            Cube(en.Body, "Thorax", new Vector3(0f, 0.04f, 0.04f), new Vector3(0.5f, 0.28f, 0.52f), _hideMat);
            Cube(en.Body, "Belly", new Vector3(0f, -0.12f, 0.04f), new Vector3(0.42f, 0.08f, 0.44f), _hideDarkMat);
            en.Head = Pivot(en.Body, new Vector3(0f, 0.06f, 0.36f));
            Cube(en.Head, "Head", new Vector3(0f, 0f, 0.04f), new Vector3(0.32f, 0.2f, 0.2f), _hideDarkMat);
            AddSpiderEyes(en.Head);
            Part(en.Head, "FangL", new Vector3(-0.07f, -0.13f, 0.12f), new Vector3(0.04f, 0.14f, 0.04f), _clawMat, PrimitiveType.Cube, new Vector3(-25f, 0f, 8f));
            Part(en.Head, "FangR", new Vector3(0.07f, -0.13f, 0.12f), new Vector3(0.04f, 0.14f, 0.04f), _clawMat, PrimitiveType.Cube, new Vector3(-25f, 0f, -8f));
            AddTank(en);
            AddSpiderLegs(en);

            // Dummy limb pivots so the shared code paths never null-ref (the spider's legs are its own).
            en.ArmL = Pivot(root.transform, Vector3.zero);
            en.ArmR = Pivot(root.transform, Vector3.zero);
            en.LegL = Pivot(root.transform, Vector3.zero);
            en.LegR = Pivot(root.transform, Vector3.zero);
            return en;
        }

        /// <summary>Two rows of red sensor eyes on the head's front — the spider's cluster in the Guardians' colour.</summary>
        private static void AddSpiderEyes(Transform head)
        {
            Cube(head, "EyeL1", new Vector3(-0.06f, 0.04f, 0.145f), new Vector3(0.06f, 0.05f, 0.02f), _eyeMat);
            Cube(head, "EyeR1", new Vector3(0.06f, 0.04f, 0.145f), new Vector3(0.06f, 0.05f, 0.02f), _eyeMat);
            Cube(head, "EyeL2", new Vector3(-0.12f, -0.03f, 0.145f), new Vector3(0.04f, 0.035f, 0.02f), _eyeMat);
            Cube(head, "EyeR2", new Vector3(0.12f, -0.03f, 0.145f), new Vector3(0.04f, 0.035f, 0.02f), _eyeMat);
        }

        /// <summary>The tank where the abdomen would be: a brass sphere on a pivot behind the thorax, a glowing band around
        /// its middle and one along its back, a valve on top with a light, and two hoses down into the thorax.</summary>
        private static void AddTank(Entry en)
        {
            var sp = en.Spider;
            sp.Tank = Pivot(en.Body, new Vector3(0f, 0.16f, -0.5f));
            Part(sp.Tank, "Tank", Vector3.zero, new Vector3(0.6f, 0.52f, 0.66f), _tankMat, PrimitiveType.Sphere, Vector3.zero);
            var band = Part(sp.Tank, "BandRound", Vector3.zero, new Vector3(0.63f, 0.06f, 0.69f), _tankGlowMat, PrimitiveType.Sphere, Vector3.zero);
            var spine = Part(sp.Tank, "BandSpine", Vector3.zero, new Vector3(0.06f, 0.55f, 0.69f), _tankGlowMat, PrimitiveType.Sphere, Vector3.zero);
            Cube(sp.Tank, "ValveStem", new Vector3(0f, 0.3f, 0.04f), new Vector3(0.08f, 0.1f, 0.08f), _clawMat);
            sp.Valve = Pivot(sp.Tank, new Vector3(0f, 0.37f, 0.04f));
            var cap = Part(sp.Valve, "ValveLight", Vector3.zero, new Vector3(0.1f, 0.05f, 0.1f), _tankGlowMat, PrimitiveType.Cube, Vector3.zero);
            Cube(en.Body, "HoseL", new Vector3(-0.12f, 0.14f, -0.22f), new Vector3(0.05f, 0.05f, 0.2f), _hideDarkMat);
            Cube(en.Body, "HoseR", new Vector3(0.12f, 0.14f, -0.22f), new Vector3(0.05f, 0.05f, 0.2f), _hideDarkMat);
            sp.Glow = new[] { band, spine, cap };
        }

        /// <summary>Eight two-part legs in four rows on the thorax flanks (row 0 at the front, the arachnid rig's
        /// convention): the femur rises outward to the knee, the shin drops to the ground; the front rows splay forward,
        /// the back rows backward.</summary>
        private static void AddSpiderLegs(Entry en)
        {
            var sp = en.Spider;
            for (int row = 0; row < 4; row++)
            {
                float z = Mathf.Lerp(0.2f, -0.14f, row / 3f);
                float splay = Mathf.Lerp(38f, -38f, row / 3f);
                for (int side = 0; side < 2; side++)
                {
                    float s = side == 0 ? -1f : 1f;
                    var hip = Pivot(en.Body, new Vector3(s * 0.24f, -0.02f, z));
                    Part(hip, "Femur", new Vector3(s * 0.16f, 0.1f, 0f), new Vector3(0.38f, 0.06f, 0.06f), _hideMat, PrimitiveType.Cube, new Vector3(0f, 0f, s * 32f));
                    Part(hip, "Shin", new Vector3(s * 0.41f, -0.16f, 0f), new Vector3(0.05f, 0.74f, 0.05f), _clawMat, PrimitiveType.Cube, new Vector3(0f, 0f, s * 13.5f));
                    int i = (row * 2) + side;
                    sp.Legs[i] = hip;
                    sp.LegYaw[i] = -s * splay;
                    hip.localRotation = Quaternion.Euler(0f, sp.LegYaw[i], 0f);
                }
            }
        }

        /// <summary>A primitive part (cube or sphere) at a local position, size and rotation — visual only, like
        /// <see cref="Cube"/>. Returns its renderer.</summary>
        private static Renderer Part(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, PrimitiveType type, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only — the server owns combat/positions
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>The tank's brass and the glow of its bands — shared by every tank spider.</summary>
        private static void EnsureTankMaterials()
        {
            if (_tankMat != null)
            {
                return;
            }

            var lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var unlit = Shader.Find("Unlit/Color") ?? lit;
            _tankMat = WithFill(new Material(lit) { color = ShaderColor.Srgb(new Color(0.74f, 0.56f, 0.3f)) });
            _tankGlowMat = new Material(unlit) { color = ShaderColor.Srgb(TankGlowIdle) }; // bloom picks it up
            _tankGlowBlock = new MaterialPropertyBlock();
        }

        /// <summary>Follows the server's <c>Inflating</c>: the moment it turns on, the swelling clock starts and the hiss plays.</summary>
        private void TrackTankSpider(Entry en, NetCombatEntity e)
        {
            var sp = en.Spider;
            if (e.Inflating && sp.InflateStart < 0f)
            {
                sp.InflateStart = Game.WorldTime;
                ClientAudio.Instance?.At(TankSpiderRules.InflateCue, en.Root.transform.position, 1f, 1f);
            }
            else if (!e.Inflating)
            {
                sp.InflateStart = -1f;
            }
        }

        /// <summary>The spider's pose: skittering legs while it walks, the crouch, tremble, swelling tank, blinking bands and
        /// steam while its tank swells (<paramref name="e"/> is the server's latest state of it).</summary>
        private void AnimateTankSpider(Entry en, NetCombatEntity e, float speed)
        {
            TrackTankSpider(en, e);
            var sp = en.Spider;
            float swell = sp.InflateStart < 0f ? 0f : TankSpiderRules.InflateProgress(Game.WorldTime - sp.InflateStart);
            float moving = swell > 0f ? 0f : Mathf.Clamp01(speed / 2.5f);
            en.WalkPhase += Game.WorldDeltaTime * (4f + (speed * 3.2f)); // quick little steps
            for (int i = 0; i < sp.Legs.Length; i++)
            {
                int row = i / 2;
                float s = (i % 2) == 0 ? -1f : 1f;
                float phase = en.WalkPhase + ((((row + (i % 2)) % 2) == 0) ? 0f : Mathf.PI); // two groups of four in turn
                float swing = Mathf.Sin(phase) * 18f * moving;
                float lift = Mathf.Max(0f, Mathf.Cos(phase)) * 16f * moving;
                float crouch = 10f * swell; // the knees rise as the body sinks to brace, the feet stay on the ground
                sp.Legs[i].localRotation = Quaternion.Euler(0f, sp.LegYaw[i] - (s * swing), s * (lift + crouch));
            }

            float t = Game.WorldTime + en.Seed;
            float bob = Mathf.Sin(en.WalkPhase * 2f) * 0.02f * moving;
            Vector3 tremble = swell > 0f ? new Vector3(Mathf.Sin(t * 53f), 0f, Mathf.Cos(t * 47f)) * (0.012f * swell) : Vector3.zero;
            en.Body.localPosition = new Vector3(0f, SpiderHipHeight + bob - (0.1f * swell), 0f) + tremble;
            en.Head.localRotation = Quaternion.Euler(moving < 0.05f && swell <= 0f ? Mathf.Sin(t * 0.7f) * 6f : 0f, Mathf.Sin(t * 0.5f) * 12f * (1f - swell), 0f);

            float grow = 1f + (0.55f * swell * swell) + (Mathf.Sin(t * 30f) * 0.03f * swell);
            sp.Tank.localScale = Vector3.one * grow;
            TintTankGlow(sp, swell, t);
            if (swell > 0f && Game.WorldTime >= sp.NextPuff)
            {
                sp.NextPuff = Game.WorldTime + 0.1f;
                FxKit.Burst(FxKit.Kind.Smoke, sp.Valve.position, 1, Vector3.up, 30f, 0.6f, 1.6f, 0.06f, 0.12f, 0.3f, 0.5f,
                    new Color(0.92f, 0.92f, 0.95f, 0.6f)); // a hiss of steam from the valve
            }
        }

        /// <summary>The bands' glow: a calm amber at rest; while the tank swells it brightens toward white and blinks faster
        /// and faster (with Reduce flashes on it only brightens, no blinking).</summary>
        private static void TintTankGlow(TankSpiderParts sp, float swell, float t)
        {
            Color c = TankGlowIdle * (0.75f + (0.1f * Mathf.Sin(t * 1.5f)));
            if (swell > 0f)
            {
                float blink = FxKit.ReduceFlashes ? 1f : 0.55f + (0.45f * Mathf.Sin(t * (6f + (30f * swell))));
                c = Color.Lerp(TankGlowIdle, TankGlowHot, swell) * Mathf.Lerp(0.8f, 1.15f, blink);
            }

            c.a = 1f;
            _tankGlowBlock.Clear();
            _tankGlowBlock.SetColor(TankGlowColorId, ShaderColor.Srgb(c));
            foreach (var r in sp.Glow)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(_tankGlowBlock);
                }
            }
        }

        /// <summary>A defeated tank spider breaks apart like every machine. One that was shot before it burst only pops — a
        /// small puff and a quiet pop; one that burst has its bang from the burst effect already.</summary>
        private void TankSpiderDefeated(Entry en)
        {
            var sp = en.Spider;
            bool burst = sp.InflateStart >= 0f && Game.WorldTime - sp.InflateStart >= TankSpiderRules.InflateSeconds * 0.8f;
            if (!burst)
            {
                var at = sp.Tank.position;
                ClientAudio.Instance?.At(TankSpiderRules.BurstCue, at, 1.45f, 0.45f);
                FxKit.Burst(FxKit.Kind.Smoke, at, 5, Vector3.up, 70f, 0.4f, 1.2f, 0.18f, 0.3f, 0.5f, 0.9f, new Color(0.85f, 0.85f, 0.88f, 0.7f));
            }

            FxDefeat.BreakApart(en.Root, TankBurstColor, robot: true);
            Destroy(en.Root);
        }

        /// <summary>The server's burst effect (<see cref="TankSpiderRules.BurstFx"/>): flash, sparks and smoke, the bang, a
        /// shake with distance — and, for the local player in its reach whom no wall shelters, the push away from the tank
        /// (the server deals the damage by the same reach and the same cover rule).</summary>
        private void OnWorldFx(WorldFx fx)
        {
            if (Game == null || fx.Kind != TankSpiderRules.BurstFx)
            {
                return;
            }

            var spot = Game.ScenePos(fx.X, fx.Y, fx.Z);
            var at = spot + (Vector3.up * 0.7f); // the tank's height
            FxKit.Flash(at, TankBurstColor, FxKit.ReduceFlashes ? 2f : 3.4f, 0.18f);
            FxLights.Flash(at, TankBurstColor, 2f, 9f, 0.3f);
            FxKit.Burst(FxKit.Kind.Sparks, at, 26, Vector3.zero, 0f, 3f, 8f, 0.04f, 0.08f, 0.3f, 0.6f, TankBurstColor, new Color(1f, 0.92f, 0.5f));
            FxKit.Burst(FxKit.Kind.Smoke, at, 12, Vector3.up, 80f, 0.8f, 2.4f, 0.45f, 0.85f, 1f, 1.8f, new Color(0.66f, 0.66f, 0.68f, 0.8f));
            ClientAudio.Instance?.At(TankSpiderRules.BurstCue, at, Random.Range(0.95f, 1.05f), 1f);

            _tankKnockPlayer = _tankKnockPlayer != null ? _tankKnockPlayer : FindAnyObjectByType<PlayerController>();
            var player = _tankKnockPlayer;
            if (player == null)
            {
                return;
            }

            var feet = Game.PlayerPosition;
            var away = feet - spot;
            player.AddCameraShake(Mathf.Clamp01(1f - (away.magnitude / 25f)) * 0.5f);
            float share = TankSpiderRules.FalloffAt(away.magnitude);
            if (share <= 0f || Game.Aboard || !BurstReaches(spot, feet))
            {
                return;
            }

            away.y = 0f;
            var dir = away.sqrMagnitude > 0.01f ? away.normalized : new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            player.ApplyKnock((dir * (TankSpiderRules.KnockSpeed * share)) + (Vector3.up * (TankSpiderRules.KnockLift * share)));
        }

        /// <summary>The server's cover rule for the burst, client side: its sightline from the tank to the player (both lifted
        /// to eye height, like <c>HasLineOfSight</c>) — a wall in between shelters the player from the push too.</summary>
        private bool BurstReaches(Vector3 spot, Vector3 feet)
        {
            if (Game?.World == null)
            {
                return true;
            }

            const float Eye = 1.5f;
            return SightLine.Clear(IsSightBlockingCell, IsFluidCell, BlocksBeyondTheStars.Shared.World.WorldConstants.FluidSightRange,
                spot.x, spot.y + Eye, spot.z, feet.x, feet.y + Eye, feet.z);
        }
    }
}
