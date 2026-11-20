// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The blocks a settlement's <see cref="MaterialTokens"/> resolve to (#1885, Marcel 2026-09-14: "walls follow the
/// biome"). The rules are the procedural buildings' own (<see cref="SettlementGenerator.Generate"/> reads its wall,
/// accent and path from here): a village-style wall is the planet's surface block, a town-style wall iron; the accent
/// is crystal for aliens, glass for towns, carbon for villages; an alien roof is the accent.
/// </summary>
public sealed class ModuleMaterials
{
    public ushort Wall;
    public ushort Accent;
    public ushort Roof;
    public ushort Floor;
    public ushort Path;

    /// <summary>The lively places: the dye (0xRRGGBB; 0 = none) a roof token and an accent token cell get when the module
    /// gives it none of its own — the settlement's colour (<see cref="SettlementPalette"/>).</summary>
    public int RoofTint;
    public int AccentTint;

    /// <summary>The dye of a token cell (0 for a token that carries none, and for anything that is not a token).</summary>
    public int TintOf(string id) => id switch
    {
        MaterialTokens.Roof => RoofTint,
        MaterialTokens.Accent => AccentTint,
        _ => 0,
    };

    /// <summary>
    /// The lively places (plan revision 2): a settlement's materials with its own colour and a wall of its own. The colour
    /// dyes the roofs and the accents. A village-style wall is no longer always the planet's first surface block: by the
    /// settlement's seed it is that block, planks, or the stone of the land (sandstone on sand) — still the planet's
    /// materials, but two neighbouring villages need not be built of the same one; its roof is planks, so the dye shows.
    /// A town keeps its iron.
    /// </summary>
    public static ModuleMaterials ForLivelySettlement(string tier, string surface, bool alien, GameContent content, long seed, int colour)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        var m = ForSettlement(tier, surface, alien, content);
        m.RoofTint = colour;
        m.AccentTint = colour;
        if (StructureRoles.IsTownStyleTier(tier))
        {
            return m;
        }

        ushort stone = surface == "sand" ? B("sandstone", B("stone")) : B("stone");
        ushort planks = B(Shared.World.StableHashing.Index("settlement:planks", seed, 2) == 0 ? "wood_planks" : "wood_planks_light", B("wood_planks", m.Wall));
        m.Wall = Shared.World.StableHashing.Index("settlement:wall", seed, 3) switch
        {
            0 => m.Wall,
            1 => planks,
            _ => stone,
        };
        m.Floor = m.Wall;
        if (!alien)
        {
            m.Roof = planks; // a wooden roof takes the dye; an alien roof stays its crystal
        }

        return m;
    }

    /// <summary>The block for a token cell, 0 for anything that is not a token.</summary>
    public ushort Resolve(string id) => id switch
    {
        MaterialTokens.Wall => Wall,
        MaterialTokens.Accent => Accent != 0 ? Accent : Wall,
        MaterialTokens.Roof => Roof != 0 ? Roof : Wall,
        MaterialTokens.Floor => Floor != 0 ? Floor : Wall,
        MaterialTokens.Path => Path != 0 ? Path : Wall,
        _ => 0,
    };

    /// <summary>A settlement's materials: <paramref name="tier"/> decides the style (town / city tiers build iron),
    /// <paramref name="surface"/> is the biome's surface block, <paramref name="alien"/> the inhabitants.</summary>
    public static ModuleMaterials ForSettlement(string tier, string surface, bool alien, GameContent content)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        bool town = StructureRoles.IsTownStyleTier(tier);
        bool desert = surface == "sand";
        ushort wall = town ? B("iron_wall") : B(surface, B("stone"));
        ushort path = town
            ? B("carbon", B("stone"))
            : desert ? B("sand", B("stone"))
            : surface == "ice" ? B("ice", B("stone"))
            : B("stone", wall);
        ushort accent = alien ? B("crystal", B("carbon")) : (town ? B("glass") : B("carbon", B("stone")));
        return new ModuleMaterials
        {
            Wall = wall,
            Accent = accent,
            Roof = alien ? accent : wall,
            Floor = town ? B("steel_floor", wall) : wall,
            Path = path,
        };
    }

    /// <summary>#3519: a space station's materials — the procedural stations' own hull: iron walls and roof, glass accents
    /// (the viewports), steel floors and walkways. A station template resolves its tokens with these on a world of terrain
    /// generation 38 or later (before it a token cell stayed air).</summary>
    public static ModuleMaterials ForStation(GameContent content)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort wall = B("iron_wall", B("stone"));
        ushort floor = B("steel_floor", wall);
        return new ModuleMaterials
        {
            Wall = wall,
            Accent = B("glass", wall),
            Roof = wall,
            Floor = floor,
            Path = floor,
        };
    }

    /// <summary>The G.D.S. city's materials: iron walls (the district tint colours them), glass accents, steel floors,
    /// sandstone paving.</summary>
    public static ModuleMaterials ForCity(GameContent content)
    {
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        ushort wall = B("iron_wall", stone);
        return new ModuleMaterials
        {
            Wall = wall,
            Accent = B("glass", wall),
            Roof = wall,
            Floor = B("steel_floor", B("sandstone", stone)),
            Path = B("sandstone", stone),
        };
    }
}
