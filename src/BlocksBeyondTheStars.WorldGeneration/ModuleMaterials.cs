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
