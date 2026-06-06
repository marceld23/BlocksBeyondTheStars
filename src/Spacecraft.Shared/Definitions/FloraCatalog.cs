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
        new Species("flora_flower",      new[] { "grass" }),
        new Species("flora_bush",        new[] { "grass" }),
        new Species("flora_vine",        new[] { "grass" }),
        new Species("flora_mushroom",    new[] { "grass", "mud" }),
        // Desert (sand).
        new Species("flora_cactus",      new[] { "sand" }),
        new Species("flora_dryshrub",    new[] { "sand", "dirt" }),
        // Swamp / wetland (mud).
        new Species("flora_reed",        new[] { "mud" }),
        new Species("flora_glowcap",     new[] { "mud" }),
        // Aquatic — kelp roots on the seabed, lily pads float on the water surface (world gen places these
        // under/at the sea; the host lets harvested plants regrow on the same spot, like land flora).
        new Species("flora_kelp",        new[] { "sand", "dirt", "mud", "stone" }, Aquatic: true),
        new Species("flora_lily",        new[] { "water" }, Aquatic: true),
        // Harsh worlds.
        new Species("flora_frostflower", new[] { "ice" }),
        new Species("flora_emberbloom",  new[] { "basalt" }),
        // Crystalline (crystal/stone/basalt).
        new Species("flora_crystal",     new[] { "crystal", "stone", "basalt" }),
    };
}
