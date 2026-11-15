// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The touch layout's Unity-free rules (TouchHudLayout, TouchReservation — pinned headless by TouchHudLayoutTests) mirror
    /// a handful of HUD constants; these pin the mirrors against the Unity side, and check that HudZones follows a published
    /// touch reservation: the map part moves under the touch row on a 16:9 tablet and stays put on the desktop.
    /// </summary>
    public sealed class TouchHudLayoutEditModeTests
    {
        private const float Eps = 0.01f;

        [TearDown]
        public void TearDown() => HudZones.SetTouch(TouchReservation.None);

        [Test]
        public void Mirrors_MatchTheHudZones()
        {
            Assert.That(TouchHudLayout.Gap, Is.EqualTo(HudZones.Gap));
            Assert.That(TouchHudLayout.RightX, Is.EqualTo(HudZones.RightX));
            Assert.That(TouchHudLayout.RightEdge, Is.EqualTo(HudZones.RightEdge));
            Assert.That(TouchHudLayout.MapTop, Is.EqualTo(HudZones.RadarTop));
            Assert.That(TouchHudLayout.MapTop, Is.EqualTo(HudRightColumn.CompassY), "the compass and the radar share zone C's top");
            Assert.That(TouchHudLayout.PlanetCardW, Is.EqualTo(PlanetOverviewCard.CardWidth));
            Assert.That(TouchLayout.HudRefW, Is.EqualTo(UiKit.HudRefW));
            Assert.That(TouchLayout.HudRefH, Is.EqualTo(UiKit.HudRefH));
        }

        [Test]
        public void MapPartHeights_MatchTheHeadlessFixture()
        {
            // TouchHudLayoutTests: 298 on foot (compass, captions, clock with its chip row, playtime), 212 in space.
            Assert.That(HudRightColumn.PlaytimeY(true) + HudRightColumn.PlaytimeH - HudRightColumn.CompassY, Is.EqualTo(298f).Within(Eps));
            Assert.That(HudZones.SpaceRadarBottom - HudZones.MapTop, Is.EqualTo(212f).Within(Eps));
        }

        [Test]
        public void Desktop_LeavesTheZonesWhereTheyWere()
        {
            HudZones.SetTouch(TouchReservation.None);
            Assert.That(HudZones.MapShift, Is.EqualTo(0f));
            Assert.That(HudZones.SpaceRadarBottom, Is.EqualTo(222f).Within(Eps));
            Assert.That(float.IsPositiveInfinity(HudZones.StackLimit(HudZones.RightX, HudZones.RightEdge)), Is.True);
        }

        [Test]
        public void Tablet16x9_MovesTheMapPartUnderTheTouchRow_AndCapsTheColumn()
        {
            HudZones.SetTouch(TouchReservation.ForScreen(TouchCluster.Flight, 1920f, 1080f, 1f));
            Assert.That(HudZones.MapTop, Is.EqualTo(118.4f).Within(Eps), "under ≡ (bottom 110.4) plus the gap");
            Assert.That(HudZones.SpaceRadarBottom, Is.EqualTo(330.4f).Within(Eps));
            Assert.That(HudZones.StackLimit(HudZones.RightX, HudZones.RightEdge), Is.EqualTo(563.2f).Within(Eps),
                "TARGET's top (579.2) less the thumb margin");
        }

        [Test]
        public void FlightStrip_IsTheInstrumentsAboveTheSystemsBar()
        {
            var strip = HudZones.FlightStrip(UiKit.HudRefW, UiKit.HudRefH);
            Assert.That(strip.X, Is.EqualTo(498f).Within(Eps));
            Assert.That(strip.Y, Is.EqualTo(682f).Within(Eps));
            Assert.That(strip.Right, Is.EqualTo(1038f).Within(Eps));
            Assert.That(strip.Bottom, Is.EqualTo(UiKit.HudRefH).Within(Eps));
        }
    }
}
