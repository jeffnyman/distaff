using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>
/// The VGA CD's audio track as the scripts see it: which part plays, and the music timer that
/// counts while it does, which the scripts wait on to keep text and pictures with the voice and
/// music. Mirrors ScummVM's <c>SoundCD::playCDTrack</c>, <c>startCDTimer</c>, <c>cdTimerHandler</c>
/// and <c>getCDMusicTimer</c>, and Loom's <c>SO_SAY_VOICE</c> in <c>decodeParseString</c>. The
/// sound itself is not played yet.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// The CD frames before Loom's audio: the first track (22500) and the 2-second lead-in (150).
    /// Loom gives voice offsets from the start of the CD, in units of 7.5 frames.
    /// </summary>
    private const int CdLeadIn = 22500 + 2 * 75;

    private bool _cdTimerRunning;
    private int _cdMusicTimer;
    private int _cdMusicTimerMod;

    /// <summary>The part of the CD playing: the track, and the start and length in CD frames (75 a second); null when stopped.</summary>
    public (int Track, int Start, int Duration)? CdTrack { get; private set; }

    /// <summary>
    /// <c>SO_SAY_VOICE</c>: plays the voice and music from an offset for a length, both in units of
    /// 7.5 CD frames, the length 5 frames longer; 0 and 0 stop the CD and zero the timer.
    /// </summary>
    private void SayVoice(int offset, int length)
    {
        if (offset == 0 && length == 0)
        {
            Engine.Variables[Vars.MusicTimer] = 0;
            CdTrack = null;
            return;
        }

        PlayCdTrack(1, (int)(offset * 7.5 - CdLeadIn), (int)(length * 7.5 + 5));
    }

    /// <summary>Starts part of a track, zeroing the music timer and starting it counting (<c>playCDTrack</c>, <c>startCDTimer</c>).</summary>
    private void PlayCdTrack(int track, int start, int duration)
    {
        Engine.Variables[Vars.MusicTimer] = 0;
        _cdMusicTimerMod = 0;
        _cdMusicTimer = 0;
        CdTrack = (track, start, duration);
        _cdTimerRunning = true;
    }

    /// <summary>
    /// Runs the CD timer for some quarter-frames: it counts one for every four, from when a track
    /// first starts, at the DOS timer's rate (<c>cdTimerHandler</c>).
    /// </summary>
    private void AdvanceCdTimer(int quarterFrames)
    {
        if (!_cdTimerRunning)
        {
            return;
        }

        for (int i = 0; i < quarterFrames; i++)
        {
            if ((_cdMusicTimerMod++ & 3) == 0)
            {
                _cdMusicTimer++;
            }
        }
    }
}
