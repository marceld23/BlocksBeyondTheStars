// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.FarTerrain;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Volcano craters smoke (#3277): over every summit crater within reach a steady, tall column of big smoke puffs rises
    /// out of the lava pool, bends with the wind the higher it climbs and spreads as it cools — an active volcano you can
    /// spot from far away. The craters come from the client's own copy of the world generator (the far terrain's
    /// main-thread source, <see cref="FarTerrainView.MainThreadSource"/>): the same deterministic answer the server's world
    /// holds, so no network data. Reach follows the far view (160 blocks without it, up to <see cref="MaxReach"/>), the
    /// haze fades the far columns like the land under them. Few, big, long-lived puffs on the shared
    /// <see cref="FxKit.Kind.Plume"/> emitter (no particle system or material of its own): Potato 1 crater, Low 2,
    /// Medium 3, High 4, the rate scaled by the effect density. Dark ash on volcanic worlds, lighter steam elsewhere, tinted
    /// a little by the world's mist colour. Off in the flight view. Render-only.
    /// </summary>
    public sealed class VolcanoSmokeView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int MaxReach = 400;           // blocks
        private const int NoFarViewReach = 160;     // without far terrain the haze closes in at the streamed edge
        private const float RefreshSeconds = 3f;
        private const float RefreshMove = 32f;      // blocks walked before the craters are asked again
        private const float PuffsPerSecond = 1.4f;  // per crater at density 1
        private const float RiseSpeed = 3f;         // m/s off the pool
        private const float Cooling = 0.11f;        // m/s² the lift fades by as the column climbs
        private const float WindPush = 0.32f;       // m/s² of sideways push at full wind

        private struct Column
        {
            public Vector3 Base;      // the pool's centre, one block over the lava, in the scene
            public float Radius;      // the crater's radius
            public float RimAbove;    // how far the rim stands over the pool
            public float Distance;
            public float Acc;
        }

        private readonly List<VolcanoCrater> _found = new List<VolcanoCrater>();
        private readonly List<Column> _columns = new List<Column>();
        private FarTerrainSource _source;
        private int _epoch = -1;
        private float _timer;
        private Vector3 _askedAt = new Vector3(float.MaxValue, 0f, 0f);
        private Color _smoke = Color.gray;

        private void Update()
        {
            var source = Game != null && !Game.SpaceViewActive && Game.FarView != null ? Game.FarView.MainThreadSource : null;
            if (source == null)
            {
                _columns.Clear();
                _source = null;
                return;
            }

            var p = Game.PlayerPosition;
            _timer -= Time.deltaTime;
            bool travelled = _epoch != Game.WorldEpoch;
            if (travelled || !ReferenceEquals(source, _source) || _timer <= 0f || (p - _askedAt).sqrMagnitude > RefreshMove * RefreshMove)
            {
                _epoch = Game.WorldEpoch;
                _source = source;
                _timer = RefreshSeconds;
                _askedAt = p;
                Refresh(p);
            }

            float dt = Game.WorldDeltaTime;
            if (dt <= 0f || _columns.Count == 0)
            {
                return;
            }

            Vector3 wind = Game.WindVector;
            FxKit.SetPlumeForce(new Vector3(wind.x * WindPush, -Cooling, wind.z * WindPush));
            float rate = PuffsPerSecond * FxKit.Density;
            for (int i = 0; i < _columns.Count; i++)
            {
                var c = _columns[i];
                c.Acc = Mathf.Min(c.Acc + rate * dt, 3f);
                while (c.Acc >= 1f)
                {
                    c.Acc -= 1f;
                    Puff(c, wind);
                }

                _columns[i] = c;
            }
        }

        private void OnDisable() => _columns.Clear();

        /// <summary>The craters in reach, nearest first, capped per preset; and the smoke colour of this world.</summary>
        private void Refresh(Vector3 player)
        {
            int farView = Game.FarView != null ? Game.FarView.ActiveRange : 0;
            int reach = farView > 0 ? Mathf.Clamp(farView, NoFarViewReach, MaxReach) : NoFarViewReach;
            _found.Clear();
            _source.VolcanoCratersNear(Mathf.FloorToInt(player.x), Mathf.FloorToInt(player.z), reach, _found);

            _columns.Clear();
            foreach (var crater in _found)
            {
                // The query answers in the player's frame (the short way round the seams): already scene coordinates.
                var at = new Vector3(crater.X + 0.5f, crater.LavaTopY + 1f, crater.Z + 0.5f);
                _columns.Add(new Column
                {
                    Base = at,
                    Radius = (float)crater.Radius,
                    RimAbove = Mathf.Max(0f, crater.RimY + 1f - at.y),
                    Distance = Vector3.Distance(at, player),
                    Acc = Random.value, // keeps the mean rate exact across refreshes, even far below one puff a second
                });
            }

            _columns.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int cap = FxKit.Preset switch
            {
                QualityPreset.Potato => 1,
                QualityPreset.Low => 2,
                QualityPreset.Medium => 3,
                _ => 4,
            };
            if (_columns.Count > cap)
            {
                _columns.RemoveRange(cap, _columns.Count - cap);
            }

            _smoke = SmokeColor();
        }

        /// <summary>One big puff out of the pool: it rises fast past the rim, then the cooling and the wind take over.</summary>
        private void Puff(Column c, Vector3 wind)
        {
            Vector2 spot = Random.insideUnitCircle * (c.Radius * 0.45f);
            var at = c.Base + new Vector3(spot.x, Random.Range(0f, 1f), spot.y);
            // Fast enough to clear the rim before the lift fades; a little of the wind from the start.
            float rise = Mathf.Max(RiseSpeed, Mathf.Sqrt(2f * Cooling * (c.RimAbove + 6f)) + 1f) * Random.Range(0.85f, 1.15f);
            var vel = new Vector3(Random.Range(-0.3f, 0.3f), rise, Random.Range(-0.3f, 0.3f)) + wind * 0.8f;
            float size = Random.Range(6f, 9.5f) * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(c.Radius / 9f));
            var color = _smoke;
            color.a *= Random.Range(0.85f, 1f);
            FxKit.Emit(FxKit.Kind.Plume, at, vel, size, Random.Range(14f, 20f), color);
        }

        /// <summary>Dark ash on a volcanic world (lava or the volcanic tag), a lighter grey steam elsewhere; a light tint of
        /// the world's mist colour either way.</summary>
        private Color SmokeColor()
        {
            var env = Game.Environment;
            var planet = env != null ? Game.Content?.GetPlanet(env.Biome ?? string.Empty) : null;
            bool volcanic = planet != null && WorldGenerator.VentsVolcanicFor(planet, volcanoWorld: false);
            var (r, g, b) = VentRules.SteamTint(env != null ? env.MistColor : -1);
            var steam = new Color(r * 0.86f, g * 0.86f, b * 0.88f, 0.55f);
            return volcanic ? new Color(0.3f, 0.28f, 0.27f, 0.7f) : steam;
        }
    }
}
