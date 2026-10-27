using System.Buffers.Binary;

namespace Distaff.Scumm.Saves;

/// <summary>
/// Reads or writes a save's state with one set of calls, each field taking part only in the
/// versions that have it. Mirrors ScummVM's <c>Common::Serializer</c>: a field with a version
/// range outside the save's version is neither read nor written, and keeps its value.
/// </summary>
public sealed class SaveSerializer
{
    private readonly MemoryStream _stream;

    private SaveSerializer(MemoryStream stream, int version, bool loading)
    {
        _stream = stream;
        Version = version;
        IsLoading = loading;
    }

    /// <summary>The save format version being read or written. Mirrors <c>getVersion</c>.</summary>
    public int Version { get; }

    public bool IsLoading { get; }

    public bool IsSaving => !IsLoading;

    /// <summary>Where the next field starts. Mirrors <c>pos</c>.</summary>
    public long Position => _stream.Position;

    /// <summary>The length of the state being read, or written so far. Mirrors <c>size</c>.</summary>
    public long Length => _stream.Length;

    public static SaveSerializer ForLoading(byte[] data, int version) => new(new MemoryStream(data, writable: false), version, loading: true);

    public static SaveSerializer ForSaving(int version) => new(new MemoryStream(), version, loading: false);

    /// <summary>What has been written.</summary>
    public byte[] ToArray() => _stream.ToArray();

    /// <summary>Whether the save's version is within a field's range.</summary>
    public bool Has(int minVersion = 0, int maxVersion = int.MaxValue) => Version >= minVersion && Version <= maxVersion;

    public void Byte(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 1, minVersion, maxVersion, b => b[0], (b, v) => b[0] = (byte)v);

    public void SByte(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 1, minVersion, maxVersion, b => (sbyte)b[0], (b, v) => b[0] = (byte)v);

    public void Bool(ref bool value, int minVersion = 0, int maxVersion = int.MaxValue)
    {
        int v = value ? 1 : 0;
        Byte(ref v, minVersion, maxVersion);
        value = v != 0;
    }

    public void Uint16(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 2, minVersion, maxVersion, b => BinaryPrimitives.ReadUInt16LittleEndian(b), (b, v) => BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v));

    public void Int16(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 2, minVersion, maxVersion, b => BinaryPrimitives.ReadInt16LittleEndian(b), (b, v) => BinaryPrimitives.WriteInt16LittleEndian(b, (short)v));

    public void Uint32(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 4, minVersion, maxVersion, b => (int)BinaryPrimitives.ReadUInt32LittleEndian(b), (b, v) => BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)v));

    public void Int32(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 4, minVersion, maxVersion, b => BinaryPrimitives.ReadInt32LittleEndian(b), (b, v) => BinaryPrimitives.WriteInt32LittleEndian(b, v));

    public void Int32BigEndian(ref int value, int minVersion = 0, int maxVersion = int.MaxValue)
        => Field(ref value, 4, minVersion, maxVersion, b => BinaryPrimitives.ReadInt32BigEndian(b), (b, v) => BinaryPrimitives.WriteInt32BigEndian(b, v));

    /// <summary>A run of raw bytes. Mirrors <c>syncBytes</c>.</summary>
    public void Bytes(byte[] bytes, int count, int minVersion = 0, int maxVersion = int.MaxValue)
    {
        if (!Has(minVersion, maxVersion))
        {
            return;
        }

        if (IsLoading)
        {
            ReadExactly(bytes.AsSpan(0, count));
        }
        else
        {
            _stream.Write(bytes, 0, count);
        }
    }

    /// <summary>An array of fields of one kind. Mirrors <c>syncArray</c>.</summary>
    public void Array(int[] values, int count, Action<SaveSerializer, int> sync, int minVersion = 0, int maxVersion = int.MaxValue)
    {
        if (!Has(minVersion, maxVersion))
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            sync(this, i);
        }
    }

    /// <summary>Bytes that are written as zeros and passed over when read. Mirrors <c>skip</c>.</summary>
    public void Skip(int count, int minVersion = 0, int maxVersion = int.MaxValue)
    {
        if (!Has(minVersion, maxVersion))
        {
            return;
        }

        if (IsLoading)
        {
            _stream.Seek(count, SeekOrigin.Current);
        }
        else
        {
            _stream.Write(new byte[count]);
        }
    }

    /// <summary>Moves the read position, as <c>seek</c> does to pass over a block of unknown content.</summary>
    public void Seek(long position) => _stream.Position = position;

    private void Field(ref int value, int size, int minVersion, int maxVersion, Func<byte[], int> read, Action<byte[], int> write)
    {
        if (!Has(minVersion, maxVersion))
        {
            return;
        }

        var buffer = new byte[size];
        if (IsLoading)
        {
            ReadExactly(buffer);
            value = read(buffer);
        }
        else
        {
            write(buffer, value);
            _stream.Write(buffer);
        }
    }

    private void ReadExactly(Span<byte> buffer)
    {
        if (_stream.Read(buffer) != buffer.Length)
        {
            throw new InvalidDataException($"The save ends early, at byte {_stream.Position}.");
        }
    }
}
