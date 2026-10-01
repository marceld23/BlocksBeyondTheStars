// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Mining/placing feedback (M27 polish, reworked in the VFX overhaul #2155). The action players repeat most:
    /// <list type="bullet">
    /// <item>a wireframe selection box on the block the player is looking at;</item>
    /// <item>a <b>crack overlay</b> (<c>FxCrack</c>) that spreads cell by cell with the server's MiningProgress fraction,
    /// wobbles on every hit, and glows red→white-hot under the mining beam;</item>
    /// <item><b>chips from the struck face</b> in the block's own colour (sampled from its atlas tile), plus the drill's
    /// own look — dust and grit, hot orange sparks, cyan glitter, or the mining beam itself (<see cref="DrillTick"/>);</item>
    /// <item>the <b>break</b>: the block pops and splits into 2×2×2 tumbling cubes in its colour, with a glow flash and a
    /// dust puff, and the mined resource flies to the player as a glowing gem before the HUD tile flies to the hotbar;</item>
    /// <item>the <b>place pop</b>: a springy holo frame and a few motes.</item>
    /// </list>
    /// Everything goes through <see cref="FxKit"/>'s shared emitters (no per-break GameObjects or Materials) and is
    /// density-scaled. Render-only; the server stays authoritative over the actual block changes.
    /// </summary>
    public sealed class MiningFx : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>The local player rig — the outline asks it for the exact cell the next click would target
        /// (voxel, parked-ship cell or, with the right tool/item held, a water/lava cell) (#1353).</summary>
        public PlayerController Player;

        /// <summary>The world's mining effects (one per world rig).</summary>
        public static MiningFx Instance { get; private set; }

        private GameObject _outline;
        private Material _outlineMat;
        private bool _subscribed;

        private MeshRenderer _crack;
        private Material _crackMat;
        private float _crackShown;   // eased progress drawn by the overlay
        private float _heat;         // 0..1 mining-beam heat on the cracked block
        private float _heatUntil;
        private float _wobble;       // a little kick on every hit

        private void Awake() => Instance = this;

        private void Start()
        {
            // The outline tints with the crack, so it owns its material (one per world, destroyed with it).
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            _outlineMat = shader != null ? new Material(shader) { color = FxKit.Lin(new Color(0.05f, 0.05f, 0.06f)) } : null;
            _outline = BuildWireCube(_outlineMat);
            _outline.transform.SetParent(transform, false);
            _outline.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_outlineMat != null)
            {
                Destroy(_outlineMat);
            }

            if (_crackMat != null)
            {
                Destroy(_crackMat);
            }

            if (_subscribed && Game != null)
            {
                if (Game.Network != null)
                {
                    Game.Network.BlockChanged -= OnBlock;
                    Game.Network.MiningProgressReceived -= OnMineProgress;
                }

                Game.BlockChangeApplied -= OnBlockApplied;
            }
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.BlockChanged += OnBlock;
                Game.Network.MiningProgressReceived += OnMineProgress;
                Game.BlockChangeApplied += OnBlockApplied;
                _subscribed = true;
            }

            _breaksThisFrame = 0;
            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (_outline == null || Camera == null || Game == null || Game.MenuOpen || Game.SpaceViewActive)
            {
                if (_outline != null)
                {
                    _outline.SetActive(false);
                }

                ShowCrack(false, default);
                return;
            }

            if (AimVoxel(out int bx, out int by, out int bz))
            {
                var center = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                _outline.transform.position = center;
                _outline.SetActive(true);

                // Crack + tint while this block is being worked (fresh progress for this very cell).
                bool working = bx == _crackX && by == _crackY && bz == _crackZ && Time.time - _crackAt < 0.6f;
                float frac = working ? _crackFrac : 0f;
                if (_outlineMat != null)
                {
                    _outlineMat.color = FxKit.Lin(Color.Lerp(new Color(0.05f, 0.05f, 0.06f), new Color(1f, 0.45f, 0.12f), frac));
                }

                ShowCrack(working, center);
            }
            else
            {
                _outline.SetActive(false);
                ShowCrack(false, default);
            }
        }

        private void ShowCrack(bool on, Vector3 center)
        {
            float dt = Time.deltaTime;
            bool hot = Time.time < _heatUntil;
            _heat = Mathf.MoveTowards(_heat, hot ? 1f : 0f, dt * (hot ? 2.2f : 1.4f));
            if (!on)
            {
                _crackShown = 0f;
                if (_crack != null)
                {
                    _crack.gameObject.SetActive(false);
                }

                return;
            }

            if (_crack == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/FxCrack");
                if (shader == null)
                {
                    return;
                }

                _crackMat = new Material(shader) { name = "FxCrack (mining)" };
                var go = new GameObject("CrackOverlay");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = FxKit.CubeMesh;
                _crack = go.AddComponent<MeshRenderer>();
                _crack.sharedMaterial = _crackMat;
                _crack.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _crack.receiveShadows = false;
            }

            _crack.gameObject.SetActive(true);
            _crackShown = Mathf.MoveTowards(_crackShown, _crackFrac, dt * 3f);
            _wobble = Mathf.MoveTowards(_wobble, 0f, dt * 6f);
            _crack.transform.position = center;
            _crack.transform.localScale = Vector3.one * (1.004f + 0.025f * _wobble);
            _crackMat.SetFloat("_Progress", _crackShown);
            _crackMat.SetFloat("_Heat", _heat);
            _crackMat.SetFloat("_Seed", (_crackX * 7 + _crackY * 13 + _crackZ * 31) % 97);
        }

        /// <summary>The cell the selection box sits on: <see cref="PlayerController.AimOutlineCell"/>, i.e. the
        /// SAME voxel march the mine/place click uses (voxel world OR a parked ship cell, and since #1353 a
        /// water/lava cell whenever the held tool can mine it or the held item would be placed into it) —
        /// so the highlight can never disagree with the accepted target. Reading the world directly instead of
        /// <see cref="Physics.Raycast"/> is what kills the "black outline flickers in the air" glitch.</summary>
        private bool AimVoxel(out int bx, out int by, out int bz)
        {
            bx = by = bz = 0;
            if (Player == null || !Player.AimOutlineCell(out var cell))
            {
                return false;
            }

            bx = cell.x;
            by = cell.y;
            bz = cell.z;
            return true;
        }

        private int _crackX, _crackY, _crackZ;
        private float _crackFrac, _crackAt;
        private BlockId _crackBlock; // what is being mined (sampled pre-break)

        private void OnMineProgress(MiningProgress m)
        {
            // #1829: the server echoes the CANONICAL cell; the outline lives in scene space (the transform runs
            // unbounded across the wrap seam). Map it once here so the crack compare and the pickup still match
            // after a lap — past the seam the tint never lit and the mined tile never flew ("Wo ist die Animation?").
            var scene = SceneCell(m.X, m.Y, m.Z);
            bool sameCell = scene.x == _crackX && scene.y == _crackY && scene.z == _crackZ;
            _crackX = scene.x;
            _crackY = scene.y;
            _crackZ = scene.z;
            if (!sameCell)
            {
                _crackShown = 0f;
            }

            _crackFrac = Mathf.Clamp01(m.Fraction);
            _crackAt = Time.time;
            _wobble = 1f;
            if (Game?.World != null)
            {
                // Sample the block while it still exists — by the time BlockChanged arrives the world
                // has already been updated, so this is the only place the mined id is observable.
                _crackBlock = Game.World.GetBlock(m.X, m.Y, m.Z);
            }

            // The shipped-but-unused impact cue (#2151), quiet: one per hit the server counted.
            ClientAudio.Instance?.At("drill_impact", new Vector3(scene.x + 0.5f, scene.y + 0.5f, scene.z + 0.5f), 0.95f + Random.value * 0.1f, 0.4f);
        }

        /// <summary>One tick of the held drill on a block (called ~14×/s by the player while mining): chips from the
        /// struck face plus the drill's own look. <paramref name="face"/> = the struck face's outward normal,
        /// <paramref name="muzzle"/> = the tool tip (the mining beam starts there).</summary>
        public void DrillTick(FxLook look, Vector3Int cell, Vector3 face, Vector3 muzzle, bool drill)
        {
            var center = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            var point = FacePoint(center, face, 0.32f);
            var colors = BlockColors(Game?.World != null ? Game.World.GetBlock(cell.x, cell.y, cell.z) : default);
            DrillAt(look, point, face, muzzle, colors);
            if (drill && look.Is("mining_beam"))
            {
                _heatUntil = Time.time + 0.35f;
            }
        }

        /// <summary>A remote player's drill at work (#2158): the same look, at the face point they reported.</summary>
        public void RemoteDrill(FxLook look, Vector3 muzzle, Vector3 facePoint)
        {
            var dir = facePoint - muzzle;
            var face = dir.sqrMagnitude > 1e-4f ? -dir.normalized : Vector3.up;
            var cellCenter = facePoint - face * 0.5f;
            var colors = BlockColors(Game?.World != null
                ? Game.World.GetBlock(Mathf.FloorToInt(cellCenter.x), Mathf.FloorToInt(cellCenter.y), Mathf.FloorToInt(cellCenter.z))
                : default);
            for (int i = 0; i < 3; i++)
            {
                DrillAt(look, FacePoint(cellCenter, face, 0.32f), face, muzzle, colors);
            }
        }

        private static void DrillAt(FxLook look, Vector3 point, Vector3 face, Vector3 muzzle, Color[] colors)
        {
            var chipColor = colors[Random.Range(0, colors.Length)];
            switch (look.Style)
            {
                case "mining_beam":
                {
                    var mat = FxKit.BeamMaterial("mining_beam", look.Color2, coreWidth: 0.5f, noiseScale: 2f, noiseSpeed: 22f, pulse: 6f, intensity: 3f);
                    FxKit.Beam(muzzle, point, look.Color, 0.1f * look.Size, 0.11f, mat);
                    FxKit.Flash(point, look.Color2, 0.35f, 0.08f);
                    FxKit.Burst(FxKit.Kind.Sparks, point, 3, face, 60f, 2f, 5.5f, 0.03f, 0.05f, 0.2f, 0.4f, look.Color, look.Color2);
                    FxLights.Flash(point + face * 0.4f, look.Color, 1.2f, 4.5f, 0.12f);
                    Chip(point, face, chipColor);
                    break;
                }

                case "drill_hot":
                    FxKit.Burst(FxKit.Kind.Sparks, point, 4, face, 65f, 2.5f, 6f, 0.03f, 0.05f, 0.25f, 0.45f, look.Color, look.Color2);
                    Chip(point, face, chipColor);
                    break;
                case "drill_crystal":
                    FxKit.Burst(FxKit.Kind.Sparks, point, 2, face, 60f, 2f, 5f, 0.03f, 0.045f, 0.2f, 0.35f, look.Color);
                    FxKit.Burst(FxKit.Kind.Motes, point, 3, face, 80f, 0.5f, 1.6f, 0.03f, 0.06f, 0.35f, 0.7f, look.Color2, look.Color);
                    Chip(point, face, chipColor);
                    break;
                case "hand":
                    FxKit.Burst(FxKit.Kind.Dust, point, 2, face, 60f, 0.4f, 1.2f, 0.1f, 0.2f, 0.35f, 0.7f, Color.Lerp(chipColor, Color.white, 0.15f));
                    Chip(point, face, chipColor);
                    break;
                default: // "drill" and anything else: grit, a spark, a chip
                    FxKit.Burst(FxKit.Kind.Dust, point, 1, face, 50f, 0.4f, 1.1f, 0.1f, 0.18f, 0.3f, 0.6f, Color.Lerp(chipColor, Color.white, 0.15f));
                    FxKit.Burst(FxKit.Kind.Sparks, point, 1, face, 60f, 2f, 4.5f, 0.025f, 0.04f, 0.15f, 0.3f, look.Color2);
                    Chip(point, face, chipColor);
                    break;
            }
        }

        private static void Chip(Vector3 point, Vector3 face, Color color)
        {
            var tangent = Vector3.Cross(face, Random.onUnitSphere).normalized;
            var vel = face * Random.Range(1.4f, 3f) + tangent * Random.Range(0.3f, 1.4f) + Vector3.up * Random.Range(0.6f, 1.6f);
            FxKit.Emit(FxKit.Kind.Debris, point + face * 0.03f, vel, Random.Range(0.05f, 0.09f), Random.Range(0.55f, 0.85f), color);
        }

        /// <summary>A random point on the face of the block at <paramref name="center"/> facing <paramref name="face"/>.</summary>
        private static Vector3 FacePoint(Vector3 center, Vector3 face, float spread)
        {
            var n = face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.up;
            var t1 = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var t2 = Vector3.Cross(n, t1);
            return center + n * 0.5f + t1 * Random.Range(-spread, spread) + t2 * Random.Range(-spread, spread);
        }

        private int _breaksThisFrame;
        private Vector3Int _lastBrokenOwn = new Vector3Int(int.MinValue, 0, 0);

        /// <summary>Old→new transitions (with the id of the block that WAS there) — the break and place effects.</summary>
        private void OnBlockApplied(Vector3i pos, BlockId oldId, BlockId newId)
        {
            string oldKey = Game?.Content?.BlockById(oldId)?.Key ?? string.Empty;
            string newKey = Game?.Content?.BlockById(newId)?.Key ?? string.Empty;
            if (oldKey.Contains("water") || oldKey.Contains("lava") || newKey.Contains("water") || newKey.Contains("lava"))
            {
                return; // fluid simulation steps are not mining
            }

            var cell = SceneCell(pos.X, pos.Y, pos.Z);
            var center = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            if (Camera != null && (center - Camera.transform.position).sqrMagnitude > 64f * 64f)
            {
                return; // far-off edits (another player across the map) — nothing to see
            }

            // A terrain blaster (or a fast excavator) breaks dozens of cells in one frame: the first few get the full
            // break, the rest only a puff — the blast effect itself carries the moment.
            _breaksThisFrame++;
            bool full = _breaksThisFrame <= 6;
            if (newId.Value == 0 && oldId.Value != 0)
            {
                Break(center, BlockColors(oldId), full);
            }
            else if (newId.Value != 0 && oldId.Value == 0 && full)
            {
                PlacePop(center);
            }
        }

        private void OnBlock(BlockChanged m)
        {
            if (m.Block != 0)
            {
                return;
            }

            var cell = SceneCell(m.X, m.Y, m.Z); // #1829: canonical → scene, else the effect pops one world-lap away
            if (cell.x == _crackX && cell.y == _crackY && cell.z == _crackZ && _crackBlock.Value != 0 && cell != _lastBrokenOwn)
            {
                _lastBrokenOwn = cell;
                var pos = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
                PickupGem(pos, _crackBlock); // the mined resource flies to the player, then into the hotbar
                _crackBlock = default;
                _crackShown = 0f;
            }
        }

        /// <summary>The break: a glow flash, the block's 2×2×2 split cubes tumbling out, a dust puff in its colour.</summary>
        private static void Break(Vector3 center, Color[] colors, bool full)
        {
            var main = colors[0];
            FxKit.Burst(FxKit.Kind.Dust, center, full ? 6 : 2, Vector3.up, 80f, 0.6f, 1.8f, 0.18f, 0.34f, 0.45f, 0.9f, Color.Lerp(main, Color.white, 0.2f));
            if (!full)
            {
                return;
            }

            FxKit.Flash(center, Color.Lerp(main, Color.white, 0.6f), 1.3f, 0.12f);
            int n = Mathf.Max(4, FxKit.Scaled(8));
            for (int i = 0; i < n; i++)
            {
                var octant = new Vector3((i & 1) == 0 ? -0.25f : 0.25f, (i & 2) == 0 ? -0.25f : 0.25f, (i & 4) == 0 ? -0.25f : 0.25f);
                var vel = octant.normalized * Random.Range(1.6f, 2.8f) + Vector3.up * Random.Range(1.8f, 3f);
                FxKit.Emit(FxKit.Kind.Debris, center + octant, vel, Random.Range(0.3f, 0.44f), Random.Range(0.55f, 0.8f), colors[i % colors.Length]);
            }

            if (FxKit.Rich)
            {
                for (int i = 0; i < 6; i++)
                {
                    Chip(center + Random.insideUnitSphere * 0.3f, Random.onUnitSphere, colors[Random.Range(0, colors.Length)]);
                }
            }
        }

        /// <summary>The mined resource pops up as a glowing gem and curves into the player; then the HUD tile flies on to
        /// the hotbar with a rising tinkle (the Deep Rock Galactic / Astroneer "it's mine now" beat).</summary>
        private void PickupGem(Vector3 from, BlockId block)
        {
            var color = Color.Lerp(BlockColors(block)[0], Color.white, 0.35f);
            var player = Player != null ? Player.transform : null;
            var up = from + Vector3.up * 0.7f;
            FxKit.Animate(0.42f, t =>
            {
                var target = player != null ? player.position + Vector3.up * 1.1f : up;
                // Pop up first, then zip in (a quadratic curve through the pop point).
                var a = Vector3.Lerp(from, up, t);
                var b = Vector3.Lerp(up, target, t);
                var p = Vector3.Lerp(a, b, t * t);
                FxKit.Emit(FxKit.Kind.Glow, p, Vector3.zero, 0.22f, 0.06f, color);
                FxKit.Emit(FxKit.Kind.Motes, p, Random.insideUnitSphere * 0.3f, 0.04f, 0.25f, color);
            }, () =>
            {
                var at = player != null ? player.position + Vector3.up * 1.1f : up;
                FxKit.Burst(FxKit.Kind.Motes, at, 6, Vector3.up, 90f, 0.6f, 1.4f, 0.03f, 0.05f, 0.25f, 0.45f, color, Color.white);
                ClientAudio.Instance?.At("chime_2", at, 1.15f + Random.value * 0.15f, 0.35f);
                HudUi.Instance?.FlyPickup(at, block);
            }, this);
        }

        /// <summary>A springy holo frame and a few motes where a block was placed.</summary>
        private static void PlacePop(Vector3 center)
        {
            var tint = new Color(0.75f, 0.9f, 1f);
            var mat = FxGadgets.HoloMaterial(tint, 0f);
            if (mat == null)
            {
                return;
            }

            var kit = FxKit.Ensure();
            var mr = kit.RentMesh(FxKit.CubeMesh, mat);
            var tr = mr.transform;
            tr.position = center;
            var lin = FxKit.Lin(tint);
            FxKit.Animate(0.22f, t =>
            {
                float s = t < 0.4f ? Mathf.Lerp(1.18f, 0.97f, t / 0.4f) : Mathf.Lerp(0.97f, 1.01f, (t - 0.4f) / 0.6f);
                tr.localScale = Vector3.one * s;
                var b = FxKit.Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, 1f - t));
                b.SetFloat("_EdgeWidth", 0.06f);
                b.SetFloat("_Fill", 0.08f);
                b.SetFloat("_LineDensity", 0f);
                b.SetFloat("_Behind", 0f);
                b.SetFloat("_Intensity", 1.8f);
                mr.SetPropertyBlock(b);
            }, () => kit.ReleaseMesh(mr), mr);
            for (int i = 0; i < 4; i++)
            {
                var corner = center + new Vector3((i & 1) == 0 ? -0.5f : 0.5f, -0.5f, (i & 2) == 0 ? -0.5f : 0.5f);
                FxKit.Emit(FxKit.Kind.Motes, corner, Vector3.up * 0.6f + (corner - center) * 0.6f, 0.04f, 0.4f, new Color(0.85f, 0.95f, 1f));
            }
        }

        // ------------------------------------------------------------------ block colours

        private readonly Dictionary<ushort, Color[]> _blockColors = new Dictionary<ushort, Color[]>();

        /// <summary>Four representative colours of a block, sampled from its atlas tile (cached) — chips and break cubes
        /// carry the block's real colours instead of one tan for everything (#2155). Falls back to the map colour.</summary>
        public Color[] BlockColors(BlockId id)
        {
            if (_blockColors.TryGetValue(id.Value, out var cached))
            {
                return cached;
            }

            var result = new Color[4];
            var def = Game?.Content?.BlockById(id);
            var fallback = def != null ? WorldMap.MapColor(def.Key) : new Color(0.62f, 0.57f, 0.47f);
            var atlas = Game?.Atlas;
            bool sampled = false;
            if (def?.NumericId != null && atlas?.Texture != null && atlas.Texture.isReadable)
            {
                try
                {
                    var uv = atlas.TileUv(def.NumericId.Value);
                    for (int i = 0; i < 4; i++)
                    {
                        float u = uv.x + uv.width * (0.25f + 0.5f * (i & 1));
                        float v = uv.y + uv.height * (0.25f + 0.5f * ((i >> 1) & 1));
                        var c = atlas.Texture.GetPixelBilinear(u, v);
                        result[i] = new Color(c.r, c.g, c.b, 1f);
                    }

                    sampled = true;
                }
                catch (UnityException)
                {
                    sampled = false;
                }
            }

            if (!sampled)
            {
                for (int i = 0; i < 4; i++)
                {
                    float k = 0.85f + 0.1f * i;
                    result[i] = new Color(fallback.r * k, fallback.g * k, fallback.b * k, 1f);
                }
            }

            _blockColors[id.Value] = result;
            return result;
        }

        /// <summary>A canonical block cell as the scene cell nearest the player (<see cref="GameBootstrap.ScenePos"/>);
        /// the identity when no game is wired up (edit-mode rigs).</summary>
        private Vector3Int SceneCell(int x, int y, int z)
        {
            if (Game == null)
            {
                return new Vector3Int(x, y, z);
            }

            var p = Game.ScenePos(x + 0.5f, y + 0.5f, z + 0.5f);
            return new Vector3Int(Mathf.FloorToInt(p.x), y, Mathf.FloorToInt(p.z));
        }

        private static GameObject BuildWireCube(Material mat)
        {
            const float t = 0.05f;   // edge thickness
            const float s = 1.04f;    // edge length (slightly proud of the block)
            var root = new GameObject("BlockOutline");

            // Twelve edges of a unit cube centred on the root: 4 along each axis.
            for (int a = 0; a < 3; a++)
            {
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -0.5f : 0.5f;
                    float v = (i & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos, scale;
                    if (a == 0) { pos = new Vector3(0f, u, v); scale = new Vector3(s, t, t); }
                    else if (a == 1) { pos = new Vector3(u, 0f, v); scale = new Vector3(t, s, t); }
                    else { pos = new Vector3(u, v, 0f); scale = new Vector3(t, t, s); }

                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    var col = edge.GetComponent<Collider>();
                    if (col != null)
                    {
                        Destroy(col);
                    }

                    edge.transform.SetParent(root.transform, false);
                    edge.transform.localPosition = pos;
                    edge.transform.localScale = scale;
                    edge.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            return root;
        }
    }
}
