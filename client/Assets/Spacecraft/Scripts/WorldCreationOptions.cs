using System.Collections.Generic;
using System.Text;

namespace Spacecraft.Client
{
    /// <summary>
    /// The world options chosen in the creation panel (sliders + presets). Indices address the
    /// 5-step enum scales the server understands (<c>AlienActivity</c> / <c>Frequency</c>); defaults
    /// mirror the server defaults exactly, and <see cref="ToArgs"/> only emits the NON-default values
    /// as server CLI overrides — so an untouched panel creates exactly yesterday's world.
    /// </summary>
    public sealed class WorldCreationOptions
    {
        // Enum name tables (index → server enum name). Order matters — it matches the C# enums.
        public static readonly string[] Activity = { "Off", "Rare", "Normal", "Frequent", "Extreme" };
        public static readonly string[] Freq = { "Off", "VeryRare", "Rare", "Normal", "Frequent" };
        public static readonly string[] OxygenSteps = { "Off", "Slow", "Normal", "Fast" };
        public static readonly string[] HazardSteps = { "Off", "Light", "Normal", "Hard" };
        public static readonly string[] DeathSteps = { "None", "Light", "Normal", "Hard" };

        // Gameplay (AlienActivity indices; live-editable later in-game)
        public int Creatures = 2;      // Normal
        public int PlanetEnemies = 2;  // Normal
        public int SpaceNpcs = 2;      // Normal (the singleplayer default so far)
        public int Ufos = 0;           // Off (matches today's default)

        // Worldgen (Frequency indices; creation-only — baked into the save)
        public int Flora = 3;          // Normal
        public int Ore = 2;            // Rare (the server default baseline)
        public int Settlements = 3;    // Normal
        public int Wrecks = 3;         // Normal
        public int Vaults = 3;         // Normal
        public int Stations = 2;       // Rare (server default)
        public int Exotic = 3;         // Normal

        /// <summary>0 Klein · 1 Normal · 2 Groß · 3 Riesig (systems / planets / moons ranges).</summary>
        public int UniverseSize = 1;

        // Survival (creation-time; part of the world's rules)
        public int Oxygen = 2;         // Normal
        public bool Hunger = true;
        public int Hazards = 2;        // Normal
        public int DeathPenalty = 1;   // Light (server default)

        /// <summary>Advanced page: per-planet-type frequency overrides (type key → Freq index).
        /// Empty = the simple "exotic worlds" slider + data weights decide.</summary>
        public readonly Dictionary<string, int> PlanetTypes = new Dictionary<string, int>();

        public static WorldCreationOptions Peaceful()
            => new WorldCreationOptions { Creatures = 3, PlanetEnemies = 0, SpaceNpcs = 0, Ufos = 0, Hazards = 1, DeathPenalty = 0 };

        public static WorldCreationOptions Standard() => new WorldCreationOptions();

        public static WorldCreationOptions Hostile()
            => new WorldCreationOptions
            {
                Creatures = 1, PlanetEnemies = 3, SpaceNpcs = 3, Ufos = 2,
                Flora = 2, Settlements = 2, Hazards = 3, DeathPenalty = 2,
            };

        /// <summary>Copies every value from a preset into this instance (keeps UI bindings alive).</summary>
        public void CopyFrom(WorldCreationOptions other)
        {
            Creatures = other.Creatures; PlanetEnemies = other.PlanetEnemies; SpaceNpcs = other.SpaceNpcs; Ufos = other.Ufos;
            Flora = other.Flora; Ore = other.Ore; Settlements = other.Settlements; Wrecks = other.Wrecks;
            Vaults = other.Vaults; Stations = other.Stations; Exotic = other.Exotic; UniverseSize = other.UniverseSize;
            Oxygen = other.Oxygen; Hunger = other.Hunger; Hazards = other.Hazards; DeathPenalty = other.DeathPenalty;
            PlanetTypes.Clear();
            foreach (var kv in other.PlanetTypes)
            {
                PlanetTypes[kv.Key] = kv.Value;
            }
        }

        /// <summary>Server CLI overrides for the chosen options — only the non-default ones.</summary>
        public string ToArgs()
        {
            var sb = new StringBuilder();
            void Arg(string key, string value) => sb.Append(' ').Append("--").Append(key).Append(' ').Append(value);

            if (Creatures != 2) Arg("creatures", Activity[Creatures]);
            if (PlanetEnemies != 2) Arg("planet-enemies", Activity[PlanetEnemies]);
            if (SpaceNpcs != 2) Arg("space-npcs", Activity[SpaceNpcs]);
            if (Ufos != 0) Arg("ufos", Activity[Ufos]);

            if (Flora != 3) Arg("flora", Freq[Flora]);
            if (Ore != 2) Arg("ore", Freq[Ore]);
            if (Settlements != 3) Arg("settlements", Freq[Settlements]);
            if (Wrecks != 3) Arg("planet-wrecks", Freq[Wrecks]);
            if (Vaults != 3) Arg("vaults", Freq[Vaults]);
            if (Stations != 2) Arg("stations", Freq[Stations]);
            if (Exotic != 3) Arg("exotic", Freq[Exotic]);

            if (UniverseSize != 1)
            {
                // Klein / Normal / Groß / Riesig → systems + planets-per-system + moons.
                (int systems, int pMin, int pMax, int moons) = UniverseSize switch
                {
                    0 => (4, 2, 4, 2),
                    2 => (12, 3, 8, 3),
                    3 => (18, 3, 10, 4),
                    _ => (8, 2, 6, 3),
                };
                Arg("systems", systems.ToString());
                Arg("planets-min", pMin.ToString());
                Arg("planets-max", pMax.ToString());
                Arg("moons-max", moons.ToString());
            }

            if (Oxygen != 2) Arg("oxygen", OxygenSteps[Oxygen]);
            if (!Hunger) Arg("hunger", "false");
            if (Hazards != 2) Arg("hazards", HazardSteps[Hazards]);
            if (DeathPenalty != 1) Arg("death-penalty", DeathSteps[DeathPenalty]);

            if (PlanetTypes.Count > 0)
            {
                var pairs = new List<string>();
                foreach (var kv in PlanetTypes)
                {
                    pairs.Add(kv.Key + "=" + Freq[kv.Value]);
                }

                Arg("planet-types", "\"" + string.Join(",", pairs) + "\"");
            }

            return sb.ToString();
        }
    }
}
