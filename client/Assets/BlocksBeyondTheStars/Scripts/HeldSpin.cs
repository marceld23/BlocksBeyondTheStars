// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3095: the moving parts of a held model (<see cref="HeldItem.Build"/>), on the holder itself — the first-person
    /// <see cref="Viewmodel"/> and every <see cref="PlayerAvatar"/> hand use the same one.
    /// <list type="bullet">
    /// <item><b>A drill's bit</b> (<see cref="HeldDrillBit.SpinParts"/>): the boxes on the drill's axis at its front turn about
    /// that axis — driven by every mining swing and the drill's equip flourish, spinning up quickly and coasting down
    /// (<see cref="HeldDrillBit.NextSpeed"/>). A pickaxe, a drill by its tool kind, has no bit and gets nothing.</item>
    /// <item><b>A blade's twirl</b>: every part of the model turns round the item's long axis (<see cref="SetTwirl"/>, set by
    /// the flourish each frame); the hand under it stays.</item>
    /// </list>
    /// Only the parts' own rotations change — their positions on the axis, and so the hand's grip and the muzzle
    /// (<see cref="HeldItem.MuzzleOf"/>), are untouched. The component sleeps (disabled) whenever the bit stands still, so a
    /// drill that is only carried costs nothing per frame.
    /// </summary>
    public sealed class HeldSpin : MonoBehaviour
    {
        private Transform[] _bit;
        private Transform[] _twirl;
        private Vector3[] _twirlPos;
        private Quaternion[] _twirlRot;
        private float _twirlDeg;
        private float _speed;          // 0..1 of HeldDrillBit.FullDegreesPerSecond
        private float _drivenUntil = -1f;
        private float _angle;

        /// <summary>Adds the moving parts to a freshly built held model of <paramref name="kind"/>, or returns null when it has
        /// none (anything but a drill with a bit or a blade).</summary>
        public static HeldSpin Attach(GameObject held, HeldItem.Kind kind)
        {
            if (held == null || (kind != HeldItem.Kind.Drill && kind != HeldItem.Kind.Blade))
            {
                return null;
            }

            var parts = new List<Transform>();
            var shapes = new List<HeldItemShapes.Part>();
            CollectParts(held.transform, parts, shapes);
            if (parts.Count == 0)
            {
                return null;
            }

            Transform[] bit = null;
            if (kind == HeldItem.Kind.Drill)
            {
                var indices = HeldDrillBit.SpinParts(shapes);
                if (indices.Count == 0)
                {
                    return null;
                }

                bit = new Transform[indices.Count];
                for (int i = 0; i < indices.Count; i++)
                {
                    bit[i] = parts[indices[i]];
                }
            }

            var spin = held.AddComponent<HeldSpin>();
            spin._bit = bit;
            if (kind == HeldItem.Kind.Blade)
            {
                spin.KeepTwirlRest(parts);
            }

            spin.enabled = false; // wakes up when the bit is driven
            return spin;
        }

        /// <summary>The model's boxes under <paramref name="held"/> (its "Part" children, in build order) and their shapes.</summary>
        private static void CollectParts(Transform held, List<Transform> parts, List<HeldItemShapes.Part> shapes)
        {
            var white = new HeldItemShapes.Rgb(1f, 1f, 1f);
            for (int i = 0; i < held.childCount; i++)
            {
                var c = held.GetChild(i);
                if (c.name != "Part")
                {
                    continue; // the hand under the item (#2777) and anything else that is not one of the model's boxes
                }

                var p = c.localPosition;
                var s = c.localScale;
                parts.Add(c);
                shapes.Add(new HeldItemShapes.Part(new System.Numerics.Vector3(p.x, p.y, p.z), new System.Numerics.Vector3(s.x, s.y, s.z), white));
            }
        }

        /// <summary>Remembers where a blade's boxes sit as built — the twirl turns them from there.</summary>
        private void KeepTwirlRest(List<Transform> parts)
        {
            _twirl = parts.ToArray();
            _twirlPos = new Vector3[parts.Count];
            _twirlRot = new Quaternion[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                _twirlPos[i] = parts[i].localPosition;
                _twirlRot[i] = parts[i].localRotation;
            }
        }

        /// <summary>Keeps a drill's bit driven for <paramref name="seconds"/> more (a mining swing, the flourish's spin-up).</summary>
        public void Drive(float seconds)
        {
            if (_bit == null)
            {
                return;
            }

            _drivenUntil = Mathf.Max(_drivenUntil, Time.time + seconds);
            enabled = true;
        }

        /// <summary>Turns a blade's parts <paramref name="degrees"/> round the item's long axis (0 = as built).</summary>
        public void SetTwirl(float degrees)
        {
            if (_twirl == null || Mathf.Approximately(degrees, _twirlDeg))
            {
                return;
            }

            _twirlDeg = degrees;
            var turn = Quaternion.AngleAxis(degrees, Vector3.forward);
            for (int i = 0; i < _twirl.Length; i++)
            {
                if (_twirl[i] != null)
                {
                    _twirl[i].localPosition = turn * _twirlPos[i];
                    _twirl[i].localRotation = turn * _twirlRot[i];
                }
            }
        }

        private void LateUpdate()
        {
            if (_bit == null)
            {
                enabled = false;
                return;
            }

            float dt = Time.deltaTime;
            _speed = HeldDrillBit.NextSpeed(_speed, Time.time < _drivenUntil, dt);
            if (_speed <= 0f)
            {
                enabled = false; // the bit stands still where it stopped
                return;
            }

            _angle = Mathf.Repeat(_angle + (_speed * HeldDrillBit.FullDegreesPerSecond * dt), 360f);
            var rot = Quaternion.Euler(0f, 0f, _angle);
            for (int i = 0; i < _bit.Length; i++)
            {
                if (_bit[i] != null)
                {
                    _bit[i].localRotation = rot;
                }
            }
        }
    }
}
