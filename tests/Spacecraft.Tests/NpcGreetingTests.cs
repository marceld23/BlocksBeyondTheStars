using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Spacecraft.GameServer;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Item 15 — contextual NPC greetings. Covers the HTTP provider's <c>/npc-line</c> parsing, and the server
/// greeting flow: the static-fallback path (AI off → empty line so the client localizes), the LLM path
/// (provider line is produced + cached + reused), the proximity gate, and that the live context (role +
/// player language) reaches the provider.
/// </summary>
public sealed class NpcGreetingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcGreetingTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spacecraft_greet_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    // ---- Provider: HTTP contract parsing -----------------------------------------------------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public StubHandler(HttpStatusCode code, string body) { _code = code; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_code) { Content = new StringContent(_body) });
    }

    private static HttpAiMissionProvider HttpProvider(HttpStatusCode code, string body)
        => new("http://test.local", new HttpClient(new StubHandler(code, body)));

    [Fact]
    public void HttpProvider_ParsesTextField()
    {
        var p = HttpProvider(HttpStatusCode.OK, "{\"Text\":\"Welcome aboard, pilot.\"}");
        Assert.Equal("Welcome aboard, pilot.", p.GenerateNpcLine(new NpcLineRequest { Role = "vendor" }));
    }

    [Fact]
    public void HttpProvider_NonSuccess_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.InternalServerError, "{\"Text\":\"x\"}").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void HttpProvider_MalformedBody_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.OK, "not json").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void HttpProvider_BlankText_ReturnsNull()
        => Assert.Null(HttpProvider(HttpStatusCode.OK, "{\"Text\":\"   \"}").GenerateNpcLine(new NpcLineRequest()));

    [Fact]
    public void NullProvider_ReturnsNull()
        => Assert.Null(new NullAiMissionProvider().GenerateNpcLine(new NpcLineRequest { Role = "vendor" }));

    // ---- Server greeting flow ----------------------------------------------------------------

    private sealed class FakeNpcProvider : IAiMissionProvider
    {
        private readonly string? _line;
        public NpcLineRequest? Last;
        public int Calls;
        public FakeNpcProvider(string? line) => _line = line;
        public MissionPlan? Generate(string context) => null;
        public string? GenerateNpcLine(NpcLineRequest request) { Last = request; Calls++; return _line; }
        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;
    }

    /// <summary>Starts a world with an inhabited settlement that staffs a vendor, then drops a player onto the
    /// vendor's marker so the greeting is in reach. Searches seeds like the other settlement tests.</summary>
    private SvGameServer StartedWithVendor(AiLevel level, IAiMissionProvider? provider, string locale,
        out SqliteWorldRepository repo, out PlayerSession player, out Vector3f vendorHome)
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, $"g_{level}_{seed}"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = $"g_{seed}", Seed = seed, StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
                PlaceSettlements = true, PlaceWrecks = false, AiLevel = level,
            };
            var server = new SvGameServer(config, _content, st, repo, logger: null, aiProvider: provider);
            server.Start();

            if (server.HasSettlement && !server.SettlementRuined
                && server.NpcSnapshots.Any(n => n.Role == "vendor"))
            {
                vendorHome = server.NpcSnapshots.First(n => n.Role == "vendor").Home;
                player = server.AddLocalPlayer("Visitor", locale);
                player.State.Position = vendorHome;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with a vendor found across 120 seeds.");
    }

    [Fact]
    public void AiOff_GreetingIsEmpty_SoClientUsesStaticFallback()
    {
        var server = StartedWithVendor(AiLevel.Off, new FakeNpcProvider("LLM line"), "en", out var repo, out var p, out _);
        using (repo)
        {
            var line = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal(string.Empty, line);       // empty ⇒ client renders its localized fallback
            Assert.Equal(0, server.GreetingCacheCount); // nothing generated/cached while AI is off
        }
    }

    [Fact]
    public void AiOn_ProducesAndCachesProviderLine()
    {
        var provider = new FakeNpcProvider("Willkommen, Pilot!");
        var server = StartedWithVendor(AiLevel.Suggest, provider, "de", out var repo, out var p, out _);
        using (repo)
        {
            var first = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal("Willkommen, Pilot!", first);
            Assert.Equal(1, server.GreetingCacheCount);

            var second = server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.Equal("Willkommen, Pilot!", second);
            Assert.Equal(1, provider.Calls);           // second hit served from cache, provider not re-called
        }
    }

    [Fact]
    public void Context_CarriesRole_AndPlayerLanguage()
    {
        var provider = new FakeNpcProvider("hi");
        var server = StartedWithVendor(AiLevel.Suggest, provider, "de", out var repo, out var p, out _);
        using (repo)
        {
            server.GreetingLineForTest(p.State.PlayerId, "vendor");
            Assert.NotNull(provider.Last);
            Assert.Equal("vendor", provider.Last!.Role);
            Assert.Equal("de", provider.Last.Language);  // the player's join locale reaches the LLM
        }
    }

    [Fact]
    public void OutOfReach_NoGreeting()
    {
        var server = StartedWithVendor(AiLevel.Suggest, new FakeNpcProvider("hi"), "en", out var repo, out var p, out var home);
        using (repo)
        {
            p.State.Position = new Vector3f(home.X + 1000f, home.Y, home.Z); // walk far from the vendor
            Assert.Null(server.GreetingLineForTest(p.State.PlayerId, "vendor"));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
