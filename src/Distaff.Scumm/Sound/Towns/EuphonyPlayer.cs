namespace Distaff.Scumm.Sound.Towns;

/// <summary>
/// Plays a Euphony track, the FM Towns' music format: events of six bytes, each a MIDI-like
/// command for one of 32 parts with the bar and beat it falls on, parsed on timer A at the
/// track's tempo. A note's length follows it as an event of its own; the note off waits among
/// the hanging notes until its time. Mirrors ScummVM's <c>EuphonyPlayer</c>, with the timed
/// updates of its tempo control mode 0, the one ScummVM uses.
/// </summary>
/// <remarks>
/// Parts of type 0xff go to the FM and PCM driver (<see cref="EuphonyDriver"/>); type 0 to a
/// driver that does nothing (<c>Type0Driver</c>); no other is present, and bytes for one are dropped.
/// </remarks>
internal sealed class EuphonyPlayer : ITownsAudioDriver
{
    private static readonly ushort[] BeatLengthTable = [0x180, 0xC0, 0x80, 0x60, 0x40, 0x30, 0x20, 0x18];

    private readonly byte[] _partConfigEnable = new byte[32];
    private readonly byte[] _partConfigType = new byte[32];
    private readonly byte[] _partConfigOrdr = new byte[32];
    private readonly sbyte[] _partConfigVolume = new sbyte[32];
    private readonly sbyte[] _partConfigTranspose = new sbyte[32];

    private PendingEvent? _pendingEventsChain;

    private byte _defaultBarLength;
    private byte _barLength;
    private int _playerUpdatesLeft;
    private int _updatesPerPulseRemainder;
    private int _updatesPerPulse;
    private int _timerSetting;
    private sbyte _tempoMode1PulseCounter;
    private int _tempoModifier;
    private uint _bar;
    private uint _parseToBar;
    private sbyte _tempoMode1UpdateF8;
    private uint _beat;
    private byte _defaultTempo;
    private int _trackTempo;

    private bool _loop;
    private bool _endOfTrack;
    private bool _paused;

    private byte[] _music = [];
    private int _musicStart;
    private int _musicPos;
    private uint _musicTrackSize;

    /// <summary>Mirrors the constructor and <c>init</c>: the driver on the Towns' audio, and a reset.</summary>
    public EuphonyPlayer(TownsAudio audio)
    {
        Driver = new EuphonyDriver(audio);
        audio.Driver = this;
        ResetTempo();
        Reset();
    }

    /// <summary>The FM and PCM driver (<c>driver</c>).</summary>
    public EuphonyDriver Driver { get; }

    /// <summary>Whether a track plays (<c>isPlaying</c>).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Starts a track, given as its data and where in it the events start, from its first bar
    /// with the bar's length; 2, and nothing starts, while one plays. Mirrors <c>startTrack</c>.
    /// </summary>
    public int StartTrack(byte[] data, int start, int trackSize, int barLen)
    {
        if (IsPlaying)
        {
            return 2;
        }

        _music = data;
        _musicPos = _musicStart = start;
        _defaultBarLength = _barLength = (byte)barLen;
        _musicTrackSize = (uint)trackSize;
        _parseToBar = _bar = 0;
        _beat = 0;
        IsPlaying = true;
        return 0;
    }

    /// <summary>Mirrors <c>stop</c>: the hanging notes end and the parts' controls are reset.</summary>
    public void Stop()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            _playerUpdatesLeft = 0;
            _endOfTrack = false;
            ClearHangingNotes();
            ResetAllControls();
        }
    }

    /// <summary>Mirrors <c>resume</c>.</summary>
    public void Resume() => _paused = false;

    /// <summary>Mirrors <c>setTempo</c>: the track's tempo, up to 250.</summary>
    public int SetTempo(int tempo)
    {
        if (tempo > 250)
        {
            return 3;
        }

        _defaultTempo = (byte)tempo;
        _trackTempo = tempo;
        SendTempo(tempo);
        return 0;
    }

    /// <summary>Mirrors <c>setLoopStatus</c>.</summary>
    public void SetLoopStatus(bool loop) => _loop = loop;

    /// <summary>Whether a part plays, 0 or 0xff (<c>configPart_enable</c>).</summary>
    public int ConfigPartEnable(int part, int val)
    {
        byte enable = (byte)val;
        if (part > 31 || ((enable + 1) & 0xff) > 1)
        {
            return 3;
        }

        _partConfigEnable[part] = enable;
        return 0;
    }

    /// <summary>The driver a part's events go to (<c>configPart_setType</c>).</summary>
    public int ConfigPartSetType(int part, int val)
    {
        byte type = (byte)val;
        if (part > 31 || ((type + 1) & 0xff) > 8)
        {
            return 3;
        }

        _partConfigType[part] = type;
        return 0;
    }

    /// <summary>The MIDI channel a part's events are sent on, or 0xff for their own (<c>configPart_remap</c>).</summary>
    public int ConfigPartRemap(int part, int val)
    {
        byte remap = (byte)val;
        if (part > 31 || ((remap + 1) & 0xff) > 16)
        {
            return 3;
        }

        _partConfigOrdr[part] = remap;
        return 0;
    }

    /// <summary>What a part's velocities are moved by, -40 to 40 (<c>configPart_adjustVolume</c>).</summary>
    public int ConfigPartAdjustVolume(int part, int val)
    {
        sbyte adjvol = (sbyte)val;
        if (part > 31 || adjvol < -40 || adjvol > 40)
        {
            return 3;
        }

        _partConfigVolume[part] = adjvol;
        return 0;
    }

    /// <summary>What a part's notes are moved by, -40 to 40 (<c>configPart_setTranspose</c>).</summary>
    public int ConfigPartSetTranspose(int part, int val)
    {
        sbyte trans = (sbyte)val;
        if (part > 31 || trans < -40 || trans > 40)
        {
            return 3;
        }

        _partConfigTranspose[part] = trans;
        return 0;
    }

    /// <summary>Timer A runs the track: the pulses due, each a beat's step, the hanging notes and the events (<c>timerCallback</c>).</summary>
    public void TimerCallback(int timerId)
    {
        if (timerId != 0)
        {
            return;
        }

        UpdatePulseCounters();
        while (_playerUpdatesLeft != 0)
        {
            --_playerUpdatesLeft;
            UpdateBeat();
            if (!IsPlaying)
            {
                continue;
            }

            UpdateHangingNotes();
            UpdateParser();
            UpdateCheckEot();
        }
    }

    /// <summary>Mirrors <c>reset</c>: the driver, the timers (A stopped, B at 221), the parts, and the tempo.</summary>
    private void Reset()
    {
        Driver.Reset();
        Driver.SetTimerA(true, 1);
        Driver.SetTimerA(false, 1);
        Driver.SetTimerB(true, 221);

        ResetPartConfig();
        _pendingEventsChain = null;

        IsPlaying = _endOfTrack = _paused = _loop = false;
        _tempoMode1UpdateF8 = 0;
        _tempoMode1PulseCounter = 0;

        ResetTempo();
        SendTempo(_defaultTempo);
        ResetAllControls();
    }

    private void ResetPartConfig()
    {
        Array.Fill(_partConfigEnable, (byte)0xff);
        Array.Fill(_partConfigType, (byte)0xff, 0, 16);
        Array.Fill(_partConfigType, (byte)0, 16, 16);
        for (int i = 0; i < 32; i++)
        {
            _partConfigOrdr[i] = (byte)(i & 0x0f);
        }

        Array.Clear(_partConfigVolume);
        Array.Clear(_partConfigTranspose);
    }

    private void ResetTempo()
    {
        _defaultBarLength = _barLength = 0x33;
        _playerUpdatesLeft = 0;
        _updatesPerPulseRemainder = 0;
        _updatesPerPulse = 0x10;
        _tempoModifier = 0;
        _bar = 0;
        _beat = 0;
        _defaultTempo = 90;
        _trackTempo = 90;
    }

    /// <summary>Mirrors <c>updatePulseCounters</c>: the updates a timer tick is worth, in sixteenths.</summary>
    private void UpdatePulseCounters()
    {
        int tc = _updatesPerPulse + _updatesPerPulseRemainder;
        _updatesPerPulseRemainder = tc & 0x0f;
        tc >>= 4;
        _tempoMode1PulseCounter = (sbyte)(_tempoMode1PulseCounter - tc);

        while (_tempoMode1PulseCounter < 0)
        {
            _tempoMode1UpdateF8++;
            _tempoMode1PulseCounter += 4;
        }

        if (IsPlaying && !_paused)
        {
            _playerUpdatesLeft += tc;
        }
    }

    /// <summary>Mirrors <c>updateBeat</c>: the beat, and the bar when the bar's beats are done.</summary>
    private void UpdateBeat()
    {
        int beatsPerBar = (_barLength & 0x0f) + 1;
        int beatNoteValue = _barLength >> 4;

        if ((uint)(BeatLengthTable[beatNoteValue] * beatsPerBar) > ++_beat)
        {
            return;
        }

        ++_bar;
        _beat = 0;
    }

    /// <summary>Mirrors <c>updateParser</c>: the events up to the bar and beat reached.</summary>
    private void UpdateParser()
    {
        for (bool loop = true; loop;)
        {
            byte cmd = _music[_musicPos];

            if (cmd == 0xff || cmd == 0xf7)
            {
                ProceedToNextEvent();
            }
            else if (cmd < 0x90)
            {
                _endOfTrack = true;
                ClearHangingNotes();
                loop = false;
            }
            else if (_parseToBar > _bar)
            {
                loop = false;
            }
            else
            {
                if (_parseToBar == _bar)
                {
                    int parseToBeat = ((_music[_musicPos + 3] << 8) | ((_music[_musicPos + 2] << 1) & 0xff)) >> 1;
                    if (parseToBeat > _beat)
                    {
                        loop = false;
                    }
                }

                if (loop && ParseEvent())
                {
                    loop = false;
                }
            }
        }
    }

    private void UpdateCheckEot()
    {
        if (!_endOfTrack || _pendingEventsChain is not null)
        {
            return;
        }

        Stop();
    }

    /// <summary>
    /// Mirrors <c>parseEvent</c>: an event, or the track's end (0xfe), where it starts again when
    /// it loops; 0xfd pauses it. True when parsing stops for now.
    /// </summary>
    private bool ParseEvent()
    {
        int cmd = _music[_musicPos];
        if (cmd != 0xfe && cmd != 0xfd)
        {
            bool result = cmd >= 0xf0 ? SystemEvent((cmd - 0xf0) >> 1) : ChannelEvent((cmd - 0x80) >> 4);
            if (!result)
            {
                ProceedToNextEvent();
                return false;
            }
        }

        if (cmd == 0xfd)
        {
            _paused = true;
            return true;
        }

        if (!_loop)
        {
            _endOfTrack = true;
            return true;
        }

        _endOfTrack = false;
        _musicPos = _musicStart;
        _parseToBar = _bar = _beat = 0;
        _barLength = _defaultBarLength;
        return false;
    }

    /// <summary>The events 0x80 to 0xef by their high nibble (<c>_euphonyEvents</c> 0 to 6).</summary>
    private bool ChannelEvent(int index) => index switch
    {
        1 => EventNoteOn(),
        2 => EventPolyphonicAftertouch(),
        3 or 6 => EventControlChangePitchWheel(),
        4 or 5 => EventProgramChangeChannelAftertouch(),
        _ => false,
    };

    /// <summary>The events 0xf0 to 0xfd by pairs (<c>_euphonyEvents</c> 7 to 13).</summary>
    private bool SystemEvent(int index) => index switch
    {
        0 => EventSysex(),
        1 => EventAdvanceBar(),
        4 => EventSetTempo(),
        6 => EventTypeOrdrChange(),
        _ => false,
    };

    private void ProceedToNextEvent()
    {
        _musicPos += 6;
        if (_musicPos >= _musicStart + _musicTrackSize)
        {
            _musicPos = _musicStart;
        }
    }

    /// <summary>Mirrors <c>updateHangingNotes</c>: each waiting note off counts down, and is sent when it reaches 0.</summary>
    private void UpdateHangingNotes()
    {
        PendingEvent? l = null;
        var e = _pendingEventsChain;

        while (e is not null)
        {
            if (--e.Len != 0)
            {
                l = e;
                e = e.Next;
                continue;
            }

            var n = e.Next;
            if (l is not null)
            {
                l.Next = n;
            }

            if (_pendingEventsChain == e)
            {
                _pendingEventsChain = n;
            }

            SendPendingEvent(e.Type, e.Evt, e.Note, e.Velo);
            e = n;
        }
    }

    private void ClearHangingNotes()
    {
        while (_pendingEventsChain is { } e)
        {
            _pendingEventsChain = e.Next;
            SendPendingEvent(e.Type, e.Evt, e.Note, e.Velo);
        }
    }

    private void ResetAllControls()
    {
        for (int i = 0; i < 32; i++)
        {
            if (_partConfigOrdr[i] > 15)
            {
                for (int ii = 0; ii < 16; ii++)
                {
                    SendControllerReset(_partConfigType[i], ii);
                }
            }
            else
            {
                SendControllerReset(_partConfigType[i], _partConfigOrdr[i]);
            }
        }
    }

    /// <summary>An event's status byte on the channel its part is remapped to (<c>appendEvent</c>).</summary>
    private byte AppendEvent(byte evt, byte chan)
    {
        if (evt >= 0x80 && evt < 0xf0 && _partConfigOrdr[chan] < 16)
        {
            return (byte)((evt & 0xf0) | _partConfigOrdr[chan]);
        }

        return evt;
    }

    /// <summary>
    /// Mirrors <c>event_noteOn</c>: the note goes out now; the next event holds its length and
    /// note-off velocity, and the note off waits among the hanging notes.
    /// </summary>
    private bool EventNoteOn()
    {
        int p = _musicPos;
        if (_music[p + 1] > 31)
        {
            return false;
        }

        if (_partConfigEnable[_music[p + 1]] == 0)
        {
            ProceedToNextEvent();
            return _music[_musicPos] == 0xfe || _music[_musicPos] == 0xfd;
        }

        byte evt = AppendEvent(_music[p], _music[p + 1]);
        byte type = _partConfigType[_music[p + 1]];
        byte note = _music[p + 4];
        byte velo = _music[p + 5];

        SendByte(type, evt);
        SendByte(type, ApplyTranspose(note));
        SendByte(type, ApplyVolumeAdjust(velo));

        ProceedToNextEvent();
        p = _musicPos;
        if (_music[p] == 0xfe || _music[p] == 0xfd)
        {
            return true;
        }

        velo = _music[p + 5];
        int len = (_music[p + 1] & 0x0f) | ((_music[p + 2] & 0x0f) << 4) | ((_music[p + 3] & 0x0f) << 8) | ((_music[p + 4] & 0x0f) << 12);

        _pendingEventsChain = new PendingEvent(evt, type, note, velo, (ushort)(len != 0 ? len : 1), _pendingEventsChain);
        return false;
    }

    private bool EventPolyphonicAftertouch()
    {
        int p = _musicPos;
        if (_music[p + 1] > 31 || _partConfigEnable[_music[p + 1]] == 0)
        {
            return false;
        }

        byte evt = AppendEvent(_music[p], _music[p + 1]);
        byte type = _partConfigType[_music[p + 1]];

        SendByte(type, evt);
        SendByte(type, ApplyTranspose(_music[p + 4]));
        SendByte(type, _music[p + 5]);
        return false;
    }

    private bool EventControlChangePitchWheel()
    {
        int p = _musicPos;
        if (_music[p + 1] > 31 || _partConfigEnable[_music[p + 1]] == 0)
        {
            return false;
        }

        byte evt = AppendEvent(_music[p], _music[p + 1]);
        byte type = _partConfigType[_music[p + 1]];

        SendByte(type, evt);
        SendByte(type, _music[p + 4]);
        SendByte(type, _music[p + 5]);
        return false;
    }

    private bool EventProgramChangeChannelAftertouch()
    {
        int p = _musicPos;
        if (_music[p + 1] > 31 || _partConfigEnable[_music[p + 1]] == 0)
        {
            return false;
        }

        byte evt = AppendEvent(_music[p], _music[p + 1]);
        byte type = _partConfigType[_music[p + 1]];

        SendByte(type, evt);
        SendByte(type, _music[p + 4]);
        return false;
    }

    /// <summary>Mirrors <c>event_sysex</c>: the bytes of the events after it, up to a status byte.</summary>
    private bool EventSysex()
    {
        byte type = _partConfigType[_music[_musicPos + 1]];
        SendByte(type, 0xF0);
        ProceedToNextEvent();

        for (bool loop = true; loop;)
        {
            for (int i = 0; i < 6; i++)
            {
                byte b = _music[_musicPos + i];
                if (b != 0xFF)
                {
                    SendByte(type, b);
                    if (b >= 0x80)
                    {
                        loop = false;
                        break;
                    }
                }
            }

            if (loop)
            {
                ProceedToNextEvent();
            }
        }

        return false;
    }

    private bool EventAdvanceBar()
    {
        ++_parseToBar;
        _barLength = _music[_musicPos + 1];
        return false;
    }

    private bool EventSetTempo()
    {
        _trackTempo = ((_music[_musicPos + 5] << 8) | ((_music[_musicPos + 4] << 1) & 0xff)) >> 1;
        SendTempo(_trackTempo);
        return false;
    }

    private bool EventTypeOrdrChange()
    {
        int p = _musicPos;
        if (_music[p + 1] > 31 || _partConfigEnable[_music[p + 1]] == 0)
        {
            return false;
        }

        if (_music[p + 4] == 1)
        {
            _partConfigType[_music[p + 1]] = _music[p + 5];
        }
        else if (_music[p + 4] == 2)
        {
            _partConfigOrdr[_music[p + 1]] = _music[p + 5];
        }

        return false;
    }

    private byte ApplyTranspose(byte input)
    {
        int output = _partConfigTranspose[_music[_musicPos + 1]];
        if (output == 0)
        {
            return input;
        }

        output += input & 0x7f;
        if (output > 127)
        {
            output -= 12;
        }

        if (output < 0)
        {
            output += 12;
        }

        return (byte)output;
    }

    private byte ApplyVolumeAdjust(byte input)
    {
        int output = _partConfigVolume[_music[_musicPos + 1]];
        output += input & 0x7f;
        return (byte)Math.Clamp(output, 1, 127);
    }

    /// <summary>A byte to the driver of a part's type: 0xff the FM and PCM one, 0 the silent one (<c>sendByte</c>).</summary>
    private void SendByte(byte type, byte command)
    {
        int drv = ((type >> 4) + 1) & 3;
        if (drv == 0)
        {
            Driver.Send(command);
        }
    }

    private void SendPendingEvent(int type, int evt, int note, int velo)
    {
        if (velo != 0)
        {
            evt &= 0x8f;
        }

        SendByte((byte)type, (byte)evt);
        SendByte((byte)type, (byte)note);
        SendByte((byte)type, (byte)velo);
    }

    private void SendControllerReset(int type, int part)
    {
        SendByte((byte)type, (byte)(0xb0 | part));
        SendByte((byte)type, 0x40);
        SendByte((byte)type, 0);
        SendByte((byte)type, (byte)(0xb0 | part));
        SendByte((byte)type, 0x7b);
        SendByte((byte)type, 0);
        SendByte((byte)type, (byte)(0xb0 | part));
        SendByte((byte)type, 0x79);
        SendByte((byte)type, 0x40);
    }

    /// <summary>Mirrors <c>sendTempo</c> in tempo control mode 0: timer A's setting and the updates a tick is worth.</summary>
    private void SendTempo(int tempo)
    {
        tempo = Math.Clamp(tempo + _tempoModifier, 0, 500);
        _timerSetting = 34750 / (tempo + 30);
        _updatesPerPulse = 0x10;

        while (_timerSetting < 126)
        {
            _timerSetting <<= 1;
            _updatesPerPulse <<= 1;
        }

        while (_timerSetting > 383)
        {
            _timerSetting >>= 1;
            _updatesPerPulse >>= 1;
        }

        Driver.SetTimerA(true, -(_timerSetting - 2));
    }

    /// <summary>A note off waiting for its time (<c>PendingEvent</c>).</summary>
    private sealed class PendingEvent(byte evt, byte type, byte note, byte velo, ushort len, PendingEvent? next)
    {
        public readonly byte Evt = evt, Type = type, Note = note, Velo = velo;
        public ushort Len = len;
        public PendingEvent? Next = next;
    }
}

/// <summary>
/// Takes the Euphony player's MIDI-like bytes to the Towns' audio: each of 16 parts has a chain of
/// channels, FM and PCM, and a note takes the part's free channel or the one held longest.
/// Mirrors ScummVM's <c>EuphonyDriver</c>.
/// </summary>
internal sealed class EuphonyDriver
{
    private static readonly int[] EventSize = [3, 3, 3, 3, 2, 2, 3];

    private readonly TownsAudio _intf;
    private readonly List<byte> _currentEvent = [];
    private readonly sbyte[] _partToChanMapping = new sbyte[16];
    private readonly sbyte[] _sustainChannels = new sbyte[16];
    private readonly Channel[] _channels = new Channel[128];

    public EuphonyDriver(TownsAudio intf) => _intf = intf;

    /// <summary>The Towns' audio the driver plays on.</summary>
    public TownsAudio Audio => _intf;

    /// <summary>Mirrors <c>reset</c>: the audio reset, and the 6 FM channels and 8 PCM ones given to parts 0 to 13.</summary>
    public void Reset()
    {
        _intf.ResetAll();

        _currentEvent.Clear();
        Array.Clear(_sustainChannels);
        Array.Fill(_partToChanMapping, (sbyte)-1);

        for (int i = 0; i < 128; i++)
        {
            _channels[i] = new Channel { Part = -1, Next = -1 };
        }

        int e = 0;
        for (int i = 0; i < 6; i++)
        {
            AssignPartToChannel(i, e++);
        }

        for (int i = 0x40; i < 0x48; i++)
        {
            AssignPartToChannel(i, e++);
        }
    }

    /// <summary>Moves a channel to a part's chain, keying off a note it plays (<c>assignPartToChannel</c>).</summary>
    public int AssignPartToChannel(int chan, int part)
    {
        if (part > 15 || chan > 127 || chan < 0)
        {
            return 3;
        }

        var a = _channels[chan];
        if (a.Part == part)
        {
            return 0;
        }

        if (a.Part != -1)
        {
            // the link that names this channel: the part's first, or a channel's next
            int owner = -1;
            int b = _partToChanMapping[a.Part];
            while (b != chan)
            {
                owner = b;
                b = _channels[b].Next;
                if (b == -1)
                {
                    return 3;
                }
            }

            if (owner == -1)
            {
                _partToChanMapping[a.Part] = a.Next;
            }
            else
            {
                _channels[owner].Next = a.Next;
            }

            if (a.Note != 0)
            {
                _intf.KeyOff(chan);
            }

            a.Part = a.Next = -1;
            a.Note = 0;
        }

        a.Next = _partToChanMapping[part];
        _partToChanMapping[part] = (sbyte)chan;
        a.Part = (sbyte)part;
        a.Note = a.Pri = 0;
        return 0;
    }

    /// <summary>A byte of an event; a whole event is acted on (<c>send</c>).</summary>
    public void Send(byte command)
    {
        if (command >= 0x80)
        {
            _currentEvent.Clear();
            _currentEvent.Add(command >= 0xf0 ? (byte)0 : command);
        }
        else if (_currentEvent[0] >= 0x80)
        {
            int cmd = (_currentEvent[0] - 0x80) >> 4;
            _currentEvent.Add(command);

            if (_currentEvent.Count != EventSize[cmd])
            {
                return;
            }

            switch (cmd)
            {
                case 0:
                    NoteOff();
                    break;
                case 1:
                    if (_currentEvent[2] != 0)
                    {
                        NoteOn();
                    }
                    else
                    {
                        NoteOff();
                    }

                    break;
                case 3:
                    if (_currentEvent[1] == 7)
                    {
                        ControlChangeVolume();
                    }
                    else if (_currentEvent[1] == 10)
                    {
                        ControlChangePanPos();
                    }
                    else if (_currentEvent[1] == 64)
                    {
                        ControlChangeAllNotesOff();
                    }

                    break;
                case 4:
                    ProgramChange();
                    break;
                case 6:
                    PitchWheel();
                    break;
            }
        }
    }

    /// <summary>Command 21 (<c>setTimerA</c>).</summary>
    public void SetTimerA(bool enable, int tempo) => _intf.SetTimerA(enable, tempo);

    /// <summary>Command 22 (<c>setTimerB</c>).</summary>
    public void SetTimerB(bool enable, int tempo) => _intf.SetTimerB(enable, tempo);

    /// <summary>Command 5 (<c>loadInstrument</c>).</summary>
    public void LoadInstrument(int chanType, int id, ReadOnlySpan<byte> data) => _intf.LoadInstrument(chanType, id, data);

    /// <summary>Command 4 (<c>setInstrument</c>).</summary>
    public void SetInstrument(int chan, int instrId) => _intf.SetInstrument(chan, instrId);

    /// <summary>Command 33, and the reserved channels counted as effects for the volume (<c>reserveSoundEffectChannels</c>).</summary>
    public void ReserveSoundEffectChannels(int num)
    {
        _intf.ReserveEffectChannels(num);
        int volMask = 0;

        if (num > 8)
        {
            return;
        }

        for (int v = 1 << 13; num != 0; num--)
        {
            volMask |= v;
            v >>= 1;
        }

        _intf.SetSoundEffectChanMask(volMask);
    }

    /// <summary>Command 8 (<c>channelVolume</c>).</summary>
    public void ChannelVolume(int chan, int vol) => _intf.SetLevel(chan, vol);

    /// <summary>Mirrors <c>noteOff</c>: the part's channel playing the note stops, or is held while the part sustains.</summary>
    private void NoteOff()
    {
        int chan = _partToChanMapping[_currentEvent[0] & 0x0f];
        if (chan == -1)
        {
            return;
        }

        while (_channels[chan].Note != _currentEvent[1])
        {
            chan = _channels[chan].Next;
            if (chan == -1)
            {
                return;
            }
        }

        if (_sustainChannels[_currentEvent[0] & 0x0f] != 0)
        {
            _channels[chan].Note |= 0x80;
        }
        else
        {
            _channels[chan].Note = 0;
            _intf.KeyOff(chan);
        }
    }

    /// <summary>Mirrors <c>noteOn</c>: the part's first free channel, or else the one longest held, keyed off first.</summary>
    private void NoteOn()
    {
        if (_currentEvent[1] == 0)
        {
            return;
        }

        int first = _partToChanMapping[_currentEvent[0] & 0x0f];
        if (first == -1)
        {
            return;
        }

        int chan = first;
        do
        {
            _channels[chan].Pri++;
            chan = _channels[chan].Next;
        }
        while (chan != -1);

        chan = first;
        int d = 0;
        int c = 0;
        bool found = false;

        do
        {
            if (_channels[chan].Note == 0)
            {
                found = true;
                break;
            }

            if (d <= _channels[chan].Pri)
            {
                c = chan;
                d = _channels[chan].Pri;
            }

            chan = _channels[chan].Next;
        }
        while (chan != -1);

        if (found)
        {
            c = chan;
        }
        else
        {
            _intf.KeyOff(c);
        }

        _channels[c].Note = _currentEvent[1];
        _channels[c].Pri = 0;
        _intf.KeyOn(c, _currentEvent[1], _currentEvent[2]);
    }

    private void ControlChangeVolume()
    {
        for (int chan = _partToChanMapping[_currentEvent[0] & 0x0f]; chan != -1; chan = _channels[chan].Next)
        {
            _intf.SetLevel(chan, _currentEvent[2] & 0x7f);
        }
    }

    private void ControlChangePanPos()
    {
        for (int chan = _partToChanMapping[_currentEvent[0] & 0x0f]; chan != -1; chan = _channels[chan].Next)
        {
            _intf.SetPanPos(chan, _currentEvent[2] & 0x7f);
        }
    }

    /// <summary>Mirrors <c>controlChange_allNotesOff</c>, which is the sustain pedal: released, the held notes stop.</summary>
    private void ControlChangeAllNotesOff()
    {
        if (_currentEvent[2] > 63)
        {
            _sustainChannels[_currentEvent[0] & 0x0f] = -1;
            return;
        }

        _sustainChannels[_currentEvent[0] & 0x0f] = 0;
        for (int chan = _partToChanMapping[_currentEvent[0] & 0x0f]; chan != -1; chan = _channels[chan].Next)
        {
            if ((_channels[chan].Note & 0x80) != 0)
            {
                _channels[chan].Note = 0;
                _intf.KeyOff(chan);
            }
        }
    }

    private void ProgramChange()
    {
        for (int chan = _partToChanMapping[_currentEvent[0] & 0x0f]; chan != -1; chan = _channels[chan].Next)
        {
            _intf.SetInstrument(chan, _currentEvent[1]);
            _intf.SetPitch(chan, 0);
        }
    }

    /// <summary>Mirrors <c>pitchWheel</c>, which doubles the event's low byte again for each channel it reaches.</summary>
    private void PitchWheel()
    {
        for (int chan = _partToChanMapping[_currentEvent[0] & 0x0f]; chan != -1; chan = _channels[chan].Next)
        {
            _currentEvent[1] = (byte)(_currentEvent[1] + _currentEvent[1]);
            short pitch = (short)(((((_currentEvent[2] << 8) | _currentEvent[1]) >> 1) & 0x3fff) - 0x2000);
            _intf.SetPitch(chan, pitch);
        }
    }

    private sealed class Channel
    {
        public sbyte Part;
        public sbyte Next;
        public byte Note;
        public byte Pri;
    }
}
