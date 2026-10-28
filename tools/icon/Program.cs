using System.IO.Compression;
using Distaff.Player;

// make-icon <distaff.ico> [preview.png]: the player's icon from AppIcon at the sizes Windows asks
// for, each a PNG in the .ico, and a sheet of them side by side to look at
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: make-icon <distaff.ico> [preview.png]");
    return 2;
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(s => (Size: s, Png: Png(s, s, AppIcon.Render(s)))).ToList();

// ICONDIR, then an ICONDIRENTRY for each image (0 for 256), then the images
using (var ico = new BinaryWriter(File.Create(args[0])))
{
    ico.Write((ushort)0);
    ico.Write((ushort)1);
    ico.Write((ushort)images.Count);
    int offset = 6 + 16 * images.Count;
    foreach (var (size, png) in images)
    {
        ico.Write((byte)(size >= 256 ? 0 : size));
        ico.Write((byte)(size >= 256 ? 0 : size));
        ico.Write((byte)0);
        ico.Write((byte)0);
        ico.Write((ushort)1);
        ico.Write((ushort)32);
        ico.Write(png.Length);
        ico.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in images)
    {
        ico.Write(png);
    }
}

Console.WriteLine($"wrote {args[0]}: {string.Join(", ", sizes)}");
if (args.Length > 1)
{
    // every size at four times, nearest neighbour, on a mid grey to show the edges
    const int zoom = 4, gap = 8;
    int width = sizes.Sum(s => s * zoom + gap) + gap, height = sizes.Max() * zoom + 2 * gap;
    var sheet = new byte[width * height * 4];
    for (int i = 0; i < sheet.Length; i += 4)
    {
        (sheet[i], sheet[i + 1], sheet[i + 2], sheet[i + 3]) = (128, 128, 128, 255);
    }

    int left = gap;
    foreach (int size in sizes)
    {
        var pixels = AppIcon.Render(size);
        for (int y = 0; y < size * zoom; y++)
        {
            for (int x = 0; x < size * zoom; x++)
            {
                int s = ((y / zoom) * size + x / zoom) * 4, d = ((gap + y) * width + left + x) * 4;
                double a = pixels[s + 3] / 255.0;
                for (int c = 0; c < 3; c++)
                {
                    sheet[d + c] = (byte)Math.Round(pixels[s + c] * a + sheet[d + c] * (1 - a));
                }
            }
        }

        left += size * zoom + gap;
    }

    File.WriteAllBytes(args[1], Png(width, height, sheet));
    Console.WriteLine($"wrote {args[1]}");
}

return 0;

// A PNG of rows of red, green, blue and alpha bytes.
static byte[] Png(int width, int height, byte[] rgba)
{
    using var raw = new MemoryStream();
    using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
    {
        for (int y = 0; y < height; y++)
        {
            z.WriteByte(0);
            z.Write(rgba, y * width * 4, width * 4);
        }
    }

    using var png = new MemoryStream();
    png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    var header = new byte[13];
    BigEndian(header, 0, width);
    BigEndian(header, 4, height);
    (header[8], header[9]) = (8, 6);
    Chunk(png, "IHDR", header);
    Chunk(png, "IDAT", raw.ToArray());
    Chunk(png, "IEND", []);
    return png.ToArray();
}

static void Chunk(Stream png, string type, byte[] data)
{
    var length = new byte[4];
    BigEndian(length, 0, data.Length);
    png.Write(length);
    byte[] body = [.. type.Select(c => (byte)c), .. data];
    png.Write(body);
    var crc = new byte[4];
    BigEndian(crc, 0, (int)Crc32(body));
    png.Write(crc);
}

static void BigEndian(byte[] buffer, int at, int value)
    => (buffer[at], buffer[at + 1], buffer[at + 2], buffer[at + 3]) = ((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

static uint Crc32(byte[] data)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte b in data)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
    }

    return ~crc;
}
