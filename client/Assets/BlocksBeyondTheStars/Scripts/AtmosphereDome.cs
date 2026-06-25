// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// An additive atmosphere-glow dome (<c>BlocksBeyondTheStars/Atmosphere</c>) that brightens the horizon and
    /// scatters a warm halo around the sun at dawn/dusk on planets with air — so the sky reads like a real
    /// atmosphere instead of a flat fill, without replacing <see cref="Sky"/>'s day/night sky colour. Follows the
    /// camera at "infinity" like the <see cref="Starfield"/> / <see cref="NebulaField"/>; fades out in space, on
    /// airless bodies and inside stations (where <see cref="NebulaField"/> takes over). The shader reads the same
    /// sky globals Sky.cs sets and self-dims at night (the sun colour goes dark), so this just gates the fade.
    /// </summary>
    public sealed class AtmosphereDome : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private Transform _dome;
        private Material _mat;
        private float _brightness; // smoothed 0..1 fade

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Atmosphere");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader);
            _mat.SetFloat("_Brightness", 0f);

            var go = new GameObject("Atmosphere");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = BuildDomeMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _dome = go.transform;
        }

        private void LateUpdate()
        {
            if (_dome == null || Camera == null || Game == null)
            {
                return;
            }

            _dome.SetPositionAndRotation(Camera.transform.position, Quaternion.identity);
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.43f; // just inside the star/nebula domes
            _dome.localScale = new Vector3(r, r, r);

            // Visible only where there IS an atmosphere: a normal planet sky. Off in the space view, on airless
            // bodies and inside an orbital station (those show the nebula/stars instead).
            bool spaceSky = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                            || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;
            float target = spaceSky ? 0f : 1f;
            _brightness = Mathf.MoveTowards(_brightness, target, Time.deltaTime * 0.9f);
            _mat.SetFloat("_Brightness", _brightness);
        }

        /// <summary>A unit UV-sphere dome (positions only — the shader derives the view direction from them).</summary>
        private static Mesh BuildDomeMesh()
        {
            const int rings = 20;
            const int sectors = 40;
            int vCount = (rings + 1) * (sectors + 1);
            var verts = new Vector3[vCount];
            var tris = new int[rings * sectors * 6];

            int vi = 0;
            for (int ring = 0; ring <= rings; ring++)
            {
                float theta = (float)ring / rings * Mathf.PI;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int sec = 0; sec <= sectors; sec++)
                {
                    float phi = (float)sec / sectors * Mathf.PI * 2f;
                    verts[vi++] = new Vector3(sinT * Mathf.Cos(phi), cosT, sinT * Mathf.Sin(phi));
                }
            }

            int ti = 0, stride = sectors + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int sec = 0; sec < sectors; sec++)
                {
                    int a = ring * stride + sec;
                    int b = a + stride;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return mesh;
        }
    }
}
