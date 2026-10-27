namespace Distaff.Scumm.Graphics;

/// <summary>
/// The light a dark room shows around the ego: a rectangle of the lit room with rounded corners.
/// Mirrors the copy and the version 1 to 3 corner masks of ScummVM's <c>drawFlashlight</c>.
/// </summary>
public static class Flashlight
{
    // each byte masks two pixels, the high nibble the left one (v2FwdCurveData, v2BkwdCurveData)
    private static readonly byte[] FwdCurve =
    [
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x0F, 0xFF,
        0x00, 0x00, 0xFF, 0xFF,
        0x00, 0x0F, 0xFF, 0xFF,
        0x00, 0xFF, 0xFF, 0xFF,
        0x0F, 0xFF, 0xFF, 0xFF,
        0x0F, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] BkwdCurve =
    [
        0x00, 0x00, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0x00, 0x00,
        0xFF, 0xFF, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0xFF,
    ];

    /// <summary>
    /// Copies <paramref name="rect"/> of the lit room into the dark one and blacks out its
    /// corners: in each of the eight rows nearest the top and bottom, up to eight pixels at each
    /// end, as the curves say. The rectangle must lie inside both pictures and be at least 8 by 8.
    /// </summary>
    public static void Copy(Surface8 lit, Surface8 dark, Rect rect)
    {
        dark.Blit(lit, rect, rect.X, rect.Y);

        int width = rect.Width - 8;
        int height = rect.Height - 1;
        for (int row = 0; row < 8; row++, height -= 2)
        {
            int top = rect.Y + row;
            int bottom = top + height;
            for (int k = 0; k < 4; k++)
            {
                byte fwd = FwdCurve[row * 4 + k];
                byte bkwd = BkwdCurve[row * 4 + k];
                foreach (int y in (int[])[top, bottom])
                {
                    Mask(dark, rect.X + 2 * k, y, (fwd & 0xF0) != 0);
                    Mask(dark, rect.X + 2 * k + 1, y, (fwd & 0x0F) != 0);
                    Mask(dark, rect.X + width + 2 * k, y, (bkwd & 0x0F) != 0);
                    Mask(dark, rect.X + width + 2 * k + 1, y, (bkwd & 0xF0) != 0);
                }
            }
        }
    }

    private static void Mask(Surface8 surface, int x, int y, bool keep)
    {
        if (!keep && x >= 0 && x < surface.Width && y >= 0 && y < surface.Height)
        {
            surface[x, y] = 0;
        }
    }
}
