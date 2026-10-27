using Distaff.Scumm.Scripts;
using Distaff.Scumm.Sound;

namespace Distaff.Scumm.Game;

/// <summary>
/// The VGA CD's audio track as the scripts see it: which part plays, and the music timer that
/// counts while it does, which the scripts wait on to keep text and pictures with the voice and
/// music. Mirrors ScummVM's <c>SoundCD::playCDTrack</c>, <c>startCDTimer</c>, <c>cdTimerHandler</c>
/// and <c>getCDMusicTimer</c>, and Loom's <c>SO_SAY_VOICE</c> in <c>decodeParseString</c>. The
/// sound is played from the track's file by <see cref="SoundSystem.Cd"/>.
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
            Sound.Cd.Stop();
            return;
        }

        PlayCdTrack(1, (int)(offset * 7.5 - CdLeadIn), (int)(length * 7.5 + 5), 1);
    }

    /// <summary>Starts part of a track, zeroing the music timer and starting it counting (<c>playCDTrack</c>, <c>startCDTimer</c>).</summary>
    private void PlayCdTrack(int track, int start, int duration, int numLoops)
    {
        Sound.Cd.Play(track, start, duration, numLoops);
        Engine.Variables[Vars.MusicTimer] = 0;
        _cdMusicTimerMod = 0;
        _cdMusicTimer = 0;
        CdTrack = (track, start, duration);
        _cdTimerRunning = true;
    }

    /// <summary>
    /// After a load, plays again the part the save says was playing. Mirrors
    /// <c>SoundCD::restoreCDAudioAfterLoad</c> for a game that starts no CD sound by number: a part
    /// played once goes on from 1.25 CD frames for each count of the music timer the save holds,
    /// and is as many frames shorter as the timer has counted. The timer is not saved: it goes on
    /// counting, or not, as before the load.
    /// </summary>
    private void RestoreCdAfterLoad(int track, int start, int duration, int numLoops)
    {
        if (numLoops == 1)
        {
            int musicTimer = Engine.Variables[Vars.MusicTimer];
            start += (int)(musicTimer * 1.25);
            if (duration != 0)
            {
                duration -= musicTimer;
            }
        }

        Sound.Cd.Play(track, start, duration, numLoops);
        CdTrack = (track, start, duration);
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
