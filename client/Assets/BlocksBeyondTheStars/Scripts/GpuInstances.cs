// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The instance data of one instanced draw, kept on the graphics card (#4046 part b). The near drawers and the ground
    /// detail — crown clusters, plant bodies, grass clumps, the grass carpet with its flowers, the tufts — bake their
    /// instances once per chunk build, but handed every batch's matrices and per-instance vectors to the engine again
    /// each frame (<c>Graphics.DrawMeshInstanced</c>): measured on the reference laptop on High, 2.6 ms of main-thread
    /// time a frame for about 130 chunks in reach. Here a batch uploads its instances once into a structured buffer and a
    /// frame only names that buffer (<c>Graphics.RenderMeshPrimitives</c>); the shaders read the instance in their
    /// procedural function (<c>Shaders/GpuInstances.hlsl</c>). The picture is meant to be the same, with two differences
    /// that are gains: a batch is no longer capped at 1023 instances by the engine (the drawers keep their batches all
    /// the same), and a draw is culled as a whole by the box of its instances, per camera and per shadow cascade, where
    /// the array draw was never culled.
    /// <para>
    /// <b>Fallback.</b> A drawer keeps its arrays and draws them as before whenever <see cref="Active"/> is false: on a
    /// graphics API without structured buffers in a vertex shader, in the browser (where these drawers do not run
    /// anyway), or with the switch off (<see cref="Enabled"/> — PerfProbe's <c>instances=arrays</c>, which measures the
    /// two ways against each other in one build).
    /// </para>
    /// Desktop only (ADR 0021). Main thread only. Whoever owns a batch releases it (<see cref="Release"/>) when the
    /// chunk goes — a buffer is not garbage collected.
    /// </summary>
    public sealed class GpuInstances
    {
        /// <summary>Bytes per instance: two matrices and four vectors — <c>BbtsGpuInstance</c> in the shader include.</summary>
        public const int Stride = 192;

        /// <summary>The engine's cap of one array draw; the scratch array is this long, and so is the longest batch.</summary>
        public const int MaxBatch = 1023;

        /// <summary>World units a shader may move a vertex beyond its mesh (wind sway, flutter): added to every draw's box.</summary>
        private const float MotionSlack = 1f;

        private static readonly int BufferId = Shader.PropertyToID("_BbtsInstances");
        private static readonly Instance[] Scratch = new Instance[MaxBatch];
        private static int _supported = -1;

        private GraphicsBuffer _buffer;
        private MaterialPropertyBlock _props;
        private Vector3 _min, _max;
        private float _maxScale;
        private Mesh _boundsMesh;
        private Bounds _bounds;

        /// <summary>The switch: off, every drawer hands its arrays over each frame as before #4046 part b. Off from the
        /// start with the command-line option <c>-instanceArrays</c> — the way out for a machine whose graphics driver
        /// draws the buffers wrong.</summary>
        public static bool Enabled { get; set; } = !HasStartOption("-instanceArrays");

        /// <summary>True when the drawers draw from buffers on the graphics card.</summary>
        public static bool Active => Enabled && Supported;

        /// <summary>Whether this machine can: a desktop graphics API with structured buffers a vertex shader may read.</summary>
        public static bool Supported
        {
            get
            {
                if (_supported < 0)
                {
                    var api = SystemInfo.graphicsDeviceType;
                    bool desktopApi = api == GraphicsDeviceType.Direct3D11 || api == GraphicsDeviceType.Direct3D12
                        || api == GraphicsDeviceType.Vulkan || api == GraphicsDeviceType.OpenGLCore || api == GraphicsDeviceType.Metal;
                    _supported = desktopApi && Application.platform != RuntimePlatform.WebGLPlayer && SystemInfo.supportsInstancing
                        && SystemInfo.supportsComputeShaders && SystemInfo.maxComputeBufferInputsVertex >= 1 ? 1 : 0;
                    Debug.Log($"[Look] instance data of the near drawers: {(_supported == 1 && Enabled ? "buffers on the graphics card" : "arrays each frame")} ({api}, switch {(Enabled ? "on" : "off")}).");
                }

                return _supported == 1;
            }
        }

        /// <summary>Instances uploaded since the start (diagnostics: PerfProbe reports it).</summary>
        public static long UploadedInstances { get; private set; }

        /// <summary>Buffer draws submitted since the start (diagnostics).</summary>
        public static long BufferDraws { get; private set; }

        /// <summary>How many instances the buffer holds.</summary>
        public int Count { get; private set; }

        /// <summary>Uploads a batch: its matrices and up to four per-instance vectors, in the order the shader's procedural
        /// function assigns them (a list left out, or shorter than the batch, reads as zero).</summary>
        public void Upload(List<Matrix4x4> matrices, List<Vector4> a, List<Vector4> b = null, List<Vector4> c = null, List<Vector4> d = null)
        {
            int count = Mathf.Min(matrices.Count, MaxBatch);
            for (int i = 0; i < count; i++)
            {
                Scratch[i].ObjectToWorld = matrices[i];
            }

            Finish(count, a, b, c, d);
        }

        /// <summary>As <see cref="Upload(List{Matrix4x4}, List{Vector4}, List{Vector4}, List{Vector4}, List{Vector4})"/> for
        /// a batch that keeps its matrices in an array.</summary>
        public void Upload(Matrix4x4[] matrices, int count, List<Vector4> a, List<Vector4> b = null, List<Vector4> c = null, List<Vector4> d = null)
        {
            count = Mathf.Min(Mathf.Min(count, matrices.Length), MaxBatch);
            for (int i = 0; i < count; i++)
            {
                Scratch[i].ObjectToWorld = matrices[i];
            }

            Finish(count, a, b, c, d);
        }

        /// <summary>Submits the batch for this frame: <paramref name="mesh"/> once per uploaded instance.</summary>
        public void Draw(Mesh mesh, Material material, ShadowCastingMode cast, int layer)
        {
            if (Count == 0 || _buffer == null || mesh == null || material == null)
            {
                return;
            }

            if (!ReferenceEquals(_boundsMesh, mesh))
            {
                // The box of everything the draw can touch: every instance's place, plus how far its mesh reaches from
                // there at the instance's scale, plus the shader's own motion.
                _boundsMesh = mesh;
                var mb = mesh.bounds;
                float reach = ((mb.extents.magnitude + mb.center.magnitude) * _maxScale) + MotionSlack;
                var pad = new Vector3(reach, reach, reach);
                _bounds = new Bounds((_min + _max) * 0.5f, (_max - _min) + (pad * 2f));
            }

            var rp = new RenderParams(material)
            {
                worldBounds = _bounds,
                matProps = _props,
                shadowCastingMode = cast,
                receiveShadows = true,
                layer = layer,
                lightProbeUsage = LightProbeUsage.Off,
            };
            Graphics.RenderMeshPrimitives(rp, mesh, 0, Count);
            BufferDraws++;
        }

        /// <summary>Gives the buffer back. Safe to call twice; the batch may upload again afterwards.</summary>
        public void Release()
        {
            _buffer?.Release();
            _buffer = null;
            Count = 0;
        }

        private void Finish(int count, List<Vector4> a, List<Vector4> b, List<Vector4> c, List<Vector4> d)
        {
            Count = count;
            if (count == 0)
            {
                return;
            }

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            float maxScaleSq = 0f;
            for (int i = 0; i < count; i++)
            {
                ref var inst = ref Scratch[i];
                var m = inst.ObjectToWorld;
                Matrix4x4.Inverse3DAffine(m, ref inst.WorldToObject);
                inst.A = a != null && i < a.Count ? a[i] : default;
                inst.B = b != null && i < b.Count ? b[i] : default;
                inst.C = c != null && i < c.Count ? c[i] : default;
                inst.D = d != null && i < d.Count ? d[i] : default;

                var p = new Vector3(m.m03, m.m13, m.m23);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                float sx = (m.m00 * m.m00) + (m.m10 * m.m10) + (m.m20 * m.m20);
                float sy = (m.m01 * m.m01) + (m.m11 * m.m11) + (m.m21 * m.m21);
                float sz = (m.m02 * m.m02) + (m.m12 * m.m12) + (m.m22 * m.m22);
                maxScaleSq = Mathf.Max(maxScaleSq, Mathf.Max(sx, Mathf.Max(sy, sz)));
            }

            _min = min;
            _max = max;
            _maxScale = Mathf.Sqrt(maxScaleSq);
            _boundsMesh = null; // the box is worked out again at the next draw

            if (_buffer == null || _buffer.count < count)
            {
                _buffer?.Release();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Min(MaxBatch, Mathf.NextPowerOfTwo(Mathf.Max(count, 16))), Stride);
                _props ??= new MaterialPropertyBlock();
                _props.SetBuffer(BufferId, _buffer);
            }

            _buffer.SetData(Scratch, 0, 0, count);
            UploadedInstances += count;
        }

        private static bool HasStartOption(string option)
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return false; // a browser has no command line
            }

            foreach (string arg in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, option, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One instance as the shader reads it (<c>BbtsGpuInstance</c>): matrices in the engine's column order,
        /// which is the shader's default for a matrix in a buffer.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance
        {
            public Matrix4x4 ObjectToWorld;
            public Matrix4x4 WorldToObject;
            public Vector4 A;
            public Vector4 B;
            public Vector4 C;
            public Vector4 D;
        }
    }
}
