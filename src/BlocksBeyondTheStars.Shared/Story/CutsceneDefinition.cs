// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Story;

// The data-driven cutscene model (#2467, synthesis §6.2/§6.3): a pack's data/stories/<pack>/cutscenes.json, loaded by
// ContentLoader next to story.json into StoryDefinition.Cutscenes. Server and client read the same file: the server
// only sends an id (CutscenePlay, tag 295) and keeps the per-player seen ledger; the client resolves shots and texts
// from its own copy. Reference for authors: docs/developer/CUTSCENES.md.

/// <summary>The root object of a pack's <c>cutscenes.json</c> (<c>{ "cutscenes": [ … ] }</c>; a bare array is accepted
/// too).</summary>
public sealed class StoryCutsceneFile
{
    /// <summary>The pack's cutscene definitions.</summary>
    public List<CutsceneDefinition> Cutscenes { get; set; } = new();
}

/// <summary>
/// One cutscene of a pack (synthesis §6.3). <see cref="Tier"/> says how heavy it is (§6.2: T1 banner, T2 card, T3 staged
/// scene, T4 set piece — T0 is a plain VEGA line and never a cutscene); <see cref="Shots"/> are played in order. A scene
/// is presentation on top of the game: the trigger keeps sending its own narrator line for every client (an older client
/// and the log need it), and a client that plays the scene drops the lines named in <see cref="ReplaceLineKeys"/> from
/// the VEGA panel.
/// </summary>
public sealed class CutsceneDefinition
{
    /// <summary>Id, unique within the pack (letters, digits, <c>_</c>, <c>-</c>, <c>.</c>; ids starting with <c>__</c>
    /// are built in). The official packs follow synthesis §5.3 (<see cref="StoryCutsceneIds"/>). Ledger key
    /// <c>story:&lt;pack&gt;:cut:&lt;id&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The presentation tier (<see cref="CutsceneTiers"/>): 1 banner, 2 card, 3 staged scene, 4 set piece.</summary>
    public int Tier { get; set; } = CutsceneTiers.Card;

    /// <summary>The pack chapter the scene belongs to (Story-tab grouping, the recap card); empty = none.</summary>
    public string Chapter { get; set; } = string.Empty;

    /// <summary>A template used by several bindings (the tutorial's chapter card, d4 §4.1): every binding is its own
    /// instance with its own ledger entry (<c>&lt;id&gt;@&lt;chapter or step&gt;</c>, <see cref="StoryCutsceneIds.Instance"/>)
    /// and the bound chapter's title as the argument. A non-template scene plays once per player however often it is
    /// bound (the prologue is the pack intro AND chapter 0's opener).</summary>
    public bool Template { get; set; }

    /// <summary>Narrator lines the scene shows itself: a client that plays it keeps them out of the VEGA panel (they still
    /// go to the log). Resolved from the client's OWN definition, so a client without the scene drops nothing.</summary>
    public List<string> ReplaceLineKeys { get; set; } = new();

    /// <summary>Music track of the scene (a file name in <c>client/Music</c> without extension, e.g.
    /// <c>music_mystery_signal</c>); empty = the music keeps playing.</summary>
    public string Music { get; set; } = string.Empty;

    /// <summary>What fills the captions' <c>{0}</c> when the server plays the scene (#2473, <see cref="CutsceneArgSources"/>):
    /// empty = the bound chapter (its title key, then its place and the count, <see cref="CutsceneArgs.For"/>); <c>body</c>
    /// = the name of the body the player is on (the tutorial's first landing); <c>system</c> = the name of the player's star
    /// system.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>The shots, played in order (at least one; a banner is one <c>player_view</c> shot).</summary>
    public List<CutsceneShot> Shots { get; set; } = new();

    /// <summary>World hold, HUD, input, skip and chaining options.</summary>
    public CutsceneOptions Options { get; set; } = new();

    /// <summary>Whether any shot takes the camera (<see cref="CutsceneCameras.TakesCamera"/>).</summary>
    public bool TakesCamera => Shots?.Any(s => CutsceneCameras.TakesCamera(s?.Camera?.Type)) == true;

    /// <summary>Whether the scene holds a singleplayer world: <see cref="CutsceneOptions.HoldWorld"/>, else T3/T4 scenes that
    /// take the camera (synthesis §5.3 "Stufen": a card never holds the world).</summary>
    public bool EffectiveHoldWorld => Options?.HoldWorld ?? (Tier >= CutsceneTiers.Scene && TakesCamera);

    /// <summary>Whether the HUD goes away: <see cref="CutsceneOptions.HideHud"/>, else from T3 on (a T2 card only dims it).</summary>
    public bool EffectiveHideHud => Options?.HideHud ?? Tier >= CutsceneTiers.Scene;

    /// <summary>Whether the player's controls freeze: <see cref="CutsceneOptions.LockInput"/>, else when a T3/T4 scene takes
    /// the camera (a card keeps the input free).</summary>
    public bool EffectiveLockInput => Options?.LockInput ?? (Tier >= CutsceneTiers.Scene && TakesCamera);
}

/// <summary>One shot of a cutscene: a camera, how long it lasts, its captions, colour grade and sound cues.</summary>
public sealed class CutsceneShot
{
    /// <summary>Where the camera is.</summary>
    public CutsceneCamera Camera { get; set; } = new();

    /// <summary>Seconds the shot lasts (a page-gated speaker caption may hold it longer, step 9).</summary>
    public float Duration { get; set; }

    /// <summary>The shot's captions.</summary>
    public List<CutsceneCaption> Captions { get; set; } = new();

    /// <summary>Colour grade (<see cref="CutsceneGrades"/>): <c>none</c>, <c>memory</c> or <c>flashback</c>.</summary>
    public string Grade { get; set; } = CutsceneGrades.None;

    /// <summary>Sound cues of the shot.</summary>
    public List<CutsceneSfx> Sfx { get; set; } = new();
}

/// <summary>The camera of a shot: a type (<see cref="CutsceneCameras"/>) and what it looks at (<see cref="CutsceneAnchors"/>).</summary>
public sealed class CutsceneCamera
{
    /// <summary>Camera type; default <c>player_view</c> (letterbox and captions over the live view).</summary>
    public string Type { get; set; } = CutsceneCameras.PlayerView;

    /// <summary>What the camera is placed around; empty = <c>player</c>. <c>custom</c> = the coordinates of the
    /// <c>CutscenePlay</c> message (a server-only position such as the core chamber).</summary>
    public string Anchor { get; set; } = string.Empty;
}

/// <summary>One caption of a shot: a locale key in a style, shown from <see cref="At"/> for <see cref="Hold"/> seconds.
/// A <c>{0}</c> in the text is filled with the play's argument.</summary>
public sealed class CutsceneCaption
{
    /// <summary>Locale key of the text (core or pack table; English and German mandatory).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Caption style (<see cref="CutsceneCaptionStyles"/>); default <c>subtitle</c>.</summary>
    public string Style { get; set; } = CutsceneCaptionStyles.Subtitle;

    /// <summary><c>title_card</c> only: locale key of the small line above the title ("Prologue", "Chapter 1"); empty = none.</summary>
    public string KickerKey { get; set; } = string.Empty;

    /// <summary><c>speaker</c> only: the narrator id of the speaker box; empty = the pack's narrator.</summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>Seconds into the shot when the caption appears.</summary>
    public float At { get; set; }

    /// <summary>Seconds the caption stays; 0 = until the shot ends.</summary>
    public float Hold { get; set; }
}

/// <summary>A sound cue of a shot: a clip in <c>client/Assets/Resources/audio</c> (name without extension) at a time.</summary>
public sealed class CutsceneSfx
{
    /// <summary>The clip name (e.g. <c>ai_blip</c>).</summary>
    public string Cue { get; set; } = string.Empty;

    /// <summary>Seconds into the shot.</summary>
    public float At { get; set; }
}

/// <summary>The options of a cutscene. Unset (null) world/HUD/input switches follow the tier (synthesis §6.2).</summary>
public sealed class CutsceneOptions
{
    /// <summary>Hold the singleplayer world while the scene plays — only a T3/T4 scene that takes the camera may
    /// (synthesis §5.3 "Stufen"); null = derived.</summary>
    public bool? HoldWorld { get; set; }

    /// <summary>Hide the HUD; null = from T3 on.</summary>
    public bool? HideHud { get; set; }

    /// <summary>Freeze the player's controls — only when the camera is taken; null = derived.</summary>
    public bool? LockInput { get; set; }

    /// <summary>Whether the player may skip the scene (default true).</summary>
    public bool Skippable { get; set; } = true;

    /// <summary>Seconds the scene shows before a skip is accepted (against an accidental double press); 0 = at once.</summary>
    public float MinDisplay { get; set; }

    /// <summary>Scenes that may follow this one at once instead of after the 90 s cooldown (synthesis §5.3
    /// "Verkettungen": prologue → <c>tut.welcome</c>, graduation → <c>ch1.open</c>/recap, B12 → reveal): a cutscene id of
    /// the same pack, <c>&lt;pack&gt;/&lt;id&gt;</c> for another pack, or <c>__recap</c>. Chaining only lifts the
    /// cooldown — it never plays the follower by itself.</summary>
    public List<string> ChainNext { get; set; } = new();

    /// <summary>A "now or never" moment (first launch, after-warp card, finale arrival): never waits for the cooldown.</summary>
    public bool CooldownExempt { get; set; }

    /// <summary>Whether the Story tab may replay the scene once seen (default true).</summary>
    public bool Replayable { get; set; } = true;

    /// <summary>#2474: lines of <see cref="CutsceneDefinition.ReplaceLineKeys"/> the player must not miss: when the player
    /// skips the scene before such a line's caption was on screen, the client gives the line back to the narrator panel
    /// (VEGA's Guardian reveal carries the jump-generator requirement). Every other replaced line stays in the log only.</summary>
    public List<string> ReturnOnSkip { get; set; } = new();
}

/// <summary>The values of <see cref="CutsceneDefinition.Tier"/> (synthesis §6.2; T0 is a plain VEGA line).</summary>
public static class CutsceneTiers
{
    /// <summary>T1: a banner at the top — icon, kicker, one line; 3–4 s, never blocks.</summary>
    public const int Banner = 1;

    /// <summary>T2: a title or beat card in the frame; never takes the camera, never holds or freezes.</summary>
    public const int Card = 2;

    /// <summary>T3: a staged scene on real objects; takes the camera, SP hold, controls frozen.</summary>
    public const int Scene = 3;

    /// <summary>T4: a multi-part set piece (B12 reveal, finale, ending).</summary>
    public const int SetPiece = 4;

    /// <summary>Whether <paramref name="tier"/> is a cutscene tier (1–4).</summary>
    public static bool IsValid(int tier) => tier >= Banner && tier <= SetPiece;
}

/// <summary>The camera types (synthesis §6.3): the v1 set and #2477's moves (<c>dolly</c>, <c>pan</c>, <c>flyover</c>) and
/// synthetic stages (<c>space_vista</c>, <c>diorama</c>). Reference: docs/developer/CUTSCENES.md "Cameras".</summary>
public static class CutsceneCameras
{
    /// <summary>Letterbox and captions over the live view — the camera stays the player's.</summary>
    public const string PlayerView = "player_view";

    /// <summary>On top of a running flight sequence; never moves the camera.</summary>
    public const string Overlay = "overlay";

    /// <summary>A fixed shot of the anchor.</summary>
    public const string Static = "static";

    /// <summary>Circling the anchor (the prologue's maths, radius by ship size).</summary>
    public const string Orbit = "orbit";

    /// <summary>Rising above the anchor.</summary>
    public const string Crane = "crane";

    /// <summary>Tilted up from the player — always clear, place-neutral.</summary>
    public const string Sky = "sky";

    /// <summary>The galaxy chart full screen.</summary>
    public const string Chart = "chart";

    /// <summary>#2477: a straight, eased move between two points near the anchor (a slow push-in that slides a little
    /// sideways), the whole path checked against the terrain.</summary>
    public const string Dolly = "dolly";

    /// <summary>#2477: turning in place across an arc — from just above the player's head, or from a vantage point across
    /// the anchor; only where the view stays open.</summary>
    public const string Pan = "pan";

    /// <summary>#2477: a high arc over the anchor and its ground, rising in the middle, always looking at the anchor.</summary>
    public const string Flyover = "flyover";

    /// <summary>#2477: a synthetic space panorama (stars, nebula, sun, a planet; with the <c>ship</c> anchor the player's own
    /// ship) — the same recipe as the menu's attract scene, independent of where the player is (on the ground or in space).</summary>
    public const string SpaceVista = "space_vista";

    /// <summary>#2477: a small synthetic stage showing what the anchor names: <c>core</c> the sleeping Guardian core (a red
    /// glowing column in an iron shell), <c>star_map</c> VEGA's memory of the star chart (the camera drifts to the rim).</summary>
    public const string Diorama = "diorama";

    /// <summary>Every camera type.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        PlayerView, Overlay, Static, Orbit, Crane, Sky, Chart, Dolly, Pan, Flyover, SpaceVista, Diorama,
    };

    /// <summary>The anchors a <c>diorama</c> can show (#2477).</summary>
    public static readonly IReadOnlyList<string> DioramaAnchors = new[] { CutsceneAnchors.Core, CutsceneAnchors.StarMap };

    /// <summary>Whether the type takes the camera away from the player (everything but <c>player_view</c> and <c>overlay</c>).</summary>
    public static bool TakesCamera(string? type)
        => !string.IsNullOrEmpty(type) && type != PlayerView && type != Overlay;

    /// <summary>#2477: whether the type shows a synthetic stage instead of the world (<c>space_vista</c>, <c>diorama</c>) —
    /// skipped on the low quality presets, where a scene made only of them plays as a card.</summary>
    public static bool IsSynthetic(string? type) => type == SpaceVista || type == Diorama;
}

/// <summary>What a shot's camera is placed around (synthesis §6.3).</summary>
public static class CutsceneAnchors
{
    /// <summary>The player (the default).</summary>
    public const string Player = "player";

    /// <summary>The player's ship (radius scaled by the ship's size).</summary>
    public const string Ship = "ship";

    /// <summary>The body the player is on.</summary>
    public const string Planet = "planet";

    /// <summary>The space station the player is aboard.</summary>
    public const string Station = "station";

    /// <summary>The galaxy chart.</summary>
    public const string StarMap = "star_map";

    /// <summary>The heal tank of the player's ship (every starter ship has the medbay).</summary>
    public const string HealTank = "heal_tank";

    /// <summary>The Guardian core chamber.</summary>
    public const string Core = "core";

    /// <summary>The coordinates carried by the <c>CutscenePlay</c> message.</summary>
    public const string Custom = "custom";

    /// <summary>#2478: the other players — on a <c>chart</c> their star systems are labelled with their names (VEGA's "Many
    /// minds" in multiplayer). It needs another player on the star map; without one (singleplayer) the scene plays as a
    /// card, like a heal tank that is not there.</summary>
    public const string Players = "players";

    /// <summary>Every anchor.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Player, Ship, Planet, Station, StarMap, HealTank, Core, Custom, Players };
}

/// <summary>Caption styles (synthesis §6.3).</summary>
public static class CutsceneCaptionStyles
{
    /// <summary>Lower third inside the letterbox, auto-paged at sentence ends.</summary>
    public const string Subtitle = "subtitle";

    /// <summary>A big centred card: kicker + title.</summary>
    public const string TitleCard = "title_card";

    /// <summary>The speaker box with avatar, name, typewriter and voice.</summary>
    public const string Speaker = "speaker";

    /// <summary>A framed reading panel over the dimmed scene (long texts: the recap, the epilogue).</summary>
    public const string ReadingFrame = "reading_frame";

    /// <summary>The credits roll.</summary>
    public const string Credits = "credits";

    /// <summary>Every style.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Subtitle, TitleCard, Speaker, ReadingFrame, Credits };
}

/// <summary>The values of <see cref="CutsceneDefinition.Arg"/> (#2473): what fills a scene's <c>{0}</c>.</summary>
public static class CutsceneArgSources
{
    /// <summary>The bound chapter: its title key as <c>{0}</c>, its place among the pack's chapters of its kind as
    /// <c>{1}</c> and how many there are as <c>{2}</c> (the default).</summary>
    public const string Chapter = "";

    /// <summary>The name of the body the player is on when the scene is played (a landing).</summary>
    public const string Body = "body";

    /// <summary>The name of the player's star system when the scene is played (a hyperjump).</summary>
    public const string System = "system";

    /// <summary>Every source an author may write (the default is written by leaving the field out).</summary>
    public static readonly IReadOnlyList<string> All = new[] { Body, System };

    /// <summary>Whether <paramref name="source"/> is the default or a known source.</summary>
    public static bool IsValid(string? source) => string.IsNullOrEmpty(source) || All.Contains(source!);
}

/// <summary>Colour grades of a shot.</summary>
public static class CutsceneGrades
{
    /// <summary>The live colours.</summary>
    public const string None = "none";

    /// <summary>VEGA's memory (sepia, used by the memory star chart).</summary>
    public const string Memory = "memory";

    /// <summary>The flashback grade of the memory beats.</summary>
    public const string Flashback = "flashback";

    /// <summary>Every grade.</summary>
    public static readonly IReadOnlyList<string> All = new[] { None, Memory, Flashback };
}

/// <summary>
/// Cutscene ids with a fixed meaning (synthesis §5.3 "VEGA-Cutscene-Ids", "Tutorial-Cutscene-Ids") and the instance-id
/// form of a template binding.
/// </summary>
public static class StoryCutsceneIds
{
    /// <summary>The built-in recap card ("The story so far"): never defined in a pack; the server sends it with the reached
    /// chapters' title keys as the argument, joined by <see cref="Localization.LineText.ArgSeparator"/>.</summary>
    public const string Recap = "__recap";

    /// <summary>Prefix of the built-in ids (a pack may not define one).</summary>
    public const string BuiltInPrefix = "__";

    /// <summary>Separator between a template's id and its binding owner in an instance id.</summary>
    public const char InstanceSeparator = '@';

    /// <summary>Separator between a pack and a cutscene id in a cross-pack reference (<c>tutorial/tut.welcome</c>).</summary>
    public const char PackSeparator = '/';

    /// <summary>VEGA's cold open.</summary>
    public const string Prologue = "prologue";

    /// <summary>VEGA's ending.</summary>
    public const string Resolution = "resolution";

    /// <summary>The ids the <c>vega_protocol</c> pack may define.</summary>
    public static readonly IReadOnlyList<string> Vega = new[]
    {
        Prologue, "ch1.open", "ch2.open", "ch3.open", "ch4.open", "ch5.open", "ch6.open", "ch7.open",
        "b01", "b03", "b04", "b07", "b08", "b10",
        "fin.arrival", "fin.descent", "fin.breach", "fin.duel", "fin.standdown", Resolution,
    };

    /// <summary>The ids the <c>tutorial</c> pack may define.</summary>
    public static readonly IReadOnlyList<string> Tutorial = new[]
    {
        "tut.welcome", "tut.strip", "tut.course", "tut.done", "tut.first_launch", "tut.first_dock", "tut.first_landing",
        "tut.first_jump", "tut.first_base", "tut.ship_commissioned", "tut.first_circuit", "tut.first_tame", "tut.graduation",
    };

    /// <summary>The chain entry the built-in recap leaves behind (#2470): <c>&lt;pack&gt;/*</c> — "The story so far" leads
    /// straight into the pack's next scene (the current chapter's opening at a story-track start, synthesis §5.3
    /// "Spurstart-Folge"), without the 90 s cooldown in between.</summary>
    public static string AnyOfPack(string packId) => packId + PackSeparator + "*";

    /// <summary>Whether <paramref name="id"/> is a built-in id (<c>__recap</c>).</summary>
    public static bool IsBuiltIn(string? id) => id is not null && id.StartsWith(BuiltInPrefix, StringComparison.Ordinal);

    /// <summary>The instance id of a template bound by <paramref name="owner"/> (a chapter or step id, or the pack id for
    /// the pack intro): <c>tut.strip@b3</c>.</summary>
    public static string Instance(string definitionId, string owner) => definitionId + InstanceSeparator + owner;

    /// <summary>The definition id of a (possibly instance) id: the part before <c>@</c>.</summary>
    public static string DefinitionOf(string id)
    {
        int at = id.IndexOf(InstanceSeparator);
        return at < 0 ? id : id.Substring(0, at);
    }

    /// <summary>The binding owner of an instance id (the part after <c>@</c>), or empty.</summary>
    public static string OwnerOf(string id)
    {
        int at = id.IndexOf(InstanceSeparator);
        return at < 0 ? string.Empty : id.Substring(at + 1);
    }
}
