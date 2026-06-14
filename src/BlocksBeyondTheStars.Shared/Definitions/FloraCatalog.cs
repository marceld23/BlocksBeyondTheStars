using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Climate/style affinities of a flora species. A planet/biome <see cref="FloraThemes.Theme"/> prefers
/// some of these, which biases both which species a world activates (<see cref="FloraGenerator"/>) and
/// which one a given column grows — so the same surface block reads differently from world to world
/// (a tropical grass world leans to ferns/vines/orchids, a savanna grass world to dry tufts/shrubs).
/// </summary>
[Flags]
public enum FloraTag
{
    None = 0,
    Lush = 1 << 0,      // temperate / fertile greenery
    Dry = 1 << 1,       // arid desert / scrub
    Cold = 1 << 2,      // tundra / icy
    Fungal = 1 << 3,    // mushrooms / mycelium
    Alien = 1 << 4,     // corrupted / otherworldly
    Rocky = 1 << 5,     // stone / mountain / crust
    Wetland = 1 << 6,   // swamp / marsh / shore
    Tropical = 1 << 7,  // jungle heat
    Glow = 1 << 8,      // bioluminescent (cosmetic grouping)
}

/// <summary>How tall a flora billboard renders. <see cref="Tall"/> plants get a taller cross-billboard
/// (client mesher) so a field reads in layers — low ground cover under tall grass/reeds/ferns. Only
/// affects leafy cross-billboard flora; solid/cube flora ignore it.</summary>
public enum FloraHeight
{
    Short,
    Tall,
}

/// <summary>
/// The fixed catalog of surface flora species and the surface block(s) each grows on. Shared by world
/// generation (which seeds biome-appropriate flora from a per-surface pool) and the game server (harvest
/// regrow + replant host validation) so the two never disagree. Flora are plain voxel blocks — their
/// stats/drops live in <c>blocks.json</c>; this records which surface each belongs on plus its climate
/// <see cref="FloraTag"/>s and render <see cref="FloraHeight"/>.
/// </summary>
public static class FloraCatalog
{
    /// <param name="Aquatic">True for in-water plants (kelp/lily): world gen places these directly in the
    /// submerged columns, so they are excluded from the land surface-flora pool even though their hosts
    /// (seabed blocks / water) overlap dry-land surfaces.</param>
    /// <param name="Tags">Climate/style affinities used by <see cref="FloraThemes"/> to weight this species.</param>
    /// <param name="Height">Render height class (tall plants form an upper vegetation layer).</param>
    public sealed record Species(
        string Key,
        string[] Hosts,
        bool Aquatic = false,
        FloraTag Tags = FloraTag.None,
        FloraHeight Height = FloraHeight.Short);

    /// <summary>All flora species, paired with the surface block keys they may grow on.</summary>
    public static readonly IReadOnlyList<Species> All = new[]
    {
        // Temperate / jungle greenery (grass, dirt, mud).
        new Species("flora_plant",       new[] { "grass", "dirt", "mud" }, Tags: FloraTag.Lush),
        new Species("flora_fern",        new[] { "grass", "dirt" }, Tags: FloraTag.Lush | FloraTag.Tropical, Height: FloraHeight.Tall),
        new Species("flora_flower",      new[] { "grass", "alien_grass" }, Tags: FloraTag.Lush),
        new Species("flora_bush",        new[] { "grass" }, Tags: FloraTag.Lush),
        new Species("flora_vine",        new[] { "grass" }, Tags: FloraTag.Lush | FloraTag.Tropical, Height: FloraHeight.Tall),
        new Species("flora_mushroom",    new[] { "grass", "mud", "mycelium" }, Tags: FloraTag.Fungal),
        // Desert (sand) + dry salt flats.
        new Species("flora_cactus",      new[] { "sand" }, Tags: FloraTag.Dry),
        new Species("flora_dryshrub",    new[] { "sand", "dirt", "salt" }, Tags: FloraTag.Dry),
        // Swamp / wetland (mud) + fungal mycelium.
        new Species("flora_reed",        new[] { "mud" }, Tags: FloraTag.Wetland, Height: FloraHeight.Tall),
        new Species("flora_glowcap",     new[] { "mud", "mycelium" }, Tags: FloraTag.Fungal | FloraTag.Glow),
        // Aquatic — kelp roots on the seabed, lily pads float on the water surface (world gen places these
        // under/at the sea; the host lets harvested plants regrow on the same spot, like land flora).
        new Species("flora_kelp",        new[] { "sand", "dirt", "mud", "stone" }, Aquatic: true, Tags: FloraTag.Wetland, Height: FloraHeight.Tall),
        new Species("flora_lily",        new[] { "water" }, Aquatic: true, Tags: FloraTag.Wetland),
        // Harsh worlds — icy tundra + volcanic ash.
        new Species("flora_frostflower", new[] { "ice", "snow" }, Tags: FloraTag.Cold),
        new Species("flora_emberbloom",  new[] { "basalt", "ash" }, Tags: FloraTag.Dry | FloraTag.Glow),
        // Crystalline (crystal/stone/basalt).
        new Species("flora_crystal",     new[] { "crystal", "stone", "basalt" }, Tags: FloraTag.Rocky | FloraTag.Glow),

        // --- Task 6: more variety ---
        // Temperate / jungle greenery.
        new Species("flora_palm",        new[] { "grass", "sand" }, Tags: FloraTag.Tropical, Height: FloraHeight.Tall),
        new Species("flora_orchid",      new[] { "grass", "mud", "alien_grass" }, Tags: FloraTag.Tropical | FloraTag.Lush),
        new Species("flora_bellflower",  new[] { "grass", "alien_grass" }, Tags: FloraTag.Lush),
        new Species("flora_glowvine",    new[] { "grass", "mud", "mycelium", "alien_grass" }, Tags: FloraTag.Lush | FloraTag.Glow), // bioluminescent (ChunkMesher.GlowFor)
        // Stony / rocky.
        new Species("flora_moss",        new[] { "stone", "dirt" }, Tags: FloraTag.Rocky | FloraTag.Lush),
        new Species("flora_sporepod",    new[] { "crystal", "stone", "mycelium" }, Tags: FloraTag.Fungal | FloraTag.Glow), // faintly glowing
        // Desert + dry salt flats.
        new Species("flora_succulent",   new[] { "sand", "salt" }, Tags: FloraTag.Dry),
        new Species("flora_thornbush",   new[] { "sand", "dirt", "alien_grass" }, Tags: FloraTag.Dry, Height: FloraHeight.Tall),
        // Swamp / wetland + fungal mycelium.
        new Species("flora_pitcher",     new[] { "mud", "grass" }, Tags: FloraTag.Wetland),
        new Species("flora_puffball",    new[] { "mud", "dirt", "mycelium" }, Tags: FloraTag.Fungal),
        // Harsh worlds — icy tundra.
        new Species("flora_lichen",      new[] { "ice", "stone", "snow" }, Tags: FloraTag.Cold | FloraTag.Rocky),
        new Species("flora_ashweed",     new[] { "basalt", "ash" }, Tags: FloraTag.Dry),
        // Aquatic — coral reefs + seagrass on the seabed.
        new Species("flora_coral",       new[] { "sand", "stone" }, Aquatic: true, Tags: FloraTag.Wetland | FloraTag.Rocky),
        new Species("flora_seagrass",    new[] { "sand", "dirt", "mud" }, Aquatic: true, Tags: FloraTag.Wetland, Height: FloraHeight.Tall),

        // --- Item 21 V3: alien flora (corrupted / fungal / crystal worlds) ---
        new Species("flora_tendril",     new[] { "alien_grass", "mycelium" }, Tags: FloraTag.Alien, Height: FloraHeight.Tall),
        new Species("flora_bulb",        new[] { "alien_grass", "mycelium" }, Tags: FloraTag.Alien | FloraTag.Glow),        // bioluminescent (ChunkMesher.GlowFor)
        new Species("flora_gasbloom",    new[] { "alien_grass", "mud" }, Tags: FloraTag.Alien),
        new Species("flora_alienfern",   new[] { "alien_grass", "grass" }, Tags: FloraTag.Alien, Height: FloraHeight.Tall),
        new Species("flora_shardbloom",  new[] { "crystal", "stone" }, Tags: FloraTag.Alien | FloraTag.Rocky | FloraTag.Glow), // crystal flower (faint glow)

        // --- Flora variety V2: fill the thin biomes (rock / ice / snow / salt / ash) + signature tall grass ---
        new Species("flora_grasstuft",   new[] { "grass", "dirt" }, Tags: FloraTag.Lush, Height: FloraHeight.Tall),  // waving tall grass — forest-floor / meadow staple
        new Species("flora_rockflower",  new[] { "stone", "dirt" }, Tags: FloraTag.Rocky | FloraTag.Lush),
        new Species("flora_snowbush",    new[] { "snow", "ice" }, Tags: FloraTag.Cold),
        new Species("flora_icereed",     new[] { "ice", "snow" }, Tags: FloraTag.Cold, Height: FloraHeight.Tall),
        new Species("flora_saltgrass",   new[] { "salt", "sand" }, Tags: FloraTag.Dry, Height: FloraHeight.Tall),
        new Species("flora_cinderbush",  new[] { "ash", "basalt" }, Tags: FloraTag.Dry | FloraTag.Glow),
    };

    /// <summary>The set of species block keys that render as a TALL cross-billboard (an upper vegetation
    /// layer). Mirrored by the client mesher (ChunkMesher.TallFlora) for the actual geometry.</summary>
    public static bool IsTall(string key)
    {
        foreach (var sp in All)
        {
            if (sp.Key == key)
            {
                return sp.Height == FloraHeight.Tall;
            }
        }

        return false;
    }
}
