using Distaff.Scumm.Graphics;

namespace Distaff.Scumm.Game;

/// <summary>
/// The screen shake a room asks for (<c>SO_ROOM_SHAKE_ON</c>), as ScummVM shows it
/// (<c>updateScreenShakeEffect</c>): every 8 ticks of the timer, the whole picture, cursor and
/// all, is raised by the next of 0, 1, 2, 1, 0, 2, 3 and 1 rows, black showing below. The first
/// step is taken at once. It is kept by the time the game has played, in whole milliseconds as
/// ScummVM's clock counts them, so it is the same each time. A menu or banner stops it while it is
/// up and starts it again from the beginning when it goes.
/// </summary>
public sealed partial class GameWorld
{
    private static readonly int[] ShakePositions = [0, 1, 2, 1, 0, 2, 3, 1];

    private bool _shakeEnabled;
    private int _shakeBase;
    private TimeSpan _shakeStart;
    private bool _shakePaused;

    /// <summary>Whether the screen shakes (<c>_shakeEnabled</c>).</summary>
    public bool ShakeEnabled => _shakeEnabled;

    /// <summary>Where the shake is in its 8 steps (<c>_shakeFrame</c>); 0 when it is off.</summary>
    public int ShakeFrame
    {
        get
        {
            if (!_shakeEnabled || _shakePaused)
            {
                return 0;
            }

            // _shakeNextTick goes on by whole milliseconds, the microseconds of 8 ticks carried over
            long period = (long)(1000000 / (FmTowns ? 240.0 : TimerFrequency) * 8);
            long elapsed = (long)(Time - _shakeStart).TotalMilliseconds;
            long steps = ((elapsed + 1) * 1000 - 1) / period + 1;
            return (int)((_shakeBase + steps) % ShakePositions.Length);
        }
    }

    /// <summary>How many rows the picture is raised now.</summary>
    public int ShakeOffset => ShakePositions[ShakeFrame];

    /// <summary>Turns the shake on or off, from its first step (<c>setShake</c>).</summary>
    private void SetShake(bool on) => (_shakeEnabled, _shakeBase, _shakeStart) = (on, 0, Time);

    /// <summary>A menu or banner coming up stops the shake; its going starts it again (<c>_shakeTempSavedState</c>).</summary>
    private void PauseShake()
    {
        if (_shakePaused != InInterface)
        {
            _shakePaused = InInterface;
            if (!_shakePaused)
            {
                SetShake(_shakeEnabled);
            }
        }
    }

    /// <summary>The picture raised by the shake, rows the size of the room's (the FM Towns' frame is twice the size).</summary>
    private RgbFrame Shaken(RgbFrame frame)
    {
        int rows = ShakeOffset * (frame.Width / View.Camera.ScreenWidth);
        if (rows == 0)
        {
            return frame;
        }

        var shaken = new RgbFrame(frame.Width, frame.Height);
        frame.Pixels.AsSpan(rows * frame.Width).CopyTo(shaken.Pixels);
        return shaken;
    }
}
