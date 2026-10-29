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
