namespace Distaff.Scumm.Input;

/// <summary>Queue of input events the host has collected since the engine last drained it.</summary>
public interface IInputDriver
{
    /// <summary>
    /// Takes the oldest event waiting, or returns false when there is none. The engine follows
    /// the mouse by these events alone: a host reports every move as a <see cref="InputEventType.MouseMove"/>.
    /// </summary>
    bool TryRead(out InputEvent inputEvent);

    /// <summary>Current mouse position in framebuffer pixels and held buttons.</summary>
    MouseState GetMouseState();
}

/// <summary>Where the mouse is, in the screen's pixels, and which of its buttons are held.</summary>
/// <param name="X">Where the mouse is, in the screen's pixels.</param>
/// <param name="Y">Where the mouse is, in the screen's pixels.</param>
/// <param name="LeftDown">Whether the left button is held.</param>
/// <param name="RightDown">Whether the right button is held.</param>
/// <param name="MiddleDown">Whether the middle button is held.</param>
public readonly record struct MouseState(int X, int Y, bool LeftDown, bool RightDown, bool MiddleDown);
