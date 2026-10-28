"""Writes the FM Towns release's sound resources, as ScummVM's getResourceAddress gives them
(their 6-byte header first), to so<number>.bin files for towns-reference's scripts.

Usage: python extract_sounds.py <loom-fmtowns folder> <output folder>

The index, 00.LFL, is blocks of a 4-byte size and a 2-letter tag; its sound directory, 0N, is
a count and, for each sound, its room (whose file is <room>.LFL) and its offset there, 0xFFFFFFFF
for none; room 0 is the index itself, and holds none. A resource is a block of the same kind.
"""
import os
import struct
import sys


def sound_directory(index):
    at = 0
    while at + 6 <= len(index):
        size = struct.unpack_from('<I', index, at)[0]
        if index[at + 4:at + 6] == b'0N':
            count = struct.unpack_from('<H', index, at + 6)[0]
            return [struct.unpack_from('<BI', index, at + 8 + 5 * i) for i in range(count)]
        at += size
    raise ValueError('no sound directory (0N) in the index')


def main(folder, out):
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(folder, '00.LFL'), 'rb') as f:
        directory = sound_directory(f.read())
    for number, (room, offset) in enumerate(directory):
        path = os.path.join(folder, '%02d.LFL' % room)
        if room == 0 or offset == 0xFFFFFFFF or not os.path.exists(path):
            continue
        with open(path, 'rb') as f:
            f.seek(offset)
            header = f.read(6)
            size = struct.unpack_from('<I', header)[0]
            data = header + f.read(size - 6)
        with open(os.path.join(out, 'so%03d.bin' % number), 'wb') as f:
            f.write(data)
        print('%3d: room %2d, %6d bytes, type %d' % (number, room, size, data[13] if size > 13 else -1))


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
