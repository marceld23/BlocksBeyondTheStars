using System.Collections.Generic;

namespace Spacecraft.Shared.Definitions;

/// <summary>
/// The fixed catalog of surface flora species and the surface block(s) each grows on. Shared by world
/// generation (which seeds biome-appropriate flora from a per-surface pool) and the game server (harvest
/// regrow + replant host validation) so the two never disagree. Flora are plain voxel blocks — their
/// stats/drops live in <c>blocks.json</c>; this only records which surface each belongs on.
/// </summary>
public static class FloraCatalog
{
    /// <param name="Aquatic">True for in-water plants (kelp/lily): world gen places these directly in the
    /// submerged columns, so they are excluded from the land surface-flora pool even though their hosts
    /// (seabed blocks / water) overlap dry-land surfaces.</param>
    public sealed record Species(string Key, string[] Hosts, bool Aquatic = false);

    /// <summary>All flora species, paired with the surface block keys they may grow on.</summary>
    public static readonly IReadOnlyList<Species> All = new[]
    {
        // Temperate / jungle greenery (grass, dirt, mud).
        new Species("flora_plant",       new[] { "grass", "dirt", "mud" }),
        new Species("flora_fern",        new[] { "grass", "dirt" }),
        new Species("flora_flower",      new[] { "grass", "alien_grass" }),
        new Species("flora_bush",        new[] { "grass" }),
        new Species("flora_vine",        new[] { "grass" }),
        new Species("flora_mushroom",    new[] { "grass", "mud", "mycelium" }),
        // Desert (sand) + dry salt flats.
        new Species("flora_cactus",      new[] { "sand" }),
        new Species("flora_dryshrub",    new[] { "sand", "dirt", "salt" }),
        // Swamp / wetland (mud) + fungal mycelium.
        new Species("flora_reed",        new[] { "mud" }),
        new Species("flora_glowcap",     new[] { "mud", "mycelium" }),
        // Aquatic — kelp roots on the seabed, lily pads float on the water surface (world gen places these
        // under/at the sea; the host lets harvested plants regrow on the same spot, like land flora).
        new Species("flora_kelp",        new[] { "sand", "dirt", "mud", "stone" }, Aquatic: true),
        new Species("flora_lily",        new[] { "water" }, Aquatic: true),
        // Harsh worlds — icy tundra + volcanic ash.
        new Species("flora_frostflower", new[] { "ice", "snow" }),
        new Species("flora_emberbloom",  new[] { "basalt", "ash" }),
        // Crystalline (crystal/stone/basalt).
        new Species("flora_crystal",     new[] { "crystal", "stone", "basalt" }),

        // --- Task 6: more variety ---
        // Temperate / jungle greenery.
        new Species("flora_palm",        new[] { "grass", "sand" }),
        new Species("flora_orchid",      new[] { "grass", "mud", "alien_grass" }),
        new Species("flora_bellflower",  new[] { "grass", "alien_grass" }),
        new Species("flora_glowvine",    new[] { "grass", "mud", "mycelium", "alien_grass" }), // bioluminescent (ChunkMesher.GlowFor)
        // Stony / rocky.
        new Species("flora_moss",        new[] { "stone", "dirt" }),
        new Species("flora_sporepod",    new[] { "crystal", "stone", "mycelium" }), // faintly glowing
        // Desert + dry salt flats.
        new Species("flora_succulent",   new[] { "sand", "salt" }),
        new Species("flora_thornbush",   new[] { "sand", "dirt", "alien_grass" }),
        // Swamp / wetland + fungal mycelium.
        new Species("flora_pitcher",     new[] { "mud", "grass" }),
        new Species("flora_puffball",    new[] { "mud", "dirt", "mycelium" }),
        // Harsh worlds — icy tundra.
        new Species("flora_lichen",      new[] { "ice", "stone", "snow" }),
        new Species("flora_ashweed",     new[] { "basalt", "ash" }),
        // Aquatic — coral reefs + seagrass on the seabed.
        new Species("flora_coral",       new[] { "sand", "stone" }, Aquatic: true),
        new Species("flora_seagrass",    new[] { "sand", "dirt", "mud" }, Aquatic: true),

        // --- Item 21 V3: alien flora (corrupted / fungal / crystal worlds) ---
        new Species("flora_tendril",     new[] { "alien_grass", "mycelium" }),
        new Species("flora_bulb",        new[] { "alien_grass", "mycelium" }),        // bioluminescent (ChunkMesher.GlowFor)
        new Species("flora_gasbloom",    new[] { "alien_grass", "mud" }),
        new Species("flora_alienfern",   new[] { "alien_grass", "grass" }),
        new Species("flora_shardbloom",  new[] { "crystal", "stone" }),              // crystal flower (faint glow)
    };
}
