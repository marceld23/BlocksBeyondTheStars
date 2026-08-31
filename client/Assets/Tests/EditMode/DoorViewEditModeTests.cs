// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the door-identity check from issue #1429: door ids are per world and restart at 1 after
    /// travel, so <see cref="DoorView"/> must treat an id whose record moved (or changed kind/axis/width)
    /// as a DIFFERENT door and rebuild it — reusing the old object left a stale door floating at its old
    /// world's coordinates while the landed ship went doorless.
    /// </summary>
    public sealed class DoorViewEditModeTests
    {
        private static NetDoor Door(float x = 10f, float y = 20f, float z = 30f, string kind = "energy",
            bool axisX = true, float width = 2f, bool open = false)
            => new NetDoor { Id = 1, X = x, Y = y, Z = z, Kind = kind, AxisX = axisX, Width = width, Open = open };

        [Test]
        public void UnchangedRecordIsTheSameDoor()
        {
            var nd = Door();
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.True);
        }

        [Test]
        public void OpenStateIsNotPartOfTheIdentity()
        {
            var nd = Door(open: true);
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.True);
        }

        [Test]
        public void MovedDoorIsADifferentDoor()
        {
            // The #1429 replay: the recycled id 1 now names the ship hatch on the asteroid, far from the
            // old settlement door's coordinates.
            var nd = Door(x: 863f, y: 50f, z: -31f);
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.False);
        }

        [Test]
        public void ChangedKindIsADifferentDoor()
        {
            var nd = Door(kind: "hinge");
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.False);
        }

        [Test]
        public void FlippedAxisIsADifferentDoor()
        {
            var nd = Door(axisX: false);
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.False);
        }

        [Test]
        public void ChangedWidthIsADifferentDoor()
        {
            var nd = Door(width: 3f);
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 2f, true, nd), Is.False);
        }

        [Test]
        public void WidthComparesAgainstTheBuildClamp()
        {
            // Build() clamps the width to at least 1, so a sub-1 wire value must still match the stored 1.
            var nd = Door(width: 0.5f);
            Assert.That(DoorView.SameDoor("energy", new Vector3(10f, 20f, 30f), 1f, true, nd), Is.True);
        }
    }
}
