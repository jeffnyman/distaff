namespace Distaff.Scumm.Graphics;

/// <summary>
/// The host's display. The engine hands it frames of colours; the host decides how to scale and
/// present them.
/// </summary>
public interface IGraphicsDriver
{
    /// <summary>Sets the logical framebuffer size the engine will present.</summary>
    void SetMode(int width, int height);

    /// <summary>Presents a complete frame. The host must not keep a reference to it.</summary>
    void Present(RgbFrame frame);

    void SetTitle(string title);
}
