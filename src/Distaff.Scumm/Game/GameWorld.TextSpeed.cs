using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>
/// How long the game's lines stay up, as ScummVM keeps it: a setting of its own (its
/// <c>talkspeed</c>), 0 slow to 9 fast, 6 unless set, held in the scripts' text speed variable as
/// 9 less the setting (<c>VAR_CHARINC</c>, the ticks a character adds to a line's time). The
/// setting is put there when the game starts and after every load (<c>syncSoundSettings</c>), and
/// it takes the place of what the boot script puts there; a value the scripts write later becomes
/// the setting (<c>writeVar</c>).
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>ScummVM's text speed when none is set (<c>_defaultTextSpeed</c>).</summary>
    public const int DefaultTextSpeed = 6;

    private int _textSpeed;

    /// <summary>
    /// Whether the VGA CD draws its lines while the CD plays, as ScummVM's <c>subtitles</c> setting;
    /// off, a line's characters are not drawn while the CD plays, though they still keep it up as
    /// long. ScummVM has it off unless set; Distaff has it on. The other releases always draw them.
    /// </summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>How fast the game's lines go by, 0 to 9 (ScummVM's <c>talkspeed</c>): each character of a line keeps it up 9 less this many ticks.</summary>
    public int TextSpeed
    {
        get => _textSpeed;
        set
        {
            _textSpeed = Math.Clamp(value, 0, 9);
            Engine.Variables[Vars.CharInc] = 9 - _textSpeed;
        }
    }

    /// <summary>
    /// A script writes the text speed. Before the first room the setting has its way, as the user's
    /// override of the boot script's own; after it, a speed from 0 to 9 becomes the setting.
    /// </summary>
    private int WriteCharInc(int value)
    {
        if (CurrentRoomNumber == 0)
        {
            return 9 - _textSpeed;
        }

        if (value is >= 0 and <= 9)
        {
            _textSpeed = 9 - value;
        }

        return value;
    }
}
