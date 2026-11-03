using Distaff.Scumm.Detection;
using Distaff.Scumm.Game;
using Distaff.Scumm.Hosting;
using Distaff.Scumm.Inspection;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm;

/// <summary>
/// A Loom release, opened from its files: which one it is, and the ways to play it or look
/// inside it.
/// <code>
/// var game = Loom.Open(folder);        // "Loom (EGA)"
/// var world = game.Play();             // headless: Boot, then Step
/// var inspector = game.Inspect();      // rooms and scripts
/// var engine = game.CreateEngine(host); // on a host: a window's, or one of your own
/// </code>
/// It does not change once opened, so it may be shared between threads; each world and
/// inspector it gives opens resources of its own, and so may go to a thread of its own.
/// </summary>
public sealed class Loom
{
    private readonly ScummGame _found;

    private Loom(IGameContainer files, ScummGame found)
    {
        Files = files;
        _found = found;
        Detected = new DetectedGame("scumm", "loom", "Loom", found.Variant, found.Language, found.Platform);
    }

    /// <summary>The release's files.</summary>
    public IGameContainer Files { get; }

    /// <summary>The release as detection names it.</summary>
    public DetectedGame Detected { get; }

    /// <summary>The release in words: <c>Loom (EGA)</c>, <c>Loom (FM-TOWNS)</c>, <c>Loom (VGA)</c>.</summary>
    public string Description => $"Loom ({Variant})";

    /// <summary>The release's row of ScummVM's detection table: <c>EGA</c>, <c>FM-TOWNS</c> or <c>VGA</c>.</summary>
    public string Variant => _found.Variant;

    /// <summary>The release's SCUMM version: 3, or 4 for the VGA CD.</summary>
    public int Version => _found.Version;

    /// <summary>The release's language, as ScummVM codes it (<c>en</c>), when known.</summary>
    public string? Language => _found.Language;

    /// <summary>The release's platform, as ScummVM names it (<c>dos</c>, <c>fmtowns</c>), when known.</summary>
    public string? Platform => _found.Platform;

    /// <summary>The MD5 of the index file the release is detected by, in hex.</summary>
    public string IndexMd5 => _found.Entry.Md5;

    /// <summary>
    /// The name ScummVM gives the release's target, which its saves are named after:
    /// <c>loom-ega</c>, <c>loom-fm</c>, <c>loom-vga</c>.
    /// </summary>
    public string Target => _found.PreferredTarget;

    /// <summary>
    /// Opens the release in a folder, or in a folder up to two levels below it. Throws
    /// <see cref="DirectoryNotFoundException"/> when there is no such folder,
    /// <see cref="FileNotFoundException"/> when it holds no Loom release, and
    /// <see cref="NotSupportedException"/> for a release Distaff does not play (the message says
    /// which).
    /// </summary>
    public static Loom Open(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"There is no folder {folder}.");
        }

        return Open(new DirectoryGameContainer(folder));
    }

    /// <summary>
    /// Opens the release whose files a container holds: a folder's, or files kept in memory.
    /// Throws as <see cref="Open(string)"/> does, but for the folder.
    /// </summary>
    public static Loom Open(IGameContainer files)
    {
        var found = ScummDetector.Identify(files)
            ?? throw new FileNotFoundException($"There is no Loom release in {files.Name}.");

        // opening the resources refuses a release Distaff does not play
        ResourceLoader.Open(files);
        return new Loom(files, found);
    }

    /// <summary>
    /// A new game, headless: <see cref="GameWorld.Boot"/> it (or <see cref="GameWorld.Load"/> a
    /// save) and <see cref="GameWorld.Step"/> it. Each world has its own resources.
    /// </summary>
    /// <param name="seed">The seed of the scripts' random numbers.</param>
    /// <param name="sound">The EGA's sound card; the other releases have their own sound.</param>
    /// <param name="sampleRate">The rate the sound is made at, in stereo frames a second.</param>
    /// <param name="cdTracks">The CD audio tracks of the FM Towns and the VGA CD, by number; none, and they are silent.</param>
    /// <param name="mt32">The MT-32 the EGA's Roland Upgrade plays on, made for a sample rate, when <paramref name="sound"/> is <see cref="SoundDevice.Mt32"/>.</param>
    public GameWorld Play(int seed = 1, SoundDevice sound = SoundDevice.AdLib, int sampleRate = GameWorld.DefaultSampleRate, Func<int, ICdTrack?>? cdTracks = null, Func<int, IMidiDevice>? mt32 = null)
        => new(ResourceLoader.Open(Files), seed, sound, sampleRate, cdTracks, mt32);

    /// <summary>A look inside the release: its rooms and scripts.</summary>
    public GameInspector Inspect() => new(ResourceLoader.Open(Files));

    /// <summary>The engine that plays the release on a host, a window's or one of your own.</summary>
    public ScummEngine CreateEngine(IHost host) => new(host, Files, Detected);

    /// <summary>The release in words, as <see cref="Description"/>.</summary>
    public override string ToString() => Description;
}
