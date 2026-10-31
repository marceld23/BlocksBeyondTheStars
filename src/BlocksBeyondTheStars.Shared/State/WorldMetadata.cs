// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Top-level, rarely changing world parameters. Combined with player deltas this fully
/// describes a save: the procedural baseline is regenerated from <see cref="Seed"/>.
/// </summary>
public sealed class WorldMetadata
{
    public string WorldName { get; set; } = "New World";

    /// <summary>Master world seed driving all procedural generation.</summary>
    public long Seed { get; set; }

    /// <summary>Planet type key the player starts on / the active surface for the MVP.</summary>
    public string DefaultPlanetType { get; set; } = "rocky";

    /// <summary>Logical id of the currently active planet/location.</summary>
    public string ActiveLocationId { get; set; } = "rocky";

    /// <summary>
    /// The save version this build writes (#2223). A build stamps every world it opens with it and refuses to open
    /// a world whose stored version is HIGHER: such a save was written by a newer build, and opening it here would
    /// rewrite it for good — block keys this build does not know are remapped to air on the first load.
    /// <para>Bump it whenever a build starts writing something an older build would misread or destroy: new blocks
    /// (the palette remap drops unknown keys), a changed record format, a re-used field. Lower or missing versions
    /// always load.</para>
    /// <para>A changed block set cannot slip through without a bump: <c>SaveCompatibilityTests</c> pins the block
    /// fingerprint (<c>GameContent.BlockFingerprint</c>) this version stands for and fails until the version is
    /// raised and the new pair is written down there.</para>
    /// <para>1 = every save before the check existed. 2 = the bio lab's blocks (<c>bio_lab</c>,
    /// <c>flora_hybrid</c>) and the remap of a self-built ship's hull (#2221). 3 = the Crystal Net 2 blocks (#2251:
    /// the phase block, the trapdoor, the bridge motor and deck, the piston and its head, the lift, the display, the
    /// dice block, the signal sender / receiver, the environment and ship sensors, and the unlit / open twins of the
    /// force field, the campfire and the forge). 4 = the derelict stations' guardian bay (#2635, <c>guardian_spawner</c>).
    /// 5 = the twelve wild plants of the mixed meadows (#2833, generation 28: <c>flora_berrybush</c> … <c>flora_lupine</c>).
    /// 6 = the tree variety (#2850, #2851, #2853, generation 29: <c>tree_leaves_b</c>, <c>tree_leaves_c</c>,
    /// <c>blossom_leaves</c>, <c>wood_log_b</c>, <c>saguaro</c>, <c>flora_lantern_fruit</c>). 7 = the sapling kinds
    /// (#2977–#2980: <c>flora_sapling_conifer</c>, the four <c>flora_sapling_fruit_*</c>, <c>flora_sapling_fifi</c>,
    /// <c>flora_sapling_mahogany</c>). 8 = the pumpkin (#3005: <c>flora_pumpkin</c>, <c>pumpkin_lantern</c>). 9 = the festival
    /// decorations (#3024: <c>gift_box</c>, <c>paper_lantern</c>, <c>diya_lamp</c>, <c>flora_marigold</c>).</para>
    /// </summary>
    public const int CurrentSaveVersion = 9;

    /// <summary>The save version of the build that last opened this world — see <see cref="CurrentSaveVersion"/>.
    /// Absent in the JSON ⇒ 1, the version every save carried before the check existed.</summary>
    public int SaveVersion { get; set; } = 1;

    /// <summary>Total wall-clock seconds this world has been actively played (accumulated server-side only
    /// while at least one player is joined, so an idle dedicated server doesn't inflate it). Shown in the HUD
    /// and the save picker. 0 on saves from before playtime tracking existed.</summary>
    public long CumulativePlaytimeSeconds { get; set; }

    /// <summary>#3041: the system clock (system days of <c>PlanetSeasons.SystemDaySeconds</c>) — the orbits, the wet/dry season
    /// (#900) and the four seasons run on it. Saved so a restart continues the year instead of starting every season over
    /// (it was runtime-only before). 0 on older saves.</summary>
    public double SystemTimeDays { get; set; }

    /// <summary>Real seconds this world was played — at least one player joined — since its last rotating backup
    /// (#2223). The backup interval (<c>ServerConfig.BackupIntervalMinutes</c>) is measured on it, and it is saved
    /// with the world, so the interval carries across server runs: a world played 45 minutes a day gets its copy
    /// on the second day instead of never. Set back to 0 when a backup starts. Absent in older saves ⇒ 0.</summary>
    public double PlaySecondsSinceBackup { get; set; }

    /// <summary>Admin-defined universe description; combined with the seed it yields the galaxy.</summary>
    public WorldDescription Description { get; set; } = new();

    /// <summary>
    /// Keys of structure loot markers (wreck/ruin caches) already turned into containers, so they
    /// aren't re-spawned on reload — even after the container has been looted and removed.
    /// </summary>
    public System.Collections.Generic.List<string> GeneratedLoot { get; set; } = new();

    /// <summary>
    /// The space salvage ledger (#2354): salvage pays once per galaxy. <c>wreck:&lt;bodyId&gt;</c> → the space wreck's
    /// remaining hull cells (0 = salvaged down to nothing — it is not spawned again); <c>debris:&lt;bodyId&gt;</c> → a
    /// bitmask of the debris field's collected salvage capsules. Absent keys mean "untouched". Additive: an older
    /// build ignores the field and would drop it on its next save, which only re-opens the salvage.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, int> SpaceSalvage { get; set; } = new();

    /// <summary>
    /// Player claims over spawned structures (factories): each maps a structure's stable per-world key to its
    /// owner. A claimed structure becomes an editable player base for the owner + their allies. Founded by
    /// consuming an access code at the structure. Persisted (the structures themselves re-derive from the seed).
    /// </summary>
    public System.Collections.Generic.List<StructureClaim> Claims { get; set; } = new();

    /// <summary>
    /// Hidden POIs an NPC hint has revealed on the map, world-globally. Keys carry the location id (unlike
    /// <see cref="GeneratedLoot"/>) because coordinates repeat across a save's worlds: "{locationId}|wreck"
    /// and "{locationId}|chest:{x}:{y}:{z}". The POIs themselves re-derive from the seed every session.
    /// </summary>
    public System.Collections.Generic.List<string> RevealedPois { get; set; } = new();

    /// <summary>
    /// One-time stamp registry (#467): "{locationId}|ruins" / "|vaults" / "|wreck" once that feature's
    /// blocks were written into the world. The old in-memory guards died with the unload (a world unloads
    /// when its last player leaves), so re-entering re-ran the whole stamp chain — resurrecting mined
    /// ruins/vault/wreck blocks and carving away player builds inside the footprints. A missing entry
    /// simply means "stamp once more", so old saves migrate by stamping one final time.
    /// </summary>
    public System.Collections.Generic.List<string> StampedFeatures { get; set; } = new();

    /// <summary>
    /// Persisted structure placements (#586): where each rolled structure instance actually landed (or that
    /// it could not land), pinned at first stamp. Without this the positions re-derived from re-running the
    /// placement SEARCH on every load — which froze the search algorithm forever: any improvement would have
    /// detached existing worlds' markers/protection/NPCs from their already-stamped blocks. A missing entry
    /// means "this world predates the registry": the frozen legacy search re-derives it once, then records
    /// the outcome here (self-healing migration; monuments pioneered this pattern via StampedFeatures).
    /// </summary>
    public System.Collections.Generic.List<StructurePlacementRecord> Placements { get; set; } = new();

    /// <summary>
    /// Persisted body identity (#468): bodyId → planetType, pinned when a body is first generated. Without
    /// it the whole galaxy's types re-derived from <c>planets.json</c> on every start — ANY data edit (a
    /// new type, a weight rebalance, a reorder) silently re-typed bodies under players' buildings (it
    /// happened twice in shipped history). Bodies absent from the map (fresh saves, new systems) roll from
    /// the generator once and are frozen here (decision #1: freeze — no migration re-roll).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> BodyPlanetTypes { get; set; } = new();

    /// <summary>
    /// Pinned landing pads (#1989): bodyId → the pads of that body, one entry per pad as
    /// <c>index,x,z,y,radius,depth,flags</c> (flags: 1 wet, 2 islet, 4 classic, 8 molten, 16 lava islet),
    /// pads separated by <c>;</c>.
    /// <para>Finding a body's pads means searching its terrain for dry, flat ground — thousands of column
    /// queries, measured at 2.7 s on a meadow world and 7.9 s on a dune world, paid on EVERY load because
    /// the pads were only ever held in memory. They are deterministic, so the search's answer is written
    /// down the first time and read back afterwards.</para>
    /// <para>Pinning is also the safer half: the old code comment put it plainly — "pads are not persisted,
    /// the rule that re-derives them is the only thing holding them in place" — so any future change to the
    /// pad rules could have moved the ground out from under a player's parked ship and their base. A body
    /// absent from the map (every save written before this) searches once, exactly as before, and is frozen
    /// from then on. Additive JSON field, no migration.</para>
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> BodyLandingPads { get; set; } = new();

    /// <summary>
    /// The monorail (#2113): bodyId → the rail graph of that world (one pylon per entry as <c>x,y,z:lx,ly,lz;lx,ly,lz</c>,
    /// pylons separated by <c>|</c>) and bodyId → its trains (one per entry as <c>id,owner,line,arc,speed,dir,auto,wagon+wagon</c>,
    /// trains separated by <c>|</c>). The pylons are blocks in the world; the links between them and the trains are
    /// not, so they are written down here on every change. Additive JSON fields, no migration.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> RailGraphs { get; set; } = new();
    public System.Collections.Generic.Dictionary<string, string> Trains { get; set; } = new();

    /// <summary>
    /// VEGA's relay-network insight stages already spoken ("relay" / "lane" / "growth", F-2 of #1125) —
    /// each epilogue insight plays exactly once per save. Additive JSON field, no migration.
    /// </summary>
    public System.Collections.Generic.List<string> RelayInsights { get; set; } = new();

    /// <summary>
    /// Pinned station interiors (#1115): station id → template key ("" = procedurally generated), written
    /// at the station's FIRST interior stamp. Replays use the pin, so a growing template pool never morphs
    /// an already-boarded station's interior under its persisted blocks. Additive JSON field; a station
    /// absent from the map replays against the legacy pool (the pre-#1115 behaviour, draw-for-draw).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> StationTemplates { get; set; } = new();

    /// <summary>
    /// Pinned kit stations (#1874): station id → the composition (kit, seed, every module with its origin and turns)
    /// the interior was baked from, written at the first stamp. <see cref="StationTemplates"/> holds
    /// <c>"kit:&lt;key&gt;"</c> for such a station; replays bake the pinned modules and never re-run the kit, so
    /// editing a kit or a module list never changes a station somebody has boarded. Additive JSON field.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, StationKitRecord> StationKits { get; set; } = new();

    /// <summary>
    /// Growing galaxy (#1123): how many systems were appended BEYOND the description's
    /// <c>StarSystemCount</c> by frontier jumps. The galaxy is re-derived from the seed on every start,
    /// so persisting the COUNT is enough — system N is a pure function of (seed, N), and regenerating
    /// with count + grown reproduces every grown system byte-identically. 0 (absent in older saves) means
    /// the galaxy is exactly the description's fixed one.
    /// </summary>
    public int GalaxyGrownSystems { get; set; }

    /// <summary>
    /// SPS relay upgrades (#1125, Track F): per commissioned player station, what has been contributed
    /// toward its relay conversion and whether it is complete. World-shared (co-op contributable) and tiny,
    /// so it lives in the metadata blob — an additive JSON field, no migration. Jump lanes are NOT stored:
    /// they re-derive from the completed relays' systems + the data-driven link range on every start.
    /// </summary>
    public System.Collections.Generic.List<RelayStationRecord> Relays { get; set; } = new();

    /// <summary>
    /// True for worlds created after ships became placed objects (#870): such a save never persisted a
    /// stamped hull, so the legacy stamp-residue cleanup must never run on it (it would delete the player's
    /// own builds beside a pad on their first landing there). False (missing) on older saves — they migrate
    /// via <see cref="ShipResidueCleaned"/>.
    /// </summary>
    public bool CreatedWithShipObjects { get; set; }

    /// <summary>
    /// One-time ship-stamp residue cleanups already performed on pre-object saves (#870):
    /// "{locationId}|shipresidue:{padX}:{padZ}" once the legacy stamped-hull migration ran for that pad.
    /// The cleanup used to run on EVERY ship placement (join, respawn, landing, ship switch), deleting all
    /// persisted block edits in a box around the parked ship — wiping the player's own builds beside their
    /// pad on each rejoin ("singleplayer doesn't save"). A missing entry means "clean once more", so
    /// pre-object saves migrate by cleaning one final time (the <see cref="StampedFeatures"/> pattern).
    /// </summary>
    public System.Collections.Generic.List<string> ShipResidueCleaned { get; set; } = new();

    // --- Singleplayer "Creative" world options (chosen at creation; persisted so they reapply on every load).
    // A head-start sandbox: everything available + a starter set, while survival mechanics stay on. All false =
    // the normal "Explorer" world. Blueprints + ships are re-applied per join (idempotent); the kit is one-time. ---
    public bool CreativeUnlockAllBlueprints { get; set; }
    public bool CreativeStartAllShips { get; set; }
    public bool CreativeStarterKit { get; set; }

    /// <summary>True once the one-time creative starter kit has been granted, so reloads don't refill it.</summary>
    public bool CreativeKitGranted { get; set; }

    /// <summary>
    /// World rules chosen at creation (world options) and updated by in-game admin edits — the world
    /// OWNS its rules once created: on load this replaces the launch config's rules, so singleplayer
    /// relaunches (which pass creation options only once) and dedicated restarts keep the chosen set.
    /// Null on saves from before world options existed (the launch config's rules apply then).
    /// </summary>
    public BlocksBeyondTheStars.Shared.Configuration.GameRules? RulesOverride { get; set; }

    /// <summary>The local news a reporter keeps per place (2026-09): place key → the latest articles, newest last, at most
    /// <see cref="NewsArticle.MaxPerPlace"/>. Written from player interviews, screened like chat.</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NewsArticle>> News { get; set; } = new();

    /// <summary>#2599: the deed log — what players did that the world talks about (a bandit camp cleared, a giant beaten,
    /// a first landing, a settlement helped, a festival; <see cref="DeedKinds"/>), per world (location id) → the latest
    /// <see cref="DeedRecord.MaxPerWorld"/>, oldest first. The reporter's and the innkeeper's news and the villagers'
    /// chatter read it. Additive: an older build drops it, a missing entry means "nothing happened there yet".</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DeedRecord>> Deeds { get; set; } = new();

    /// <summary>#2604: the village festivals — "&lt;locationId&gt;|&lt;settlement name&gt;" → the festival a settlement throws
    /// for the player who finished its "settlement needs" chain, once per settlement ever (a later helper gets the thanks
    /// and the present, no second festival). Pending until its hero is there in the late afternoon, then held. Additive:
    /// an older build drops it, a missing entry means "no festival planned there".</summary>
    public System.Collections.Generic.Dictionary<string, FestivalRecord> Festivals { get; set; } = new();

    /// <summary>When the next Sreekmakra may appear per world (2026-09, Valuma): location id → unix seconds. Set when one is
    /// defeated (a few in-game days) or fled (a day).</summary>
    public System.Collections.Generic.Dictionary<string, long> SreekmakraBackAt { get; set; } = new();

    /// <summary>When a defeated giant comes back (#1998): "locationId|colossus" / "locationId|sandworm0" → unix seconds.</summary>
    public System.Collections.Generic.Dictionary<string, long> GiantBackAt { get; set; } = new();

    /// <summary>#2269: when each world's Crystal Net last ran (UTC unix seconds, by location id) — the machines of a world
    /// that was not ticking catch up for the gap (bounded by the <c>MachineCatchUpMinutes</c> rule). Additive: an older
    /// build drops it, a missing entry means "no credit".</summary>
    public System.Collections.Generic.Dictionary<string, double> CrystalLastTicked { get; set; } = new();

    /// <summary>#2999: the yield stores of the base cores ("base:&lt;body&gt;|x|y|z") and player stations ("station:&lt;id&gt;") —
    /// what each gathered and when it was last read. Filled lazily from the wall clock. Additive: an older build drops it,
    /// a missing entry starts a fresh store.</summary>
    public System.Collections.Generic.Dictionary<string, BaseYieldStore> BaseYields { get; set; } = new();

    /// <summary>#2643: the cells NPC miners broke and that fill up again — per world (location id) → the pending refills,
    /// oldest first, at most <see cref="NpcRefillRecord.MaxPerWorld"/>. Persisted so a restart never leaves a hole in a
    /// quarry face. Additive: an older build drops it (the holes it leaves are ordinary mined cells), a missing entry means
    /// "nothing to refill".</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<NpcRefillRecord>> NpcRefills { get; set; } = new();

    /// <summary>#2656: who lives at each founded base and where they sleep — the base key (its core cell,
    /// <c>base:&lt;body&gt;:&lt;x&gt;:&lt;y&gt;:&lt;z&gt;</c>, never the load-order base id) → its residents by slot with their bed and
    /// job. Read when the base's residents (re)appear, so a reload or a return to the planet brings back the same people in
    /// the same beds at the same jobs; written whenever the assignment changes. Additive: an older build drops it, a missing
    /// entry means "assign afresh".</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<BaseResidentRecord>> BaseResidents { get; set; } = new();

    /// <summary>#2716: the residents who left — moved out for want of a bed, or went with their dissolved base — with the base
    /// (core-cell key) and slot they had, oldest first, at most <see cref="RetiredBaseResident.MaxKept"/>. Their memory keys
    /// are never minted for a resident of another base (a later base may get the same load-order id), and the same base
    /// filling the same slot again — a rebuilt bed, a core put back on its cell — brings the same person back. Additive: an
    /// older build drops it, a missing list means "nobody left".</summary>
    public System.Collections.Generic.List<RetiredBaseResident> RetiredBaseResidents { get; set; } = new();
}

/// <summary>#2716: a base resident who left (<see cref="WorldMetadata.RetiredBaseResidents"/>): where they lived and who they
/// were — the pinned identity of their <see cref="BaseResidentRecord"/>.</summary>
public sealed class RetiredBaseResident
{
    /// <summary>The most departed residents a save remembers; the oldest drops off.</summary>
    public const int MaxKept = 256;

    /// <summary>The base they lived at, as its core-cell key (<c>base:&lt;body&gt;:&lt;x&gt;:&lt;y&gt;:&lt;z&gt;</c>).</summary>
    public string Base { get; set; } = string.Empty;

    public int Slot { get; set; }

    public string Seed { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public int Face { get; set; }
}

/// <summary>#2656: one resident of a base (<see cref="WorldMetadata.BaseResidents"/>): the slot (slot 0 is the founding
/// settler), the head cell of the bed, the job and the job's post — and (#2716) the person's pinned identity: the seed of
/// their look and name, their memory key and their face.</summary>
public sealed class BaseResidentRecord
{
    /// <summary>0 = the founding settler, 1..4 = the people the beds brought.</summary>
    public int Slot { get; set; }

    /// <summary>The bed's head cell as <c>"x,y,z"</c> (canonical world cell); empty = sleeps rough.</summary>
    public string Bed { get; set; } = string.Empty;

    /// <summary>The job ("vendor", "quartermaster", a profession, "guard", "gardener", "craftsman"); empty = none.</summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>The block cell the job is tied to — the trading post, the board, the workbench, the profession's post, the
    /// first crop, the sentry post — as <c>"x,y,z"</c>; empty for a guard on a walled round or no job. After a reload it
    /// counts as inside the base like the bed, so the job stays while the post's room is not loaded yet.</summary>
    public string Post { get; set; } = string.Empty;

    /// <summary>#2716: the seed the resident's look and name come from (<c>base-settler:…</c>), pinned when they moved in —
    /// or, for a resident from before the pin, at the first load after it, from the base id of that load. Base ids are
    /// load-order numbers that shift when an older core goes, so a pinned resident never reads the id again. Empty = not
    /// pinned yet. Additive: an older build drops it.</summary>
    public string Seed { get; set; } = string.Empty;

    /// <summary>#2716: the resident's NPC-memory key (<c>base_&lt;id&gt;:settler</c>, <c>base_&lt;id&gt;#&lt;slot&gt;:settler</c>
    /// with the id of the moment it was pinned) — the key every player's memory of this person is filed under. Empty = not
    /// pinned yet. Additive.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>#2716: the face seed the resident showed when it was pinned (the client's <c>NetNpc.FaceVariant</c>); 0 = not
    /// pinned yet (the client derives one from the runtime NPC id, which follows the spawn order). Additive.</summary>
    public int Face { get; set; }
}

/// <summary>#2643: one cell an NPC miner broke (<see cref="WorldMetadata.NpcRefills"/>): what stood there and from when on it
/// may fill up again.</summary>
public sealed class NpcRefillRecord
{
    /// <summary>The most pending refills one world keeps; mining one more restores the oldest at once.</summary>
    public const int MaxPerWorld = 512;

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    /// <summary>The block KEY that stood there — not the numeric id, which follows the block list and shifts when a block
    /// is added.</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>The cell's colour modifier and shape descriptor as they were (0 = none / a plain cube).</summary>
    public int Tint { get; set; }

    public int Glow { get; set; }

    public int Shape { get; set; }

    /// <summary>The world's playtime (<see cref="WorldMetadata.CumulativePlaytimeSeconds"/>) from which on the cell may fill
    /// up again.</summary>
    public long DueAt { get; set; }
}

/// <summary>One interview a player gave a reporter (2026-09).</summary>
public sealed class NewsArticle
{
    /// <summary>The most articles a place keeps; older ones drop off.</summary>
    public const int MaxPerPlace = 10;

    /// <summary>The longest answer a player may give.</summary>
    public const int MaxTextLength = 300;

    public string PlayerName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>The in-game day index it was written on.</summary>
    public long Day { get; set; }

    /// <summary>#2599: the world's playtime (<see cref="WorldMetadata.CumulativePlaytimeSeconds"/>, at least 1) when it was
    /// written — orders interviews and deeds in the news. 0 on articles from before the deed log: they sort last.</summary>
    public long At { get; set; }
}

/// <summary>#2599: one deed in the world's deed log (<see cref="WorldMetadata.Deeds"/>): who did what, near which
/// settlement, and when.</summary>
public sealed class DeedRecord
{
    /// <summary>The most deeds one world keeps; older ones drop off.</summary>
    public const int MaxPerWorld = 16;

    /// <summary>Increasing per world (1, 2, …) — identifies the deed for "already talked about it" and for its cached AI
    /// headline.</summary>
    public long Id { get; set; }

    /// <summary>What happened — one of <see cref="DeedKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;

    /// <summary>The hero's name as it was then (the news prints it).</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>The settlement it happened at or near, "" = the whole world talks about it.</summary>
    public string Settlement { get; set; } = string.Empty;

    /// <summary>The "thing" of the deed when it has one: the giant's kind ("colossus", "sandworm", "leviathan",
    /// "sky_giant"), a cleared camp's key; "" otherwise.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>The world's playtime (<see cref="WorldMetadata.CumulativePlaytimeSeconds"/>, at least 1) when it happened.</summary>
    public long At { get; set; }
}

/// <summary>#2599: the kinds of deed the world remembers (<see cref="DeedRecord.Kind"/>). The news line of a kind is
/// <c>news.deed.&lt;kind&gt;</c>; a villager chatter topic about it needs <c>deed:&lt;kind&gt;</c>.</summary>
public static class DeedKinds
{
    /// <summary>The last bandit of a camp went down (by weapon: the attacker; by a sentry: the base owner; by fire: the
    /// nearest player within 64 m).</summary>
    public const string CampCleared = "camp_cleared";

    /// <summary>A settlement's "settlement needs" chain was finished (the festival package records it).</summary>
    public const string SettlementHelped = "settlement_helped";

    /// <summary>A village held its festival for the hero (the festival package records it).</summary>
    public const string Festival = "festival";

    /// <summary>A giant (colossus, sandworm, leviathan, sky giant) was defeated.</summary>
    public const string Giant = "giant";

    /// <summary>The Sreekmakra, Valuma's shapeshifter, was defeated.</summary>
    public const string Sreekmakra = "sreekmakra";

    /// <summary>A player's first landing on an inhabited world (never their spawn world).</summary>
    public const string Landing = "landing";

    /// <summary>Every kind, in authored order.</summary>
    public static readonly System.Collections.Generic.IReadOnlyList<string> All = new[]
    {
        CampCleared, SettlementHelped, Festival, Giant, Sreekmakra, Landing,
    };
}

/// <summary>#2604: one settlement's festival (<see cref="WorldMetadata.Festivals"/>): for whom, since when, and whether it
/// was held yet.</summary>
public sealed class FestivalRecord
{
    /// <summary><see cref="State"/>: planned, waiting (without a deadline) for its hero in the late afternoon.</summary>
    public const string Pending = "pending";

    /// <summary><see cref="State"/>: the festival took place (it happens once per settlement).</summary>
    public const string Held = "held";

    /// <summary><see cref="Pending"/> or <see cref="Held"/>.</summary>
    public string State { get; set; } = Pending;

    /// <summary>The hero — the player who finished the settlement's chain first.</summary>
    public string HeroId { get; set; } = string.Empty;

    /// <summary>The hero's name as it was then (the cheers name them).</summary>
    public string HeroName { get; set; } = string.Empty;

    /// <summary>The world's playtime (<see cref="WorldMetadata.CumulativePlaytimeSeconds"/>, at least 1) when it was planned.</summary>
    public long ScheduledAt { get; set; }

    /// <summary>The world's playtime when it began; 0 while pending.</summary>
    public long HeldAt { get; set; }
}

/// <summary>Where one rolled structure instance landed (#586), pinned at first stamp so the placement search
/// can evolve without moving structures under existing worlds. <see cref="Placed"/> false records the
/// decision "no spot" permanently (legacy worlds only — the guaranteed search always finds one).</summary>
public sealed class StructurePlacementRecord
{
    public string LocationId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // settlement | factory | banditcamp | ruin | vault
    public int Index { get; set; }                    // instance index within its kind's deterministic roll order
    public bool Placed { get; set; }
    public int X { get; set; }                        // structure-local (0,0,0) world column (vaults: shaft centre)
    public int GroundY { get; set; }
    public int Z { get; set; }
    public bool OnIsland { get; set; }
    public string Seat { get; set; } = "legacy";      // seat style: legacy|flat|slope|shelf|stilts|lava|island|buried|wellhead
    public string Name { get; set; } = string.Empty;  // display name (derives from rng draws AFTER the search, so it must be pinned too)

    /// <summary>The hand-designed template this instance was stamped from (#1115), "" for procedural — or
    /// for records from before template pinning existed, which replay against the LEGACY template pool so
    /// a growing pool never morphs their layout under the stamped blocks.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>Settlement records (#1827): whether authored building MODULES may be composed into this
    /// instance's plots / districts. 0 = never (records from before modules existed, and legacy re-derives —
    /// their layout must not change under the stamped blocks), 1 = plot + district modules. Written once at
    /// the first stamp, never bumped afterwards. 2 = composed from a structure KIT (#1876), see <see cref="Kit"/>.</summary>
    public int Modules { get; set; }

    /// <summary>Settlement records (#1876): the structure kit this instance was composed from, "" for none.</summary>
    public string Kit { get; set; } = string.Empty;

    /// <summary>Settlement records (#1876): the grid the kit gave this instance (a settlement's cols, rows, plot stride,
    /// building envelope, storeys, modules-only; a city's grid, district size, street, height, role map), pinned so a
    /// replay lays the same grid whatever the kit says later.</summary>
    public string KitLayout { get; set; } = string.Empty;

    /// <summary>
    /// Settlement records (#1872): WHICH module went into each plot (settlements) or district (the city), in slot
    /// order — the module key, "" for a procedural building or an empty plot. The composers replay this list and
    /// never the pool: before it existed the pick was a hash over the pool AS LOADED, so adding or removing a module
    /// (shipped data, a user-content export, a toggled pack) changed the buildings of an existing settlement on the
    /// next load, stamped over its old blocks. Null on records from before the list existed — the loader freezes
    /// their current picks into it once (the roster pattern, #1299), so nothing changes and later pool edits are safe.
    /// </summary>
    public System.Collections.Generic.List<string>? Composition { get; set; }

    /// <summary>Factory records only (#1299): the recipe roster this factory offers, frozen at first stamp so a
    /// growing factory recipe set never re-rolls what an existing (possibly claimed) factory makes. Null on
    /// records from before roster pinning — the loader freezes the current roll into it once.</summary>
    public System.Collections.Generic.List<string>? Roster { get; set; }

    /// <summary>The <see cref="HashRevision"/> a fresh record is written with: the well-mixed
    /// <c>StableHashing</c> picks (#2686).</summary>
    public const int MixedHashRevision = 1;

    /// <summary>
    /// Settlement records (#2689): which hash decided the instance's per-plot details that no other field pins — the
    /// crop of each procedural greenhouse. 0 = the historical raw <c>WorldGenerator.StableHash</c> pick (every record
    /// from before this field, and the legacy re-derive of a pre-record world), <see cref="MixedHashRevision"/> = the
    /// mixed pick. Written once with the record and never bumped: the blocks are stamped only once (#1990), but a save
    /// without the stamp mark stamps once more, and the in-memory layout is re-derived on every load — both must show
    /// the crops the players saw. Additive JSON field.
    /// </summary>
    public int HashRevision { get; set; }
}

/// <summary>A kit station's pinned composition (#1874) — see <see cref="WorldMetadata.StationKits"/>.</summary>
public sealed class StationKitRecord
{
    /// <summary>The furnishing revision a fresh composition is baked with: 1 = door lanes stay clear and a room ends at
    /// its doorway (#1901).</summary>
    public const int CurrentRevision = 1;

    public string Kit { get; set; } = string.Empty;
    public long Seed { get; set; }
    public System.Collections.Generic.List<StationKitModuleRecord> Modules { get; set; } = new();

    /// <summary>
    /// The furnishing revision the station's stamped blocks were last brought up to (0 = absent in saves from before
    /// #1901). A station stamps only its NON-air cells over the persisted world each session, so furniture an older
    /// composer put where the current one leaves air — a chair in a cabin doorway — would stay forever; the stamp removes
    /// such pieces once and raises this to <see cref="CurrentRevision"/>. Additive JSON field.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>
    /// The exterior detail this station carries (#1918), pinned at its first compose — or, for a station composed before
    /// the feature, at its first replay after it, from its kit as it was then — so editing the kit later never changes a
    /// station that already exists. Null in saves from before #1918. Additive JSON field.
    /// </summary>
    public StationKitExteriorRecord? Exterior { get; set; }
}

/// <summary>A kit station's pinned exterior detail counts (#1918).</summary>
public sealed class StationKitExteriorRecord
{
    public int SolarWings { get; set; }
    public int Antennas { get; set; }
    public int Domes { get; set; }
}

/// <summary>One placed module of a kit station: key, origin inside the station, quarter turns.</summary>
public sealed class StationKitModuleRecord
{
    public string Key { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Turns { get; set; }
}

/// <summary>One player station's SPS relay conversion (#1125): what has been poured into it so far, and
/// whether it is done. Keyed by the station's persisted id ("pstation:…"); contributions are per item key.
/// The station build itself lives in its own repository row — this records only the relay meter.</summary>
public sealed class RelayStationRecord
{
    public string StationId { get; set; } = string.Empty;

    /// <summary>Item key → amount contributed so far (clamped to the definition's required amounts).</summary>
    public System.Collections.Generic.Dictionary<string, int> Contributed { get; set; } = new();

    /// <summary>True once every cost line is fully contributed — the station IS a relay from then on.</summary>
    public bool Completed { get; set; }
}

/// <summary>One player claim over a spawned structure: a stable per-world key, the owner, and a display name.
/// The structure itself re-derives from the seed every session; this persisted record re-applies the claim.</summary>
public sealed class StructureClaim
{
    public string Key { get; set; } = string.Empty;       // stable per-world structure id (e.g. "loc|factory|x|y|z")
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
