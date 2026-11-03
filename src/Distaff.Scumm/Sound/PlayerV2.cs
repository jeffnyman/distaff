using System.Buffers.Binary;

namespace Distaff.Scumm.Sound;

/// <summary>
/// The PC speaker and PCjr music and sound player of versions 2 to 4: four channels, each running
/// a little program of notes, pauses, loops and parameter changes, ticked 236 times a second; a
/// note's volume follows a hull and its pitch a modulation curve. The PC speaker plays the first
/// channel that sounds; the PCjr plays three voices and noise. Version 3 keeps its own music
/// timer. Mirrors ScummVM's <c>Player_V2Base</c> and <c>Player_V2</c>. Ported by way of Maniacal.
/// </summary>
/// <remarks>
/// A sound resource starts with its 4-byte header, then its priority and whether it may be
/// resumed, then the start of each channel's program: one set for the speaker, one for the PCjr.
/// </remarks>
internal class PlayerV2 : SpeakerSynth
{
    // the 16-bit fields of channel_data, by index
    private const int TimeLeft = 0, NextCommand = 1, BaseFreq = 2, FreqDelta = 3, Freq = 4, Volume = 5,
        VolumeDelta = 6, Tempo = 7, InterNotePause = 8, Transpose = 9, NoteLength = 10, HullCurve = 11,
        HullOffset = 12, HullCounter = 13, FreqmodTable = 14, FreqmodOffset = 15, FreqmodIncrement = 16,
        FreqmodMultiplier = 17, FreqmodModulo = 18, MusicTimerField = 23, MusicScriptNumber = 24, FieldCount = 25;

    // the sequencer's rate (FREQ_HZ); the header of old-bundle resources (_header_len)
    private const int TicksPerSecond = 236, HeaderLength = 4;

    // a channel number past these is mapped to a spare fifth channel, which never sounds
    private readonly ushort[][] _channels = [new ushort[FieldCount], new ushort[FieldCount], new ushort[FieldCount], new ushort[FieldCount], new ushort[FieldCount]];
    private readonly ushort[] _frequencies;
    private int _returnAddress;

    // version 3's music timer: counted up every so many ticks once a sound sets its tempo (_music_timer, _music_timer_ctr, _ticks_per_music_timer)
    private int _musicTimer, _musicTimerCounter, _ticksPerMusicTimer = 65535;

    public PlayerV2(int sampleRate, bool pcjr)
        : base(sampleRate, pcjr, TicksPerSecond)
    {
        _frequencies = (pcjr ? SoundTables.PcjrFrequencies : SoundTables.SpeakerFrequencies).ToArray();
    }

    /// <summary>Whether a channel's program runs, and the frequency and volume it sets (<c>time_left</c>, <c>freq</c> and <c>volume</c>).</summary>
    protected (bool Running, ushort Freq, ushort Volume) ChannelState(int channel)
        => (_channels[channel][TimeLeft] != 0, _channels[channel][Freq], _channels[channel][Volume]);

    /// <summary>Version 3's own music timer. Mirrors <c>Player_V2Base::getMusicTimer</c> for <c>_isV3Game</c>.</summary>
    public override int MusicTimer
    {
        get
        {
            lock (Sync)
            {
                return _musicTimer;
            }
        }
    }

    /// <summary>
    /// A sound of at least the playing one's priority starts at once; the one it interrupts, if it
    /// may be resumed, waits to play after it, as does a new sound that cannot start yet. Mirrors
    /// <c>Player_V2::startSound</c>.
    /// </summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (Sync)
        {
            int currentPriority = CurrentData?[HeaderLength] ?? 0;
            int priority = data[HeaderLength];
            int nextPriority = NextData?[HeaderLength] ?? 0;
            bool restartable = data[HeaderLength + 1] != 0;
            byte[]? waiting = data;

            if (CurrentNumber == 0 || currentPriority <= priority)
            {
                (int interrupted, int interruptedPriority, byte[]? interruptedData) = (CurrentNumber, currentPriority, CurrentData);
                ChainSound(number, data);
                (number, priority, waiting) = (interrupted, interruptedPriority, interruptedData);
                restartable = waiting is not null && waiting[HeaderLength + 1] != 0;
            }

            if (CurrentNumber == 0)
            {
                number = 0;
                NextNumber = 0;
                NextData = null;
            }

            if (number != CurrentNumber && restartable && (NextNumber == 0 || nextPriority <= priority))
            {
                NextNumber = number;
                NextData = waiting;
            }
        }
    }

    public override void StopSound(int number)
    {
        lock (Sync)
        {
            if (NextNumber == number)
            {
                NextNumber = 0;
                NextData = null;
            }

            if (CurrentNumber == number)
            {
                ClearChannels();
                CurrentNumber = 0;
                CurrentData = null;
                ChainNextSound();
            }
        }
    }

    public override void StopAllSounds()
    {
        lock (Sync)
        {
            ClearChannels();
            NextNumber = CurrentNumber = 0;
            NextData = CurrentData = null;
        }
    }

    /// <summary>Each channel points at its program in the sound. Mirrors <c>Player_V2Base::chainSound</c>.</summary>
    protected override void ChainSound(int number, byte[]? data)
    {
        int offset = HeaderLength + (Pcjr ? 10 : 2);
        CurrentNumber = number;
        CurrentData = data;
        for (int i = 0; i < 4; i++)
        {
            Array.Clear(_channels[i]);
            _channels[i][MusicScriptNumber] = (ushort)number;
            if (data is not null)
            {
                _channels[i][NextCommand] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2 * i));
                if (_channels[i][NextCommand] != 0)
                {
                    _channels[i][TimeLeft] = 1;
                }
            }
        }

        _musicTimer = 0;
    }

    /// <summary>Mirrors <c>Player_V2Base::nextTick</c>: each channel's tick, then version 3's music timer.</summary>
    protected override void NextTick()
    {
        for (int i = 0; i < 4; i++)
        {
            if (_channels[i][TimeLeft] != 0)
            {
                NextFrequencies(i);
            }
        }

        if (_musicTimerCounter++ >= _ticksPerMusicTimer)
        {
            _musicTimerCounter = 0;
            _musicTimer++;
        }
    }

    /// <summary>The speaker plays the first channel that sounds. Mirrors <c>Player_V2::generateSpkSamples</c>.</summary>
    protected override void GenerateSpeakerSamples(Span<short> samples)
    {
        int winner = Array.FindIndex(_channels, 0, 4, c => c[Volume] != 0 && c[TimeLeft] != 0);
        samples.Clear();
        if (winner != -1)
        {
            SquareGenerator(0, _channels[winner][Freq], 0, 0, samples);
        }
        else if (Level == 0)
        {
            return;
        }

        LowPassFilter(samples);
    }

    /// <summary>Three voices, and noise whose rate is fixed or follows the third voice. Mirrors <c>Player_V2::generatePCjrSamples</c>.</summary>
    protected override void GeneratePcjrSamples(Span<short> samples)
    {
        samples.Clear();
        bool sounding = false;

        for (int i = 1; i < 3; i++)
        {
            int frequency = _channels[i][Freq] >> 6;
            if (Sounds(i))
            {
                for (int j = 0; j < i; j++)
                {
                    if (Sounds(j) && frequency == _channels[j][Freq] >> 6)
                    {
                        SyncPhase(i, j);
                    }
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            int frequency = _channels[i][Freq] >> 6;
            int volume = (65535 - _channels[i][Volume]) >> 12;
            if (!Sounds(i))
            {
                IdleTimer(i, samples.Length);
            }
            else if (i < 3)
            {
                sounding = true;
                SquareGenerator(i, frequency, volume, 0, samples);
            }
            else
            {
                int feedback = (frequency & 4) != 0 ? WhiteNoise : PeriodicNoise;
                int n = frequency & 3;
                frequency = n == 3 ? 2 * (_channels[2][Freq] >> 6) : 1 << (5 + n);
                sounding = true;
                SquareGenerator(i, frequency, volume, feedback, samples);
            }
        }

        if (Level != 0 || sounding)
        {
            LowPassFilter(samples);
        }
    }

    private bool Sounds(int channel) => _channels[channel][Volume] != 0 && _channels[channel][TimeLeft] != 0;

    private void ClearChannels()
    {
        for (int i = 0; i < 4; i++)
        {
            Array.Clear(_channels[i]);
        }
    }

    /// <summary>
    /// A channel's tick: volume and pitch slide, the modulation curve moves on, the note's sustain
    /// ends, its time runs out (and the next command runs), and the hull moves to its next part.
    /// Mirrors <c>Player_V2Base::next_freqs</c>.
    /// </summary>
    private void NextFrequencies(int index)
    {
        var channel = _channels[index];
        channel[Volume] += channel[VolumeDelta];
        channel[BaseFreq] += channel[FreqDelta];

        channel[FreqmodOffset] = channel[FreqmodModulo] > 0
            ? (ushort)((channel[FreqmodOffset] + channel[FreqmodIncrement]) % channel[FreqmodModulo])
            : (ushort)0;

        var table = SoundTables.FreqmodTable;
        int position = channel[FreqmodTable] + (channel[FreqmodOffset] >> 4);
        int modulation = position < table.Length ? table[position] : 0;
        channel[Freq] = (ushort)(modulation * channel[FreqmodMultiplier] / 256 + channel[BaseFreq]);

        if (channel[NoteLength] != 0 && --channel[NoteLength] == 0)
        {
            channel[HullOffset] = 16;
            channel[HullCounter] = 1;
        }

        if (--channel[TimeLeft] == 0)
        {
            ExecuteCommand(index);
        }

        if (channel[HullCounter] != 0 && --channel[HullCounter] == 0)
        {
            var hulls = SoundTables.Hulls;
            while (true)
            {
                int hull = channel[HullCurve] + channel[HullOffset] / 2;
                if (hull + 1 >= hulls.Length)
                {
                    break;
                }

                if (hulls[hull + 1] == -1)
                {
                    channel[Volume] = (ushort)hulls[hull];
                    if (hulls[hull] == 0)
                    {
                        channel[VolumeDelta] = 0;
                    }

                    channel[HullOffset] += 4;
                }
                else
                {
                    channel[VolumeDelta] = (ushort)hulls[hull];
                    channel[HullCounter] = (ushort)hulls[hull + 1];
                    channel[HullOffset] += 4;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Runs a channel's program until it waits: commands of 0xF8 and up set a hull or modulation
    /// curve, clear a channel, call or return, loop or set a field; the others play a note on a
    /// channel (a later channel can be given notes by an earlier one) or pause. When no channel has
    /// time left, the sound is over and the queued one starts. Mirrors <c>Player_V2Base::execute_cmd</c>.
    /// </summary>
    private void ExecuteCommand(int index)
    {
        var channel = _channels[index];
        var data = CurrentData;
        if (channel[NextCommand] != 0 && data is not null)
        {
            int p = channel[NextCommand];
            if (RunCommands(ref channel, data, ref p))
            {
                channel = _channels[index];
                if (channel[TimeLeft] != 0)
                {
                    channel[NextCommand] = (ushort)p;
                    return;
                }
            }

            channel = _channels[index];
            channel[NextCommand] = 0;
        }

        for (int i = 0; i < 4; i++)
        {
            if (_channels[i][TimeLeft] != 0)
            {
                return;
            }
        }

        CurrentNumber = 0;
        CurrentData = null;
        ChainNextSound();
    }

    /// <summary>The command loop of <c>execute_cmd</c>, up to its <c>end</c> label; true when it got there.</summary>
    private bool RunCommands(ref ushort[] channel, byte[] data, ref int p)
    {
        while (p < data.Length)
        {
            byte opcode = data[p++];
            if (opcode >= 0xF8)
            {
                switch (opcode)
                {
                    case 0xF8:
                        channel[HullCurve] = SoundTables.HullOffsets[Math.Min(data[p] / 2, SoundTables.HullOffsets.Length - 1)];
                        p++;
                        break;

                    case 0xF9:
                        {
                            int curve = Math.Min(data[p] / 4, SoundTables.FreqmodOffsets.Length - 1);
                            channel[FreqmodTable] = SoundTables.FreqmodOffsets[curve];
                            channel[FreqmodModulo] = SoundTables.FreqmodLengths[curve];
                            p++;
                            break;
                        }

                    case 0xFD:
                        {
                            // clear another channel: its offset in the original's channel table
                            int other = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p)) / (FieldCount * 2);
                            p += 2;
                            channel = _channels[Math.Min(other, 4)];
                            ClearChannel(channel);
                            break;
                        }

                    case 0xFA:
                        ClearChannel(channel);
                        break;

                    case 0xFB:
                        p = _returnAddress;
                        break;

                    case 0xFC:
                        {
                            int target = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(p));
                            p += 2;
                            _returnAddress = p;
                            p = target;
                            break;
                        }

                    case 0xFE:
                        {
                            var (fields, field) = Field(channel, data[p++] / 2);
                            int offset = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(p));
                            p += 2;
                            if (fields[field] == 0 || --fields[field] != 0)
                            {
                                p += offset;
                            }

                            break;
                        }

                    case 0xFF:
                        {
                            int field = data[p++];
                            var (fields, index) = Field(channel, field / 2);
                            fields[index] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
                            p += 2;

                            // setting the tempo starts version 3's music timer
                            if (field == 14)
                            {
                                _ticksPerMusicTimer = 125;
                            }

                            if (field == 0)
                            {
                                return true;
                            }

                            break;
                        }
                }

                continue;
            }

            while (true)
            {
                var target = _channels[(opcode >> 5) & 3];
                bool last;
                int note;
                if ((opcode & 0x80) == 0)
                {
                    int tempo = channel[Tempo] == 0 ? 1 : channel[Tempo];
                    var lengths = SoundTables.NoteLengths;
                    int length = (opcode & 0x1F) < lengths.Length ? lengths[opcode & 0x1F] : 0;
                    channel[TimeLeft] = (ushort)(tempo * length);
                    note = data[p++];
                    last = (note & 0x80) != 0;
                    note &= 0x7F;
                    if (note == 0x7F)
                    {
                        return true;
                    }
                }
                else
                {
                    channel[TimeLeft] = (ushort)(((opcode & 7) << 8) | data[p++]);
                    if ((opcode & 0x10) != 0)
                    {
                        return true;
                    }

                    last = false;
                    note = data[p++] & 0x7F;
                }

                target[TimeLeft] = channel[TimeLeft];
                target[NoteLength] = (ushort)(channel[TimeLeft] - target[InterNotePause]);
                note = (short)(note + target[Transpose]);
                while (note < 0)
                {
                    note += 12;
                }

                int octave = note / 12;
                note %= 12;
                target[HullOffset] = 0;
                target[HullCounter] = 1;
                ushort frequency;
                if (Pcjr && target == _channels[3])
                {
                    target[HullCurve] = (ushort)(196 + note * 12);
                    frequency = (ushort)(384 - 64 * octave);
                }
                else
                {
                    frequency = (ushort)(octave < 16 ? _frequencies[note] >> octave : 0);
                }

                target[Freq] = target[BaseFreq] = frequency;
                if (last)
                {
                    return true;
                }

                opcode = data[p++];
            }
        }

        return false;
    }

    /// <summary>
    /// A field of a channel, counted on into the next channels as the original's packed channel
    /// table runs on; past the last channel, the spare one's last field.
    /// </summary>
    private (ushort[] Fields, int Index) Field(ushort[] channel, int field)
    {
        int at = Array.IndexOf(_channels, channel) * FieldCount + field;
        return at < _channels.Length * FieldCount ? (_channels[at / FieldCount], at % FieldCount) : (_channels[^1], FieldCount - 1);
    }

    /// <summary>The fields the clear commands reset; the time left, the tempo and the music timer stay.</summary>
    private static void ClearChannel(ushort[] channel)
    {
        foreach (int field in (ReadOnlySpan<int>)[NextCommand, BaseFreq, FreqDelta, Freq, Volume, VolumeDelta, InterNotePause,
            Transpose, HullCurve, HullOffset, HullCounter, FreqmodTable, FreqmodOffset, FreqmodIncrement, FreqmodMultiplier, FreqmodModulo])
        {
            channel[field] = 0;
        }
    }
}
