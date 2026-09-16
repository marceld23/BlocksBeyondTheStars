// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One NPC profession beyond the classic posts (Justus' ideas, 2026-09-15): who staffs it (the <see cref="Role"/> the
/// market / dialogue code knows — a trading profession is a <c>vendor</c> with its own <see cref="Job"/> and
/// <see cref="Theme"/>), the marker an author places for it, the block a player builds at home or on their station,
/// the settlement building function that houses it and how that room is furnished.
/// </summary>
public sealed class NpcProfession
{
    public NpcProfession(string job, string role, string theme, string postBlock, string function, string room, string held, string activity,
        bool shopOnly = false, bool keepsPet = false, bool worksOutside = false, bool asksForPhotos = false, bool interviews = false)
    {
        Job = job;
        Role = role;
        Theme = theme;
        PostBlock = postBlock;
        Function = function;
        Room = room;
        Held = held;
        ActivityKey = "npc.activity." + activity;
        ShopOnly = shopOnly;
        KeepsPet = keepsPet;
        WorksOutside = worksOutside;
        AsksForPhotos = asksForPhotos;
        Interviews = interviews;
    }

    /// <summary>The job key, also the marker id and the suffix of every locale key (<c>npc.role.&lt;job&gt;</c>,
    /// <c>npc.greet.&lt;job&gt;</c>, <c>ui.marker.&lt;job&gt;</c>).</summary>
    public string Job { get; }

    /// <summary>"vendor" for a trading profession (market + trade-or-talk), "settler" otherwise.</summary>
    public string Role { get; }

    /// <summary>The market theme of a trading profession (<c>marketTheme</c> in recipes.json), "" otherwise.</summary>
    public string Theme { get; }

    /// <summary>The block a player places at a base or on a station to open this post.</summary>
    public string PostBlock { get; }

    /// <summary>The settlement building function (<see cref="StructureRoles"/>) whose module carries this post.</summary>
    public string Function { get; }

    /// <summary>How the room holding the post marker is furnished: a <c>RoomFurnisher.RoomRole</c> name.</summary>
    public string Room { get; }

    /// <summary>What the NPC carries (a render hint for the client), "" = empty-handed.</summary>
    public string Held { get; }

    /// <summary>The nameplate activity while at work.</summary>
    public string ActivityKey { get; }

    /// <summary>The grocer: offers only while the customer stands in the same closed room as the keeper.</summary>
    public bool ShopOnly { get; }

    /// <summary>The animal tamer: a tamed animal of the planet walks with them.</summary>
    public bool KeepsPet { get; }

    /// <summary>The blockfarmer: works outside the settlement or base by day.</summary>
    public bool WorksOutside { get; }

    /// <summary>The streamer: asks nearby players for a photo, at most once per day each.</summary>
    public bool AsksForPhotos { get; }

    /// <summary>The reporter: interviews players and keeps the place's news.</summary>
    public bool Interviews { get; }

    public string Marker => Job;

    public string NameKey => "npc.role." + Job;

    public string GreetKey => "npc.greet." + Job;

    public bool Trades => Role == "vendor";
}

/// <summary>The profession table — append only: the order is the staffing order at a base and in a settlement.</summary>
public static class NpcProfessions
{
    public static readonly IReadOnlyList<NpcProfession> All = new[]
    {
        new NpcProfession("doctor", "vendor", "medics", "clinic_post", "clinic", "Medbay", "npc_medkit", "healing"),
        new NpcProfession("grocer", "vendor", "grocer", "shop_counter", "shop", "Market", "npc_basket", "selling", shopOnly: true),
        new NpcProfession("arms_dealer", "vendor", "arms", "arms_rack", "armory", "Workshop", "blade", "polishing"),
        new NpcProfession("sage", "vendor", "sage", "sage_lectern", "library", "Hall", "npc_book", "reading"),
        new NpcProfession("tamer", "vendor", "tamer", "tamer_post", "stable", "Storage", "npc_leash", "taming", keepsPet: true),
        new NpcProfession("blockfarmer", "vendor", "blocks", "quarry_post", "quarry", "Storage", "npc_pickaxe", "quarrying", worksOutside: true),
        new NpcProfession("streamer", "settler", string.Empty, "streamer_post", "studio", "Lounge", "npc_camera", "streaming", asksForPhotos: true),
        new NpcProfession("reporter", "settler", string.Empty, "press_desk", "newsroom", "Board", "npc_microphone", "writing", interviews: true),
    };

    private static readonly Dictionary<string, NpcProfession> ByJobMap = Build(p => p.Job);
    private static readonly Dictionary<string, NpcProfession> ByPostBlockMap = Build(p => p.PostBlock);
    private static readonly Dictionary<string, NpcProfession> ByFunctionMap = Build(p => p.Function);

    private static Dictionary<string, NpcProfession> Build(Func<NpcProfession, string> key)
    {
        var map = new Dictionary<string, NpcProfession>(StringComparer.Ordinal);
        foreach (var p in All)
        {
            map[key(p)] = p;
        }

        return map;
    }

    /// <summary>The profession with this job key (also its marker id), or null.</summary>
    public static NpcProfession? ByJob(string? job) => job != null && ByJobMap.TryGetValue(job, out var p) ? p : null;

    /// <summary>The profession whose post marker this is, or null.</summary>
    public static NpcProfession? ByMarker(string? marker) => ByJob(marker);

    /// <summary>The profession whose post block this is, or null.</summary>
    public static NpcProfession? ByPostBlock(string? blockKey) => blockKey != null && ByPostBlockMap.TryGetValue(blockKey, out var p) ? p : null;

    /// <summary>The profession housed by a settlement building of this function, or null.</summary>
    public static NpcProfession? ByFunction(string? function) => function != null && ByFunctionMap.TryGetValue(function, out var p) ? p : null;

    /// <summary>A post marker where someone trades: the classic <c>vendor</c> or a trading profession's post.</summary>
    public static bool IsTradeMarker(string? marker) => marker == "vendor" || ByMarker(marker) is { Trades: true };

    /// <summary>A marker a station crew member staffs: the classic vendor / mission board or any profession post.</summary>
    public static bool IsStaffedPostMarker(string? marker) => marker is "vendor" or "mission_board" || ByMarker(marker) != null;
}
