namespace Distaff.Scumm.Sound.IMuse;

/// <summary>
/// iMuse's MT-32 driver as ScummVM sets it up for the games before Sam &amp; Max (the old system):
/// nine channels, the eight melodic parts of the module (MIDI channels 1 to 8) and its rhythm
/// part (channel 9), each iMuse channel on its own part. A program is set by writing the timbre's
/// number into the part's patch temp area and sending the part's own program; repeated controller
/// and pitch bend messages that would not change the module's state are not sent. On opening it
/// greets the module's display, resets it, and sets its master tune, reverb, channels, partial
/// reserve, volume, rhythm keys and every patch's bend range. Mirrors <c>IMuseDriver_MT32</c>,
/// <c>IMuseChannel_MT32</c> and what of <c>IMuseDriver_GMidi</c> and <c>IMuseChannel_Midi</c>
/// they use (<c>engines/scumm/imuse/drivers/midi.cpp</c>).
/// </summary>
internal sealed class IMuseDriverMt32
{
    private const int NumChannels = 9;

    private readonly IMidiDevice _drv;
    private readonly Channel[] _parts = new Channel[NumChannels];
    private readonly byte[] _midiRegState = new byte[160];

    /// <param name="device">The MT-32 the driver plays on.</param>
    /// <param name="ticksPerSecond">How often the device calls its timer, which is the driver's base tempo.</param>
    public IMuseDriverMt32(IMidiDevice device, int ticksPerSecond)
    {
        _drv = device;
        BaseTempo = (uint)(1000000 / ticksPerSecond);
        Array.Fill(_midiRegState, (byte)0xFF);
    }

    /// <summary>The microseconds between timer calls (<c>getBaseTempo</c>).</summary>
    public uint BaseTempo { get; }

    /// <summary>
    /// Makes the channels and sets the module up. Mirrors <c>IMuseDriver_GMidi::open</c> with
    /// <c>IMuseDriver_MT32::createChannels</c> and <c>initDevice</c>; the module shows
    /// <paramref name="welcome"/>, centred, as ScummVM shows its name and version. ScummVM's waits
    /// for the module to take the messages take no time here.
    /// </summary>
    public void Open(string welcome)
    {
        for (int i = 0; i < NumChannels; i++)
        {
            _parts[i] = new Channel(this, (i + 1) & 0x0F);
        }

        // the melodic parts each play on the module's part of their number
        for (int i = 1; i < 9; ++i)
        {
            GetPart(i)!.Out = (i, 0xC000 + (i << 4));
        }

        for (int i = (20 - welcome.Length) >> 1; i > 0; --i)
        {
            welcome = ' ' + welcome + ' ';
        }

        SendMt32Sysex(0x80000, [.. welcome.Take(20).Select(c => (byte)c)]);

        // reset the module
        SendMt32Sysex(0x1FC000, []);

        // master tune, reverb mode, time and level, channel mapping, partial reserve and master volume
        SendMt32Sysex(0x40000, [0x40, 0x00, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x64]);

        // percussion on notes 24 to 34, without reverb
        SendMt32Sysex(0xC090, [0x40, 0x64, 0x07, 0x00, 0x4a, 0x64, 0x06, 0x00, 0x41, 0x64, 0x07, 0x00, 0x4b, 0x64, 0x08, 0x00, 0x45, 0x64, 0x06, 0x00, 0x44, 0x64,
            0x0b, 0x00, 0x51, 0x64, 0x05, 0x00, 0x43, 0x64, 0x08, 0x00, 0x50, 0x64, 0x07, 0x00, 0x42, 0x64, 0x03, 0x00, 0x4c, 0x64, 0x07, 0x00]);

        for (int i = 0; i < 128; ++i)
        {
            SendMt32Sysex(0x014004 + (i << 3), [0x10]);
        }

        for (uint i = 0; i < 16; ++i)
        {
            Send(0x0000C0 | i);
            Send(0x0040B0 | i);
            Send(0x007BB0 | i);
            Send(0x3F0AB0 | i);
            Send(0x4000E0 | i);
        }

        foreach (var part in _parts)
        {
            part.Reset();
        }
    }

    /// <summary>A free melodic channel, or null. Mirrors <c>IMuseDriver_GMidi::allocateChannel</c>.</summary>
    public MidiChannel? AllocateChannel()
    {
        foreach (var ch in _parts)
        {
            if (ch.Number != 9 && ch.Allocate())
            {
                return ch;
            }
        }

        return null;
    }

    /// <summary>The rhythm part (<c>getPercussionChannel</c>).</summary>
    public MidiChannel PercussionChannel => GetPart(9)!;

    private Channel? GetPart(int number) => _parts.FirstOrDefault(p => p.Number == number);

    /// <summary>A message to the module, unless it would not change what the module has. Mirrors <c>IMuseDriver_GMidi::send</c> and <c>trackMidiState</c>.</summary>
    public void Send(uint b)
    {
        if (TrackMidiState(b))
        {
            _drv.Send(b);
        }
    }

    private bool TrackMidiState(uint b)
    {
        // program change, control change and pitch bend only; the old MT-32 programs are always sent
        byte evt = (byte)(((b & 0xF0) - 0xB0) >> 4);
        if (evt > 3 || evt == 1)
        {
            return true;
        }

        int part = (int)(b & 0x0F);
        b >>= 8;
        int var = part;
        switch (evt)
        {
            case 0:
                // these controllers only; the first entry stands for the program changes' state
                int found = Array.IndexOf(ControllerOrder, (byte)(b & 0xFF));
                if (found <= 0)
                {
                    return true;
                }

                var += found * 0x10;
                b >>= 8;
                if (_midiRegState[var] == (byte)b)
                {
                    return false;
                }

                _midiRegState[var] = (byte)b;
                break;

            case 3:
                var += 0x80 + part;
                ushort value = (ushort)(_midiRegState[var] | (_midiRegState[var + 1] << 8));
                if (value == (ushort)b)
                {
                    return false;
                }

                _midiRegState[var] = (byte)b;
                _midiRegState[var + 1] = (byte)(b >> 8);
                break;
        }

        return true;
    }

    private static readonly byte[] ControllerOrder = [0xFF, 0, 1, 7, 10, 64, 91, 93];

    /// <summary>A Roland data set to the module's memory, its checksum computed. Mirrors <c>sendMT32Sysex</c>.</summary>
    public void SendMt32Sysex(int addr, ReadOnlySpan<byte> data)
    {
        var msg = new byte[4 + 3 + data.Length + 1];
        msg[0] = 0x41;
        msg[1] = 0x10;
        msg[2] = 0x16;
        msg[3] = 0x12;
        msg[4] = (byte)((addr >> 14) & 0x7F);
        msg[5] = (byte)((addr >> 7) & 0x7F);
        msg[6] = (byte)(addr & 0x7F);
        data.CopyTo(msg.AsSpan(7));
        byte checkSum = 0;
        for (int i = 4; i < msg.Length - 1; i++)
        {
            checkSum -= msg[i];
        }

        msg[^1] = (byte)(checkSum & 0x7F);
        _drv.SysEx(msg);
    }

    /// <summary>An iMuse channel on the module (<c>IMuseChannel_MT32</c>, the old system).</summary>
    private sealed class Channel(IMuseDriverMt32 drv, int number) : MidiChannel
    {
        private readonly int _number = number;
        private readonly int _sysexPatchAddrBase = 0x14000 + (number << 3);

        private bool _allocated;
        private short _pitchBend = 0x2000;
        private short _detune;
        private sbyte _transpose;
        private short _pitchBendTemp;
        private byte _pitchBendSensitivity = 2;
        private byte _program;
        private byte _timbre = 0xFF;
        private byte _reverbSwitch = 1;

        /// <summary>The module's part this channel plays on, and its patch temp address; none for the rhythm part.</summary>
        public (int Number, int Addr)? Out { get; set; }

        public override byte Number => (byte)_number;

        public bool Allocate()
        {
            if (_allocated)
            {
                return false;
            }

            _allocated = true;
            _program = (byte)_number;
            return true;
        }

        public override void Release() => _allocated = false;

        /// <summary>The part's patch: its timbre, key shift, fine tune, bend range, assign mode and reverb. Mirrors <c>reset</c>.</summary>
        public void Reset() => SendSysexPatchData(0, [(byte)(_timbre >> 6), (byte)(_timbre & 0x3F), 0x18, 0x32, 0x10, 0x00, _reverbSwitch]);

        public override void NoteOff(byte note) => SendMidi(0x80, note, 0x40);

        public override void NoteOn(byte note, byte velocity) => SendMidi(0x90, note, velocity);

        public override void ProgramChange(byte program)
        {
            if (program > 127)
            {
                return;
            }

            if (_timbre != program)
            {
                _timbre = program;
                SendSysexPatchData(0, [(byte)(program >> 6), (byte)(program & 0x3F)]);
            }

            if (_program < 128)
            {
                SendMidi(0xC0, _program, 0);
            }
        }

        /// <summary>The bend with the channel's detune and transposition. Mirrors <c>IMuseChannel_Midi::pitchBend</c>, the formula of DOTT, Indy 4 and Monkey Island 2.</summary>
        public override void PitchBend(short bend)
        {
            _pitchBendTemp = bend;
            bend = (short)(Math.Clamp(((bend * _pitchBendSensitivity) >> 6) + _detune + (_transpose << 7), -2048, 2047) << 2);
            _pitchBend = (short)(bend + 0x2000);
            SendMidi(0xE0, (byte)(_pitchBend & 0x7F), (byte)((_pitchBend >> 7) & 0x7F));
        }

        public override void PitchBendFactor(byte value)
        {
            PitchBend(0);
            _pitchBendSensitivity = value;
        }

        public override void Transpose(sbyte value)
        {
            _transpose = value;
            PitchBend(_pitchBendTemp);
        }

        public override void Detune(short value)
        {
            _detune = value;
            PitchBend(_pitchBendTemp);
        }

        public override void Priority(byte value)
        {
        }

        public override void Sustain(bool value) => SendMidi(0xB0, 0x40, (byte)(value ? 1 : 0));

        public override void AllNotesOff() => SendMidi(0xB0, 0x7B, 0);

        public override void ModulationWheel(byte value) => SendMidi(0xB0, 0x01, value);

        public override void Volume(byte value) => SendMidi(0xB0, 0x07, value);

        public override void PanPosition(byte value) => SendMidi(0xB0, 0x0A, value);

        /// <summary>Reverb on or off, in the part's patch and the module's part. Mirrors <c>effectLevel</c>.</summary>
        public override void EffectLevel(byte value)
        {
            value = (byte)(value != 0 ? 1 : 0);
            if (_reverbSwitch == value)
            {
                return;
            }

            _reverbSwitch = value;
            SendSysexPatchData(6, [_reverbSwitch]);
            if (Out is { } output)
            {
                drv.SendMt32Sysex(output.Addr + 6, [_reverbSwitch]);
            }
        }

        public override void ChorusLevel(byte value)
        {
        }

        /// <summary>To the module's part, or the rhythm part. Mirrors <c>IMuseChannel_MT32::sendMidi</c>.</summary>
        private void SendMidi(byte stat, byte par1, byte par2)
        {
            if (Out is not null || _number == 9)
            {
                drv.Send(((uint)par2 << 16) | ((uint)par1 << 8) | (uint)((stat & 0xF0) | (Out?.Number ?? 9)));
            }
        }

        private void SendSysexPatchData(int offset, ReadOnlySpan<byte> data) => drv.SendMt32Sysex(_sysexPatchAddrBase + offset, data);
    }
}
