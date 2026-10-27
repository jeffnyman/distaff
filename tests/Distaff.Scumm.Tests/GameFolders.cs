namespace Distaff.Scumm.Tests;

/// <summary>
/// Finds a release's game data: in the folder the DISTAFF_GAMES environment variable names, or,
/// walking up from the test binary, in a folder of that name or in a games folder beside it (the
/// repository's own games folder, which git ignores).
/// </summary>
internal static class GameFolders
{
    public const string Ega = "loom-ega";

    public const string Vga = "loom-vga";

    public const string FmTowns = "loom-fmtowns";

    public static string? Find(string folder)
    {
        var fromEnv = Environment.GetEnvironmentVariable("DISTAFF_GAMES");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(Path.Combine(fromEnv, folder)))
        {
            return Path.Combine(fromEnv, folder);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var candidate in new[] { Path.Combine(dir.FullName, folder), Path.Combine(dir.FullName, "games", folder) })
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
