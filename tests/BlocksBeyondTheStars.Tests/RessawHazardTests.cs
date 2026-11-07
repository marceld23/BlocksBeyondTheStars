// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ressaw on the server (#3335/#3337/#3339): the liquid stings after a short grace with its own death line, the suit liners'
/// corrosion resistance softens it, only the complete diving suit keeps it out — and lets the tank hold with the head in
/// Ressaw while water still drains it; the fluid pump pulls it into the pack; a lit Ressaw lamp keeps the night sight
/// running and switching it off ends it. A breathable world (the air regenerates the tank outside the liquid), the player
/// on foot high above the ground, no creatures.
/// </summary>
public sealed class RessawHazardTests : IDisposable
{
    private const int Y = 200; // mid-air on "varied" (terrain tops out far below, the atmosphere line is at 240)
    private static readonly Vector3i Feet = new(500, Y, 500);
    private static readonly Vector3i Head = new(500, Y + 1, 500);
    private static readonly string[] Suit = { "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots" };

    private readonly string _root;
    private readonly GameContent _content;

    public RessawHazardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ressaw_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a locked temp file is no test failure
        }
    }

    private SvGameServer Start(string name, Action<ServerConfig>? tune = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = "varied",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        config.Rules.AggressiveAliens = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot at the probe cells (no ship is placed, so the spawn's "aboard" must be dropped).</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Feet.X + 0.5f, Y, Feet.Z + 0.5f);
        return p;
    }

    private void Fill(SvGameServer server, Vector3i cell, string block)
        => server.World.SetBlock(cell, block == "air" ? BlockId.Air : _content.GetBlock(block)!.NumericId);

    private static void TickSeconds(SvGameServer server, PlayerSession p, double seconds)
    {
        var at = p.State.Position;
        for (double t = 0; t < seconds - 1e-6; t += 0.1)
        {
            p.State.Position = at; // nothing may nudge the player out of the probe cells
            server.TickForTest(0.1);
        }
    }

    private static void WearAll(PlayerState p, params string[] keys)
    {
        foreach (string key in keys)
        {
            TestGear.Wear(p, key);
        }
    }

    // ---------------- #3335: contact damage ----------------

    [Fact]
    public void Ressaw_StingsAfterAShortGrace_Gently_WithItsOwnDeathLine()
    {
        var server = Start("sting");
        var p = OnFoot(server, "Justus");
        Fill(server, Feet, "ressaw"); // wading: feet in it, head in the air — the breathable-air regen must not cancel it

        TickSeconds(server, p, 2);
        Assert.InRange(server.RessawSecondsForTest(p.State.PlayerId), 1.5, SvGameServer.RessawGraceSeconds);
        Assert.Equal(100f, p.State.Health); // the grace: a quick dip costs nothing

        TickSeconds(server, p, 4); // 3 s past the grace at 3 HP/s
        Assert.InRange(p.State.Health, 100f - 12f, 100f - 6f);
        Assert.Equal("@srv.death.ressaw", p.Hazards.HazardDeathReason);

        // Out of it: the clock resets and the healing comes back.
        Fill(server, Feet, "air");
        float hurt = p.State.Health;
        TickSeconds(server, p, 2);
        Assert.Equal(0, server.RessawSecondsForTest(p.State.PlayerId));
        Assert.True(p.State.Health > hurt, "out of the Ressaw the natural healing returns");
    }

    [Fact]
    public void Ressaw_HurtsNobody_WithTheHazardsOff()
    {
        var server = Start("mild", c => c.ApplyCommandLine(new[] { "--hazards", "off" }));
        var p = OnFoot(server, "Justus");
        Fill(server, Feet, "ressaw");
        TickSeconds(server, p, 6);
        Assert.Equal(100f, p.State.Health);
        Assert.Equal(0, server.RessawSecondsForTest(p.State.PlayerId));
    }

    // ---------------- #3337: the diving suit ----------------

    [Fact]
    public void ThreePiecesOfTheSuit_OnlySoftenTheSting_TheFullSuitKeepsItOut()
    {
        var server = Start("suit");
        var three = OnFoot(server, "Three");
        Fill(server, Feet, "ressaw");
        WearAll(three.State, "ressaw_helmet", "ressaw_suit", "ressaw_leggings");
        TickSeconds(server, three, 6);
        float lost = 100f - three.State.Health;
        Assert.True(lost > 5f, $"three pieces must not keep Ressaw out (lost {lost})");
        Assert.True(lost < 9f, $"the pieces' corrosion resistance softens it (lost {lost})");

        var full = OnFoot(server, "Full");
        WearAll(full.State, Suit);
        TickSeconds(server, full, 6);
        Assert.Equal(100f, full.State.Health);
        Assert.Equal(0, server.RessawSecondsForTest(full.State.PlayerId));
    }

    [Fact]
    public void TheFullSuit_HoldsTheTankWithTheHeadInRessaw_ButWaterStillDrainsIt()
    {
        var server = Start("breathe");
        var p = OnFoot(server, "Diver");
        Fill(server, Feet, "ressaw");
        Fill(server, Head, "ressaw");

        // Without the suit a head in Ressaw drains the tank like water does.
        p.State.Oxygen = 80f;
        TickSeconds(server, p, 2);
        Assert.True(p.State.Oxygen < 80f, $"a head in Ressaw spends the tank (oxygen {p.State.Oxygen})");
        Assert.False(server.BreathesInRessawForTest(p.State.PlayerId));

        // The full suit: no drain, no damage — the suit breathes the Ressaw (it does not refill like air either).
        p.State.Health = 100f;
        WearAll(p.State, Suit);
        p.State.Oxygen = 80f;
        TickSeconds(server, p, 4);
        Assert.True(server.BreathesInRessawForTest(p.State.PlayerId));
        Assert.Equal(80f, p.State.Oxygen);
        Assert.Equal(100f, p.State.Health);

        // Head out into the air: the tank refills as ever.
        Fill(server, Head, "air");
        TickSeconds(server, p, 1);
        Assert.True(p.State.Oxygen > 80f);

        // Head in water: the suit is no help there.
        Fill(server, Head, "water");
        float before = p.State.Oxygen;
        TickSeconds(server, p, 2);
        Assert.True(p.State.Oxygen < before, $"water still drains the tank of a Ressaw diver (oxygen {p.State.Oxygen})");
    }

    // ---------------- #3335: the pump ----------------

    [Fact]
    public void TheFluidPump_PullsARessawCellIntoThePack()
    {
        var server = Start("pump");
        var p = OnFoot(server, "Pumper");
        p.State.Inventory.Add("fluid_pump", 1, 1);
        var cell = new Vector3i(510, Y, 510);
        Fill(server, cell, "ressaw");
        p.State.Position = new Vector3f(cell.X + 2.5f, cell.Y, cell.Z + 0.5f); // within reach of the aim point

        server.UseGadgetForTest("Pumper", "fluid_pump", new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));

        Assert.True(server.World.GetBlock(cell).IsAir, "a pumped Ressaw cell stays air — Ressaw never flows");
        Assert.Equal(1, p.State.Inventory.CountOf("ressaw"));
    }

    // ---------------- #3339: the lamp ----------------

    private static float NightSight(PlayerSession p) => PlayerEffects.Of(p.State.Effects, BioEffect.NightSight);

    [Fact]
    public void TheRessawLamp_LitAndWorn_KeepsTheNightSightRunning_OffOrTakenOffEndsIt()
    {
        var server = Start("lamp");
        var p = OnFoot(server, "Lamp");
        TestGear.Wear(p.State, "ressaw_lamp");

        TickSeconds(server, p, 1);
        Assert.Equal(0f, NightSight(p)); // worn but off: nothing

        p.LampOn = true;
        TickSeconds(server, p, 1);
        Assert.Equal(1f, NightSight(p), 3); // the top of the curve — "the world looks like day"

        TickSeconds(server, p, 30); // longer than one run: it is renewed while it shines
        Assert.Equal(1f, NightSight(p), 3);

        p.LampOn = false;
        TickSeconds(server, p, 0.2);
        Assert.Equal(0f, NightSight(p)); // switched off: the night sight ends at once

        p.LampOn = true;
        TickSeconds(server, p, 0.5);
        Assert.Equal(1f, NightSight(p), 3);
        TestGear.TakeOff(p.State, "ressaw_lamp");
        TickSeconds(server, p, 0.2);
        Assert.Equal(0f, NightSight(p)); // taken off: the same
    }

    [Fact]
    public void ThePlainSuitLamp_GivesNoNightSight_AndADrunkNightSight_OutlastsTheLamp()
    {
        var server = Start("lamp2");
        var plain = OnFoot(server, "Plain");
        TestGear.Wear(plain.State, SuitEquipment.SuitLampKey);
        plain.LampOn = true;
        TickSeconds(server, plain, 1);
        Assert.Equal(0f, NightSight(plain));

        // A night sight the player drank (top level, five minutes) is left alone by the lamp, and survives it going off.
        var p = OnFoot(server, "Drinker");
        TestGear.Wear(p.State, "ressaw_lamp");
        server.StartEffectForTest(p, BioEffect.NightSight, BioRules.MaxLevel, 300f);
        p.LampOn = true;
        TickSeconds(server, p, 1);
        p.LampOn = false;
        TickSeconds(server, p, 0.5);
        var drunk = Assert.Single(p.State.Effects, e => e.Effect == BioEffect.NightSight);
        Assert.True(drunk.SecondsLeft > 250f, $"the drunk night sight keeps its time ({drunk.SecondsLeft} s)");
    }

    [Fact]
    public void TheLamp_TakesOneOfTheThreeEffectPlaces_AndWaitsWhenAllAreTaken()
    {
        var server = Start("lamp3");
        var p = OnFoot(server, "Busy");
        TestGear.Wear(p.State, "ressaw_lamp");
        foreach (var effect in new[] { BioEffect.Speed, BioEffect.Jump, BioEffect.Strength })
        {
            server.StartEffectForTest(p, effect, 5, 300f);
        }

        p.LampOn = true;
        TickSeconds(server, p, 1);
        Assert.Equal(0f, NightSight(p));
        Assert.Equal(BioRules.MaxActiveEffects, p.State.Effects.Count);
    }

    [Fact]
    public void BothLamps_AreLamps_ForTheKeyAndTheBeam()
    {
        Assert.True(SuitEquipment.IsLamp(_content.GetItem("suit_lamp")));
        Assert.True(SuitEquipment.IsLamp(_content.GetItem("ressaw_lamp")));
        Assert.True(_content.GetItem("ressaw_lamp")!.NightSight);
        Assert.False(_content.GetItem("suit_lamp")!.NightSight);
        Assert.False(SuitEquipment.IsLamp(_content.GetItem("jetpack")));
        Assert.False(SuitEquipment.IsLamp(null));
        Assert.Equal(EquipSlots.Module, _content.GetItem("ressaw_lamp")!.EquipSlot);
        Assert.All(_content.Items.Values.Where(i => i.NightSight), i => Assert.Equal(EquipSlots.Module, i.EquipSlot));

        // Every lamp shows the helmet lamp to the others (the beam starts there) — GearLook keeps its own key list.
        foreach (var lamp in _content.Items.Values.Where(i => SuitEquipment.IsLamp(i)))
        {
            Assert.True(GearLook.Has(GearLook.Mask(k => k == lamp.Key), GearLook.Lamp), $"{lamp.Key} must set the Lamp bit");
        }
    }
}
