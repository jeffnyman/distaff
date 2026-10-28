using Distaff.Scumm.Graphics;
using SDL;
using static SDL.SDL3;

namespace Distaff.Player.Sdl;

/// <summary>
/// Presents the engine's frames through an SDL surface of 32-bit colours scaled onto the window
/// surface. No GPU renderer is involved, which keeps the path simple and exact. The window can be
/// resized and switched to full screen; the frame keeps its aspect ratio and is centred with black
/// borders.
/// </summary>
internal sealed unsafe class SdlGraphicsDriver : IGraphicsDriver, IDisposable
{
    private readonly int _scale;
    private SDL_Window* _window;
    private SDL_Surface* _frame;
    private int _width;
    private int _height;
    private SDL_Rect _target; // where the frame lands on the window surface

    private readonly string _title;

    public SdlGraphicsDriver(string title, int scale, bool fullscreen, bool aspectRatioCorrection)
    {
        _title = title;
        _scale = Math.Max(1, scale);
        AspectRatioCorrection = aspectRatioCorrection;
        var flags = SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (fullscreen)
        {
            flags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        _window = SdlException.ThrowIfNull(SDL_CreateWindow(title, 320 * _scale, 200 * _scale, flags), "SDL_CreateWindow");
        SetIcon();
    }

    /// <summary>The window shows Distaff's icon (<see cref="AppIcon"/>), where the video driver has window icons; without one it keeps its own.</summary>
    private void SetIcon()
    {
        const int size = 64;
        // red, green, blue and alpha bytes in that order (SDL's RGBA32)
        var format = BitConverter.IsLittleEndian ? SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888 : SDL_PixelFormat.SDL_PIXELFORMAT_RGBA8888;
        var icon = SDL_CreateSurface(size, size, format);
        if (icon is null)
        {
            return;
        }

        var pixels = AppIcon.Render(size);
        for (int y = 0; y < size; y++)
        {
            new ReadOnlySpan<byte>(pixels, y * size * 4, size * 4).CopyTo(new Span<byte>((byte*)icon->pixels + (y * icon->pitch), size * 4));
        }

        SDL_SetWindowIcon(_window, icon);
        SDL_DestroySurface(icon);
    }

    public int Width => _width;

    public int Height => _height;

    public SDL_Window* Window => _window;

    public bool IsFullscreen => (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;

    /// <summary>
    /// Shows a 320x200 or 640x400 frame at 4:3, the shape it had on a CRT, where each pixel was a
    /// fifth taller than wide. Other frame sizes are shown with square pixels.
    /// </summary>
    public bool AspectRatioCorrection { get; set; }

    public void SetMode(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        bool changed = width != _width || height != _height;
        DestroyFrame();
        _width = width;
        _height = height;
        _frame = SdlException.ThrowIfNull(SDL_CreateSurface(width, height, SDL_PixelFormat.SDL_PIXELFORMAT_XRGB8888), "SDL_CreateSurface");
        SdlException.ThrowIfFalse(SDL_SetWindowMinimumSize(_window, width, height), "SDL_SetWindowMinimumSize");

        // a new mode gets the window at its natural size, shrunk to fit the display and centred
        // again (resizing keeps the top left corner, which left the 640x480 games off centre);
        // a size the user chose is kept otherwise
        if (changed && !IsFullscreen)
        {
            int scale = _scale;
            var bounds = new SDL_Rect();
            if (SDL_GetDisplayUsableBounds(SDL_GetDisplayForWindow(_window), &bounds))
            {
                while (scale > 1 && (width * scale > bounds.w || height * scale > bounds.h))
                {
                    scale--;
                }
            }

            SdlException.ThrowIfFalse(SDL_SetWindowSize(_window, width * scale, height * scale), "SDL_SetWindowSize");
            // centring is a courtesy: some video drivers (Wayland, offscreen) cannot place windows
            SDL_SetWindowPosition(_window, (int)SDL_WINDOWPOS_CENTERED, (int)SDL_WINDOWPOS_CENTERED);
        }
    }

    public void Present(RgbFrame frame)
    {
        if (_frame is null)
        {
            throw new InvalidOperationException("SetMode must be called before Present.");
        }

        if (frame.Width != _width || frame.Height != _height)
        {
            throw new ArgumentException($"Frame is {frame.Width}x{frame.Height} but mode is {_width}x{_height}.", nameof(frame));
        }

        SdlException.ThrowIfFalse(SDL_LockSurface(_frame), "SDL_LockSurface");
        try
        {
            byte* dest = (byte*)_frame->pixels;
            int pitch = _frame->pitch;
            for (int y = 0; y < _height; y++)
            {
                var row = new Span<uint>(dest + y * pitch, _width);
                var source = frame.Row(y);
                for (int x = 0; x < _width; x++)
                {
                    row[x] = (uint)(source[x].R << 16 | source[x].G << 8 | source[x].B);
                }
            }
        }
        finally
        {
            SDL_UnlockSurface(_frame);
        }

        Redraw();
    }

    /// <summary>
    /// Paints the last frame onto the window again, scaled to fit. Needed after the window was
    /// resized or exposed, since the engines only present when something changed.
    /// </summary>
    public void Redraw()
    {
        if (_frame is null)
        {
            return;
        }

        var windowSurface = SdlException.ThrowIfNull(SDL_GetWindowSurface(_window), "SDL_GetWindowSurface");
        _target = FitFrame(windowSurface->w, windowSurface->h);

        // the borders, when the window is not the frame's shape
        if (_target.x > 0 || _target.y > 0)
        {
            SdlException.ThrowIfFalse(SDL_FillSurfaceRect(windowSurface, null, SDL_MapSurfaceRGB(windowSurface, 0, 0, 0)), "SDL_FillSurfaceRect");
        }

        fixed (SDL_Rect* target = &_target)
        {
            SdlException.ThrowIfFalse(
                SDL_BlitSurfaceScaled(_frame, null, windowSurface, target, SDL_ScaleMode.SDL_SCALEMODE_NEAREST),
                "SDL_BlitSurfaceScaled");
        }

        SdlException.ThrowIfFalse(SDL_UpdateWindowSurface(_window), "SDL_UpdateWindowSurface");
    }

    /// <summary>Switches between a window and the full screen, keeping the frame's aspect ratio either way.</summary>
    public void ToggleFullscreen()
    {
        SdlException.ThrowIfFalse(SDL_SetWindowFullscreen(_window, !IsFullscreen), "SDL_SetWindowFullscreen");
    }

    /// <summary>Switches the aspect ratio correction on or off and repaints.</summary>
    public void ToggleAspectRatioCorrection()
    {
        AspectRatioCorrection = !AspectRatioCorrection;
        Redraw();
    }

    public void SetTitle(string title)
    {
        SDL_SetWindowTitle(_window, title);
    }

    /// <summary>Shows a short status, such as the volume, after the title in the window's title bar.</summary>
    public void ShowStatus(string status) => SetTitle($"{_title}  ({status})");

    /// <summary>Converts window coordinates, as the mouse reports them, to framebuffer coordinates.</summary>
    public (int X, int Y) ToFramebuffer(float windowX, float windowY)
    {
        if (_width == 0 || _height == 0 || _target.w == 0 || _target.h == 0)
        {
            return (0, 0);
        }

        // mouse positions are in window coordinates, the surface is in pixels; the two differ on
        // a high-DPI display
        int winW;
        int winH;
        if (SDL_GetWindowSize(_window, &winW, &winH) && winW > 0 && winH > 0)
        {
            var surface = SDL_GetWindowSurface(_window);
            if (surface is not null)
            {
                windowX = windowX * surface->w / winW;
                windowY = windowY * surface->h / winH;
            }
        }

        int x = (int)MathF.Floor((windowX - _target.x) * _width / _target.w);
        int y = (int)MathF.Floor((windowY - _target.y) * _height / _target.h);
        return (Math.Clamp(x, 0, _width - 1), Math.Clamp(y, 0, _height - 1));
    }

    public void Dispose()
    {
        DestroyFrame();
        if (_window is not null)
        {
            SDL_DestroyWindow(_window);
            _window = null;
        }
    }

    /// <summary>The largest rectangle with the frame's aspect ratio that fits the window, centred.</summary>
    private SDL_Rect FitFrame(int windowWidth, int windowHeight)
    {
        // the frame's shape on screen; a 16:10 frame becomes 4:3 when corrected
        int shapeWidth = _width;
        int shapeHeight = AspectRatioCorrection && _height * 8 == _width * 5 ? _height * 6 / 5 : _height;
        float scale = Math.Min((float)windowWidth / shapeWidth, (float)windowHeight / shapeHeight);
        int w = Math.Max(1, (int)(shapeWidth * scale));
        int h = Math.Max(1, (int)(shapeHeight * scale));
        return new SDL_Rect { x = (windowWidth - w) / 2, y = (windowHeight - h) / 2, w = w, h = h };
    }

    private void DestroyFrame()
    {
        if (_frame is not null)
        {
            SDL_DestroySurface(_frame);
            _frame = null;
        }
    }
}
