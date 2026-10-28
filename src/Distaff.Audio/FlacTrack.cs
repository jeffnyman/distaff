using Distaff.Scumm.Sound;

namespace Distaff.Audio;

/// <summary>
/// A CD track in FLAC, as ScummVM plays a <c>track1.flac</c> (<c>makeFLACStream</c>, on libFLAC).
/// Decodes the format's fixed and linear predictors, Rice-coded residuals and stereo
/// decorrelation (<see href="https://www.rfc-editor.org/rfc/rfc9639">RFC 9639</see>); checksums
/// are not checked, but for frame headers when seeking. The file is read whole, and decoded a
/// frame at a time.
/// </summary>
public sealed class FlacTrack : ICdTrack
{
    private readonly byte[] _data;
    private readonly int _bitsPerSample;
    private readonly int _fixedBlockSize;

    // frames found so far: where each starts in the file and its first sample, in order
    private readonly List<(int Offset, long Sample)> _frames = [];

    // the frame decoded last, and where reading is in it
    private int[][] _block = [];
    private int _blockSize;
    private int _blockPosition;
    private int _nextOffset;
    private long _nextSample;

    public FlacTrack(Stream stream)
    {
        using (stream)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            _data = copy.ToArray();
        }

        if (_data.Length < 8 || _data[0] != 'f' || _data[1] != 'L' || _data[2] != 'a' || _data[3] != 'C')
        {
            throw new InvalidDataException("The file is not FLAC.");
        }

        // metadata blocks: a last flag and a type in a byte, a 24-bit length; STREAMINFO comes first
        int at = 4;
        bool last;
        do
        {
            if (at + 4 > _data.Length)
            {
                throw new InvalidDataException("The FLAC metadata runs past the end of the file.");
            }

            last = (_data[at] & 0x80) != 0;
            int type = _data[at] & 0x7F;
            int length = (_data[at + 1] << 16) | (_data[at + 2] << 8) | _data[at + 3];
            if (type == 0)
            {
                var info = new BitReader(_data, at + 4);
                int minBlock = (int)info.Read(16);
                int maxBlock = (int)info.Read(16);
                info.Read(24);
                info.Read(24);
                SampleRate = (int)info.Read(20);
                Channels = (int)info.Read(3) + 1;
                _bitsPerSample = (int)info.Read(5) + 1;
                Frames = (long)info.Read(36);
                _fixedBlockSize = minBlock == maxBlock ? maxBlock : 0;
            }

            at += 4 + length;
        }
        while (!last);

        if (SampleRate == 0)
        {
            throw new InvalidDataException("The FLAC file has no stream information.");
        }

        _nextOffset = at;
        _frames.Add((at, 0));
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public long Frames { get; }

    public void Seek(long frame)
    {
        frame = Math.Clamp(frame, 0, Frames);

        // the last frame known to start at or before it, then on through the headers
        int known = _frames.FindLastIndex(f => f.Sample <= frame);
        var (offset, sample) = _frames[known];
        while (known == _frames.Count - 1 && FindNextFrame(offset) is { } next && next.Sample <= frame)
        {
            _frames.Add(next);
            (offset, sample) = next;
            known++;
        }

        while (known + 1 < _frames.Count && _frames[known + 1].Sample <= frame)
        {
            (offset, sample) = _frames[++known];
        }

        _nextOffset = offset;
        _nextSample = sample;
        _blockSize = _blockPosition = 0;
        if (frame > sample && DecodeFrame())
        {
            _blockPosition = (int)Math.Min(frame - sample, _blockSize);
        }
    }

    public int Read(Span<float> interleaved)
    {
        int frames = interleaved.Length / Channels;
        int done = 0;
        while (done < frames)
        {
            if (_blockPosition == _blockSize && !DecodeFrame())
            {
                break;
            }

            int count = Math.Min(frames - done, _blockSize - _blockPosition);
            // a full scale one short of the power of two, as ov_read's rounding takes it back:
            // the CD player gives the 16-bit samples back as ScummVM's FLAC stream has them
            float scale = 1f / ((1 << (_bitsPerSample - 1)) - 1);
            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < Channels; c++)
                {
                    interleaved[(done + i) * Channels + c] = _block[c][_blockPosition + i] * scale;
                }
            }

            _blockPosition += count;
            done += count;
        }

        return done;
    }

    public void Dispose()
    {
    }

    /// <summary>Decodes the frame at <see cref="_nextOffset"/>; false at the end of the stream.</summary>
    private bool DecodeFrame()
    {
        if (_nextSample >= Frames || ReadHeader(_nextOffset) is not { } header)
        {
            return false;
        }

        var reader = new BitReader(_data, header.End);
        if (_block.Length != Channels || _block[0].Length < header.BlockSize)
        {
            _block = [.. Enumerable.Range(0, Channels).Select(_ => new int[header.BlockSize])];
        }

        int assignment = header.ChannelAssignment;
        for (int c = 0; c < Channels; c++)
        {
            // the side channel has a bit more
            bool side = (assignment == 8 && c == 1) || (assignment == 9 && c == 0) || (assignment == 10 && c == 1);
            DecodeSubframe(ref reader, _block[c], header.BlockSize, header.BitsPerSample + (side ? 1 : 0));
        }

        var a = _block[0];
        var b = Channels > 1 ? _block[1] : a;
        for (int i = 0; i < header.BlockSize; i++)
        {
            switch (assignment)
            {
                case 8:
                    b[i] = a[i] - b[i];
                    break;
                case 9:
                    a[i] += b[i];
                    break;
                case 10:
                    long mid = ((long)a[i] << 1) | (b[i] & 1L);
                    long s = b[i];
                    a[i] = (int)((mid + s) >> 1);
                    b[i] = (int)((mid - s) >> 1);
                    break;
            }
        }

        // the frame ends byte-aligned, with a 16-bit checksum
        reader.AlignToByte();
        int end = reader.BytePosition + 2;
        _blockSize = header.BlockSize;
        _blockPosition = 0;
        long sample = header.Sample;
        _nextSample = sample + header.BlockSize;
        _nextOffset = end;
        if (_frames[^1].Offset < end && _nextSample < Frames)
        {
            _frames.Add((end, _nextSample));
        }

        return true;
    }

    private static void DecodeSubframe(ref BitReader reader, int[] samples, int blockSize, int bits)
    {
        reader.Read(1);
        int type = (int)reader.Read(6);
        int wasted = 0;
        if (reader.Read(1) == 1)
        {
            wasted = 1;
            while (reader.Read(1) == 0)
            {
                wasted++;
            }

            bits -= wasted;
        }

        switch (type)
        {
            case 0:
                Array.Fill(samples, (int)reader.ReadSigned(bits), 0, blockSize);
                break;
            case 1:
                for (int i = 0; i < blockSize; i++)
                {
                    samples[i] = (int)reader.ReadSigned(bits);
                }

                break;
            case >= 8 and <= 12:
                DecodeFixed(ref reader, samples, blockSize, bits, type - 8);
                break;
            case >= 32:
                DecodeLpc(ref reader, samples, blockSize, bits, type - 31);
                break;
            default:
                throw new InvalidDataException($"FLAC subframe type {type} is reserved.");
        }

        if (wasted > 0)
        {
            for (int i = 0; i < blockSize; i++)
            {
                samples[i] <<= wasted;
            }
        }
    }

    /// <summary>The fixed predictors of orders 0 to 4, from their warm-up samples and the residual.</summary>
    private static void DecodeFixed(ref BitReader reader, int[] samples, int blockSize, int bits, int order)
    {
        for (int i = 0; i < order; i++)
        {
            samples[i] = (int)reader.ReadSigned(bits);
        }

        DecodeResidual(ref reader, samples, blockSize, order);
        for (int i = order; i < blockSize; i++)
        {
            long prediction = order switch
            {
                0 => 0,
                1 => samples[i - 1],
                2 => 2L * samples[i - 1] - samples[i - 2],
                3 => 3L * samples[i - 1] - 3L * samples[i - 2] + samples[i - 3],
                _ => 4L * samples[i - 1] - 6L * samples[i - 2] + 4L * samples[i - 3] - samples[i - 4],
            };
            samples[i] = (int)(samples[i] + prediction);
        }
    }

    /// <summary>A linear predictor: warm-up samples, the coefficients' precision and shift, the coefficients, then the residual.</summary>
    private static void DecodeLpc(ref BitReader reader, int[] samples, int blockSize, int bits, int order)
    {
        for (int i = 0; i < order; i++)
        {
            samples[i] = (int)reader.ReadSigned(bits);
        }

        int precision = (int)reader.Read(4) + 1;
        if (precision == 16)
        {
            throw new InvalidDataException("A FLAC predictor's precision is invalid.");
        }

        int shift = (int)reader.ReadSigned(5);
        Span<long> coefficients = stackalloc long[order];
        for (int i = 0; i < order; i++)
        {
            coefficients[i] = reader.ReadSigned(precision);
        }

        DecodeResidual(ref reader, samples, blockSize, order);
        for (int i = order; i < blockSize; i++)
        {
            long sum = 0;
            for (int j = 0; j < order; j++)
            {
                sum += coefficients[j] * samples[i - 1 - j];
            }

            samples[i] += (int)(shift >= 0 ? sum >> shift : sum << -shift);
        }
    }

    /// <summary>The residual after the warm-up samples: Rice-coded partitions, each with its parameter or an escape to plain values.</summary>
    private static void DecodeResidual(ref BitReader reader, int[] samples, int blockSize, int order)
    {
        int method = (int)reader.Read(2);
        if (method > 1)
        {
            throw new InvalidDataException($"FLAC residual coding method {method} is reserved.");
        }

        int parameterBits = method == 0 ? 4 : 5;
        int escape = (1 << parameterBits) - 1;
        int partitionOrder = (int)reader.Read(4);
        int partitions = 1 << partitionOrder;
        int at = order;
        for (int p = 0; p < partitions; p++)
        {
            int count = (blockSize >> partitionOrder) - (p == 0 ? order : 0);
            int parameter = (int)reader.Read(parameterBits);
            if (parameter == escape)
            {
                int raw = (int)reader.Read(5);
                for (int i = 0; i < count; i++)
                {
                    samples[at++] = raw == 0 ? 0 : (int)reader.ReadSigned(raw);
                }

                continue;
            }

            for (int i = 0; i < count; i++)
            {
                uint quotient = reader.ReadUnary();
                uint value = (quotient << parameter) | (uint)reader.Read(parameter);
                samples[at++] = (int)(value >> 1) ^ -(int)(value & 1);
            }
        }
    }

    /// <summary>The header of the frame at an offset, or null when there is none there or its checksum is wrong.</summary>
    private Header? ReadHeader(int offset)
    {
        if (offset + 6 > _data.Length || _data[offset] != 0xFF || (_data[offset + 1] & 0xFE) != 0xF8)
        {
            return null;
        }

        var reader = new BitReader(_data, offset);
        reader.Read(15);
        bool variable = reader.Read(1) == 1;
        int sizeCode = (int)reader.Read(4);
        int rateCode = (int)reader.Read(4);
        int assignment = (int)reader.Read(4);
        int bitsCode = (int)reader.Read(3);
        reader.Read(1);
        if (assignment > 10 || rateCode == 15 || bitsCode is 3 || sizeCode == 0)
        {
            return null;
        }

        // the frame or sample number, coded as UTF-8 is
        long number = (long)reader.Read(8);
        int extra = 0;
        for (long mask = 0x80; (number & mask) != 0 && mask > 1; mask >>= 1)
        {
            extra++;
        }

        if (extra == 1 || extra > 7)
        {
            return null;
        }

        if (extra > 1)
        {
            number &= (1 << (7 - extra)) - 1;
            for (int i = 1; i < extra; i++)
            {
                number = (number << 6) | (long)(reader.Read(8) & 0x3F);
            }
        }

        int blockSize = sizeCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (sizeCode - 2),
            6 => (int)reader.Read(8) + 1,
            7 => (int)reader.Read(16) + 1,
            _ => 256 << (sizeCode - 8),
        };
        switch (rateCode)
        {
            case 12:
                reader.Read(8);
                break;
            case 13 or 14:
                reader.Read(16);
                break;
        }

        int crcAt = reader.BytePosition;
        if (crcAt >= _data.Length || Crc8(_data.AsSpan(offset, crcAt - offset)) != _data[crcAt])
        {
            return null;
        }

        int bits = bitsCode switch { 0 => _bitsPerSample, 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, _ => 32 };
        long sample = variable ? number : number * (_fixedBlockSize != 0 ? _fixedBlockSize : blockSize);
        return new Header(blockSize, assignment, bits, sample, crcAt + 1);
    }

    /// <summary>
    /// The next frame after the one at <paramref name="offset"/>, found by its header alone: the
    /// next place with the frame sync code, a header whose checksum is right, and the sample
    /// that frame should start at.
    /// </summary>
    private (int Offset, long Sample)? FindNextFrame(int offset)
    {
        if (ReadHeader(offset) is not { } current)
        {
            return null;
        }

        long expected = current.Sample + current.BlockSize;
        if (expected >= Frames)
        {
            return null;
        }

        for (int at = current.End; at + 1 < _data.Length; at++)
        {
            if (_data[at] == 0xFF && (_data[at + 1] & 0xFE) == 0xF8 && ReadHeader(at) is { } next && next.Sample == expected)
            {
                return (at, expected);
            }
        }

        return null;
    }

    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        int crc = 0;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
        }

        return (byte)crc;
    }

    private readonly record struct Header(int BlockSize, int ChannelAssignment, int BitsPerSample, long Sample, int End);

    /// <summary>Reads bits from a byte array, most significant first.</summary>
    private ref struct BitReader(byte[] data, int offset)
    {
        private readonly byte[] _data = data;
        private long _bit = offset * 8L;

        public readonly int BytePosition => (int)(_bit >> 3);

        public ulong Read(int count)
        {
            ulong value = 0;
            while (count > 0)
            {
                int index = (int)(_bit >> 3);
                if (index >= _data.Length)
                {
                    throw new InvalidDataException("A FLAC frame runs past the end of the file.");
                }

                int used = (int)(_bit & 7);
                int take = Math.Min(8 - used, count);
                int bits = (_data[index] >> (8 - used - take)) & ((1 << take) - 1);
                value = (value << take) | (uint)bits;
                _bit += take;
                count -= take;
            }

            return value;
        }

        public long ReadSigned(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            long value = (long)Read(count);
            return (value << (64 - count)) >> (64 - count);
        }

        /// <summary>Counts 0 bits up to the next 1, which is passed.</summary>
        public uint ReadUnary()
        {
            uint count = 0;
            while (true)
            {
                int index = (int)(_bit >> 3);
                if (index >= _data.Length)
                {
                    throw new InvalidDataException("A FLAC frame runs past the end of the file.");
                }

                int used = (int)(_bit & 7);
                int rest = _data[index] & (0xFF >> used);
                if (rest == 0)
                {
                    count += (uint)(8 - used);
                    _bit += 8 - used;
                    continue;
                }

                int lead = System.Numerics.BitOperations.LeadingZeroCount((uint)rest) - 24 - used;
                count += (uint)lead;
                _bit += lead + 1;
                return count;
            }
        }

        public void AlignToByte() => _bit = (_bit + 7) & ~7L;
    }
}
