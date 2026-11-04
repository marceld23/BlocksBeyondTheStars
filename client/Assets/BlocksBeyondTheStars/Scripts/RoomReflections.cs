// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Reflections inside ship and base rooms on the High preset of a desktop client (#3057). The block shader's only
    /// reflection is the sky colour faded by skylight, so indoors a polished floor looked like stone. Here the room the
    /// camera stands in is measured (<see cref="RoomBounds"/>, a capped flood-fill — the server knows no room shapes on
    /// the client), a realtime <see cref="ReflectionProbe"/> renders it into a small HDR cube, and the shader globals
    /// <c>_Sc_RoomCube/_Sc_RoomProbe/_Sc_RoomMin/_Sc_RoomMax</c> let <c>BlockAtlas</c> box-project it onto the room's
    /// glossy faces. Two cubes take turns: the probe renders into the back one (time-sliced, a face per frame) while the
    /// shader samples the front one, so a face never reads the texture it is being drawn into. Unity's probe does the
    /// cube-face orientation; the probe's own influence on other renderers is unused (our shaders read the globals).
    /// </summary>
    public sealed class RoomReflections : MonoBehaviour
    {
        /// <summary>Set by <see cref="WorldRig"/>.</summary>
        public GameBootstrap Game;

        private const int Resolution = 128;      // per cube face; blurry by design (rough metal), cheap to render
        private const float ScanSeconds = 0.5f;  // how often the room is re-measured
        private const float RefreshSeconds = 4f; // the slow background re-render (lamps, day and night)
        private const float FadePerSecond = 2f;  // the reflection fades in and out over ~0.5 s
        private const int NearRadius = 12;       // a base room with its door open: the open space this far around

        private static readonly int CubeId = Shader.PropertyToID("_Sc_RoomCube");
        private static readonly int ProbeId = Shader.PropertyToID("_Sc_RoomProbe");
        private static readonly int MinId = Shader.PropertyToID("_Sc_RoomMin");
        private static readonly int MaxId = Shader.PropertyToID("_Sc_RoomMax");

        private ReflectionProbe _probe;
        private RenderTexture _front, _back;
        private int _renderId = -1;
        private bool _frontValid;
        private bool _hasRoom;
        private RoomBox _room;
        private RoomBox _rendering;  // the box the back cube is being rendered for
        private RoomBox _shown;      // the box the front cube shows
        private float _weight;
        private float _scanTimer;
        private float _refreshTimer;
        private bool _dirty;
        private bool _subscribed;
        private bool _loggedNoRoom;

        private void Awake()
        {
            var go = new GameObject("RoomReflectionProbe");
            go.transform.SetParent(transform, false);
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            _probe.resolution = Resolution;
            _probe.hdr = true;
            _probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            _probe.backgroundColor = Color.black;
            _probe.nearClipPlane = 0.05f;
            _probe.farClipPlane = RoomBounds.MaxExtent * 2f;
            _probe.importance = 0;
            _probe.size = Vector3.one; // its own blending region is unused — the shader reads the globals
            _probe.cullingMask = ~((1 << 5) | (1 << 8)); // not the UI layer, not the visor HUD (layer 8)
            Shader.SetGlobalVector(ProbeId, Vector4.zero);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            Shader.SetGlobalVector(ProbeId, Vector4.zero);
            Release(ref _front);
            Release(ref _back);
        }

        private void LateUpdate()
        {
            Subscribe();
            bool want = HighDetails.RoomReflections && Game != null && Game.World != null && !Game.SpaceViewActive
                        && (Game.Aboard || Game.AtHome || !Game.ExposedToSky);
            _scanTimer -= Time.unscaledDeltaTime;
            if (!want)
            {
                _hasRoom = false;
            }
            else if (_scanTimer <= 0f || _dirty)
            {
                _scanTimer = ScanSeconds;
                Scan();
            }

            DriveProbe();
            float target = _hasRoom && _frontValid && _shown == _room ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, FadePerSecond * Time.unscaledDeltaTime);
            PushGlobals();
            if (!want && _weight <= 0f && _renderId < 0 && _front != null)
            {
                Release(ref _front); // nothing to show for a while: give the memory back
                Release(ref _back);
                _frontValid = false;
            }
        }

        private void Scan()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                _hasRoom = false;
                return;
            }

            var p = cam.transform.position;
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            bool found = RoomBounds.TryFind(x, y, z, CellAt, out var box)
                         || RoomBounds.TryFind(x, y - 1, z, CellAt, out box) // the eye can sit in a half-height cell
                         || (Game.Aboard && ShipBox(x, y, z, out box)) // a cabin with its hatch open: the ship's own box
                         || (Game.AtHome && RoomBounds.TryFindNear(x, y, z, NearRadius, CellAt, out box)); // a door left open
            if (!found && !_loggedNoRoom)
            {
                _loggedNoRoom = true;
                Debug.Log($"[RoomReflections] no room at ({x},{y},{z}) aboard={Game.Aboard} home={Game.AtHome}");
            }
            if (found && (!_hasRoom || box != _room))
            {
                _dirty = true; // a new room: render it now
                Debug.Log($"[RoomReflections] room ({box.MinX},{box.MinY},{box.MinZ})..({box.MaxX},{box.MaxY},{box.MaxZ}) from ({x},{y},{z})");
            }
            if (found)
            {
                _loggedNoRoom = false;
            }

            _hasRoom = found;
            _room = box;
        }

        /// <summary>The box of the parked ship the camera stands in — the room when the closed scan leaks out of an open
        /// hatch. Around the world's wrap seam the box is taken relative to the camera cell.</summary>
        private bool ShipBox(int x, int y, int z, out RoomBox box)
        {
            box = default;
            var ship = Game.LandedShipBoundsAt(x, y, z, out var local);
            if (ship == null)
            {
                return false;
            }

            int minX = x - local.X, minY = y - local.Y, minZ = z - local.Z;
            box = new RoomBox(minX, minY, minZ, minX + ship.Width - 1, minY + ship.Height - 1, minZ + ship.Length - 1);
            return true;
        }

        /// <summary>A parked ship's hull first (it is not part of the world grid), then the world.</summary>
        private RoomCell CellAt(int x, int y, int z)
        {
            if (!Game.LandedShipBlockAt(x, y, z, out _, out _).IsAir)
            {
                return RoomCell.Wall;
            }

            if (!Game.World.TryGetBlock(x, y, z, out BlockId block))
            {
                return RoomCell.Unknown;
            }

            return block.IsAir ? RoomCell.Open : RoomCell.Wall;
        }

        private void DriveProbe()
        {
            if (_renderId >= 0)
            {
                if (!_probe.IsFinishedRendering(_renderId))
                {
                    return;
                }

                (_front, _back) = (_back, _front);
                if (!_frontValid || _shown != _rendering)
                {
                    Debug.Log("[RoomReflections] cube ready for the room");
                }

                _shown = _rendering;
                _frontValid = true;
                _renderId = -1;
                Shader.SetGlobalTexture(CubeId, _front);
            }

            _refreshTimer -= Time.unscaledDeltaTime;
            if (!_hasRoom || (!_dirty && _refreshTimer > 0f))
            {
                return;
            }

            _dirty = false;
            _refreshTimer = RefreshSeconds;
            EnsureTextures();
            _rendering = _room;
            _probe.transform.position = Center(_room);
            _renderId = _probe.RenderProbe(_back);
        }

        private void PushGlobals()
        {
            if (_weight <= 0f || !_frontValid)
            {
                Shader.SetGlobalVector(ProbeId, Vector4.zero);
                return;
            }

            Vector3 c = Center(_shown);
            Shader.SetGlobalVector(ProbeId, new Vector4(c.x, c.y, c.z, _weight));
            Shader.SetGlobalVector(MinId, new Vector4(_shown.MinX, _shown.MinY, _shown.MinZ, 0f));
            float mips = Mathf.Log(Resolution, 2f);
            Shader.SetGlobalVector(MaxId, new Vector4(_shown.MaxX + 1, _shown.MaxY + 1, _shown.MaxZ + 1, mips));
        }

        private static Vector3 Center(RoomBox b)
            => new Vector3((b.MinX + b.MaxX + 1) * 0.5f, (b.MinY + b.MaxY + 1) * 0.5f, (b.MinZ + b.MaxZ + 1) * 0.5f);

        private void EnsureTextures()
        {
            _front ??= NewCube("RoomReflectionA");
            _back ??= NewCube("RoomReflectionB");
        }

        private static RenderTexture NewCube(string name)
        {
            var rt = new RenderTexture(Resolution, Resolution, 16, RenderTextureFormat.ARGBHalf)
            {
                name = name,
                dimension = TextureDimension.Cube,
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
            };
            rt.Create();
            return rt;
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
                rt = null;
            }
        }

        private void Subscribe()
        {
            if (_subscribed || Game == null)
            {
                return;
            }

            Game.BlockChangeApplied += OnBlockChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_subscribed && Game != null)
            {
                Game.BlockChangeApplied -= OnBlockChanged;
            }

            _subscribed = false;
        }

        /// <summary>A block edit near the room (a lamp placed, a wall opened) re-measures and re-renders it. The cell is
        /// canonical; compare across the world wrap. A moved block (#3173, a bridge deck or a trapdoor) counts the same.</summary>
        private void OnBlockChanged(Vector3i pos, BlockId oldId, BlockId newId, bool moved)
        {
            if (!_hasRoom)
            {
                return;
            }

            var c = Center(_room);
            int dx = WorldConstants.WrapDeltaX(pos.X - Mathf.FloorToInt(c.x), Game.Circumference);
            int dz = WorldConstants.WrapDeltaZ(pos.Z - Mathf.FloorToInt(c.z), Game.Circumference);
            if (Mathf.Abs(dx) <= RoomBounds.MaxExtent && Mathf.Abs(pos.Y - c.y) <= RoomBounds.MaxExtent
                && Mathf.Abs(dz) <= RoomBounds.MaxExtent)
            {
                _dirty = true;
            }
        }
    }
}
