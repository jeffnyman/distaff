namespace Distaff.Scumm.Sound.IMuse;

/// <summary>
/// iMuse, the music engine ScummVM plays the Roland Upgrade's sounds with: up to 8 sounds at once,
/// each a player reading its music and playing it on parts (up to 32 between them), which the
/// driver gives MIDI channels by priority. Each tick of the driver's timer moves every player on.
/// Mirrors <c>IMuseInternal</c> (<c>engines/scumm/imuse/imuse.cpp</c>) for a native MT-32 and the
/// old system (Loom, Monkey Island 1 and 2, Indiana Jones 4, Day of the Tentacle), with dynamic
/// channel allocation, as Loom has it. Left out, as Loom's scripts and music never reach them:
/// the scripts' commands (<c>doCommand</c>, which only <c>soundKludge</c> reaches), so the
/// command queue, triggers, hooks, deferred commands and faders' targets; iMuse's own sysex
/// messages and Roland instruments (an RO sound has no sysex); the AdLib and other drivers; and
/// saving, which is to come.
/// </summary>
internal sealed class IMuseEngine
{
    public const int TicksPerBeat = 480;

    private readonly IMuseDriverMt32 _midi;
    private readonly Player[] _players = new Player[8];
    private readonly Part[] _parts = new Part[32];
    private readonly ushort[] _channelVolume = new ushort[8];
    private readonly ushort[] _channelVolumeEff = new ushort[8];
    private readonly List<Part> _waitingPartsQueue = [];
    private byte _masterVolume;
    private byte _musicVolume;
    private byte _musicVolumeEff;
    private bool _paused;

    public IMuseEngine(IMuseDriverMt32 midi)
    {
        _midi = midi;
        TempoFactor = 100;
        _masterVolume = 255;
        for (int i = 0; i != 8; i++)
        {
            _channelVolume[i] = _channelVolumeEff[i] = 127;
        }

        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new Player(this);
        }

        for (int i = 0; i < _parts.Length; i++)
        {
            _parts[i] = new Part(this, i);
        }
    }

    public int TempoFactor { get; }

    public IMuseDriverMt32 Midi => _midi;

    /// <summary>Notes are as the MT-32 has them, not mapped from General MIDI (<c>_native_mt32</c>).</summary>
    public bool IsNativeMt32 => true;

    /// <summary>The rhythm part's last volume, polyphony and priority (<c>_rhyState</c>).</summary>
    public (byte Vol, byte Poly, byte Prio) RhyState { get; set; } = (127, 1, 0);

    /// <summary>The music volume, 0 to 255; each channel's effective volume scales with it. Mirrors <c>setMusicVolume</c>.</summary>
    public void SetMusicVolume(int vol)
    {
        vol = Math.Min(vol, 255);
        if (_musicVolume == vol)
        {
            return;
        }

        _musicVolume = (byte)vol;
        _musicVolumeEff = _musicVolume;
        vol = _masterVolume * _musicVolumeEff / 255;
        for (int i = 0; i < _channelVolume.Length; i++)
        {
            _channelVolumeEff[i] = (ushort)(_channelVolume[i] * vol / 255);
        }

        if (!_paused)
        {
            UpdateVolumes();
        }
    }

    /// <summary>
    /// Silences the players while paused, and turns every note of the module off, which an MT-32
    /// sometimes needs (bug #1263). Mirrors <c>pause</c>.
    /// </summary>
    public void Pause(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        byte vol = _musicVolumeEff;
        if (paused)
        {
            _musicVolumeEff = 0;
        }

        UpdateVolumes();
        _musicVolumeEff = vol;
        for (uint i = 0; i < 16; ++i)
        {
            _midi.Send(123 << 8 | 0xB0 | i);
        }

        _paused = paused;
    }

    /// <summary>The driver's timer: every player moves on. Mirrors <c>on_timer</c> and <c>sequencer_timers</c>.</summary>
    public void OnTimer()
    {
        if (_paused)
        {
            return;
        }

        foreach (var player in _players)
        {
            if (player.IsActive)
            {
                player.OnTimer();
            }
        }
    }

    /// <summary>
    /// Starts a sound, from its beginning again if it plays, on a free player or one of lower
    /// priority. Mirrors <c>startSound_internal</c>.
    /// </summary>
    public bool StartSound(int sound, byte[] data)
    {
        var player = FindActivePlayer(sound) ?? AllocatePlayer(128);
        if (player is null)
        {
            return false;
        }

        player.Clear();
        player.SetOffsetNote(0);
        return player.StartSound(sound, data);
    }

    public void StopSound(int sound) => FindActivePlayer(sound)?.Clear();

    public void StopAllSounds()
    {
        foreach (var player in _players)
        {
            if (player.IsActive)
            {
                player.Clear();
            }
        }
    }

    /// <summary>Whether a sound plays. Mirrors <c>getSoundStatus_internal</c> (the queue is never used).</summary>
    public bool IsPlaying(int sound) => _players.Any(p => p.IsActive && p.Id == (ushort)sound);

    /// <summary>The most any player's music timer has counted. Mirrors <c>getMusicTimer</c>.</summary>
    public int MusicTimer => _players.Where(p => p.IsActive).Select(p => p.MusicTimer).DefaultIfEmpty(0).Max();

    private Player? FindActivePlayer(int id) => _players.FirstOrDefault(p => p.IsActive && p.Id == (ushort)id);

    private Player? AllocatePlayer(byte priority)
    {
        Player? best = null;
        byte bestpri = 255;
        foreach (var player in _players)
        {
            if (!player.IsActive)
            {
                return player;
            }

            if (player.Priority < bestpri)
            {
                best = player;
                bestpri = player.Priority;
            }
        }

        return bestpri < priority ? best : null;
    }

    /// <summary>The volume of a volume channel, or with none half the music volume. Mirrors <c>get_channel_volume</c>.</summary>
    public int GetChannelVolume(uint a) => a < 8 ? _channelVolumeEff[a] : (_masterVolume * _musicVolumeEff / 255) / 2;

    private void UpdateVolumes()
    {
        foreach (var player in _players)
        {
            if (player.IsActive)
            {
                player.SetVolume(player.Volume);
            }
        }
    }

    /// <summary>A free part, or the one of lowest priority at or below <paramref name="pri"/>, taken from its player. Mirrors <c>allocate_part</c>.</summary>
    public Part? AllocatePart(byte pri)
    {
        Part? best = null;
        foreach (var part in _parts)
        {
            if (part.Player is null)
            {
                return part;
            }

            if (pri >= part.PriEff)
            {
                pri = part.PriEff;
                best = part;
            }
        }

        if (best is not null)
        {
            best.Uninit();
            ReallocateMidiChannels();
        }

        return best;
    }

    /// <summary>
    /// Gives the parts that are on and have no channel the driver's free channels, the highest
    /// priority first, taking one from a part of lower priority when none is free. Mirrors
    /// <c>reallocateMidiChannels</c>.
    /// </summary>
    public void ReallocateMidiChannels()
    {
        while (true)
        {
            byte hipri = 0;
            Part? hipart = null;
            foreach (var part in _parts)
            {
                if (part.Player is not null && !part.Percussion && part.On && part.Mc is null && part.PriEff >= hipri)
                {
                    hipri = part.PriEff;
                    hipart = part;
                }
            }

            if (hipart is null)
            {
                return;
            }

            if ((hipart.Mc = _midi.AllocateChannel()) is null)
            {
                byte lopri = 255;
                Part? lopart = null;
                foreach (var part in _parts)
                {
                    if (part.Mc is not null && part.PriEff <= lopri)
                    {
                        lopri = part.PriEff;
                        lopart = part;
                    }
                }

                if (lopart is null || lopri >= hipri)
                {
                    return;
                }

                lopart.Off(true);
                if ((hipart.Mc = _midi.AllocateChannel()) is null)
                {
                    return;
                }
            }

            hipart.SendAll();
        }
    }

    /// <summary>A part waiting for a channel takes this one. Mirrors <c>reassignChannelAndResumePart</c>.</summary>
    public bool ReassignChannelAndResumePart(MidiChannel mc)
    {
        while (_waitingPartsQueue.Count > 0)
        {
            var part = _waitingPartsQueue[0];
            _waitingPartsQueue.RemoveAt(0);
            if (part.Player is not null)
            {
                part.Mc = mc;
                part.SendAll();
                return true;
            }
        }

        return false;
    }

    public void RemoveSuspendedPart(Part part) => _waitingPartsQueue.Remove(part);
}

/// <summary>
/// An instrument a part plays: here always a program of the MT-32's (<c>Instrument_Program</c>,
/// the sound being the MT-32's and the module an MT-32, so the number goes as it is).
/// </summary>
internal sealed class Instrument
{
    private byte _program = 255;

    public bool IsValid => _program < 128;

    public void Clear() => _program = 255;

    /// <summary>Mirrors <c>Instrument::program</c>.</summary>
    public void Program(byte program) => _program = program > 127 ? (byte)255 : program;

    public void Send(MidiChannel mc)
    {
        if (_program < 128)
        {
            mc.ProgramChange(_program);
        }
    }
}

/// <summary>
/// A sound playing: its music read by a parser, its channels played on parts, with its own
/// priority, volume, pan, transposition and speed. Mirrors iMuse's <c>Player</c>
/// (<c>imuse_player.cpp</c>) for RO music; scanning ahead, jumps and loops, which only the
/// scripts' commands ask for, are left out.
/// </summary>
internal sealed class Player(IMuseEngine se) : IMidiReceiver
{
    private MidiParserRo? _parser;
    private Part? _parts;
    private sbyte _pan;
    private sbyte _transpose;
    private short _detune;
    private int _noteOffset;
    private byte _speed = 128;
    private uint _transitionTimer;
    private uint _volChan;

    public bool IsActive { get; private set; }

    public ushort Id { get; private set; }

    public byte Priority { get; private set; }

    public byte Volume { get; private set; }

    public byte EffectiveVolume { get; private set; }

    public sbyte Pan => _pan;

    public sbyte Transpose => _transpose;

    public short Detune => _detune;

    public bool IsMt32 => true;

    public bool IsMidi => true;

    /// <summary>The music timer: the parser's place in half beats, for RO the markers passed. Mirrors <c>getMusicTimer</c>.</summary>
    public int MusicTimer => _parser is null ? 0 : (int)(_parser.Tick * 2 / _parser.Ppqn);

    /// <summary>Mirrors <c>startSound</c>, <c>loadStartParameters</c> and <c>start_seq_sound</c> for an RO sound, which has no start parameters of its own.</summary>
    public bool StartSound(int sound, byte[] data)
    {
        _parts = null;
        IsActive = true;
        Id = (ushort)sound;

        Priority = 0x80;
        Volume = 0x7F;
        _volChan = 0xFFFF;
        EffectiveVolume = (byte)((se.GetChannelVolume(0xFFFF) << 7) >> 7);
        _pan = 0;
        _transpose = 0;
        _detune = 0;

        _parser ??= new MidiParserRo();
        _parser.SetMidiDriver(this);
        _parser.SmartJump = true;
        _parser.LoadMusic(data, 0);
        _parser.SetTrack(0);
        SetSpeed(128);
        return true;
    }

    public void SetOffsetNote(int offset) => _noteOffset = offset;

    /// <summary>Stops the sound: its parts let go. Mirrors <c>clear</c>.</summary>
    public void Clear()
    {
        if (!IsActive)
        {
            return;
        }

        if (_parser is not null)
        {
            _parser.UnloadMusic();
            _parser.SetMidiDriver(null);
        }

        UninitParts();
        IsActive = false;
        Id = 0;
        _noteOffset = 0;
        _speed = 128;
    }

    private void UninitParts()
    {
        while (_parts is not null)
        {
            _parts.Uninit();
        }

        se.ReallocateMidiChannels();
    }

    /// <summary>The parser's timer rate for a speed, 128 being the music's own. Mirrors <c>setSpeed</c>.</summary>
    private void SetSpeed(byte speed)
    {
        _speed = speed;
        _parser?.SetTimerRate((uint)((((se.Midi.BaseTempo * speed) >> 7) * se.TempoFactor) / 100));
    }

    /// <summary>A channel message of the music to its part. Mirrors <c>Player::send</c>.</summary>
    public void Send(uint b)
    {
        byte cmd = (byte)(b & 0xF0);
        byte chan = (byte)(b & 0x0F);
        byte param1 = (byte)((b >> 8) & 0xFF);
        byte param2 = (byte)((b >> 16) & 0xFF);
        Part? part;
        switch (cmd >> 4)
        {
            case 0x8:
                GetPart(chan)?.NoteOff(param1);
                break;

            case 0x9:
                param1 += (byte)_noteOffset;
                GetPart(chan)?.NoteOn(param1, param2);
                break;

            case 0xB:
                part = param1 == 123 ? GetActivePart(chan) : GetPart(chan);
                if (part is null)
                {
                    break;
                }

                switch (param1)
                {
                    case 0:
                        break;
                    case 1:
                        part.ModulationWheel(param2);
                        break;
                    case 7:
                        part.SetVolume(param2);
                        break;
                    case 10:
                        part.SetPan((sbyte)(param2 - 0x40));
                        break;
                    case 16:
                        part.PitchBendFactor(param2);
                        break;
                    case 17:
                        part.SetDetune((sbyte)(param2 - 0x40));
                        break;
                    case 18:
                        part.SetPri((sbyte)(param2 - 0x40));
                        se.ReallocateMidiChannels();
                        break;
                    case 64:
                        part.Sustain(param2 != 0);
                        break;
                    case 91:
                        part.EffectLevel(param2);
                        break;
                    case 93:
                        part.ChorusLevel(param2);
                        break;
                    case 116 or 117:
                        break;
                    case 123:
                        part.AllNotesOff();
                        break;
                    default:
                        throw new InvalidDataException($"Player::send(): Invalid control change {param1}");
                }

                break;

            case 0xC:
                part = GetPart(chan);
                if (part is not null && param1 < 128)
                {
                    part.ProgramChange(param1);
                }

                break;

            case 0xE:
                GetPart(chan)?.PitchBend((short)(((param2 << 7) | param1) - 0x2000));
                break;

            case 0xA or 0xD or 0xF:
                break;

            default:
                Clear();
                throw new InvalidDataException($"Player::send(): Invalid command {cmd}");
        }
    }

    /// <summary>An RO sound has no system exclusive messages.</summary>
    public ushort SysExNoDelay(ReadOnlySpan<byte> message) => 0;

    /// <summary>The end of the music stops the sound. Mirrors <c>metaEvent</c>.</summary>
    public void MetaEvent(byte type, ReadOnlySpan<byte> data)
    {
        if (type == 0x2F)
        {
            Clear();
        }
    }

    private Part? GetActivePart(byte chan)
    {
        for (var part = _parts; part is not null; part = part.Next)
        {
            if (part.Chan == chan)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>The part playing a channel, taken when there is none. Mirrors <c>getPart</c>.</summary>
    private Part? GetPart(byte chan)
    {
        if (GetActivePart(chan) is { } active)
        {
            return active;
        }

        var part = se.AllocatePart(Priority);
        if (part is null)
        {
            return null;
        }

        part.Prev = null;
        part.Next = _parts;
        if (_parts is not null)
        {
            _parts.Prev = part;
        }

        _parts = part;
        part.Chan = chan;
        part.Setup(this);
        return part;
    }

    public void RemovePart(Part part)
    {
        if (part.Next is not null)
        {
            part.Next.Prev = part.Prev;
        }

        if (part.Prev is not null)
        {
            part.Prev.Next = part.Next;
        }
        else
        {
            _parts = part.Next;
        }

        part.Next = part.Prev = null;
    }

    /// <summary>Mirrors <c>setVolume</c>: the player's effective volume from its volume channel, and its parts' again.</summary>
    public int SetVolume(byte vol)
    {
        if (vol > 127)
        {
            return -1;
        }

        Volume = vol;
        EffectiveVolume = (byte)(se.GetChannelVolume(_volChan) * (vol + 1) >> 7);
        for (var part = _parts; part is not null; part = part.Next)
        {
            part.SetVolume(part.Vol);
        }

        return 0;
    }

    /// <summary>A tick: the faders' time passes, and the music moves on. Mirrors <c>Player::onTimer</c>.</summary>
    public void OnTimer()
    {
        // transitionParameters: with no faders, only their clock
        _transitionTimer += se.Midi.BaseTempo;
        while (_transitionTimer >= 16667)
        {
            _transitionTimer -= 16667;
        }

        if (!IsActive || _parser is null)
        {
            return;
        }

        _parser.OnTimer();
    }
}

/// <summary>
/// A channel of a sound being played: its volume, pan, transposition, pitch bend and instrument,
/// sent to the MIDI channel the engine gives it, or to the rhythm part. Mirrors iMuse's
/// <c>Part</c> (<c>imuse_part.cpp</c>) for the old system and a native MT-32; a part's own
/// transposition, which only the scripts' commands set, is left out.
/// </summary>
internal sealed class Part(IMuseEngine se, int slot)
{
    private short _pitchbend;
    private byte _pitchbendFactor;
    private sbyte _transposeEff;
    private byte _volEff;
    private sbyte _detune;
    private short _detuneEff;
    private sbyte _pan;
    private sbyte _panEff;
    private byte _modwheel;
    private bool _pedal;
    private sbyte _pri;
    private byte _effectLevel;
    private byte _chorus;
    private readonly Instrument _instrument = new();

    public int Slot => slot;

    public Part? Next { get; set; }

    public Part? Prev { get; set; }

    public MidiChannel? Mc { get; set; }

    public Player? Player { get; private set; }

    public byte Chan { get; set; }

    public byte Vol { get; private set; }

    public byte PriEff { get; private set; }

    public bool On { get; private set; }

    public bool Percussion { get; private set; }

    public void SetDetune(sbyte detune)
    {
        _detuneEff = (short)Math.Clamp((_detune = detune) + Player!.Detune, -128, 127);
        SendDetune();
    }

    public void PitchBend(short value)
    {
        _pitchbend = value;
        SendPitchBend();
    }

    public void SetVolume(byte value)
    {
        Vol = value;
        SendVolume(0);
    }

    public void SetPri(sbyte pri)
    {
        PriEff = (byte)Math.Clamp((_pri = pri) + Player!.Priority, 0, 255);
        Mc?.Priority(PriEff);
    }

    public void SetPan(sbyte pan)
    {
        _panEff = (sbyte)Math.Clamp((_pan = pan) + Player!.Pan, -64, 63);
        SendPanPosition((byte)(_panEff + 0x40));
    }

    public void Sustain(bool value)
    {
        _pedal = value;
        Mc?.Sustain(value);
    }

    public void ModulationWheel(byte value)
    {
        _modwheel = value;
        Mc?.ModulationWheel(value);
    }

    public void ChorusLevel(byte value)
    {
        _chorus = value;
        Mc?.ChorusLevel(value);
    }

    public void EffectLevel(byte value)
    {
        _effectLevel = value;
        SendEffectLevel(value);
    }

    public void PitchBendFactor(byte value)
    {
        if (value > 12)
        {
            return;
        }

        PitchBend(0);
        _pitchbendFactor = value;
        Mc?.PitchBendFactor(value);
    }

    /// <summary>A note on, on the part's channel when it has an instrument, or on the rhythm part. Mirrors <c>Part::noteOn</c>.</summary>
    public void NoteOn(byte note, byte velocity)
    {
        if (!On)
        {
            return;
        }

        var mc = Mc;
        if (mc is not null && _instrument.IsValid)
        {
            mc.NoteOn(note, velocity);
        }
        else if (Percussion)
        {
            mc = se.Midi.PercussionChannel;
            if (_volEff != se.RhyState.Vol)
            {
                mc.Volume(_volEff);
            }

            se.RhyState = (_volEff, 1, PriEff);
            mc.NoteOn(note, velocity);
        }
    }

    public void NoteOff(byte note)
    {
        if (!On)
        {
            return;
        }

        if (Mc is { } mc)
        {
            mc.NoteOff(note);
        }
        else if (Percussion)
        {
            se.Midi.PercussionChannel.NoteOff(note);
        }
    }

    /// <summary>The part taken for a channel of a player, at its defaults. Mirrors <c>setup</c>.</summary>
    public void Setup(Player player)
    {
        Player = player;
        Percussion = player.IsMidi && Chan == 9;
        On = true;
        PriEff = player.Priority;
        _pri = 0;
        Vol = 127;
        _volEff = player.EffectiveVolume;
        _pan = (sbyte)Math.Clamp((int)player.Pan, -64, 63);
        _transposeEff = player.Transpose;
        _detune = 0;
        _detuneEff = player.Detune;
        _pitchbendFactor = 2;
        _pitchbend = 0;
        _effectLevel = (byte)(se.IsNativeMt32 ? 127 : 64);
        _instrument.Clear();
        _chorus = 0;
        _modwheel = 0;
        _pedal = false;
        Mc = null;
    }

    /// <summary>The part let go: its notes off, its channel to a part waiting or freed. Mirrors <c>uninit</c>.</summary>
    public void Uninit()
    {
        if (Player is null)
        {
            return;
        }

        Off(false);
        if (Mc is not null && !se.ReassignChannelAndResumePart(Mc))
        {
            Mc.Release();
        }

        Mc = null;
        Player.RemovePart(this);
        se.RemoveSuspendedPart(this);
        Player = null;
    }

    public void Off(bool releaseChannel)
    {
        if (Mc is { } mc)
        {
            mc.Sustain(false);
            mc.AllNotesOff();
            if (!releaseChannel)
            {
                return;
            }

            mc.Release();
            Mc = null;
        }
    }

    /// <summary>Whether the part has a channel to send to; one with an instrument asks for one. Mirrors <c>clearToTransmit</c>.</summary>
    private bool ClearToTransmit()
    {
        if (Mc is not null)
        {
            return true;
        }

        if (_instrument.IsValid)
        {
            se.ReallocateMidiChannels();
        }

        return false;
    }

    /// <summary>Everything the part has, to its new channel. Mirrors <c>sendAll</c>.</summary>
    public void SendAll()
    {
        if (!ClearToTransmit())
        {
            return;
        }

        var mc = Mc!;
        mc.PitchBendFactor(_pitchbendFactor);
        SendTranspose();
        SendDetune();
        SendPitchBend();
        mc.Volume(_volEff);
        mc.Sustain(_pedal);
        mc.ModulationWheel(_modwheel);
        SendPanPosition((byte)(_panEff + 0x40));
        if (_instrument.IsValid)
        {
            _instrument.Send(mc);
        }

        // the effect level after the instrument, or the MT-32's reverb setting would be overwritten
        SendEffectLevel(_effectLevel);
        mc.ChorusLevel(_chorus);
        mc.Priority(PriEff);
    }

    private void SendPitchBend() => Mc?.PitchBend(_pitchbend);

    private void SendVolume(sbyte fadeModifier)
    {
        ushort vol = (ushort)((Vol + fadeModifier + 1) * Player!.EffectiveVolume);
        _volEff = (byte)(vol >> 7);
        Mc?.Volume(_volEff);
    }

    private void SendTranspose() => Mc?.Transpose(_transposeEff);

    private void SendDetune() => Mc?.Detune(_detuneEff);

    public void ProgramChange(byte value)
    {
        _instrument.Program(value);
        if (ClearToTransmit())
        {
            _instrument.Send(Mc!);
        }
    }

    public void AllNotesOff() => Mc?.AllNotesOff();

    /// <summary>The pan, reversed for a native MT-32 as the original driver reverses it (bug #1849). Mirrors <c>sendPanPosition</c>.</summary>
    private void SendPanPosition(byte value)
    {
        if (Mc is null)
        {
            return;
        }

        if (se.IsNativeMt32)
        {
            value = (byte)(127 - value);
        }

        Mc.PanPosition(value);
    }

    private void SendEffectLevel(byte value) => Mc?.EffectLevel(value);
}
