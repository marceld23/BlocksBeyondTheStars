// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A save across builds (#2221, #2223). Numeric block ids follow the sorted block keys, so a build that adds a
/// block shifts them; the first load remaps every stored id by key. These tests pin the three things that
/// surround that remap: it reaches the hull of a self-built ship, a copy of the save is taken before it runs, and
/// a save a NEWER build wrote is refused instead of being rewritten. And the rotating backups of a running server,
/// and the copy of an OLDER save before it is stamped with this build's save version (#2464).
/// </summary>
public sealed class SaveCompatibilityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SaveCompatibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_savecompat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort (Windows may still hold a handle for a moment)
        }
    }

    private SaveGamePaths Paths(string world) => new(_root, world);

    private SqliteWorldRepository OpenRepo(string world)
    {
        var repo = new SqliteWorldRepository(Paths(world));
        repo.Initialize();
        return repo;
    }

    private ServerConfig Config(string world, Action<ServerConfig>? configure = null)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9001,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        return config;
    }

    private readonly CapturingLogger _log = new();

    private SvGameServer NewServer(string world, IWorldRepository repo, Action<ServerConfig>? configure = null)
        => new(Config(world, configure), _content, new LoopbackServerTransport(new LoopbackLink()), repo, _log);

    private string[] Backups(string world, string prefix)
        => Directory.Exists(Paths(world).BackupsDirectory)
            ? Directory.GetFiles(Paths(world).BackupsDirectory, prefix + "*").Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    /// <summary>The palette a build WITHOUT the given blocks assigned: the same rule (air = 0, the rest in ordinal
    /// key order from 1), minus those keys — what a save written before they existed has on record.</summary>
    private Dictionary<ushort, string> PaletteWithout(params string[] missing)
    {
        var palette = new Dictionary<ushort, string>();
        ushort next = 1;
        foreach (var key in _content.Blocks.Keys.Where(k => !missing.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
        {
            palette[key == "air" ? (ushort)0 : next++] = key;
        }

        return palette;
    }

    private static ushort IdOf(Dictionary<ushort, string> palette, string key) => palette.First(kv => kv.Value == key).Key;

    private static string ShipJson(string databaseFile, string shipId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM ship WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", shipId);
        return (string)cmd.ExecuteScalar()!;
    }

    // ---------------- #2221: the hull of a self-built ship ----------------

    private static readonly Dictionary<ushort, string> PaletteA = new() { [5] = "steel_wall", [6] = "torch", [7] = "gone_block" };

    // A block now sorts in at 5: steel_wall and torch move up by one, gone_block is no longer in the game.
    private static readonly Dictionary<ushort, string> PaletteB = new() { [5] = "new_block", [6] = "steel_wall", [7] = "torch" };

    private static ShipState SelfBuiltShip() => new()
    {
        ShipType = ShipState.CustomShipType,
        BuiltCells = "0:0:0:5;1:0:0:6;-2:3:0:7",
        Commissioned = false,
        BuildLocationId = "rocky",
        BuildX = 11,
        BuildY = 64,
        BuildZ = -7,
        Hull = 77f,
        Modules = { "cockpit" },
    };

    private static void AssertRemappedHull(ShipState? built, ShipState? starter)
    {
        Assert.NotNull(built);
        Assert.Equal("0:0:0:6;1:0:0:7;-2:3:0:0", built!.BuiltCells); // steel_wall 5→6, torch 6→7, gone_block→air; negative coordinates intact

        // Nothing else in the row moved.
        Assert.True(built.IsCustom);
        Assert.False(built.Commissioned);
        Assert.Equal("rocky", built.BuildLocationId);
        Assert.Equal((11, 64, -7), (built.BuildX, built.BuildY, built.BuildZ));
        Assert.Equal(77f, built.Hull);
        Assert.Equal(new[] { "cockpit" }, built.Modules);

        Assert.NotNull(starter);
        Assert.Equal(string.Empty, starter!.BuiltCells); // a content ship has no hull string — left alone
        Assert.Equal("starter", starter.ShipType);
    }

    [Fact]
    public void PaletteShift_RemapsTheHullOfASelfBuiltShip_Sqlite()
    {
        using (var repo = OpenRepo("hull"))
        {
            repo.SaveShip("ship_Pilot#built", SelfBuiltShip());
            repo.SaveShip("ship_Pilot#starter", new ShipState());
            repo.EnsureBlockPalette(PaletteA);
            Assert.False(repo.BlockPaletteNeedsRemap(PaletteA));
        }

        using (var repo = OpenRepo("hull"))
        {
            Assert.True(repo.BlockPaletteNeedsRemap(PaletteB));
            repo.EnsureBlockPalette(PaletteB);
            Assert.False(repo.BlockPaletteNeedsRemap(PaletteB)); // done once: the next load finds nothing to do
            repo.EnsureBlockPalette(PaletteB);
        }

        using var reopened = OpenRepo("hull");
        AssertRemappedHull(reopened.LoadShip("ship_Pilot#built"), reopened.LoadShip("ship_Pilot#starter"));
    }

    [Fact]
    public void PaletteShift_RemapsTheHullOfASelfBuiltShip_InTheBrowserSave()
    {
        byte[] blob;
        var first = new MemoryWorldRepository(Paths("hull_mem"));
        first.Initialize();
        first.SaveShip("ship_Pilot#built", SelfBuiltShip());
        first.SaveShip("ship_Pilot#starter", new ShipState());
        first.EnsureBlockPalette(PaletteA);
        blob = first.ExportSnapshotBlob();

        var second = new MemoryWorldRepository(Paths("hull_mem"));
        second.Initialize();
        second.ImportSnapshotBlob(blob);
        Assert.True(second.BlockPaletteNeedsRemap(PaletteB));
        second.EnsureBlockPalette(PaletteB);
        Assert.False(second.BlockPaletteNeedsRemap(PaletteB));

        // …and it survives the next save/load of the blob.
        var third = new MemoryWorldRepository(Paths("hull_mem"));
        third.Initialize();
        third.ImportSnapshotBlob(second.ExportSnapshotBlob());
        AssertRemappedHull(third.LoadShip("ship_Pilot#built"), third.LoadShip("ship_Pilot#starter"));
    }

    [Fact]
    public void ShipRow_KeepsFieldsThisBuildDoesNotKnow_WhenItsHullIsRemapped()
    {
        // The remap rewrites one property of the row, not the row: a field a later build added stays.
        using (var repo = OpenRepo("hull_raw"))
        {
            repo.SaveShip("ship_Pilot#built", SelfBuiltShip());
            repo.EnsureBlockPalette(PaletteA);
        }

        string db = Paths("hull_raw").DatabaseFile;
        using (var connection = new SqliteConnection($"Data Source={db};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE ship SET json = json_insert(json, '$.FutureField', 'kept');";
            cmd.ExecuteNonQuery();
        }

        using (var repo = OpenRepo("hull_raw"))
        {
            repo.EnsureBlockPalette(PaletteB);
        }

        string json = ShipJson(db, "ship_Pilot#built");
        Assert.Contains("\"FutureField\":\"kept\"", json);
        Assert.Contains("0:0:0:6;1:0:0:7;-2:3:0:0", json);
    }

    [Fact]
    public void OldSave_WithASelfBuiltShip_KeepsItsHull_AndIsBackedUpBeforeTheRemap()
    {
        // A save from before the bio lab: its palette has no bio_lab / flora_hybrid, so every key behind them sits
        // one or two ids lower than in this build — and the hull of its self-built ship is written in THOSE ids.
        var oldPalette = PaletteWithout("bio_lab", "flora_hybrid");
        string[] hullKeys = { "ship_core", "ship_helm", "ship_engine", "steel_wall" };
        string oldHull = string.Join(";", hullKeys.Select((key, i) => $"{i}:0:-1:{IdOf(oldPalette, key)}"));
        using (var repo = OpenRepo("oldsave"))
        {
            repo.SaveShip("ship_Pilot#built", new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = oldHull });
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), IdOf(oldPalette, "ship_helm"));
            repo.EnsureBlockPalette(oldPalette);
        }

        Assert.Empty(Backups("oldsave", string.Empty));

        using (var repo = new SqliteWorldRepository(Paths("oldsave")))
        {
            var server = NewServer("oldsave", repo);
            server.Start();

            // The hull still names the same blocks, in this build's ids.
            var ship = repo.LoadShip("ship_Pilot#built");
            Assert.NotNull(ship);
            Assert.NotEqual(oldHull, ship!.BuiltCells); // the ids really did shift — this is not a no-op
            var cells = ship.BuiltCells.Split(';').Select(c => c.Split(':')).ToArray();
            Assert.Equal(hullKeys.Length, cells.Length);
            for (int i = 0; i < hullKeys.Length; i++)
            {
                Assert.Equal(new[] { i.ToString(), "0", "-1" }, cells[i].Take(3));
                Assert.Equal(hullKeys[i], _content.BlockById(new BlockId(ushort.Parse(cells[i][3])))?.Key);
            }

            // …exactly like the block edit beside it, which the remap has always covered.
            var edit = Assert.Single(repo.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(1, 2, 3));
            Assert.Equal("ship_helm", _content.BlockById(new BlockId(edit.Block))?.Key);

            server.Stop();
        }

        // One copy was taken, and it is the save as it was BEFORE the remap: the old hull, the old palette.
        string backup = Assert.Single(Backups("oldsave", BackupRotation.PreRemapPrefix));
        string backupFile = Path.Combine(Paths("oldsave").BackupsDirectory, backup);
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains(backupFile)); // the log says where it is
        Assert.DoesNotContain(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("become air")); // nothing was dropped
        Assert.Contains(oldHull, ShipJson(backupFile, "ship_Pilot#built"));
        using (var connection = new SqliteConnection($"Data Source={backupFile};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM block_palette WHERE key IN ('bio_lab', 'flora_hybrid');";
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
        }

        // The next start finds the palette current: no second copy.
        using (var repo = new SqliteWorldRepository(Paths("oldsave")))
        {
            var server = NewServer("oldsave", repo);
            server.Start();
            server.Stop();
        }

        Assert.Single(Backups("oldsave", BackupRotation.PreRemapPrefix));
    }

    // ---------------- #2751: hulls an earlier update left in old block ids ----------------

    private static IReadOnlyList<BlocksBeyondTheStars.GameServer.HistoricalBlockSets.Palette> HistoricalSets
        => BlocksBeyondTheStars.GameServer.HistoricalBlockSets.All;

    [Fact]
    public void TheHistoricalBlockSets_AreFrozen_AndEveryKeyIsStillInTheGame()
    {
        // Reconstructed from `git show <tag>:data/blocks.json` (v2026.8.12 … v2026.10.4) when the table was written; these
        // sizes pin it. No block was removed since self-built ships exist, so every set holds the one before it.
        var sets = HistoricalSets;
        Assert.Equal("v2026.8.12", sets[0].Release);
        Assert.Equal(160, sets[0].Count);
        Assert.Equal(new[] { 160, 163, 164, 169, 171, 173, 190, 200, 201, 202, 206, 210, 240, 261, 262, 264 }, sets.Select(s => s.Count));
        Assert.Equal("v2026.10.4", sets[^1].Release);
        for (int i = 0; i < sets.Count; i++)
        {
            var keys = sets[i].Keys.Skip(1).ToList();
            Assert.All(keys, k => Assert.True(_content.Blocks.ContainsKey(k), $"{k} of {sets[i].Release} is no longer in the game"));
            if (i > 0)
            {
                Assert.Superset(new HashSet<string>(sets[i - 1].Keys.Skip(1)), new HashSet<string>(keys));
            }

            // The ids follow the game's rule: the palette a build with exactly these keys assigns.
            var expected = PaletteWithout(_content.Blocks.Keys.Except(keys).ToArray());
            Assert.Equal(expected.Count, sets[i].Count);
            Assert.All(expected, kv => Assert.Equal(kv.Value, sets[i].Keys[kv.Key]));
        }
    }

    /// <summary>A save whose self-built ship was built in v2026.8.12, last opened before v2026.10.4 with v2026.9.17 (the hull
    /// still in 8.12 ids, the palette on record 9.17's), then updated: the remap reads the hull as 9.17 ids, so helm, engine
    /// and door turn into other blocks (a horn, a heal tank, concrete …). Returns the hull keys in cell order. The mix of
    /// blocks matters: with helm, engine, door and walls alone, the pair (v2026.9.4, v2026.9.20) reads the same ids as a
    /// hull with a hinged door — two readings, and such a hull is left alone.</summary>
    private string[] SeedABrokenHull(string world, string shipId, Func<string, int, string>? writtenIn = null, bool plainWalls = false)
    {
        var sets = HistoricalSets;
        var built = sets.First(s => s.Release == "v2026.8.12");
        var readAs = sets.First(s => s.Release == "v2026.9.17");
        var keys = plainWalls
            ? new List<string> { "ship_core", "ship_helm", "ship_engine", "door_slide" }
            : new List<string> { "ship_core", "ship_helm", "ship_engine", "door_slide", "glass", "steel_floor", "cargo_floor", "torch" };
        keys.AddRange(Enumerable.Repeat("steel_wall", 25 - keys.Count));
        string cells = string.Join(";", keys.Select((key, i) => $"{i % 5}:{i / 5}:-1:{(writtenIn?.Invoke(key, i) ?? built.IdOf[key].ToString())}"));
        var palette = new Dictionary<ushort, string>();
        for (ushort id = 1; id < readAs.Keys.Length; id++)
        {
            palette[id] = readAs.Keys[id];
        }

        using var repo = OpenRepo(world);
        repo.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot", FleetShipIds = { shipId } });
        repo.SaveShip("ship_Pilot#" + shipId, new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = cells, Commissioned = true });
        repo.EnsureBlockPalette(palette);
        return keys.ToArray();
    }

    private string[] HullKeys(ShipState ship)
        => ship.BuiltCells.Split(';').Select(c => _content.BlockById(new BlockId(ushort.Parse(c.Split(':')[3])))?.Key ?? "?").ToArray();

    [Fact]
    public void AHullLeftInOldIds_IsRepairedOnce_AndCanLaunchAgain()
    {
        string[] keys = SeedABrokenHull("hullfix", "built");

        string repaired;
        using (var repo = new SqliteWorldRepository(Paths("hullfix")))
        {
            var server = NewServer("hullfix", repo, FromBeforeTerrainGenerations);
            server.Start();
            var ship = repo.LoadShip("ship_Pilot#built")!;
            Assert.Equal(keys, HullKeys(ship)); // every cell names the block it was built as again
            string? problem = server.CustomShipLaunchProblemForTest(ship);
            Assert.False(problem is "@srv.ship.need_helm" or "@srv.ship.need_engine" or "@srv.ship.need_door", problem);
            repaired = ship.BuiltCells;
            server.Stop();
        }

        string backup = Assert.Single(Backups("hullfix", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains("'built'") && l.Contains("v2026.8.12")
                                         && l.Contains("repaired") && l.Contains(backup));

        // The next start finds the save marked: nothing changes, no second copy.
        _log.Lines.Clear();
        using (var repo = new SqliteWorldRepository(Paths("hullfix")))
        {
            var server = NewServer("hullfix", repo, FromBeforeTerrainGenerations);
            server.Start();
            Assert.Equal(repaired, repo.LoadShip("ship_Pilot#built")!.BuiltCells);
            server.Stop();
        }

        Assert.Single(Backups("hullfix", BackupRotation.PreHullRepairPrefix));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("'built'"));
    }

    [Fact]
    public void ASoundHull_IsNeverTouched()
    {
        // A self-built ship in this build's ids with its helm, engine and door: nothing to repair, no copy taken.
        var keys = new List<string> { "ship_helm", "ship_engine", "door_slide" };
        keys.AddRange(Enumerable.Repeat("steel_wall", 22));
        string cells = string.Join(";", keys.Select((key, i) => $"{i % 5}:{i / 5}:-1:{_content.GetBlock(key)!.NumericId.Value}"));
        using (var repo = OpenRepo("hullsound"))
        {
            repo.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot", FleetShipIds = { "sound" } });
            repo.SaveShip("ship_Pilot#sound", new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = cells, Commissioned = true });
            repo.EnsureBlockPalette(_content.BlockPalette());
        }

        using (var repo = new SqliteWorldRepository(Paths("hullsound")))
        {
            var server = NewServer("hullsound", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(cells, repo.LoadShip("ship_Pilot#sound")!.BuiltCells);
            server.Stop();
        }

        Assert.Empty(Backups("hullsound", BackupRotation.PreHullRepairPrefix));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("'sound'"));
    }

    [Fact]
    public void AMixedHull_IsLeftAlone_AndNamedInTheLog()
    {
        // The helm was put in again after the update (this build's ids), the rest still sits in the old ones: no single
        // older version reads the whole hull, so it is not guessed at — it stays as it is, and the log says where the copy is.
        var readAs = HistoricalSets.First(s => s.Release == "v2026.9.17");
        SeedABrokenHull("hullmixed", "mixed", (key, _) => key == "ship_helm"
            ? readAs.IdOf[key].ToString() // the remap turns it into this build's helm
            : HistoricalSets[0].IdOf[key].ToString());

        string before;
        using (var repo = OpenRepo("hullmixed"))
        {
            before = repo.LoadShip("ship_Pilot#mixed")!.BuiltCells;
        }

        using (var repo = new SqliteWorldRepository(Paths("hullmixed")))
        {
            var server = NewServer("hullmixed", repo, CreatedBeforeTheHullRemap);
            server.Start();
            var after = repo.LoadShip("ship_Pilot#mixed")!;
            Assert.Equal(PaletteRemapped(before), after.BuiltCells); // the palette remap only — no repair on top
            server.Stop();
        }

        Assert.Empty(Backups("hullmixed", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("'mixed'") && l.Contains("'Pilot'")
                                         && l.Contains("pre-remap"));
    }

    /// <summary>The update that remapped the save ran before, and its copy is gone (pruned, or the remap ran without the
    /// server): the palette is current, no <c>pre-remap_…</c> copy names the version that misread the hull.</summary>
    private void RemapWithoutACopy(string world)
    {
        using var repo = OpenRepo(world);
        repo.EnsureBlockPalette(_content.BlockPalette());
    }

    [Fact]
    public void AHullLeftInOldIds_IsRepaired_AlsoWithoutTheCopyFromBeforeTheRemap()
    {
        // No copy to pin the version that misread it: the open search over every pair still finds one reading only.
        string[] keys = SeedABrokenHull("hullfix_nocopy", "built");
        RemapWithoutACopy("hullfix_nocopy");

        using (var repo = new SqliteWorldRepository(Paths("hullfix_nocopy")))
        {
            var server = NewServer("hullfix_nocopy", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(keys, HullKeys(repo.LoadShip("ship_Pilot#built")!));
            server.Stop();
        }

        Assert.Empty(Backups("hullfix_nocopy", BackupRotation.PreRemapPrefix));
        Assert.Single(Backups("hullfix_nocopy", BackupRotation.PreHullRepairPrefix));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("block ids — the block set on record"));
    }

    [Fact]
    public void AnAmbiguousHull_IsLeftAlone_AndNamedInTheLog()
    {
        // Helm, engine, a sliding door and plain walls: read as built in v2026.8.12 and opened with v2026.9.17 it is the
        // ship it was — but read as v2026.9.4 / v2026.9.20 it is the same ship with a hinged door. Two readings, and no copy
        // from before the remap to tell which version read it: no guess.
        SeedABrokenHull("hullambiguous", "twofold", plainWalls: true);
        string before;
        using (var repo = OpenRepo("hullambiguous"))
        {
            before = repo.LoadShip("ship_Pilot#twofold")!.BuiltCells;
        }

        RemapWithoutACopy("hullambiguous");
        using (var repo = new SqliteWorldRepository(Paths("hullambiguous")))
        {
            var server = NewServer("hullambiguous", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(PaletteRemapped(before), repo.LoadShip("ship_Pilot#twofold")!.BuiltCells);
            server.Stop();
        }

        Assert.Empty(Backups("hullambiguous", BackupRotation.PreRemapPrefix));
        Assert.Empty(Backups("hullambiguous", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("'twofold'")
                                         && l.Contains("more than one way"));
    }

    [Fact]
    public void AnAmbiguousHull_IsRepaired_WhenTheCopyFromBeforeTheRemapNamesTheVersionThatReadIt()
    {
        // The same two-way hull, but this time the update's remap runs now and takes its pre-remap copy: that copy has the
        // v2026.9.17 palette on record and was written by a build without the hull remap — so the hull was read as 9.17
        // ids, the (9.4, 9.20) reading is out, and one reading is left.
        string[] keys = SeedABrokenHull("hullpinned", "twofold", plainWalls: true);
        string oldHull;
        using (var repo = OpenRepo("hullpinned"))
        {
            oldHull = repo.LoadShip("ship_Pilot#twofold")!.BuiltCells;
        }

        using (var repo = new SqliteWorldRepository(Paths("hullpinned")))
        {
            var server = NewServer("hullpinned", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(keys, HullKeys(repo.LoadShip("ship_Pilot#twofold")!));
            server.Stop();
        }

        Assert.Single(Backups("hullpinned", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains("read as v2026.9.17 block ids"));
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains("'twofold'") && l.Contains("repaired"));

        // The copy was only read: it still holds the save as it was before the remap.
        string copy = Path.Combine(Paths("hullpinned").BackupsDirectory, Assert.Single(Backups("hullpinned", BackupRotation.PreRemapPrefix)));
        Assert.Contains(oldHull, ShipJson(copy, "ship_Pilot#twofold"));
        using var connection = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM block_palette;";
        Assert.Equal((long)HistoricalSets.First(s => s.Release == "v2026.9.17").Count, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void ACopyAHullRemappingVersionWrote_PinsNothing()
    {
        // A pre-remap copy of a save that a v2026.10.4+ build last wrote (save version 2 or higher) says nothing about the
        // version that misread the hull — even when its palette happens to be one of the old sets. The open search runs.
        using (var repo = new SqliteWorldRepository(Paths("hullstamped")))
        {
            var first = NewServer("hullstamped", repo, CreatedBeforeTheHullRemap);
            first.Start(); // a world this build created and stamped …
            first.Stop();
        }

        var readAs = HistoricalSets.First(s => s.Release == "v2026.9.17");
        var built = HistoricalSets[0];
        var keys = new List<string> { "ship_core", "ship_helm", "ship_engine", "door_slide" };
        keys.AddRange(Enumerable.Repeat("steel_wall", 21));
        using (var repo = OpenRepo("hullstamped"))
        {
            var meta = repo.LoadMetadata()!;
            Assert.True(meta.SaveVersion >= 2);
            meta.StampedFeatures.Remove(SvGameServer.HullRepairMarker); // … whose repair has not run yet
            repo.SaveMetadata(meta);
            var palette = new Dictionary<ushort, string>();
            for (ushort id = 1; id < readAs.Keys.Length; id++)
            {
                palette[id] = readAs.Keys[id];
            }

            repo.EnsureBlockPalette(palette); // … and whose palette on record is the 9.17 set
            repo.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot", FleetShipIds = { "twofold" } });
            repo.SaveShip("ship_Pilot#twofold", new ShipState
            {
                ShipType = ShipState.CustomShipType,
                BuiltCells = string.Join(";", keys.Select((key, i) => $"{i % 5}:{i / 5}:-1:{built.IdOf[key]}")),
                Commissioned = true,
            });
        }

        using (var repo = new SqliteWorldRepository(Paths("hullstamped")))
        {
            var server = NewServer("hullstamped", repo, CreatedBeforeTheHullRemap);
            server.Start(); // the remap takes a pre-remap copy — stamped with save version 2+
            server.Stop();
        }

        Assert.Single(Backups("hullstamped", BackupRotation.PreRemapPrefix));
        Assert.Empty(Backups("hullstamped", BackupRotation.PreHullRepairPrefix));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("block ids — the block set on record"));
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("'twofold'")
                                         && l.Contains("more than one way"));
    }

    /// <summary>A world created before the hull remap: terrain generation 20, what v2026.10.4 still created.</summary>
    private static void CreatedBeforeTheHullRemap(ServerConfig c) => c.World.TerrainGeneration = 20;

    /// <summary>A world from before the terrain-generation field (generation 0) — the oldest saves.</summary>
    private static void FromBeforeTerrainGenerations(ServerConfig c) => c.World.TerrainGeneration = 0;

    /// <summary>A self-built ship in THIS build's ids (nothing misread), the save's palette current.</summary>
    private string SeedACurrentHull(string world, string shipId, IReadOnlyList<string> keys, bool commissioned)
    {
        string cells = string.Join(";", keys.Select((key, i) => $"{i % 5}:{i / 5}:0:{_content.GetBlock(key)!.NumericId.Value}"));
        using var repo = OpenRepo(world);
        repo.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot", FleetShipIds = { shipId } });
        repo.SaveShip("ship_Pilot#" + shipId, new ShipState { ShipType = ShipState.CustomShipType, BuiltCells = cells, Commissioned = commissioned });
        repo.EnsureBlockPalette(_content.BlockPalette());
        return cells;
    }

    [Fact]
    public void ARedesignedHull_WithItsHelmMinedOut_IsNotTouched()
    {
        // The review's case: a commissioned ship being rebuilt — read correctly, its helm mined out. The open search finds
        // exactly one "reading" for it (v2026.9.4 → 9.5 turns the core into an engine and the engine into a helm), which
        // would have handed out a second helm and eaten the core. Its core still sits at keel level and it only misses what
        // mining takes out: weak evidence, and without a copy from before the remap no repair.
        var keys = new List<string> { "ship_core", "ship_engine", "door_slide", "metal_panel", "iron_wall", "glass", "light_white", "crate" };
        keys.AddRange(Enumerable.Repeat("metal_panel", 14));
        string cells = SeedACurrentHull("hullredesign", "rebuild", keys, commissioned: true);

        using (var repo = new SqliteWorldRepository(Paths("hullredesign")))
        {
            var server = NewServer("hullredesign", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(cells, repo.LoadShip("ship_Pilot#rebuild")!.BuiltCells);
            Assert.Equal("@srv.ship.need_helm", server.CustomShipLaunchProblemForTest(repo.LoadShip("ship_Pilot#rebuild")!));
            server.Stop();
        }

        Assert.Empty(Backups("hullredesign", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains("'rebuild'") && l.Contains("being rebuilt"));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("'rebuild'") && l.Contains("repaired"));
    }

    [Fact]
    public void AConstructionSiteWhoseCoreWasMined_IsNotTouched()
    {
        // A construction site with its core mined out and no helm yet fails the site's only invariant (a core or a helm).
        // The open search reads the sentry post at keel level as a core and the engine as a helm — one reading, and wrong:
        // a construction site's invariant is too weak to tell that from a misread hull, so it needs the pinned palette.
        var keys = new List<string> { "sentry_post", "ship_engine", "steel_floor", "steel_floor", "steel_floor" };
        string cells = SeedACurrentHull("hullsite", "site", keys, commissioned: false);

        using (var repo = new SqliteWorldRepository(Paths("hullsite")))
        {
            var server = NewServer("hullsite", repo, CreatedBeforeTheHullRemap);
            server.Start();
            Assert.Equal(cells, repo.LoadShip("ship_Pilot#site")!.BuiltCells);
            server.Stop();
        }

        Assert.Empty(Backups("hullsite", BackupRotation.PreHullRepairPrefix));
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains("'site'") && l.Contains("construction site"));
    }

    [Fact]
    public void AWorldCreatedAfterTheHullRemap_NeverRunsThePass()
    {
        // Terrain generation 21 first shipped after v2026.10.4: such a world never held a hull in old ids, so even a hull
        // that looks exactly like a misread one is left alone — and the pass is marked done without looking.
        SeedABrokenHull("hullnew", "built");
        string before;
        using (var repo = OpenRepo("hullnew"))
        {
            before = repo.LoadShip("ship_Pilot#built")!.BuiltCells;
        }

        using (var repo = new SqliteWorldRepository(Paths("hullnew")))
        {
            var server = NewServer("hullnew", repo, c => c.World.TerrainGeneration = SvGameServer.FirstTerrainGenerationAfterHullRemap);
            server.Start();
            Assert.Equal(PaletteRemapped(before), repo.LoadShip("ship_Pilot#built")!.BuiltCells); // the palette remap only
            Assert.Contains(SvGameServer.HullRepairMarker, server.Metadata.StampedFeatures);
            server.Stop();
        }

        Assert.Empty(Backups("hullnew", BackupRotation.PreHullRepairPrefix));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("'built'"));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("block ids — the block set on record")); // no copy was even read
    }

    /// <summary>A hull string as the palette remap (9.17 → this build) rewrites it.</summary>
    private string PaletteRemapped(string cells)
    {
        var readAs = HistoricalSets.First(s => s.Release == "v2026.9.17");
        return string.Join(";", cells.Split(';').Select(c =>
        {
            int colon = c.LastIndexOf(':');
            ushort id = ushort.Parse(c.Substring(colon + 1));
            return c.Substring(0, colon + 1) + _content.GetBlock(readAs.Keys[id])!.NumericId.Value;
        }));
    }

    [Fact]
    public void RemovedBlocks_AreNamedInTheLog_BeforeTheyBecomeAir()
    {
        // A save that knows two blocks this build does not — a block taken out of the game, or a world last saved
        // by a build with more blocks whose save version was not raised. Its version is not higher, so it loads:
        // the cells become air as documented, but not silently, and the copy from before is there.
        var stored = new Dictionary<ushort, string>(_content.BlockPalette());
        ushort free = (ushort)(stored.Keys.Max() + 1);
        stored[free] = "zz_retired_block";
        stored[(ushort)(free + 1)] = "zz_other_retired_block";
        using (var repo = OpenRepo("retired"))
        {
            repo.SetBlock("rocky", new Vector3i(2, 2, 2), free);
            repo.EnsureBlockPalette(stored);
            Assert.Equal(stored.Count, repo.LoadBlockPalette().Count);
        }

        using (var repo = new SqliteWorldRepository(Paths("retired")))
        {
            var server = NewServer("retired", repo);
            server.Start();
            Assert.Equal(BlockId.AirValue, Assert.Single(repo.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(2, 2, 2)).Block);
            Assert.DoesNotContain("zz_retired_block", repo.LoadBlockPalette().Values);
            server.Stop();
        }

        string warning = Assert.Single(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("become air"));
        Assert.Contains("2 block type(s)", warning);
        Assert.Contains("zz_other_retired_block, zz_retired_block", warning);
        Assert.Single(Backups("retired", BackupRotation.PreRemapPrefix));
    }

    [Fact]
    public void FreshWorld_AndUnchangedBlockSet_TakeNoBackup()
    {
        for (int start = 0; start < 2; start++)
        {
            using var repo = new SqliteWorldRepository(Paths("fresh"));
            var server = NewServer("fresh", repo);
            server.Start();
            server.Stop();
        }

        Assert.Empty(Backups("fresh", string.Empty));
    }

    // ---------------- #2223: the save version ----------------

    [Fact]
    public void NewWorld_IsStampedWithTheCurrentSaveVersion()
    {
        using (var repo = new SqliteWorldRepository(Paths("stamp")))
        {
            var server = NewServer("stamp", repo);
            server.Start();
            Assert.Equal(WorldMetadata.CurrentSaveVersion, server.Metadata.SaveVersion);
            server.Stop();
        }

        using var reopened = OpenRepo("stamp");
        Assert.Equal(WorldMetadata.CurrentSaveVersion, reopened.LoadMetadata()!.SaveVersion);
        Assert.True(WorldMetadata.CurrentSaveVersion >= 2); // 1 is every save from before the check
    }

    [Theory]
    [InlineData(true)]  // an old save that says "SaveVersion": 1
    [InlineData(false)] // a save whose metadata carries no version at all
    public void OlderSave_LoadsAsBefore_AndIsStampedCurrent(bool versionWritten)
    {
        using (var repo = OpenRepo("older"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "older", Seed = 31337, DefaultPlanetType = "rocky", SaveVersion = 1 });
            repo.SetBlock("rocky", new Vector3i(4, 5, 6), _content.GetBlock("stone")!.NumericId.Value);
        }

        if (!versionWritten)
        {
            using var connection = new SqliteConnection($"Data Source={Paths("older").DatabaseFile};Pooling=False");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE world_meta SET json = json_remove(json, '$.SaveVersion');";
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        using (var repo = new SqliteWorldRepository(Paths("older")))
        {
            var server = NewServer("older", repo);
            server.Start(); // no refusal
            Assert.Equal(31337, server.Metadata.Seed); // the world itself, not a fresh one
            server.Stop();
        }

        using var reopened = OpenRepo("older");
        Assert.Equal(WorldMetadata.CurrentSaveVersion, reopened.LoadMetadata()!.SaveVersion);
        Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, Assert.Single(reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(4, 5, 6)).Block);
    }

    // ---------------- #2464: the copy before the save-version stamp ----------------

    /// <summary>The save version stored in a backup file (a SQLite copy), read the way the game reads it.</summary>
    private static int StoredSaveVersion(string databaseFile)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(json_extract(json, '$.SaveVersion'), 1) FROM world_meta WHERE id = 0;";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void OlderSave_IsCopiedBeforeItIsStamped_Once()
    {
        // An older save with this build's block set: nothing to remap, but the stamp alone locks the older build
        // out (a fleet or store rollback, a lagging hosted image). The copy is the way back.
        int older = WorldMetadata.CurrentSaveVersion - 1;
        using (var repo = OpenRepo("stampcopy"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "stampcopy", Seed = 31337, DefaultPlanetType = "rocky", SaveVersion = older });
            repo.EnsureBlockPalette(_content.BlockPalette());
        }

        using (var repo = new SqliteWorldRepository(Paths("stampcopy")))
        {
            var server = NewServer("stampcopy", repo);
            server.Start();
            Assert.Equal(WorldMetadata.CurrentSaveVersion, server.Metadata.SaveVersion);
            server.Stop();
        }

        string backup = Assert.Single(Backups("stampcopy", string.Empty));
        Assert.StartsWith(BackupRotation.PreUpgradePrefix, backup);
        string backupFile = Path.Combine(Paths("stampcopy").BackupsDirectory, backup);
        Assert.Equal(older, StoredSaveVersion(backupFile)); // the save as it was before the stamp …
        Assert.Equal(WorldMetadata.CurrentSaveVersion, StoredSaveVersion(Paths("stampcopy").DatabaseFile)); // … the live one stamped
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains(backupFile) && l.Contains($"save version {older}"));

        // The next start finds the save current: no second copy.
        using (var repo = new SqliteWorldRepository(Paths("stampcopy")))
        {
            var server = NewServer("stampcopy", repo);
            server.Start();
            server.Stop();
        }

        Assert.Single(Backups("stampcopy", string.Empty));
    }

    [Fact]
    public void OlderSave_WithARemapDue_TakesOneCopy_NotTwo()
    {
        // The usual update raises the save version AND changes the block set: the copy before the remap is also
        // the copy before the stamp.
        var oldPalette = PaletteWithout("bio_lab", "flora_hybrid");
        using (var repo = OpenRepo("bothcopies"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "bothcopies", Seed = 5, DefaultPlanetType = "rocky", SaveVersion = 1 });
            repo.EnsureBlockPalette(oldPalette);
        }

        using (var repo = new SqliteWorldRepository(Paths("bothcopies")))
        {
            var server = NewServer("bothcopies", repo);
            server.Start();
            server.Stop();
        }

        string backup = Assert.Single(Backups("bothcopies", string.Empty));
        Assert.StartsWith(BackupRotation.PreRemapPrefix, backup);
        Assert.Equal(1, StoredSaveVersion(Path.Combine(Paths("bothcopies").BackupsDirectory, backup)));
    }

    [Fact]
    public void FailedCopyBeforeTheStamp_DoesNotStopTheStart()
    {
        // The stamp drops nothing — a full or read-only backups folder must not make the world unstartable.
        using (var repo = OpenRepo("stampnocopy"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "stampnocopy", Seed = 5, DefaultPlanetType = "rocky", SaveVersion = 1 });
            repo.EnsureBlockPalette(_content.BlockPalette());
        }

        using (var repo = new SqliteWorldRepository(Paths("stampnocopy")))
        {
            var server = NewServer("stampnocopy", repo);
            var at = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
            server.SetBackupTimeForTest(at);
            Directory.CreateDirectory(Path.Combine(Paths("stampnocopy").BackupsDirectory, BackupRotation.Label(BackupRotation.PreUpgradePrefix, at) + ".db" + BackupRotation.TempSuffix));

            server.Start();

            Assert.Equal(WorldMetadata.CurrentSaveVersion, server.Metadata.SaveVersion);
            Assert.Contains(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("opens without it"));
            server.Stop();
        }
    }

    [Fact]
    public void OlderBrowserSave_IsCopiedBeforeItIsStamped_KeepingOne()
    {
        var first = new MemoryWorldRepository(Paths("mem_stamp"));
        first.Initialize();
        first.SaveMetadata(new WorldMetadata { WorldName = "mem_stamp", Seed = 5, DefaultPlanetType = "rocky", SaveVersion = 1 });
        first.EnsureBlockPalette(_content.BlockPalette());
        byte[] blob = first.ExportSnapshotBlob();

        // A copy from an earlier update is still there: the browser keeps one, the newest.
        Directory.CreateDirectory(Paths("mem_stamp").BackupsDirectory);
        string stale = Path.Combine(Paths("mem_stamp").BackupsDirectory, "pre-upgrade_20200101_000000.world.json.gz");
        File.WriteAllText(stale, "older copy");

        var repo = new MemoryWorldRepository(Paths("mem_stamp"));
        repo.ImportSnapshotBlob(blob);
        var server = NewServer("mem_stamp", repo);
        server.Start();

        string backup = Assert.Single(Backups("mem_stamp", BackupRotation.PreUpgradePrefix));
        Assert.EndsWith(".world.json.gz", backup);
        Assert.False(File.Exists(stale));
        Assert.Equal(blob, File.ReadAllBytes(Path.Combine(Paths("mem_stamp").BackupsDirectory, backup))); // the blob as it came in
        server.Stop();
    }

    [Fact]
    public void NewerSave_IsRefused_AndLeftUntouched()
    {
        // A save a newer build wrote: a higher version, and a block this build has never heard of at an id that
        // belongs to another key here. Opening it would remap that block to air for good.
        int newer = WorldMetadata.CurrentSaveVersion + 1;
        var newerPalette = new Dictionary<ushort, string>(_content.BlockPalette());
        ushort stolenId = _content.GetBlock("stone")!.NumericId.Value;
        newerPalette[stolenId] = "block_from_the_future";
        using (var repo = OpenRepo("tomorrow"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "tomorrow", Seed = 7, DefaultPlanetType = "rocky", SaveVersion = newer });
            repo.SetBlock("rocky", new Vector3i(1, 1, 1), stolenId);
            repo.EnsureBlockPalette(newerPalette);
        }

        using (var repo = new SqliteWorldRepository(Paths("tomorrow")))
        {
            var server = NewServer("tomorrow", repo);
            var refusal = Assert.Throws<SaveVersionTooNewException>(server.Start);
            Assert.Equal(newer, refusal.SaveVersion);
            Assert.Equal(WorldMetadata.CurrentSaveVersion, refusal.SupportedVersion);
            Assert.Contains("'tomorrow'", refusal.Message); // names the world …
            Assert.Contains("update", refusal.Message, StringComparison.OrdinalIgnoreCase); // … and what to do
            Assert.Single(_log.Lines, l => l == $"ERROR {SvGameServer.SaveTooNewMarker}: {refusal.Message}"); // logged once, behind the marker
        }

        // Untouched: the version, the palette and the block are what the newer build wrote; no backup was needed.
        using var reopened = OpenRepo("tomorrow");
        Assert.Equal(newer, reopened.LoadMetadata()!.SaveVersion);
        Assert.True(reopened.BlockPaletteNeedsRemap(_content.BlockPalette())); // the palette was not rewritten
        Assert.Equal(stolenId, Assert.Single(reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0))).Block);
        Assert.Empty(Backups("tomorrow", string.Empty));
    }

    [Fact]
    public void NewerSave_IsRefused_InTheBrowserSaveToo()
    {
        var repo = new MemoryWorldRepository(Paths("newer_mem"));
        repo.Initialize();
        repo.SaveMetadata(new WorldMetadata { WorldName = "newer_mem", SaveVersion = WorldMetadata.CurrentSaveVersion + 5 });

        var server = NewServer("newer_mem", repo);
        Assert.Throws<SaveVersionTooNewException>(server.Start);
        Assert.Equal(WorldMetadata.CurrentSaveVersion + 5, repo.LoadMetadata()!.SaveVersion);
    }

    [Fact]
    public void HostStart_AnswersANewerSave_WithItsOwnExitCode_AndOneMarkedLogLine()
    {
        // What the console host does with the refusal — and what a launcher keys on to say "update the game"
        // instead of "the server could not start": the exit code, or the marker in the server's output.
        using (var repo = OpenRepo("host_newer"))
        {
            repo.SaveMetadata(new WorldMetadata { WorldName = "host_newer", Seed = 7, DefaultPlanetType = "rocky", SaveVersion = WorldMetadata.CurrentSaveVersion + 1 });
        }

        using (var repo = new SqliteWorldRepository(Paths("host_newer")))
        {
            Assert.Equal(SvGameServer.SaveTooNewExitCode, NewServer("host_newer", repo).StartForHost());
        }

        Assert.Equal(3, SvGameServer.SaveTooNewExitCode); // launchers key on the number: it must not drift
        Assert.Equal("[fatal] save-too-new", SvGameServer.SaveTooNewMarker);
        string line = Assert.Single(_log.Lines, l => l.Contains("save-too-new"));
        Assert.StartsWith("ERROR " + SvGameServer.SaveTooNewMarker + ": ", line);
        Assert.Contains("'host_newer'", line);
    }

    [Fact]
    public void HostStart_AnswersAWorldItCanOpen_WithZero()
    {
        using var repo = new SqliteWorldRepository(Paths("host_ok"));
        var server = NewServer("host_ok", repo);

        Assert.Equal(0, server.StartForHost());

        Assert.Equal(WorldMetadata.CurrentSaveVersion, server.Metadata.SaveVersion); // it really is up
        Assert.DoesNotContain(_log.Lines, l => l.Contains("save-too-new"));
        server.Stop();
    }

    /// <summary>
    /// The block set each save version stands for (<see cref="GameContent.BlockFingerprint"/>). An older build maps
    /// the blocks it does not know to air for good when it opens a newer save, and only a HIGHER save version stops
    /// it — so a change of the block set needs a new version, and this table is where that is written down.
    /// </summary>
    private static readonly Dictionary<int, string> BlockSetOfSaveVersion = new()
    {
        [2] = "f7f844cfbd21f315", // the bio lab: bio_lab, flora_hybrid
        [3] = "f616ce9eeb51b88d", // Crystal Net 2 (#2251): moving blocks, new devices, the open / unlit twins
        [4] = "07bf8919f5d6d7f1", // the derelict stations' guardian bay (#2635): guardian_spawner
        [5] = "4eda69f2b9ee2343", // the mixed meadows (#2833): twelve wild plants, flora_berrybush … flora_lupine
        [6] = "3d95fd830ec41f4b", // the tree variety (#2850/#2851/#2853): leaf slots, blossom crown, pale bark, saguaro, lantern fruit
        [7] = "3c9be21d2581a4e5", // the sapling kinds (#2977–#2980): conifer, four fruit trees, Fifi plant, giant mahogany
        [8] = "01ca6f8b5ac48409", // the pumpkin (#3005): flora_pumpkin, pumpkin_lantern
    };

    [Fact]
    public void ChangedBlockSet_NeedsANewSaveVersion()
    {
        int version = WorldMetadata.CurrentSaveVersion;
        Assert.True(BlockSetOfSaveVersion.TryGetValue(version, out string? pinned),
            $"WorldMetadata.CurrentSaveVersion is {version}, but BlockSetOfSaveVersion has no row for it — add [{version}] = \"{_content.BlockFingerprint}\".");
        Assert.True(pinned == _content.BlockFingerprint,
            $"The block set changed (fingerprint {pinned} -> {_content.BlockFingerprint}) while the save version is still {version}. " +
            "A build with the old block set opens a save of this one and turns the new blocks to air for good — only a higher save " +
            $"version stops it. Raise WorldMetadata.CurrentSaveVersion to {version + 1}, say why in its doc comment, and add " +
            $"[{version + 1}] = \"{_content.BlockFingerprint}\" to BlockSetOfSaveVersion (leave the rows of released versions as they are).");
        Assert.Equal(BlockSetOfSaveVersion.Keys.Max(), version); // the newest row is the version this build writes
    }

    // ---------------- #2223: rotating backups ----------------

    [Fact]
    public void RotatingBackups_KeepTheNewestN_AndNeverTouchOtherBackups()
    {
        using var repo = new SqliteWorldRepository(Paths("rotate"));
        var server = NewServer("rotate", repo, c => c.BackupKeepCount = 3);
        server.Start();
        string manual = repo.CreateBackup("backup_20260101_000000"); // made by hand (admin UI / tools)
        string preRemap = repo.CreateBackup(BackupRotation.Label(BackupRotation.PreRemapPrefix, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        var start = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        for (int hour = 0; hour < 5; hour++)
        {
            string? written = server.BackupNowForTest(start.AddHours(hour));
            Assert.NotNull(written);
            Assert.True(File.Exists(written));
        }

        Assert.Equal(
            new[] { "auto_20261003_140000.db", "auto_20261003_150000.db", "auto_20261003_160000.db" },
            Backups("rotate", BackupRotation.AutoPrefix));
        Assert.True(File.Exists(manual));
        Assert.True(File.Exists(preRemap));
        Assert.DoesNotContain(Backups("rotate", string.Empty), f => f.EndsWith(BackupRotation.TempSuffix, StringComparison.Ordinal));
        server.Stop();
    }

    [Fact]
    public void Backup_IsAConsistentCopyOfTheSave_TakenWhileTheServerHoldsItOpen()
    {
        using var repo = new SqliteWorldRepository(Paths("copy"));
        var server = NewServer("copy", repo);
        server.Start();
        repo.SetBlock("rocky", new Vector3i(9, 9, 9), _content.GetBlock("stone")!.NumericId.Value);

        // Inside an open write transaction of the tick thread: the copy must neither wait for it nor see it.
        string? path = null;
        repo.RunInTransaction(() =>
        {
            repo.SetBlock("rocky", new Vector3i(8, 8, 8), _content.GetBlock("stone")!.NumericId.Value);
            path = server.BackupNowForTest(new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));
        });

        Assert.NotNull(path);
        using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            // Only the test's own location ("rocky" is no body id): the start world's structures write edits of their own,
            // and which columns they cover moves with the world (from terrain generation 26 its size is seeded, #2691).
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM world_meta), (SELECT COUNT(*) FROM block_edit WHERE planet = 'rocky' AND x = 9), "
                + "(SELECT COUNT(*) FROM block_edit WHERE planet = 'rocky' AND x = 8);";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0)); // the world's metadata
            Assert.Equal(1L, reader.GetInt64(1)); // what was committed
            Assert.Equal(0L, reader.GetInt64(2)); // not what was still in flight
        }

        server.Stop();
    }

    [Fact]
    public void Interval_CountsPlayOnly_AndStartsABackupWhenItIsFull()
    {
        using var repo = new SqliteWorldRepository(Paths("interval"));
        var server = NewServer("interval", repo, c => c.BackupIntervalMinutes = 1);
        server.Start();

        // Time passes, but nobody is online: no play, nothing new to keep.
        server.AdvanceBackupClockForTest(61);
        server.TickForTest(0.1);
        Assert.Null(server.WaitForBackupForTest());
        Assert.Empty(Backups("interval", BackupRotation.AutoPrefix));
        Assert.Equal(0.0, server.Metadata.PlaySecondsSinceBackup);

        // A player is online: the count starts with them, and half an interval is not enough.
        server.AddLocalPlayer("Pilot");
        server.TickForTest(0.1);
        server.AdvanceBackupClockForTest(30);
        server.TickForTest(0.1);
        Assert.Null(server.WaitForBackupForTest());
        Assert.Empty(Backups("interval", BackupRotation.AutoPrefix));
        Assert.InRange(server.Metadata.PlaySecondsSinceBackup, 30.0, 59.0);

        // A full interval of play: one copy, written off the tick thread.
        server.AdvanceBackupClockForTest(31);
        server.TickForTest(0.1);
        string? path = server.WaitForBackupForTest();
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.StartsWith(BackupRotation.AutoPrefix, Path.GetFileName(path));
        Assert.Single(Backups("interval", BackupRotation.AutoPrefix));

        // The count starts over: the very next tick does not write another one.
        server.TickForTest(0.1);
        Assert.Null(server.WaitForBackupForTest());
        Assert.Single(Backups("interval", BackupRotation.AutoPrefix));
        Assert.InRange(server.Metadata.PlaySecondsSinceBackup, 0.0, 29.0);
        server.Stop();
    }

    [Fact]
    public void ShortSessions_AddUp_AcrossServerRuns()
    {
        // The bundled singleplayer host starts one server per play session, and a hosted world stops itself when
        // it is idle. Two sessions of 40 minutes are 80 minutes of play — the hourly backup must not wait for a
        // single session that is long enough.
        using (var repo = new SqliteWorldRepository(Paths("sessions")))
        {
            var server = NewServer("sessions", repo, c => c.BackupIntervalMinutes = 60);
            server.Start();
            server.AddLocalPlayer("Pilot");
            server.TickForTest(0.1);
            server.AdvanceBackupClockForTest(40 * 60);
            server.TickForTest(0.1);
            Assert.Null(server.WaitForBackupForTest());
            server.Stop(); // the count is saved with the world
        }

        Assert.Empty(Backups("sessions", BackupRotation.AutoPrefix));
        using (var reopened = OpenRepo("sessions"))
        {
            Assert.InRange(reopened.LoadMetadata()!.PlaySecondsSinceBackup, 40 * 60, 41 * 60);
        }

        using (var repo = new SqliteWorldRepository(Paths("sessions")))
        {
            var server = NewServer("sessions", repo, c => c.BackupIntervalMinutes = 60);
            server.Start();
            server.AddLocalPlayer("Pilot");
            server.TickForTest(0.1);
            server.AdvanceBackupClockForTest(15 * 60); // 55 minutes of play so far
            server.TickForTest(0.1);
            Assert.Null(server.WaitForBackupForTest());

            server.AdvanceBackupClockForTest(6 * 60); // 61
            server.TickForTest(0.1);
            string? path = server.WaitForBackupForTest();
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
            server.Stop();
        }

        Assert.Single(Backups("sessions", BackupRotation.AutoPrefix));
        using var after = OpenRepo("sessions");
        Assert.InRange(after.LoadMetadata()!.PlaySecondsSinceBackup, 0.0, 60.0); // a new interval began with the copy
    }

    [Fact]
    public void RotatingBackup_IsTakenRightAfterASave_SoItHoldsPlayersAndBlocksOfTheSameMoment()
    {
        using var repo = new SqliteWorldRepository(Paths("aligned"));
        var server = NewServer("aligned", repo, c => c.BackupIntervalMinutes = 1);
        server.Start();
        var pilot = server.AddLocalPlayer("Pilot");
        server.TickForTest(0.1);

        // Since the last save: a block edit (written as it happens) and a change to the player (written only by a save).
        repo.SetBlock("rocky", new Vector3i(7, 7, 7), _content.GetBlock("stone")!.NumericId.Value);
        pilot.State.KnowledgePoints = 4711;
        Assert.True(repo.LoadPlayer(pilot.State.PlayerId)?.KnowledgePoints != 4711); // not saved yet

        server.AdvanceBackupClockForTest(61);
        server.TickForTest(0.1);
        string? path = server.WaitForBackupForTest();
        Assert.NotNull(path);

        // Restore the copy the way an operator would: as the world.db of a save folder.
        var restored = new SaveGamePaths(Path.Combine(_root, "restored"), "aligned");
        Directory.CreateDirectory(restored.WorldDirectory);
        File.Copy(path!, restored.DatabaseFile);
        using (var copy = new SqliteWorldRepository(restored))
        {
            copy.Initialize();
            Assert.Equal(4711, copy.LoadPlayer(pilot.State.PlayerId)!.KnowledgePoints); // the player as of the copy …
            Assert.Single(copy.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(7, 7, 7)); // … with the block
            Assert.Equal(0.0, copy.LoadMetadata()!.PlaySecondsSinceBackup); // and a restored copy starts a fresh interval
        }

        server.Stop();
    }

    // ---------------- #2223: when the copy before a remap cannot be written ----------------

    /// <summary>Makes the copy before a remap fail: a FOLDER sits where the copy's temp file must go.</summary>
    private void BlockPreRemapBackup(SvGameServer server, string world)
    {
        var at = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
        server.SetBackupTimeForTest(at);
        Directory.CreateDirectory(Path.Combine(Paths(world).BackupsDirectory, BackupRotation.Label(BackupRotation.PreRemapPrefix, at) + ".db" + BackupRotation.TempSuffix));
    }

    [Fact]
    public void FailedCopy_StopsARemapThatWouldDropBlocks_AndLeavesTheSaveUntouched()
    {
        // A world that knows a block this build does not (a save version nobody raised), and a backups folder
        // that cannot take the copy: without the copy there is no way back, so the world is not opened.
        var stored = new Dictionary<ushort, string>(_content.BlockPalette());
        ushort free = (ushort)(stored.Keys.Max() + 1);
        stored[free] = "zz_block_of_a_newer_build";
        using (var repo = OpenRepo("nocopy_drop"))
        {
            repo.SetBlock("rocky", new Vector3i(2, 2, 2), free);
            repo.EnsureBlockPalette(stored);
        }

        using (var repo = new SqliteWorldRepository(Paths("nocopy_drop")))
        {
            var server = NewServer("nocopy_drop", repo);
            BlockPreRemapBackup(server, "nocopy_drop");

            var refusal = Assert.Throws<IOException>(server.Start);
            Assert.Contains("'nocopy_drop'", refusal.Message);
            Assert.Contains("zz_block_of_a_newer_build", refusal.Message);
            Assert.Contains("was not changed", refusal.Message);
            Assert.Contains(_log.Lines, l => l == "ERROR " + refusal.Message);
        }

        using var reopened = OpenRepo("nocopy_drop");
        Assert.Contains("zz_block_of_a_newer_build", reopened.LoadBlockPalette().Values); // the palette was not rewritten
        Assert.Equal(free, Assert.Single(reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0))).Block); // nor the block
        Assert.Empty(Backups("nocopy_drop", string.Empty)); // and no half-written copy pretends to be one
    }

    [Fact]
    public void FailedCopy_DoesNotStopARemapThatOnlyMovesIds()
    {
        // The ordinary update: new blocks, nothing removed. A full or read-only backups folder must not keep the
        // server from starting — the remap is one transaction and loses nothing.
        var oldPalette = PaletteWithout("bio_lab", "flora_hybrid");
        using (var repo = OpenRepo("nocopy_move"))
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), IdOf(oldPalette, "ship_helm"));
            repo.EnsureBlockPalette(oldPalette);
        }

        using (var repo = new SqliteWorldRepository(Paths("nocopy_move")))
        {
            var server = NewServer("nocopy_move", repo);
            BlockPreRemapBackup(server, "nocopy_move");

            server.Start();

            var edit = Assert.Single(repo.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(1, 2, 3));
            Assert.Equal("ship_helm", _content.BlockById(new BlockId(edit.Block))?.Key);
            Assert.Contains(_log.Lines, l => l.StartsWith("WARN ", StringComparison.Ordinal) && l.Contains("continues without it"));
            server.Stop();
        }
    }

    [Fact]
    public void IntervalZero_SwitchesRotatingBackupsOff()
    {
        using var repo = new SqliteWorldRepository(Paths("off"));
        var server = NewServer("off", repo, c => c.BackupIntervalMinutes = 0);
        server.Start();
        server.AddLocalPlayer("Pilot");
        server.AdvanceBackupClockForTest(24 * 3600);
        server.TickForTest(0.1);

        Assert.Null(server.WaitForBackupForTest());
        Assert.Empty(Backups("off", string.Empty));
        server.Stop();
    }

    [Fact]
    public void BrowserSave_TakesNoRotatingBackups()
    {
        var repo = new MemoryWorldRepository(Paths("mem"));
        Assert.False(repo.SupportsAutomaticBackups);
        Assert.Null(repo.CreateBackgroundBackup("auto_x"));

        var server = NewServer("mem", repo, c => c.BackupIntervalMinutes = 1);
        server.Start();
        server.AddLocalPlayer("Pilot");
        server.AdvanceBackupClockForTest(3600);
        server.TickForTest(0.1);

        Assert.Null(server.WaitForBackupForTest());
        Assert.Empty(Backups("mem", string.Empty));
        Assert.Equal(0.0, server.Metadata.PlaySecondsSinceBackup); // nothing counts towards a copy that is never taken
        server.Stop();
    }

    [Fact]
    public void BrowserSave_IsCopiedBeforeARemap_AsTheBlobThatWasImported()
    {
        // The browser's save is one blob, and the host overwrites its only copy on the next save. Before the ids
        // in it are rewritten, the blob is put aside as it came in — one copy, in the save's own storage.
        var oldPalette = PaletteWithout("bio_lab", "flora_hybrid");
        var first = new MemoryWorldRepository(Paths("mem_remap"));
        first.Initialize();
        first.SaveMetadata(new WorldMetadata { WorldName = "mem_remap", Seed = 5, DefaultPlanetType = "rocky" });
        first.SetBlock("rocky", new Vector3i(1, 2, 3), IdOf(oldPalette, "ship_helm"));
        first.EnsureBlockPalette(oldPalette);
        byte[] blob = first.ExportSnapshotBlob();

        // A copy from an earlier update is still there: the browser keeps one, the newest.
        string stale = Path.Combine(Paths("mem_remap").BackupsDirectory, "pre-remap_20200101_000000.world.json.gz");
        File.WriteAllText(stale, "older copy");

        var repo = new MemoryWorldRepository(Paths("mem_remap"));
        repo.ImportSnapshotBlob(blob);
        var server = NewServer("mem_remap", repo);
        server.Start();

        string backup = Assert.Single(Backups("mem_remap", BackupRotation.PreRemapPrefix));
        Assert.EndsWith(".world.json.gz", backup);
        Assert.False(File.Exists(stale));
        string backupFile = Path.Combine(Paths("mem_remap").BackupsDirectory, backup);
        Assert.Equal(blob, File.ReadAllBytes(backupFile)); // byte for byte what the host handed in
        Assert.Contains(_log.Lines, l => l.StartsWith("INFO ", StringComparison.Ordinal) && l.Contains(backupFile));

        // The running world is remapped; the copy still reads as the world from before.
        var edit = Assert.Single(repo.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0)), e => e.WorldPosition == new Vector3i(1, 2, 3));
        Assert.Equal("ship_helm", _content.BlockById(new BlockId(edit.Block))?.Key);
        var restored = new MemoryWorldRepository(Paths("mem_restored"));
        restored.ImportSnapshotBlob(File.ReadAllBytes(backupFile));
        Assert.DoesNotContain("bio_lab", restored.LoadBlockPalette().Values);
        Assert.Equal(IdOf(oldPalette, "ship_helm"), Assert.Single(restored.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0))).Block);
        server.Stop();
    }

    [Fact]
    public void Prune_KeepsTheNewestOfItsOwnKind_AndClearsUnfinishedCopies()
    {
        string dir = Path.Combine(_root, "prune");
        Directory.CreateDirectory(dir);
        string[] files =
        {
            "auto_20261001_000000.db", "auto_20261002_000000.db", "auto_20261003_000000.db", "auto_20261004_000000.db",
            "auto_20261005_000000.db.tmp",   // a copy a crash cut short
            "pre-remap_20260101_000000.db",  // another kind
            "backup_20260101_000000.db",     // made by hand
        };
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(dir, file), "x");
        }

        Assert.Equal(2, BackupRotation.Prune(dir, BackupRotation.AutoPrefix, keep: 2));

        Assert.Equal(
            new[] { "auto_20261003_000000.db", "auto_20261004_000000.db", "backup_20260101_000000.db", "pre-remap_20260101_000000.db" },
            Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToArray());
        Assert.Equal(0, BackupRotation.Prune(dir, BackupRotation.AutoPrefix, keep: 2)); // nothing left to do
        Assert.Equal(0, BackupRotation.Prune(Path.Combine(dir, "missing"), BackupRotation.AutoPrefix, keep: 2));
        Assert.Equal("auto_20261003_161500", BackupRotation.Label(BackupRotation.AutoPrefix, new DateTime(2026, 10, 3, 16, 15, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void BackupSettings_ComeFromConfigAndCommandLine_AndStayBounded()
    {
        var config = new ServerConfig();
        Assert.Equal(60, config.BackupIntervalMinutes);
        Assert.Equal(5, config.BackupKeepCount);

        var applied = config.ApplyCommandLine(new[] { "--backup-interval-minutes", "15", "--backup-keep", "7" });
        Assert.Equal(15, config.BackupIntervalMinutes);
        Assert.Equal(7, config.BackupKeepCount);
        Assert.Contains("backup-interval-minutes", applied);
        Assert.Contains("backup-keep", applied);

        config.ApplyCommandLine(new[] { "--backup-interval-minutes", "0" });
        Assert.Equal(0, config.BackupIntervalMinutes); // off

        config.BackupKeepCount = 0;
        Assert.Equal(1, config.BackupKeepCount); // never "keep none"
        config.BackupKeepCount = 100000;
        Assert.Equal(ServerConfig.BackupKeepCountCeiling, config.BackupKeepCount);

        var fromFile = ServerConfig.FromJson("""{ "backupIntervalMinutes": 30, "backupKeepCount": 9 }""");
        Assert.Equal(30, fromFile.BackupIntervalMinutes);
        Assert.Equal(9, fromFile.BackupKeepCount);
    }
}
