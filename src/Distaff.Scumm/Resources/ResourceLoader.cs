using System.Buffers.Binary;
using Distaff.Scumm.Detection;
using Distaff.Scumm.Rooms;
using Distaff.Scumm.Storage;

namespace Distaff.Scumm.Resources;

/// <summary>
/// Loads resources from the room files the index points to. In the EGA release every resource
/// starts with a 4-byte header whose first word is its size, the header included; a room file is
/// the room itself followed by the costumes, scripts and sounds stored with it. Mirrors the
/// <c>GF_OLD_BUNDLE</c> path of ScummVM's <c>ScummEngine::loadResource</c>. Decoded room files
/// are cached.
/// </summary>
public sealed class ResourceLoader
{
    /// <summary>The size of a resource's header (<c>_resourceHeaderSize</c> of <c>ScummEngine_v3old</c>).</summary>
    public const int HeaderSize = 4;

    private readonly Dictionary<int, byte[]> _files = [];

    public ResourceLoader(IGameContainer game, ResourceIndex index)
    {
        Game = game;
        Index = index;
    }

    public ResourceIndex Index { get; }

    /// <summary>The files of the release.</summary>
    public IGameContainer Game { get; }

    /// <summary>
    /// Reads the index from <c>00.LFL</c> and returns a loader over it. Only the EGA layout is
    /// read so far; the others throw <see cref="NotSupportedException"/>.
    /// </summary>
    public static ResourceLoader Open(IGameContainer game)
    {
        var release = ScummDetector.Identify(game)
            ?? throw new InvalidDataException($"No Loom release found in {game.Name}.");
        if (release.Layout != ResourceLayout.V3Old)
        {
            throw new NotSupportedException($"Loom ({release.Variant}) is not supported yet; only the EGA release's resources are read.");
        }

        return new(game, ResourceIndex.Parse(LflFile.Read(game, 0)));
    }

    /// <summary>The decoded contents of a room file.</summary>
    public ReadOnlyMemory<byte> RoomFile(int room)
    {
        if (!_files.TryGetValue(room, out var data))
        {
            data = LflFile.Read(Game, room);
            _files[room] = data;
        }

        return data;
    }

    /// <summary>
    /// The bytes of a resource, starting with its header, or null when the index has no entry
    /// for it or its room file is missing. Throws <see cref="InvalidDataException"/> when the entry
    /// points outside its file. A sound's first block only; <see cref="LoadSound"/> reads both.
    /// </summary>
    public ReadOnlyMemory<byte>? Load(ResourceType type, int index)
    {
        if (Find(type, index) is not { } found)
        {
            return null;
        }

        var (file, offset) = found;
        int size = BinaryPrimitives.ReadUInt16LittleEndian(file.Span[offset..]);
        if (size < HeaderSize || offset + size > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: size {size} at offset {offset} does not fit in {LflFile.NameOf(Index.Locate(type, index).Room)} ({file.Length} bytes).");
        }

        return file.Slice(offset, size);
    }

    /// <summary>Loads and parses a room, or returns null when the index has no such room.</summary>
    public Room? LoadRoom(int room)
        => Load(ResourceType.Room, room) is { } data ? Room.Parse(room, data) : null;

    /// <summary>A global script's code: the resource after its header (<c>getScriptBaseAddress</c> with <c>_resourceHeaderSize</c>).</summary>
    public ReadOnlyMemory<byte>? LoadScript(int script)
    {
        // not a conditional expression: its null would become an empty ReadOnlyMemory, by way of byte[]
        if (Load(ResourceType.Script, script) is not { } data)
        {
            return null;
        }

        return data[HeaderSize..];
    }

    /// <summary>Loads a sound's speaker and AdLib blocks, or returns null when the index has no such sound.</summary>
    public SoundResource? LoadSound(int sound)
        => Find(ResourceType.Sound, sound) is { } found ? SoundResource.Parse(sound, found.File, found.Offset) : null;

    /// <summary>
    /// A charset: <c>99.LFL</c> for charset 0, counting down. These files are not encoded. The
    /// data follows the size word, as <c>ScummEngine_v3::loadCharset</c> reads it. Null when the
    /// file is missing.
    /// </summary>
    public ReadOnlyMemory<byte>? LoadCharset(int charset)
    {
        string name = LflFile.NameOf(99 - charset);
        if (!Game.Exists(name))
        {
            return null;
        }

        var data = Game.ReadAllBytes(name);
        if (data.Length < 2)
        {
            throw new InvalidDataException($"Charset {charset}: {name} is too short.");
        }

        // 99.LFL's size word claims more than the file holds; ScummVM reads what is there
        int size = BinaryPrimitives.ReadUInt16LittleEndian(data);
        return data.AsMemory(2, Math.Min(size, data.Length - 2));
    }

    private (ReadOnlyMemory<byte> File, int Offset)? Find(ResourceType type, int index)
    {
        var location = Index.Locate(type, index);
        if (!location.IsPresent || !LflFile.Exists(Game, location.Room))
        {
            return null;
        }

        var file = RoomFile(location.Room);
        if (location.Offset + HeaderSize > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: offset {location.Offset} is past the end of {LflFile.NameOf(location.Room)} ({file.Length} bytes).");
        }

        return (file, location.Offset);
    }
}
