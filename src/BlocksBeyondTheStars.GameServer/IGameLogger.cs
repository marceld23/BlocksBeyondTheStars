namespace BlocksBeyondTheStars.GameServer;

/// <summary>Minimal logging abstraction so the server has no heavy logging dependency.</summary>
public interface IGameLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>Writes timestamped log lines to the console (and optionally a log file).</summary>
public sealed class ConsoleGameLogger : IGameLogger
{
    private readonly object _gate = new();
    private readonly TextWriter? _file;

    public ConsoleGameLogger(string? logFilePath = null)
    {
        if (!string.IsNullOrEmpty(logFilePath))
        {
            var dir = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _file = new StreamWriter(File.Open(logFilePath!, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
            };
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (_gate)
        {
            Console.WriteLine(line);
            _file?.WriteLine(line);
        }
    }
}

/// <summary>A logger that discards everything — handy for tests.</summary>
public sealed class NullGameLogger : IGameLogger
{
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
