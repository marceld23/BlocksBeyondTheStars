// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The preview picture of a submitted ship, station/town module or avatar skin (#3262) — what the reviewer sees
    /// first in the inbox. Same technique as the camera item (<see cref="CameraTool"/>): a throwaway clone of the
    /// editor's camera renders into a <see cref="RenderTexture"/> (so neither the editor's panels nor the live frame are
    /// involved) and <see cref="GpuReadback"/> brings it to the CPU — synchronously where allowed, a frame later on
    /// WebGPU. The avatar's picture comes from an <see cref="AvatarPreviewRig"/> the same way. Any failure hands back
    /// null: the creation is what counts, the picture is optional.
    /// </summary>
    public static class EditorPreviewCapture
    {
        /// <summary>16:9, small enough that the PNG stays far below the report's preview cap.</summary>
        public const int Width = 640, Height = 360;

        /// <summary>The 3/4 view a framed preview takes (degrees of yaw).</summary>
        public const float FramedYaw = 35f;

        /// <summary>Renders <paramref name="source"/>'s view — or, with <paramref name="frameCells"/>, a 3/4 view framing
        /// those cells (<see cref="EditorSceneKit.Frame"/>) — and hands the PNG to <paramref name="done"/>.</summary>
        public static IEnumerator FromCamera(Camera source, IReadOnlyCollection<Vector3i> frameCells, int roomW, int roomL, Action<byte[]> done)
        {
            if (source == null)
            {
                done?.Invoke(null);
                yield break;
            }

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "SubmitPreviewRT" };
            rt.Create();
            var camGo = new GameObject("SubmitPreviewCamera");
            camGo.transform.SetParent(source.transform.parent, false); // leaves with the editor if it closes mid-capture
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(source); // clear colour, clip planes, culling mask — not the audio listener
            UrpCameraClone.Sync(source, cam, -1);
            cam.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            if (frameCells != null && frameCells.Count > 0)
            {
                float pitch = 30f;
                EditorSceneKit.Frame(cam.transform, ref pitch, FramedYaw, frameCells, roomW, roomL);
            }

            cam.targetTexture = rt;
            cam.aspect = (float)Width / Height;
            cam.enabled = true;

            yield return new WaitForEndOfFrame(); // the pipeline has drawn the clone into the RT by now
            cam.enabled = false;
            byte[] png = null;
            yield return Read(rt, bytes => png = bytes);

            cam.targetTexture = null;
            UnityEngine.Object.Destroy(camGo);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            done?.Invoke(png);
        }

        /// <summary>Waits for the end of the frame (so a rig's camera has drawn into <paramref name="rt"/>) and hands
        /// <paramref name="rt"/> back as a PNG. The texture stays the caller's.</summary>
        public static IEnumerator FromRenderTexture(RenderTexture rt, Action<byte[]> done)
        {
            if (rt == null)
            {
                done?.Invoke(null);
                yield break;
            }

            yield return new WaitForEndOfFrame();
            yield return Read(rt, done);
        }

        private static IEnumerator Read(RenderTexture rt, Action<byte[]> done)
        {
            Texture2D tex = null;
            yield return GpuReadback.ReadInto(rt, TextureFormat.RGB24, t => tex = t);
            byte[] png = null;
            try
            {
                png = tex != null ? tex.EncodeToPNG() : null;
                if (png == null)
                {
                    Debug.LogWarning("[SubmitPreview] capture failed: readback returned nothing.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SubmitPreview] capture failed: {ex.Message}");
            }
            finally
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }

            done?.Invoke(png);
        }
    }
}
