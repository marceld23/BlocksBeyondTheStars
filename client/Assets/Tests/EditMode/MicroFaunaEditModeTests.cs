// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The micro-fauna's pure rules (<see cref="MicroFauna"/>). #2581: crawlers took the top of the gas giant's
    /// gas sea — and of every pond, lava lake and oil pool — for ground, so beetles and worms walked on the
    /// see-through haze in mid-air; the column probe now gives crawlers and hoppers a real floor only, while
    /// fliers and drifters keep the liquid surface as their reference. #2582: the gas giant got the generic
    /// insect roster (butterflies and bees in a −120 °C gale); it now has its own drifters, with the gas-bag
    /// as its always-kept signature species.
    /// </summary>
    public sealed class MicroFaunaEditModeTests
    {
        private const int Top = 20;
        private const int Bottom = -5;

        /// <summary>A column for the probe, listed bottom-up from y = 0; everything outside it is air.</summary>
        private readonly struct Column : IFaunaColumn
        {
            private readonly FaunaCell[] _cells;

            public Column(params FaunaCell[] cellsBottomUp)
            {
                _cells = cellsBottomUp;
            }

            public FaunaCell CellAt(int y) => y >= 0 && y < _cells.Length ? _cells[y] : FaunaCell.Air;
        }

        private const FaunaCell A = FaunaCell.Air;
        private const FaunaCell L = FaunaCell.Liquid;
        private const FaunaCell G = FaunaCell.Ground;

        private static int Floor(Column column) => MicroFauna.ColumnTopY(column, Top, Bottom, liquidIsFloor: false);

        private static int Surface(Column column) => MicroFauna.ColumnTopY(column, Top, Bottom, liquidIsFloor: true);

        [Test]
        public void GroundTop_IsTheFloor_ForCrawlersAndFliersAlike()
        {
            var column = new Column(G, G, G); // terrain up to y = 2
            Assert.That(Floor(column), Is.EqualTo(2));
            Assert.That(Surface(column), Is.EqualTo(2));
        }

        [Test]
        public void LiquidTop_IsNoFloor_ButStillTheFliersSurface()
        {
            var column = new Column(G, G, L, L); // a pond: ground to y = 1, liquid to y = 3
            Assert.That(Floor(column), Is.EqualTo(int.MinValue), "a crawler must not stand on the liquid's surface");
            Assert.That(Surface(column), Is.EqualTo(3), "a butterfly over a pond keeps the water surface as its reference");
        }

        [Test]
        public void GroundBelowALiquidSurface_IsNotFoundForCrawlers()
        {
            // Ground at y = 0, a cave (air) at y = 1..2, its roof at y = 3, a lake on top (y = 4..5). The cave
            // floor is a "non-air with air above" cell further down — searching on past the lake would seat a
            // beetle in the cave under it (or on the sea bed). The probe stops at the liquid surface.
            var column = new Column(G, A, A, G, L, L);
            Assert.That(Floor(column), Is.EqualTo(int.MinValue));
            Assert.That(Surface(column), Is.EqualTo(5));
        }

        [Test]
        public void DeckOverTheGasSea_IsAFloor()
        {
            // The gas giant: the gas sea up to y = 3, open air at y = 4..5, a landing deck at y = 6.
            var column = new Column(L, L, L, L, A, A, G);
            Assert.That(Floor(column), Is.EqualTo(6), "decks and islands are real ground for the frostmite");
            Assert.That(Surface(column), Is.EqualTo(6));
        }

        [Test]
        public void AirEverywhere_HasNoSurface()
        {
            var column = new Column(A, A, A);
            Assert.That(Floor(column), Is.EqualTo(int.MinValue));
            Assert.That(Surface(column), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void SurfaceBelowTheScanWindow_IsNotSeen()
        {
            var column = new Column(G, G, G); // top at y = 2
            Assert.That(MicroFauna.ColumnTopY(column, 10, 3, liquidIsFloor: true), Is.EqualTo(int.MinValue));
            Assert.That(MicroFauna.ColumnTopY(column, 10, 2, liquidIsFloor: true), Is.EqualTo(2), "the window's bottom row is inclusive");
        }

        private static readonly string[] GasGiantDay = { "gasbag", "wisp", "frostmite" };
        private static readonly string[] GasGiantNight = { "gasbag", "sporedrifter", "wisp", "frostmite" };

        private static HashSet<string> SurfaceKeys(string biome, bool night, bool wet)
        {
            var into = new List<int>();
            MicroFauna.SurfaceKinds(biome, night, wet, into);
            var keys = new HashSet<string>();
            foreach (int i in into)
            {
                keys.Add(MicroFauna.Kinds[i].Key);
            }

            return keys;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void GasGiant_HasItsOwnRoster_DayAndNight_WetOrDry(bool night, bool wet)
        {
            var keys = SurfaceKeys("gas_giant", night, wet);
            var allowed = night ? GasGiantNight : GasGiantDay;
            CollectionAssert.AreEquivalent(allowed, keys,
                $"gas giant {(night ? "night" : "day")}{(wet ? " in the rain" : string.Empty)}: only its own drifters and the frostmite");
            CollectionAssert.DoesNotContain(keys, "butterfly");
            CollectionAssert.DoesNotContain(keys, "beetle");
        }

        [Test]
        public void GasGiant_IsSparserThanARockWorld()
        {
            Assert.That(MicroFauna.Richness("gas_giant"), Is.GreaterThan(0f));
            Assert.That(MicroFauna.Richness("gas_giant"), Is.LessThan(MicroFauna.Richness("rock")));
        }

        [Test]
        public void SignatureKind_IsTheGasBagOnTheGasGiant_AndNoneElsewhere()
        {
            int gasbag = MicroFauna.SignatureKind("gas_giant");
            Assert.That(gasbag, Is.GreaterThanOrEqualTo(0));
            Assert.That(MicroFauna.Kinds[gasbag].Key, Is.EqualTo("gasbag"));
            Assert.That(MicroFauna.SignatureKind("jungle"), Is.EqualTo(-1));
            Assert.That(MicroFauna.SignatureKind(null), Is.EqualTo(-1));
        }

        [Test]
        public void ClassicWorlds_KeepTheirRoster()
        {
            // Regression guard for #2582's "no change for any other planet type".
            var day = SurfaceKeys("jungle", night: false, wet: false);
            CollectionAssert.Contains(day, "butterfly");
            CollectionAssert.Contains(day, "beetle");
            Assert.That(MicroFauna.Richness("jungle"), Is.EqualTo(1.3f));
            Assert.That(MicroFauna.Richness("rock"), Is.EqualTo(0.45f));
        }
    }
}
