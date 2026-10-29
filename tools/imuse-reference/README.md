# imuse-reference

Builds ScummVM's own iMuse (`engines/scumm/imuse`: `imuse.cpp`, `imuse_player.cpp`,
`imuse_part.cpp`, `instrument.cpp` and the MT-32 driver in `drivers/midi.cpp`), its RO parser
(`engines/scumm/midiparser_ro.cpp`) and its MIDI parser (`audio/midiparser.cpp`), compiled in
place from a ScummVM checkout against the stand-in headers in `shim/`, into `imuse-reference.exe`,
the reference for Distaff's iMuse; and ScummVM's mixer rate converter (`audio/rate.cpp`) into
`rate-reference.exe`. The stand-ins are for ScummVM's common code, the engine (the sound
resources, the game's id), the mixer and `audio/mididrv.h` (its classes as declared there; the
program maps are copied from `mididrv.cpp` into `mididrv_tables.cpp`). iMuse's SMF and XMIDI
parsers, which Loom never uses, stop the reference if they are reached (`unused_parsers.cpp`).

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\imuse-reference.exe` and `out\rate-reference.exe`.

`out\imuse-reference.exe <sound.bin> <number> <output.txt> <frames>` plays a sound as ScummVM's
`setupMusic` sets iMuse up for an MT-32 (`IMuseDriver_MT32`, the old system, a native MT-32, the
tempo at 100%) at ScummVM's default music volume, 192. The sound is the resource as ScummVM loads
it for the Roland Upgrade: "RO", then its block after the 4-byte header, the buffer 6 bytes longer
(`PlayerImuse.RoResource`). In place of the MT-32 a device writes what reaches it, timed as
ScummVM's `MidiDriver_MT32` times it (`MidiDriver_Emulated` at 32000 Hz: the timer at frame 0 and
every 128 frames after, 250 a second), one line each: `<frame> M <packed message hex>`,
`<frame> S <sysex hex>`, a Roland data set `<frame> W <device> <address and data hex>` (as the
driver writes those straight to Munt's memory), the music timer as it changes `<frame> T <value>`,
and `<frame> E` at the end. It is `mt32-reference`'s script format, so Munt can render it.

`... save <frame> <state.bin>` also writes iMuse's state as ScummVM saves it (`saveLoadIMuse`, at
save version 124) before the first tick at or after a frame. `... load <state.bin> <version>`
plays no sound of its own: it reads a state at a save version as ScummVM's `loadState` does (the
music volume to 0, the sounds stopped, iMuse paused, the state read and its players and parts set
up again, iMuse going on, the music volume set again as `syncSoundSettings` does after a load)
and plays on from there. `shim/common/serializer.h` reads and writes as ScummVM's `Serializer`.

`out\rate-reference.exe <input.raw> <output.raw> <rate>` converts a 32 kHz stereo render to
`rate` as ScummVM's mixer plays `MidiDriver_MT32`'s stream at full volume, reading 512 samples at a
time.

`ImuseTests` holds the SHA-256 of three minutes of `imuse-reference`'s output for every sound read
as the Roland Upgrade reads it (lines ending in a line feed), and of the overture's first
2,640,000 frames at 44100 Hz through `mt32-reference` (its chunk 128) and `rate-reference`, on the
MT-32 1.07 and the CM-32L 1.02; and the SHA-256 of the overture's state saved at three places, and
of a minute played on after loading each.
