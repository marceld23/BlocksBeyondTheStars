// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's living things (#2912): planet enemies and the crosshair aiming, creatures, NPCs with their
    /// vendors and market, and companions.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>This player's tamed-creature roster, for the Companions menu tab. Refreshed by the server.</summary>
        public CompanionList Companions { get; private set; } = new CompanionList();

        /// <summary>Live taming-ritual state (decoded mood + what the creature wants now), or null when no
        /// attempt is in progress. Drives the HUD taming prompt + response buttons.</summary>
        public TameProgress TameState { get; private set; }

        /// <summary>Badge flag: a creature was tamed since the player last opened the Companions tab.</summary>
        public bool NewCompanionUnseen { get; private set; }
        public void MarkCompanionsSeen() => NewCompanionUnseen = false;

        public NetCombatEntity[] PlanetEnemies { get; private set; } = System.Array.Empty<NetCombatEntity>();

        /// <summary>#2670: the server clock of the tick <see cref="PlanetEnemies"/> belongs to (0 = an older server).</summary>
        public double PlanetEnemiesServerTime { get; private set; }

        /// <summary>#2670: that list's stamp on the render clock — taken ONCE when the list arrived (the clock sync learns from
        /// every call, so stamping per enemy would drag its offset).</summary>
        public double PlanetEnemiesLocalTime { get; private set; }

        /// <summary>#2664–#2670: maps the server's snapshot clock onto <see cref="WorldTime"/> for every planet-entity
        /// interpolator (animals, NPCs, machines, bandits) — one offset per connection, re-synced on a world reset.</summary>
        public ServerClockSync EntityClock { get; } = new ServerClockSync();

        // --- Crosshair enemy aiming (#693): published by PlayerController every frame ---

        /// <summary>Id of the enemy/creature currently under the crosshair (null = none) — drives the
        /// crosshair's hostile tint and keeps that entity's health bar always visible.</summary>
        public string AimedEnemyId { get; set; }

        /// <summary>Whether the active world's rules mandate auto-aim (defaults to ON before rules arrive).</summary>
        public bool AutoAimOn => Rules == null || Rules.AutoAim;

        /// <summary>The entity id of the local player's most recent shot + when it left — lets the entity
        /// views attribute an observed hull drop to "my hit" and flash the crosshair hit marker.</summary>
        public string LastShotTargetId { get; set; }
        public float LastShotTime { get; set; } = -999f;

        /// <summary>#2564: the last blow the server reported on the local player — who hit (entity id), from where, and
        /// when (on the <see cref="Time.time"/> clock). The HUD's damage marker points there; the attacker blinks red.</summary>
        public readonly HitSourceTracker LastHit = new HitSourceTracker();

        /// <summary>Live procedural creatures near the player (fauna), with their species descriptor.</summary>
        public NetCreature[] Creatures { get; private set; } = System.Array.Empty<NetCreature>();

        /// <summary>#2665: the server clock of the tick <see cref="Creatures"/> belongs to (0 = an older server).</summary>
        public double CreaturesServerTime { get; private set; }

        /// <summary>#2665: that list's stamp on the render clock — taken ONCE when the list arrived (see
        /// <see cref="PlanetEnemiesLocalTime"/>).</summary>
        public double CreaturesLocalTime { get; private set; }

        /// <summary>#1998: world effects (thumps, stomps, breaches) waiting for the creature view to play them.</summary>
        public readonly System.Collections.Generic.List<WorldFx> PendingWorldFx = new System.Collections.Generic.List<WorldFx>();

        /// <summary>Settlement / station NPCs (vendors, quartermasters, settlers) near the player.</summary>
        public NetNpc[] Npcs { get; private set; } = System.Array.Empty<NetNpc>();

        /// <summary>Relationship stage per live NPC id (#1118, locale keys like <c>npc.stage.known</c>) —
        /// per receiver, absent = stranger. Nameplates append it so a known face reads as one.</summary>
        public readonly System.Collections.Generic.Dictionary<int, string> NpcStandings
            = new System.Collections.Generic.Dictionary<int, string>();

        /// <summary>#2601: villagers with a personal wish for this player, per live NPC id — 1 = an offer (a gold "!" over
        /// the head), 2 = a held wish ready to hand over ("?"). Arrives with the standings; empty from an older server.</summary>
        public readonly System.Collections.Generic.Dictionary<int, byte> NpcWishMarks
            = new System.Collections.Generic.Dictionary<int, byte>();

        /// <summary>The "People you know" roster (#1118), friendliest first — filled on request
        /// (<c>SendRequestKnownNpcs</c>) for the Character tab.</summary>
        public NetKnownNpc[] KnownNpcs { get; private set; } = System.Array.Empty<NetKnownNpc>();

        /// <summary>#2613: the person E talks to right now — the nearest NPC within <see cref="InteractReach.TalkReach"/>,
        /// measured the short way round both seams (NPCs arrive canonical, the player's position runs unbounded), or null.
        /// THE one picker: PlayerController's E and the HUD's "Talk to … (E)" prompt both ask this, so the prompt never
        /// names someone E would not reach — and a settler across X = 0 from where the game loaded is talkable at all.</summary>
        public NetNpc NearestTalkableNpc()
            => InteractReach.NearestTalkableNpc(Npcs, PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, Circumference);

        /// <summary>#2626: the vendor the player can start trading with right now — the nearest vendor within
        /// <c>MarketStock.TradeReach</c>, measured the short way round both seams, or null. THE one picker: the "Trade"
        /// prompt (<see cref="NearVendor"/>), E's trade-or-talk, the vendor screen's binding and the crafting menu's market
        /// page all ask it, and <see cref="VendorReach"/> mirrors the server's barter reach.</summary>
        public NetNpc NearestTradingVendor()
            => VendorReach.NearestVendor(Npcs, PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, Circumference);

        /// <summary>True when a settlement/station vendor stands within trading reach — enables market
        /// barter at their stall (the server also allows it aboard your ship).</summary>
        public bool NearVendor => NearestTradingVendor() != null;

        /// <summary>Whether the player can barter at a market right now (aboard ship or at a vendor).</summary>
        public bool MarketAvailable => Aboard || NearVendor;

        /// <summary>The trade theme of the closest vendor NPC within trading reach (miner, grocer, doctor, …), or empty
        /// when none stands by — aboard, the ship's own console has no theme.</summary>
        public string NearestVendorTheme => NearestTradingVendor()?.Theme ?? string.Empty;

        /// <summary>The in-game day the rotating market offers and the travelling merchant's mixed stock go by — the server's
        /// <c>MarketDay</c> (the system clock of <c>WorldEnvironment.SystemTimeDays</c>).</summary>
        public long MarketDay => Environment != null ? (long)System.Math.Floor(Environment.SystemTimeDays) : 0;

        /// <summary>Whether a market (barter) recipe is on offer right here, today — the server's rule: a themeless deal
        /// trades wherever the market is (any vendor, the ship's console), a themed one only beside a vendor of that
        /// trade, a rotating one only on its day. The crafting tab's "Markt" page lists by this, so the ship no longer
        /// offers the grocer's food as if it could make it (#2137); the vendor screen asks <see cref="MarketOfferedBy"/>
        /// for the vendor it is bound to (#2626).</summary>
        public bool MarketOfferedHere(BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition recipe)
            => MarketOfferedBy(recipe, NearestVendorTheme);

        /// <summary>Whether a vendor of <paramref name="vendorTheme"/> ("" = the ship's console) has the recipe in stock
        /// today — <c>MarketStock.Offers</c>, the very rule the server's barter check uses (#2630: the travelling merchant's
        /// mixed stock included).</summary>
        public bool MarketOfferedBy(BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition recipe, string vendorTheme)
            => BlocksBeyondTheStars.Shared.Definitions.MarketStock.Offers(MarketRecipes(), vendorTheme, recipe, MarketDay);

        /// <summary>The recipe table the market rules read (empty before the content is loaded).</summary>
        public System.Collections.Generic.IEnumerable<BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition> MarketRecipes()
            => Content != null
                ? Content.Recipes.Values
                : (System.Collections.Generic.IEnumerable<BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition>)
                    System.Array.Empty<BlocksBeyondTheStars.Shared.Definitions.RecipeDefinition>();
    }
}
