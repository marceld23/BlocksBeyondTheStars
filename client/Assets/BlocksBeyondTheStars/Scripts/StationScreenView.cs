// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places, a station's screens are on (docs/developer/LIVELY_PLACES.md §8): aboard a station the console
    /// screens near the player — the <c>data_cache</c> blocks its rooms use as displays — carry a lit panel that breathes
    /// and now and then blips brighter, like the terminals of the player's own ship (<see cref="StationDecorView"/>).
    /// Render-only and client-only: nothing is sent, the block is what it was, and no shader changes.
    /// <list type="bullet">
    /// <item><b>Which.</b> Every open side face of a screen block within <see cref="Reach"/> blocks of the player (a few
    /// rows up and down), the nearest <see cref="MaxPanels"/> of them; looked up once a second.</item>
    /// <item><b>Cheap.</b> Thin unlit boxes on four shared materials (four rhythms, so a wall of screens is not one
    /// pulse); no light, no particle. Fewer panels on the lower presets, none with Reduced effects.</item>
    /// </list>
    /// </summary>
    public sealed class StationScreenView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The block a station's rooms use as a display (<c>tools/gen_station_modules.py</c>: SCREEN).</summary>
        private const string ScreenBlock = "data_cache";

        private const int Reach = 12;
        private const int RiseUp = 3, RiseDown = 2;
        private const float LookSeconds = 1.0f;

        /// <summary>The lit panel: its edge on the block's face, its thickness, and how far it stands off the face.</summary>
        private const float PanelSize = 0.62f, PanelThick = 0.02f, PanelOff = 0.012f;

        private static readonly Color[] Tones =
        {
            new Color(0.30f, 0.75f, 0.95f), new Color(0.35f, 0.90f, 0.70f), new Color(0.95f, 0.72f, 0.30f), new Color(0.55f, 0.70f, 1.00f),
        };

        private static readonly (int X, int Z)[] Sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly Dictionary<long, GameObject> _panels = new Dictionary<long, GameObject>();
        private readonly List<(long Key, Vector3Int Cell, int Side, float DistSq)> _found = new List<(long, Vector3Int, int, float)>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        private Material[] _mats;
        private float _lookAt;

        /// <summary>The most panels at once, by the effect density of the quality preset.</summary>
        private static int MaxPanels => FxKit.Density >= 0.99f ? 16 : FxKit.Density >= 0.5f ? 10 : 6;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (Time.unscaledTime >= _lookAt)
            {
                _lookAt = Time.unscaledTime + LookSeconds;
                Look();
            }

            if (_mats == null || _panels.Count == 0)
            {
                return;
            }

            // Four rhythms: breathing with a rare brighter blip (the ship terminals' curve).
            for (int i = 0; i < _mats.Length; i++)
            {
                float n = Mathf.PerlinNoise(Time.time * 2.2f, i * 7.31f);
                float k = 0.65f + (0.35f * n) + (n > 0.92f ? 0.4f : 0f);
                _mats[i].color = ShaderColor.Srgb(Tones[i] * k);
            }
        }

        /// <summary>Finds the screens round the player and keeps a panel on the nearest ones.</summary>
        private void Look()
        {
            _wanted.Clear();
            _found.Clear();
            // A station is the current place only while the player walks its decks (GameBootstrap.TickPlaces).
            var screen = Game.World != null && Game.Content != null && !FxKit.ReducedEffects && Game.CurrentPlace is { Kind: PlaceKinds.Station }
                ? Game.Content.GetBlock(ScreenBlock)
                : null;
            if (screen != null)
            {
                Collect(screen.NumericId);
                _found.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
                int n = Mathf.Min(_found.Count, MaxPanels);
                for (int i = 0; i < n; i++)
                {
                    Keep(_found[i].Key, _found[i].Cell, _found[i].Side);
                }
            }

            _stale.Clear();
            foreach (var pair in _panels)
            {
                if (!_wanted.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }

            foreach (long key in _stale)
            {
                Destroy(_panels[key]);
                _panels.Remove(key);
            }
        }

        private void Collect(BlocksBeyondTheStars.Shared.Primitives.BlockId screen)
        {
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                for (int dz = -Reach; dz <= Reach; dz++)
                {
                    for (int dy = -RiseDown; dy <= RiseUp; dy++)
                    {
                        int x = px + dx, y = py + dy, z = pz + dz;
                        if (Game.World.GetBlock(x, y, z).Value != screen.Value)
                        {
                            continue;
                        }

                        for (int side = 0; side < Sides.Length; side++)
                        {
                            if (Game.World.GetBlock(x + Sides[side].X, y, z + Sides[side].Z).IsAir)
                            {
                                long key = ((long)(uint)(x & 0xFFFFF) << 44) | ((long)(uint)(z & 0xFFFFF) << 24) | ((long)(uint)(y & 0xFFF) << 12) | (uint)side;
                                _found.Add((key, new Vector3Int(x, y, z), side, (dx * dx) + (dy * dy * 4) + (dz * dz)));
                            }
                        }
                    }
                }
            }
        }

        private void Keep(long key, Vector3Int cell, int side)
        {
            _wanted.Add(key);
            if (!_panels.TryGetValue(key, out var panel) || panel == null)
            {
                _mats ??= BuildMaterials();
                bool alongX = Sides[side].X != 0; // the face looks along X: the panel is thin in X
                var scale = alongX ? new Vector3(PanelThick, PanelSize, PanelSize) : new Vector3(PanelSize, PanelSize, PanelThick);
                int rhythm = (int)(((uint)(cell.x * 73856093) ^ (uint)(cell.y * 19349663) ^ (uint)(cell.z * 83492791)) % (uint)_mats.Length);
                panel = Cube("StationScreen", transform, Vector3.zero, scale, _mats[rhythm]);
                var renderer = panel.GetComponent<Renderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                _panels[key] = panel;
            }

            // Device forms: the data cache is a pedestal — a column one eighth of a block narrower than the cube between a
            // foot and a top plate. The panel hangs on the column's face, framed by the two plates, instead of floating
            // 14 cm in front of it.
            float off = 0.5f + PanelOff + (PanelThick / 2f) - 0.125f;
            var at = Game.ScenePos(cell.x + 0.5f + (Sides[side].X * off), cell.y + 0.5f, cell.z + 0.5f + (Sides[side].Z * off));
            panel.transform.position = at;
        }

        private void OnDestroy()
        {
            if (_mats == null)
            {
                return;
            }

            foreach (var m in _mats)
            {
                Destroy(m);
            }
        }

        private static Material[] BuildMaterials()
        {
            var mats = new Material[Tones.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = Unlit(Tones[i]);
            }

            return mats;
        }
    }
}
