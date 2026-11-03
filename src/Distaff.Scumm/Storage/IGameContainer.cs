namespace Distaff.Scumm.Storage;

/// <summary>
/// Read-only access to a game's data files. Names are matched case-insensitively because the
/// games were written for DOS. Implementations may be a directory, a zip archive, or a browser upload.
/// </summary>
public interface IGameContainer
{
    /// <summary>Human readable name for the container, typically the folder name.</summary>
    string Name { get; }

    /// <summary>The names of the files the container holds.</summary>
    IEnumerable<string> EnumerateFiles();

    /// <summary>Whether a file of that name is there.</summary>
    bool Exists(string name);

    /// <summary>A file's length in bytes. Throws <see cref="FileNotFoundException"/> if absent.</summary>
    long GetLength(string name);

    /// <summary>Opens a seekable read-only stream. Throws <see cref="FileNotFoundException"/> if absent.</summary>
    Stream Open(string name);
}

internal static class GameContainerExtensions
{
    public static byte[] ReadAllBytes(this IGameContainer container, string name)
    {
        using var stream = container.Open(name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Returns the first name that exists from a list of candidates, or null.</summary>
    public static string? FirstExisting(this IGameContainer container, params string[] names)
        => names.FirstOrDefault(container.Exists);
}
