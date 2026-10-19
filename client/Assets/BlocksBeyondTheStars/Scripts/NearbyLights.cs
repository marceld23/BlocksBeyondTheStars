// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The light-emitting cells near the player, for the cosy effects that hang off them (#2818 lamplight motes, #2821
    /// campfire sparks). Two sources: the world's light-source index (<c>ClientWorld.LightSourcesNear</c>, which visits
    /// only the chunk buckets in reach, so nothing walks the block grid) and the lamps of the ships parked on the world —
    /// a parked ship is an object outside the world grid (<see cref="LandedShipView"/>), so its cabin lamps are not in that
    /// index. A parked ship's lamps are listed once per hull change. Render-only; each user keeps its own instance.
    /// </summary>
    public sealed class NearbyLights
    {
        /// <summary>One light-emitting cell: its block, its packed light (<c>BlockLight</c>), the scene position of the
        /// cell's centre and its distance from the player.</summary>
        public struct Hit
        {
            public BlockId Block;
            public int Packed;
            public Vector3 Scene;
            public float Distance;
        }

        private readonly List<(Vector3i Pos, int Rgb)> _world = new List<(Vector3i Pos, int Rgb)>();
        private readonly Dictionary<string, List<(Vector3i Pos, int Rgb)>> _shipLights = new Dictionary<string, List<(Vector3i Pos, int Rgb)>>();
        private GameBootstrap _subscribed;
        private bool _shipsDirty = true;

        /// <summary>Fills <paramref name="result"/> (cleared first) with every light-emitting cell within
        /// <paramref name="radius"/> blocks of the player, nearest first. <paramref name="fixturesOnly"/> skips the
        /// short-reach natural emitters (lava faces, crystals, glowing plants) up front — a lava lake holds thousands of
        /// them, and no lamp or campfire is one.</summary>
        public void Collect(GameBootstrap game, float radius, List<Hit> result, bool fixturesOnly = true)
        {
            result.Clear();
            if (game == null || game.World == null)
            {
                return;
            }

            Vector3 player = game.PlayerPosition;
            float r2 = radius * radius;

            // The world grid: the index keeps its sources canonical and hands them back in the frame of the chunk asked
            // for; ScenePos puts each at the copy nearest the player (round worlds).
            var cell = WorldConstants.CanonicalBlock(
                new Vector3i(Mathf.FloorToInt(player.x), Mathf.FloorToInt(player.y), Mathf.FloorToInt(player.z)), game.Circumference);
            _world.Clear();
            game.World.LightSourcesNear(WorldConstants.WorldToChunk(cell), Mathf.CeilToInt(radius), _world);
            for (int i = 0; i < _world.Count; i++)
            {
                if (fixturesOnly && BlockLight.RadiusFrom(_world[i].Rgb) < BlockLight.DefaultRadius)
                {
                    continue;
                }

                var pos = _world[i].Pos;
                var scene = game.ScenePos(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
                float d2 = (scene - player).sqrMagnitude;
                if (d2 > r2)
                {
                    continue;
                }

                result.Add(new Hit { Block = game.World.GetBlock(pos.X, pos.Y, pos.Z), Packed = _world[i].Rgb, Scene = scene, Distance = Mathf.Sqrt(d2) });
            }

            CollectShips(game, player, radius, result, fixturesOnly);
            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        /// <summary>The lamps of the parked ships in reach (placed exactly like <see cref="LandedShipView"/> draws them).</summary>
        private void CollectShips(GameBootstrap game, Vector3 player, float radius, List<Hit> result, bool fixturesOnly)
        {
            if (game.Content == null || game.LandedShips.Count == 0)
            {
                return;
            }

            if (_subscribed != game)
            {
                if (_subscribed != null)
                {
                    _subscribed.LandedShipsChanged -= OnShipsChanged;
                }

                game.LandedShipsChanged += OnShipsChanged;
                _subscribed = game;
                _shipsDirty = true;
            }

            if (_shipsDirty)
            {
                _shipLights.Clear(); // re-listed lazily below, only for the ships in reach
                _shipsDirty = false;
            }

            float r2 = radius * radius;
            foreach (var kv in game.LandedShips)
            {
                var ship = kv.Value;
                if (ship == null)
                {
                    continue;
                }

                float ox = game.SceneX(ship.Origin.X), oz = game.SceneZ(ship.Origin.Z);
                float margin = radius + 4f; // the hull box plus wings and nozzles
                if (player.x < ox - margin || player.x > ox + ship.Width + margin
                    || player.z < oz - margin || player.z > oz + ship.Length + margin
                    || player.y < ship.Origin.Y - margin || player.y > ship.Origin.Y + ship.Height + margin)
                {
                    continue;
                }

                if (!_shipLights.TryGetValue(kv.Key, out var lights))
                {
                    lights = ShipMeshBuilder.LightSources(game.Content, ship.Cells, ship.Mods);
                    _shipLights[kv.Key] = lights;
                }

                for (int i = 0; i < lights.Count; i++)
                {
                    if (fixturesOnly && BlockLight.RadiusFrom(lights[i].Rgb) < BlockLight.DefaultRadius)
                    {
                        continue;
                    }

                    var local = lights[i].Pos;
                    var scene = new Vector3(ox + local.X + 0.5f, ship.Origin.Y + local.Y + 0.5f, oz + local.Z + 0.5f);
                    float d2 = (scene - player).sqrMagnitude;
                    if (d2 > r2)
                    {
                        continue;
                    }

                    result.Add(new Hit { Block = ship.Get(local), Packed = lights[i].Rgb, Scene = scene, Distance = Mathf.Sqrt(d2) });
                }
            }
        }

        private void OnShipsChanged() => _shipsDirty = true;

        /// <summary>Whether a scene position is free air — neither a world block nor a parked ship's cell.</summary>
        public static bool AirAt(GameBootstrap game, int x, int y, int z)
            => game.World != null && game.World.GetBlock(x, y, z).IsAir && game.LandedShipBlockAt(x, y, z, out _, out _).IsAir;
    }
}
