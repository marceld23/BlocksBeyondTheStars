// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A shared uGUI layer for world-anchored floating labels (player/NPC nameplates) — the modern
    /// replacement for the per-component IMGUI <c>GUI.Label</c> projections. Callers push labels each
    /// frame from <c>LateUpdate</c> via <see cref="World"/>; the layer pools <see cref="Text"/> elements
    /// (with a drop-shadow for legibility) and hides any untouched ones at end-of-frame, so the result
    /// is correct regardless of caller execution order. Lives on a dedicated DPI-scaled overlay canvas
    /// that sits just below the HUD.
    /// </summary>
    public sealed class ScreenLabelLayer : MonoBehaviour
    {
        private static ScreenLabelLayer _instance;

        private RectTransform _root;
        private readonly List<Entry> _pool = new List<Entry>();
        private int _used;
        private int _frame = -1;

        private sealed class Entry
        {
            public RectTransform Rt;
            public Text Main;
            public Text Shadow;
        }

        /// <summary>Lazily creates (or returns) the singleton layer.</summary>
        public static ScreenLabelLayer Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                var canvas = UiKit.CreateCanvas("Nameplate Layer");
                canvas.sortingOrder = 8; // below the HUD (10) and menus
                _instance = canvas.gameObject.AddComponent<ScreenLabelLayer>();
                _instance._root = canvas.GetComponent<RectTransform>();
                return _instance;
            }
        }

        private void OnEnable() => StartCoroutine(FinalizeLoop());

        /// <summary>Pushes a label anchored to a world position (skipped if behind the camera).
        /// When <paramref name="fadeEnd"/> &gt; 0 the label fades out with distance: fully opaque up to
        /// <paramref name="fadeStart"/>, linearly down to zero at <paramref name="fadeEnd"/>, and dropped
        /// entirely beyond it — so names only read up close instead of across the whole world.</summary>
        public void World(Camera cam, Vector3 world, string text, Color color, bool bold = false,
                          float fadeStart = 0f, float fadeEnd = 0f)
        {
            if (cam == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return; // behind the camera
            }

            float alpha = 1f;
            if (fadeEnd > 0f)
            {
                float dist = Vector3.Distance(cam.transform.position, world);
                if (dist >= fadeEnd)
                {
                    return; // too far away to show at all
                }

                if (dist > fadeStart && fadeEnd > fadeStart)
                {
                    alpha = 1f - (dist - fadeStart) / (fadeEnd - fadeStart);
                }
            }

            BeginFrameIfNeeded();
            var e = Acquire();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, new Vector2(sp.x, sp.y), null, out var local);
            e.Rt.anchoredPosition = local;
            e.Main.text = e.Shadow.text = text;
            var main = color;
            main.a *= alpha;
            e.Main.color = main;
            // The drop-shadow must fade with the text or it lingers as an orphan smudge.
            var shadow = e.Shadow.color;
            shadow.a = 0.7f * alpha;
            e.Shadow.color = shadow;
            e.Main.fontStyle = e.Shadow.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            e.Rt.gameObject.SetActive(true);
        }

        private void BeginFrameIfNeeded()
        {
            if (_frame == Time.frameCount)
            {
                return;
            }

            _frame = Time.frameCount;
            _used = 0;
        }

        private Entry Acquire()
        {
            if (_used < _pool.Count)
            {
                return _pool[_used++];
            }

            var go = new GameObject("Nameplate", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(220f, 22f);

            var shadow = MakeText(rt, new Vector2(1f, -1f), new Color(0f, 0f, 0f, 0.7f));
            var main = MakeText(rt, Vector2.zero, UiKit.TextCol);

            var e = new Entry { Rt = rt, Main = main, Shadow = shadow };
            _pool.Add(e);
            _used++;
            return e;
        }

        private static Text MakeText(RectTransform parent, Vector2 offset, Color color)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offset;
            rt.offsetMax = offset;
            var t = go.AddComponent<Text>();
            t.font = UiKit.Font;
            t.fontSize = 16;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Runs after all Update/LateUpdate/OnGUI each frame and hides labels nobody refreshed.</summary>
        private IEnumerator FinalizeLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                for (int i = _used; i < _pool.Count; i++)
                {
                    if (_pool[i].Rt.gameObject.activeSelf)
                    {
                        _pool[i].Rt.gameObject.SetActive(false);
                    }
                }

                _used = 0; // next producer call starts a fresh frame
            }
        }
    }
}
