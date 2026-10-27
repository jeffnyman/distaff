namespace Distaff.Scumm.Detection;

/// <summary>One row of ScummVM's <c>scumm-md5.h</c>.</summary>
/// <param name="Md5">MD5 of the first megabyte of the detection file (00.LFL or 000.LFL).</param>
/// <param name="GameId">ScummVM's game id, always "loom" here.</param>
/// <param name="Variant">The row of <c>detection_tables.h</c> the release belongs to, such as "EGA" or "FM-TOWNS".</param>
/// <param name="Extra">Version notes shown to the player.</param>
/// <param name="Size">Size of the detection file, or -1 when ScummVM does not check it.</param>
/// <param name="Language">ISO language code, or null when the table does not say.</param>
/// <param name="Platform">Original platform, or null when the table does not say.</param>
public sealed record Md5Entry(
    string Md5,
    string GameId,
    string Variant,
    string Extra,
    long Size,
    string? Language,
    string? Platform);

internal static partial class ScummMd5Table
{
    // built on first use: field initialisers in two parts of a partial class run in no defined order
    private static Dictionary<string, Md5Entry>? _byMd5;

    public static Md5Entry? Find(string md5) => (_byMd5 ??= Entries.ToDictionary(e => e.Md5)).GetValueOrDefault(md5);
}
