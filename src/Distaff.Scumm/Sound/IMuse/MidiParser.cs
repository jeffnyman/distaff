namespace Distaff.Scumm.Sound.IMuse;

/// <summary>What a MIDI parser plays to: iMuse's player (<c>MidiDriver_BASE</c>, as the player uses it).</summary>
internal interface IMidiReceiver
{
    void Send(uint b);

    /// <summary>A system exclusive message without its F0; returns the delay it asks for, in milliseconds.</summary>
    ushort SysExNoDelay(ReadOnlySpan<byte> message);

    void MetaEvent(byte type, ReadOnlySpan<byte> data);

    /// <summary>All Notes Off on every channel (<c>MidiDriver_BASE::stopAllNotes</c>).</summary>
    void StopAllNotes(bool stopSustainedNotes)
    {
        for (uint i = 0; i < 16; ++i)
        {
            Send(0xB0 | i | (0x7B << 8));
            if (stopSustainedNotes)
            {
                Send(0xB0 | i | (0x40 << 8));
            }
        }
    }
}

/// <summary>
/// A MIDI event as a parser reads it. Mirrors ScummVM's <c>EventInfo</c>; the data it points
/// into is the parser's, by index.
/// </summary>
internal sealed class MidiEvent
{
    public int Start = -1;
    public byte Subtrack;
    public uint Delta;
    public byte Event;
    public byte Param1, Param2;
    public byte Type;
    public int Data = -1;
    public uint Length;
    public bool Loop, Noop;

    public byte Channel => (byte)(Event & 0x0F);

    public byte Command => (byte)(Event >> 4);

    public void Clear()
    {
        Start = -1;
        Delta = 0;
        Event = 0;
        Param1 = Param2 = 0;
        Type = 0;
        Data = -1;
        Length = 0;
        Loop = Noop = false;
    }

    public void CopyFrom(MidiEvent other)
    {
        (Start, Subtrack, Delta, Event, Param1, Param2) = (other.Start, other.Subtrack, other.Delta, other.Event, other.Param1, other.Param2);
        (Type, Data, Length, Loop, Noop) = (other.Type, other.Data, other.Length, other.Loop, other.Noop);
    }
}

/// <summary>
/// Where a parser is in its music. Mirrors ScummVM's <c>Tracker</c>; a subtrack's place is an index
/// into the parser's data, -1 when it does not play.
/// </summary>
internal sealed class MidiTracker
{
    public uint PlayTime, PlayTick, LastEventTime, LastEventTick;
    public readonly int[] PlayPos = new int[MidiParser.MaximumSubtracks];
    public readonly uint[] SubLastEventTime = new uint[MidiParser.MaximumSubtracks];
    public readonly uint[] SubLastEventTick = new uint[MidiParser.MaximumSubtracks];
    public readonly byte[] RunningStatus = new byte[MidiParser.MaximumSubtracks];

    public MidiTracker() => Clear();

    public void Clear()
    {
        PlayTime = PlayTick = LastEventTime = LastEventTick = 0;
        for (int i = 0; i < MidiParser.MaximumSubtracks; i++)
        {
            PlayPos[i] = -1;
            SubLastEventTime[i] = SubLastEventTick[i] = 0;
            RunningStatus[i] = 0;
        }
    }

    public bool IsTracking() => PlayPos.Any(p => p >= 0);

    public bool IsTracking(int subtrack) => PlayPos[subtrack] >= 0;

    public void StopTracking() => Array.Fill(PlayPos, -1);

    public void StopTracking(int subtrack) => PlayPos[subtrack] = -1;

    public void CopyFrom(MidiTracker other)
    {
        (PlayTime, PlayTick, LastEventTime, LastEventTick) = (other.PlayTime, other.PlayTick, other.LastEventTime, other.LastEventTick);
        other.PlayPos.CopyTo(PlayPos, 0);
        other.SubLastEventTime.CopyTo(SubLastEventTime, 0);
        other.SubLastEventTick.CopyTo(SubLastEventTick, 0);
        other.RunningStatus.CopyTo(RunningStatus, 0);
    }
}

/// <summary>
/// ScummVM's generic MIDI parser (<c>audio/midiparser.cpp</c>): the timing of events by ticks and
/// tempo against a timer rate, the notes left hanging when a jump cuts them, the end of a track
/// (looping round or stopping), and jumps. A format reads its own events (<see cref="ParseNextEvent"/>).
/// Only what iMuse's player uses of it is here: one track, the source of every message -1.
/// </summary>
internal abstract class MidiParser
{
    public const int MaximumSubtracks = 35;

    private readonly ushort[] _activeNotes = new ushort[128];
    private readonly (byte Channel, byte Note, uint TimeLeft)[] _hangingNotes = new (byte, byte, uint)[32];
    private int _hangingNotesCount;
    private uint _sysExDelay;

    protected byte[] _data = [];
    protected IMidiReceiver? _driver;
    protected uint _timerRate = 0x4A0000;
    protected uint _ppqn = 96;
    protected uint _tempo = 500000;
    protected uint _psecPerTick = 5208;
    protected bool _autoLoop;
    protected bool _smartJump;
    protected int _numTracks;
    protected int _activeTrack = 255;
    protected readonly int[] _numSubtracks = [1];
    protected readonly int[][] _tracks = [new int[MaximumSubtracks]];
    protected bool _abortParse;
    protected readonly MidiTracker _position = new();
    protected readonly MidiEvent[] _nextSubtrackEvents = [.. Enumerable.Range(0, MaximumSubtracks).Select(i => new MidiEvent { Subtrack = (byte)i })];
    protected MidiEvent _nextEvent;
    private bool _doParse = true;
    private bool _pause;

    protected MidiParser()
    {
        _nextEvent = _nextSubtrackEvents[0];
    }

    /// <summary>The parser's place in ticks (<c>getTick</c>).</summary>
    public virtual uint Tick => _position.PlayTick;

    public uint Ppqn => _ppqn;

    public bool SmartJump
    {
        set => _smartJump = value;
    }

    public void SetMidiDriver(IMidiReceiver? driver) => _driver = driver;

    /// <summary>The microseconds that pass at each call of <see cref="OnTimer"/> (<c>setTimerRate</c>).</summary>
    public void SetTimerRate(uint rate) => _timerRate = rate;

    public bool IsPlaying => _position.IsTracking() && _doParse;

    public abstract bool LoadMusic(byte[] data, int offset);

    protected abstract void ParseNextEvent(MidiEvent info);

    protected void SendToDriver(uint b) => _driver!.Send(b);

    protected void SendToDriver(byte status, byte firstOp, byte secondOp) => SendToDriver(status | ((uint)firstOp << 8) | ((uint)secondOp << 16));

    /// <summary>Mirrors <c>setTempo</c>.</summary>
    protected void SetTempo(uint tempo)
    {
        _tempo = tempo;
        if (_ppqn != 0)
        {
            _psecPerTick = (tempo + (_ppqn >> 2)) / _ppqn;
        }
    }

    private void ActiveNote(byte channel, byte note, bool active)
    {
        if (note >= 128 || channel >= 16)
        {
            return;
        }

        if (active)
        {
            _activeNotes[note] |= (ushort)(1 << channel);
        }
        else
        {
            _activeNotes[note] &= (ushort)~(1 << channel);
        }

        // see if there are hanging notes that can be cancelled
        for (int i = 0; i < _hangingNotes.Length; i++)
        {
            if (_hangingNotes[i].Channel == channel && _hangingNotes[i].Note == note && _hangingNotes[i].TimeLeft != 0)
            {
                _hangingNotes[i].TimeLeft = 0;
                --_hangingNotesCount;
                break;
            }
        }
    }

    private void HangingNote(byte channel, byte note, uint timeLeft, bool recycle)
    {
        if (_hangingNotesCount >= _hangingNotes.Length)
        {
            return;
        }

        int best = -1;
        for (int i = 0; i < _hangingNotes.Length; i++)
        {
            ref var ptr = ref _hangingNotes[i];
            if (ptr.Channel == channel && ptr.Note == note)
            {
                if (ptr.TimeLeft != 0 && ptr.TimeLeft < timeLeft && recycle)
                {
                    return;
                }

                best = i;
                if (ptr.TimeLeft != 0)
                {
                    if (recycle)
                    {
                        SendToDriver((byte)(0x80 | channel), note, 0);
                    }

                    --_hangingNotesCount;
                }

                break;
            }
            else if (best < 0 && ptr.TimeLeft == 0)
            {
                best = i;
            }
        }

        // a note of no or negative length is turned off in the next cycle
        if (timeLeft == 0 || (timeLeft & 0x80000000) != 0)
        {
            timeLeft = 1;
        }

        if (best >= 0)
        {
            _hangingNotes[best] = (channel, note, timeLeft);
            ++_hangingNotesCount;
        }
    }

    /// <summary>
    /// Plays what falls in the next stretch of the timer rate: notes left hanging that end, then
    /// each event up to the stretch's end. Mirrors <c>onTimer</c>.
    /// </summary>
    public void OnTimer()
    {
        _sysExDelay -= _sysExDelay > _timerRate ? _timerRate : _sysExDelay;
        if (!_position.IsTracking() || _driver is null || !_doParse || _pause)
        {
            return;
        }

        _abortParse = false;
        uint endTime = _position.PlayTime + _timerRate;
        if (_hangingNotesCount != 0)
        {
            for (int i = 0; i < _hangingNotes.Length; i++)
            {
                ref var ptr = ref _hangingNotes[i];
                if (ptr.TimeLeft != 0)
                {
                    if (ptr.TimeLeft <= _timerRate)
                    {
                        SendToDriver((byte)(0x80 | ptr.Channel), ptr.Note, 0);
                        ptr.TimeLeft = 0;
                        --_hangingNotesCount;
                    }
                    else
                    {
                        ptr.TimeLeft -= _timerRate;
                    }
                }
            }
        }

        bool loopEvent = false;
        while (!_abortParse)
        {
            var info = _nextEvent;
            int subtrack = info.Subtrack;
            uint eventTick = _position.SubLastEventTick[subtrack] + info.Delta;
            uint eventTime = _position.LastEventTime + (eventTick - _position.LastEventTick) * _psecPerTick;
            if (eventTime > endTime)
            {
                break;
            }

            if (!info.Noop)
            {
                if (info.Event < 0x80)
                {
                    _position.StopTracking();
                    return;
                }

                if (info.Command == 0x8)
                {
                    ActiveNote(info.Channel, info.Param1, false);
                }
                else if (info.Command == 0x9)
                {
                    if (info.Length > 0)
                    {
                        HangingNote(info.Channel, info.Param1, info.Length * _psecPerTick - (endTime - eventTime), true);
                    }
                    else
                    {
                        ActiveNote(info.Channel, info.Param1, true);
                    }
                }

                // the player's meta event handler may have unloaded the music
                if (!ProcessEvent(info, true))
                {
                    return;
                }
            }

            loopEvent |= info.Loop;
            if (!_abortParse)
            {
                _position.PlayTime = eventTime;
                _position.LastEventTime = eventTime;
                _position.SubLastEventTime[subtrack] = eventTime;
                _position.PlayTick = eventTick;
                _position.LastEventTick = eventTick;
                _position.SubLastEventTick[subtrack] = eventTick;
                if (_position.IsTracking(subtrack))
                {
                    ParseNextEvent(_nextSubtrackEvents[subtrack]);
                }

                DetermineNextEvent();
            }
        }

        if (!_abortParse)
        {
            _position.PlayTime = endTime;
            _position.PlayTick = (endTime - _position.LastEventTime) / _psecPerTick + _position.LastEventTick;
            if (loopEvent)
            {
                RebaseTracking();
            }
        }
    }

    private void RebaseTracking()
    {
        uint earliestTick = 0xFFFFFFFF;
        int earliest = -1;
        for (int i = 0; i < _numSubtracks[_activeTrack]; i++)
        {
            if (_position.IsTracking(i) && _position.SubLastEventTick[i] < earliestTick)
            {
                earliestTick = _position.SubLastEventTick[i];
                earliest = i;
            }
        }

        if (earliest == -1)
        {
            return;
        }

        uint earliestTime = _position.SubLastEventTime[earliest];
        for (int i = 0; i < _numSubtracks[_activeTrack]; i++)
        {
            if (_position.IsTracking(i))
            {
                _position.SubLastEventTime[i] = _position.SubLastEventTime[i] >= earliestTime ? _position.SubLastEventTime[i] - earliestTime : 0;
                _position.SubLastEventTick[i] -= earliestTick;
            }
        }

        _position.PlayTime = _position.PlayTime >= earliestTime ? _position.PlayTime - earliestTime : 0;
        _position.LastEventTime = _position.LastEventTime >= earliestTime ? _position.LastEventTime - earliestTime : 0;
        _position.PlayTick -= earliestTick;
        _position.LastEventTick -= earliestTick;
    }

    protected void DetermineNextEvent()
    {
        uint lowest = 0xFFFFFFFF;
        int lowestSubtrack = -1;
        for (int i = 0; i < _numSubtracks[_activeTrack]; i++)
        {
            if (_position.IsTracking(i))
            {
                uint next = _position.SubLastEventTick[i] + _nextSubtrackEvents[i].Delta;
                if (next < lowest)
                {
                    lowest = next;
                    lowestSubtrack = i;
                }
            }
        }

        _nextEvent = _nextSubtrackEvents[lowestSubtrack >= 0 ? lowestSubtrack : 0];
    }

    /// <summary>Mirrors <c>processEvent</c>: a system exclusive message, a meta event (the end of the track loops round or stops), or a channel message.</summary>
    private bool ProcessEvent(MidiEvent info, bool fireEvents)
    {
        if (info.Event == 0xF0)
        {
            if (fireEvents)
            {
                if (_sysExDelay > 0)
                {
                    return false;
                }

                int length = _data[info.Data + (int)info.Length - 1] == 0xF7 ? (int)info.Length - 1 : (int)info.Length;
                ushort delay = _driver!.SysExNoDelay(_data.AsSpan(info.Data, length));
                _sysExDelay = delay * 1000u;
            }
        }
        else if (info.Event == 0xFF)
        {
            bool sendEventToDriver = true;
            if (info.Type == 0x2F)
            {
                _position.StopTracking(info.Subtrack);
                if (!_position.IsTracking())
                {
                    if (_autoLoop)
                    {
                        JumpToTick(0);
                    }
                    else
                    {
                        StopPlaying();
                        if (fireEvents)
                        {
                            SendMeta(info);
                        }
                    }

                    return false;
                }

                sendEventToDriver = false;
            }
            else if (info.Type == 0x51 && info.Length >= 3)
            {
                SetTempo((uint)(_data[info.Data] << 16 | _data[info.Data + 1] << 8 | _data[info.Data + 2]));
            }

            if (fireEvents && sendEventToDriver)
            {
                SendMeta(info);
            }
        }
        else if (fireEvents)
        {
            SendToDriver(info.Event, info.Param1, info.Param2);
        }

        return true;
    }

    private void SendMeta(MidiEvent info) => _driver!.MetaEvent(info.Type, info.Data < 0 ? [] : _data.AsSpan(info.Data, (int)info.Length));

    /// <summary>Every note playing or hanging is turned off, and every channel told so. Mirrors <c>allNotesOff</c>.</summary>
    private void AllNotesOff()
    {
        if (_driver is null)
        {
            return;
        }

        for (int i = 0; i < 128; ++i)
        {
            for (int j = 0; j < 16; ++j)
            {
                if ((_activeNotes[i] & (1 << j)) != 0)
                {
                    SendToDriver((byte)(0x80 | j), (byte)i, 0);
                }
            }
        }

        for (int i = 0; i < _hangingNotes.Length; i++)
        {
            if (_hangingNotes[i].TimeLeft != 0)
            {
                SendToDriver((byte)(0x80 | _hangingNotes[i].Channel), _hangingNotes[i].Note, 0);
                _hangingNotes[i].TimeLeft = 0;
            }
        }

        _hangingNotesCount = 0;
        _driver.StopAllNotes(false);
        Array.Clear(_activeNotes);
    }

    protected void ResetTracking() => _position.Clear();

    /// <summary>Starts a track from its beginning. Mirrors <c>setTrack</c>.</summary>
    public bool SetTrack(int track)
    {
        if (track < 0 || track >= _numTracks)
        {
            return false;
        }
        else if (track == _activeTrack && IsPlaying)
        {
            return true;
        }

        if (_smartJump)
        {
            HangAllActiveNotes();
        }
        else if (IsPlaying)
        {
            AllNotesOff();
        }

        ResetTracking();
        _pause = false;
        Array.Clear(_activeNotes);
        foreach (var e in _nextSubtrackEvents)
        {
            e.Clear();
        }

        _nextEvent = _nextSubtrackEvents[0];
        _activeTrack = track;
        for (int i = 0; i < _numSubtracks[_activeTrack]; i++)
        {
            _position.PlayPos[i] = _tracks[_activeTrack][i];
            ParseNextEvent(_nextSubtrackEvents[i]);
        }

        DetermineNextEvent();
        return true;
    }

    private void StopPlaying()
    {
        if (IsPlaying)
        {
            AllNotesOff();
        }

        ResetTracking();
        _pause = false;
    }

    /// <summary>
    /// Every note playing is left to hang until its note off would have come, found by reading
    /// ahead without playing. Mirrors <c>hangAllActiveNotes</c>.
    /// </summary>
    private void HangAllActiveNotes()
    {
        var tempActive = (ushort[])_activeNotes.Clone();
        var currentPos = new MidiTracker();
        currentPos.CopyFrom(_position);
        while (true)
        {
            int i;
            for (i = 0; i < 128; ++i)
            {
                if (tempActive[i] != 0)
                {
                    break;
                }
            }

            if (i == 128)
            {
                break;
            }

            if (_position.IsTracking(_nextEvent.Subtrack))
            {
                ParseNextEvent(_nextSubtrackEvents[_nextEvent.Subtrack]);
            }

            DetermineNextEvent();
            int subtrack = _nextEvent.Subtrack;
            uint eventTick = _position.SubLastEventTick[subtrack] + _nextEvent.Delta;
            if (_nextEvent.Command == 0x8)
            {
                if ((tempActive[_nextEvent.Param1] & (1 << _nextEvent.Channel)) != 0)
                {
                    HangingNote(_nextEvent.Channel, _nextEvent.Param1, (eventTick - currentPos.LastEventTick) * _psecPerTick, false);
                    tempActive[_nextEvent.Param1] &= (ushort)~(1 << _nextEvent.Channel);
                }
            }
            else if (!_position.IsTracking() || (_nextEvent.Event == 0xFF && _nextEvent.Type == 0x2F))
            {
                _position.StopTracking(subtrack);
                if (!_position.IsTracking())
                {
                    for (i = 0; i < 128; ++i)
                    {
                        for (int j = 0; j < 16; ++j)
                        {
                            if ((tempActive[i] & (1 << j)) != 0)
                            {
                                ActiveNote((byte)j, (byte)i, false);
                                SendToDriver((byte)(0x80 | j), (byte)i, 0);
                            }
                        }
                    }

                    break;
                }
            }

            _position.LastEventTick = eventTick;
            _position.SubLastEventTick[subtrack] = eventTick;
        }

        _position.CopyFrom(currentPos);
    }

    /// <summary>
    /// Goes to a tick of the track from its start, playing the events on the way unless told
    /// not to; the notes playing are left to hang or turned off. Mirrors <c>jumpToTick</c>.
    /// </summary>
    public bool JumpToTick(uint tick, bool fireEvents = false, bool stopNotes = true, bool dontSendNoteOn = false)
    {
        if (_activeTrack >= _numTracks || _pause)
        {
            return false;
        }

        var currentPos = new MidiTracker();
        currentPos.CopyFrom(_position);
        var currentEvent = _nextEvent;
        var currentSubtrackEvents = Snapshot();

        ResetTracking();
        for (int i = 0; i < _numSubtracks[_activeTrack]; i++)
        {
            _position.PlayPos[i] = _tracks[_activeTrack][i];
            ParseNextEvent(_nextSubtrackEvents[i]);
        }

        DetermineNextEvent();
        if (tick > 0)
        {
            while (true)
            {
                var info = _nextEvent;
                int subtrack = info.Subtrack;
                uint eventTick = _position.SubLastEventTick[subtrack] + info.Delta;
                if (eventTick >= tick)
                {
                    _position.PlayTime += (tick - _position.LastEventTick) * _psecPerTick;
                    _position.PlayTick = tick;
                    break;
                }

                if (info.Command == 0x9 && dontSendNoteOn)
                {
                    // a note on is not played on the way
                }
                else if (info.Event == 0xFF && info.Type == 0x2F)
                {
                    _position.StopTracking(info.Subtrack);
                    if (!_position.IsTracking())
                    {
                        _position.CopyFrom(currentPos);
                        _nextEvent = currentEvent;
                        Restore(currentSubtrackEvents);
                        return false;
                    }
                }
                else
                {
                    ProcessEvent(info, fireEvents);
                }

                uint eventTime = _position.LastEventTime + (eventTick - _position.LastEventTick) * _psecPerTick;
                _position.PlayTime = eventTime;
                _position.LastEventTime = eventTime;
                _position.SubLastEventTime[subtrack] = eventTime;
                _position.PlayTick = eventTick;
                _position.LastEventTick = eventTick;
                _position.SubLastEventTick[subtrack] = eventTick;
                if (_position.IsTracking(subtrack))
                {
                    ParseNextEvent(_nextSubtrackEvents[subtrack]);
                }

                DetermineNextEvent();
            }
        }

        if (stopNotes)
        {
            if (!_smartJump || !currentPos.IsTracking())
            {
                AllNotesOff();
            }
            else
            {
                var targetPosition = new MidiTracker();
                targetPosition.CopyFrom(_position);
                var targetEvent = _nextEvent;
                var targetSubtrackEvents = Snapshot();

                _position.CopyFrom(currentPos);
                _nextEvent = currentEvent;
                Restore(currentSubtrackEvents);
                HangAllActiveNotes();

                _position.CopyFrom(targetPosition);
                _nextEvent = targetEvent;
                Restore(targetSubtrackEvents);
            }
        }

        _abortParse = true;
        return true;
    }

    // the subtracks' next events copied, and copied back: _nextEvent keeps pointing at the same slot
    private MidiEvent[] Snapshot()
    {
        var copy = new MidiEvent[_numSubtracks[_activeTrack]];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = new MidiEvent();
            copy[i].CopyFrom(_nextSubtrackEvents[i]);
        }

        return copy;
    }

    private void Restore(MidiEvent[] copy)
    {
        for (int i = 0; i < copy.Length; i++)
        {
            _nextSubtrackEvents[i].CopyFrom(copy[i]);
        }
    }

    /// <summary>Stops the music and forgets it. Mirrors <c>unloadMusic</c>.</summary>
    public void UnloadMusic()
    {
        if (_numTracks == 0)
        {
            return;
        }

        StopPlaying();
        _numTracks = 0;
        _activeTrack = 255;
        _abortParse = true;
        foreach (var e in _nextSubtrackEvents)
        {
            e.Clear();
        }

        _nextEvent = _nextSubtrackEvents[0];
    }
}
