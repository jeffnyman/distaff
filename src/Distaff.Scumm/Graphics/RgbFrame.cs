namespace Distaff.Scumm.Graphics;

/// <summary>
/// A frame of colours, as the host shows it. The EGA and VGA CD releases draw 8-bit pictures and
/// map them through their palette; the FM Towns release puts two layers with palettes of their
/// own together, as ScummVM does in 16-bit colour.
/// </summary>
public sealed class RgbFrame
{
    /// <summary>A frame of a size, black.</summary>
    public RgbFrame(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        Width = width;
        Height = height;
        Pixels = new Rgb[width * height];
    }

    /// <summary>The frame's width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The frame's height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The colours, row by row.</summary>
    public Rgb[] Pixels { get; }

    /// <summary>The colour of a pixel.</summary>
    public Rgb this[int x, int y]
    {
        get => Pixels[y * Width + x];
        set => Pixels[y * Width + x] = value;
    }

    /// <summary>A row's colours, from the left.</summary>
    public Span<Rgb> Row(int y) => Pixels.AsSpan(y * Width, Width);

    /// <summary>An 8-bit picture in the colours of its palette.</summary>
    internal static RgbFrame FromIndexed(Surface8 surface, ReadOnlySpan<Rgb> palette)
    {
        var frame = new RgbFrame(surface.Width, surface.Height);
        for (int i = 0; i < surface.Pixels.Length; i++)
        {
            byte index = surface.Pixels[i];
            frame.Pixels[i] = index < palette.Length ? palette[index] : Rgb.Black;
        }

        return frame;
    }

    /// <summary>A copy of the frame.</summary>
    public RgbFrame Clone()
    {
        var copy = new RgbFrame(Width, Height);
        Pixels.CopyTo(copy.Pixels, 0);
        return copy;
    }
}
