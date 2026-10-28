namespace Distaff.Player;

/// <summary>
/// Distaff's own icon, drawn here rather than kept as a picture, so that the window and the
/// <c>.ico</c> the executable carries (<c>tools/icon</c> writes it) come from the same drawing: a
/// distaff, its staff and the bundle of thread wound at its head, with a strand drawn off it,
/// glowing on a tile of night sky. Nothing in it is taken from the game.
/// </summary>
public static class AppIcon
{
    private const int Samples = 4;

    /// <summary>The bundle's half length along the staff and half width across it.</summary>
    private const double BundleLength = 0.165, BundleWidth = 0.10;

    private static readonly (double X, double Y)[] StaffEnds = [(0.25, 0.90), (0.705, 0.12)];
    private static readonly (double X, double Y) BundleCentre = (0.585, 0.345);
    private static readonly (double X, double Y, double Size)[] Stars = [(0.20, 0.19, 0.050), (0.83, 0.17, 0.034), (0.16, 0.50, 0.026), (0.86, 0.80, 0.030)];

    /// <summary>The strand drawn off the bundle, as points along it.</summary>
    private static readonly (double X, double Y)[] Strand = Bezier((0.60, 0.36), (0.99, 0.48), (0.62, 0.87), 48);

    /// <summary>The icon at a size, as rows of red, green, blue and alpha bytes, alpha not premultiplied.</summary>
    public static byte[] Render(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var pixels = new byte[size * size * 4];
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < Samples; sy++)
                {
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        double u = (px + (sx + 0.5) / Samples) / size, v = (py + (sy + 0.5) / Samples) / size;
                        var (cr, cg, cb, ca) = Shade(u, v);
                        r += cr * ca;
                        g += cg * ca;
                        b += cb * ca;
                        a += ca;
                    }
                }

                int i = (py * size + px) * 4;
                double n = Samples * Samples;
                pixels[i + 3] = ToByte(a / n);
                if (a > 0)
                {
                    pixels[i] = ToByte(r / a);
                    pixels[i + 1] = ToByte(g / a);
                    pixels[i + 2] = ToByte(b / a);
                }
            }
        }

        return pixels;
    }

    /// <summary>The colour and cover at a point of the icon, from 0 to 1 across and down.</summary>
    private static (double R, double G, double B, double A) Shade(double u, double v)
    {
        // the tile: a rounded square of night sky, lighter towards the bundle
        double tile = RoundedSquare(u, v, 0.03, 0.2);
        if (tile <= 0)
        {
            return (0, 0, 0, 0);
        }

        double sky = Math.Exp(-Distance(u, v, BundleCentre) * 3.2);
        double r = 0.020 + 0.07 * sky, g = 0.030 + 0.10 * sky, b = 0.13 + 0.25 * sky - 0.05 * v;

        foreach (var (sx, sy, s) in Stars)
        {
            double dx = Math.Abs(u - sx), dy = Math.Abs(v - sy);
            double star = Math.Exp(-(dx * dx + dy * dy) / (s * s * 0.04)) + 0.6 * Math.Exp(-dx / (s * 0.05) - dy / (s * 0.55)) + 0.6 * Math.Exp(-dy / (s * 0.05) - dx / (s * 0.55));
            Add(ref r, ref g, ref b, 0.65, 0.85, 1.0, star);
        }

        // the glow of the thread, under everything drawn on the tile
        double strand = PolylineDistance(u, v, Strand);
        double bundle = BundleDistance(u, v);
        double glow = Math.Exp(-Math.Max(bundle, 0) / 0.05) * 0.55 + Math.Exp(-strand / 0.035) * 0.45;
        Add(ref r, ref g, ref b, 0.15, 0.75, 1.0, glow);

        // the staff, pale wood, on top of the glow
        double staff = SegmentDistance(u, v, StaffEnds[0], StaffEnds[1]);
        double staffCover = Cover(0.022 - staff);
        if (staffCover > 0)
        {
            double shade = 0.75 + 0.25 * Math.Clamp((staff / 0.022) * -1 + 1, 0, 1);
            (r, g, b) = Mix((r, g, b), (0.93 * shade, 0.78 * shade, 0.50 * shade), staffCover);
        }

        // the strand's bright core, from inside the bundle out
        double core = Cover(0.012 - strand);
        (r, g, b) = Mix((r, g, b), (0.85, 1.0, 1.0), core);

        // the bundle of thread wound round the staff's head: crossed windings, rounded, lit from the top
        double bundleCover = Cover(-bundle);
        if (bundleCover > 0)
        {
            var (x, y) = ToBundle(u, v);
            double nx = x / BundleLength, ny = y / BundleWidth;
            double round = Math.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
            double windings = Math.Max(Math.Cos((x + 0.55 * y) * 120), Math.Cos((x - 0.55 * y) * 120));
            double light = 0.35 + 0.65 * round + 0.25 * nx;
            double bright = Math.Clamp(light * (0.78 + 0.22 * windings), 0, 1.2);
            (r, g, b) = Mix((r, g, b), (0.30 + 0.55 * bright, 0.55 + 0.45 * bright, 0.85 + 0.15 * bright), bundleCover);
        }
        return (r, g, b, tile);
    }

    /// <summary>The point in the bundle's own frame: along the staff, and across it.</summary>
    private static (double Along, double Across) ToBundle(double u, double v)
    {
        double dx = StaffEnds[1].X - StaffEnds[0].X, dy = StaffEnds[1].Y - StaffEnds[0].Y, len = Math.Sqrt(dx * dx + dy * dy);
        (dx, dy) = (dx / len, dy / len);
        double x = u - BundleCentre.X, y = v - BundleCentre.Y;
        return (x * dx + y * dy, -x * dy + y * dx);
    }

    /// <summary>How far outside the bundle, an ellipse along the staff, a point is (negative inside), roughly in tile units.</summary>
    private static double BundleDistance(double u, double v)
    {
        var (x, y) = ToBundle(u, v);
        double k = Math.Sqrt(x * x / (BundleLength * BundleLength) + y * y / (BundleWidth * BundleWidth));
        return (k - 1) * Math.Min(BundleLength, BundleWidth);
    }

    private static double RoundedSquare(double u, double v, double margin, double radius)
    {
        double half = 0.5 - margin;
        double qx = Math.Abs(u - 0.5) - (half - radius), qy = Math.Abs(v - 0.5) - (half - radius);
        double outside = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - radius;
        return Cover(-outside);
    }

    /// <summary>Cover from a signed distance in tile units: a 1/128 edge, about a pixel at the largest size.</summary>
    private static double Cover(double d) => Math.Clamp(d * 128 + 0.5, 0, 1);

    private static double Distance(double u, double v, (double X, double Y) p) => Math.Sqrt((u - p.X) * (u - p.X) + (v - p.Y) * (v - p.Y));

    private static double SegmentDistance(double u, double v, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t = Math.Clamp(((u - a.X) * dx + (v - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Distance(u, v, (a.X + t * dx, a.Y + t * dy));
    }

    private static double PolylineDistance(double u, double v, (double X, double Y)[] points)
    {
        double d = double.MaxValue;
        for (int i = 1; i < points.Length; i++)
        {
            d = Math.Min(d, SegmentDistance(u, v, points[i - 1], points[i]));
        }

        return d;
    }

    private static (double X, double Y)[] Bezier((double X, double Y) p0, (double X, double Y) p1, (double X, double Y) p2, int steps)
        => [.. Enumerable.Range(0, steps + 1).Select(i =>
        {
            double t = (double)i / steps, s = 1 - t;
            return (s * s * p0.X + 2 * s * t * p1.X + t * t * p2.X, s * s * p0.Y + 2 * s * t * p1.Y + t * t * p2.Y);
        })];

    private static void Add(ref double r, ref double g, ref double b, double cr, double cg, double cb, double amount)
        => (r, g, b) = (r + cr * amount, g + cg * amount, b + cb * amount);

    private static (double, double, double) Mix((double R, double G, double B) under, (double R, double G, double B) over, double amount)
        => (under.R + (over.R - under.R) * amount, under.G + (over.G - under.G) * amount, under.B + (over.B - under.B) * amount);

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
