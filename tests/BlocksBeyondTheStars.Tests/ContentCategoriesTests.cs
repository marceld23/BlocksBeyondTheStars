// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3102–#3112: the shared build categories of <c>data/categories.json</c> — every craftable thing, blueprint, ship module
/// and ship is findable in at least one category, a thing may sit in several, content added later still lands somewhere,
/// and the file names nothing that does not exist. Also the two forward/backward lookups the menu shows next to them:
/// the ships under a blueprint's "Unlocks" (#3105) and an item's "Needed for" (#3112).
/// </summary>
public sealed class ContentCategoriesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static ContentCategories Cats => Content.Categories;

    [Fact]
    public void CategoryFile_IsLoaded_AndNamesOnlyExistingContent()
    {
        Assert.NotNull(Content.CategoryCatalog);
        Assert.False(Cats.IsEmpty);
        Assert.Empty(ContentCategories.Problems(Content.CategoryCatalog!, Content));
    }

    [Fact]
    public void EveryItem_HasAtLeastOneCategory()
    {
        var homeless = Content.Items.Keys.Where(k => Cats.OfItem(k).Count == 0).ToList();
        Assert.True(homeless.Count == 0, "items without a category: " + string.Join(", ", homeless));
    }

    [Fact]
    public void EveryBlueprintModuleAndShip_HasAtLeastOneCategory()
    {
        var missing = Content.Blueprints.Keys.Where(k => Cats.OfBlueprint(k).Count == 0).Select(k => "blueprint " + k)
            .Concat(Content.ShipModules.Keys.Where(k => Cats.OfModule(k).Count == 0).Select(k => "module " + k))
            .Concat(Content.Ships.Keys.Where(k => Cats.OfShip(k).Count == 0).Select(k => "ship " + k))
            .ToList();
        Assert.True(missing.Count == 0, "without a category: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryCategory_HasSomethingToCraft()
    {
        var crafted = Content.Recipes.Values.Where(r => r.Station != CraftingStation.Market)
            .SelectMany(r => r.Outputs).Select(o => o.Item).ToHashSet(StringComparer.Ordinal);
        foreach (var c in Cats.Categories)
        {
            Assert.True(crafted.Any(i => Cats.OfItem(i).Contains(c.Key)), $"category '{c.Key}' has no craftable item");
        }
    }

    [Theory]
    [InlineData("jetpack", "movement", "suit")]
    [InlineData("boat", "movement")]
    [InlineData("speeder", "movement")]
    [InlineData("rail_cab", "movement")]
    [InlineData("rail_pylon", "movement")]
    [InlineData("suit_teleporter", "movement", "suit")]
    [InlineData("piston", "crystal")]
    [InlineData("daylight_sensor", "crystal")]
    [InlineData("comm_radio", "explore", "suit")]
    [InlineData("laser_pistol", "combat")]
    [InlineData("medpack", "food")]
    [InlineData("hearty_stew", "food")]
    [InlineData("sapling", "garden")]
    [InlineData("iron_plate", "materials")]
    [InlineData("steel_wall", "build")]
    [InlineData("lantern", "deco")]
    public void Item_SitsInTheExpectedCategories(string item, params string[] expected)
    {
        foreach (string category in expected)
        {
            Assert.Contains(category, Cats.OfItem(item));
        }
    }

    [Theory]
    [InlineData("monorail", "movement")]
    [InlineData("crystal_lift", "crystal", "movement")]
    [InlineData("jetpack", "movement", "suit")]
    [InlineData("ship_corvette", "ship", "movement", "combat")]
    [InlineData("station_profession_posts", "base")]
    public void Blueprint_SitsInTheExpectedCategories(string blueprint, params string[] expected)
    {
        foreach (string category in expected)
        {
            Assert.Contains(category, Cats.OfBlueprint(blueprint));
        }
    }

    [Fact]
    public void ModulesAndShips_AreAlwaysShip_PlusTheirRole()
    {
        Assert.All(Content.ShipModules.Keys, k => Assert.Contains("ship", Cats.OfModule(k)));
        Assert.All(Content.Ships.Keys, k => Assert.Contains("ship", Cats.OfShip(k)));
        Assert.Contains("storage", Cats.OfModule("cargo_hold_1"));
        Assert.Contains("explore", Cats.OfModule("planet_scanner"));
        Assert.Contains("storage", Cats.OfShip("hauler"));
    }

    [Fact]
    public void CrystalNet_HoldsEveryItemOfItsBlueprints()
    {
        var crystalBlueprints = Content.Blueprints.Values.Where(b => b.Category == "Crystal").Select(b => b.Key).ToHashSet();
        var made = Content.Recipes.Values
            .Where(r => r.Station != CraftingStation.Market && r.RequiredBlueprint is { } bp && crystalBlueprints.Contains(bp))
            .SelectMany(r => r.Outputs).Select(o => o.Item).Distinct().ToList();
        Assert.True(made.Count >= 30, "the Crystal Net should make dozens of things, found " + made.Count);
        Assert.All(made, item => Assert.Contains("crystal", Cats.OfItem(item)));
        Assert.All(crystalBlueprints, bp => Assert.Contains("crystal", Cats.OfBlueprint(bp)));
    }

    [Fact]
    public void Lists_FollowTheFileOrder()
    {
        var jetpack = Cats.OfItem("jetpack");
        Assert.Equal(jetpack.OrderBy(Cats.OrderOf).ToList(), jetpack.ToList());
        Assert.Equal(0, Cats.OrderOf(Cats.Categories[0].Key));
        Assert.Equal(int.MaxValue, Cats.OrderOf("no_such_category"));
    }

    [Fact]
    public void NamesExist_InEnglishAndGerman()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = LocaleKeys(code);
            foreach (var c in Cats.Categories)
            {
                Assert.True(table.Contains(c.NameKey), $"{code}.json lacks {c.NameKey}");
            }

            foreach (var g in Cats.Groups)
            {
                Assert.True(table.Contains(g.NameKey), $"{code}.json lacks {g.NameKey}");
            }
        }
    }

    [Fact]
    public void NoFile_MeansNoCategories()
    {
        var none = ContentCategories.Build(null, Content);
        Assert.True(none.IsEmpty);
        Assert.Empty(none.OfItem("jetpack"));
        Assert.Empty(none.OfBlueprint("jetpack"));
    }

    [Fact]
    public void Fallbacks_KeepUnlistedContentFindable()
    {
        // A file that lists only the speeder and sorts blocks by item category: stone falls back to "blocks", and the
        // speeder blueprint — named by no list — inherits the speeder's category.
        var catalog = new CategoryCatalog
        {
            Groups = { new CategoryGroupDefinition { Key = "g", NameKey = "x" } },
            Categories =
            {
                new CategoryDefinition { Key = "rides", Group = "g", Items = { "speeder" } },
                new CategoryDefinition { Key = "blocks", Group = "g", FallbackFor = { "block" } },
            },
        };
        var cats = ContentCategories.Build(catalog, Content);
        Assert.Equal(new[] { "blocks" }, cats.OfItem("stone"));
        Assert.Equal(new[] { "rides" }, cats.OfBlueprint("speeder"));
        Assert.Empty(cats.OfItem("iron_plate")); // a material, and no category takes materials here
    }

    [Fact]
    public void Problems_NameUnknownKeys()
    {
        var catalog = new CategoryCatalog
        {
            Categories =
            {
                new CategoryDefinition
                {
                    Key = "broken", Group = "nowhere", Items = { "no_such_item" }, Derive = { "no_such_rule" },
                    FallbackFor = { "no_such_kind" },
                },
            },
        };
        var problems = ContentCategories.Problems(catalog, Content);
        Assert.Contains(problems, p => p.Contains("unknown group 'nowhere'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown item 'no_such_item'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown rule 'no_such_rule'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("unknown item category 'no_such_kind'", StringComparison.Ordinal));
        Assert.Empty(ContentCategories.Build(catalog, Content).OfItem("no_such_item"));
    }

    [Fact]
    public void ShipBlueprints_NameTheirShipUnderUnlocks()
    {
        Assert.Equal(new[] { "corvette" }, BlueprintUnlocks.For(Content, "ship_corvette").Ships);
        Assert.All(Content.Ships.Values.Where(s => !string.IsNullOrEmpty(s.RequiredBlueprint)),
            s => Assert.Contains(s.Key, BlueprintUnlocks.For(Content, s.RequiredBlueprint!).Ships));
        Assert.Empty(BlueprintUnlocks.For(Content, "jetpack").Ships);
    }

    [Fact]
    public void ItemUses_ListRecipesBlueprintsModulesAndShips()
    {
        var plate = ItemUses.For(Content, "iron_plate");
        Assert.False(plate.IsEmpty);
        Assert.Contains(plate.Recipes, r => r.Inputs.Any(i => i.Item == "iron_plate"));
        Assert.DoesNotContain(plate.Recipes, r => r.Station == CraftingStation.Market);
        Assert.Contains("oxygen_tank_1", plate.Blueprints); // its research costs iron plates
        Assert.DoesNotContain("iron_plate", plate.Products);
        Assert.Equal(plate.Products.Count, plate.Products.Distinct().Count());
    }

    [Fact]
    public void ItemUses_CountGroupIngredients_AndSkipTheMarket()
    {
        // The boat asks for "any log": the pale bark log is a member of that group, so it is needed for the boat too.
        Assert.Contains("boat", ItemUses.For(Content, "wood_log_b").Products);
        var ore = ItemUses.For(Content, "iron_ore");
        Assert.DoesNotContain(ore.Recipes, r => r.Key == "market_iron_to_titanium");
        Assert.True(ItemUses.For(Content, "no_such_item").IsEmpty);
    }

    private static HashSet<string> LocaleKeys(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }
}
