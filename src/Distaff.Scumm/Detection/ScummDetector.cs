using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Detection;

/// <summary>
/// How a release's resource files are laid out, which follows from the engine class ScummVM's
/// <c>metaengine.cpp</c> creates for it.
/// </summary>
internal enum ResourceLayout
{
    /// <summary>
    /// <c>ScummEngine_v3old</c>: 16 colours, 4-byte resource headers (<c>GF_OLD_BUNDLE</c>), the
    /// index and rooms in XOR-0xFF encoded NN.LFL files. Loom EGA.
    /// </summary>
    V3Old,

    /// <summary>
    /// <c>ScummEngine_v3</c> with <c>GF_OLD256</c>: 256 colours, 6-byte resource headers
    /// (<c>GF_SMALL_HEADER</c>), NN.LFL files that are not encoded (no <c>GF_USE_KEY</c>). Loom
    /// FM Towns and PC Engine.
    /// </summary>
    V3Old256,

    /// <summary>
    /// <c>ScummEngine_v4</c>: 256 colours, 6-byte resource headers, the index in 000.LFL and the
    /// rooms in a DISK01.LEC bundle encoded with XOR 0x69. Loom VGA CD.
    /// </summary>
    V4,
}

/// <summary>A Loom release identified in a game folder.</summary>
/// <param name="Entry">The matching row of ScummVM's MD5 table.</param>
/// <param name="Version">The SCUMM version: 3, or 4 for the VGA CD release.</param>
/// <param name="Layout">How the resource files are laid out.</param>
/// <param name="IsDemo">True for the demo releases.</param>
/// <param name="Tag">The tag ScummVM names the release's target with, or null (<c>preferredTag</c>).</param>
internal sealed record ScummGame(Md5Entry Entry, int Version, ResourceLayout Layout, bool IsDemo, string? Tag = null)
{
    // ScummVM's short names for the platforms Loom came out on (getPlatformAbbrev)
    private static readonly Dictionary<string, string> PlatformAbbreviations = new()
    {
        ["amiga"] = "amiga", ["atari"] = "st", ["fmtowns"] = "fm", ["mac"] = "mac", ["macintosh"] = "mac", ["pce"] = "pce", ["windows"] = "win",
    };

    public string Variant => Entry.Variant;

    /// <summary>
    /// The name ScummVM gives the release's target, which its saves are named after: the game,
    /// the release's tag, "demo" for a demo, the platform unless DOS, the language unless English
    /// or unknown; "loom-ega" for the EGA release, "loom-vga" for the VGA CD. Mirrors
    /// <c>generatePreferredTarget</c>.
    /// </summary>
    public string PreferredTarget
    {
        get
        {
            string target = Entry.GameId;
            if (Tag is not null)
            {
                target += "-" + Tag;
            }

            if (IsDemo)
            {
                target += "-demo";
            }

            if (Platform is { } platform && platform != "dos" && PlatformAbbreviations.TryGetValue(platform, out var abbreviation))
            {
                target += "-" + abbreviation;
            }

            if (Language is { } language && language != "en")
            {
                target += "-" + language;
            }

            return target;
        }
    }

    public string? Language => Entry.Language;

    public string? Platform => Entry.Platform;
}

/// <summary>
/// Identifies Loom releases the way ScummVM's <c>detection_internal.h</c> does: by the MD5 of the
/// first megabyte of the room-0 index file, 00.LFL for the version 3 releases and 000.LFL for
/// the VGA CD. The Steam releases, detected by their executable, are not identified.
/// </summary>
internal static class ScummDetector
{
    /// <summary>ScummVM hashes at most this much of a detection file (<c>kMD5FileSizeLimit</c>).</summary>
    private const int Md5FileSizeLimit = 1024 * 1024;

    private static readonly string[] IndexFiles = ["00.LFL", "000.LFL"];

    /// <summary>
    /// The SCUMM version, layout, demo flag and target tag of each Loom row of <c>detection_tables.h</c>.
    /// </summary>
    private static readonly Dictionary<string, (int Version, ResourceLayout Layout, bool Demo, string? Tag)> Variants = new()
    {
        ["EGA"] = (3, ResourceLayout.V3Old, false, "ega"),
        ["No AdLib"] = (3, ResourceLayout.V3Old, false, "ega"),
        ["Mac"] = (3, ResourceLayout.V3Old, false, "ega"),
        ["Demo"] = (3, ResourceLayout.V3Old, true, "ega"),
        ["FM-TOWNS"] = (3, ResourceLayout.V3Old256, false, null),
        ["PC-Engine"] = (3, ResourceLayout.V3Old256, false, null),
        ["VGA"] = (4, ResourceLayout.V4, false, "vga"),
    };

    /// <summary>The MD5 of the index file ScummVM detects a release by, as the bytes ScummVM keeps in its saves (<c>_gameMD5</c>).</summary>
    public static byte[] IndexMd5(IGameContainer container)
        => ReadIndexFile(container) is { } buffer ? Md5.Compute(buffer) : new byte[16];

    public static ScummGame? Identify(IGameContainer container)
    {
        if (ReadIndexFile(container) is not { } buffer)
        {
            return null;
        }

        if (ScummMd5Table.Find(Md5.ComputeHex(buffer)) is { } entry && Variants.TryGetValue(entry.Variant, out var variant))
        {
            return new ScummGame(entry, variant.Version, variant.Layout, variant.Demo, variant.Tag);
        }

        return null;
    }

    private static byte[]? ReadIndexFile(IGameContainer container)
    {
        if (container.FirstExisting(IndexFiles) is not { } name)
        {
            return null;
        }

        using var stream = container.Open(name);
        var buffer = new byte[Math.Min(stream.Length, Md5FileSizeLimit)];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
