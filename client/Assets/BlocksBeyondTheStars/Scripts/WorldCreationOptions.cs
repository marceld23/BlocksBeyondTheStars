using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Client
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
        public static readonly string[] StoryModes = { "Default", "None" };           // 0 = built-in pack, 1 = sandbox
        public static readonly string[] StoryDensitySteps = { "Sparse", "Normal", "Dense" };

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

        // Hand-designed structure templates: how readily an authored station/settlement is used in place
        // of a procedural one (Freq index; server default = Rare). 0 (Off) ⇒ always procedural.
        public int StationTemplates = 2;     // Rare
        public int SettlementTemplates = 2;  // Rare

        /// <summary>Structure-template packs DISABLED for this world (advanced page). Empty ⇒ all packs
        /// allowed. We track the disabled set so the common "leave everything on" case emits no override.</summary>
        public readonly HashSet<string> DisabledPacks = new HashSet<string>();

        /// <summary>All structure-template packs the content offers (filled by the UI from the loaded
        /// content). Used to translate <see cref="DisabledPacks"/> into the enabled list the server wants.</summary>
        public readonly List<string> KnownPacks = new List<string>();

        /// <summary>0 Klein · 1 Normal · 2 Groß · 3 Riesig (systems / planets / moons ranges).</summary>
        public int UniverseSize = 1;

        // Survival (creation-time; part of the world's rules)
        public int Oxygen = 2;         // Normal
        public bool Hunger = true;
        public int Hazards = 2;        // Normal
        public int DeathPenalty = 1;   // Light (server default)

        // Story (P8 world option): which story pack runs + how fast it unfolds.
        public int Story = 0;          // 0 = Default pack, 1 = None (sandbox)
        public int StoryDensity = 1;   // 0 Sparse · 1 Normal · 2 Dense

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
            StationTemplates = other.StationTemplates; SettlementTemplates = other.SettlementTemplates;
            Oxygen = other.Oxygen; Hunger = other.Hunger; Hazards = other.Hazards; DeathPenalty = other.DeathPenalty;
            Story = other.Story; StoryDensity = other.StoryDensity;
            PlanetTypes.Clear();
            foreach (var kv in other.PlanetTypes)
            {
                PlanetTypes[kv.Key] = kv.Value;
            }

            // Pack enable/disable is a per-world choice, not part of a difficulty preset, so a preset never
            // disables packs — but keep the set consistent (presets reset it to "all on").
            DisabledPacks.Clear();
            foreach (var p in other.DisabledPacks)
            {
                DisabledPacks.Add(p);
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

            if (StationTemplates != 2) Arg("station-templates", Freq[StationTemplates]);
            if (SettlementTemplates != 2) Arg("settlement-templates", Freq[SettlementTemplates]);

            // Pack picker: emit the ENABLED packs only when the player turned some off (else all = default).
            if (DisabledPacks.Count > 0 && KnownPacks.Count > 0)
            {
                var enabled = new List<string>();
                foreach (var p in KnownPacks)
                {
                    if (!DisabledPacks.Contains(p))
                    {
                        enabled.Add(p);
                    }
                }

                // "none enabled" would otherwise read as "all" on the server; send a sentinel so it means none.
                Arg("structure-packs", "\"" + (enabled.Count > 0 ? string.Join(",", enabled) : "__none__") + "\"");
            }

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

            if (Story == 1) Arg("story", "none");                              // sandbox (no story)
            if (StoryDensity != 1) Arg("story-density", StoryDensitySteps[StoryDensity]);

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
