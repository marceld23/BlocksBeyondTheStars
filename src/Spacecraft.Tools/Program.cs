using Spacecraft.Persistence;
using Spacecraft.Shared.Content;

// Spacecraft server tools — a small CLI for hosts and developers: validate content,
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
    Console.WriteLine("Spacecraft Tools");
    Console.WriteLine("Usage:");
    Console.WriteLine("  spacecraft-tools validate [dataDir]          Validate data-driven content definitions");
    Console.WriteLine("  spacecraft-tools info <savesRoot> <world>    Show world metadata and stats");
    Console.WriteLine("  spacecraft-tools backup <savesRoot> <world>  Create a consistent world backup");
}

static void RequireArgs(string[] args, int count, string usage)
{
    if (args.Length < count)
    {
        throw new ArgumentException($"Usage: {usage}");
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
