// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The block sets of the releases a self-built ship's hull may have been written in before its ids were remapped with
/// the rest of the save (#2751). Numeric block ids are the 1-based positions of the ordinally sorted block keys (air is
/// id 0 and no key of <c>data/blocks.json</c>), so a block set IS a palette. No block was removed since self-built ships
/// exist (v2026.8.12), so every historical set is the base set plus the keys added since.
/// <para>FROZEN: reconstructed from <c>git show &lt;tag&gt;:data/blocks.json</c> for every release from v2026.8.12 to
/// v2026.10.4 on 2026-10-18 and never to be edited. Only releases that changed the set are listed — a release without
/// an entry ran the set of the last one before it. The main build <c>9da3b28b</c> (the bio lab, which the hosted worlds
/// ran between v2026.10.3 and v2026.10.4) already carried the v2026.10.4 set. From v2026.10.4 on the remap covers the
/// hull (#2221), so no later set is needed. <c>SaveCompatibilityTests</c> pins the sizes.</para>
/// </summary>
internal static class HistoricalBlockSets
{
    /// <summary>One historical palette: the release that introduced the set, and id → key (index 0 = air, empty).</summary>
    internal sealed class Palette
    {
        public Palette(string release, IEnumerable<string> keys)
        {
            Release = release;
            var sorted = keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            Keys = new string[sorted.Count + 1];
            Keys[0] = string.Empty;
            IdOf = new Dictionary<string, ushort>(sorted.Count, StringComparer.Ordinal);
            for (int i = 0; i < sorted.Count; i++)
            {
                Keys[i + 1] = sorted[i];
                IdOf[sorted[i]] = (ushort)(i + 1);
            }
        }

        /// <summary>The release that introduced this set (it stayed until the next entry).</summary>
        public string Release { get; }

        /// <summary>Id → key; index 0 is air.</summary>
        public string[] Keys { get; }

        /// <summary>Key → id.</summary>
        public Dictionary<string, ushort> IdOf { get; }

        /// <summary>How many block keys the set has (air not counted).</summary>
        public int Count => Keys.Length - 1;
    }

    private static List<Palette>? _all;

    /// <summary>Every historical palette, oldest first (built on first use — after the key tables below exist).</summary>
    public static IReadOnlyList<Palette> All => _all ??= Build();

    private static List<Palette> Build()
    {
        var keys = new List<string>(BaseKeys);
        var list = new List<Palette> { new("v2026.8.12", keys) };
        foreach (var (release, added) in Additions)
        {
            keys.AddRange(added);
            list.Add(new Palette(release, keys));
        }

        return list;
    }

    /// <summary>The block keys of v2026.8.12, the first release with self-built ships.</summary>
    private static readonly string[] BaseKeys =
    {
        "algae_tank", "alien_grass", "aluminium_block", "aluminium_ore", "ancient_brick", "ash", "basalt", "base_core",
        "beam_block", "bed", "bedrock", "brass_block", "bronze_block", "campfire", "carbon", "cargo_floor",
        "cobalt_block", "cobalt_ore", "concrete", "copper_block", "copper_ore", "crate", "crystal", "data_cache",
        "deepslate", "detoxifier", "diamond_ore", "dirt", "door_energy", "door_hinge", "door_slide", "door_wood",
        "energy_fence", "energy_gate", "engine_nozzle", "engine_panel", "factory_pipe", "factory_terminal", "fire",
        "flora_alienfern", "flora_ashweed", "flora_bellflower", "flora_bulb", "flora_bush", "flora_cactus",
        "flora_cinderbush", "flora_coral", "flora_cropberry", "flora_crystal", "flora_dryshrub", "flora_emberbloom",
        "flora_fern", "flora_flower", "flora_frostflower", "flora_gasbloom", "flora_glowcap", "flora_glowvine",
        "flora_grasstuft", "flora_icereed", "flora_kelp", "flora_lichen", "flora_lily", "flora_moss", "flora_mushroom",
        "flora_orchid", "flora_palm", "flora_pitcher", "flora_plant", "flora_puffball", "flora_reed",
        "flora_rockflower", "flora_saltgrass", "flora_seagrass", "flora_shardbloom", "flora_snowbush",
        "flora_sporepod", "flora_succulent", "flora_tendril", "flora_thornbush", "flora_vine", "flower_pot",
        "force_field", "forge", "geyser_vent", "glass", "gold_block", "gold_ore", "granite", "grass", "heal_tank",
        "hydro_tray", "ice", "insulated_wall", "iron_block", "iron_ore", "iron_wall", "lab_panel", "ladder", "lantern",
        "lava", "lead_block", "lead_ore", "light_green", "light_red", "light_white", "lithium_ore", "machine_block",
        "matter_forge", "medbay_panel", "metal_panel", "mission_board", "mud", "mushroom_cap", "mushroom_stem",
        "mycelium", "neodymium_ore", "nickel_block", "nickel_ore", "obsidian", "palm_frond", "pine_needles",
        "platinum_block", "platinum_ore", "radio_beacon", "rug", "rune_stone", "salt", "sand", "ship_core",
        "ship_engine", "ship_helm", "silicate", "silver_block", "silver_ore", "snow", "stairs", "station_container",
        "station_core", "station_vendor", "steel_floor", "steel_wall", "stone", "strip_light_cyan", "strip_light_warm",
        "sulfur_ore", "tin_block", "tin_ore", "titanium_block", "titanium_ore", "torch", "tree_leaves",
        "tungsten_block", "tungsten_ore", "uranium_ore", "water", "wood_crate", "wood_log", "workbench", "zinc_block",
        "zinc_ore",
    };

    /// <summary>The releases that added blocks, in order, with the keys each one added.</summary>
    private static readonly (string Release, string[] Added)[] Additions =
    {
        ("v2026.8.21", new[] { "flora_cropgrain", "flora_cropshroom", "sentry_post" }),
        ("v2026.8.22", new[] { "glass_clear" }),
        ("v2026.9.3", new[] { "bone", "moss_stone", "sandstone", "scree", "tar" }),
        ("v2026.9.4", new[] { "coral_rock", "peat" }),
        ("v2026.9.5", new[] { "power_relay", "water_spout" }),
        ("v2026.9.6", new[]
        {
            "broken_machine", "flora_hangkelp", "flora_sapling", "flora_sunblossom", "flora_tulip", "gaming_keyboard",
            "gaming_monitor", "gaming_mouse", "gaming_pc", "giant_leaves", "giant_log", "paul_leaf", "paul_petals",
            "paul_stem", "rusted_panel", "scrap_metal", "scrap_pile",
        }),
        ("v2026.9.10", new[]
        {
            "arms_rack", "clinic_post", "press_desk", "quarry_post", "sage_lectern", "shop_counter", "streamer_post",
            "stretcher", "sulfur_stone", "tamer_post",
        }),
        ("v2026.9.11", new[] { "crew_bunk" }),
        ("v2026.9.14", new[] { "thumper" }),
        ("v2026.9.15", new[] { "flora_cavecap", "flora_glowmoss", "flora_glowthread", "flora_prismbloom" }),
        ("v2026.9.16", new[] { "flora_fruit_banana", "flora_fruit_grape", "flora_fruit_long", "flora_fruit_round" }),
        ("v2026.9.17", new[]
        {
            "alarm_siren", "announcer", "auto_drill_1", "auto_drill_2", "auto_drill_3", "caller", "chime",
            "clone_tank", "crystal_button", "crystal_conduit", "crystal_switch", "daylight_sensor", "fabricator",
            "horn", "lantern_off", "light_green_off", "light_red_off", "light_white_off", "logic_block",
            "matter_receiver", "matter_sender", "melody_block", "proximity_sensor", "step_plate", "storage_sensor",
            "strip_light_cyan_off", "strip_light_warm_off", "timer_block", "torch_off", "watcher",
        }),
        ("v2026.9.18", new[]
        {
            "black_sand", "decontaminator", "device_eye", "drill_laser", "fifi_blossom", "fifi_leaf", "fifi_stem",
            "flora_fifi_berries", "gas", "oil", "rail_post", "rail_pylon", "rail_stop", "tainted_copper_ore",
            "tainted_diamond_ore", "tainted_iron_ore", "tainted_log", "tainted_soil", "tainted_stone",
            "tainted_subsoil", "tainted_titanium_ore",
        }),
        ("v2026.9.20", new[] { "gas_dense" }),
        ("v2026.10.4", new[] { "bio_lab", "flora_hybrid" }),
    };
}
