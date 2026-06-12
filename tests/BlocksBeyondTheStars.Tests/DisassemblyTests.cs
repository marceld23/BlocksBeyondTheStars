using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Gear disassembly: at a workshop, a crafted item can be dismantled back into a portion of its
/// recipe components (partial recovery). Raw/un-craftable items can't be disassembled, and a
/// workshop is required.
/// </summary>
public sealed class DisassemblyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DisassemblyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_disasm_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "disasm"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "disasm",
            Seed = 2,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Disassemble_RecoversPortionOfComponents_AtWorkshop()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // The starter ship has a workshop module; AddLocalPlayer is aboard by default.
            var p = server.AddLocalPlayer("Tinker");
            p.State.Inventory.Remove("machete", p.State.Inventory.CountOf("machete")); // drop the starter melee weapon for a deterministic count
            p.State.Inventory.Add("machete", 1, 1); // recipe: iron_plate x3 + carbon x2 -> machete

            server.Disassemble("Tinker", "machete");

            Assert.Equal(0, p.State.Inventory.CountOf("machete"));         // consumed
            Assert.True(p.State.Inventory.CountOf("iron_plate") >= 1);     // ~half of 3 recovered
            Assert.True(p.State.Inventory.CountOf("carbon") >= 1);         // ~half of 2 recovered
        }
    }

    [Fact]
    public void Disassemble_RejectsRawMaterial()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Tinker");
            p.State.Inventory.Add("iron_ore", 3, 99); // no recipe produces iron_ore

            server.Disassemble("Tinker", "iron_ore");

            Assert.Equal(3, p.State.Inventory.CountOf("iron_ore")); // unchanged
        }
    }

    [Fact]
    public void Disassemble_RequiresAWorkshop()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Tinker");
            p.State.AboardShip = false; // no workshop reachable outside the ship
            p.State.Inventory.Remove("machete", p.State.Inventory.CountOf("machete")); // drop the starter melee weapon for a deterministic count
            p.State.Inventory.Add("machete", 1, 1);

            server.Disassemble("Tinker", "machete");

            Assert.Equal(1, p.State.Inventory.CountOf("machete")); // not dismantled
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
