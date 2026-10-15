// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Lodges (#2640): the data, the hut the generator builds, where the stamper puts them (new lush worlds only, clear of
/// everything), the protection of house and yard, the store crate a player may only look into, and the map marker a
/// player finds on foot. The residents and their talk are <see cref="LodgeResidentTests"/>, the work machine
/// <see cref="NpcWorkTests"/>.
/// </summary>
public sealed class LodgeTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A lush generation-25 world with three lodges (a woodcutter, a hunter and a miner at the time of writing).</summary>
    private const string LushPlanet = "meadowlands";
    private const long LushSeed = 3;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_lodge_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private SvGameServer Start(string save, string planet, long seed, out SqliteWorldRepository repo, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static HashSet<string> LocaleKeys(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    // ---- the data ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheFourProfessions_AreData_WithTheirTools_Trades_AndYards()
    {
        var byKey = Content.Lodges.ToDictionary(l => l.Key);
        Assert.Equal(new[] { "gatherer", "hunter", "miner", "woodcutter" }, byKey.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(("npc_basket", "gatherers", "gather", "plot"), (byKey["gatherer"].Held, byKey["gatherer"].Theme, byKey["gatherer"].Work, byKey["gatherer"].Yard));
        Assert.Equal(("npc_pickaxe", "miners", "mine", "quarry"), (byKey["miner"].Held, byKey["miner"].Theme, byKey["miner"].Work, byKey["miner"].Yard));
        Assert.Equal(("npc_axe", "woodcutters", "chop", "woodpile"), (byKey["woodcutter"].Held, byKey["woodcutter"].Theme, byKey["woodcutter"].Work, byKey["woodcutter"].Yard));
        Assert.Equal(("npc_bow", "hunters", "hunt", "drying_frame"), (byKey["hunter"].Held, byKey["hunter"].Theme, byKey["hunter"].Work, byKey["hunter"].Yard));
        foreach (var l in Content.Lodges)
        {
            Assert.Equal("npc.activity." + l.Activity, l.ActivityKey);
            Assert.Contains(l.Activity, new[] { "gathering", "mining", "chopping", "hunting" });
            int deals = Content.Recipes.Values.Count(r => MarketStock.IsTrade(r) && r.MarketTheme == l.Theme);
            Assert.InRange(deals, 2, 6);
            if (l.Partner.Length > 0)
            {
                Assert.NotEqual(LodgeRules.WorkMine, Content.LodgeProfession(l.Partner)!.Work); // only the lodge's own profession has the face
            }
        }
    }

    [Fact]
    public void TheValidation_RejectsUnknownWorkYardTraitThemeAndAMiningPartner()
    {
        var copy = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        copy.SetLodges(new[]
        {
            new LodgeProfessionDefinition { Key = "x", Held = "npc_axe", Theme = "nobody", Activity = "a", Work = "dig", Yard = "moat", Weights = { ["gold"] = 1.0 } },
            new LodgeProfessionDefinition { Key = "y", Held = "npc_axe", Theme = "miners", Activity = "a", Work = "chop", Yard = "woodpile", Partner = "z", Weights = { ["trees"] = 1.0 } },
            new LodgeProfessionDefinition { Key = "z", Held = "npc_pickaxe", Theme = "miners", Activity = "a", Work = "mine", Yard = "quarry", Weights = { ["ores"] = 0.0 } },
        });
        var ex = Assert.Throws<ContentValidationException>(() => copy.Validate());
        string all = string.Join("\n", ex.Problems);
        Assert.Contains("unknown work 'dig'", all);
        Assert.Contains("unknown yard 'moat'", all);
        Assert.Contains("unknown trait 'gold'", all);
        Assert.Contains("'nobody', which has no market deals", all);
        Assert.Contains("partner 'z', who mines", all);
        Assert.Contains("'z' has no positive weight", all);
    }

    [Fact]
    public void EveryNewText_ResolvesInEnglishAndGerman()
    {
        var en = LocaleKeys("en");
        var de = LocaleKeys("de");
        var keys = new List<string> { "poi.lodge", "npc.hint.lodge", "npc.hint.bandit_camp", "npc.hint.settlement", "srv.lodge.store_owned", "srv.protect.lodge" };
        foreach (var l in Content.Lodges)
        {
            keys.Add(l.NameKey);
            keys.Add("npc.greet." + l.Key);
            keys.Add(l.ActivityKey);
            keys.Add("ui.vendor.theme." + l.Theme);
        }

        foreach (var d in Content.Dialogs.Where(d => d.Key.StartsWith("lodge_", StringComparison.Ordinal)))
        {
            foreach (var node in d.Nodes)
            {
                keys.Add(node.PromptKey);
                keys.AddRange(node.Choices.SelectMany(c => new[] { c.TextKey, c.ResponseKey }));
            }
        }

        keys.AddRange(new[] { "dlg.lodge.r_gift_again", "dlg.lodge.r_gift_empty", "dlg.lodge.r_gift_full", "dlg.lodge_gatherer.r_poison" });
        foreach (string key in keys.Distinct())
        {
            Assert.True(en.Contains(key), $"en.json lacks '{key}'");
            Assert.True(de.Contains(key), $"de.json lacks '{key}'");
        }

        var english = Content.CreateLocalizer(Shared.Localization.GameLocale.English);
        Assert.Contains("{name}", english.Get("srv.lodge.store_owned")); // the server token's argument fills {name}
        Assert.Contains("{0}", english.Get("poi.lodge"));
        Assert.Contains("{name}", english.Get("npc.hint.lodge"));
        Assert.Contains("{job}", english.Get("npc.hint.lodge"));
    }

    // ---- the generator -------------------------------------------------------------------------------------------

    private static readonly string[] Yards = { LodgeRules.YardPlot, LodgeRules.YardQuarry, LodgeRules.YardWoodpile, LodgeRules.YardDryingFrame };

    private static SettlementStructure Build(long seed, string yard, IReadOnlyList<string>? ores = null, int beds = 0)
        => LodgeGenerator.Generate(seed, yard, "grass", ores ?? new[] { "iron_ore", "copper_ore" }, alien: false, Content, beds);

    private ushort Id(string key) => Content.GetBlock(key)!.NumericId.Value;

    [Fact]
    public void TheGenerator_IsDeterministic()
    {
        foreach (string yard in Yards)
        {
            var a = Build(4711, yard);
            var b = Build(4711, yard);
            Assert.Equal((a.Width, a.Height, a.Length), (b.Width, b.Height, b.Length));
            for (int x = 0; x < a.Width; x++)
                for (int y = 0; y < a.Height; y++)
                    for (int z = 0; z < a.Length; z++)
                    {
                        Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                        Assert.Equal(a.GetShape(x, y, z), b.GetShape(x, y, z));
                    }

            Assert.Equal(a.Markers.Select(m => (m.Type, m.LocalPos)), b.Markers.Select(m => (m.Type, m.LocalPos)));
        }
    }

    [Fact]
    public void EveryYard_HasTheHouse_ABedPerResident_TheFire_TheBench_TheStore_AndItsOwnMarkers()
    {
        ushort bed = Id("bed"), campfire = Id("campfire"), crate = Id("wood_crate"), log = Id("wood_log");
        foreach (string yard in Yards)
        {
            foreach (int beds in new[] { 1, 2 })
            {
                var s = Build(99, yard, beds: beds);
                string ctx = $"{yard}, {beds} bed(s)";
                var markers = s.Markers;
                var bedMarkers = markers.Where(m => m.Type == LodgeGenerator.MarkerBed).ToList();
                Assert.True(bedMarkers.Count == beds, ctx);
                Assert.True(markers.Count(m => m.Type == LodgeGenerator.MarkerNpc) == beds, ctx);
                foreach (var m in bedMarkers)
                {
                    Assert.Equal(bed, s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z));
                    Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(s.GetShape(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)));
                }

                var door = Assert.Single(markers, m => m.Type == LodgeGenerator.MarkerDoor);
                Assert.Equal(DoorWall.AlongX, door.DoorAxis);
                Assert.Equal(0, s.Get(door.LocalPos.X, door.LocalPos.Y, door.LocalPos.Z)); // the doorway is open air (the door hangs in it)
                var fire = Assert.Single(markers, m => m.Type == LodgeGenerator.MarkerCampfire);
                Assert.Equal(campfire, s.Get(fire.LocalPos.X, fire.LocalPos.Y, fire.LocalPos.Z));
                var store = Assert.Single(markers, m => m.Type == LodgeGenerator.MarkerStore);
                Assert.Equal(crate, s.Get(store.LocalPos.X, store.LocalPos.Y, store.LocalPos.Z));
                var seats = markers.Where(m => m.Type == LodgeGenerator.MarkerSeat).ToList();
                Assert.True(seats.Count >= 2, ctx);
                Assert.All(seats, m => Assert.True(FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s.GetShape(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)))));
                Assert.Equal(log, s.Get(1, 1, 1)); // the log frame's corner post
                Assert.Single(markers, m => m.Type == LodgeGenerator.MarkerYard);

                switch (yard)
                {
                    case LodgeRules.YardPlot:
                        var plot = markers.Where(m => m.Type == LodgeGenerator.MarkerPlot).ToList();
                        Assert.NotEmpty(plot);
                        Assert.All(plot, m => Assert.Contains(s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z), new[] { Id("flora_cropberry"), Id("flora_cropgrain") }));
                        break;
                    case LodgeRules.YardQuarry:
                        Assert.InRange(markers.Count(m => m.Type == LodgeGenerator.MarkerQuarry), 4, 8);
                        break;
                    case LodgeRules.YardWoodpile:
                        var grove = markers.Where(m => m.Type == LodgeGenerator.MarkerGrove).ToList();
                        Assert.InRange(grove.Count, 2, 3);
                        foreach (var t in grove)
                        {
                            Assert.Equal(log, s.Get(t.LocalPos.X, 1, t.LocalPos.Z));
                            Assert.Equal(log, s.Get(t.LocalPos.X, 2, t.LocalPos.Z)); // at least two trunk logs
                            Assert.Contains(s.Get(t.LocalPos.X, 0, t.LocalPos.Z), new[] { Id("grass"), Id("dirt") }); // sapling ground
                        }

                        break;
                    default:
                        Assert.Single(markers, m => m.Type == LodgeGenerator.MarkerHunt);
                        break;
                }
            }
        }
    }

    [Fact]
    public void TheQuarryFace_UsesOnlyThePlanetsOwnTier1Ores()
    {
        var ores = new[] { "tin_ore" };
        for (long seed = 1; seed <= 8; seed++)
        {
            var s = Build(seed, LodgeRules.YardQuarry, ores);
            var face = s.Markers.Where(m => m.Type == LodgeGenerator.MarkerQuarry).ToList();
            Assert.InRange(face.Count, 4, 8);
            Assert.All(face, m => Assert.Equal(Id("tin_ore"), s.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)));
            for (int x = 8; x < s.Width; x++) // the yard (the house wears the village trim, carbon)
                for (int y = 0; y < s.Height; y++)
                    for (int z = 0; z < s.Length; z++)
                    {
                        ushort b = s.Get(x, y, z);
                        if (b != 0 && Content.BlockById(new Shared.Primitives.BlockId(b))?.Category == "ore")
                        {
                            Assert.Equal(Id("tin_ore"), b); // no ore in the quarry but the planet's own
                        }
                    }
        }

        // No tier-1 ore of its own: the face is plain stone, still something for the miner to break.
        var bare = Build(3, LodgeRules.YardQuarry, Array.Empty<string>());
        var cells = bare.Markers.Where(m => m.Type == LodgeGenerator.MarkerQuarry).ToList();
        Assert.Equal(4, cells.Count);
        Assert.All(cells, m => Assert.Equal(Id("stone"), bare.Get(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z)));
    }

    // ---- placement -----------------------------------------------------------------------------------------------

    [Fact]
    public void AGeneration25LushWorld_GrowsLodges_ClearOfPadsSettlementsAndOtherLodges()
    {
        var server = Start("lush", LushPlanet, LushSeed, out var repo);
        using (repo)
        {
            var lodges = server.LodgesForTest;
            Assert.InRange(lodges.Count, 1, LodgeRules.HardCap);
            int circ = server.World.Circumference;
            foreach (var l in lodges)
            {
                int cx = l.Origin.X + LodgeGenerator.Size / 2, cz = l.Origin.Z + LodgeGenerator.Size / 2;
                foreach (var s in server.SettlementsForTest)
                {
                    int dx = Math.Abs(WorldConstants.WrapDeltaX(cx - (s.MinX + s.MaxX) / 2, circ)) - (s.MaxX - s.MinX) / 2 - LodgeGenerator.Size / 2;
                    int dz = Math.Abs(cz - (s.MinZ + s.MaxZ) / 2) - (s.MaxZ - s.MinZ) / 2 - LodgeGenerator.Size / 2;
                    Assert.True(Math.Max(dx, dz) >= SvGameServerLodgeMargin, $"lodge {l.Key} stands {Math.Max(dx, dz)} blocks from a settlement");
                }

                foreach (var pad in server.LandingPadCenters)
                {
                    double d = Math.Sqrt(Math.Pow(WorldConstants.WrapDeltaX(cx - pad.X, circ), 2) + Math.Pow(cz - pad.Z, 2));
                    Assert.True(d > 20, $"lodge {l.Key} sits {d:0} blocks from pad {pad.Index}");
                }

                foreach (var other in lodges.Where(o => o.Key != l.Key))
                {
                    int dx = Math.Abs(WorldConstants.WrapDeltaX(l.Origin.X - other.Origin.X, circ));
                    int dz = Math.Abs(l.Origin.Z - other.Origin.Z);
                    Assert.True(Math.Max(dx, dz) >= LodgeGenerator.Size + 20, $"lodges {l.Key} and {other.Key} crowd each other");
                }

                Assert.True(server.IsLodgeBlockForTest(new Vector3i(l.Origin.X + 3, l.Origin.Y + 1, l.Origin.Z + 3)), "the house is protected");
                Assert.True(server.IsSettlementProtectedForTest(new Vector3i(l.Origin.X + 1, l.Origin.Y + 1, l.Origin.Z + 1)), "the log frame is the hut's");
                Assert.False(server.IsLodgeBlockForTest(new Vector3i(l.Origin.X + 3, l.Origin.Y + 1, l.Origin.Z + LodgeGenerator.Size + 3)), "the land round it is free");
                Assert.False(l.Revealed); // nobody found it yet
                Assert.StartsWith("lodge:", l.StoreId, StringComparison.Ordinal);
                Assert.Contains(server.Containers, c => c.Id == l.StoreId && c.Kind == "crate");
            }

            // The placement is pinned, the profession with it.
            var records = server.PlacementRecordsForTest.Where(r => r.Kind == "lodge" && r.Placed).ToList();
            Assert.Equal(lodges.Count, records.Count);
            Assert.All(records, r => Assert.Contains(lodges, l => l.Profession == r.Template && l.Origin.X == r.X && l.Origin.Z == r.Z));
            Assert.True(server.FeatureStampedForTest("lodges"));
            server.Stop();
        }
    }

    /// <summary>The margin the stamper keeps round settlements (<c>LodgeReserveMargin</c>) less the slack of the search.</summary>
    private const int SvGameServerLodgeMargin = 40;

    [Fact]
    public void TheLodges_AreTheSameAfterAReload_StampedOnce_WithTheSameResidents()
    {
        List<(string Key, string Profession, Vector3i Origin)> first;
        List<(string Name, string Job, string PersonKey)> people;
        var server = Start("reload", LushPlanet, LushSeed, out var repo);
        using (repo)
        {
            first = server.LodgesForTest.Select(l => (l.Key, l.Profession, l.Origin)).ToList();
            Assert.NotEmpty(first);
            people = first.SelectMany(l => server.LodgeResidentsForTest(l.Key)).Select(r => (r.Name, r.Job, r.PersonKey)).ToList();
            Assert.NotEmpty(people);
            server.Stop();
        }

        var again = Start("reload", LushPlanet, LushSeed, out var repo2);
        using (repo2)
        {
            Assert.Equal(first, again.LodgesForTest.Select(l => (l.Key, l.Profession, l.Origin)).ToList());
            Assert.Equal(people, first.SelectMany(l => again.LodgeResidentsForTest(l.Key)).Select(r => (r.Name, r.Job, r.PersonKey)).ToList());
            Assert.Equal(first.Count, again.Containers.Count(c => c.Id.StartsWith("lodge:", StringComparison.Ordinal))); // one store each, never a second
            again.Stop();
        }
    }

    [Fact]
    public void NoLodge_OnAnOlderGeneration_AnAirlessOrARuinWorld_OrWithSettlementsOff()
    {
        var older = Start("gen24", LushPlanet, LushSeed, out var r24, c => c.World.TerrainGeneration = 24);
        using (r24)
        {
            Assert.Empty(older.LodgesForTest);
            Assert.DoesNotContain(older.PlacementRecordsForTest, r => r.Kind == "lodge");
            older.Stop();
        }

        var off = Start("off", LushPlanet, LushSeed, out var rOff, c => c.World.Settlements = Frequency.Off);
        using (rOff)
        {
            Assert.Empty(off.LodgesForTest);
            off.Stop();
        }

        var noConfig = Start("noconfig", LushPlanet, LushSeed, out var rNo, c => c.PlaceLodges = false);
        using (rNo)
        {
            Assert.Empty(noConfig.LodgesForTest);
            noConfig.Stop();
        }

        foreach (string planet in new[] { "crystal", "toxic_world" })
        {
            var harsh = Start("harsh_" + planet, planet, LushSeed, out var rh);
            using (rh)
            {
                Assert.Empty(harsh.LodgesForTest);
                harsh.Stop();
            }
        }
    }

    // ---- on the arena: protection, the store, the map ------------------------------------------------------------

    private static string LodgeAt(NpcLifeWorld w, string profession, int beds = 1)
        => w.Server.SpawnLodgeForTest(NpcLifeWorld.Cx + 4, w.PadY, NpcLifeWorld.Cz - 6, profession, beds);

    private static Vector3i Local(NpcLifeWorld w, int x, int y, int z) => new(NpcLifeWorld.Cx + 4 + x, w.PadY + y, NpcLifeWorld.Cz - 6 + z);

    private static string? LastRejection(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;

    [Fact]
    public void HouseYardAndFace_AreProtected_APlotCropCanStillBePicked_TheGroveIsFree()
    {
        using var w = new NpcLifeWorld(Content, "lodge_protect");
        string key = LodgeAt(w, "gatherer");
        string name = w.Server.LodgeResidentsForTest(key)[0].Name;
        var p = w.Player.State;
        p.Inventory.SetSlot(0, new ItemStack("basic_drill", 1));

        // The log frame of the house: refused, and the refusal names whose hut it is.
        var post = Local(w, 1, 1, 1);
        p.Position = new Vector3f(post.X - 1.5f, w.Feet, post.Z - 1.5f);
        w.Server.MineBlock(NpcLifeWorld.Owner, post.X, post.Y, post.Z);
        Assert.False(w.Server.World.GetBlock(post).IsAir);
        Assert.Equal("@srv.protect.lodge:" + name, LastRejection(w));

        // The plot's fence: protected too. A crop in it: picked like any plant (the flora exemption).
        var fence = Local(w, 12, 1, 3);
        var crop = Local(w, 9, 1, 3);
        p.Position = new Vector3f(crop.X - 1.5f, w.Feet, crop.Z + 0.5f);
        Assert.True(w.Server.IsLodgeBlockForTest(fence));
        w.Server.MineBlock(NpcLifeWorld.Owner, fence.X, fence.Y, fence.Z);
        Assert.False(w.Server.World.GetBlock(fence).IsAir);
        Assert.True(w.Server.IsLodgeBlockForTest(crop));
        w.Server.MineBlock(NpcLifeWorld.Owner, crop.X, crop.Y, crop.Z);
        Assert.True(w.Server.World.GetBlock(crop).IsAir, "a plot crop is picked like any plant");

        // A miner's face is protected; a woodcutter's grove is not, its woodpile is.
        string minerKey = w.Server.SpawnLodgeForTest(NpcLifeWorld.Cx - 22, w.PadY, NpcLifeWorld.Cz - 6, "miner", 1);
        Assert.All(w.Server.LodgeFaceForTest(minerKey), f => Assert.True(w.Server.IsLodgeBlockForTest(f.Cell)));
        string woodKey = w.Server.SpawnLodgeForTest(NpcLifeWorld.Cx - 6, w.PadY, NpcLifeWorld.Cz + 9, "woodcutter", 1);
        var trunks = w.Server.LodgeMarkersForTest(woodKey).Where(m => m.Type == LodgeGenerator.MarkerGrove).Select(m => m.Pos.ToBlock()).ToList();
        Assert.NotEmpty(trunks);
        Assert.All(trunks, t => Assert.False(w.Server.IsLodgeBlockForTest(t)));
        Assert.All(trunks, t => Assert.False(w.Server.IsSettlementProtectedForTest(t)));
        Assert.True(w.Server.IsLodgeBlockForTest(new Vector3i(NpcLifeWorld.Cx - 6 + 8, w.Feet, NpcLifeWorld.Cz + 9 + 1)), "the woodpile is the hut's");
    }

    [Fact]
    public void TheStoreCrate_OpensToLook_ButNothingIsTakenPutSortedOrMoved()
    {
        using var w = new NpcLifeWorld(Content, "lodge_store");
        string key = LodgeAt(w, "gatherer");
        string name = w.Server.LodgeResidentsForTest(key)[0].Name;
        string store = w.Server.LodgesForTest.Single(l => l.Key == key).StoreId;
        w.Server.DepositIntoLodgeStoreForTest(key, "berries", 5);
        var crate = w.Server.Containers.Single(c => c.Id == store);
        var p = w.Player.State;
        p.Position = new Vector3f(crate.Position.X + 0.5f, w.Feet, crate.Position.Z + 2.5f);
        p.Inventory.Add("stone", 10, 64);
        p.Inventory.Add("berries", 2, 20);
        int berries = p.Inventory.CountOf("berries"), stones = p.Inventory.CountOf("stone");

        int mark = w.Transport.Sent.Count;
        w.Server.OpenContainerForTest(NpcLifeWorld.Owner, store);
        var shown = w.Transport.Sent.Skip(mark).Select(s => s.Msg).OfType<ContainerContents>().Last();
        Assert.Equal(store, shown.ContainerId);
        Assert.Contains(shown.Items, i => i.Item == "berries" && i.Count == 5); // looking is fine

        string refusal = "@srv.lodge.store_owned:" + name;
        w.Server.LootContainer(p.PlayerId, store);
        Assert.Equal(refusal, LastRejection(w));
        Assert.False(w.Server.MoveContainerItem(p.PlayerId, store, "berries", toContainer: false, all: true));
        Assert.False(w.Server.MoveContainerItem(p.PlayerId, store, "berries", toContainer: true, all: true));
        Assert.Equal(refusal, LastRejection(w));
        w.Server.DepositToContainer(p.PlayerId, store);
        Assert.Equal(refusal, LastRejection(w));
        w.Server.SetContainerFilterForTest(w.Player, store, new[] { "stone" });
        Assert.Equal(refusal, LastRejection(w));

        crate = w.Server.Containers.Single(c => c.Id == store);
        Assert.Equal(new[] { ("berries", 5) }, crate.Items.Select(s => (s.Item, s.Count)));
        Assert.Empty(crate.Filter);
        Assert.Equal(berries, p.Inventory.CountOf("berries"));
        Assert.Equal(stones, p.Inventory.CountOf("stone"));
    }

    [Fact]
    public void TheStoreCrate_KeepsSixGoods_AFullOneTakesTheOldestToTheVillage()
    {
        using var w = new NpcLifeWorld(Content, "lodge_store_cap");
        string key = LodgeAt(w, "gatherer");
        string store = w.Server.LodgesForTest.Single(l => l.Key == key).StoreId;
        foreach (string item in new[] { "berries", "grain", "plant_fiber", "fruit_round", "wood_log", "stone" })
        {
            w.Server.DepositIntoLodgeStoreForTest(key, item, 3);
        }

        w.Server.DepositIntoLodgeStoreForTest(key, "grain", 100); // merged, capped
        w.Server.DepositIntoLodgeStoreForTest(key, "copper_ore", 2); // a seventh good: the oldest lot leaves
        var items = w.Server.Containers.Single(c => c.Id == store).Items;
        Assert.Equal(6, items.Count);
        Assert.DoesNotContain(items, s => s.Item == "berries");
        Assert.Equal(40, items.Single(s => s.Item == "grain").Count);
        Assert.Equal("copper_ore", items[^1].Item);
    }

    [Fact]
    public void ALodge_IsOffTheMap_UntilAPlayerOnFootComesWithin40m()
    {
        using var w = new NpcLifeWorld(Content, "lodge_poi");
        string key = LodgeAt(w, "hunter");
        string name = w.Server.LodgeResidentsForTest(key)[0].Name;
        var center = w.Server.LodgesForTest.Single(l => l.Key == key).Center;
        var p = w.Player.State;

        p.Position = new Vector3f(center.X - 60f, w.Feet, center.Z);
        w.Server.TickLodgesForTest();
        Assert.DoesNotContain(w.Server.PlanetPoisForTest(p.PlayerId), poi => poi.Type == "lodge");

        p.Position = new Vector3f(center.X - 35f, w.Feet, center.Z);
        w.Server.TickLodgesForTest();
        var poi = Assert.Single(w.Server.PlanetPoisForTest(p.PlayerId), x => x.Type == "lodge");
        Assert.Equal(name + "'s hut", poi.Name);
        Assert.True(w.Server.LodgesForTest.Single(l => l.Key == key).Revealed);
    }
}
