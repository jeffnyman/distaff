# towns-reference

Builds ScummVM's FM Towns audio (`audio/softsynth/fmtowns_pc98`: `towns_audio.cpp`,
`towns_pc98_fmsynth.cpp`, `pcm_common.cpp`, `towns_euphony.cpp`) and its `Player_Towns_v1`
(`engines/scumm/players/player_towns.cpp`), compiled in place from a ScummVM checkout against the
stand-in headers in `shim/`, to produce the reference samples `TownsSoundTests` compares
`PlayerTowns` with. The shims stand in for the mixer (the harness reads the Towns' stream itself,
at full volume), the engine (version 3, the sounds' resources, `Sound::stopSound` reaching the
player; the CD is not played) and the common classes the sources use.

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\towns-reference.exe`.

Sounds: `python extract_sounds.py <loom-fmtowns folder> sounds` writes each sound resource, its
6-byte header first, to `sounds\so<number>.bin`, as the scripts load them.

Run: `out\towns-reference.exe <script> <output.raw> [sample rate]` (44100 Hz by default), from
the folder holding `sounds`. The player is made and set up as ScummVM sets it up
(`Player_Towns_v1::init`, then `syncSoundSettings` at the full music and effects volumes). Script
lines: `load <sound> <file>`, `start <sound>`, `stop <sound>`, `stopall`, `vol <sound> <left>
<right>` (`setSoundVolume`), `note <sound> <note>` (`setSoundNote`), `cdvol <left> <right>`,
`g <frames> [chunk]` (reads that many stereo frames, in reads of `chunk` frames), `status <sound>`
(prints `getSoundStatus`), `#` comment. The output is the 16-bit little-endian stereo samples.
`out\towns-reference.exe --tables <output.bin>` writes the FM synth's tables instead.

The scripts in `scripts/` are the ones `TownsSoundTests` plays; it holds the SHA-256 of each
output and the statuses printed.

ScummVM's `TownsPC98_FmSynth::readBuffer` keeps the samples a read made beyond its end for the
next read, and reckons their place as `consumed - _numPending`; after a read of only a few frames
that place can fall before its buffer, and it reads what lies there. Reads of a few frames
therefore give samples that cannot be reproduced; the scripts read 64 frames or more at a time.
