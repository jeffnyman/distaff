using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Resources;

/// <summary>
/// The room files of the EGA release, <c>00.LFL</c> to <c>98.LFL</c>. Every byte is stored XOR-ed
/// with <see cref="EncodingKey"/>. Mirrors ScummVM's <c>ScummFile</c> opened with the encoding byte
/// that <c>getEncByte</c> gives version 3 games with <c>GF_USE_KEY</c>.
/// </summary>
internal static class LflFile
{
    public const byte EncodingKey = 0xFF;

    /// <summary>The file holding a room: its number in two digits (<c>generateFilename</c>, <c>kGenRoomNum</c>).</summary>
    public static string NameOf(int room) => $"{room:D2}.LFL";

    public static bool Exists(IGameContainer game, int room) => game.Exists(NameOf(room));

    /// <summary>Reads a room file and undoes its encoding.</summary>
    public static byte[] Read(IGameContainer game, int room)
    {
        var data = game.ReadAllBytes(NameOf(room));
        Decode(data);
        return data;
    }

    public static void Decode(Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= EncodingKey;
        }
    }
}
