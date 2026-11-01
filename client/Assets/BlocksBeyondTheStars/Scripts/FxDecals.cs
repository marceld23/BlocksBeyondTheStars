// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Scorch marks and impact craters (#3058, "Details (High)" — <see cref="HighDetails.Decals"/>): shots, blasts and giant
    /// stomps leave a mark on the terrain that fades after about a minute. Voxel-face decals, not depth projection: each
    /// mark is a small mesh laid on the visible faces of the solid cells around the hit (<see cref="DecalFaces"/>), so it
    /// never lands on a creature or player, never slides, and needs no depth texture or stencil.
    /// <list type="bullet">
    /// <item>Kinds: a small scorch (laser, rail, plasma — plasma with embers), a dent with a crack star (slug), a cracked
    /// ring where a giant's foot or a worm came down, a scorched crater with embers (terrain blaster, wrecked speeder).</item>
    /// <item>At most <see cref="MaxDecals"/> at a time (the oldest is recycled); 45 s + a 15 s fade, craters 90 s.</item>
    /// <item>A block change inside a mark's box rebuilds it (debounced): a blaster crater scorches the faces it carved, and
    /// a mined or covered scorch moves with the surface. A world reset or hyperjump clears them all.</item>
    /// </list>
    /// Render-only; every hook goes through the static entry points, which do nothing unless the switch is on (so Medium
    /// and below, the browser and phones are untouched). Lives on the world root (WorldRig).
    /// </summary>
    public sealed partial class FxDecals : MonoBehaviour
    {
        /// <summary>The live component (null outside a world).</summary>
        public static FxDecals Instance { get; private set; }

        public GameBootstrap Game;

        /// <summary>The cap on simultaneous marks; the oldest one is recycled for a new one.</summary>
        public const int MaxDecals = 64;

        /// <summary>The craters' kinds — they share the crater geometry, differ in texture, reach and embers.</summary>
        public enum CraterKind
        {
            /// <summary>The terrain blaster: a scorched crater with embers, on the floor and the walls facing the blast.</summary>
            Blast,

            /// <summary>A giant's stomp, a worm's strike or dive: a cracked ring on the ground, no embers.</summary>
            Quake,

            /// <summary>A speeder blown up: a smaller scorched crater with embers.</summary>
            Wreck,
        }

        private const float MaxDistance = 96f;  // beyond this a new mark would be a few pixels in the haze
        private const float RebuildDelay = 0.08f; // block changes arrive in bursts (a blast is ~120 of them)

        private bool _subscribed;

        // ---------------------------------------------------------------- entry points (the hooks' one-liners)

        /// <summary>A hand weapon's shot struck terrain at <paramref name="at"/> (on the struck face, <paramref name="normal"/>
        /// its outward normal) — called the moment the impact effect plays. Laser, rail and plasma leave a small scorch in
        /// a shade of the weapon's colour (plasma with embers), the slug a dent with a crack star; other looks nothing.</summary>
        public static void Shot(FxLook look, Vector3 at, Vector3 normal)
        {
            var d = Live;
            if (d == null || normal.sqrMagnitude < 1e-4f)
            {
                return;
            }

            switch (look.Style)
            {
                case FxStyles.Slug:
                    d.Place(SmallSpec(DecalPatterns.Dent, at, normal, 0.32f * look.Size, look.Color, 0.85f, 0f));
                    break;
                case FxStyles.Laser:
                    d.Place(SmallSpec(DecalPatterns.Scorch, at, normal, 0.36f * look.Size, look.Color, 0.8f, 0f));
                    break;
                case FxStyles.Rail:
                    d.Place(SmallSpec(DecalPatterns.Scorch, at, normal, 0.45f * look.Size, look.Color, 0.85f, 0f));
                    break;
                case FxStyles.Plasma:
                    d.Place(SmallSpec(DecalPatterns.Scorch, at, normal, 0.58f * look.Size, look.Color, 0.9f, 2.5f));
                    break;
            }
        }

        /// <summary>A crater ring centred at <paramref name="at"/> (radius in metres, at most four).</summary>
        public static void Crater(Vector3 at, float radius, CraterKind kind)
        {
            var d = Live;
            if (d != null)
            {
                d.Place(CraterSpec(at, Mathf.Clamp(radius, 1.2f, DecalFaces.MaxReach), kind));
            }
        }

        /// <summary>
        /// Another player's shot arrives only as from/to/hit (#2158): walks the voxels from <paramref name="from"/> a little
        /// past <paramref name="to"/> and, when a surface lies right at <paramref name="to"/>, snaps <paramref name="to"/>
        /// onto it and returns its face normal — the shot struck terrain, not a creature. False (normal zero, <paramref
        /// name="to"/> untouched) when the switch is off, the shot missed, or it ended anywhere else.
        /// </summary>
        public static bool TerrainHit(bool hit, Vector3 from, ref Vector3 to, out Vector3 normal)
        {
            normal = Vector3.zero;
            var d = Live;
            if (d == null || !hit)
            {
                return false;
            }

            var dir = to - from;
            if (dir.sqrMagnitude < 1e-4f)
            {
                return false;
            }

            var end = to + dir.normalized * 0.3f;
            if (!VoxelRay.Cast(d._blocks, from.x, from.y, from.z, end.x, end.y, end.z, out var vh))
            {
                return false;
            }

            var point = from + dir.normalized * vh.Distance;
            if ((point - to).sqrMagnitude > 0.3f * 0.3f)
            {
                return false; // the first surface on the way is not where the shot ended: it struck something else
            }

            var (nx, ny, nz) = DecalFaces.Normal(vh.Face);
            normal = new Vector3(nx, ny, nz);
            to = point;
            return true;
        }

        private static FxDecals Live => Instance != null && HighDetails.Decals ? Instance : null;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            _cellOf = CellAt;
            _blocks = (x, y, z) => CellAt(x, y, z) != DecalCell.Open;
            _container = new GameObject("FxDecals");
            _container.transform.SetParent(transform, false);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game != null)
            {
                Game.BlockChangeApplied -= OnBlockApplied;
                Game.HyperjumpStarted -= ClearAll;
                Game.WorldLoadStarted -= ClearAll;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            DestroyResources();
        }

        private void Update()
        {
            if (!_subscribed && Game != null)
            {
                Game.BlockChangeApplied += OnBlockApplied;
                Game.HyperjumpStarted += ClearAll; // every world reset raises one of the two
                Game.WorldLoadStarted += ClearAll;
                _subscribed = true;
            }

            if (!HighDetails.Decals)
            {
                if (_liveCount > 0)
                {
                    ClearAll(); // the switch went off (or the preset dropped below High): no marks stay behind
                }

                return;
            }

            bool show = Game == null || !Game.SpaceViewActive; // the marks stay on the planet while the ship flies
            if (_container.activeSelf != show)
            {
                _container.SetActive(show);
            }

            if (EnsureResources()) // the atlas is baked on the first frame the switch is on, not at the first shot
            {
                Tick(Time.time);
            }
        }

        /// <summary>A server block change (canonical cell) landed: every mark whose box it touches — or borders, since a
        /// face's visibility depends on the cell in front of it — rebuilds shortly.</summary>
        private void OnBlockApplied(Vector3i pos, BlockId oldId, BlockId newId)
        {
            if (_liveCount == 0 || Game == null)
            {
                return;
            }

            // #1829: canonical → scene, the copy of the cell nearest the player (the marks live in scene space).
            var p = Game.ScenePos(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
            var cell = new Vector3Int(Mathf.FloorToInt(p.x), pos.Y, Mathf.FloorToInt(p.z));
            float at = Time.time + RebuildDelay;
            foreach (var d in _pool)
            {
                if (d != null && d.Live && d.RebuildAt < 0f && Touches(d, cell))
                {
                    d.RebuildAt = at;
                }
            }
        }

        private static bool Touches(Decal d, Vector3Int c)
            => c.x >= d.Min.x - 1 && c.x <= d.Max.x + 1 && c.y >= d.Min.y - 1 && c.y <= d.Max.y + 1
               && c.z >= d.Min.z - 1 && c.z <= d.Max.z + 1;

        // ---------------------------------------------------------------- specs

        /// <summary>Everything that shapes one mark. <see cref="Center"/> may move when the surface under a small mark is
        /// mined or covered.</summary>
        private struct Spec
        {
            public int Tile;            // DecalPatterns tile
            public Vector3 Center;      // scene position on the struck surface
            public Vector3 Normal;      // main direction (unit)
            public float Radius;        // across the mark's plane (m)
            public float Depth;         // how far off its plane a face may lie (m)
            public float MinDot;        // DecalBox.MinDot
            public float FacingLift;    // DecalBox.FacingLift (craters)
            public bool Follows;        // a small mark moves with its surface; a crater stays put
            public Color Tint;          // linear soot colour
            public float Strength;      // 0..1 darkening at full life
            public float Life;          // seconds at full strength
            public float Fade;          // seconds of fade-out after that
            public float EmberTime;     // seconds the embers glow (0 = none)
            public float EmberPeak;     // HDR multiplier at the start
            public float Angle;         // texture rotation (radians) — no two marks look alike
        }

        /// <summary>The soot's base colour (sRGB): a warm near-black.</summary>
        private static readonly Color Soot = new Color(0.1f, 0.085f, 0.075f);

        private static Spec SmallSpec(int tile, Vector3 at, Vector3 normal, float size, Color weapon, float strength, float embers)
        {
            return new Spec
            {
                Tile = tile,
                Center = at,
                Normal = normal.normalized,
                Radius = Mathf.Clamp(size, 0.3f, 0.6f) * 0.5f,
                Depth = 0.55f,
                MinDot = 0.17f, // cos 80°: the struck wall only, not the floor beside it
                Follows = true,
                Tint = FxKit.Lin(Color.Lerp(Soot, weapon * 0.3f, 0.25f)),
                Strength = strength,
                Life = 45f,
                Fade = 15f,
                EmberTime = embers,
                EmberPeak = 3f,
                Angle = Random.Range(0f, Mathf.PI * 2f),
            };
        }

        private static Spec CraterSpec(Vector3 at, float radius, CraterKind kind)
        {
            bool quake = kind == CraterKind.Quake;
            return new Spec
            {
                Tile = quake ? DecalPatterns.QuakeRing : DecalPatterns.BurnCrater,
                Center = at,
                Normal = Vector3.up,
                Radius = radius,
                Depth = quake ? 2.5f : radius + 0.5f,
                MinDot = quake ? 0.5f : -0.1f, // a quake cracks the ground; a blast also scorches the walls facing it
                FacingLift = radius * 0.5f,
                Follows = false,
                Tint = FxKit.Lin(quake ? new Color(0.16f, 0.14f, 0.12f) : Soot),
                Strength = quake ? 0.65f : 0.9f,
                Life = 90f,
                Fade = 15f,
                EmberTime = quake ? 0f : 3f,
                EmberPeak = 4f,
                Angle = Random.Range(0f, Mathf.PI * 2f),
            };
        }
    }
}
