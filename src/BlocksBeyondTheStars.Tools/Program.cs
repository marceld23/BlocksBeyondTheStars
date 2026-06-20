using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;

// BlocksBeyondTheStars server tools — a small CLI for hosts and developers: validate content,
// inspect a world, and create backups without starting the full server (§19).

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "validate":
            return ValidateContent(args.Length > 1 ? args[1] : "data");

        case "info":
            RequireArgs(args, 3, "info <savesRoot> <worldName>");
            return WorldInfo(args[1], args[2]);

        case "backup":
            RequireArgs(args, 3, "backup <savesRoot> <worldName>");
            return Backup(args[1], args[2]);

        case "export-pack":
            RequireArgs(args, 4, "export-pack <savesRoot> <worldName> <outFile>");
            return ExportPack(args[1], args[2], args[3]);

        case "import-pack":
            RequireArgs(args, 4, "import-pack <savesRoot> <worldName> <inFile> [dataDir]");
            return ImportPack(args[1], args[2], args[3], args.Length > 4 ? args[4] : "data");

        default:
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("BlocksBeyondTheStars Tools");
    Console.WriteLine("Usage:");
    Console.WriteLine("  BlocksBeyondTheStars.Tools validate [dataDir]          Validate data-driven content definitions");
    Console.WriteLine("  BlocksBeyondTheStars.Tools info <savesRoot> <world>    Show world metadata and stats");
    Console.WriteLine("  BlocksBeyondTheStars.Tools backup <savesRoot> <world>  Create a consistent world backup");
    Console.WriteLine("  BlocksBeyondTheStars.Tools export-pack <savesRoot> <world> <outFile>          Export admin content pack");
    Console.WriteLine("  BlocksBeyondTheStars.Tools import-pack <savesRoot> <world> <inFile> [dataDir]  Import & validate a content pack");
}

static void RequireArgs(string[] args, int count, string usage)
{
    if (args.Length < count)
    {
        throw new ArgumentException($"Usage: {usage}", nameof(args));
    }
}

static int ValidateContent(string dataDir)
{
    var content = ContentLoader.LoadFromDirectory(dataDir); // throws on validation failure
    Console.WriteLine($"OK — content is valid:");
    Console.WriteLine($"  Blocks:       {content.Blocks.Count}");
    Console.WriteLine($"  Items:        {content.Items.Count}");
    Console.WriteLine($"  Recipes:      {content.Recipes.Count}");
    Console.WriteLine($"  Blueprints:   {content.Blueprints.Count}");
    Console.WriteLine($"  Ship modules: {content.ShipModules.Count}");
    Console.WriteLine($"  Planets:      {content.Planets.Count}");
    return 0;
}

static int WorldInfo(string savesRoot, string world)
{
    var paths = new SaveGamePaths(savesRoot, world);
    if (!File.Exists(paths.DatabaseFile))
    {
        Console.Error.WriteLine($"No world database at {paths.DatabaseFile}");
        return 1;
    }

    using var repo = new SqliteWorldRepository(paths);
    repo.Initialize();
    var meta = repo.LoadMetadata();
    Console.WriteLine($"World:        {meta?.WorldName ?? "(unknown)"}");
    Console.WriteLine($"Seed:         {meta?.Seed}");
    Console.WriteLine($"Start planet: {meta?.DefaultPlanetType}");
    Console.WriteLine($"Save version: {meta?.SaveVersion}");
    Console.WriteLine($"Players:      {repo.ListPlayerIds().Count}");
    Console.WriteLine($"DB size:      {new FileInfo(paths.DatabaseFile).Length / 1024.0:0.0} KiB");
    return 0;
}

static int Backup(string savesRoot, string world)
{
    var paths = new SaveGamePaths(savesRoot, world);
    if (!File.Exists(paths.DatabaseFile))
    {
        Console.Error.WriteLine($"No world database at {paths.DatabaseFile}");
        return 1;
    }

    using var repo = new SqliteWorldRepository(paths);
    repo.Initialize();
    var path = repo.CreateBackup("backup_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
    Console.WriteLine($"Backup created: {path}");
    return 0;
}

static int ExportPack(string savesRoot, string world, string outFile)
{
    using var repo = new SqliteWorldRepository(new SaveGamePaths(savesRoot, world));
    repo.Initialize();
    var pack = new BlocksBeyondTheStars.Shared.Missions.ContentPack { Name = world + "-content", Missions = repo.ListMissions().ToList() };
    File.WriteAllText(outFile, System.Text.Json.JsonSerializer.Serialize(pack, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Exported {pack.Missions.Count} missions to {outFile}");
    return 0;
}

static int ImportPack(string savesRoot, string world, string inFile, string dataDir)
{
    if (!File.Exists(inFile))
    {
        Console.Error.WriteLine($"File not found: {inFile}");
        return 1;
    }

    var content = ContentLoader.LoadFromDirectory(dataDir);
    var pack = System.Text.Json.JsonSerializer.Deserialize<BlocksBeyondTheStars.Shared.Missions.ContentPack>(File.ReadAllText(inFile));
    if (pack is null)
    {
        Console.Error.WriteLine("Empty or invalid content pack.");
        return 1;
    }

    using var repo = new SqliteWorldRepository(new SaveGamePaths(savesRoot, world));
    repo.Initialize();
    int imported = 0, rejected = 0;
    foreach (var mission in pack.Missions)
    {
        var problems = BlocksBeyondTheStars.Shared.Missions.MissionValidator.Validate(mission, content);
        if (problems.Count > 0)
        {
            rejected++;
            Console.Error.WriteLine($"Rejected '{mission.Id}': {string.Join("; ", problems)}");
            continue;
        }

        repo.SaveMission(mission);
        imported++;
    }

    Console.WriteLine($"Imported {imported}, rejected {rejected}.");
    return rejected == 0 ? 0 : 2;
}
