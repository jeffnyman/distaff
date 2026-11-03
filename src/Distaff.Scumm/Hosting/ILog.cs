namespace Distaff.Scumm.Hosting;

/// <summary>How much a log line matters.</summary>
public enum LogLevel
{
    /// <summary>Detail for following what the engine does.</summary>
    Debug = 0,
    /// <summary>Ordinary progress.</summary>
    Info = 1,
    /// <summary>Something unexpected that the engine carried on past.</summary>
    Warning = 2,
    /// <summary>Something that failed.</summary>
    Error = 3,
}

/// <summary>Where the engine writes its log lines.</summary>
public interface ILog
{
    /// <summary>Writes a line at a level, under a category naming the part of the engine it comes from.</summary>
    void Write(LogLevel level, string category, string message);
}

/// <summary>Writes a line at each level without naming the level.</summary>
public static class LogExtensions
{
    /// <summary>Writes a <see cref="LogLevel.Debug"/> line.</summary>
    public static void Debug(this ILog log, string category, string message) => log.Write(LogLevel.Debug, category, message);

    /// <summary>Writes a <see cref="LogLevel.Info"/> line.</summary>
    public static void Info(this ILog log, string category, string message) => log.Write(LogLevel.Info, category, message);

    /// <summary>Writes a <see cref="LogLevel.Warning"/> line.</summary>
    public static void Warning(this ILog log, string category, string message) => log.Write(LogLevel.Warning, category, message);

    /// <summary>Writes a <see cref="LogLevel.Error"/> line.</summary>
    public static void Error(this ILog log, string category, string message) => log.Write(LogLevel.Error, category, message);
}

/// <summary>Writes log lines to a <see cref="TextWriter"/>, filtered by a minimum level.</summary>
public sealed class TextWriterLog(TextWriter writer, LogLevel minimum = LogLevel.Info) : ILog
{
    /// <inheritdoc />
    public void Write(LogLevel level, string category, string message)
    {
        if (level < minimum)
        {
            return;
        }

        writer.WriteLine($"[{level}] {category}: {message}");
    }
}

/// <summary>A log that keeps nothing.</summary>
public sealed class NullLog : ILog
{
    /// <summary>The one instance there needs to be.</summary>
    public static readonly NullLog Instance = new();

    /// <inheritdoc />
    public void Write(LogLevel level, string category, string message)
    {
    }
}
