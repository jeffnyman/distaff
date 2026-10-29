namespace Distaff.Scumm.Sound;

/// <summary>
/// The CMS (Game Blaster) player of versions 2 and 3, on an emulation of its two SAA1099 chips
/// (<see cref="CmsSynth"/>), ticked 236 times a second. A sound whose AdLib block holds a music
/// track plays that track: notes on up to 8 voices, each with an envelope, a vibrato and a
/// tremolo of its channel's instrument. Other sounds play the PCjr's programs through the same
/// sequencer as <see cref="PlayerV2"/>, its three voices and noise set on the first chip; music
/// playing, they wait. Mirrors ScummVM's <c>Player_V2CMS</c>.
/// </summary>
/// <remarks>
/// The sound is the AdLib block, from its size word, when its seventh byte is 0x80, and the
/// speaker and PCjr block otherwise (<c>readSoundResourceSmallHeader</c> for <c>MDT_CMS</c>).
/// ScummVM plays the chips at full volume, whatever the music and effects volumes.
/// </remarks>
public sealed class PlayerV2Cms : PlayerV2
{
    private const int TicksPerSecond = 236;

    private static readonly (byte Frequency, byte BaseOctave)[] MidiNotes =
        [.. Enumerable.Range(0, 132).Select(i => ((byte)new[] { 3, 31, 58, 83, 107, 130, 151, 172, 191, 209, 226, 242 }[i % 12], (byte)(i / 12)))];

    private static readonly byte[] AttackRates = [0, 2, 4, 7, 14, 26, 48, 82, 128, 144, 160, 176, 192, 208, 224, 255];
    private static readonly byte[] DecayRates = [0, 1, 2, 3, 4, 6, 12, 24, 48, 96, 192, 215, 255, 255, 255, 255];
    private static readonly byte[] SustainRates = [255, 180, 128, 96, 80, 64, 56, 48, 42, 36, 32, 28, 24, 20, 16, 0];
    private static readonly byte[] ReleaseRates = [0, 1, 2, 4, 6, 9, 14, 22, 36, 56, 80, 100, 120, 140, 160, 255];
    private static readonly byte[] CmsVolumes = [0x00, 0x10, 0x10, 0x11, 0x11, 0x21, 0x22, 0x22, 0x33, 0x44, 0x55, 0x66, 0x88, 0xAA, 0xCC, 0xFF];

    // pairs of a register and its value, written to both chips at the start; 1 to 10 turn them quiet
    private static readonly byte[] InitData =
        [0x1C, 0x02, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x14, 0x3F, 0x15, 0x00, 0x16, 0x00, 0x18, 0x00, 0x19, 0x00, 0x1C, 0x01];

    private readonly CmsSynth _cms;
    private readonly uint _samplesPerTick;
    private uint _nextTick;

    private readonly Instrument[] _instruments = [.. Enumerable.Range(0, 16).Select(_ => new Instrument())];
    private readonly Voice[] _voices = new Voice[8];

    // what goes to each chip's amplitude, frequency and octave registers (MusicChip)
    private readonly byte[][] _chipAmplitudes = [new byte[4], new byte[4]];
    private readonly byte[][] _chipFrequencies = [new byte[4], new byte[4]];
    private readonly byte[][] _chipOctaves = [new byte[2], new byte[2]];

    private byte _tempo, _tempoSum, _looping, _octaveMask, _lastMidiCommand, _voiceTimer;
    private short _midiDelay;
    private readonly Voice?[] _midiChannels = new Voice?[16];
    private readonly bool[] _midiChannelUse = new bool[16];
    private byte[]? _song;
    private int _midiData = -1, _songBegin;
    private int _loadedMidiSong;
    private int _outputTableReady;
    private int _musicTimer, _musicTimerTicks;

    private readonly byte[] _sfxFreq = [0xFF, 0xFF, 0xFF, 0xFF], _sfxAmplitude = new byte[4], _sfxOctave = [0x66, 0x66];

    public PlayerV2Cms(int sampleRate)
        : base(sampleRate, pcjr: true)
    {
        for (int i = 0; i < 8; i++)
        {
            _voices[i] = new Voice { Chip = i / 4, Slot = i % 4, OctaveSlot = i % 4 / 2 };
        }

        // the chips set up as the original does
        _cms = new CmsSynth(sampleRate);
        for (int chip = 0, port = 0x220; chip < 2; chip++, port += 2)
        {
            for (int off = 0; off < 13; off++)
            {
                _cms.Write(port + 1, InitData[off * 2]);
                _cms.Write(port, InitData[off * 2 + 1]);
            }
        }

        _samplesPerTick = (uint)(((sampleRate / TicksPerSecond) << FixpShift) + ((sampleRate % TicksPerSecond) << FixpShift) / TicksPerSecond);
    }

    private bool PlayingMusic => _midiData >= 0;

    /// <summary>The music track's own timer while one plays, a second's worth of ticks at a time; version 3's otherwise. Mirrors <c>getMusicTimer</c>.</summary>
    public override int MusicTimer
    {
        get
        {
            lock (Sync)
            {
                return PlayingMusic ? _musicTimer : base.MusicTimer;
            }
        }
    }

    /// <summary>The chips play at full volume (<c>setMusicVolume</c> does nothing).</summary>
    public override void SetVolumes(int music, int sfx)
    {
    }

    /// <summary>A music track starts at once, in place of the one playing; another sound as the PCjr's does. Mirrors <c>startSound</c>.</summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (Sync)
        {
            if (data[6] == 0x80)
            {
                _musicTimer = _musicTimerTicks = 0;
                LoadMidiData(data, number);
            }
            else
            {
                base.StartSound(number, data);
            }
        }
    }

    public override void StopSound(int number)
    {
        lock (Sync)
        {
            base.StopSound(number);
            if (_loadedMidiSong == number)
            {
                StopMusic();
                OffAllChannels();
            }
        }
    }

    public override void StopAllSounds()
    {
        lock (Sync)
        {
            base.StopAllSounds();
            StopMusic();
            _musicTimer = _musicTimerTicks = 0;
            OffAllChannels();
        }
    }

    public override bool IsPlaying(int number)
    {
        lock (Sync)
        {
            return base.IsPlaying(number) || _loadedMidiSong == number;
        }
    }

    private void StopMusic()
    {
        _loadedMidiSong = 0;
        _song = null;
        _midiData = -1;
        _songBegin = 0;
        _midiDelay = 0;
    }

    /// <summary>
    /// Renders the chips, ticking the player between runs of samples as ScummVM's emulated chips
    /// call their timer (<c>EmulatedChip::readBuffer</c>).
    /// </summary>
    protected override void RenderStereo(Span<short> interleavedStereo)
    {
        lock (Sync)
        {
            while (interleavedStereo.Length > 0)
            {
                int step = (int)Math.Min((uint)interleavedStereo.Length / 2, _nextTick >> FixpShift);
                _cms.Generate(interleavedStereo[..(step * 2)]);
                _nextTick -= (uint)step << FixpShift;
                if ((_nextTick >> FixpShift) == 0)
                {
                    OnTimer();
                    _nextTick += _samplesPerTick;
                }

                interleavedStereo = interleavedStereo[(step * 2)..];
            }
        }
    }

    protected override void RenderMono(Span<short> buffer) => throw new NotSupportedException("The CMS renders in stereo.");

    /// <summary>The track's voices, every other tick, and its notes as its tempo says; or the PCjr's sequencer and its sounds. Mirrors <c>onTimer</c>.</summary>
    private void OnTimer()
    {
        if (PlayingMusic)
        {
            _voiceTimer--;
            if ((_voiceTimer & 0x01) == 0)
            {
                PlayVoice();
            }

            int tempoSum = _tempo + _tempoSum;
            _tempoSum = (byte)tempoSum;
            if (tempoSum > 0xFF)
            {
                ProcessMidiData();
            }
        }
        else
        {
            try
            {
                NextTick();
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // a program that runs off its sound's data stops it, as for the PCjr
                base.StopSound(CurrentNumber);
            }

            Play();
        }
    }

    /// <summary>
    /// A music track: its tempo, whether it ends or goes round, and each channel's instrument,
    /// its envelope's rates and its vibrato and tremolo; its notes from byte 151. Mirrors <c>loadMidiData</c>.
    /// </summary>
    private void LoadMidiData(byte[] data, int sound)
    {
        Array.Clear(_midiChannelUse);
        Array.Clear(_midiChannels);
        _tempo = data[7];
        _looping = data[8];

        int channels = data[14];
        for (int current = 0, voice = 23; channels != 0; current++, channels--, voice += 16)
        {
            if (data[15 + current] == 0)
            {
                continue;
            }

            int channel = data[15 + current] - 1;
            _midiChannelUse[channel] = true;
            var instrument = _instruments[channel];
            instrument.Attack = AttackRates[data[voice + 10] >> 4];
            instrument.Decay = DecayRates[data[voice + 10] & 0x0F];
            instrument.Sustain = SustainRates[data[voice + 11] >> 4];
            instrument.Release = ReleaseRates[data[voice + 11] & 0x0F];
            instrument.Vibrato = (data[voice + 3] & 0x40) == 0 ? (short)0 : (data[voice + 13] & 0x40) != 0 ? (short)0x0601 : (short)0x0301;
            instrument.Vibrato2 = (data[voice + 8] & 0x80) == 0 ? (short)0 : (data[voice + 13] & 0x80) != 0 ? (short)0x050C : (short)0x0506;
            instrument.OctaveAdd = (byte)((data[voice + 8] & 0x0F) > 1 ? 1 : 0);
        }

        foreach (var voice in _voices)
        {
            voice.ChanNumber = 0xFF;
            voice.CurVolume = 0;
            voice.NextVoice = null;
        }

        _midiDelay = 0;
        foreach (var table in (byte[][][])[_chipAmplitudes, _chipFrequencies, _chipOctaves])
        {
            Array.Clear(table[0]);
            Array.Clear(table[1]);
        }

        _song = data;
        _midiData = 151;
        _songBegin = _midiData + data[9];
        _loadedMidiSong = sound;
    }

    /// <summary>
    /// The track's next events when its delay has run out: notes on and off, the end (round to the
    /// song's start again unless it ends), and the delay to the next. Mirrors <c>processMidiData</c>.
    /// </summary>
    private void ProcessMidiData()
    {
        var data = _song!;
        int current = _midiData;
        if (++_musicTimerTicks > 60)
        {
            _musicTimerTicks = 0;
            _musicTimer++;
        }

        if (_midiDelay == 0)
        {
            short delay;
            while (true)
            {
                byte command = data[current++];
                if (command == 0xFF)
                {
                    command = data[current++];
                    if (command == 0x2F)
                    {
                        if (_looping == 0)
                        {
                            current = _midiData = _songBegin;
                            continue;
                        }

                        StopMusic();
                        OffAllChannels();
                        return;
                    }

                    if (command == 0x58)
                    {
                        current += 6;
                    }
                }
                else
                {
                    _lastMidiCommand = command;
                    if (command < 0x90)
                    {
                        ClearNote(data, ref current);
                    }
                    else
                    {
                        PlayNote(data, ref current);
                    }
                }

                command = data[current++];
                delay = command;
                if ((command & 0x80) != 0)
                {
                    delay = (short)((command & 0x7F) << 8);
                    command = data[current++];
                    delay |= (short)(command << 1);
                    delay >>= 1;
                }

                delay >>= 1;
                int lastBit = delay & 1;
                delay >>= 1;
                delay += (short)lastBit;
                if (delay != 0)
                {
                    break;
                }
            }

            _midiData = current;
            _midiDelay = delay;
        }

        if (--_midiDelay < 0)
        {
            _midiDelay = 0;
        }
    }

    /// <summary>The chips are given what the voices set, and each voice moves on its envelope; a voice let go fades by its release. Mirrors <c>playVoice</c>.</summary>
    private void PlayVoice()
    {
        if (_outputTableReady != 0)
        {
            PlayMusicChips();
            _outputTableReady = 0;
        }

        _octaveMask = 0xF0;
        foreach (var voice in _voices)
        {
            _octaveMask = (byte)~_octaveMask;
            if (voice.ChanNumber != 0xFF)
            {
                ProcessChannel(voice);
            }
            else
            {
                if (voice.CurVolume == 0)
                {
                    _chipAmplitudes[voice.Chip][voice.Slot] = 0;
                }

                int volume = Math.Max(0, voice.CurVolume - voice.ReleaseRate);
                voice.CurVolume = (byte)volume;
                _chipAmplitudes[voice.Chip][voice.Slot] = (byte)(((volume >> 4) | (volume & 0xF0)) & voice.Channel);
                _outputTableReady++;
            }
        }
    }

    private void ProcessChannel(Voice voice)
    {
        _outputTableReady++;
        switch (voice.State)
        {
            case EnvelopeState.Attack:
                int attack = voice.CurVolume + voice.AttackRate;
                if (attack > voice.MaxAmplitude)
                {
                    voice.CurVolume = voice.MaxAmplitude;
                    voice.State = EnvelopeState.Decay;
                }
                else
                {
                    voice.CurVolume = (byte)attack;
                }

                break;

            case EnvelopeState.Decay:
                int decay = voice.CurVolume - voice.DecayRate;
                if (decay <= voice.SustainRate)
                {
                    voice.CurVolume = voice.SustainRate;
                    voice.State = EnvelopeState.Sustain;
                }
                else
                {
                    voice.CurVolume = (byte)decay;
                }

                break;

            case EnvelopeState.Sustain:
                if (voice.UnkVibratoRate != 0)
                {
                    short volume = (short)(voice.CurVolume + voice.UnkRate);
                    if ((volume & 0xFF00) != 0)
                    {
                        volume = (sbyte)(volume >> 8);
                        volume = (short)-volume;
                    }

                    voice.CurVolume = (byte)volume;
                    if (--voice.UnkCount == 0)
                    {
                        voice.UnkRate = (sbyte)-voice.UnkRate;
                        voice.UnkCount = (sbyte)((voice.UnkVibratoDepth & 0x0F) << 1);
                    }
                }

                break;

            case EnvelopeState.Release:
                voice.CurVolume = (byte)Math.Max(0, voice.CurVolume - voice.ReleaseRate);
                break;
        }

        ProcessVibrato(voice);
    }

    /// <summary>The voice's pitch moves by its vibrato, and its amplitude, frequency and octave go to the chip's table. Mirrors <c>processVibrato</c>.</summary>
    private void ProcessVibrato(Voice voice)
    {
        if (voice.VibratoRate != 0)
        {
            short temp = (short)(voice.CurFreq + voice.CurVibratoRate);
            voice.CurOctave = (byte)(voice.CurOctave + ((temp & 0xFF00) >> 8));
            voice.CurFreq = (byte)temp;
            if (--voice.CurVibratoUnk == 0)
            {
                voice.CurVibratoRate = (sbyte)-voice.CurVibratoRate;
                voice.CurVibratoUnk = (sbyte)((voice.VibratoDepth & 0x0F) << 1);
            }
        }

        _chipAmplitudes[voice.Chip][voice.Slot] = (byte)(((voice.CurVolume >> 4) | (voice.CurVolume & 0xF0)) & voice.Channel);
        _chipFrequencies[voice.Chip][voice.Slot] = voice.CurFreq;
        ref byte octave = ref _chipOctaves[voice.Chip][voice.OctaveSlot];
        octave = (byte)((((voice.CurOctave << 4) | (voice.CurOctave & 0x0F)) & _octaveMask) | (~_octaveMask & octave));
    }

    /// <summary>Both chips quiet, as at the start. Mirrors <c>offAllChannels</c>.</summary>
    private void OffAllChannels()
    {
        for (int chip = 0, port = 0x220; chip < 2; chip++, port += 2)
        {
            for (int off = 1; off <= 10; off++)
            {
                _cms.Write(port + 1, InitData[off * 2]);
                _cms.Write(port, InitData[off * 2 + 1]);
            }
        }
    }

    /// <summary>
    /// A voice for a note: one let go and silent, or the quietest let go, taken for the command's
    /// channel and put first in its list. Mirrors <c>getFreeVoice</c>.
    /// </summary>
    private Voice? GetFreeVoice()
    {
        Voice? selected = null;
        byte volume = 0xFF;
        foreach (var voice in _voices)
        {
            if (voice.ChanNumber == 0xFF)
            {
                if (voice.CurVolume == 0)
                {
                    selected = voice;
                    break;
                }

                if (voice.CurVolume < volume)
                {
                    selected = voice;
                    volume = voice.CurVolume;
                }
            }
        }

        if (selected is not null)
        {
            selected.ChanNumber = (byte)(_lastMidiCommand & 0x0F);
            selected.NextVoice = _midiChannels[selected.ChanNumber];
            _midiChannels[selected.ChanNumber] = selected;
        }

        return selected;
    }

    /// <summary>A note on: a voice takes its channel's instrument and starts its attack at the note's pitch. Mirrors <c>playNote</c>.</summary>
    private void PlayNote(byte[] data, ref int at)
    {
        int channel = _lastMidiCommand & 0x0F;
        if (_midiChannelUse[channel] && GetFreeVoice() is { } voice)
        {
            var instrument = _instruments[voice.ChanNumber];
            voice.AttackRate = instrument.Attack;
            voice.DecayRate = instrument.Decay;
            voice.SustainRate = instrument.Sustain;
            voice.ReleaseRate = instrument.Release;
            voice.OctaveAdd = (sbyte)instrument.OctaveAdd;
            voice.VibratoRate = voice.CurVibratoRate = (sbyte)(instrument.Vibrato & 0xFF);
            voice.VibratoDepth = voice.CurVibratoUnk = (sbyte)(instrument.Vibrato >> 8);
            voice.UnkVibratoRate = voice.UnkRate = (sbyte)(instrument.Vibrato2 & 0xFF);
            voice.UnkVibratoDepth = voice.UnkCount = (sbyte)(instrument.Vibrato2 >> 8);
            voice.MaxAmplitude = 0xFF;
            voice.PlayingNote = (sbyte)data[at];

            int note = Math.Clamp(voice.PlayingNote + 3, 0, MidiNotes.Length - 1);
            int octave = Math.Clamp(MidiNotes[note].BaseOctave + voice.OctaveAdd - 3, 0, 7);
            voice.CurOctave = (byte)(octave == 0 ? 1 : octave);
            voice.CurFreq = MidiNotes[note].Frequency;
            voice.CurVolume = 0;
            voice.State = EnvelopeState.Attack;
            voice.Channel = (_lastMidiCommand & 1) == 0 ? (byte)0xF0 : (byte)0x0F;
        }

        at += 2;
    }

    /// <summary>The voice playing a note on the command's channel, taken out of its list. Mirrors <c>getPlayVoice</c>.</summary>
    private Voice? GetPlayVoice(byte note)
    {
        int channel = _lastMidiCommand & 0x0F;
        var voice = _midiChannels[channel];
        if (voice is not null)
        {
            Voice? previous = null;
            while (voice.PlayingNote != note)
            {
                previous = voice;
                voice = voice.NextVoice;
                if (voice is null)
                {
                    return null;
                }
            }

            if (previous is not null)
            {
                previous.NextVoice = voice.NextVoice;
            }
            else
            {
                _midiChannels[channel] = voice.NextVoice;
            }
        }

        return voice;
    }

    /// <summary>A note off: its voice is let go and releases. Mirrors <c>clearNote</c>.</summary>
    private void ClearNote(byte[] data, ref int at)
    {
        if (GetPlayVoice(data[at]) is { } voice)
        {
            voice.ChanNumber = 0xFF;
            voice.NextVoice = null;
            voice.State = EnvelopeState.Release;
        }

        at += 2;
    }

    /// <summary>
    /// The PCjr's three voices and noise, as the sequencer sets them, to the first chip: each
    /// voice's frequency to the chip's octave and note, its volume through the table; the noise
    /// either set by its own frequency or following the third voice. Mirrors <c>play</c>.
    /// </summary>
    private void Play()
    {
        _octaveMask = 0xF0;
        byte noiseGen = 3;
        for (int i = 1; i <= 4; i++)
        {
            var (running, channelFreq, volume) = ChannelState(i - 1);
            if (running)
            {
                ushort freq = channelFreq;
                if (i == 4)
                {
                    if (((freq >> 8) & 0x40) != 0)
                    {
                        noiseGen = (byte)freq;
                    }
                    else
                    {
                        noiseGen = 3;
                        _sfxFreq[0] = _sfxFreq[3];
                        _sfxOctave[0] = (byte)((_sfxOctave[0] & 0xF0) | ((_sfxOctave[1] & 0xF0) >> 4));
                    }
                }
                else
                {
                    if (freq == 0)
                    {
                        freq = 0xFFC0;
                    }

                    int cmsOct = 2, freqOct = 0x8000;
                    while (freq < freqOct)
                    {
                        freqOct >>= 1;
                        if (++cmsOct == 8)
                        {
                            cmsOct--;
                            freq = 1024;
                            break;
                        }
                    }

                    byte oct = (byte)((cmsOct << 4) | cmsOct);
                    oct &= _octaveMask;
                    oct |= (byte)(~_octaveMask & _sfxOctave[(i & 3) >> 1]);
                    _sfxOctave[(i & 3) >> 1] = oct;
                    freq >>= 9 - cmsOct;
                    _sfxFreq[i & 3] = (byte)-(freq - 511);
                }

                _sfxAmplitude[i & 3] = CmsVolumes[volume >> 12];
            }
            else
            {
                _sfxAmplitude[i & 3] = 0;
            }

            _octaveMask ^= 0xFF;
        }

        // the amplitude's high nibble is the right channel's, its low the left's
        for (int i = 0; i < 4; i++)
        {
            Register(0x220, i, _sfxAmplitude[i]);
        }

        for (int i = 0; i < 4; i++)
        {
            Register(0x220, 8 + i, _sfxFreq[i]);
        }

        Register(0x220, 0x10, _sfxOctave[0]);
        Register(0x220, 0x11, _sfxOctave[1]);
        Register(0x220, 0x14, 0x3E);
        Register(0x220, 0x15, 0x01);
        Register(0x220, 0x16, noiseGen);
    }

    /// <summary>Each chip takes its table's amplitudes, frequencies and octaves, its square waves on and its noise off. Mirrors <c>playMusicChips</c>.</summary>
    private void PlayMusicChips()
    {
        for (int chip = 0, port = 0x220; chip < 2; chip++, port += 2)
        {
            for (int i = 0; i < 4; i++)
            {
                Register(port, i, _chipAmplitudes[chip][i]);
            }

            for (int i = 0; i < 4; i++)
            {
                Register(port, 8 + i, _chipFrequencies[chip][i]);
            }

            Register(port, 0x10, _chipOctaves[chip][0]);
            Register(port, 0x11, _chipOctaves[chip][1]);
            Register(port, 0x14, 0x3F);
            Register(port, 0x15, 0x00);
        }
    }

    private void Register(int port, int register, int value)
    {
        _cms.Write(port + 1, register);
        _cms.Write(port, value);
    }

    private enum EnvelopeState
    {
        Attack,
        Decay,
        Sustain,
        Release,
    }

    /// <summary>A channel's instrument, as the track sets it (<c>Voice</c>).</summary>
    private sealed class Instrument
    {
        public byte Attack, Decay, Sustain, Release, OctaveAdd;
        public short Vibrato, Vibrato2;
    }

    /// <summary>One of the 8 voices, 4 on each chip (<c>Voice2</c>); the third and fourth of a chip share an octave register with each other, as do the first and second.</summary>
    private sealed class Voice
    {
        public int Chip, Slot, OctaveSlot;
        public byte Channel, AttackRate, MaxAmplitude, DecayRate, SustainRate, ReleaseRate;
        public sbyte VibratoRate, VibratoDepth, CurVibratoRate, CurVibratoUnk, UnkVibratoRate, UnkVibratoDepth, UnkRate, UnkCount;
        public EnvelopeState State;
        public byte CurVolume, CurOctave, CurFreq;
        public sbyte OctaveAdd, PlayingNote;
        public Voice? NextVoice;
        public byte ChanNumber;
    }
}
