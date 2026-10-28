# pcjr-reference

Builds ScummVM's PC speaker and PCjr player, `Player_V2`
(`engines/scumm/players/player_v2base.cpp` and `player_v2.cpp`, compiled in place from a ScummVM
checkout), against the stand-in headers in `shim/`, to produce the reference samples
`SoundTests` compares `PlayerV2` with. The shims stand in for the mixer (the harness reads the
player's stream itself, at full volume) and the engine (version 3 with old-bundle resources, as
Loom's EGA release, and the sound resource it is given).

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\pcjr-reference.exe`.

Run: `out\pcjr-reference.exe <sound.bin> <number> <output.raw> <frames> [chunk] [speaker]`. The
sound is the resource as the player takes it, its 4-byte header first (`ResourceLoader.LoadSound`'s
speaker data). The player renders the PCjr at 44100 Hz, or the speaker given `speaker`, and the
harness reads `frames` stereo frames in reads of `chunk` frames (1024 by default). The output is
the 16-bit little-endian stereo samples; `SoundTests` holds the SHA-256 of 25 seconds of the
title's music, sound 25, on each.
