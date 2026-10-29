using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Distaff.Audio.Mt32;

namespace Distaff.Scumm.Tests;

/// <summary>
/// The MT-32 emulator, a port of Munt by way of Daventry. Telling ROMs from other files needs no
/// data; everything else needs ROM dumps, from the folder DISTAFF_MT32_ROMS names or a
/// <c>mt32-roms</c> folder beside the games, and passes without checking when there are none.
/// </summary>
public class Mt32Tests
{
    private const int Rate = 44100;

    [Fact]
    public void Other_files_are_not_roms()
    {
        Assert.Null(Mt32RomImage.Create(new byte[65536]));
        Assert.Null(Mt32RomSet.Select([new byte[65536], new byte[1048576]]));
    }

    /// <summary>The ROMs are told by their contents; the CM-32L is preferred, as ScummVM prefers its files.</summary>
    [Theory]
    [InlineData(Mt32Model.Auto, "CM-32L")]
    [InlineData(Mt32Model.Mt32, "MT-32 Control v1.07")]
    public void A_module_is_picked_from_the_roms(Mt32Model model, string expected)
    {
        if (RomFolder() is not { } folder)
        {
            return;
        }

        Assert.StartsWith(expected, Mt32RomSet.FromDirectory(folder, model)!.Description);
    }

    /// <summary>Dumps of a module's two chips are put together.</summary>
    [Fact]
    public void Split_chips_are_merged()
    {
        if (RomFolder() is not { } folder || !File.Exists(Path.Combine(folder, "mt32_ctrl_1_07_a.rom")) || !File.Exists(Path.Combine(folder, "mt32_pcm_l.rom")))
        {
            return;
        }

        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(folder, name));
        var roms = Mt32RomSet.Select([Read("mt32_ctrl_1_07_a.rom"), Read("mt32_ctrl_1_07_b.rom"), Read("mt32_pcm_l.rom"), Read("mt32_pcm_h.rom")]);
        Assert.Equal(("ctrl_mt32_1_07", "pcm_mt32"), (roms!.Control.Info.ShortName, roms.Pcm.Info.ShortName));
    }

    /// <summary>A note sounds on the first part (MIDI channel 2), and is gone a few seconds after it is let go.</summary>
    [Theory]
    [InlineData(Mt32Model.Cm32l)]
    [InlineData(Mt32Model.Mt32)]
    public void A_note_plays_and_releases(Mt32Model model)
    {
        if (RomFolder() is not { } folder)
        {
            return;
        }

        var emulator = new Mt32Emulator(Mt32RomSet.FromDirectory(folder, model)!, Rate);
        emulator.Send(0xC1);
        emulator.Send(0x91 | (60 << 8) | (100 << 16));
        var buffer = new short[Rate * 2];
        emulator.Render(buffer);
        Assert.True(buffer.Max(s => Math.Abs((int)s)) > 1000, "the note is silent");

        emulator.Send(0x81 | (60 << 8));
        var tail = new short[Rate * 6 * 2];
        emulator.Render(tail);
        Assert.All(tail.Skip(Rate * 4 * 2), s => Assert.InRange(s, -50, 50));
    }

    /// <summary>
    /// The port renders the same samples as Munt itself: <c>tools/mt32-reference</c> builds the
    /// copy of Munt in ScummVM (libmt32emu 2.7.0) into a harness that plays each script in its
    /// <c>scripts</c> folder (10 seconds of notes on every part, program changes, controllers,
    /// pitch bends and Roland data sets, from <c>genscript.py</c>) as ScummVM drives the synth;
    /// the hashes are the SHA-256 of its output. Munt's pitch timer adds <c>rand() &amp; 3</c>, which
    /// the harness and this both take as 0.
    /// </summary>
    [Theory]
    [InlineData("cm32l_ctrl_1_02.rom", "cm32l_pcm.rom", 1, "742be1c163cacc2c77773c041c8606918086357c5590e47214acc3cc5946d268")]
    [InlineData("cm32l_ctrl_1_02.rom", "cm32l_pcm.rom", 2, "901979e04d1754b1e25614d1e8d9ea68f2b31ba0f720b11b1cc283893c645f9b")]
    [InlineData("mt32_ctrl_1_07.rom", "mt32_pcm.rom", 1, "e1b8d1d47d47f7edcb3a161728dc7c360022f88b022fb5374619d3e3ffe128e1")]
    [InlineData("mt32_ctrl_1_07.rom", "mt32_pcm.rom", 2, "b18b69eaa72226fab07c4d531e211bb8178e1b276c2fc012337f6bf656b73372")]
    public void The_port_renders_as_munt(string control, string pcm, int script, string referenceHash)
    {
        if (RomFolder() is not { } folder || !File.Exists(Path.Combine(folder, control)) || !File.Exists(Path.Combine(folder, pcm)))
        {
            return;
        }

        var synth = new Synth { Random = new ZeroRandom() };
        Assert.True(synth.Open(Mt32RomImage.Create(File.ReadAllBytes(Path.Combine(folder, control)))!, Mt32RomImage.Create(File.ReadAllBytes(Path.Combine(folder, pcm)))!));
        synth.SetOutputGain(1.0f);
        synth.SetReverbOutputGain(1.0f);
        synth.MIDIDelayMode = MIDIDelayMode.Immediate;

        var rendered = new List<short>();
        uint at = 0;
        foreach (string line in File.ReadLines(Path.Combine(Scripts(), $"script{script}.txt")))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            uint frame = uint.Parse(parts[0]);
            if (frame > at)
            {
                var buffer = new short[(frame - at) * 2];
                synth.Render(buffer, (int)(frame - at));
                rendered.AddRange(buffer);
                at = frame;
            }

            if (parts[1] == "M")
            {
                synth.PlayMsg(Convert.ToUInt32(parts[2], 16));
            }
            else if (parts[1] == "S")
            {
                synth.PlaySysex(Convert.FromHexString(parts[2]));
            }
            else if (parts[1] == "E")
            {
                break;
            }
        }

        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(rendered)))));
    }

    /// <summary>The ROM dumps: in the folder DISTAFF_MT32_ROMS names, or a <c>mt32-roms</c> folder beside the games.</summary>
    internal static string? RomFolder()
    {
        string? path = Environment.GetEnvironmentVariable("DISTAFF_MT32_ROMS");
        return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : GameFolders.Find("mt32-roms");
    }

    /// <summary>The reference scripts, in <c>tools/mt32-reference/scripts</c> above the test binary.</summary>
    private static string Scripts()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "mt32-reference", "scripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tools/mt32-reference/scripts was not found above the test binary.");
    }

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
    }
}
