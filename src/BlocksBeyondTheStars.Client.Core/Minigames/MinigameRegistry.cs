// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Minigames.Games;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>
    /// The catalogue of native minigames, keyed the same as <c>data/minigames/catalog.json</c>. The ORDER is
    /// authoritative: a data cube's seed maps to a game by index (seed mod count), so this list must stay in the
    /// same order as the JSON catalogue — append new games, never reorder. The Unity Arcade host resolves a key
    /// to a fresh <see cref="IMinigame"/> through <see cref="Create"/>; <see cref="IndexOf"/> backs the cube→game
    /// mapping. Pure (Unity-free) so it is unit-tested headless.
    /// </summary>
    public static class MinigameRegistry
    {
        private static readonly (string Key, Func<IMinigame> Make)[] Entries =
        {
            ("blockfall", () => new BlockfallGame()),
            ("asteroid_breaker", () => new AsteroidBreakerGame()),
            ("circuit_weaver", () => new CircuitWeaverGame()),
            ("signal_tuner", () => new SignalTunerGame()),
            ("drone_rescue", () => new DroneRescueGame()),
            ("cargo_sorter", () => new CargoSorterGame()),
            ("blueprint_scramble", () => new BlueprintScrambleGame()),
            ("orbit_slingshot", () => new OrbitSlingshotGame()),
            ("laser_grid", () => new LaserGridGame()),
            ("micro_miner", () => new MicroMinerGame()),
            ("star_memory", () => new StarMemoryGame()),
            ("glyph_decoder", () => new GlyphDecoderGame()),
            ("reactor_balance", () => new ReactorBalanceGame()),
            ("oxygen_loop", () => new OxygenLoopGame()),
            ("comet_courier", () => new CometCourierGame()),
            ("docking_sim", () => new DockingSimGame()),
            ("data_fishing", () => new DataFishingGame()),
            ("nanobot_repair", () => new NanobotRepairGame()),
            ("planet_scanner", () => new PlanetScannerGame()),
            ("void_solitaire", () => new VoidSolitaireGame()),
        };

        /// <summary>The game keys in catalogue order.</summary>
        public static IReadOnlyList<string> Keys
        {
            get
            {
                var keys = new string[Entries.Length];
                for (int i = 0; i < Entries.Length; i++)
                {
                    keys[i] = Entries[i].Key;
                }

                return keys;
            }
        }

        public static int Count => Entries.Length;

        public static bool Has(string key) => IndexOf(key) >= 0;

        public static int IndexOf(string key)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Key == key)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>A fresh game instance for the key, or null if unknown.</summary>
        public static IMinigame? Create(string key)
        {
            int i = IndexOf(key);
            return i >= 0 ? Entries[i].Make() : null;
        }

        /// <summary>The game a data cube's seed yields (seed mod count) — mirrors the web catalogue's index map.</summary>
        public static IMinigame ForSeed(int seed)
        {
            int i = ((seed % Entries.Length) + Entries.Length) % Entries.Length;
            return Entries[i].Make();
        }
    }
}
