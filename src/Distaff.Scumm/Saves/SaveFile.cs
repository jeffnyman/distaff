using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Distaff.Scumm.Saves;

/// <summary>What a save says about itself: its name, format version, the time played and when it was made.</summary>
public sealed record SaveInfo(string Name, int Version, TimeSpan PlayTime, DateTime? Saved);

/// <summary>
/// A ScummVM save file: gzip-compressed, a header naming it and giving its format version, then
/// (optionally) a thumbnail, an information section with the time played and the date, and the
/// engine state. Mirrors <c>saveSaveGameHeader</c>, <c>loadSaveGameHeader</c>, <c>saveInfos</c>,
/// <c>loadInfos</c>, <c>Graphics::skipThumbnail</c> and <c>ScummEngine::saveState</c> and
/// <c>loadState</c>. No thumbnail is written; ScummVM has not needed one since save version 75.
/// </summary>
public static class SaveFile
{
    /// <summary>The version ScummVM 2.7 writes and reads, and every ScummVM since reads: the default here.</summary>
    public const int CompatibleVersion = 108;

    /// <summary>The version the ScummVM this port follows writes (<c>CURRENT_VER</c>).</summary>
    public const int CurrentVersion = 124;

    private const int NameLength = 32;
    private const int InfoVersion = 2, InfoSize = 4 + 4 + 4 + 4 + 4 + 4 + 2;

    /// <summary>A save's file name for a game and slot. Mirrors <c>makeSavegameName</c>.</summary>
    public static string FileName(string target, int slot) => $"{target}.s{slot:D2}";

    /// <summary>Writes a save, compressed as ScummVM writes them.</summary>
    public static byte[] Write(ScummState state, string name, TimeSpan playTime, DateTime saved, int version = CompatibleVersion)
    {
        if (version is < ScummState.OldestVersion or > CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, $"Saves are written in versions {ScummState.OldestVersion} to {CurrentVersion}.");
        }

        using var raw = new MemoryStream();
        Span<byte> word = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(word, Tag("SCVM"));
        raw.Write(word);
        BinaryPrimitives.WriteUInt32LittleEndian(word, 0);
        raw.Write(word);
        BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)version);
        raw.Write(word);
        var nameBytes = new byte[NameLength];
        Encoding.UTF8.GetBytes(name.AsSpan(0, Math.Min(name.Length, NameLength - 1)), nameBytes);
        raw.Write(nameBytes);

        WriteBigEndian(raw, Tag("INFO"));
        WriteBigEndian(raw, InfoVersion);
        WriteBigEndian(raw, InfoSize);
        WriteBigEndian(raw, 0);
        WriteBigEndian(raw, (uint)playTime.TotalSeconds);
        WriteBigEndian(raw, (uint)((saved.Day << 24) | (saved.Month << 16) | saved.Year));
        Span<byte> time = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(time, (ushort)((saved.Hour << 8) | saved.Minute));
        raw.Write(time);

        var serializer = SaveSerializer.ForSaving(version);
        state.Sync(serializer);
        raw.Write(serializer.ToArray());

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw.GetBuffer(), 0, (int)raw.Length);
        }

        return compressed.ToArray();
    }

    /// <summary>Reads a save, compressed or not, into a state for a game with so many global objects.</summary>
    public static (SaveInfo Info, ScummState State) Read(byte[] file, int globalObjects)
    {
        var data = Decompress(file);
        var (info, start) = ReadHeader(data);
        if (info.Version is < ScummState.OldestVersion or > CurrentVersion)
        {
            throw new InvalidDataException($"Save version {info.Version} is not one of {ScummState.OldestVersion} to {CurrentVersion}.");
        }

        var state = new ScummState(globalObjects);
        var serializer = SaveSerializer.ForLoading(data[start..], info.Version);
        state.Sync(serializer);
        return (info, state);
    }

    /// <summary>A save's name, version, time played and date, without its state.</summary>
    public static SaveInfo ReadInfo(byte[] file) => ReadHeader(Decompress(file)).Info;

    /// <summary>The same save under another name, compressed as ScummVM writes it. Mirrors <c>changeSavegameName</c>.</summary>
    public static byte[] Rename(byte[] file, string name)
    {
        var data = Decompress(file);
        ReadHeader(data);
        var nameBytes = data.AsSpan(12, NameLength);
        nameBytes.Clear();
        Encoding.UTF8.GetBytes(name.AsSpan(0, Math.Min(name.Length, NameLength - 1)), nameBytes);

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return compressed.ToArray();
    }

    private static (SaveInfo Info, int StateStart) ReadHeader(byte[] data)
    {
        if (data.Length < 12 + NameLength || BinaryPrimitives.ReadUInt32BigEndian(data) != Tag("SCVM"))
        {
            throw new InvalidDataException("This is not a ScummVM save.");
        }

        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        var nameBytes = data.AsSpan(12, NameLength);
        int nameEnd = nameBytes.IndexOf((byte)0);
        string name = Encoding.UTF8.GetString(nameEnd < 0 ? nameBytes[..^1] : nameBytes[..nameEnd]);
        int position = 12 + NameLength;

        // a thumbnail, from version 52: its size counts its own header
        if (version >= 52 && position + 8 <= data.Length && BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position)) is var tag
            && (tag == Tag("THMB") || tag == Tag("BMHT")))
        {
            position += (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 4));
        }

        var playTime = TimeSpan.Zero;
        DateTime? saved = null;
        if (version >= 56)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position)) != Tag("INFO"))
            {
                throw new InvalidDataException("The save's information section is missing.");
            }

            int infoVersion = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 4));
            int infoSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8));
            playTime = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 16)));
            if (infoVersion >= 2)
            {
                uint date = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 20));
                int clock = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position + 24));
                try
                {
                    saved = new DateTime((int)(date & 0xFFFF), (int)((date >> 16) & 0xFF), (int)(date >> 24), clock >> 8, clock & 0xFF, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    saved = null;
                }
            }

            position += Math.Max(infoSize, InfoSize);
        }

        return (new SaveInfo(name, version, playTime, saved), position);
    }

    private static byte[] Decompress(byte[] file)
    {
        if (file.Length < 2 || file[0] != 0x1F || file[1] != 0x8B)
        {
            return file;
        }

        using var input = new GZipStream(new MemoryStream(file), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static uint Tag(string tag) => BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(tag));

    private static void WriteBigEndian(Stream stream, int value) => WriteBigEndian(stream, (uint)value);

    private static void WriteBigEndian(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
