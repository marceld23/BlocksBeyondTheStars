namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Resolves the portable savegame directory layout from the technical requirements (§10.3):
/// <code>
/// blocks-beyond-the-stars-server/
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

    /// <summary>Plain-JSON sidecar next to <see cref="DatabaseFile"/> mirroring a few headline save stats
    /// (name, playtime, last-played). Lets the menu world-picker show metadata without opening the SQLite
    /// DB — the Unity client has no SQLite library, and the picker runs before any server is launched.</summary>
    public string MetaSidecarFile { get; }

    public SaveGamePaths(string savesRoot, string worldName)
    {
        WorldDirectory = Path.Combine(savesRoot, Sanitize(worldName));
        DatabaseFile = Path.Combine(WorldDirectory, "world.db");
        BackupsDirectory = Path.Combine(WorldDirectory, "backups");
        LogsDirectory = Path.Combine(WorldDirectory, "logs");
        MetaSidecarFile = Path.Combine(WorldDirectory, "world.meta.json");
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
