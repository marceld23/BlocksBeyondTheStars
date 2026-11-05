// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3247: the module emitters aboard — the reactor at the engines, the life support at the ceiling, the medbay tank and
    /// the cargo hold at their stations (<see cref="ShipSoundscape.EmittersFor"/>). Only the nearest three within 8 m play
    /// (<see cref="ShipSoundscape.PickEmitters"/>), on three pooled voices that fade in and out
    /// (<see cref="ShipSoundscape.EmitterVoices"/>); the cargo hold's voice plays a rare creak instead of a loop. Below 30 %
    /// hull the structure groans now and then somewhere over the player's head.
    /// </summary>
    public sealed partial class ShipSoundscapeView
    {
        private readonly List<ShipSoundscape.Emitter> _emitters = new List<ShipSoundscape.Emitter>();         // world space
        private readonly List<ShipSoundscape.Emitter> _sceneEmitters = new List<ShipSoundscape.Emitter>();    // this frame, wrapped
        private readonly List<ShipSoundscape.EmitterPick> _picks = new List<ShipSoundscape.EmitterPick>();
        private readonly List<int> _started = new List<int>();
        private readonly ShipSoundscape.EmitterVoices _voices = new ShipSoundscape.EmitterVoices();
        private readonly List<Vector3> _engineCells = new List<Vector3>();                                   // the hull aboard
        private AudioSource[] _voiceSources;
        private float[] _nextCargoCreak;
        private LandedShipModel _emittersShip;
        private NetShipStation[] _emittersStations;
        private string[] _emittersModules;
        private float _nextHullCreak = -1f;

        /// <summary>One frame of the module emitters for the hull the player walks in (null: none — every voice fades out).</summary>
        private void TickModules(LandedShipModel ship, Vector3 listener, float sfx)
        {
            _sceneEmitters.Clear();
            if (ship != null)
            {
                RebuildEmitters(ship);
                foreach (var e in _emitters)
                {
                    var p = Game.ScenePos(e.X, e.Y, e.Z); // the world wraps in longitude: the copy nearest the player
                    _sceneEmitters.Add(e.At(p.x, p.y, p.z));
                }
            }

            ShipSoundscape.PickEmitters(_sceneEmitters, listener.x, listener.y, listener.z, _picks, _voices);
            _voices.Tick(_picks, Time.deltaTime, _started);
            foreach (int v in _started)
            {
                StartVoice(v);
            }

            for (int v = 0; v < _voiceSources.Length; v++)
            {
                UpdateVoice(v, sfx);
            }
        }

        /// <summary>Rebuilds the emitter list when the hull, the station list or the modules changed. A list that sounds the
        /// same (a re-sent station list) keeps the voices that already play.</summary>
        private void RebuildEmitters(LandedShipModel ship)
        {
            string[] modules = ship.OwnerId == Game.LocalPlayerId ? Game.ShipCombat?.Modules : null;
            if (ReferenceEquals(ship, _emittersShip) && ReferenceEquals(Game.Stations, _emittersStations)
                && ReferenceEquals(modules, _emittersModules))
            {
                return;
            }

            _emittersShip = ship;
            _emittersStations = Game.Stations;
            _emittersModules = modules;
            var box = new ShipSoundscape.ShipBox(ship.Origin.X, ship.Origin.Y, ship.Origin.Z, ship.Width, ship.Height, ship.Length);
            var stations = new List<ShipSoundscape.StationSpot>();
            foreach (var s in Game.Stations ?? System.Array.Empty<NetShipStation>())
            {
                stations.Add(new ShipSoundscape.StationSpot(s.Type ?? string.Empty, s.X, s.Y, s.Z));
            }

            CollectEngineCells(ship, _engineCells);
            var engines = new List<(float X, float Z)>(_engineCells.Count);
            foreach (var c in _engineCells)
            {
                engines.Add((c.x, c.z));
            }

            var fresh = ShipSoundscape.EmittersFor(box, stations, modules, engines);
            fresh.RemoveAll(e => !Audible(e)); // a recording that is not bundled stays silent
            if (ShipSoundscape.SameLayout(fresh, _emitters))
            {
                return;
            }

            _emitters.Clear();
            _emitters.AddRange(fresh);
            _voices.Reset(); // the old indices mean nothing now
            foreach (var src in _voiceSources)
            {
                src.Stop();
                src.volume = 0f;
            }
        }

        private bool Audible(ShipSoundscape.Emitter e)
            => e.Kind == ShipSoundscape.EmitterKind.Loop ? ClientAudio.Instance.Clip(e.Clip) != null
                : PickClip(ShipSoundscape.CargoCreakClips) != null;

        /// <summary>A voice took a new emitter: a loop starts silent at its spot (the voice fades it in); the cargo hold's
        /// voice waits a while before its first creak.</summary>
        private void StartVoice(int v)
        {
            var src = _voiceSources[v];
            var e = _sceneEmitters[_voices.EmitterOf(v)];
            src.transform.position = new Vector3(e.X, e.Y, e.Z);
            src.volume = 0f;
            src.pitch = 1f;
            src.Stop();
            if (e.Kind == ShipSoundscape.EmitterKind.Loop)
            {
                src.clip = ClientAudio.Instance.Clip(e.Clip);
                src.loop = true;
                src.Play();
            }
            else
            {
                src.clip = null;
                _nextCargoCreak[v] = Time.time + 6f + (float)_rng.NextDouble() * 14f;
            }
        }

        /// <summary>A voice follows its emitter's spot and faded level; a freed voice stops once silent.</summary>
        private void UpdateVoice(int v, float sfx)
        {
            var src = _voiceSources[v];
            float level = _voices.LevelOf(v);
            src.volume = level * sfx;
            int index = _voices.EmitterOf(v);
            if (index < 0 || index >= _sceneEmitters.Count)
            {
                if (level <= 0f && src.isPlaying)
                {
                    src.Stop();
                }

                return;
            }

            var e = _sceneEmitters[index];
            src.transform.position = new Vector3(e.X, e.Y, e.Z);
            if (e.Kind == ShipSoundscape.EmitterKind.Creaks && level > 0.02f && Time.time >= _nextCargoCreak[v])
            {
                _nextCargoCreak[v] = Time.time + ShipSoundscape.NextCargoCreakDelay(_rng);
                var clip = ClientAudio.Instance.Clip(PickClip(ShipSoundscape.CargoCreakClips) ?? string.Empty);
                if (clip != null)
                {
                    src.pitch = Random.Range(0.92f, 1.06f);
                    src.PlayOneShot(clip, 1f); // the voice's volume carries the distance fade
                }
            }
        }

        /// <summary>Below 30 % hull the structure creaks now and then — rare, never closer than 8 s
        /// (<see cref="ShipSoundscape.NextHullCreakDelay"/>), somewhere in the walls and the roof around the player.</summary>
        private void TickHullCreaks(bool walking, float hull, Vector3 listener, ClientAudio audio)
        {
            if (!walking || !(hull < ShipSoundscape.CreakBelowHull))
            {
                _nextHullCreak = -1f;
                return;
            }

            if (_nextHullCreak < 0f)
            {
                _nextHullCreak = Time.time + ShipSoundscape.NextHullCreakDelay(hull, _rng); // never right as the line is crossed
                return;
            }

            if (Time.time < _nextHullCreak)
            {
                return;
            }

            _nextHullCreak = Time.time + ShipSoundscape.NextHullCreakDelay(hull, _rng);
            string id = PickClip(ShipSoundscape.HullCreakClips);
            if (id != null)
            {
                var at = listener + new Vector3(Random.Range(-3f, 3f), Random.Range(1f, 2.2f), Random.Range(-3f, 3f));
                audio.At(id, at, Random.Range(0.9f, 1.08f), ShipSoundscape.HullCreakLevel);
            }
        }

        /// <summary>The world-space centres of a hull's engine cells (empty for a hull without any).</summary>
        private void CollectEngineCells(LandedShipModel ship, List<Vector3> into)
        {
            into.Clear();
            if (Game.Content == null)
            {
                return;
            }

            foreach (var kv in ship.Cells)
            {
                if (ShipExhausts.IsEngineBlock(Game.Content.BlockById(kv.Value)?.Key))
                {
                    var c = kv.Key;
                    into.Add(new Vector3(ship.Origin.X + c.X + 0.5f, ship.Origin.Y + c.Y + 0.5f, ship.Origin.Z + c.Z + 0.5f));
                }
            }
        }
    }
}
