// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3180 — the board delivery jobs say who needs the goods, why, and what came of it (Justus, school club: "more story
/// with the jobs"). Content: every delivery item of the boards has at least two stories at every kind of board, with all
/// three texts in English AND German (read raw — the German localizer would fall back to English silently), only the
/// giver's placeholder and no story away from a village speaking of one. Server: the story is stable per job, a station
/// job tells a station's story, the hand-in answers with the thank-you, and with the AI backend on the request carries the
/// story as a brief and the backend's own thank-you wins.
/// </summary>
public sealed class BoardStoryTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_boardstory_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort temp cleanup */ }
    }

    /// <summary>The AI backend stand-in: every posting gets the same text; the requests are recorded (the server also asks
    /// off-thread for the other jobs on the board, hence the concurrent bag).</summary>
    private sealed class StoryAiProvider : IAiMissionProvider
    {
        public readonly ConcurrentBag<MissionTextRequest> Requests = new();

        public string Thanks { get; init; } = string.Empty;

        public MissionPlan? Generate(string context) => null;

        public string? GenerateNpcLine(NpcLineRequest request) => null;

        public MissionTextResult? GenerateMissionText(MissionTextRequest request)
        {
            Requests.Add(request);
            return new MissionTextResult { Title = "Kabelsalat im Orbit", Description = "Die Station ruft um Hilfe.", Thanks = Thanks };
        }

        public string[]? GenerateNpcLines(NpcLinesRequest request) => null;
    }

    // ---- content ---------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryDeliveryItem_HasTwoStories_AtEveryKindOfBoard()
    {
        var needs = SvGameServer.BoardDeliveryNeedsForTest();
        Assert.Equal(7, needs.Count);
        foreach (string need in needs)
        {
            foreach (string place in BoardStory.Places)
            {
                int count = Content.Value.BoardStoriesFor(need, place).Count;
                Assert.True(count >= 2, $"'{need}' at a {place} board has {count} stories (two at least)");
            }
        }
    }

    [Fact]
    public void EveryStory_ResolvesInEnglishAndGerman_SaysWhoNeedsIt_AndStatesNoNumbers()
    {
        var tables = new[] { ("en", TestLocales.Load("en")), ("de", TestLocales.Load("de")) };
        Assert.NotEmpty(Content.Value.BoardStories);
        foreach (var story in Content.Value.BoardStories)
        {
            Assert.False(string.IsNullOrWhiteSpace(story.Brief), $"'{story.Key}' has no brief for the AI wording");
            foreach (var (lang, table) in tables)
            {
                foreach (string key in new[] { story.TitleKey, story.DescriptionKey, story.ThanksKey })
                {
                    Assert.True(table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text), $"{lang}: missing '{key}'");
                    Assert.All(Placeholder.Matches(text!).Select(m => m.Value), token => Assert.Equal(BoardStory.GiverToken, token));
                    Assert.DoesNotMatch(@"\d", text!); // the amount is the job's roll — the objective row states it
                }

                // Who needs it: a board with a giver names them; the ship's board has nobody standing at it.
                bool ship = story.Place == BoardStory.PlaceShip;
                Assert.True(ship != table[story.DescriptionKey].Contains(BoardStory.GiverToken, StringComparison.Ordinal),
                    $"{lang} '{story.DescriptionKey}': " + (ship ? "a ship story names no giver" : "the description must name the giver"));
                Assert.True(!ship || !table[story.ThanksKey].Contains(BoardStory.GiverToken, StringComparison.Ordinal), $"{lang} '{story.ThanksKey}'");
                Assert.DoesNotContain(BoardStory.GiverToken, table[story.TitleKey], StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NoTextAwayFromAVillage_SpeaksOfASettlement()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var keys = Content.Value.BoardStories.Where(s => s.Place != BoardStory.PlaceSettlement)
            .SelectMany(s => new[] { s.TitleKey, s.DescriptionKey, s.ThanksKey })
            .Concat(new[] { "mission.station.gather.title", "mission.station.gather.desc", "mission.base.gather.title", "mission.base.gather.desc" });
        foreach (string key in keys)
        {
            Assert.DoesNotContain("settlement", en[key], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Siedlung", de[key], StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- the pick ----------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("settle_1234_0", BoardStory.PlaceSettlement)]
    [InlineData("settle_1234_b0", BoardStory.PlaceSettlement)]
    [InlineData("station_7_3", BoardStory.PlaceStation)]
    [InlineData("station_7_m3", BoardStory.PlaceStation)]
    [InlineData("home_4242_m0", BoardStory.PlaceBase)]
    [InlineData("shipboard_99_1", BoardStory.PlaceShip)]
    public void TheJobId_NamesTheKindOfBoard(string missionId, string place)
        => Assert.Equal(place, SvGameServer.BoardStoryPlaceOf(missionId));

    [Fact]
    public void AJobKeepsItsStory_AndTheStoriesTakeTurns()
    {
        var stories = Content.Value.BoardStoriesFor("copper_ore", BoardStory.PlaceStation);
        Assert.Equal(stories.Count, stories.Distinct().Count());

        var picked = new HashSet<string>(StringComparer.Ordinal);
        for (int slot = 0; slot < 64; slot++)
        {
            string id = "station_7_m" + slot;
            var story = SvGameServer.PickBoardStory(stories, id);
            Assert.NotNull(story);
            Assert.Same(story, SvGameServer.PickBoardStory(stories, id)); // a pure function of the id — stable across reloads
            picked.Add(story!.Key);
        }

        Assert.Equal(stories.Count, picked.Count); // every story is told on some slot
        Assert.Null(SvGameServer.PickBoardStory(Array.Empty<BoardStory>(), "station_7_m0"));
    }

    // ---- the server --------------------------------------------------------------------------------------------------

    private (SvGameServer Server, SqliteWorldRepository Repo, RecordingTransport Transport) StartWithStations(string world,
        AiLevel ai = AiLevel.Off, IAiMissionProvider? provider = null)
    {
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            AiLevel = ai,
            World = new WorldDescription { SpaceStations = Frequency.Frequent },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, Content.Value, transport, repo, logger: null, aiProvider: provider);
        server.Start();
        return (server, repo, transport);
    }

    /// <summary>Docks at the first station and stands the player at its mission board; returns a delivery job it offers.</summary>
    private static string DockAndPickADelivery(SvGameServer server, PlayerSession player)
    {
        string id = player.State.PlayerId;
        server.EnterSpace(id);
        var station = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(id, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(id, station.Id);
        player.State.Position = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;
        return server.StationMissionIds.First(m => server.MissionDefForTest(m) is { } d && d.ChainId.Length == 0
            && d.Objectives.Count == 1 && d.Objectives[0].Type == MissionObjectiveType.Deliver);
    }

    private static MissionResult TurnInWithTheGoods(SvGameServer server, RecordingTransport transport, PlayerSession player, string job)
    {
        server.AcceptMission(player.State.PlayerId, job);
        var need = server.FirstObjectiveForTest(job)!;
        player.State.Inventory.Add(need.Target, need.Required, Content.Value.MaxStackOf(need.Target));
        server.TurnInMission(player.State.PlayerId, job);
        return transport.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<MissionResult>()
            .Last(r => r.Success && r.MissionId == job);
    }

    [Fact]
    public void AStationJob_TellsAStationStory_NamesItsGiver_AndThanksOnHandIn()
    {
        var (server, repo, transport) = StartWithStations("story_station");
        using (repo)
        {
            var player = server.AddLocalPlayer("Runner", "de");
            string job = DockAndPickADelivery(server, player);
            var def = server.MissionDefForTest(job)!;
            var de = TestLocales.Load("de");
            Assert.StartsWith(BoardStory.KeyPrefix + BoardStory.PlaceStation + ".", def.NameKey, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(def.GiverName));

            // The posting is display text in the player's language with the giver filled in — not the settlement's text.
            var posting = server.BoardPostingTextForTest(player.State.PlayerId, job)!;
            Assert.Equal(de[def.NameKey], posting.Title);
            Assert.Equal(de[def.DescriptionKey].Replace(BoardStory.GiverToken, def.GiverName, StringComparison.Ordinal), posting.Description);
            Assert.Contains(def.GiverName, posting.Description, StringComparison.Ordinal);
            Assert.NotEqual(de["mission.settlement.gather.title"], posting.Title);
            Assert.DoesNotContain("Siedlung", posting.Description, StringComparison.Ordinal);

            // The hand-in answers with the story's thank-you.
            var done = TurnInWithTheGoods(server, transport, player, job);
            Assert.Equal(MissionOutcomes.Completed, done.Outcome);
            Assert.Equal(de[def.ThanksKey].Replace(BoardStory.GiverToken, def.GiverName, StringComparison.Ordinal), done.Thanks);
            Assert.True(done.FreeText);
            Assert.Equal(posting.Title, done.Title);

            // Accepting is no hand-in: no thank-you there.
            Assert.All(transport.Sent.Select(s => s.Msg).OfType<MissionResult>().Where(r => r.Outcome == MissionOutcomes.Accepted),
                r => Assert.Equal(string.Empty, r.Thanks));
        }
    }

    [Fact]
    public void AStationJob_IsCoinedWithTheSameStory_AfterItsDefinitionIsForgotten()
    {
        var (server, repo, _) = StartWithStations("story_recoin");
        using (repo)
        {
            var player = server.AddLocalPlayer("Runner");
            string job = DockAndPickADelivery(server, player);
            server.AcceptMission(player.State.PlayerId, job);
            string story = server.MissionDefForTest(job)!.NameKey;

            server.ForgetMissionDefForTest(job); // what a restart does to a board job's definition
            server.OfferedMissionIdsForTest(player.State.PlayerId); // the board window coins the held job back

            Assert.Equal(story, server.MissionDefForTest(job)!.NameKey);
        }
    }

    [Fact]
    public void WithTheAiBackend_TheRequestCarriesTheStory_AndTheBackendsThanksIsUsed()
    {
        var provider = new StoryAiProvider { Thanks = "Danke! Der Funk läuft wieder, und die Frachter docken an." };
        var (server, repo, transport) = StartWithStations("story_ai", AiLevel.TextOnly, provider);
        using (repo)
        {
            var player = server.AddLocalPlayer("Runner", "de");
            string job = DockAndPickADelivery(server, player);
            var def = server.MissionDefForTest(job)!;
            Assert.NotNull(server.MissionTextForTest(player.State.PlayerId, job));

            var story = Content.Value.BoardStories.Single(s => s.TitleKey == def.NameKey);
            Assert.Contains(provider.Requests, r => r.Brief == story.Brief && r.NeedItem == story.Need && r.GiverName == def.GiverName);

            Assert.Equal(provider.Thanks, server.MissionThanksForTest(player.State.PlayerId, job));
            Assert.Equal(provider.Thanks, TurnInWithTheGoods(server, transport, player, job).Thanks);
        }
    }

    [Fact]
    public void ABackendWithoutAThankYou_LeavesTheAuthoredOne()
    {
        var provider = new StoryAiProvider(); // an older backend: Title + Description only
        var (server, repo, _) = StartWithStations("story_ai_old", AiLevel.TextOnly, provider);
        using (repo)
        {
            var player = server.AddLocalPlayer("Runner");
            string job = DockAndPickADelivery(server, player);
            var def = server.MissionDefForTest(job)!;
            Assert.NotNull(server.MissionTextForTest(player.State.PlayerId, job));

            string authored = TestLocales.Load("en")[def.ThanksKey].Replace(BoardStory.GiverToken, def.GiverName, StringComparison.Ordinal);
            Assert.Equal(authored, server.MissionThanksForTest(player.State.PlayerId, job));
            Assert.Equal("Kabelsalat im Orbit", server.BoardPostingTextForTest(player.State.PlayerId, job)!.Title); // the AI posting
        }
    }

    [Fact]
    public void TheScreen_DropsARefusedThankYou_ButKeepsThePosting()
    {
        var screened = new ScreenedAiTextProvider(new StoryAiProvider { Thanks = "BAD WORDS" }, t => t == "BAD WORDS" ? null : t);
        var text = screened.GenerateMissionText(new MissionTextRequest());
        Assert.NotNull(text);
        Assert.Equal("Kabelsalat im Orbit", text!.Title);
        Assert.Equal(string.Empty, text.Thanks);
    }
}
