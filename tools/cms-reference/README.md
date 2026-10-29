# cms-reference

Builds ScummVM's CMS (Game Blaster) player, `Player_V2CMS`
(`engines/scumm/players/player_v2base.cpp` and `player_v2cms.cpp`), and its CMS emulator, DOSBox's
SAA1099 (`audio/cms.cpp`, `audio/softsynth/cms.cpp` and the emulated chip's timer in
`audio/chip.cpp`), compiled in place from a ScummVM checkout, against the stand-in headers in
`shim/`, to produce the reference samples `SoundTests` compares `PlayerV2Cms` with. The shims stand
in for the system and its mixer (the harness reads the chip's stream itself, at full volume, at
44100 Hz) and the engine (version 3 with old-bundle resources, as Loom's EGA release, and the sound
resource it is given).

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\cms-reference.exe`.

Run: `out\cms-reference.exe <sound.bin> <number> <output.raw> <frames> [chunk]`. The sound is the
resource as the player takes it: the AdLib block, from its size word, when its seventh byte is 0x80
(a music track), the speaker block otherwise (`ResourceLoader.LoadSound`'s blocks). The harness
reads `frames` stereo frames in reads of `chunk` frames (1024 by default). The output is the 16-bit
little-endian stereo samples; `SoundTests` holds the SHA-256 of 25 seconds of the title's music,
sound 25, and of 10 seconds of sound 5, an effect.
