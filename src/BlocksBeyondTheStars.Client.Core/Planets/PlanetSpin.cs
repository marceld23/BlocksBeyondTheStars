// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How far to turn a planet sphere about its axis so its lit side is its real day side (#2177). The local time at
    /// map longitude u is <c>timeOfDay + u</c> (the surface sky and the landing-pad map's day/night band use the same
    /// rule), so local noon sits at <c>u = 0.5 − timeOfDay</c>; that meridian must face the star.
    /// <para>Angles are azimuths in the sphere's parent XZ plane, <c>atan2(z, x)</c> in degrees. A Unity rotation of
    /// ψ degrees about +Y moves a direction's azimuth from φ to φ − ψ. <paramref name="seamDeg"/> and
    /// <paramref name="direction"/> describe the mesh: longitude u points at azimuth <c>seam + direction·u·360</c>
    /// (measured from the sphere mesh once by the caller).</para>
    /// </summary>
    public static class PlanetSpin
    {
        /// <summary>The map longitude (0..1) at local noon.</summary>
        public static double NoonLongitude(double timeOfDay)
        {
            double u = 0.5 - timeOfDay;
            return u - Math.Floor(u);
        }

        /// <summary>The yaw (degrees about +Y, 0..360) that turns the noon meridian toward the star at azimuth
        /// <paramref name="sunAzimuthDeg"/>.</summary>
        public static double YawDegrees(double timeOfDay, double sunAzimuthDeg, double seamDeg, double direction)
        {
            double noonAzimuth = seamDeg + Math.Sign(direction == 0 ? 1 : direction) * NoonLongitude(timeOfDay) * 360.0;
            double yaw = noonAzimuth - sunAzimuthDeg;
            yaw %= 360.0;
            return yaw < 0 ? yaw + 360.0 : yaw;
        }

        /// <summary>The azimuth a mesh longitude points at after a yaw — the inverse used by the tests and the
        /// pad-on-sphere placement.</summary>
        public static double AzimuthOf(double u, double yawDeg, double seamDeg, double direction)
        {
            double a = seamDeg + Math.Sign(direction == 0 ? 1 : direction) * u * 360.0 - yawDeg;
            a %= 360.0;
            return a < 0 ? a + 360.0 : a;
        }
    }
}
