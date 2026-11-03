namespace Distaff.Scumm.Sound;

/// <summary>
/// The Creative Music System (Game Blaster): two Philips SAA1099 chips, each six square-wave
/// channels with a volume for each side, two noise generators and two envelope generators, mixed
/// into stereo. The chips are written through their ports, 0x220 and 0x222 taking a value for the
/// first and second chip, 0x221 and 0x223 choosing its register. Mirrors ScummVM's
/// <c>DOSBoxCMS</c> (<c>audio/softsynth/cms.cpp</c>), taken from DOSBox and MAME.
/// </summary>
internal sealed class CmsSynth
{
    private const int Left = 0, Right = 1;

    // the chips' clock, in 1/256ths (MASTER_CLOCK, FRAC_ONE_CMS)
    private const long FracOne = 1 << 8;
    private const long MasterClock = 7159090 * FracOne;

    private static readonly byte[][] Envelopes =
    [
        // zero amplitude
        [.. Enumerable.Repeat((byte)0, 64)],

        // maximum amplitude
        [.. Enumerable.Repeat((byte)15, 64)],

        // single decay
        [.. Ramp(15, -1), .. Enumerable.Repeat((byte)0, 48)],

        // repetitive decay
        [.. Ramp(15, -1), .. Ramp(15, -1), .. Ramp(15, -1), .. Ramp(15, -1)],

        // single triangular
        [.. Ramp(0, 1), .. Ramp(15, -1), .. Enumerable.Repeat((byte)0, 32)],

        // repetitive triangular
        [.. Ramp(0, 1), .. Ramp(15, -1), .. Ramp(0, 1), .. Ramp(15, -1)],

        // single attack
        [.. Ramp(0, 1), .. Enumerable.Repeat((byte)0, 48)],

        // repetitive attack
        [.. Ramp(0, 1), .. Ramp(0, 1), .. Ramp(0, 1), .. Ramp(0, 1)],
    ];

    // the amplitude of each of the 16 levels (amplitude_lookup)
    private static readonly int[] AmplitudeLookup = [.. Enumerable.Range(0, 16).Select(i => i * 32767 / 16)];

    private readonly Chip[] _chips = [new(), new()];
    private readonly int _rate;

    /// <param name="rate">The output's rate (the mixer's, <c>getRate</c>).</param>
    public CmsSynth(int rate) => _rate = rate;

    private static IEnumerable<byte> Ramp(int from, int step) => Enumerable.Range(0, 16).Select(i => (byte)(from + step * i));

    /// <summary>A byte to a port: 0x220 or 0x222 the value for the first or second chip, 0x221 or 0x223 its register. Mirrors <c>write</c>.</summary>
    public void Write(int port, int value)
    {
        switch (port - 0x220)
        {
            case 0:
                PortWrite(0, 0, value);
                break;
            case 1:
                PortWrite(0, 1, value);
                break;
            case 2:
                PortWrite(1, 0, value);
                break;
            case 3:
                PortWrite(1, 1, value);
                break;
        }
    }

    /// <summary>
    /// Interleaved stereo samples, left first: the first chip's into the cleared buffer, the
    /// second's added to them. Mirrors <c>generateSamples</c>.
    /// </summary>
    public void Generate(Span<short> stereo)
    {
        Update(0, stereo);
        Update(1, stereo);
    }

    private void Envelope(int chip, int ch)
    {
        var saa = _chips[chip];
        var channels = saa.Channels;
        if (saa.EnvEnable[ch] != 0)
        {
            int mode = saa.EnvMode[ch];

            // step from 0 to 63, then loop over 32 to 63
            int step = saa.EnvStep[ch] = ((saa.EnvStep[ch] + 1) & 0x3F) | (saa.EnvStep[ch] & 0x20);
            int mask = 15;
            if (saa.EnvBits[ch] != 0)
            {
                // 3 bits of resolution: the lowest is masked
                mask &= ~1;
            }

            int left = Envelopes[mode][step] & mask;
            int right = (saa.EnvReverseRight[ch] & 0x01) != 0 ? (15 - Envelopes[mode][step]) & mask : left;
            for (int i = 0; i < 3; i++)
            {
                channels[ch * 3 + i].Envelope[Left] = left;
                channels[ch * 3 + i].Envelope[Right] = right;
            }
        }
        else
        {
            // the envelope off: every factor 16
            for (int i = 0; i < 3; i++)
            {
                channels[ch * 3 + i].Envelope[Left] = channels[ch * 3 + i].Envelope[Right] = 16;
            }
        }
    }

    private static int Frequency(Channel channel) => (int)(((2 * MasterClock / 512) << channel.Octave) / (511 - channel.Frequency));

    private void Update(int chip, Span<short> buffer)
    {
        var saa = _chips[chip];
        if (chip == 0)
        {
            buffer.Clear();
        }

        // the channels disabled: nothing more
        if (saa.AllChannelsEnable == 0)
        {
            return;
        }

        for (int ch = 0; ch < 2; ch++)
        {
            switch (saa.NoiseParams[ch])
            {
                case 0:
                    saa.Noise[ch].Freq = (int)(MasterClock * 2 / 256);
                    break;
                case 1:
                    saa.Noise[ch].Freq = (int)(MasterClock * 2 / 512);
                    break;
                case 2:
                    saa.Noise[ch].Freq = (int)(MasterClock * 2 / 1024);
                    break;
                case 3:
                    saa.Noise[ch].Freq = saa.Channels[ch * 3].Freq;
                    break;
            }
        }

        int rate = (int)(_rate * FracOne);
        for (int j = 0; j < buffer.Length / 2; j++)
        {
            int outputLeft = 0, outputRight = 0;
            for (int ch = 0; ch < 6; ch++)
            {
                var channel = saa.Channels[ch];
                if (channel.Freq == 0)
                {
                    channel.Freq = Frequency(channel);
                }

                // where the square wave is
                channel.Counter -= channel.Freq;
                while (channel.Counter < 0)
                {
                    // the new frequency once the half wave is done
                    channel.Freq = Frequency(channel);
                    channel.Counter += rate;
                    channel.Level ^= 1;

                    // the envelope counters clocked
                    if (ch == 1 && saa.EnvClock[0] == 0)
                    {
                        Envelope(chip, 0);
                    }

                    if (ch == 4 && saa.EnvClock[1] == 0)
                    {
                        Envelope(chip, 1);
                    }
                }

                // the noise, if on and its generator is high (noise 0 for channels 0 to 2, 1 for 3 to 5), at half amplitude
                if (channel.NoiseEnable != 0 && (saa.Noise[ch / 3].Level & 1) != 0)
                {
                    outputLeft -= channel.Amplitude[Left] * channel.Envelope[Left] / 16 / 2;
                    outputRight -= channel.Amplitude[Right] * channel.Envelope[Right] / 16 / 2;
                }

                // the square wave, if on and high
                if (channel.FreqEnable != 0 && (channel.Level & 1) != 0)
                {
                    outputLeft += channel.Amplitude[Left] * channel.Envelope[Left] / 16;
                    outputRight += channel.Amplitude[Right] * channel.Envelope[Right] / 16;
                }
            }

            for (int ch = 0; ch < 2; ch++)
            {
                // where the noise generator is
                var noise = saa.Noise[ch];
                noise.Counter -= noise.Freq;
                while (noise.Counter < 0)
                {
                    noise.Counter += rate;
                    noise.Level = ((noise.Level & 0x4000) == 0) == ((noise.Level & 0x0040) == 0) ? (noise.Level << 1) | 1 : noise.Level << 1;
                }
            }

            buffer[j * 2] = (short)Math.Clamp(buffer[j * 2] + outputLeft / 6, short.MinValue, short.MaxValue);
            buffer[j * 2 + 1] = (short)Math.Clamp(buffer[j * 2 + 1] + outputRight / 6, short.MinValue, short.MaxValue);
        }
    }

    private void PortWrite(int chip, int offset, int data)
    {
        var saa = _chips[chip];
        if (offset == 1)
        {
            // the address port
            saa.SelectedRegister = data & 0x1F;
            if (saa.SelectedRegister is 0x18 or 0x19)
            {
                // the envelopes clocked
                if (saa.EnvClock[0] != 0)
                {
                    Envelope(chip, 0);
                }

                if (saa.EnvClock[1] != 0)
                {
                    Envelope(chip, 1);
                }
            }

            return;
        }

        int reg = saa.SelectedRegister;
        int ch;
        switch (reg)
        {
            // a channel's amplitudes
            case >= 0x00 and <= 0x05:
                ch = reg & 7;
                saa.Channels[ch].Amplitude[Left] = AmplitudeLookup[data & 0x0F];
                saa.Channels[ch].Amplitude[Right] = AmplitudeLookup[(data >> 4) & 0x0F];
                break;

            // a channel's frequency
            case >= 0x08 and <= 0x0D:
                ch = reg & 7;
                saa.Channels[ch].Frequency = data & 0xFF;
                break;

            // two channels' octaves
            case >= 0x10 and <= 0x12:
                ch = (reg - 0x10) << 1;
                saa.Channels[ch].Octave = data & 0x07;
                saa.Channels[ch + 1].Octave = (data >> 4) & 0x07;
                break;

            // the channels' square waves on
            case 0x14:
                for (int i = 0; i < 6; i++)
                {
                    saa.Channels[i].FreqEnable = data & (1 << i);
                }

                break;

            // the channels' noise on
            case 0x15:
                for (int i = 0; i < 6; i++)
                {
                    saa.Channels[i].NoiseEnable = data & (1 << i);
                }

                break;

            // the noise generators
            case 0x16:
                saa.NoiseParams[0] = data & 0x03;
                saa.NoiseParams[1] = (data >> 4) & 0x03;
                break;

            // the envelope generators, reset
            case 0x18 or 0x19:
                ch = reg - 0x18;
                saa.EnvReverseRight[ch] = data & 0x01;
                saa.EnvMode[ch] = (data >> 1) & 0x07;
                saa.EnvBits[ch] = data & 0x10;
                saa.EnvClock[ch] = data & 0x20;
                saa.EnvEnable[ch] = data & 0x80;
                saa.EnvStep[ch] = 0;
                break;

            // all channels on, and the generators synchronised and reset
            case 0x1C:
                saa.AllChannelsEnable = data & 0x01;
                saa.SyncState = data & 0x02;
                if ((data & 0x02) != 0)
                {
                    foreach (var channel in saa.Channels)
                    {
                        channel.Level = 0;
                        channel.Counter = 0;
                    }
                }

                break;
        }
    }

    /// <summary>A square-wave channel (<c>saa1099_channel</c>).</summary>
    private sealed class Channel
    {
        public int Frequency, FreqEnable, NoiseEnable, Octave;
        public readonly int[] Amplitude = new int[2], Envelope = new int[2];
        public int Counter, Freq, Level;
    }

    /// <summary>A noise generator (<c>saa1099_noise</c>), its level a polynomial shifter.</summary>
    private sealed class NoiseGenerator
    {
        public int Counter, Freq, Level;
    }

    /// <summary>A chip (<c>SAA1099</c>).</summary>
    private sealed class Chip
    {
        public readonly int[] NoiseParams = new int[2], EnvEnable = new int[2], EnvReverseRight = new int[2],
            EnvMode = new int[2], EnvBits = new int[2], EnvClock = new int[2], EnvStep = new int[2];

        public int AllChannelsEnable, SyncState, SelectedRegister;
        public readonly Channel[] Channels = [new(), new(), new(), new(), new(), new()];
        public readonly NoiseGenerator[] Noise = [new(), new()];
    }
}
