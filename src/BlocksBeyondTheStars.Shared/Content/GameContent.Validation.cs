// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;

namespace BlocksBeyondTheStars.Shared.Content;

// Cross-reference validation of the loaded content — the second part of GameContent. Validate() is moved here as one
// unchanged block (its internals are earmarked for a later pass); the class <summary> lives in GameContent.cs.
public sealed partial class GameContent
{
    /// <summary>#2640: the lodge professions — a unique key, a known work and yard kind, a held tool, an activity, a market
    /// theme that has market deals, a partner that is another known profession needing no yard of its own (never a miner,
    /// whose work is the lodge's own face), and weights over known traits that are not negative and not all zero. Whether
    /// the <c>npc.role|greet|activity</c> and <c>ui.vendor.theme</c> texts exist in English and German is checked by
    /// <c>LodgeContentTests</c> (the locale tables load lazily).</summary>
    private void ValidateLodges(List<string> problems)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in _lodges)
        {
            if (string.IsNullOrWhiteSpace(l.Key))
            {
                problems.Add("Lodge profession without a key.");
                continue;
            }

            string ctx = $"Lodge profession '{l.Key}'";
            if (!keys.Add(l.Key))
            {
                problems.Add($"{ctx} is defined twice.");
            }

            if (!LodgeRules.IsWorkKind(l.Work))
            {
                problems.Add($"{ctx} has an unknown work '{l.Work}' ({string.Join("|", LodgeRules.WorkKinds)}).");
            }

            if (!LodgeRules.IsYardKind(l.Yard))
            {
                problems.Add($"{ctx} has an unknown yard '{l.Yard}' ({string.Join("|", LodgeRules.YardKinds)}).");
            }

            if (string.IsNullOrWhiteSpace(l.Held) || string.IsNullOrWhiteSpace(l.Activity))
            {
                problems.Add($"{ctx} needs a held tool and an activity.");
            }

            if (string.IsNullOrWhiteSpace(l.Theme)
                || !_recipes.Values.Any(r => r.Station == CraftingStation.Market && string.Equals(r.MarketTheme, l.Theme, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{ctx} trades theme '{l.Theme}', which has no market deals.");
            }

            double total = 0.0;
            foreach (var kv in l.Weights)
            {
                if (!LodgeRules.IsTrait(kv.Key))
                {
                    problems.Add($"{ctx} weighs an unknown trait '{kv.Key}' ({string.Join("|", LodgeRules.Traits)}).");
                }

                if (kv.Value < 0.0)
                {
                    problems.Add($"{ctx} has a negative weight for '{kv.Key}'.");
                }

                total += Math.Max(0.0, kv.Value);
            }

            if (total <= 0.0)
            {
                problems.Add($"{ctx} has no positive weight — no world would ever call for it.");
            }
        }

        foreach (var l in _lodges)
        {
            if (string.IsNullOrEmpty(l.Partner))
            {
                continue;
            }

            var partner = LodgeProfession(l.Partner);
            if (partner is null)
            {
                problems.Add($"Lodge profession '{l.Key}' has an unknown partner '{l.Partner}'.");
            }
            else if (partner.Work == LodgeRules.WorkMine)
            {
                problems.Add($"Lodge profession '{l.Key}' has the partner '{l.Partner}', who mines — only the lodge's own profession has a quarry face.");
            }
        }
    }

    /// <summary>#2601: the wish templates — a unique key, a known kind, a real item (or a valid scan target), a sane count,
    /// known jobs and world condition, a tier in range and a present that names real items. Whether the
    /// <c>wish.&lt;key&gt;.ask|thanks</c> texts exist in English and German is checked by <c>WishContentTests</c> (the
    /// locale tables load lazily, like the chatter lines').</summary>
    private void ValidateWishes(List<string> problems, Action<string, string?> requireItem)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in _wishes)
        {
            if (string.IsNullOrWhiteSpace(w.Key))
            {
                problems.Add("Wish template without a key.");
                continue;
            }

            string ctx = $"Wish template '{w.Key}'";
            if (!keys.Add(w.Key))
            {
                problems.Add($"{ctx} is defined twice.");
            }

            if (w.Kind == WishTemplate.KindScan)
            {
                if (!ScanTargets.IsValid(w.Item, k => _blocks.ContainsKey(k)))
                {
                    problems.Add($"{ctx} has an invalid scan target '{w.Item}'.");
                }
            }
            else if (w.Kind is WishTemplate.KindDeliver or WishTemplate.KindShow)
            {
                if (string.IsNullOrEmpty(w.Item))
                {
                    problems.Add($"{ctx} names no item.");
                }

                requireItem(ctx, w.Item);
                ValidateItemGroup(problems, ctx, new ItemAmount(w.Item, w.Count) { Group = w.Group }); // #2958
            }
            else
            {
                problems.Add($"{ctx} has an unknown kind '{w.Kind}' (deliver|show|scan).");
            }

            if (w.Count < 1 || w.Jitter < 0 || w.Count - w.Jitter < 1)
            {
                problems.Add($"{ctx} needs a count of at least 1 that its jitter can never take to 0 ({w.Count} ± {w.Jitter}).");
            }

            foreach (string job in w.Jobs)
            {
                if (Array.IndexOf(WishTemplate.EligibleJobs, job ?? string.Empty) < 0)
                {
                    problems.Add($"{ctx} names a job that has no wishes: '{job}'.");
                }
            }

            if (Array.IndexOf(WishTemplate.KnownRequirements, w.Requires) < 0)
            {
                problems.Add($"{ctx} requires something unknown: '{w.Requires}'.");
            }

            if (w.Tier < 1 || w.Tier > WishTemplate.MaxTier)
            {
                problems.Add($"{ctx} has a tier outside 1..{WishTemplate.MaxTier} ({w.Tier}).");
            }

            if (w.Knowledge < 0 || (w.Gift.Count == 0 && w.Knowledge == 0))
            {
                problems.Add($"{ctx} says thank you with nothing (no gift, no knowledge).");
            }

            foreach (var gift in w.Gift)
            {
                if (gift is null || string.IsNullOrEmpty(gift.Item) || gift.Count < 1)
                {
                    problems.Add($"{ctx} has a gift without an item or with a count below 1.");
                    continue;
                }

                requireItem(ctx + " gift", gift.Item);
            }
        }
    }

    /// <summary>
    /// Cross-validates all references between definitions. Throws
    /// <see cref="ContentValidationException"/> if anything is inconsistent.
    /// </summary>
    public void Validate()
    {
        var problems = new List<string>();

        void RequireItem(string ctx, string? itemKey)
        {
            if (!string.IsNullOrEmpty(itemKey) && !_items.ContainsKey(itemKey!))
            {
                problems.Add($"{ctx} references unknown item '{itemKey}'.");
            }
        }

        void RequireBlueprint(string ctx, string? bpKey)
        {
            if (!string.IsNullOrEmpty(bpKey) && !_blueprints.ContainsKey(bpKey!))
            {
                problems.Add($"{ctx} references unknown blueprint '{bpKey}'.");
            }
        }

        foreach (var block in _blocks.Values)
        {
            if (string.IsNullOrWhiteSpace(block.Key))
            {
                problems.Add("Block definition has an empty or whitespace key.");
            }
            if (block.NumericId.Value >= AtlasTileCapacity)
            {
                problems.Add($"Block '{block.Key}' has numeric ID {block.NumericId.Value} exceeding the {AtlasTileCapacity}-tile atlas limit.");
            }

            foreach (var drop in block.Drops)
            {
                RequireItem($"Block '{block.Key}' drop", drop.Item);
            }

            // #1761: a weighted table needs at least one real row with a positive weight, and every named row
            // must be an item (the empty item is the deliberate "nothing" outcome).
            if (block.RandomDrops is { } table)
            {
                int weight = 0;
                foreach (var row in table)
                {
                    RequireItem($"Block '{block.Key}' random drop", row.Item);
                    if (row.Weight < 0)
                    {
                        problems.Add($"Block '{block.Key}' random drop '{row.Item}' has a negative weight.");
                    }

                    weight += System.Math.Max(0, row.Weight);
                }

                if (weight <= 0)
                {
                    problems.Add($"Block '{block.Key}' declares randomDrops without any positive weight.");
                }
            }
        }

        // #1763: an authored species must pass the same sanity band a rolled one always meets.
        foreach (var creature in _authoredCreatures.Values)
        {
            if (string.IsNullOrWhiteSpace(creature.Key))
            {
                problems.Add("Authored creature has an empty key.");
            }

            RequireItem($"Authored creature '{creature.Key}' drop", creature.DropItem);
            if (creature.Size < 0.3f || creature.Size > 6f || creature.Speed <= 0f || creature.SocialGroupSize < 1)
            {
                problems.Add($"Authored creature '{creature.Key}' has a size, speed or group size outside the sane band.");
            }

            foreach (var surface in creature.BiomeSurfaces)
            {
                RequireBlock($"Authored creature '{creature.Key}' biome surface", surface);
            }
        }

        foreach (var item in _items.Values)
        {
            if (!string.IsNullOrEmpty(item.PlacesBlock) && !_blocks.ContainsKey(item.PlacesBlock!))
            {
                problems.Add($"Item '{item.Key}' places unknown block '{item.PlacesBlock}'.");
            }

            if (string.IsNullOrWhiteSpace(item.Key))
            {
                problems.Add("Item definition has an empty or whitespace key.");
            }

            if (item.MaxStack < 1)
            {
                problems.Add($"Item '{item.Key}' has invalid MaxStack {item.MaxStack}; must be >= 1.");
            }
        }

        foreach (var recipe in _recipes.Values)
        {
            RequireBlueprint($"Recipe '{recipe.Key}'", recipe.RequiredBlueprint);
            foreach (var input in recipe.Inputs)
            {
                RequireItem($"Recipe '{recipe.Key}' input", input.Item);
                ValidateItemGroup(problems, $"Recipe '{recipe.Key}' input", input);
                if (input.Count < 1)
                {
                    problems.Add($"Recipe '{recipe.Key}' input '{input.Item}' has invalid amount {input.Count}; must be >= 1.");
                }
            }

            foreach (var output in recipe.Outputs)
            {
                RequireItem($"Recipe '{recipe.Key}' output", output.Item);
                if (output.Count < 1)
                {
                    problems.Add($"Recipe '{recipe.Key}' output '{output.Item}' has invalid amount {output.Count}; must be >= 1.");
                }
            }

            if (recipe.Outputs.Count == 0)
            {
                problems.Add($"Recipe '{recipe.Key}' has no outputs.");
            }

            // #2958: two ingredients drawing on the same item (a log and "any log") would both count the same stack.
            var drawn = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in recipe.Inputs)
            {
                foreach (var key in new[] { input.Item }.Concat(GroupMembersOf(input)).Distinct(StringComparer.Ordinal))
                {
                    if (!drawn.Add(key))
                    {
                        problems.Add($"Recipe '{recipe.Key}' draws on '{key}' in two ingredients.");
                    }
                }
            }

            // A recipe that consumes what it produces is a data error (a free loop or a no-op), not a catalyst (#1048).
            var inputKeys = new HashSet<string>(recipe.Inputs.Select(i => i.Item)
                .Concat(recipe.Inputs.SelectMany(i => GroupMembersOf(i))), StringComparer.Ordinal); // #2958: any member
            foreach (var output in recipe.Outputs)
            {
                if (inputKeys.Contains(output.Item))
                {
                    problems.Add($"Recipe '{recipe.Key}' cannot use '{output.Item}' as both input and output.");
                }
            }
        }

        foreach (var bp in _blueprints.Values)
        {
            if (string.IsNullOrWhiteSpace(bp.Key))
            {
                problems.Add("Blueprint definition has an empty or whitespace key.");
            }

            foreach (var pre in bp.Prerequisites)
            {
                RequireBlueprint($"Blueprint '{bp.Key}' prerequisite", pre);
            }

            foreach (var cost in bp.UnlockCost)
            {
                RequireItem($"Blueprint '{bp.Key}' unlock cost", cost.Item);
                ValidateItemGroup(problems, $"Blueprint '{bp.Key}' unlock cost", cost);
            }
        }

        foreach (var module in _shipModules.Values)
        {
            RequireBlueprint($"Ship module '{module.Key}'", module.RequiredBlueprint);
            foreach (var cost in module.BuildCost)
            {
                RequireItem($"Ship module '{module.Key}' build cost", cost.Item);
            }
        }

        foreach (var ship in _ships.Values)
        {
            RequireBlueprint($"Ship '{ship.Key}'", ship.RequiredBlueprint);
            foreach (var cost in ship.CraftCost)
            {
                RequireItem($"Ship '{ship.Key}' craft cost", cost.Item);
            }

            foreach (var mod in ship.StartModules)
            {
                if (!_shipModules.ContainsKey(mod))
                {
                    problems.Add($"Ship '{ship.Key}' references unknown module '{mod}'.");
                }
            }
        }

        void RequireBlock(string ctx, string? blockKey)
        {
            if (!string.IsNullOrEmpty(blockKey) && !_blocks.ContainsKey(blockKey!))
            {
                problems.Add($"{ctx} references unknown block '{blockKey}'.");
            }
        }

        foreach (var planet in _planets.Values)
        {
            if (string.IsNullOrWhiteSpace(planet.SurfaceBlock) || planet.SurfaceBlock == "air")
            {
                problems.Add($"Planet '{planet.Key}' surface block cannot be empty or air.");
            }
            RequireBlock($"Planet '{planet.Key}' surface", planet.SurfaceBlock);
            RequireBlock($"Planet '{planet.Key}' sub-surface", planet.SubSurfaceBlock);
            RequireBlock($"Planet '{planet.Key}' deep", planet.DeepBlock);
            RequireBlock($"Planet '{planet.Key}' beach", planet.BeachBlock);
            RequireBlock($"Planet '{planet.Key}' seabed", planet.SeabedBlock); // #1757
            if (planet.RuinsBias < 0 || planet.FactoriesBias < 0)
            {
                problems.Add($"Planet '{planet.Key}' has a negative ruins/factories bias.");
            }

            // Generation 8 (2026-09, Titas + Valuma).
            if (planet.EnemyDensity < 0 || planet.SnowCoverDepth < 0 || planet.SnowCoverDepth > 32 || planet.IceSheetDepth < 0 || planet.IceSheetDepth > 16
                || planet.HotZoneShare < 0 || planet.HotZoneShare > 0.5 || planet.WaterDamagePerSecond < 0
                || planet.ExposureMinutesCold < 0 || planet.ExposureMinutesHot < 0 || planet.MaxAquaticSpecies < -1)
            {
                problems.Add($"Planet '{planet.Key}' has a generation-8 field out of range.");
            }

            if (planet.HotZoneShare > 0 && (!planet.Biomes.Any(b => b.HotZone) || !planet.Biomes.Any(b => !b.HotZone)))
            {
                problems.Add($"Planet '{planet.Key}' has a hot-zone share but not both a hot and a cool biome.");
            }

            foreach (var kind in planet.AllowedStructures)
            {
                if (kind is not ("sps_labs" or "net_fragments"))
                {
                    problems.Add($"Planet '{planet.Key}' allows unknown structure kind '{kind}'.");
                }
            }

            // Generation 15 (#2062, Toxica-Maxima): the new type fields name real content, and only the forms the client draws.
            RequireBlock($"Planet '{planet.Key}' dead-tree block", planet.DeadTreeBlock);
            if (planet.Precipitation.Length > 0
                && planet.Precipitation is not ("rain" or "drizzle" or "snow" or "sleet" or "hail" or "sandstorm" or "dust" or "ash" or "acid" or "spores"))
            {
                problems.Add($"Planet '{planet.Key}' names unknown precipitation '{planet.Precipitation}'.");
            }

            if (planet.Weather.Length > 0 && !string.Equals(planet.Weather, "dynamic", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(planet.Weather, "clear", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(planet.Weather, "overcast", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(planet.Weather, "stormy", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Planet '{planet.Key}' names unknown weather mode '{planet.Weather}'.");
            }

            if (planet.FactoryCount.Count != 0 && (planet.FactoryCount.Count != 2 || planet.FactoryCount[0] < 1 || planet.FactoryCount[1] < planet.FactoryCount[0]))
            {
                problems.Add($"Planet '{planet.Key}' factoryCount must be [min, max] with 1 <= min <= max.");
            }

            foreach (var recipeKey in planet.FactoryRecipes)
            {
                if (!_recipes.TryGetValue(recipeKey, out var factoryRecipe))
                {
                    problems.Add($"Planet '{planet.Key}' names unknown factory recipe '{recipeKey}'.");
                }
                else if (factoryRecipe.Station != CraftingStation.Factory)
                {
                    problems.Add($"Planet '{planet.Key}' factory recipe '{recipeKey}' is not a factory recipe.");
                }
            }

            // #1763: every authored key must exist, and an "authored" roster must name at least one.
            foreach (var key in planet.AuthoredCreatures)
            {
                if (!_authoredCreatures.ContainsKey(key))
                {
                    problems.Add($"Planet '{planet.Key}' names unknown authored creature '{key}'.");
                }
            }

            if (string.Equals(planet.CreatureAbundance, "authored", StringComparison.OrdinalIgnoreCase) && planet.AuthoredCreatures.Count == 0)
            {
                problems.Add($"Planet '{planet.Key}' has creatureAbundance \"authored\" but names no authored creature.");
            }
            // Terrain tags (#1644): resolved once here so worldgen reads a flags enum, never the string list.
            planet.Tags = TerrainTags.Parse(planet.TerrainTags, out var unknownTag);
            // City worlds (#1793): the composer key must be one the server knows, and every authored outfit
            // must be a colour — parsed once here so the NPC caster never re-reads strings.
            if (planet.CityWorld.Length > 0 && !string.Equals(planet.CityWorld, "gds", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"Planet '{planet.Key}' names unknown city composer '{planet.CityWorld}'.");
            }

            var outfits = new List<uint>();
            foreach (var hex in planet.NpcOutfits)
            {
                if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
                {
                    outfits.Add(rgb);
                }
                else
                {
                    problems.Add($"Planet '{planet.Key}' has a malformed npcOutfits colour '{hex}' (expected RRGGBB).");
                }
            }

            planet.NpcOutfitRgb = outfits.ToArray();
            if (unknownTag is not null)
            {
                problems.Add($"Planet '{planet.Key}' carries unknown terrain tag '{unknownTag}'.");
            }

            foreach (var biome in planet.Biomes)
            {
                RequireBlock($"Planet '{planet.Key}' biome surface", biome.SurfaceBlock);
                RequireBlock($"Planet '{planet.Key}' biome sub-surface", biome.SubSurfaceBlock);
            }
            foreach (var ore in planet.Ores)
            {
                RequireBlock($"Planet '{planet.Key}' ore", ore.Block);
                // Frontier boost eligibility (#1122): derived from the block's tool gate, so the ore
                // data never has to repeat (and can never contradict) the tier.
                ore.RareTier = (GetBlock(ore.Block)?.MinToolTier ?? 0) >= 2;
            }
        }

        foreach (var mission in _missions.Values)
        {
            foreach (var reward in mission.Rewards)
            {
                RequireItem($"Mission '{mission.Id}' reward", reward.Item);
            }

            foreach (var obj in mission.Objectives)
            {
                switch (obj.Type)
                {
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Mine:
                        RequireBlock($"Mission '{mission.Id}' mine objective", obj.Target);
                        break;
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Collect:
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Deliver:
                        RequireItem($"Mission '{mission.Id}' objective", obj.Target);
                        break;
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Scan:
                        if (!BlocksBeyondTheStars.Shared.Missions.ScanTargets.IsValid(obj.Target, _blocks.ContainsKey))
                        {
                            problems.Add($"Mission '{mission.Id}' scan objective has an unknown target '{obj.Target}' (see ScanTargets).");
                        }
                        break;
                    case BlocksBeyondTheStars.Shared.Missions.MissionObjectiveType.Circuit: // #2258
                        if (!BlocksBeyondTheStars.Shared.Missions.CircuitEvents.IsObjective(obj.Target))
                        {
                            problems.Add($"Mission '{mission.Id}' circuit objective has an unknown target '{obj.Target}' (see CircuitEvents).");
                        }
                        break;
                }
            }

            if (!string.IsNullOrEmpty(mission.RequiresBlueprint) && !_blueprints.ContainsKey(mission.RequiresBlueprint))
            {
                problems.Add($"Mission '{mission.Id}' requiresBlueprint references unknown blueprint '{mission.RequiresBlueprint}'.");
            }

            // Mission chains (#1212): the chain vocabulary + every id reference must resolve at load.
            if (mission.Step > 0 && string.IsNullOrEmpty(mission.ChainId))
            {
                problems.Add($"Mission '{mission.Id}' has step {mission.Step} but no chainId.");
            }

            if (!string.IsNullOrEmpty(mission.ChainId) && mission.Step <= 0)
            {
                problems.Add($"Mission '{mission.Id}' belongs to chain '{mission.ChainId}' but has no step (1-based).");
            }

            foreach (var pre in mission.Prerequisites)
            {
                if (!_missions.ContainsKey(pre))
                {
                    problems.Add($"Mission '{mission.Id}' prerequisite references unknown mission '{pre}'.");
                }
                else if (pre == mission.Id)
                {
                    problems.Add($"Mission '{mission.Id}' lists itself as a prerequisite.");
                }
            }

            if (!string.IsNullOrEmpty(mission.NextMissionId) && !_missions.ContainsKey(mission.NextMissionId))
            {
                problems.Add($"Mission '{mission.Id}' nextMissionId references unknown mission '{mission.NextMissionId}'.");
            }

            if (!BlocksBeyondTheStars.Shared.Missions.MissionChains.IsValidGiverRole(mission.GiverRole))
            {
                problems.Add($"Mission '{mission.Id}' has an unknown giverRole '{mission.GiverRole}' (quartermaster|vendor|settler|vega|character:<id>).");
            }
            else if (BlocksBeyondTheStars.Shared.Missions.MissionChains.IsVegaGiver(mission)
                     && BlocksBeyondTheStars.Shared.Missions.MissionChains.SurfaceOf(mission) != BlocksBeyondTheStars.Shared.Missions.MissionChains.SurfaceRadio)
            {
                problems.Add($"Mission '{mission.Id}' is handed out by VEGA (giverRole 'vega'), which needs surface 'radio' — VEGA has no board or face of her own.");
            }

            if (!BlocksBeyondTheStars.Shared.Missions.MissionChains.IsValidStage(mission.MinStage))
            {
                problems.Add($"Mission '{mission.Id}' has an unknown minStage '{mission.MinStage}' (known|trusted).");
            }

            if (!BlocksBeyondTheStars.Shared.Missions.MissionChains.IsValidSurface(mission.Surface))
            {
                problems.Add($"Mission '{mission.Id}' has an unknown surface '{mission.Surface}' (board|radio|dialog).");
            }

            if (!BlocksBeyondTheStars.Shared.Missions.MissionChains.IsValidOfferAt(mission.OfferAt))
            {
                problems.Add($"Mission '{mission.Id}' has an unknown offerAt '{mission.OfferAt}' (settlement|station|any).");
            }

            if (!BlocksBeyondTheStars.Shared.Missions.MissionChains.IsValidRequiresStory(mission.RequiresStory))
            {
                problems.Add($"Mission '{mission.Id}' has an unknown requiresStory '{mission.RequiresStory}' (guardian_defeated).");
            }
        }

        // NPC dialogues handing out missions (#1212): the `mission:<id>` consequence must name a known mission.
        foreach (var dialog in _dialogs)
        {
            // #2594: a typo in "home" would silently hide the dialogue from everyone.
            if (!DialogDefinition.IsValidHome(dialog.Home))
            {
                problems.Add($"Dialogue '{dialog.Key}' has an unknown home '{dialog.Home}' (own_base|away|lodge|\"\").");
            }

            foreach (var node in dialog.Nodes)
            {
                foreach (var choice in node.Choices)
                {
                    var parts = (choice.Consequence ?? string.Empty).Split(':');
                    if (parts.Length >= 2 && parts[0] == BlocksBeyondTheStars.Shared.Missions.MissionChains.DialogConsequence
                        && !_missions.ContainsKey(parts[1]))
                    {
                        problems.Add($"Dialogue '{dialog.Key}' hands out unknown mission '{parts[1]}'.");
                    }

                    // #2602: a guest's present — "guest:gift:<item>:<n>" must name a real item and a positive count.
                    if (parts.Length >= 2 && parts[0] == "guest" && parts[1] == "gift"
                        && (parts.Length < 4 || GetItem(parts[2]) is null
                            || !int.TryParse(parts[3], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int giftCount)
                            || giftCount <= 0))
                    {
                        problems.Add($"Dialogue '{dialog.Key}' has a bad guest present '{choice.Consequence}' (guest:gift:<item>:<n>).");
                    }
                }
            }
        }

        // #2598: a villager friend's presents name real items in a sane amount — a typo would hand out nothing.
        foreach (var gift in VillageLife.FriendGifts)
        {
            if (gift is null || string.IsNullOrEmpty(gift.Item))
            {
                problems.Add("Village life: a friend gift without an item.");
                continue;
            }

            RequireItem("Village life friend gift", gift.Item);
            if (gift.Count < 1)
            {
                problems.Add($"Village life friend gift '{gift.Item}' has a count below 1 ({gift.Count}).");
            }
        }

        // #2600: villager chatter topics — a typo in "when" or "needs" would make a topic silently never come up. Whether
        // the line keys resolve in English and German, stay within VillageChatterTopic.MaxLineChars and use only the
        // placeholders their need provides is checked by VillageLifeContentTests: the locale tables load lazily and a
        // too-long translation must never stop a server (or the client) from starting.
        var chatterKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topic in VillageLife.Chatter)
        {
            if (topic is null || string.IsNullOrWhiteSpace(topic.Key))
            {
                problems.Add("Village life: a chatter topic without a key.");
                continue;
            }

            if (!chatterKeys.Add(topic.Key))
            {
                problems.Add($"Village life chatter topic '{topic.Key}' is defined twice.");
            }

            if (topic.When is not ("day" or "evening" or "any"))
            {
                problems.Add($"Village life chatter topic '{topic.Key}' has an unknown when '{topic.When}' (day|evening|any).");
            }

            if (!VillageChatterNeeds.IsKnown(topic.Needs))
            {
                problems.Add($"Village life chatter topic '{topic.Key}' needs something unknown: '{topic.Needs}'.");
            }

            if (topic.Weight < 1)
            {
                problems.Add($"Village life chatter topic '{topic.Key}' has a weight below 1 ({topic.Weight}).");
            }

            if (topic.Lines.Count < VillageChatterTopic.MinLines || topic.Lines.Count > VillageChatterTopic.MaxLines
                || topic.Lines.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"Village life chatter topic '{topic.Key}' needs {VillageChatterTopic.MinLines}–{VillageChatterTopic.MaxLines} non-empty line keys.");
            }
        }

        // #2604: the festival's present names real items in a sane amount, and its cheers are distinct line keys (whether
        // they resolve in English and German is VillageFestivalTests' job, like the chatter lines above).
        foreach (var gift in VillageLife.Festival.Gift)
        {
            if (gift is null || string.IsNullOrEmpty(gift.Item))
            {
                problems.Add("Village life: a festival gift without an item.");
                continue;
            }

            RequireItem("Village life festival gift", gift.Item);
            if (gift.Count < 1)
            {
                problems.Add($"Village life festival gift '{gift.Item}' has a count below 1 ({gift.Count}).");
            }
        }

        var cheerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cheer in VillageLife.Festival.Cheers)
        {
            if (string.IsNullOrWhiteSpace(cheer))
            {
                problems.Add("Village life: an empty festival cheer key.");
            }
            else if (!cheerKeys.Add(cheer))
            {
                problems.Add($"Village life festival cheer '{cheer}' is listed twice.");
            }
        }

        ValidateWishes(problems, RequireItem);
        ValidateLodges(problems);

        // #427: authored voxel ship layouts. The server used to turn an unknown cell id silently into hull
        // (`?? iron_wall`), so a data typo would ship invisibly — surface it at load like every other reference.
        foreach (var layout in _shipLayouts.Values)
        {
            foreach (var cell in layout.Cells)
            {
                if (cell.Kind == "station")
                {
                    continue; // station types map to marker blocks server-side (unknown → hull marker, by design)
                }

                if (string.IsNullOrEmpty(cell.Id) || (!ShipLayoutCell.IsElementId(cell.Id) && !_blocks.ContainsKey(cell.Id)))
                {
                    problems.Add($"Ship layout '{layout.Key}' cell ({cell.X},{cell.Y},{cell.Z}) references unknown block or element '{cell.Id}'.");
                }
            }
        }

        // #2352: the space salvage data — every loot row names a real item with a chance in 0..1 and a sane range, every
        // theme's blocks exist, and the weights are not negative. Only for data from the file: the built-in defaults
        // name the shipped items, which a minimal test content does not define.
        if (_spaceSalvageFromData)
        {
            void CheckRolls(string ctx, IEnumerable<SalvageRoll> rolls)
            {
                foreach (var r in rolls)
                {
                    RequireItem(ctx, r.Item);
                    if (r.Chance < 0.0 || r.Chance > 1.0)
                    {
                        problems.Add($"{ctx} '{r.Item}' has a chance outside 0..1 ({r.Chance}).");
                    }

                    if (r.Min < 0 || r.Max < r.Min)
                    {
                        problems.Add($"{ctx} '{r.Item}' has an invalid count range {r.Min}..{r.Max}.");
                    }
                }
            }

            foreach (var (themeKey, theme) in SpaceSalvage.Themes)
            {
                string ctx = $"Debris theme '{themeKey}'";
                if (theme.Weight < 0)
                {
                    problems.Add($"{ctx} has a negative weight.");
                }

                RequireBlock(ctx, theme.Hull);
                RequireBlock(ctx, theme.Accent);
                RequireBlock(ctx, theme.Scorch);
                CheckRolls(ctx + " fragment loot", theme.FragmentLoot);
                CheckRolls(ctx + " capsule loot", theme.CapsuleLoot);
            }

            CheckRolls("Combat debris loot", SpaceSalvage.CombatDebris.Loot);
            foreach (var row in SpaceSalvage.Wreck.Human.Concat(SpaceSalvage.Wreck.Alien))
            {
                RequireItem("Space wreck salvage", row.Item);
                if (row.Chance < 0.0 || row.Chance > 1.0)
                {
                    problems.Add($"Space wreck salvage '{row.Item}' has a chance outside 0..1 ({row.Chance}).");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new ContentValidationException(problems);
        }
    }
}
