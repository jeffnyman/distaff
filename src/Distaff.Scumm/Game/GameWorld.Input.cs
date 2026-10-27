using Distaff.Scumm.Scripts;

namespace Distaff.Scumm.Game;

/// <summary>
/// The player's mouse and keys, as the scripts see them. Mirrors ScummVM's mouse variables
/// (<c>processInput</c>, <c>scummLoop_updateScummVars</c>) and <c>checkExecVerbs</c>, which hands
/// a click or key to the game's verb script (<c>runInputScript</c>). Clicks on verbs come later:
/// a verb's place on screen needs its text's width.
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
    /// <c>checkExecVerbs</c> for version 3: a key is a verb's when one has it, a click in the view is
    /// the scene's and one below it the verbs' area.
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

        int y = MouseY - ViewTop;
        if (y >= 0 && y < ViewHeight)
        {
            RunInputScript(SceneClickArea, 0, 0);
        }
        else if (MouseY >= ViewTop + ViewHeight)
        {
            RunInputScript(VerbClickArea, 0, 0);
        }
    }

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
