using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Distaff.Audio.Mt32;
using Distaff.Scumm.Resources;
using Distaff.Scumm.Sound;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Tests;

/// <summary>
/// iMuse playing the Roland Upgrade's sounds on an MT-32, against ScummVM's own: the messages it
/// sends, and the sound they make. Tests against the game data, and against the ROMs (see
/// <see cref="Mt32Tests"/>), pass without checking when they are absent.
/// </summary>
public class ImuseTests
{
    /// <summary>
    /// Every sound read as the Roland Upgrade reads it plays the same messages as ScummVM's iMuse
    /// (<c>tools/imuse-reference</c> builds it to play the same sound to a device that writes what
    /// reaches it, timed as ScummVM's MT-32 driver times it): three minutes of each, the driver's
    /// setup of the module first, with the music timer as it changes. The hashes are the SHA-256
    /// of its output, its lines ending in a line feed. Sounds 1 to 32, not Roland data, stop at
    /// their first byte as there; one stands for them all.
    /// </summary>
    [Theory]
    [InlineData(1, "8b0d37e01aba44fd")]
    [InlineData(33, "23d772c0090ceccb")]
    [InlineData(34, "860c2fb77fb63ece")]
    [InlineData(35, "e3d800ab6d6e1107")]
    [InlineData(36, "60d44f646d40eb91")]
    [InlineData(37, "9134ace044e18b59")]
    [InlineData(38, "d742117c3312b54d")]
    [InlineData(39, "42bdefed79cc8f7d")]
    [InlineData(40, "cf698f578977ed43")]
    [InlineData(41, "f1fd1fca7637783e")]
    [InlineData(42, "40bcf7204b859f45")]
    [InlineData(43, "c1728eb549b18655")]
    [InlineData(44, "b2a1a453813a93fa")]
    [InlineData(45, "ddaff8fbcbfaee40")]
    [InlineData(46, "d862a51dae086068")]
    [InlineData(47, "46a8fb7cafe2d6cb")]
    [InlineData(48, "ee520cbae57dec58")]
    [InlineData(49, "ff523e59649bcbe6")]
    [InlineData(50, "b0533cefefdf574e")]
    [InlineData(51, "c3906dcd8cead0fa")]
    [InlineData(52, "488968adf6b65a14")]
    [InlineData(53, "a1779e8471e164bc")]
    [InlineData(54, "434acf553a0a1efc")]
    [InlineData(55, "351dd0859a32ee36")]
    [InlineData(56, "41bdd8e14a196743")]
    [InlineData(57, "ab5999ae102bc3cc")]
    [InlineData(58, "dd795567a5595b25")]
    [InlineData(59, "2d11cad548aed20f")]
    [InlineData(60, "73433f0bd3792c7e")]
    [InlineData(61, "117b4f80a3ae6f56")]
    [InlineData(62, "e4e23056cd7f4bcd")]
    [InlineData(63, "66de4d5afe5cc3bb")]
    [InlineData(64, "3d0df71aeac8a13c")]
    public void A_sound_sends_what_scummvms_imuse_sends(int sound, string referenceHash)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var recorder = new Recorder();
        var player = new PlayerImuse(44100, recorder, "ScummVM 2026.4.0");
        player.SetVolumes(192, 192);
        player.StartSound(sound, PlayerImuse.RoResource(loader.LoadRolandSound(sound)!.Value.Span));

        // the timer at frame 0 and every 128 frames after, the last at the end, as
        // MidiDriver_Emulated calls it at 32 kHz
        const uint Frames = 3 * 60 * 32000;
        int timer = -1;
        while (true)
        {
            recorder.Tick!();
            int now = player.MusicTimer;
            if (now != timer)
            {
                recorder.Log.Append($"{recorder.Frame} T {now}\n");
                timer = now;
            }

            if (recorder.Frame == Frames)
            {
                break;
            }

            recorder.Frame += 128;
        }

        recorder.Log.Append($"{recorder.Frame} E\n");
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(recorder.Log.ToString())))[..16]);
    }

    /// <summary>
    /// The overture (sound 56) sounds sample for sample as ScummVM's: its first 2,640,000 frames at
    /// 44100 Hz, as ScummVM's iMuse sends it to Munt (<c>tools/mt32-reference</c>, the synth
    /// rendered 128 frames at a time as ScummVM's driver renders it) and ScummVM's mixer converts
    /// its rate (<c>rate-reference</c>, from <c>tools/imuse-reference</c>), on the MT-32 the music
    /// was written for and on the CM-32L ScummVM prefers. Munt's pitch timer jitter, <c>rand()</c>,
    /// is 0 in both.
    /// </summary>
    [Theory]
    [InlineData(Mt32Model.Mt32, "861f3b58e205ded4828bee77b0e492db658dce027e6e2a5dc8f67da727187da3")]
    [InlineData(Mt32Model.Cm32l, "7c5c76f9c61d606c23b4ecb54496f59dc70853eca93a9bb467e596d5c16c5773")]
    public void The_overture_sounds_as_scummvms(Mt32Model model, string referenceHash)
    {
        if (Open() is not { } loader || Mt32Tests.RomFolder() is not { } roms || Mt32RomSet.FromDirectory(roms, model) is not { } set)
        {
            return;
        }

        var emulator = new Mt32Emulator(set, 44100) { Random = new ZeroRandom() };
        var player = new PlayerImuse(44100, emulator, "ScummVM 2026.4.0");
        player.SetVolumes(192, 192);
        player.StartSound(56, PlayerImuse.RoResource(loader.LoadRolandSound(56)!.Value.Span));
        var samples = new short[2640000 * 2];
        for (int o = 0; o < samples.Length; o += 2048)
        {
            player.Read(samples.AsSpan(o, Math.Min(2048, samples.Length - o)));
        }

        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan()))));
    }

    /// <summary>
    /// iMuse's state saved while the overture plays is ScummVM's, byte for byte
    /// (<c>imuse-reference ... save &lt;frame&gt;</c>, at save version 124): its eight parts on
    /// the module's programs, 3492 bytes. The hashes are the SHA-256 of the state.
    /// </summary>
    [Theory]
    [InlineData(320000, "c6fdd5592a65b637")]
    [InlineData(1600000, "8bcb97e41978436e")]
    [InlineData(3200000, "82b303273c76f84d")]
    public void A_save_holds_what_scummvms_imuse_saves(uint frame, string referenceHash)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var state = SaveOverture(loader, frame);
        Assert.Equal(3492, state.Length);
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(state))[..16]);
    }

    /// <summary>
    /// A load plays on as ScummVM's does (<c>imuse-reference ... load</c>, the state saved as
    /// above): the volume to 0, the sounds stopped, iMuse paused, the state read and the overture
    /// started again, iMuse going on and the volume set again, then a minute played, every message
    /// and music timer ScummVM's. ScummVM takes the parser back to the saved tick, which for RO is
    /// counted in markers, as if it were the music's own: saved at a music timer of 81, the
    /// overture goes on from 33, and Distaff does the same.
    /// </summary>
    [Theory]
    [InlineData(320000, "5d9bb7c23456b176")]
    [InlineData(1600000, "51318e342385ce44")]
    [InlineData(3200000, "e6ee7f16c88d3ad1")]
    public void A_load_plays_on_as_scummvms_imuse_does(uint frame, string referenceHash)
    {
        if (Open() is not { } loader)
        {
            return;
        }

        var state = SaveOverture(loader, frame);
        var recorder = new Recorder();
        var player = new PlayerImuse(44100, recorder, "ScummVM 2026.4.0");
        player.SetVolumes(192, 192);
        player.BeginLoad();
        player.StopAllSounds();
        player.LoadState(state, 124, n => PlayerImuse.RoResource(loader.LoadRolandSound(n)!.Value.Span));
        Play(player, recorder, 60 * 32000);
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(recorder.Log.ToString())))[..16]);
    }

    /// <summary>
    /// A save at version 108, as Distaff writes by default for ScummVM 2.7, holds the faders and
    /// the instruments as that version had them, and loads (as ScummVM loads it) to play on as
    /// the one at 124 does.
    /// </summary>
    [Fact]
    public void A_save_at_version_108_plays_on_as_one_at_124()
    {
        if (Open() is not { } loader)
        {
            return;
        }

        Func<int, byte[]?> sound = n => PlayerImuse.RoResource(loader.LoadRolandSound(n)!.Value.Span);
        string PlayedOn(byte[] state, int version)
        {
            var recorder = new Recorder();
            var player = new PlayerImuse(44100, recorder, "ScummVM 2026.4.0");
            player.SetVolumes(192, 192);
            player.BeginLoad();
            player.StopAllSounds();
            player.LoadState(state, version, sound);
            Play(player, recorder, 10 * 32000);
            return recorder.Log.ToString();
        }

        var old = SaveOverture(loader, 1600000, 108);
        // a byte less for each player's detune and each of its faders, two for each part's polyphony
        // and volume sensitivity, two for each program's bank and module
        Assert.Equal(3492 - 8 - 8 * 4 - 32 * 2 - 8 * 2, old.Length);
        Assert.Equal(PlayedOn(SaveOverture(loader, 1600000), 124), PlayedOn(old, 108));
    }

    /// <summary>The overture's state, saved before the first tick at or after a frame.</summary>
    private static byte[] SaveOverture(ResourceLoader loader, uint frame, int version = 124)
    {
        var recorder = new Recorder();
        var player = new PlayerImuse(44100, recorder, "ScummVM 2026.4.0");
        player.SetVolumes(192, 192);
        player.StartSound(56, PlayerImuse.RoResource(loader.LoadRolandSound(56)!.Value.Span));
        byte[]? state = null;
        Play(player, recorder, frame + 128, at => state ??= at >= frame ? player.SaveState(version) : null);
        return state!;
    }

    /// <summary>
    /// The timer at frame 0 and every 128 frames after, the last at the end, as MidiDriver_Emulated
    /// calls it at 32 kHz, the music timer written as it changes; <paramref name="beforeTick"/>
    /// sees each frame before its tick.
    /// </summary>
    private static void Play(PlayerImuse player, Recorder recorder, uint frames, Action<uint>? beforeTick = null)
    {
        int timer = -1;
        while (true)
        {
            beforeTick?.Invoke(recorder.Frame);
            recorder.Tick!();
            int now = player.MusicTimer;
            if (now != timer)
            {
                recorder.Log.Append($"{recorder.Frame} T {now}\n");
                timer = now;
            }

            if (recorder.Frame >= frames)
            {
                break;
            }

            recorder.Frame += 128;
        }

        recorder.Log.Append($"{recorder.Frame} E\n");
    }

    /// <summary>A device that writes what reaches it as <c>imuse-reference</c> writes it.</summary>
    private sealed class Recorder : IMidiDevice
    {
        public StringBuilder Log { get; } = new();

        public uint Frame { get; set; }

        public Action? Tick { get; private set; }

        public void Send(uint message) => Log.Append($"{Frame} M {message:x}\n");

        public void SysEx(ReadOnlySpan<byte> message)
        {
            // as MidiDriver_MT32::sysEx: a Roland data set is written to the synth's memory
            if (message[0] == 0xF0)
            {
                Log.Append($"{Frame} S {Convert.ToHexStringLower(message)}\n");
            }
            else if (message[3] is 0x12 or 0x42)
            {
                Log.Append($"{Frame} W {message[1]:x2} {Convert.ToHexStringLower(message[4..^1])}\n");
            }
        }

        public void Render(Span<short> interleavedStereo) => interleavedStereo.Clear();

        public void SetTimer(int ticksPerSecond, Action? tick) => Tick = tick;
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
    }

    private static ResourceLoader? Open()
        => GameFolders.Find(GameFolders.Ega) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
