using Distaff.Scumm.Saves;

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
/// messages and Roland instruments (an RO sound has no sysex); the AdLib and other drivers. What
/// a save keeps of what is left out (the queue, triggers, hooks, faders, loops) is kept as read.
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

    // what a save keeps that Loom never changes: the command queue and its triggers, the volume
    // channels' table, the queued commands and Sam & Max's triggers (init_queue, the constructor)
    private int _queueEnd, _queuePos, _queueSound, _queueAdding, _queueMarker, _queueCleared, _triggerCount, _snmTriggerIndex;
    private readonly int[] _volchanTable = new int[8];
    private readonly int[] _cmdQueue = new int[64 * 8];
    private readonly int[] _snmTriggers = new int[16 * SnmTriggerFields];

    // ImTrigger: its sound, id, expiry and 8 commands
    private const int SnmTriggerFields = 3 + 8;

    public IMuseEngine(IMuseDriverMt32 midi)
    {
        _midi = midi;
        TempoFactor = 100;
        _masterVolume = 255;
        for (int i = 0; i != 8; i++)
        {
            _channelVolume[i] = _channelVolumeEff[i] = 127;
            _volchanTable[i] = 127;
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

    /// <summary>A part by its place, 1 based as a save numbers them, 0 for none.</summary>
    public Part? PartAt(int number) => number == 0 ? null : _parts[number - 1];

    /// <summary>A player by its place, 1 based as a save numbers them, 0 for none.</summary>
    public Player? PlayerAt(int number) => number == 0 ? null : _players[number - 1];

    /// <summary>A player's place, 1 based, 0 for none.</summary>
    public int NumberOf(Player? player) => player is null ? 0 : Array.IndexOf(_players, player) + 1;

    /// <summary>
    /// Reads or writes iMuse's state: the queue, the volumes, the players, the parts and their
    /// instruments; after a read, the players' sounds started again where they were (each sound
    /// from <paramref name="sound"/>, as iMuse finds it in the resources), the parts set up again,
    /// and channels given out. Mirrors <c>saveLoadIMuse</c>, <c>fix_players_after_load</c> and
    /// <c>fix_parts_after_load</c>.
    /// </summary>
    public void SaveLoad(SaveSerializer s, Func<int, byte[]?> sound)
    {
        s.Byte(ref _queueEnd, 8);
        s.Byte(ref _queuePos, 8);
        s.Uint16(ref _queueSound, 8);
        s.Byte(ref _queueAdding, 8);
        s.Byte(ref _queueMarker, 8);
        s.Byte(ref _queueCleared, 8);
        int master = _masterVolume;
        s.Byte(ref master, 8);
        _masterVolume = (byte)master;
        s.Uint16(ref _triggerCount, 8);
        s.Uint16(ref _snmTriggerIndex, 54);
        for (int i = 0; i < _channelVolume.Length; i++)
        {
            int v = _channelVolume[i];
            s.Uint16(ref v, 8);
            _channelVolume[i] = (ushort)v;
        }

        s.Array(_volchanTable, 8, (x, i) => x.Uint16(ref _volchanTable[i]), 8);
        s.Array(_cmdQueue, _cmdQueue.Length, (x, i) => x.Uint16(ref _cmdQueue[i], 23));
        for (int t = 0; t < _snmTriggers.Length; t += SnmTriggerFields)
        {
            s.Int16(ref _snmTriggers[t], 54);
            s.Byte(ref _snmTriggers[t + 1], 54);
            s.Uint16(ref _snmTriggers[t + 2], 54);
            s.Array(_snmTriggers, 8, (x, i) => x.Uint16(ref _snmTriggers[t + 3 + i]), 54);
        }

        foreach (var player in _players)
        {
            player.SaveLoad(s);
        }

        foreach (var part in _parts)
        {
            part.SaveLoad(s);
        }

        // the instruments, as they are from version 11
        foreach (var part in _parts)
        {
            part.Instrument.SaveLoad(s);
        }

        // the volume faders before version 17
        s.Skip(13 * 8, 8, 16);

        if (s.IsLoading)
        {
            foreach (var player in _players)
            {
                if (player.IsActive)
                {
                    player.FixAfterLoad(sound);
                }
            }

            foreach (var part in _parts)
            {
                if (part.Player is not null)
                {
                    part.FixAfterLoad();
                }
            }

            SetImuseMasterVolume(_masterVolume);
            ReallocateMidiChannels();
        }
    }

    /// <summary>Mirrors <c>setImuseMasterVolume</c>.</summary>
    private void SetImuseMasterVolume(int vol)
    {
        vol = Math.Min(vol, 255);
        if (_masterVolume == vol)
        {
            return;
        }

        _masterVolume = (byte)vol;
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
}

/// <summary>
/// An instrument a part plays: none, or a program (<c>Instrument_Program</c>), for an RO sound one
/// of the MT-32's, and the module an MT-32, so the number goes as it is. Mapping a program between
/// the MT-32's and General MIDI, which a save made for another module could ask for, is left out.
/// </summary>
internal sealed class Instrument
{
    // Instrument's itNone and itProgram
    private const int TypeNone = 0, TypeProgram = 1;

    private int _type = TypeNone;
    private int _program = 255;
    private int _bank;
    private bool _soundTypeMt32;
    private bool _nativeMt32Device = true;

    public bool IsValid => _type == TypeProgram && _program < 128;

    public void Clear() => _type = TypeNone;

    /// <summary>Mirrors <c>Instrument::program</c>.</summary>
    public void Program(byte program, byte bank, bool mt32SoundType)
    {
        Clear();
        if (program > 127)
        {
            return;
        }

        _type = TypeProgram;
        (_program, _bank, _soundTypeMt32, _nativeMt32Device) = (program, bank, mt32SoundType, true);
    }

    public void Send(MidiChannel mc)
    {
        if (IsValid)
        {
            mc.ProgramChange((byte)_program);
        }
    }

    /// <summary>
    /// Its type, then a program's number, bank and kinds of sound and module. Mirrors
    /// <c>Instrument::saveLoadWithSerializer</c> and <c>Instrument_Program</c>'s; the module's kind
    /// is read from version 122, and written as it is read.
    /// </summary>
    public void SaveLoad(SaveSerializer s)
    {
        if (s.IsLoading)
        {
            Clear();
            (_program, _bank, _soundTypeMt32, _nativeMt32Device) = (255, 0, false, true);
        }

        s.Byte(ref _type);
        switch (_type)
        {
            case TypeNone:
                break;
            case TypeProgram:
                s.Byte(ref _program);
                s.Byte(ref _bank, 123);
                s.Bool(ref _soundTypeMt32);
                s.Bool(ref _nativeMt32Device, 122);
                break;
            default:
                throw new InvalidDataException($"An iMuse instrument of type {_type}, which an RO sound does not have.");
        }
    }
}

/// <summary>
/// A fader of a player's volume, transposition or speed, which only the scripts' commands set:
/// here only kept for a save. Mirrors <c>ParameterFader</c>.
/// </summary>
internal sealed class ParameterFader
{
    public int Param, Dir, Incr, Ifrac, Irem, Ttime, Cntdwn, State;

    /// <summary>
    /// Mirrors its <c>syncWithSerializer</c>: before version 116 a fader was its start, end and
    /// times, turned into steps when read, and is written so (an idle one, all 0).
    /// </summary>
    public void SaveLoad(SaveSerializer s)
    {
        s.Int16(ref Param, 17);
        if (s.Version < 116)
        {
            int start = 0, end = 0, totalTime = 0, currentTime = 0;
            s.Int16(ref start, 17);
            s.Int16(ref end, 17);
            s.Uint32(ref totalTime, 17);
            s.Uint32(ref currentTime, 17);
            if (s.IsSaving)
            {
                return;
            }

            int diff = end - start;
            uint tt = (uint)totalTime, ct = (uint)currentTime;
            if (Param != 0 && diff != 0 && tt != 0)
            {
                if (tt < 10000)
                {
                    tt = 10000;
                    ct = (uint)(tt - diff);
                }

                Dir = diff / Math.Abs(diff);
                Incr = (short)(diff / (int)(tt / 10000));
                Ifrac = (ushort)(Math.Abs(diff) % (int)(tt / 10000));
                State = (short)(start + diff * (int)ct / (int)tt);
            }
            else
            {
                Param = 0;
            }

            Irem = 0;
            Cntdwn = 0;
            return;
        }

        s.SByte(ref Dir, 116);
        s.Int16(ref Incr, 116);
        s.Uint16(ref Ifrac, 116);
        s.Uint16(ref Irem, 116);
        s.Uint16(ref Ttime, 116);
        s.Uint16(ref Cntdwn, 116);
        s.Int16(ref State, 116);
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

    // what a save keeps that Loom never changes: the track, the loop, the hooks and the faders
    private int _trackIndex, _loopToBeat, _loopFromBeat, _loopCounter, _loopToTick, _loopFromTick;
    private int _hookJump, _hookTranspose;
    private readonly byte[] _hookPartOnOff = new byte[16], _hookPartVolume = new byte[16], _hookPartProgram = new byte[16], _hookPartTranspose = new byte[16];
    private readonly ParameterFader[] _parameterFaders = [new(), new(), new(), new()];
    private int _musicTick;

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

        foreach (var fader in _parameterFaders)
        {
            fader.Param = 0;
        }

        HookClear();

        // start_seq_sound, its variables reset
        (_loopToBeat, _loopFromBeat, _trackIndex, _loopCounter, _loopToTick, _loopFromTick) = (1, 1, 0, 0, 0, 0);
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

    /// <summary>Mirrors <c>hook_clear</c>.</summary>
    private void HookClear()
    {
        (_hookJump, _hookTranspose) = (0, 0);
        Array.Clear(_hookPartOnOff);
        Array.Clear(_hookPartVolume);
        Array.Clear(_hookPartProgram);
        Array.Clear(_hookPartTranspose);
    }

    /// <summary>
    /// Reads or writes the player: its first part, its sound and how it plays, its place in the
    /// music (the parser's tick, for RO counted in markers), the hooks and the faders. A read
    /// leaves the parser to <see cref="FixAfterLoad"/>. Mirrors <c>Player::saveLoadWithSerializer</c>.
    /// </summary>
    public void SaveLoad(SaveSerializer s)
    {
        if (s.IsLoading)
        {
            _parser = null;
        }

        _musicTick = _parser is null ? 0 : (int)_parser.Tick;
        int num = _parts is null ? 0 : _parts.Slot + 1;
        s.Uint16(ref num);
        if (s.IsLoading)
        {
            _parts = se.PartAt(num);
        }

        bool active = IsActive;
        s.Bool(ref active, 8);
        IsActive = active;
        int id = Id, priority = Priority, volume = Volume, pan = _pan, transpose = _transpose, detune = _detune;
        int volChan = (int)_volChan, volEff = EffectiveVolume, speed = _speed;
        s.Uint16(ref id, 8);
        s.Byte(ref priority, 8);
        s.Byte(ref volume, 8);
        s.SByte(ref pan, 8);
        s.Byte(ref transpose, 8);
        s.SByte(ref detune, 8, 115);
        s.Int16(ref detune, 116);
        s.Uint16(ref volChan, 8);
        s.Byte(ref volEff, 8);
        s.Byte(ref speed, 8);
        (Id, Priority, Volume, _pan, _transpose, _detune) = ((ushort)id, (byte)priority, (byte)volume, (sbyte)pan, (sbyte)transpose, (short)detune);
        (_volChan, EffectiveVolume, _speed) = ((uint)(ushort)volChan, (byte)volEff, (byte)speed);

        s.Skip(2, 8, 19);
        s.Uint16(ref _trackIndex, 8);
        s.Skip(2, 8, 17);
        s.Uint16(ref _loopToBeat, 8);
        s.Uint16(ref _loopFromBeat, 8);
        s.Uint16(ref _loopCounter, 8);
        s.Uint16(ref _loopToTick, 8);
        s.Uint16(ref _loopFromTick, 8);
        s.Skip(4, 8, 19);
        s.Skip(4, 8, 17);
        s.Skip(4, 8, 17);
        s.Skip(4, 8, 17);
        s.Skip(2, 8, 17);
        s.Skip(2, 8, 17);
        s.Skip(2, 8, 17);
        s.Uint32(ref _musicTick, 19);
        s.Byte(ref _hookJump, 8);
        s.Byte(ref _hookTranspose, 8);
        s.Bytes(_hookPartOnOff, 16, 8);
        s.Bytes(_hookPartVolume, 16, 8);
        s.Bytes(_hookPartProgram, 16, 8);
        s.Bytes(_hookPartTranspose, 16, 8);
        foreach (var fader in _parameterFaders)
        {
            fader.SaveLoad(s);
        }
    }

    /// <summary>
    /// The sound read again and the parser taken to where the save had it, its notes not played on
    /// the way, at the save's speed. Mirrors <c>fixAfterLoad</c> and <c>start_seq_sound</c> with
    /// its variables kept.
    /// </summary>
    public void FixAfterLoad(Func<int, byte[]?> sound)
    {
        if (sound(Id) is { } data)
        {
            _parser = new MidiParserRo();
            _parser.SetMidiDriver(this);
            _parser.SmartJump = true;
            _parser.LoadMusic(data, 0);
            _parser.SetTrack(_trackIndex);
            SetSpeed(_speed);
        }

        SetSpeed(_speed);
        _parser?.JumpToTick((uint)_musicTick);
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
/// transposition, which only the scripts' commands set, is only kept for a save.
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
    private sbyte _transpose;
    private byte _bank;
    private byte _polyphony;
    private byte _volControlSensitivity = 127;
    private readonly Instrument _instrument = new();

    public int Slot => slot;

    public Instrument Instrument => _instrument;

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
        _transpose = 0;
        _detune = 0;
        _detuneEff = player.Detune;
        _pitchbendFactor = 2;
        _volControlSensitivity = 127;
        _polyphony = 1;
        _pitchbend = 0;
        _effectLevel = (byte)(se.IsNativeMt32 ? 127 : 64);
        _instrument.Clear();
        _chorus = 0;
        _modwheel = 0;
        _bank = 0;
        _pedal = false;
        Mc = null;
    }

    /// <summary>
    /// Reads or writes the part: its neighbours and player, by number, and its settings. Mirrors
    /// <c>Part::saveLoadWithSerializer</c>; the instrument follows with the others'.
    /// </summary>
    public void SaveLoad(SaveSerializer s)
    {
        int next = Next is null ? 0 : Next.Slot + 1, prev = Prev is null ? 0 : Prev.Slot + 1, player = se.NumberOf(Player);
        s.Uint16(ref next);
        s.Uint16(ref prev);
        s.Uint16(ref player);
        if (s.IsLoading)
        {
            (Next, Prev, Player) = (se.PartAt(next), se.PartAt(prev), se.PlayerAt(player));
        }

        int pitchbend = _pitchbend, pitchbendFactor = _pitchbendFactor, transpose = _transpose, vol = Vol, detune = _detune, pan = _pan;
        bool on = On, pedal = _pedal, percussion = Percussion;
        int modwheel = _modwheel, pri = _pri, chan = Chan, effectLevel = _effectLevel, chorus = _chorus, bank = _bank;
        int polyphony = _polyphony, volControlSensitivity = _volControlSensitivity;
        s.Int16(ref pitchbend, 8);
        s.Byte(ref pitchbendFactor, 8);
        s.SByte(ref transpose, 8);
        s.Byte(ref vol, 8);
        s.SByte(ref detune, 8);
        s.SByte(ref pan, 8);
        s.Bool(ref on, 8);
        s.Byte(ref modwheel, 8);
        s.Bool(ref pedal, 8);
        s.Skip(1, 8, 16);
        s.Byte(ref pri, 8);
        s.Byte(ref chan, 8);
        s.Byte(ref effectLevel, 8);
        s.Byte(ref chorus, 8);
        s.Bool(ref percussion, 8);
        s.Byte(ref bank, 8);
        s.Byte(ref polyphony, 116);
        s.Byte(ref volControlSensitivity, 116);
        (_pitchbend, _pitchbendFactor, _transpose, Vol, _detune, _pan) = ((short)pitchbend, (byte)pitchbendFactor, (sbyte)transpose, (byte)vol, (sbyte)detune, (sbyte)pan);
        (On, _pedal, Percussion, _modwheel, _pri, Chan) = (on, pedal, percussion, (byte)modwheel, (sbyte)pri, (byte)chan);
        (_effectLevel, _chorus, _bank, _polyphony, _volControlSensitivity) = ((byte)effectLevel, (byte)chorus, (byte)bank, (byte)polyphony, (byte)volControlSensitivity);
    }

    /// <summary>
    /// The part's settings sent again with its player's: transposed within an octave either way,
    /// as for a native MT-32. Mirrors <c>fix_after_load</c> with dynamic channel allocation.
    /// </summary>
    public void FixAfterLoad()
    {
        SetTranspose(_transpose, -12, 12);
        SetVolume(Vol);
        SetDetune(_detune);
        SetPri(_pri);
        SetPan(_pan);
        SendAll();
    }

    /// <summary>Mirrors <c>set_transpose</c> (not for the Amiga).</summary>
    private void SetTranspose(sbyte transpose, int clipRangeLow, int clipRangeHi)
    {
        _transpose = transpose;
        _transposeEff = _transpose == -128 ? (sbyte)0 : (sbyte)TransposeClamp(_transpose + Player!.Transpose, clipRangeLow, clipRangeHi);
        SendTranspose();
    }

    /// <summary>Mirrors <c>transpose_clamp</c>: brought within the range by octaves.</summary>
    private static int TransposeClamp(int a, int b, int c)
    {
        if (b > a)
        {
            a += (b - a + 11) / 12 * 12;
        }

        if (c < a)
        {
            a -= (a - c + 11) / 12 * 12;
        }

        return a;
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
        _bank = 0;
        _instrument.Program(value, 0, Player!.IsMt32);
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
