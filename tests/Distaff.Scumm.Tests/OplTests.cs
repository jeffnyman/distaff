using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Distaff.Scumm.Sound.Opl;

namespace Distaff.Scumm.Tests;

/// <summary>The OPL2 emulator against ScummVM's own (tools/opl-reference builds mame.cpp to play the same script).</summary>
public class OplTests
{
    // SHA-256 of the 16-bit little-endian samples tools/opl-reference wrote for Script at 44100 Hz, noise seed 1
    private const string ReferenceHash = "a73f1d8bad6c0912b87f864a942bcfe9fc687be3c9467fbdd4d347d1384bf0aa";
    private const int ReferenceSamples = 151595;

    // "w <reg> <val>" writes a register, "g <count>" generates samples in one call, "r" resets
    private const string Script = """
        # waveform select on, deep tremolo and vibrato (latched by the first generate)
        w 01 20
        w bd c0
        # ch0: FM, feedback 7, sustained, sine / half sine
        w 20 21
        w 23 21
        w 40 10
        w 43 0c
        w 60 f2
        w 63 f3
        w 80 44
        w 83 45
        w e0 00
        w e3 01
        w c0 0e
        w a0 98
        w b0 31
        # ch1: additive, feedback 2, tremolo and vibrato, KSL 1.5 and 3 dB/oct, abs sine / pulse sine
        w 21 e2
        w 24 61
        w 41 4a
        w 44 8c
        w 61 a5
        w 64 c4
        w 81 27
        w 84 36
        w e1 02
        w e4 03
        w c1 05
        w a1 41
        w b1 2e
        # ch2: FM, percussive envelope, KSR, KSL 6 dB/oct, high block
        w 22 01
        w 25 11
        w 42 c5
        w 45 0a
        w 62 53
        w 65 74
        w 82 1a
        w 85 2b
        w e2 03
        w e5 00
        w c2 0a
        w a2 6b
        w b2 3a
        # ch3: additive, multiple 15, slow attack
        w 28 0f
        w 2b 02
        w 48 20
        w 4b 10
        w 68 88
        w 6b 96
        w 88 f9
        w 8b 5f
        w e8 01
        w eb 02
        w c3 01
        w a3 ff
        w b3 23
        g 1
        g 7
        g 4402
        # change a waveform mid-note, then key ch0 and ch2 off (release)
        w e3 03
        g 2205
        w b0 11
        w b2 1a
        g 6615
        # waveform select off: every slot back to the sine
        w 01 00
        g 2000
        w 01 20
        w e0 02
        w e4 01
        # depth bits off (the emulator keeps the latched tables)
        w bd 00
        g 4410
        # rhythm mode: bass drum (ch6), snare and hi-hat (ch7), tom and cymbal (ch8)
        w 30 01
        w 33 01
        w 50 0b
        w 53 06
        w 70 f8
        w 73 f6
        w 90 47
        w 93 46
        w c6 08
        w a6 57
        w b6 0a
        w 31 01
        w 34 02
        w 51 08
        w 54 08
        w 71 f7
        w 74 f8
        w 91 57
        w 94 48
        w a7 05
        w b7 09
        w 32 05
        w 35 21
        w 52 0a
        w 55 08
        w 72 f6
        w 75 f5
        w 92 68
        w 95 37
        w e5 02
        w a8 30
        w b8 0a
        w bd 3f
        g 8000
        w bd 20
        g 4000
        w bd 2a
        g 3000
        w bd 35
        g 512
        g 2988
        w bd 20
        g 8820
        # vibrato and tremolo on the drums, retrigger
        w 31 c1
        w 35 61
        w bd 3f
        g 11025
        w bd 00
        g 4410
        # melodic voices again on channels 6-8 and a reset in the middle
        w b6 2a
        w b7 29
        w b8 2a
        w b1 0e
        w b3 03
        g 22050
        r
        g 1000
        w 20 21
        w 23 21
        w 60 f0
        w 63 f0
        w 80 43
        w 83 44
        w 43 08
        w a0 44
        w b0 32
        g 22050
        w b0 12
        g 44100
        """;

    /// <summary>Melodic voices, waveforms, KSL, tremolo, vibrato, rhythm mode and releases sound as ScummVM's MAME OPL does, sample for sample.</summary>
    [Fact]
    public void The_mame_opl_matches_scummvm_bit_for_bit()
    {
        var opl = new MameOpl(44100);
        var samples = new List<short>();
        foreach (var line in Script.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ');
            switch (parts[0])
            {
                case "w":
                    opl.WriteRegister(Hex(parts[1]), Hex(parts[2]));
                    break;
                case "g":
                    var buffer = new short[int.Parse(parts[1], CultureInfo.InvariantCulture)];
                    opl.Generate(buffer);
                    samples.AddRange(buffer);
                    break;
                case "r":
                    opl.Reset();
                    break;
            }
        }

        Assert.Equal(ReferenceSamples, samples.Count);
        var bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(samples));
        Assert.True(BitConverter.IsLittleEndian);
        Assert.Equal(ReferenceHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static int Hex(string text) => int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
