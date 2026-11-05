// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The suit headlamps' shader globals (#3229–#3234; the maths is in <c>Headlamp.hlsl</c>). One calm LED white for every
    /// lamp, a narrow hotspot with a faint reflector ring and a soft spill, windowed inverse-square falloff. The own lamp is
    /// published by <see cref="PlayerController"/> (from the helmet, after the camera has moved), the other players' lamps by
    /// <see cref="RemotePlayers"/>; <see cref="AllOff"/> darkens both (space, the menu, a world change).
    /// </summary>
    public static class Headlamp
    {
        /// <summary>The lamp's reach (m): the falloff window closes here.</summary>
        public const float Range = 30f;

        /// <summary>Linear intensity: 1 m in front of the visor the hotspot is ~6, at 3 m ~2, at 8 m ~0.3 (before albedo).</summary>
        public const float Intensity = 22f;

        /// <summary>The LED's colour, sRGB: a calm, very slightly cool white (#3229: no halogen yellow).</summary>
        public static readonly Color LedSrgb = new Color(0.93f, 0.97f, 1f, 1f);

        /// <summary>Half angles of the beam (degrees): the spill's outer edge, the hotspot's edge and its core.</summary>
        public const float SpillDegrees = 34f;
        public const float HotspotDegrees = 12f;
        public const float CoreDegrees = 5f;

        /// <summary>The spill's brightness relative to the hotspot.</summary>
        public const float SpillLevel = 0.2f;

        /// <summary>The source radius of the falloff (m): keeps rock right in front of the visor from burning out.</summary>
        public const float SourceRadius = 1.6f;

        private static readonly int PosId = Shader.PropertyToID("_Sc_LampPos");
        private static readonly int DirId = Shader.PropertyToID("_Sc_LampDir");
        private static readonly int ColorId = Shader.PropertyToID("_Sc_LampColor");
        private static readonly int ShapeId = Shader.PropertyToID("_Sc_LampShape");
        private static readonly int RemotePosId = Shader.PropertyToID("_Sc_RLampPos");
        private static readonly int RemoteDirId = Shader.PropertyToID("_Sc_RLampDir");
        private static readonly int RemoteCountId = Shader.PropertyToID("_Sc_RLampCount");

        private static readonly Vector4[] RemotePos = new Vector4[HeadlampRules.MaxRemoteLamps];
        private static readonly Vector4[] RemoteDir = new Vector4[HeadlampRules.MaxRemoteLamps];

        /// <summary>The own lamp is lit this frame (eye adaptation, motes, the volumetric beam and the shadows read it).</summary>
        public static bool LocalLit { get; private set; }

        /// <summary>How many other players' lamps the shaders get this frame.</summary>
        public static int RemoteCount { get; private set; }

        /// <summary>Any lamp lit — the own or another player's.</summary>
        public static bool AnyLit => LocalLit || RemoteCount > 0;

        /// <summary>The LED colour × intensity as uploaded (linear when the project renders linear).</summary>
        private static Color LedUpload(float a)
        {
            var c = ShaderColor.Srgb(LedSrgb) * Intensity;
            c.a = a;
            return c;
        }

        /// <summary>The beam shape and the arrays' size. The arrays are uploaded at full length on the first call: Unity
        /// fixes a global array's size on its first set.</summary>
        private static void PublishShape()
        {
            Shader.SetGlobalVector(ShapeId, new Vector4(
                Mathf.Cos(HotspotDegrees * Mathf.Deg2Rad), Mathf.Cos(CoreDegrees * Mathf.Deg2Rad), SpillLevel, SourceRadius * SourceRadius));
            Shader.SetGlobalVectorArray(RemotePosId, RemotePos);
            Shader.SetGlobalVectorArray(RemoteDirId, RemoteDir);
        }

        /// <summary>Lights the own lamp at <paramref name="position"/> along <paramref name="axis"/> (a unit vector).</summary>
        public static void PublishLocal(Vector3 position, Vector3 axis)
        {
            PublishShape();
            Shader.SetGlobalVector(PosId, new Vector4(position.x, position.y, position.z, Range));
            Shader.SetGlobalVector(DirId, new Vector4(axis.x, axis.y, axis.z, Mathf.Cos(SpillDegrees * Mathf.Deg2Rad)));
            Shader.SetGlobalColor(ColorId, LedUpload(1f));
            LocalLit = true;
        }

        /// <summary>Darkens the own lamp; the colour stays for the other players' lamps.</summary>
        public static void ClearLocal()
        {
            Shader.SetGlobalColor(ColorId, LedUpload(0f));
            LocalLit = false;
        }

        /// <summary>The other players' lit lamps this frame (at most <see cref="HeadlampRules.MaxRemoteLamps"/>, nearest
        /// first): positions and unit axes, paired by index.</summary>
        public static void PublishRemotes(IReadOnlyList<Vector3> positions, IReadOnlyList<Vector3> axes)
        {
            int n = Mathf.Min(positions.Count, HeadlampRules.MaxRemoteLamps);
            for (int i = 0; i < HeadlampRules.MaxRemoteLamps; i++)
            {
                if (i < n)
                {
                    var p = positions[i];
                    var d = axes[i];
                    RemotePos[i] = new Vector4(p.x, p.y, p.z, Range);
                    RemoteDir[i] = new Vector4(d.x, d.y, d.z, 0f);
                }
                else
                {
                    RemotePos[i] = Vector4.zero;
                    RemoteDir[i] = Vector4.zero;
                }
            }

            PublishShape();
            Shader.SetGlobalFloat(RemoteCountId, n);
            if (!LocalLit)
            {
                Shader.SetGlobalVector(DirId, new Vector4(0f, 0f, 1f, Mathf.Cos(SpillDegrees * Mathf.Deg2Rad))); // the spill edge is shared
                Shader.SetGlobalColor(ColorId, LedUpload(0f));
            }

            RemoteCount = n;
        }

        /// <summary>Every lamp dark: the own one and the other players' (space, the menu, leaving a world).</summary>
        public static void AllOff()
        {
            ClearLocal();
            Shader.SetGlobalFloat(RemoteCountId, 0f);
            RemoteCount = 0;
        }

        /// <summary>The lamp's position on the helmet from the eye (first person): a little right of and above it.</summary>
        public static Vector3 HelmetFromEye(Transform eye)
        {
            var o = HeadlampRules.HelmetOffset;
            return eye.position + (eye.right * o.X) + (eye.up * o.Y) + (eye.forward * o.Z);
        }

        /// <summary>The beam axis from a lamp at <paramref name="lampPos"/>: toward the point <see cref="HeadlampRules.AimDistance"/>
        /// ahead of the eye, so the hotspot meets the crosshair at a working distance.</summary>
        public static Vector3 AimFrom(Vector3 lampPos, Vector3 eyePos, Vector3 eyeForward)
        {
            var axis = eyePos + (eyeForward * HeadlampRules.AimDistance) - lampPos;
            return axis.sqrMagnitude > 1e-6f ? axis.normalized : eyeForward;
        }
    }
}
