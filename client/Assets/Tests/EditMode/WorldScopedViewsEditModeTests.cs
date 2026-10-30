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
    /// Pins the identity checks from issue #3009: net fragment, data cube and factory ids are per world and restart at 1
    /// after travel, so the views must treat an id whose record moved as a DIFFERENT object and rebuild it. Reusing the
    /// old one left the previous body's fragment hanging at its old coordinates — Lyxette saw "E: Recover net fragment"
    /// on an asteroid, and E did nothing because the server's fragment #1 lay somewhere else. (Doors: #1429.)
    /// </summary>
    public sealed class WorldScopedViewsEditModeTests
    {
        private static readonly Vector3 Held = new Vector3(967.5f, 74f, -151.5f);

        [Test]
        public void UnchangedFragmentIsTheSameFragment()
        {
            var nf = new NetStoryFragment { Id = 1, X = 967.5f, Y = 74f, Z = -151.5f, Category = "vega" };
            Assert.That(NetFragmentView.SameFragment(Held, "vega", nf), Is.True);
        }

        [Test]
        public void MovedFragmentIsADifferentFragment()
        {
            // The #3009 replay: the recycled id 1 now names the asteroid's own fragment, far from the old body's.
            var nf = new NetStoryFragment { Id = 1, X = 234.5f, Y = 81f, Z = -166.5f, Category = "vega" };
            Assert.That(NetFragmentView.SameFragment(Held, "vega", nf), Is.False);
        }

        [Test]
        public void ChangedCategoryIsADifferentFragment()
        {
            var nf = new NetStoryFragment { Id = 1, X = 967.5f, Y = 74f, Z = -151.5f, Category = "guardian" };
            Assert.That(NetFragmentView.SameFragment(Held, "vega", nf), Is.False);
        }

        [Test]
        public void UnchangedCubeIsTheSameCube()
        {
            var nc = new NetDataCube { Id = 1, X = 967.5f, Y = 74f, Z = -151.5f, Seed = 42 };
            Assert.That(DataCubeView.SameCube(Held, 42, nc), Is.True);
        }

        [Test]
        public void MovedCubeIsADifferentCube()
        {
            var nc = new NetDataCube { Id = 1, X = 12.5f, Y = 60f, Z = 300.5f, Seed = 42 };
            Assert.That(DataCubeView.SameCube(Held, 42, nc), Is.False);
        }

        [Test]
        public void ReseededCubeIsADifferentCube()
        {
            // Another body's cube on the very same spot would still hold another game.
            var nc = new NetDataCube { Id = 1, X = 967.5f, Y = 74f, Z = -151.5f, Seed = 7 };
            Assert.That(DataCubeView.SameCube(Held, 42, nc), Is.False);
        }

        [Test]
        public void FactoryWhoseTerminalStayedIsTheSameFactory()
        {
            var nf = new NetFactory { Id = 1, TerminalX = 967.5f, TerminalY = 74f, TerminalZ = -151.5f, Claimable = false };
            Assert.That(FactoryView.SameFactory(Held, nf), Is.True); // a claim changes the state, not the factory
        }

        [Test]
        public void FactoryWhoseTerminalMovedIsADifferentFactory()
        {
            var nf = new NetFactory { Id = 1, TerminalX = 100f, TerminalY = 50f, TerminalZ = 100f };
            Assert.That(FactoryView.SameFactory(Held, nf), Is.False);
        }
    }
}
