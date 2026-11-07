// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3358/#3367: Soaniri's singing plants hum. On a world whose type grows them (<see cref="SingingPlantChorus.Sings"/>) a
    /// sparse scan of the cells around the player (every half second, every second cell, <see cref="SingingPlantChorus.ScanReach"/>
    /// blocks) finds the chime reeds, bell blooms and harp vines; <see cref="SingingPlantChorus.Pick"/> keeps the nearest
    /// <see cref="SingingPlantChorus.MaxVoices"/> of them (different kinds first) that no Soundrak silences, and each plays its
    /// loop on one of three pooled positional voices that fade in and out. Everywhere else — and in space, in a cabin, during a
    /// cutscene's duck — the voices fade to silence. The echo flower's answer and the blooms' opening are the server's.
    /// </summary>
    public sealed class SingingPlantsView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float ScanSeconds = 0.5f;
        private const float FadePerSecond = 1.2f;

        private readonly List<SingingPlantChorus.Plant> _plants = new List<SingingPlantChorus.Plant>();
        private readonly List<SingingPlantChorus.Voice> _picks = new List<SingingPlantChorus.Voice>();
        private readonly List<(float X, float Y, float Z)> _soundraks = new List<(float X, float Y, float Z)>();
        private readonly AudioSource[] _voices = new AudioSource[SingingPlantChorus.MaxVoices];
        private readonly float[] _targets = new float[SingingPlantChorus.MaxVoices];
        private float _scanTimer;

        private void Awake()
        {
            for (int v = 0; v < _voices.Length; v++)
            {
                var go = new GameObject("SingingPlant" + v);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.volume = 0f;
                src.spatialBlend = 0.9f;
                src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = SingingPlantChorus.Reach; // the chorus fades by distance itself; Unity only pans
                src.maxDistance = SingingPlantChorus.Reach + 1f;
                _voices[v] = src;
            }
        }

        private void Update()
        {
            var audio = ClientAudio.Instance;
            if (Game == null || audio == null)
            {
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanSeconds;
                Rescan(audio);
            }

            float level = audio.BedLevel * SingingPlantChorus.Level;
            for (int v = 0; v < _voices.Length; v++)
            {
                var src = _voices[v];
                src.volume = Mathf.MoveTowards(src.volume, _targets[v] * level, Time.deltaTime * FadePerSecond * level);
                if (src.isPlaying && src.volume <= 0f && _targets[v] <= 0f)
                {
                    src.Stop();
                }
            }
        }

        /// <summary>One scan: which plants sing, and on which voice.</summary>
        private void Rescan(ClientAudio audio)
        {
            for (int v = 0; v < _targets.Length; v++)
            {
                _targets[v] = 0f;
            }

            if (!Audible() || !SingingPlantChorus.Sings(Game.Content.GetPlanet(Game.Environment.Biome ?? string.Empty)))
            {
                return;
            }

            var listener = Camera != null ? Camera.transform.position : Game.PlayerPosition;
            ScanPlants();
            ScanSoundraks();
            SingingPlantChorus.Pick(_plants, listener.x, listener.y, listener.z, _soundraks, _picks);
            for (int v = 0; v < _picks.Count && v < _voices.Length; v++)
            {
                var plant = _plants[_picks[v].Plant];
                var src = _voices[v];
                var clip = audio.Clip(plant.Loop);
                if (clip == null)
                {
                    continue; // a loop that is not bundled stays silent
                }

                src.transform.position = new Vector3(plant.X, plant.Y, plant.Z);
                if (src.clip != clip)
                {
                    src.clip = clip;
                    src.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f)); // two reeds never hum in lockstep
                }

                if (!src.isPlaying)
                {
                    src.Play();
                }

                _targets[v] = _picks[v].Level;
            }
        }

        private bool Audible()
            => Game.Environment != null && Game.World != null && Game.Content != null && !Game.InSpace && !Game.SpaceViewActive
               && !Game.InEva && !Game.OnFootInSpace && string.IsNullOrEmpty(Game.StationName);

        /// <summary>Every second cell of the box around the player (the plants stand in patches, so the sample finds them),
        /// as scene positions — the copy of each plant nearest the player across the world's seams.</summary>
        private void ScanPlants()
        {
            _plants.Clear();
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            int reach = SingingPlantChorus.ScanReach;
            for (int dx = -reach; dx <= reach; dx += 2)
            {
                for (int dz = -reach; dz <= reach; dz += 2)
                {
                    for (int dy = -4; dy <= 4; dy++)
                    {
                        var def = Game.Content.BlockById(Game.World.GetBlock(px + dx, py + dy, pz + dz));
                        string loop = SingingPlantChorus.LoopFor(def?.Key);
                        if (loop != null)
                        {
                            var scene = Game.ScenePos(px + dx + 0.5f, py + dy + 0.5f, pz + dz + 0.5f);
                            _plants.Add(new SingingPlantChorus.Plant(scene.x, scene.y, scene.z, loop));
                        }
                    }
                }
            }
        }

        private void ScanSoundraks()
        {
            _soundraks.Clear();
            foreach (var c in Game.Creatures ?? System.Array.Empty<NetCreature>())
            {
                if (SingingPlantChorus.IsSoundrak(c))
                {
                    var scene = Game.ScenePos(c.X, c.Y, c.Z);
                    _soundraks.Add((scene.x, scene.y, scene.z));
                }
            }
        }
    }
}
