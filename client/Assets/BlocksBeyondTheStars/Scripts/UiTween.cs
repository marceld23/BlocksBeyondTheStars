// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Minimal tween runner for HUD motion (the "motion layer" of the HUD look pass). One hidden ticker
    /// MonoBehaviour advances every active tween on unscaled time; tweens are plain entries in a list, so
    /// starting one allocates only its setter delegate (they start on state changes, never per frame).
    /// Honours <see cref="UiKit.ReducedMotion"/>: with it on, a tween applies its end value at once.
    /// No package dependency — the project builds everything in code (ADR 0002).
    /// </summary>
    public static class UiTween
    {
        public enum Ease { Linear, OutQuad, InQuad, OutCubic, InOutCubic, OutBack, OutExpo }

        private sealed class Entry
        {
            public int Id;
            public object Target;
            public float From, To, Duration, Delay, Age;
            public Ease Ease;
            public Action<float> Set;
            public Action Done;
            public bool Dead;
        }

        private static readonly List<Entry> _active = new List<Entry>(64);
        private static int _nextId = 1;
        private static Ticker _ticker;

        /// <summary>Eased 0..1 → 0..1.</summary>
        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InQuad: return t * t;
                case Ease.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case Ease.OutBack:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                case Ease.OutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                default: return t;
            }
        }

        /// <summary>Tweens a float from <paramref name="from"/> to <paramref name="to"/>, calling
        /// <paramref name="set"/> with the eased value each frame. Returns a handle for <see cref="Kill(int)"/>.
        /// <paramref name="target"/> groups tweens so <see cref="Kill(object)"/> can cancel everything on one widget.</summary>
        public static int To(float from, float to, float duration, Action<float> set, Ease ease = Ease.OutCubic,
            float delay = 0f, Action done = null, object target = null)
        {
            if (set == null)
            {
                return 0;
            }

            if (UiKit.ReducedMotion || duration <= 0f)
            {
                set(to);
                done?.Invoke();
                return 0;
            }

            EnsureTicker();
            var e = new Entry
            {
                Id = _nextId++, Target = target, From = from, To = to, Duration = duration, Delay = Mathf.Max(0f, delay),
                Ease = ease, Set = set, Done = done,
            };
            if (e.Delay <= 0f)
            {
                set(from);
            }

            _active.Add(e);
            return e.Id;
        }

        /// <summary>Fades a CanvasGroup to <paramref name="to"/>.</summary>
        public static int Alpha(CanvasGroup group, float to, float duration, Ease ease = Ease.OutCubic, float delay = 0f, Action done = null)
        {
            if (group == null)
            {
                return 0;
            }

            Kill(group);
            return To(group.alpha, to, duration, a => { if (group != null) { group.alpha = a; } }, ease, delay, done, group);
        }

        /// <summary>Slides a RectTransform's anchored position to <paramref name="to"/>.</summary>
        public static int Move(RectTransform rt, Vector2 from, Vector2 to, float duration, Ease ease = Ease.OutCubic, float delay = 0f)
        {
            if (rt == null)
            {
                return 0;
            }

            Kill(rt);
            return To(0f, 1f, duration, k => { if (rt != null) { rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k); } }, ease, delay, null, rt);
        }

        /// <summary>Uniform scale from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static int Scale(Transform t, float from, float to, float duration, Ease ease = Ease.OutBack, float delay = 0f)
        {
            if (t == null)
            {
                return 0;
            }

            Kill(t);
            return To(from, to, duration, s => { if (t != null) { t.localScale = new Vector3(s, s, 1f); } }, ease, delay, null, t);
        }

        /// <summary>A quick 1 → 1+amount → 1 "pop" (selection feedback). Centre-pivoted transforms only.</summary>
        public static int Pop(Transform t, float amount = 0.08f, float duration = 0.16f)
        {
            if (t == null)
            {
                return 0;
            }

            Kill(t);
            return To(0f, 1f, duration, k =>
            {
                if (t != null)
                {
                    float s = 1f + Mathf.Sin(k * Mathf.PI) * amount;
                    t.localScale = new Vector3(s, s, 1f);
                }
            }, Ease.Linear, 0f, () => { if (t != null) { t.localScale = Vector3.one; } }, t);
        }

        /// <summary>Cancels one tween (no completion callback).</summary>
        public static void Kill(int id)
        {
            if (id <= 0)
            {
                return;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Id == id)
                {
                    _active[i].Dead = true;
                }
            }
        }

        /// <summary>Cancels every tween registered on <paramref name="target"/>.</summary>
        public static void Kill(object target)
        {
            if (target == null)
            {
                return;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                if (ReferenceEquals(_active[i].Target, target))
                {
                    _active[i].Dead = true;
                }
            }
        }

        /// <summary>Live tween count (tests / PerfProbe).</summary>
        public static int ActiveCount => _active.Count;

        private static void EnsureTicker()
        {
            if (_ticker != null)
            {
                return;
            }

            var go = new GameObject("UiTween") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _ticker = go.AddComponent<Ticker>();
        }

        private static void Step(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var e = _active[i];
                if (e.Dead)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                float step = dt;
                if (e.Delay > 0f)
                {
                    e.Delay -= dt;
                    if (e.Delay > 0f)
                    {
                        continue;
                    }

                    step = -e.Delay; // spill the remainder into the first step
                    e.Delay = 0f;
                }

                e.Age += step;
                float k = e.Duration > 0f ? Mathf.Clamp01(e.Age / e.Duration) : 1f;
                try
                {
                    e.Set(Mathf.LerpUnclamped(e.From, e.To, Evaluate(e.Ease, k)));
                }
                catch (MissingReferenceException)
                {
                    e.Dead = true; // the widget was destroyed mid-tween
                }

                if (k >= 1f || e.Dead)
                {
                    _active.RemoveAt(i);
                    if (!e.Dead)
                    {
                        e.Done?.Invoke();
                    }
                }
            }
        }

        private sealed class Ticker : MonoBehaviour
        {
            private void Update() => Step(Time.unscaledDeltaTime);
        }
    }
}
