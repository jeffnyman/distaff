using Distaff.Scumm.Sound;

namespace Distaff.Audio;

/// <summary>
/// Finds a CD track's file in the game's folder as ScummVM does: <c>track1</c>, <c>track01</c>,
/// <c>track_1</c> and <c>track_01</c> in turn (<c>fillPotentialTrackNames</c>), each with the
/// extensions it can decode (<c>openStreamFile</c>), names in any case.
/// </summary>
public static class CdTrackFiles
{
    private static readonly (string Extension, Func<Stream, ICdTrack> Open)[] Decoders =
    [
        (".flac", s => new FlacTrack(s)),
        (".fla", s => new FlacTrack(s)),
        (".ogg", s => new VorbisTrack(s)),
    ];

    /// <summary>Opens tracks from <paramref name="folder"/>, null for one that is not there.</summary>
    public static Func<int, ICdTrack?> In(string folder) => track => Open(folder, track);

    /// <summary>The file a track is played from, or null.</summary>
    public static string? Find(string folder, int track)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            files.TryAdd(Path.GetFileName(path), path);
        }

        foreach (string name in new[] { $"track{track}", $"track{track:00}", $"track_{track}", $"track_{track:00}" })
        {
            foreach (var (extension, _) in Decoders)
            {
                if (files.TryGetValue(name + extension, out var path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    public static ICdTrack? Open(string folder, int track)
    {
        if (Find(folder, track) is not { } path)
        {
            return null;
        }

        var open = Decoders.First(d => path.EndsWith(d.Extension, StringComparison.OrdinalIgnoreCase)).Open;
        return open(File.OpenRead(path));
    }
}
