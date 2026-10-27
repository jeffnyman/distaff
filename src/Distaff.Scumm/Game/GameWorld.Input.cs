using Distaff.Scumm.Scripts;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// The player's mouse and keys, as the scripts see them. Mirrors ScummVM's mouse variables
/// (<c>processInput</c>, <c>scummLoop_updateScummVars</c>) and <c>checkExecVerbs</c>, which hands
/// a click or key to the game's verb script (<c>runInputScript</c>), and <c>processKeyboard</c>.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>The areas a click or key comes from. Mirrors <c>kVerbClickArea</c>, <c>kSceneClickArea</c> and <c>kKeyClickArea</c>.</summary>
    public const int VerbClickArea = 1, SceneClickArea = 2, KeyClickArea = 4;

    /// <summary>Mirrors <c>MBS_LEFT_CLICK</c> and <c>MBS_RIGHT_CLICK</c>: a click as <c>_mouseAndKeyboardStat</c> holds it.</summary>
    private const int LeftClick = 0x8000, RightClick = 0x4000;

    private int _input;

    /// <summary>The mouse on the screen.</summary>
    public int MouseX { get; private set; } = 104;

    public int MouseY { get; private set; } = 56;

    public void MoveMouse(int x, int y)
    {
        MouseX = Math.Clamp(x, 0, ScreenWidth - 1);
        MouseY = Math.Clamp(y, 0, ScreenHeight - 1);
    }

    /// <summary>A click at a point of the screen, for the next step.</summary>
    public void Click(int x, int y, bool right = false)
    {
        MoveMouse(x, y);
        _input = right ? RightClick : LeftClick;
    }

    /// <summary>A key, by the code the game reads (its character, or 315 on for the function keys), for the next step.</summary>
    public void PressKey(int code) => _input = code;

    /// <summary>The mouse in the room: the view's left edge added, its top taken away, -1 outside the view (<c>_virtualMouse</c>).</summary>
    private (int X, int Y) VirtualMouse()
    {
        int y = MouseY - ViewTop;
        return (MouseX + Camera.Left, y < 0 || y >= ViewHeight ? -1 : y);
    }

    /// <summary>The mouse variables. Mirrors the part of <c>scummLoop_updateScummVars</c> for version 3.</summary>
    private void UpdateMouseVariables()
    {
        var (vx, vy) = VirtualMouse();
        Engine.Variables[Vars.VirtualMouseX] = vx;
        Engine.Variables[Vars.VirtualMouseY] = vy;
        Engine.Variables[Vars.MouseX] = MouseX;
        Engine.Variables[Vars.MouseY] = MouseY;
    }

    /// <summary>
    /// Hands the step's click or key to the verb script, when the player has control. Mirrors
    /// <c>checkExecVerbs</c> for version 3: a key is a verb's when one has it; a click is a verb's
    /// when it falls on one, else the scene's in the view and the verbs' area in another band.
    /// </summary>
    private void CheckExecVerbs()
    {
        int input = _input;
        _input = 0;
        if (UserPut <= 0 || input == 0)
        {
            return;
        }

        if (input < LeftClick && input != RightClick)
        {
            for (int i = 1; i < VerbCount; i++)
            {
                var verb = Verbs[i];
                if (verb.VerbId != 0 && verb.SaveId == 0 && verb.Mode == 1 && verb.Key == input)
                {
                    RunInputScript(VerbClickArea, verb.VerbId, 1);
                    return;
                }
            }

            RunInputScript(KeyClickArea, input, 1);
            return;
        }

        if (Screens.Find(MouseY) is not { } zone)
        {
            return;
        }

        int over = FindVerbAtPos(MouseX, MouseY);
        if (over != 0)
        {
            RunInputScript(VerbClickArea, Verbs[over].VerbId, 0);
        }
        else
        {
            RunInputScript(zone.Number == VirtScreenNumber.Main ? SceneClickArea : VerbClickArea, 0, 0);
        }
    }

    /// <summary>
    /// A key as the player types it. Mirrors the part of <c>processKeyboard</c> that version 3
    /// with the original interface keeps: the pause key pauses, the period ends the line being
    /// shown, Escape skips the cutscene and hands the game its cutscene key, F1 to F9 are 315 on,
    /// Ctrl with a letter is the letter's control code, and any other character goes to the
    /// game. The restart key's question is not asked.
    /// </summary>
    public void KeyDown(Input.Key key, Input.KeyModifiers modifiers, char ascii)
    {
        int pauseKey = Engine.Variables[Vars.PauseKey];
        if (key == Input.Key.Escape)
        {
            Engine.AbortCutscene();
            _input = Engine.Variables[Vars.CutsceneExitKey];
        }
        else if (key is >= Input.Key.F1 and <= Input.Key.F9)
        {
            _input = key - Input.Key.F1 + 315;
        }
        else if (ascii == 0)
        {
            return;
        }
        else if (pauseKey != 0 && ascii == pauseKey)
        {
            Paused = true;
        }
        else if (ascii == '.')
        {
            _talkDelay = 0;
        }
        else if ((modifiers & Input.KeyModifiers.Ctrl) != 0 && char.ToLowerInvariant(ascii) is >= 'a' and <= 'z')
        {
            _input = char.ToLowerInvariant(ascii) & 0x1F;
        }
        else
        {
            _input = ascii;
        }
    }

    /// <summary>Whether the game is paused by its pause key; any key goes on (<c>showOldStyleBannerAndPause</c>, without its banner).</summary>
    public bool Paused { get; set; }

    /// <summary>Mirrors <c>runInputScript</c>.</summary>
    private void RunInputScript(int area, int value, int mode)
    {
        int script = Engine.Variables[Vars.VerbScript];
        if (script != 0)
        {
            Engine.RunScript(script, args: [area, value, mode]);
        }
    }
}
