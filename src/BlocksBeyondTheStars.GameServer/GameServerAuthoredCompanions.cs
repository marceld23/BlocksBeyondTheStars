// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The authored companions — built in code rather than tamed: the friendly hunter robot (#3295, the <c>hunter_robot</c>
/// item) and Rudolpho, the alien reindeer with the lantern nose (#3322, the <c>star_bell</c> from door 24 of the Advent
/// calendar). Each lives on the tamed-companion system (follow AI, leash, nameplate, rename, fetch, bandit ward) with a
/// fixed species, a <see cref="TamedCreature.Kind"/> and these shared rules:
/// <list type="bullet">
/// <item>Using its item puts it beside the player — refused (the item kept) in space, on a station or a void world, aboard
/// or in a ship interior, and while the player already has one of that kind: <b>one per player and kind</b>.</item>
/// <item>It <b>roams</b> (<see cref="TamedCreature.Roams"/>: follows its owner to every world but a void one), stays
/// outside the six-pet cap, keeps a <b>fixed bond</b> of 100, never eats, and cannot be scanned or bio-sampled.</item>
/// <item>"Release" in the Companions tab turns it back into its item (the robot powers down, Rudolpho goes home) —
/// refused while the backpack is full.</item>
/// </list>
/// Only the robot fights (<see cref="TickRobotCompanions"/>, keyed on its species): Rudolpho never attacks anything.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Rudolpho's item: ringing the star bell calls him.</summary>
    internal const string StarBellItem = "star_bell";

    /// <summary>Rudolpho's fixed species id — not a roster species, so it never collides with "sp0".."sp8".</summary>
    internal const string ReindeerSpeciesId = "reindeer_rudolpho";

    /// <summary>The name Rudolpho answers to (the player renames him like any companion).</summary>
    private const string ReindeerDefaultName = "Rudolpho";

    /// <summary>One kind of authored companion: its item, species and the scope of its server lines
    /// (<c>@srv.&lt;scope&gt;.deployed</c> …).</summary>
    private sealed class AuthoredCompanion
    {
        public AuthoredCompanion(string kind, string item, string speciesId, string defaultName, string scope, string packedLine, Func<CreatureSpecies> species)
        {
            Kind = kind;
            Item = item;
            SpeciesId = speciesId;
            DefaultName = defaultName;
            Scope = scope;
            PackedLine = packedLine;
            Species = species;
        }

        public string Kind { get; }

        public string Item { get; }

        public string SpeciesId { get; }

        public string DefaultName { get; }

        public string Scope { get; }

        /// <summary>The line once it is back in its item (<c>powered_down</c>, <c>sent_home</c>).</summary>
        public string PackedLine { get; }

        public Func<CreatureSpecies> Species { get; }

        /// <summary>A server line of this kind: <c>@srv.&lt;scope&gt;.&lt;what&gt;</c>.</summary>
        public string Line(string what) => "@srv." + Scope + "." + what;
    }

    private static readonly AuthoredCompanion RobotCompanion = new(
        TamedCreature.RobotKind, HunterRobotItem, RobotSpeciesId, RobotDefaultName, "robot", "powered_down", RobotSpecies);

    private static readonly AuthoredCompanion ReindeerCompanion = new(
        TamedCreature.ReindeerKind, StarBellItem, ReindeerSpeciesId, ReindeerDefaultName, "reindeer", "sent_home", ReindeerSpecies);

    private static AuthoredCompanion? AuthoredCompanionOf(string? kind) => kind switch
    {
        TamedCreature.RobotKind => RobotCompanion,
        TamedCreature.ReindeerKind => ReindeerCompanion,
        _ => null,
    };

    /// <summary>Whether a companion is authored (the robot, Rudolpho) rather than a tamed animal.</summary>
    internal static bool IsAuthoredCompanion(TamedCreature tc) => AuthoredCompanionOf(tc.Kind) is not null;

    /// <summary>Whether a live entity is an authored or travelling companion (the robot, Rudolpho, the story Soundi, #3361) —
    /// no pet slot, nothing to sample.</summary>
    private static bool IsAuthoredEntity(CombatEntity c)
        => c.IsCompanion && (c.CompanionRoams || c.SpeciesId == RobotSpeciesId || c.SpeciesId == ReindeerSpeciesId);

    /// <summary>Rudolpho's fixed species — built in code, so he looks and moves the same on every world: a soft warm-brown
    /// coat with a pale belly, branching antlers with glowing tips and a red lantern nose (the client draws antlers and nose
    /// for <see cref="CreatureBodyPlan.Reindeer"/>).</summary>
    internal static CreatureSpecies ReindeerSpecies() => new()
    {
        Id = ReindeerSpeciesId,
        NameKey = "creature.reindeer.name",
        Name = ReindeerDefaultName,
        Habitat = CreatureHabitat.Land,
        Activity = CreatureActivity.Cathemeral, // never sleeps on a walk with his friend
        Temperament = CreatureTemperament.Passive,
        LocoStyle = LocomotionStyle.Strider,
        Size = 1.5f,
        MaxHealth = 100f,
        Speed = 4.5f, // keeps up with his owner like the robot
        Legs = 4,
        HasTail = true,
        BodySegments = 2,
        ColorRgb = 0x8A5A3B,  // a soft warm brown
        BellyRgb = 0xEADCC6,  // a pale cream belly
        EyeRgb = 0x2A1B12,    // dark, friendly eyes
        Eyes = 4,             // two pairs — he is an alien after all
        Horns = 2,            // the antlers
        NeckLength = 1,
        Glows = true,         // the antler tips and the lantern nose
        BodyPlan = CreatureBodyPlan.Reindeer,
        VoiceSeed = 0x3322,
    };

    /// <summary>The <c>star_bell</c> gadget: Rudolpho trots up beside the player.</summary>
    private bool DeployReindeer(PlayerSession session) => DeployAuthoredCompanion(session, ReindeerCompanion);

    /// <summary>Puts an authored companion beside the player and takes its item. Returns true when it came.</summary>
    private bool DeployAuthoredCompanion(PlayerSession session, AuthoredCompanion kind)
    {
        var p = session.State;
        string? refusal = InSpace(p.PlayerId) || InStation(p.PlayerId) || _world.Planet.Void ? kind.Line("surface_only")
            : AboardParkedShip(p) || InShipInterior(p.PlayerId) ? kind.Line("step_outside")
            : p.TamedCreatures.Any(t => t.Kind == kind.Kind) ? kind.Line("already_active")
            : !p.Inventory.Has(kind.Item, 1) ? "@srv.gadget.missing"
            : null;
        if (refusal is not null)
        {
            Reject(session, "gadget", refusal);
            return false;
        }

        long now = NowUnixMs();
        var tc = new TamedCreature
        {
            Id = kind.Scope + "_" + Guid.NewGuid().ToString("N").Substring(0, 12),
            HomeBodyId = _world.LocationId,
            Name = kind.DefaultName,
            SpeciesId = kind.SpeciesId,
            Species = kind.Species(),
            SizeScale = 1f,
            Bond = 100,
            TamedAtUtc = now,
            LastFedUtc = now,
            Kind = kind.Kind,
            Roams = true,
        };
        _repo.RunInTransaction(() =>
        {
            p.Inventory.Remove(kind.Item, 1);
            p.TamedCreatures.Add(tc);
            _repo.SavePlayer(p);
        });
        SpawnCompanionEntity(p.PlayerId, tc, p.Position);
        SendInventory(session);
        SendCompanions(session);
        BroadcastCreatures();
        Send(session, new ServerMessage { Text = kind.Line("deployed") + ":" + tc.Name });
        return true;
    }

    /// <summary>"Release" on an authored companion: it leaves the world and its item comes back into the backpack — refused
    /// while the backpack is full, and while a knocked-out robot's crate is still on its way. #3398/#3399: the robot's gear (its
    /// weapon, its jetpack) comes back with it — all of it fits, or nothing moves.</summary>
    private void PackAwayAuthoredCompanion(PlayerSession session, TamedCreature tc)
    {
        var p = session.State;
        var kind = AuthoredCompanionOf(tc.Kind) ?? RobotCompanion;
        if (tc.KnockedOut)
        {
            Reject(session, "companion", kind.Line("knocked_out") + ":" + CompanionDisplayName(tc));
            return;
        }

        var back = new List<ItemStack> { new ItemStack(kind.Item, 1) };
        back.AddRange(RobotGearItems(tc));
        var trial = p.Inventory.Clone();
        if (back.Any(s => trial.Add(s.Item, s.Count, MaxStackOf(s.Item)) > 0))
        {
            Reject(session, "companion", "@inventory_full");
            return;
        }

        _repo.RunInTransaction(() =>
        {
            foreach (var s in back)
            {
                p.Inventory.Add(s.Item, s.Count, MaxStackOf(s.Item));
            }

            p.TamedCreatures.Remove(tc);
            _repo.SavePlayer(p);
        });
        _creatures.RemoveAll(c => c.CompanionId == tc.Id);
        SendInventory(session);
        BroadcastCreatures();
        SendCompanions(session);
        Send(session, new ServerMessage { Text = kind.Line(kind.PackedLine) + ":" + CompanionDisplayName(tc) });
    }

    /// <summary>The refusal of a feed for an authored companion (it does not eat — its bond is fixed).</summary>
    private static string AuthoredNoFoodLine(TamedCreature tc)
        => (AuthoredCompanionOf(tc.Kind) ?? RobotCompanion).Line("no_food") + ":" + CompanionDisplayName(tc);

    // ---------------- Test seams ----------------

    /// <summary>Test seam: rings the star bell as the gadget intent would (energy and cooldown aside). True = Rudolpho came.</summary>
    public bool DeployReindeerForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && DeployReindeer(s);

    /// <summary>Test seam: Rudolpho's live entity for a player on the active world, or null.</summary>
    public CombatEntity? ReindeerEntityForTest(string playerId)
        => _creatures.FirstOrDefault(c => c.IsCompanion && c.SpeciesId == ReindeerSpeciesId && c.OwnerId == playerId);
}
