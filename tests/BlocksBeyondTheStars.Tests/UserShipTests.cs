// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3520: user ships from the ship editor (<c>usercontent/ship_types/&lt;key&gt;.json</c>) — the loader reads one file per
/// ship, drops every broken one with a warning and keeps loading, keeps them out of <see cref="GameContent.Ships"/> (the wreck
/// pools and the creative grant stay as they were), and the consumers opt in by use: raiders and traders fly the <c>npc</c>
/// ones (a hull wider than a pad never lands), the craft list takes a <c>craft</c> one only with the server option.
/// </summary>
public sealed class UserShipTests : IClassFixture<UserShipTests.UserShipContent>
{
    private readonly UserShipContent _fx;

    public UserShipTests(UserShipContent fx) => _fx = fx;

    [Fact]
    public void Loader_ReadsAUserShip_ClampsItsStats_AndFillsTheDefaults()
    {
        var user = Assert.IsType<UserShip>(_fx.Content.UserShipByKey("aa_good"));
        var def = user.Definition;

        Assert.Same(def, _fx.Content.GetShip("aa_good"));
        Assert.Same(user.Layout, _fx.Content.GetShipLayout("aa_good"));
        Assert.Equal("aa_good", def.Layout); // the layout under the ship's own key
        Assert.Equal("Good Ship", def.DisplayName);
        Assert.Equal("Flies well.", def.DisplayDescription);
        Assert.Equal(string.Empty, def.NameKey); // a literal name, no locale key

        // The editor's ranges: hull 20–500, shield 0–300, speed and handling 0.4–2.5.
        Assert.Equal((500f, 0f, 0.4f, 2.5f), (def.BaseHull, def.BaseShield, def.FlightSpeed, def.Handling));
        Assert.Equal((3, 2, 3), (def.InteriorWidth, def.Height, def.InteriorLength)); // the interior from the layout
        Assert.Equal(new[] { "cockpit" }, def.StartModules); // none named → derived from the station tiles
        Assert.Equal(UserShipUses.Default, user.Uses);
        Assert.Equal(new[] { UserShipUses.Npc }, UserShipUses.Default); // a wreck (claimable) and the craft list are opted in
        Assert.Equal(GameContent.UserShipWeightMax, user.Weight); // 5000 asked for
        Assert.True(user.FitsPad);
    }

    [Fact]
    public void Loader_DerivesAMissingInterior_AndIgnoresAnUnknownUse()
    {
        var crafty = Assert.IsType<UserShip>(_fx.Content.UserShipByKey("ab_crafty"));

        Assert.Equal((4, 1, 2), (crafty.Definition.InteriorWidth, crafty.Definition.Height, crafty.Definition.InteriorLength));
        Assert.Equal(new[] { UserShipUses.Npc, UserShipUses.Craft }, crafty.Uses);
        Assert.Contains(_fx.Warnings, w => w.Contains("ab_crafty", StringComparison.Ordinal) && w.Contains("teleport", StringComparison.Ordinal));
        Assert.Equal(new[] { "cockpit" }, crafty.Definition.StartModules); // named ones are kept as named
    }

    [Theory]
    [InlineData("Hauler", "taken by a shipped ship or layout")]
    [InlineData("ship_scout", "taken by a shipped ship or layout")]
    [InlineData("Custom", "reserved")] // the self-built ships' type: it would resolve for every one of them
    [InlineData("STARTER", "reserved")]
    [InlineData("AA_GOOD", "already loaded")]
    [InlineData("bad key!", "the key must be")]
    [InlineData("bc_unknown_cell", "no_such_block")]
    [InlineData("bd_empty", "no cells")]
    [InlineData("be_outside", "outside the ship editor's room")]
    [InlineData("bf_module", "unknown module 'warp_core_9000'")]
    [InlineData("bg_item", "unknown craft-cost item 'unobtainium'")]
    [InlineData("bh_blueprint", "unknown blueprint 'bp_nope'")]
    [InlineData("ad_wide_craft", "17 × 17 landing square")]
    [InlineData("bi_broken", "unreadable user ship")]
    public void Loader_DropsABrokenShip_WithAWarning_AndKeepsLoading(string key, string reason)
    {
        Assert.Contains(_fx.Warnings, w => w.Contains(key, StringComparison.Ordinal) && w.Contains(reason, StringComparison.Ordinal));
        if (key != "AA_GOOD")
        {
            Assert.Null(_fx.Content.UserShipByKey(key));
        }

        // The good files around it still loaded.
        Assert.Equal(new[] { "aa_good", "ab_crafty", "ac_wide" }, _fx.Content.UserShips.Select(u => u.Key));
    }

    [Fact]
    public void Ships_StaysShippedOnly_SoTheWreckPoolsAndTheCreativeGrantDoNotChange()
    {
        Assert.Equal(_fx.Shipped.Ships.Keys, _fx.Content.Ships.Keys); // same keys, same (dictionary) order
        Assert.DoesNotContain(_fx.Content.UserShips, u => _fx.Content.Ships.ContainsKey(u.Key));
        Assert.Null(_fx.Content.GetShip("no_such_ship"));
        Assert.Same(_fx.Content.Ships["hauler"], _fx.Content.GetShip("hauler")); // a shipped key resolves as before
    }

    [Fact]
    public void Ships_WithoutAUserContentFolder_HaveNoUserShips()
        => Assert.Empty(_fx.Shipped.UserShips);

    [Fact]
    public void ReservedKeys_AreTheGamesOwn_CaseInsensitive()
    {
        Assert.True(_fx.Content.IsReservedUserShipKey("custom"));
        Assert.True(_fx.Content.IsReservedUserShipKey("Starter"));
        Assert.True(_fx.Content.IsReservedUserShipKey("SCOUT"));
        Assert.True(_fx.Content.IsReservedUserShipKey("ship_scout")); // a shipped layout
        Assert.False(_fx.Content.IsReservedUserShipKey("custom_2"));
        Assert.False(_fx.Content.IsReservedUserShipKey("aa_good"));
        Assert.Null(_fx.Content.GetShip("Custom")); // no user ship answers for the self-built ships
    }

    [Fact]
    public void AnOwnedUserShipWhoseFileIsGone_KeepsItsName_AndStandsAsTheStarterWithoutItsEdits()
    {
        string saves = Path.Combine(_fx.Root, "saves_gone_" + Guid.NewGuid().ToString("N"));
        string id;
        var edit = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(1, 1, 1);
        using (var run = _fx.Server(allowCrafting: true, saves: saves))
        {
            var pilot = run.Server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true;
            (bool ok, id) = run.Server.CraftShip("Pilot", "ab_crafty");
            Assert.True(ok);
            Assert.Equal("Crafty", run.Server.OwnedShips[id].DisplayName); // the name is kept with the ship
            Assert.True(run.Server.SwitchShip(id));
            run.Repo.SetStructureBlock("ship:" + pilot.State.PlayerId + "#" + id, edit, _fx.Content.GetBlock("gold_block")!.NumericId.Value);
            run.Server.Stop();
        }

        // The file is gone: the next start loads the shipped content only.
        var log = new CapturingLogger();
        using (var run = _fx.Server(allowCrafting: true, content: _fx.Shipped, saves: saves, logger: log))
        {
            var pilot = run.Server.AddLocalPlayer("Pilot");
            Assert.Equal("ab_crafty", run.Server.OwnedShips[id].ShipType); // kept: it comes back whole with its file
            var entry = run.Server.OwnedShipsMessageForTest("Pilot").Ships.Single(s => s.Id == id);
            Assert.Equal("Crafty", entry.Name); // not a locale key nobody has

            Assert.True(run.Server.SwitchShip(id));
            var hull = run.Server.ActiveShipStructureForTest(pilot.State.PlayerId);
            var starter = run.Server.NpcShipStructureForTest("starter");
            Assert.Equal(starter.Cells.Count, hull.Cells.Count);
            Assert.NotEqual(_fx.Shipped.GetBlock("gold_block")!.NumericId, hull.Get(edit)); // the other hull's edit is not stamped over it
            Assert.Contains(log.Warnings, w => w.Contains("ab_crafty", StringComparison.Ordinal) && w.Contains("starter hull", StringComparison.Ordinal));
            run.Server.Stop();
        }
    }

    [Fact]
    public void CraftShip_RefusesAUserShip_ByDefault()
    {
        using var run = _fx.Server(allowCrafting: false);
        var pilot = run.Server.AddLocalPlayer("Pilot");
        pilot.State.InstantBuild = true; // no material cost: only the user-ship rule can refuse

        var (ok, _) = run.Server.CraftShip("Pilot", "ab_crafty");

        Assert.False(ok);
        Assert.DoesNotContain(run.Server.OwnedShips.Values, s => s.ShipType == "ab_crafty");
    }

    [Fact]
    public void CraftShip_AllowsAUserShip_OnlyWithTheOptionAndTheCraftUse()
    {
        using var run = _fx.Server(allowCrafting: true);
        var pilot = run.Server.AddLocalPlayer("Pilot");
        int before = pilot.State.Inventory.CountOf("iron_plate");
        pilot.State.Inventory.Add("iron_plate", 5, 99);

        Assert.False(run.Server.CraftShip("Pilot", "aa_good").Ok); // no craft use
        var (ok, id) = run.Server.CraftShip("Pilot", "ab_crafty");

        Assert.True(ok);
        Assert.Equal("ab_crafty", run.Server.OwnedShips[id].ShipType);
        Assert.Equal(before, pilot.State.Inventory.CountOf("iron_plate")); // the file's cost was paid

        // The client's content does not know the ship: the fleet message carries its name and flight values (#949 pattern).
        var entry = run.Server.OwnedShipsMessageForTest("Pilot").Ships.Single(s => s.Id == id);
        Assert.Equal("Crafty", entry.Name);
        Assert.Equal((1.5f, 0.8f), (entry.FlightSpeed, entry.Handling));
        Assert.All(run.Server.OwnedShipsMessageForTest("Pilot").Ships.Where(s => s.Id != id), s => Assert.Equal(string.Empty, s.Name));
    }

    [Fact]
    public void Raider_CanFlyAUserShip_AppendedAfterTheShippedDesigns()
    {
        using var run = _fx.Server(allowCrafting: false, start: false);
        using var shipped = _fx.Server(allowCrafting: false, content: _fx.Shipped, start: false);

        var shippedPool = shipped.Server.RaiderDesignPoolForTest();
        var pool = run.Server.RaiderDesignPoolForTest();

        // The shipped designs keep their places (a server without user ships draws as before); the npc ships follow.
        Assert.Equal(shippedPool.Concat(new[] { "aa_good", "ab_crafty", "ac_wide" }), pool);

        var hull = run.Server.NpcShipStructureForTest("aa_good");
        Assert.Equal((3, 2, 3), (hull.Width, hull.Height, hull.Length)); // the user layout, not the starter fallback
        Assert.Contains(hull.StationCells, c => c.Type == "cockpit");
    }

    [Fact]
    public void Trader_FliesAUserShip_ButOnlyLandsOneThatFitsAPad()
    {
        using var run = _fx.Server(allowCrafting: false, start: false);
        using var shipped = _fx.Server(allowCrafting: false, content: _fx.Shipped, start: false);
        var shippedPool = shipped.Server.TraderShipPoolForTest(landing: false);

        Assert.Equal(shippedPool.Concat(new[] { "aa_good", "ab_crafty", "ac_wide" }), run.Server.TraderShipPoolForTest(landing: false));
        Assert.Equal(shippedPool.Concat(new[] { "aa_good", "ab_crafty" }), run.Server.TraderShipPoolForTest(landing: true));
        Assert.False(Assert.IsType<UserShip>(_fx.Content.UserShipByKey("ac_wide")).FitsPad);
    }

    /// <summary>The real content plus a user-content folder holding one file per case, loaded once for the class.</summary>
    public sealed class UserShipContent : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_usership_" + Guid.NewGuid().ToString("N"));

        public UserShipContent()
        {
            string dir = Path.Combine(_root, "usercontent", "ship_types");
            Directory.CreateDirectory(dir);
            foreach (var (file, json) in Files())
            {
                File.WriteAllText(Path.Combine(dir, file), json);
            }

            Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir(), Path.Combine(_root, "usercontent"), Warnings.Add);
            Shipped = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        }

        public GameContent Content { get; }

        /// <summary>The same data without a user-content folder.</summary>
        public GameContent Shipped { get; }

        public List<string> Warnings { get; } = new();

        /// <summary>The fixture's temp folder.</summary>
        public string Root => _root;

        /// <summary>A server on <paramref name="content"/> (default: with the user ships) and a save of its own (or the one in
        /// <paramref name="saves"/>) — started unless <paramref name="start"/> is false (the NPC pools and hulls read only the
        /// content).</summary>
        public ServerRun Server(bool allowCrafting, GameContent? content = null, bool start = true, string? saves = null,
            BlocksBeyondTheStars.GameServer.IGameLogger? logger = null)
        {
            saves ??= Path.Combine(_root, "saves_" + Guid.NewGuid().ToString("N"));
            var repo = new SqliteWorldRepository(new SaveGamePaths(saves, "usership"));
            var config = new ServerConfig
            {
                WorldName = "usership",
                Seed = 1,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                AllowUserShipCrafting = allowCrafting,
            };
            var server = new SvGameServer(config, content ?? Content, new LoopbackServerTransport(new LoopbackLink()), repo, logger);
            if (start)
            {
                server.Start();
            }

            return new ServerRun(server, repo);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // a save file still held open by SQLite on Windows — the temp folder is cleaned by the OS later
            }
        }

        private static IEnumerable<(string File, string Json)> Files()
        {
            const string floor3 = "{\"x\":0,\"y\":0,\"z\":0,\"id\":\"iron_wall\"},{\"x\":1,\"y\":0,\"z\":0,\"id\":\"iron_wall\"},"
                + "{\"x\":2,\"y\":0,\"z\":0,\"id\":\"iron_wall\"},{\"x\":0,\"y\":0,\"z\":1,\"id\":\"iron_wall\"},"
                + "{\"x\":1,\"y\":0,\"z\":1,\"id\":\"iron_wall\"},{\"x\":2,\"y\":0,\"z\":1,\"id\":\"iron_wall\"},"
                + "{\"x\":0,\"y\":0,\"z\":2,\"id\":\"iron_wall\"},{\"x\":1,\"y\":0,\"z\":2,\"id\":\"iron_wall\"},"
                + "{\"x\":2,\"y\":0,\"z\":2,\"id\":\"iron_wall\"},{\"x\":1,\"y\":1,\"z\":1,\"kind\":\"station\",\"id\":\"cockpit\"}";
            string Wide(int from) => string.Join(",", Enumerable.Range(from, 20).Select(x => $"{{\"x\":{x},\"y\":0,\"z\":0,\"id\":\"iron_wall\"}}"));
            string Ship(string key, string cells, string extra = "")
                => $"{{\"key\":\"{key}\",\"layout\":{{\"width\":3,\"height\":2,\"length\":3,\"cells\":[{cells}]}}{extra}}}";

            yield return ("aa_good.json", "{\"key\":\"aa_good\",\"name\":\" Good Ship \",\"description\":\"Flies well.\",\"baseHull\":9999,"
                + "\"baseShield\":-5,\"flightSpeed\":0.1,\"handling\":7,\"weight\":5000,"
                + $"\"layout\":{{\"width\":3,\"height\":2,\"length\":3,\"cells\":[{floor3}]}}}}");
            yield return ("ab_crafty.json", "{\"name\":\"Crafty\",\"flightSpeed\":1.5,\"handling\":0.8,\"uses\":[\"npc\",\"CRAFT\",\"teleport\"],"
                + "\"startModules\":[\"cockpit\"],\"craftCost\":[{\"item\":\"iron_plate\",\"count\":5}],"
                + "\"layout\":{\"cells\":[{\"x\":3,\"y\":0,\"z\":1,\"id\":\"iron_wall\"},{\"x\":-1,\"y\":0,\"z\":0,\"id\":\"light\"}]}}");
            yield return ("ac_wide.json", $"{{\"key\":\"ac_wide\",\"layout\":{{\"cells\":[{Wide(-1)}]}}}}");
            yield return ("ad_wide_craft.json", $"{{\"key\":\"ad_wide_craft\",\"uses\":[\"craft\"],\"layout\":{{\"cells\":[{Wide(-1)}]}}}}");
            yield return ("ba_hauler.json", Ship("Hauler", floor3));
            yield return ("bb_layoutkey.json", Ship("ship_scout", floor3));
            yield return ("bc_unknown_cell.json", Ship("bc_unknown_cell", "{\"x\":0,\"y\":0,\"z\":0,\"id\":\"no_such_block\"}"));
            yield return ("bd_empty.json", Ship("bd_empty", string.Empty));
            yield return ("be_outside.json", Ship("be_outside", "{\"x\":100,\"y\":0,\"z\":0,\"id\":\"iron_wall\"}"));
            yield return ("bf_module.json", Ship("bf_module", floor3, ",\"startModules\":[\"warp_core_9000\"]"));
            yield return ("bg_item.json", Ship("bg_item", floor3, ",\"craftCost\":[{\"item\":\"unobtainium\",\"count\":1}]"));
            yield return ("bh_blueprint.json", Ship("bh_blueprint", floor3, ",\"requiredBlueprint\":\"bp_nope\""));
            yield return ("bi_broken.json", "{\"key\":\"bi_broken\",\"layout\":{\"cells\":[{");
            yield return ("bj_dup.json", Ship("AA_GOOD", floor3));
            yield return ("bk_badkey.json", Ship("bad key!", floor3));
            yield return ("bl_custom.json", Ship("Custom", floor3));
            yield return ("bm_starter.json", Ship("STARTER", floor3));
        }
    }

    /// <summary>A started server and its repository, disposed together.</summary>
    public sealed class ServerRun : IDisposable
    {
        public ServerRun(SvGameServer server, SqliteWorldRepository repo)
        {
            Server = server;
            Repo = repo;
        }

        public SvGameServer Server { get; }

        public SqliteWorldRepository Repo { get; }

        public void Dispose() => Repo.Dispose();
    }
}
