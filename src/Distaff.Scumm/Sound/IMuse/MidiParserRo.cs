namespace Distaff.Scumm.Sound.IMuse;

/// <summary>
/// The Roland Upgrade's own music format, "RO" and then one track: channel messages without
/// running status, delays (F0 and a byte, F1 and a byte and 256 more), markers (an A0 to AF byte,
/// which the scripts count), a loop marker (FF) and the end (00). Its tick is half a beat for
/// each marker passed, so the music timer counts markers. Mirrors ScummVM's <c>MidiParser_RO</c>.
/// </summary>
internal sealed class MidiParserRo : MidiParser
{
    private int _markerCount;
    private int _lastMarkerCount;

    public override uint Tick => (uint)(_markerCount * (int)_ppqn / 2);

    protected override void ParseNextEvent(MidiEvent info)
    {
        _markerCount += _lastMarkerCount;
        _lastMarkerCount = 0;

        int playPos = _position.PlayPos[0];
        info.Delta = 0;
        while (true)
        {
            info.Start = playPos;
            info.Event = _data[playPos++];
            if (info.Command == 0xA)
            {
                ++_lastMarkerCount;
                info.Event = 0xF0;
            }
            else if (info.Event is 0xF0 or 0xF1)
            {
                byte delay = _data[playPos++];
                info.Delta += delay;
                if (info.Event == 0xF1)
                {
                    // only one song of the EGA's has it, to wait more than 255 ticks (bug #2650)
                    info.Delta += 256;
                }

                continue;
            }

            break;
        }

        _position.PlayPos[0] = playPos;

        // the end of the track
        if (info.Event == 0)
        {
            info.Event = 0xFF;
            info.Type = 0x2F;
            info.Length = 0;
            info.Data = -1;
            return;
        }

        if (info.Event < 0x80)
        {
            return;
        }

        _position.RunningStatus[0] = info.Event;
        switch (info.Command)
        {
            case 0xC:
                info.Param1 = _data[playPos++];
                info.Param2 = 0;
                break;

            case 0x8 or 0x9 or 0xB:
                info.Param1 = _data[playPos++];
                info.Param2 = _data[playPos++];
                if (info.Command == 0x9 && info.Param2 == 0)
                {
                    info.Event = (byte)(info.Channel | 0x80);
                }

                info.Length = 0;
                break;

            // a marker, or the loop marker, which ends the track to go round again
            case 0xF:
                info.Length = 0;
                info.Data = -1;
                if (info.Event == 0xFF)
                {
                    _autoLoop = true;
                    info.Type = 0x2F;
                }
                else
                {
                    info.Type = 0x7F;
                }

                info.Event = 0xFF;
                break;
        }

        _position.PlayPos[0] = playPos;
    }

    /// <summary>The music, its "RO" at <paramref name="offset"/>; 120 ticks a beat at 500000 microseconds a beat. Mirrors <c>loadMusic</c>.</summary>
    public override bool LoadMusic(byte[] data, int offset)
    {
        UnloadMusic();
        if (data[offset] != 'R' || data[offset + 1] != 'O')
        {
            throw new InvalidDataException($"'RO' header expected but found '{(char)data[offset]}{(char)data[offset + 1]}' instead");
        }

        _data = data;
        _numTracks = 1;
        _numSubtracks[0] = 1;
        _autoLoop = false;
        _ppqn = 120;
        _tracks[0][0] = offset + 2;
        _markerCount = _lastMarkerCount = 0;
        ResetTracking();
        SetTempo(500000);
        SetTrack(0);
        return true;
    }
}
