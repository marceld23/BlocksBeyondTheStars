// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Geyser vents (#3275, #3276): every <c>geyser_vent</c> within <see cref="VentRules.SteamRange"/> blocks breathes a
    /// calm steam column all the time — leaning with the wind under open sky, dying before a roof — and, within
    /// <see cref="VentRules.EruptionRange"/>, erupts at random intervals (<see cref="VentSchedule"/>: 8–40 s apart, of
    /// varying strength): a burst of billowing puffs thrown up fast that drag to a drift and swell, plus white droplets
    /// arcing back on a water geyser, or dark ash, glowing embers and a short warm glow on a volcanic vent. Whether a
    /// world's vents are volcanic comes from its traits (<see cref="WorldGenerator.VentsVolcanicFor"/>); the steam takes a
    /// light tint of the world's mist colour.
    /// <para>The vents come from the world's marker index (<see cref="ClientWorld.MarkersNear"/>), refreshed twice a second —
    /// no walk of the block grid. At most <see cref="VentRules.MaxVents"/> vents, nearest first, fewer puffs far away.
    /// Everything goes through the shared <see cref="FxKit"/> emitters: no GameObject, particle system or material of its
    /// own (the old view spawned 16 primitive cubes and two never-destroyed materials per eruption, #3276). One clock — the
    /// world time — schedules everything, so nothing erupts behind the pause menu. Render-only; the server owns the world.</para>
    /// </summary>
    public sealed partial class GeyserView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float RefreshSeconds = 0.5f;
        private const int MaxHeadroom = 10;     // free cells counted over a vent; this many = open air (wind, full eruptions)
        private const int EruptHeadroom = 4;    // a vent with less room over it only breathes

        private sealed class Vent
        {
            public Vector3i Cell;        // canonical world cell (the key, and the schedule's seed)
            public Vector3 Base;         // the top face's centre, in the scene
            public float Distance;       // to the player
            public int Headroom;         // free cells over the vent (0 = smothered)
            public bool Open;            // nothing over it within MaxHeadroom: the wind takes the steam
            public VentSchedule Schedule;
            public float SteamAcc;
            public float EruptAge = -1f; // seconds into the current eruption, < 0 = none
            public float EruptLength;
            public float Strength;
            public float PuffAcc;
            public float BitAcc;         // droplets or embers
            public bool Seen;
        }

        private readonly Dictionary<Vector3i, Vent> _vents = new Dictionary<Vector3i, Vent>();
        private readonly List<Vent> _active = new List<Vent>();
        private readonly List<Vent> _scratch = new List<Vent>();
        private readonly List<(Vector3i Pos, ushort Block)> _hits = new List<(Vector3i Pos, ushort Block)>();
        private ClientWorld _world;
        private ushort _ventId;
        private int _epoch = -1;
        private float _refreshTimer;
        private bool _volcanic;
        private Color _steam = Color.white;

        private void Update()
        {
            if (Game == null || Game.World == null || Game.Content == null || Game.SpaceViewActive || !EnsureIndex())
            {
                _active.Clear();
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = RefreshSeconds;
                Refresh();
            }

            // World time (#908): nothing erupts behind the pause menu, and the schedule never mixes clocks (#3276).
            float dt = Game.WorldDeltaTime;
            float now = Game.WorldTime;
            Vector3 wind = Game.WindVector;
            for (int i = 0; i < _active.Count; i++)
            {
                var v = _active[i];
                if (v.Schedule.Poll(now, out float strength) && v.Distance <= VentRules.EruptionRange && v.Headroom >= EruptHeadroom)
                {
                    StartEruption(v, strength);
                }

                if (dt <= 0f)
                {
                    continue;
                }

                Breathe(v, dt, wind);
                if (v.EruptAge >= 0f)
                {
                    Erupt(v, dt, wind);
                }
            }
        }

        private void OnDisable() => _active.Clear();

        /// <summary>Registers the vent block with the world's marker index — again whenever the world (and with it the
        /// content) was rebuilt. False while the content has no vent block.</summary>
        private bool EnsureIndex()
        {
            if (_epoch != Game.WorldEpoch)
            {
                // Travel: the old world's vents must not breathe at their old spots until the next refresh.
                _epoch = Game.WorldEpoch;
                _vents.Clear();
                _active.Clear();
                _refreshTimer = 0f;
            }

            if (ReferenceEquals(_world, Game.World))
            {
                return _ventId != 0;
            }

            _world = Game.World;
            _vents.Clear();
            _active.Clear();
            var def = Game.Content.GetBlock("geyser_vent");
            _ventId = def != null ? def.NumericId.Value : (ushort)0;
            if (_ventId != 0)
            {
                _world.SetMarkerBlocks(new[] { _ventId });
            }

            return _ventId != 0;
        }

        /// <summary>The vents in reach, their headroom and distance; the active list nearest first, capped per preset. The
        /// look follows the world (volcanic or water, the mist colour).</summary>
        private void Refresh()
        {
            var p = Game.PlayerPosition;
            _hits.Clear();
            _world.MarkersNear(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z), (int)VentRules.SteamRange + 2, _hits);
            foreach (var v in _vents.Values)
            {
                v.Seen = false;
            }

            for (int i = 0; i < _hits.Count; i++)
            {
                if (_hits[i].Block == _ventId)
                {
                    Track(_hits[i].Pos, p);
                }
            }

            _scratch.Clear();
            foreach (var v in _vents.Values)
            {
                if (!v.Seen)
                {
                    _scratch.Add(v);
                }
            }

            foreach (var gone in _scratch)
            {
                _vents.Remove(gone.Cell);
            }

            _active.Clear();
            foreach (var v in _vents.Values)
            {
                if (v.Headroom > 0 && v.Distance <= VentRules.SteamRange)
                {
                    _active.Add(v);
                }
            }

            _active.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int cap = VentRules.MaxVents((int)FxKit.Preset);
            if (_active.Count > cap)
            {
                _active.RemoveRange(cap, _active.Count - cap);
            }

            RefreshLook();
        }

        /// <summary>Updates (or starts tracking) the vent at <paramref name="pos"/> — a position in the player's frame, so it
        /// is already the scene position.</summary>
        private void Track(Vector3i pos, Vector3 player)
        {
            var cell = WorldConstants.CanonicalBlock(pos, Game.Circumference);
            if (!_vents.TryGetValue(cell, out var v))
            {
                v = new Vent { Cell = cell, Schedule = new VentSchedule(cell.X, cell.Y, cell.Z), SteamAcc = Random.value };
                _vents[cell] = v;
            }

            v.Seen = true;
            v.Base = new Vector3(pos.X + 0.5f, pos.Y + 1f, pos.Z + 0.5f);
            v.Distance = Vector3.Distance(v.Base, player);
            int free = 0;
            while (free < MaxHeadroom && NearbyLights.AirAt(Game, pos.X, pos.Y + 1 + free, pos.Z))
            {
                free++;
            }

            v.Headroom = free;
            v.Open = free >= MaxHeadroom;
        }

        /// <summary>Volcanic or water vents and the steam's tint, from the world's traits and mist colour.</summary>
        private void RefreshLook()
        {
            var env = Game.Environment;
            var planet = env != null ? Game.Content.GetPlanet(env.Biome ?? string.Empty) : null;
            // A world grows vents only when it is wet or has volcanoes, so where a vent stands, "volcano world" is the
            // honest answer for the dry case — the same rule the generator uses with its own volcano flag.
            _volcanic = planet != null && WorldGenerator.VentsVolcanicFor(planet, volcanoWorld: true);
            var (r, g, b) = VentRules.SteamTint(env != null ? env.MistColor : -1);
            _steam = new Color(r, g, b);
        }
    }
}
