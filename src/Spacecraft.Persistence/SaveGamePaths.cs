namespace Spacecraft.Persistence;

/// <summary>
/// Resolves the portable savegame directory layout from the technical requirements (§10.3):
/// <code>
/// spacecraft-server/
///   saves/&lt;worldName&gt;/
///     world.db
///     backups/
///     logs/
/// </code>
/// </summary>
public sealed class SaveGamePaths
{
    public string WorldDirectory { get; }
    public string DatabaseFile { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }

    public SaveGamePaths(string savesRoot, string worldName)
    {
        WorldDirectory = Path.Combine(savesRoot, Sanitize(worldName));
        DatabaseFile = Path.Combine(WorldDirectory, "world.db");
        BackupsDirectory = Path.Combine(WorldDirectory, "backups");
        LogsDirectory = Path.Combine(WorldDirectory, "logs");
    }

    /// <summary>Creates the directory structure if it does not yet exist.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(WorldDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "world" : name;
    }
}
