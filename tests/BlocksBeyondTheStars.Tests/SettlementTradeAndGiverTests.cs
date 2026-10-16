// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2698: a settlement's trade (what its vendors sell) and its quest giver's name came from the settlement NAME alone, so
/// every "Karth Village" sold the same goods and had the same quartermaster in every world and on every body. From terrain
/// generation 26 both come from the body-aware place key (body, name and world seed); a world of generation 25 or older
/// keeps both exactly (the pinned values below were taken from the code before the change), and one settlement never mixes
/// the two — its quartermaster, board jobs, vendors and chain giver agree.
/// </summary>
public sealed class SettlementTradeAndGiverTests : IDisposable
{
    private const int Gen = WorldDescription.HashVarietyGeneration;
    private const int OldGen = Gen - 1;

    private static readonly string[] Trades = { "miners", "traders", "researchers", "settlers" };

    /// <summary>Every settlement name the generator can coin (roots × the four tiers' suffixes).</summary>
    private static readonly string[] SettlementNames =
        new[] { "Karth", "Vega", "Mira", "Dorn", "Ysel", "Tarn", "Olun", "Reth", "Sabik", "Cael" }
            .SelectMany(r => new[] { " City", " Metropolis", " Prime", " Central", " Town", " Colony", " Outpost", " Heights",
                " Hamlet", " Cross", " Camp", " Rest", " Village", " Hollow", " Glen", " Stead", " End" }.Select(s => r + s))
            .ToArray();

    private readonly SettlementTestWorlds _worlds = new();
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        _worlds.Dispose();
    }

    /// <summary>A settlement's trade exactly as <c>SettlementTradeFor(name)</c> computed it before #2698.</summary>
    private static string PreChangeTrade(string name)
        => string.IsNullOrEmpty(name) ? "settlers" : Trades[(uint)WorldGenerator.StableHash(name) % (uint)Trades.Length];

    /// <summary>A coined giver name exactly as <c>CoinGiverName(key)</c> computed it before #2698.</summary>
    private static string PreChangeGiver(string key)
        => NameGenerator.Person(new System.Random(unchecked((int)WorldGenerator.StableHash("giver:" + key))));

    // ---------------- Pure ----------------

    [Fact]
    public void OldGeneration_TradeAndGiver_ComeFromTheNameAlone_BitForBit()
    {
        // Pinned from the code before #2698: no vendor switches its goods, nobody a player knows is renamed.
        string karth = SvGameServer.SettlementLocationKeyFor("sys0-p1", "Karth Village", 4242);
        Assert.Equal("settlers", SvGameServer.SettlementTradeFor("Karth Village", karth, OldGen));
        Assert.Equal("Tua Broompliol", SvGameServer.SettlementGiverNameFor("Karth Village", karth, OldGen));
        string vega = SvGameServer.SettlementLocationKeyFor("sys3-p2-m0", "Vega Town", -7);
        Assert.Equal("researchers", SvGameServer.SettlementTradeFor("Vega Town", vega, OldGen));
        Assert.Equal("Reishox Kroollyoz", SvGameServer.SettlementGiverNameFor("Vega Town", vega, OldGen));
        string mira = SvGameServer.SettlementLocationKeyFor("sys0-p0", "Mira Hamlet", 1);
        Assert.Equal("settlers", SvGameServer.SettlementTradeFor("Mira Hamlet", mira, OldGen));
        Assert.Equal("Sheeshzre Braenath", SvGameServer.SettlementGiverNameFor("Mira Hamlet", mira, OldGen));

        // Every generation below 26, every body, every world: the name alone decides, exactly as before.
        foreach (int generation in new[] { 0, 9, 21, OldGen })
            foreach (string body in new[] { "sys0-p1", "sys2-p0-m0", "sys7-a3" })
                foreach (long seed in new[] { 1L, 42L, -7L })
                    foreach (string name in SettlementNames)
                    {
                        string key = SvGameServer.SettlementLocationKeyFor(body, name, seed);
                        string trade = SvGameServer.SettlementTradeFor(name, key, generation);
                        Assert.Equal(PreChangeTrade(name), trade);
                        Assert.Equal(PreChangeGiver(name), SvGameServer.SettlementGiverNameFor(name, key, generation));
                        for (int vendor = 0; vendor < 4; vendor++)
                        {
                            Assert.Equal(SvGameServer.VendorThemeForTest(name, vendor, trade),
                                SvGameServer.SettlementVendorThemeFor(name, key, vendor, trade, generation));
                        }
                    }

        Assert.Equal("settlers", SvGameServer.SettlementTradeFor(string.Empty, karth, OldGen));
    }

    [Fact]
    public void NewGeneration_NamesakesOnTwoBodies_AndInTwoWorlds_DifferInTradeAndGiver()
    {
        // The same name on another body of the same world, and on the same body of another world. Four trades: two
        // independent places differ in about three cases of four; two coined names practically always.
        int n = SettlementNames.Length;
        int bodyTrade = 0, bodyGiver = 0, worldTrade = 0, worldGiver = 0, classicDiffers = 0;
        foreach (string name in SettlementNames)
        {
            string here = SvGameServer.SettlementLocationKeyFor("sys0-p1", name, 42);
            string otherBody = SvGameServer.SettlementLocationKeyFor("sys2-p0-m0", name, 42);
            string otherWorld = SvGameServer.SettlementLocationKeyFor("sys0-p1", name, 43);

            bodyTrade += Trade(name, here) != Trade(name, otherBody) ? 1 : 0;
            worldTrade += Trade(name, here) != Trade(name, otherWorld) ? 1 : 0;
            bodyGiver += Giver(name, here) != Giver(name, otherBody) ? 1 : 0;
            worldGiver += Giver(name, here) != Giver(name, otherWorld) ? 1 : 0;
            classicDiffers += SvGameServer.SettlementTradeFor(name, here, OldGen) != SvGameServer.SettlementTradeFor(name, otherBody, OldGen)
                              || SvGameServer.SettlementGiverNameFor(name, here, OldGen) != SvGameServer.SettlementGiverNameFor(name, otherWorld, OldGen) ? 1 : 0;
        }

        Assert.Equal(0, classicDiffers); // the bug: the name alone decided
        Assert.InRange(bodyTrade / (double)n, 0.6, 0.9);
        Assert.InRange(worldTrade / (double)n, 0.6, 0.9);
        Assert.True(bodyGiver >= n * 95 / 100, $"only {bodyGiver} of {n} namesakes on two bodies have their own quest giver");
        Assert.True(worldGiver >= n * 95 / 100, $"only {worldGiver} of {n} namesakes in two worlds have their own quest giver");

        static string Trade(string name, string key) => SvGameServer.SettlementTradeFor(name, key, Gen);
        static string Giver(string name, string key) => SvGameServer.SettlementGiverNameFor(name, key, Gen);
    }

    [Fact]
    public void NewGeneration_TradesAndExtraVendors_AreEvenlySpread()
    {
        var counts = Trades.ToDictionary(t => t, _ => 0);
        var extra = Trades.ToDictionary(t => t, _ => 0);
        int places = 0;
        foreach (string body in new[] { "sys0-p0", "sys0-p1", "sys4-p2", "sys9-p1-m0" })
            foreach (string name in SettlementNames)
            {
                string key = SvGameServer.SettlementLocationKeyFor(body, name, 4242);
                string trade = SvGameServer.SettlementTradeFor(name, key, Gen);
                counts[trade]++;
                places++;
                Assert.Equal(trade, SvGameServer.SettlementVendorThemeFor(name, key, 0, trade, Gen)); // the first vendor keeps the place's own trade
                extra[SvGameServer.SettlementVendorThemeFor(name, key, 1, trade, Gen)]++;
            }

        foreach (string t in Trades)
        {
            Assert.InRange(counts[t] / (double)places, 0.18, 0.32);
            Assert.InRange(extra[t] / (double)places, 0.18, 0.32);
        }
    }

    [Fact]
    public void NewGeneration_TradeAndGiver_AreBitFrozen()
    {
        // Known answers: a generation-26 save's vendors and quest givers (their names are in players' memories) must never move.
        string karth = SvGameServer.SettlementLocationKeyFor("sys0-p1", "Karth Village", 4242);
        Assert.Equal("traders", SvGameServer.SettlementTradeFor("Karth Village", karth, Gen));
        Assert.Equal("Qong Zreimez", SvGameServer.SettlementGiverNameFor("Karth Village", karth, Gen));
        Assert.Equal("traders", SvGameServer.SettlementVendorThemeFor("Karth Village", karth, 1, "x", Gen));
        Assert.Equal("miners", SvGameServer.SettlementVendorThemeFor("Karth Village", karth, 2, "x", Gen));
        string vega = SvGameServer.SettlementLocationKeyFor("sys3-p2-m0", "Vega Town", -7);
        Assert.Equal("settlers", SvGameServer.SettlementTradeFor("Vega Town", vega, Gen));
        Assert.Equal("Zaek Zaezunsuach", SvGameServer.SettlementGiverNameFor("Vega Town", vega, Gen));
        Assert.Equal("miners", SvGameServer.SettlementVendorThemeFor("Vega Town", vega, 1, "x", Gen));
        string mira = SvGameServer.SettlementLocationKeyFor("sys0-p0", "Mira Hamlet", 1);
        Assert.Equal("miners", SvGameServer.SettlementTradeFor("Mira Hamlet", mira, Gen));
        Assert.Equal("Seer Grixthyng", SvGameServer.SettlementGiverNameFor("Mira Hamlet", mira, Gen));
    }

    // ---------------- One settlement, one set of values ----------------

    /// <summary>A settlement-only jungle world of <paramref name="generation"/> with an inhabited settlement that has a
    /// mission board, a player standing at that board, and the settlement's name.</summary>
    private SvGameServer WorldWithBoard(int generation, string player, out string settlement)
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = _worlds.Start(_content, "jungle", seed, out var repo, generation);
            _repos.Add(repo);
            var board = server.SettlementMarkers.FirstOrDefault(m => m.Type == "mission_board");
            if (server.InhabitedSettlementCount == 0 || board.Type is null)
            {
                continue;
            }

            var p = server.AddLocalPlayer(player);
            p.State.Position = board.Pos;
            string here = server.CurrentPlaceKeyForTest(player);
            settlement = server.SettlementNamesForTest.First(n => server.SettlementLocationKeyForTest(n) == here);
            return server;
        }

        throw new Xunit.Sdk.XunitException($"no jungle world of generation {generation} with a mission board in 40 seeds");
    }

    [Theory]
    [InlineData(OldGen)]
    [InlineData(Gen)]
    public void OneSettlement_ItsQuartermaster_BoardJobs_VendorsAndChainGiver_Agree(int generation)
    {
        var server = WorldWithBoard(generation, "Visitor", out string name);
        string place = server.SettlementLocationKeyForTest(name);
        var (trade, giver) = server.SettlementTradeAndGiverForTest(name);

        // The values of this generation: the name alone below 26, the place from it.
        Assert.Equal(SvGameServer.SettlementTradeFor(name, place, generation), trade);
        Assert.Equal(SvGameServer.SettlementGiverNameFor(name, place, generation), giver);
        if (generation < Gen)
        {
            Assert.Equal(PreChangeTrade(name), trade);
            Assert.Equal(PreChangeGiver(name), giver);
        }

        // The quartermaster NPC carries the giver's name, and the board's jobs are handed out in it.
        var residents = server.SettlementResidentNamesForTest().Where(r => r.Settlement == name).ToList();
        Assert.Equal(giver, residents.Single(r => r.Job == "quartermaster").Name);
        var jobs = server.VisibleMissionIdsForTest("Visitor").Where(id => id.StartsWith(place + "_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, id => Assert.Equal(giver, server.MissionDefForTest(id)!.GiverName));

        // The vendors: the first one sells the settlement's trade, every further one its own roll of the same generation.
        var vendors = residents.Where(r => r.Job == "vendor").ToList();
        for (int i = 0; i < vendors.Count; i++)
        {
            Assert.Equal(SvGameServer.SettlementVendorThemeFor(name, place, i, trade, generation), vendors[i].Theme);
        }

        // A chain giver nobody named yet: the old key's coined name below 26 (as before), the quest giver from it.
        string chainGiver = server.ChainGiverFallbackNameForTest(place);
        Assert.Equal(generation < Gen ? PreChangeGiver(SvGameServer.LegacySettlementPlaceKeyForTest(name)) : giver, chainGiver);
    }

    [Fact]
    public void AHeldChain_KeepsItsGiversName()
    {
        var server = WorldWithBoard(Gen, "Hero", out string name);
        var hero = server.Sessions.Values.Single(s => s.State.Name == "Hero").State;
        string place = server.SettlementLocationKeyForTest(name);
        string giverKey = place + ":quartermaster";

        // Taking the first step names its giver in the player's memory — the settlement's quest giver.
        server.AcceptMission("Hero", "chain_needs_1");
        Assert.Contains(hero.Missions, m => m.MissionId == "chain_needs_1" && m.Status == MissionStatus.Active);
        Assert.Equal(server.SettlementTradeAndGiverForTest(name).Giver, hero.NpcMemory[giverKey].Name);

        // A chain held under another name (taken before this change) keeps it: the follow-up call names whom the player knows.
        hero.NpcMemory[giverKey].Name = "Ola Brannigan";
        int before = server.DialogRadioPendingNamesForTest("Hero").Count;
        hero.Inventory.Add("iron_ore", 8, 99);
        server.TurnInMission("Hero", "chain_needs_1");
        var calls = server.DialogRadioPendingNamesForTest("Hero");
        Assert.Equal(before + 1, calls.Count);
        Assert.Equal("Ola Brannigan", calls[^1]);
    }
}
