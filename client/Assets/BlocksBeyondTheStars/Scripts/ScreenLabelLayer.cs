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

        // Health/progress bars (#692) share the layer's projection + pooling lifecycle, so entity health
        // bars and nameplates stay in the same visual system (and one distance-fade convention).
        private readonly List<BarEntry> _barPool = new List<BarEntry>();
        private int _barUsed;

        // #2605: NPC speech bubbles have their own small pool — wrapped text, growing upward from the anchor, so a long line
        // no longer runs off as one strip across the nameplate.
        private const float BubbleWidth = 360f;
        private const float BubbleLineHeight = 22f;
        private readonly List<Entry> _bubblePool = new List<Entry>();
        private int _bubbleUsed;

        private sealed class Entry
        {
            public RectTransform Rt;
            public Text Main;
            public Text Shadow;
        }

        private sealed class BarEntry
        {
            public RectTransform Rt;
            public Image Track;
            public Image Fill;
        }

        // #2531: terrain occlusion for labels pushed with an `occludeIn` world. The labels themselves are pooled by
        // push order (no stable identity), so the line-of-sight results live in a small cache matched by position: each
        // occluding push claims the nearest unclaimed entry within OcclMatchRadius (a walking NPC moves a few cm per
        // frame), re-tests it about 5 times a second and fades its visibility instead of popping. Entries nobody
        // claims for OcclForgetSeconds are dropped. Plain struct array — no allocation per frame.
        private const float OcclMatchRadius = 1.5f;
        private const float OcclForgetSeconds = 1f;
        private const float OcclTestMin = 0.18f;   // re-test interval (staggered so labels don't all test on one frame)
        private const float OcclTestMax = 0.24f;
        private const float OcclFadePerSecond = 7f; // ~0.15 s fade in/out
        private const float OcclEndSlack = 0.75f;  // ignore blocks this close to the label: an anchor may sit in a lintel/ceiling
        private const int OcclMaxSteps = 400;      // voxel steps per test (a giant's bar reads from ~220 m)

        private struct Occl
        {
            public Vector3 Pos;     // where the label was last pushed
            public float NextTest;  // unscaled time of the next line-of-sight test
            public float Vis;       // faded visibility 0..1
            public bool Hidden;     // last test: terrain between camera and label
            public int Frame;       // frame that claimed the entry (one label per entry per frame)
            public float Seen;      // unscaled time of the last claim
        }

        private Occl[] _occl = new Occl[32];
        private int _occlCount;

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
        /// entirely beyond it — so names only read up close instead of across the whole world.
        /// #2531: with <paramref name="occludeIn"/> (the live world) the label hides while solid terrain stands between
        /// the camera and it — NPC plates, hints, health bars. Leave it null for things that are MEANT to show through
        /// walls (beacons, beams, thermal contacts, ore markers, other players' names).
        /// #2601: <paramref name="scale"/> enlarges a single glyph marker (a villager's wish "!"/"?") — 1 for every label.</summary>
        public void World(Camera cam, Vector3 world, string text, Color color, bool bold = false,
                          float fadeStart = 0f, float fadeEnd = 0f, GameBootstrap occludeIn = null, float scale = 1f)
        {
            // Under a cutscene no nameplate is pushed (#2461), so the end-of-frame sweep hides every label at once — #2496: nor
            // while a scene hides the HUD (GameBootstrap.HudHidden).
            if (cam == null || string.IsNullOrEmpty(text) || GameBootstrap.HudHidden)
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

            if (occludeIn != null)
            {
                alpha *= Visibility(occludeIn, cam.transform.position, world);
                if (alpha <= 0.001f)
                {
                    return; // behind a hill or a wall (#2531)
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
            e.Rt.localScale = scale == 1f ? Vector3.one : new Vector3(scale, scale, 1f); // pooled: reset on every push
            e.Rt.gameObject.SetActive(true);
        }

        /// <summary>
        /// #2605: pushes an NPC speech bubble anchored to a world position — like <see cref="World"/> (the same per-frame
        /// contract, distance fade and <paramref name="occludeIn"/> terrain occlusion), but the text wraps at
        /// <see cref="BubbleWidth"/> and grows UPWARD from the anchor (bottom pivot), so the nameplate under it stays free.
        /// Its own small pool; shown verbatim (no rich-text tags — the line may come from an AI backend).
        /// </summary>
        public void Bubble(Camera cam, Vector3 world, string text, Color color,
                           float fadeStart = 0f, float fadeEnd = 0f, GameBootstrap occludeIn = null)
        {
            if (cam == null || string.IsNullOrEmpty(text) || GameBootstrap.HudHidden)
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
                    return;
                }

                if (dist > fadeStart && fadeEnd > fadeStart)
                {
                    alpha = 1f - (dist - fadeStart) / (fadeEnd - fadeStart);
                }
            }

            if (occludeIn != null)
            {
                alpha *= Visibility(occludeIn, cam.transform.position, world);
                if (alpha <= 0.001f)
                {
                    return; // behind a hill or a wall (#2531)
                }
            }

            BeginFrameIfNeeded();
            var e = AcquireBubble();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, new Vector2(sp.x, sp.y), null, out var local);
            e.Rt.anchoredPosition = local;
            if (!ReferenceEquals(e.Main.text, text))
            {
                e.Main.text = e.Shadow.text = text;
            }

            var main = color;
            main.a *= alpha;
            e.Main.color = main;
            var shadow = e.Shadow.color;
            shadow.a = 0.7f * alpha;
            e.Shadow.color = shadow;
            e.Rt.gameObject.SetActive(true);
        }

        /// <summary>Pushes a horizontal bar (track + fill) anchored to a world position — the world-space
        /// sibling of <see cref="HudUi"/>'s vitals. Same per-frame contract and distance fade as
        /// <see cref="World"/>; callers re-push every frame from <c>LateUpdate</c>. <paramref name="occludeIn"/> as in
        /// <see cref="World"/> (#2531).</summary>
        public void WorldBar(Camera cam, Vector3 world, float frac, Color color,
                             float width = 46f, float fadeStart = 0f, float fadeEnd = 0f, GameBootstrap occludeIn = null)
        {
            if (cam == null || GameBootstrap.HudHidden) // no health bars over a cutscene either (#2461, #2496)
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
                    return;
                }

                if (dist > fadeStart && fadeEnd > fadeStart)
                {
                    alpha = 1f - (dist - fadeStart) / (fadeEnd - fadeStart);
                }
            }

            if (occludeIn != null)
            {
                alpha *= Visibility(occludeIn, cam.transform.position, world);
                if (alpha <= 0.001f)
                {
                    return; // behind a hill or a wall (#2531)
                }
            }

            BeginFrameIfNeeded();
            var e = AcquireBar();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, new Vector2(sp.x, sp.y), null, out var local);
            e.Rt.anchoredPosition = local;
            e.Rt.sizeDelta = new Vector2(width, 5f);
            var track = new Color(0.03f, 0.07f, 0.13f, 0.9f * alpha); // the HUD vitals' dark track
            e.Track.color = track;
            var fill = color;
            fill.a *= alpha;
            e.Fill.color = fill;
            e.Fill.fillAmount = Mathf.Clamp01(frac);
            e.Rt.gameObject.SetActive(true);
        }

        /// <summary>The faded line-of-sight visibility (0..1) of a label at <paramref name="target"/> seen from
        /// <paramref name="eye"/> (#2531). Throttled per label through the position-matched cache; a label seen for the
        /// first time is tested at once, so it never flashes up before hiding. Always 1 in the space view (the chunks
        /// in memory are the planet's, not what the camera looks through) or before the world is wired.</summary>
        private float Visibility(GameBootstrap game, Vector3 eye, Vector3 target)
        {
            var world = game.World;
            var content = game.Content;
            if (world == null || content == null || game.SpaceViewActive)
            {
                return 1f;
            }

            float now = Time.unscaledTime;
            int frame = Time.frameCount;
            int best = -1;
            float bestSq = OcclMatchRadius * OcclMatchRadius;
            for (int i = 0; i < _occlCount; i++)
            {
                if (now - _occl[i].Seen > OcclForgetSeconds)
                {
                    _occl[i] = _occl[--_occlCount]; // swap-remove a label nobody pushed lately
                    i--;
                    continue;
                }

                if (_occl[i].Frame == frame)
                {
                    continue; // already claimed by another label this frame
                }

                float sq = (_occl[i].Pos - target).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }

            if (best < 0)
            {
                if (_occlCount == _occl.Length)
                {
                    System.Array.Resize(ref _occl, _occl.Length * 2); // rare: more occluding labels than ever before
                }

                best = _occlCount++;
                bool hidden = Blocked(world, content, eye, target);
                _occl[best] = new Occl
                {
                    Hidden = hidden,
                    Vis = hidden ? 0f : 1f,
                    NextTest = now + Random.Range(OcclTestMin, OcclTestMax),
                };
            }

            ref var o = ref _occl[best];
            o.Pos = target;
            o.Frame = frame;
            o.Seen = now;
            if (now >= o.NextTest)
            {
                o.Hidden = Blocked(world, content, eye, target);
                o.NextTest = now + Random.Range(OcclTestMin, OcclTestMax);
            }

            o.Vis = Mathf.MoveTowards(o.Vis, o.Hidden ? 0f : 1f, Time.unscaledDeltaTime * OcclFadePerSecond);
            return o.Vis;
        }

        /// <summary>Whether a block that fills its cell sits on the segment eye → target (#2531): a voxel DDA over the
        /// client's streamed chunks — exact and independent of colliders. Air, see-through blocks (glass, water, force
        /// fields), plants, leaves and slim props never hide a label — the same "doesn't seal the face behind it" rule the
        /// chunk mesher culls by. The camera's own cell and the last <see cref="OcclEndSlack"/> before the label are
        /// skipped, so a camera grazing a wall or an anchor tucked into a door lintel doesn't count. Unstreamed chunks
        /// read as air (visible).</summary>
        private static bool Blocked(ClientWorld world,
                                    BlocksBeyondTheStars.Shared.Content.GameContent content, Vector3 eye, Vector3 target)
        {
            var delta = target - eye;
            float limit = delta.magnitude - OcclEndSlack;
            if (limit <= 0f)
            {
                return false;
            }

            var dir = delta.normalized;
            int x = Mathf.FloorToInt(eye.x), y = Mathf.FloorToInt(eye.y), z = Mathf.FloorToInt(eye.z);
            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - eye.x) : (eye.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - eye.y) : (eye.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - eye.z) : (eye.z - z)) * invz;

            for (int i = 0; i < OcclMaxSteps; i++)
            {
                // Step first: the camera's own cell never occludes.
                float t;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }

                if (t >= limit)
                {
                    return false;
                }

                if (!ChunkMesher.NeighbourExposesOpaqueFace(content, world.GetBlock(x, y, z)))
                {
                    return true;
                }
            }

            return false;
        }

        private void BeginFrameIfNeeded()
        {
            if (_frame == Time.frameCount)
            {
                return;
            }

            _frame = Time.frameCount;
            _used = 0;
            _barUsed = 0;
            _bubbleUsed = 0;
        }

        /// <summary>#2605: a pooled speech bubble — <see cref="BubbleWidth"/> wide, pivot at its bottom centre; the wrapped
        /// lines sit on the bottom edge and overflow upward.</summary>
        private Entry AcquireBubble()
        {
            if (_bubbleUsed < _bubblePool.Count)
            {
                return _bubblePool[_bubbleUsed++];
            }

            var go = new GameObject("SpeechBubble", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(BubbleWidth, BubbleLineHeight);

            var shadow = MakeText(rt, new Vector2(1f, -1f), new Color(0f, 0f, 0f, 0.7f));
            var main = MakeText(rt, Vector2.zero, UiKit.TextCol);
            foreach (var t in new[] { shadow, main })
            {
                t.alignment = TextAnchor.LowerCenter;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.supportRichText = false; // the line may come from an AI backend — shown verbatim
            }

            var e = new Entry { Rt = rt, Main = main, Shadow = shadow };
            _bubblePool.Add(e);
            _bubbleUsed++;
            return e;
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

        private BarEntry AcquireBar()
        {
            if (_barUsed < _barPool.Count)
            {
                return _barPool[_barUsed++];
            }

            var go = new GameObject("WorldBar", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(46f, 5f);

            var track = go.AddComponent<Image>();
            track.sprite = UiKit.SolidSprite;
            track.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(rt, false);
            var fr = fillGo.GetComponent<RectTransform>();
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(0.5f, 0.5f);
            fr.offsetMax = new Vector2(-0.5f, -0.5f);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = UiKit.SolidSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;

            var e = new BarEntry { Rt = rt, Track = track, Fill = fill };
            _barPool.Add(e);
            _barUsed++;
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

                for (int i = _barUsed; i < _barPool.Count; i++)
                {
                    if (_barPool[i].Rt.gameObject.activeSelf)
                    {
                        _barPool[i].Rt.gameObject.SetActive(false);
                    }
                }

                for (int i = _bubbleUsed; i < _bubblePool.Count; i++)
                {
                    if (_bubblePool[i].Rt.gameObject.activeSelf)
                    {
                        _bubblePool[i].Rt.gameObject.SetActive(false);
                    }
                }

                _used = 0; // next producer call starts a fresh frame
                _barUsed = 0;
                _bubbleUsed = 0;
            }
        }
    }
}
