// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bio lab, earlier and aboard (#2248), its guidance (#2249) and the research screen's forward view (#2250): the lab
/// is a root blueprint with Mix and Change right after it; aboard your own ship the bio lab MODULE is the lab — in the
/// parked cabin and in the interior in space, never from the pilot seat, a spacewalk or someone else's ship; VEGA says
/// once what Lab Tuning opens and what to do when a material changes nothing; and a blueprint lists what it unlocks.
/// </summary>
public sealed class BioLabAccessTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_bioaccess_" + Guid.NewGuid().ToString("N"));

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
        catch (IOException)
        {
            // best effort
        }
    }

    private SvGameServer NewServer(string world, Action<ServerConfig>? tune = null, IServerTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = "jungle", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        tune?.Invoke(config);
        var server = new SvGameServer(config, Content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void Empty(Inventory inventory)
    {
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            inventory.SetSlot(i, null);
        }
    }

    private static IEnumerable<T> SentTo<T>(NpcLifeWorld.RecordingTransport transport, SvSession who)
        => transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<T>();

    private static int VegaLines(NpcLifeWorld.RecordingTransport transport, SvSession who, string key)
        => SentTo<ShipAiLine>(transport, who).Count(l => l.LineKey == key);

    private static CreatureSpecies Critter() => new()
    {
        Id = "lab_critter",
        Name = "Test critter",
        VoiceSeed = 4243,
        DropKind = CreatureDropKind.Food,
        DropItem = "raw_meat",
    };

    /// <summary>Analyses one sample and returns the lab's answer.</summary>
    private static BioLabResult Analyse(SvGameServer server, NpcLifeWorld.RecordingTransport t, SvSession p, uint seed)
    {
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        return SentTo<BioLabResult>(t, p).Last();
    }

    // ---------------- #2248: the chain ----------------

    [Fact]
    public void TheLab_IsARootBlueprint_AndMixAndChangeFollowItDirectly()
    {
        var lab = Content.GetBlueprint(BioItems.LabBlueprint)!;
        Assert.Empty(lab.Prerequisites);
        Assert.Equal(40, lab.KnowledgeCost);

        foreach (var key in new[] { BioItems.SynthesisBlueprint, BioItems.TuningBlueprint })
        {
            var bp = Content.GetBlueprint(key)!;
            Assert.Equal(new[] { BioItems.LabBlueprint }, bp.Prerequisites);
            Assert.True(bp.KnowledgeCost <= 60, key);
            // Nothing that needs a refinery or oil first: the point is to experiment early.
            Assert.DoesNotContain(bp.UnlockCost, c => c.Item is "lubricant" or "oil");
        }

        // The detoxifier and bio refining keep their own content; they just no longer stand in front of the lab.
        Assert.NotNull(Content.GetBlueprint("detoxifier"));
        Assert.NotNull(Content.GetBlueprint("bio_refining"));
    }

    [Fact]
    public void TheModule_IsBuiltWithTheLabBlueprint_AndIsNamedInBothLanguages()
    {
        var module = Content.GetShipModule(BioItems.LabModule)!;
        Assert.Equal(BioItems.LabBlueprint, module.RequiredBlueprint);
        Assert.False(module.Mandatory);
        Assert.NotEmpty(module.BuildCost);
        foreach (var locale in new[] { "en", "de" })
        {
            var table = Locale(locale);
            Assert.True(table.ContainsKey(module.NameKey), locale + " " + module.NameKey);
            Assert.True(table.ContainsKey(module.DescriptionKey), locale + " " + module.DescriptionKey);
        }
    }

    // ---------------- #2248: the module aboard ----------------

    [Fact]
    public void InTheParkedCabin_TheModuleIsTheLab_WithoutItTheCabinHasNone()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_module_cabin", c => c.PlaceStarterShip = true, t);
        var p = server.AddLocalPlayer("Pilot");
        Assert.True(server.HasShip);
        var (origin, size) = server.LandedShipBoundsForTest("Pilot");
        p.State.Position = new Vector3f(origin.X + 1.5f, origin.Y + 1, origin.Z + (size.Z / 2) + 0.5f);
        p.State.AboardShip = true;
        uint seed = server.GiveCreatureSampleForTest(p, Critter(), 2);

        Assert.Equal("srv.bio.need_lab", Analyse(server, t, p, seed).MessageKey);

        server.Ship.Modules.Add(BioItems.LabModule);
        var result = Analyse(server, t, p, seed);
        Assert.True(result.Success, result.MessageKey);
        Assert.True(server.BioAnalysedForTest(p.State.PlayerId, seed));
    }

    [Fact]
    public void InSpace_TheModuleAnswersInTheInterior_NeverFromThePilotSeatOrASpacewalk()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_module_space", c =>
        {
            c.PlaceStarterShip = true;
            c.Rules.FreeSpaceFlight = true;
        }, t);
        var p = server.AddLocalPlayer("Pilot");
        server.Ship.Modules.Add(BioItems.LabModule);
        uint seed = server.GiveCreatureSampleForTest(p, Critter(), 3);

        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        Assert.Equal("srv.bio.need_lab", Analyse(server, t, p, seed).MessageKey); // the pilot seat

        server.EnterShipInterior("Pilot");
        Assert.True(server.InShipInterior("Pilot"));
        var result = Analyse(server, t, p, seed);
        Assert.True(result.Success, result.MessageKey);

        server.StartEvaFromShip("Pilot");
        Assert.True(p.State.InEva);
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Mix, Sample = seed });
        Assert.Equal("srv.bio.need_lab", SentTo<BioLabResult>(t, p).Last().MessageKey);
    }

    [Fact]
    public void AVisitor_GetsNothingFromSomeoneElsesModule()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_module_visitor", c => c.PlaceStarterShip = true, t);
        var owner = server.AddLocalPlayer("Owner");
        server.Ship.Modules.Add(BioItems.LabModule);
        var visitor = server.AddLocalPlayer("Visitor");
        var (origin, size) = server.LandedShipBoundsForTest("Owner");
        visitor.State.Position = new Vector3f(origin.X + 1.5f, origin.Y + 1, origin.Z + (size.Z / 2) + 0.5f);
        visitor.State.AboardShip = false; // a visitor in a parked hull has no aboard flag of their own
        uint seed = server.GiveCreatureSampleForTest(visitor, Critter(), 1);

        Assert.Equal("srv.bio.need_lab", Analyse(server, t, visitor, seed).MessageKey);
        Assert.NotNull(owner);
    }

    // ---------------- #2249: VEGA's guidance ----------------

    [Fact]
    public void ResearchingTheLabAndLabTuning_VegaSaysOnceWhatTheyOpen()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_hints_research", transport: t);
        var p = server.AddLocalPlayer("Researcher");
        p.State.KnowledgePoints = 200;
        foreach (var key in new[] { BioItems.LabBlueprint, BioItems.TuningBlueprint })
        {
            foreach (var cost in Content.GetBlueprint(key)!.UnlockCost)
            {
                p.State.Inventory.Add(cost.Item, cost.Count, 1024);
            }
        }

        server.UnlockBlueprint("Researcher", BioItems.LabBlueprint);
        server.UnlockBlueprint("Researcher", BioItems.TuningBlueprint);

        Assert.Contains(BioItems.TuningBlueprint, p.State.UnlockedBlueprints);
        Assert.Equal(1, VegaLines(t, p, "vega.hint.bio_lab_unlocked"));
        Assert.Equal(1, VegaLines(t, p, "vega.hint.bio_tuning_unlocked"));
    }

    [Fact]
    public void AMaterialThatChangesNothing_IsRefused_AndVegaSaysOnceWhatWorks()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_hints_nochange", transport: t);
        var p = server.AddLocalPlayer("Smith");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        Empty(p.State.Inventory);
        server.World.SetBlock(new Vector3i(2, 200, 0), Content.GetBlock(BioItems.Lab)!.NumericId);
        p.State.UnlockedBlueprints.Add(BioItems.TuningBlueprint);
        Assert.Equal(0, p.State.Inventory.Add("basic_drill", 1, 1));
        Assert.Equal(0, p.State.Inventory.Add("copper_wire", 3, 1024)); // conductive: energy — a basic drill uses none

        var change = new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "basic_drill", MaterialItem = "copper_wire" };
        server.BioLabForTest(p, change);
        server.BioLabForTest(p, change);

        Assert.All(SentTo<BioLabResult>(t, p), r => Assert.Equal("srv.bio.no_change", r.MessageKey));
        Assert.Equal(1, VegaLines(t, p, "vega.hint.bio_no_change"));
        Assert.Equal("basic_drill", p.State.Inventory.Slots[0]!.Item);
        Assert.Equal(3, p.State.Inventory.CountOf("copper_wire"));
    }

    [Fact]
    public void AMissingMaterial_IsNamedAsTheMaterial_NotAsASample()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("lab_hints_material", transport: t);
        var p = server.AddLocalPlayer("Smith");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        Empty(p.State.Inventory);
        server.World.SetBlock(new Vector3i(2, 200, 0), Content.GetBlock(BioItems.Lab)!.NumericId);
        p.State.UnlockedBlueprints.Add(BioItems.TuningBlueprint);
        Assert.Equal(0, p.State.Inventory.Add("basic_drill", 1, 1));

        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Change, TargetItem = "basic_drill", MaterialItem = "steel" });

        Assert.Equal("srv.bio.no_material", SentTo<BioLabResult>(t, p).Last().MessageKey);
    }

    // ---------------- #2250: what a blueprint unlocks ----------------

    [Fact]
    public void TheLabBlueprint_ListsTheLabTheModuleAndWhatFollows()
    {
        var unlocks = BlueprintUnlocks.For(Content, BioItems.LabBlueprint);

        Assert.Contains(BioItems.Lab, unlocks.Items);
        Assert.Contains(BioItems.Sampler, unlocks.Items);
        Assert.Contains(BioItems.LabModule, unlocks.Modules);
        Assert.Contains(BioItems.SynthesisBlueprint, unlocks.LeadsTo);
        Assert.Contains(BioItems.TuningBlueprint, unlocks.LeadsTo);
        Assert.Contains(BioItems.CrossingBlueprint, unlocks.LeadsTo);
        Assert.Equal(new[] { "blueprint.feature.bio_tuning" }, BlueprintUnlocks.For(Content, BioItems.TuningBlueprint).Features);
        Assert.True(BlueprintUnlocks.For(Content, "no_such_blueprint").IsEmpty);
    }

    [Fact]
    public void EveryDeclaredFeature_HasItsTextInEnglishAndGerman()
    {
        var keys = Content.Blueprints.Values.SelectMany(b => b.Features).ToList();
        Assert.NotEmpty(keys);
        foreach (var locale in new[] { "en", "de" })
        {
            var table = Locale(locale);
            Assert.All(keys, k => Assert.True(table.ContainsKey(k), $"{locale}: {k}"));
        }
    }

    private static Dictionary<string, string> Locale(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")))!;
}
