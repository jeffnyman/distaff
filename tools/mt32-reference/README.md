# mt32-reference

Checks the MT-32 emulator in `Distaff.Audio/Mt32` (a port of Munt, by way of Daventry) against Munt
itself, sample by sample. `build.cmd` builds the copy of Munt in a ScummVM checkout
(`audio/softsynth/mt32`, libmt32emu 2.7.0) into a small harness that renders an event script
through its `Synth` the way ScummVM drives it: the coarse analog filter, 32 kHz,
`MIDIDelayMode_IMMEDIATE`, both output gains at 1. Munt's TVP adds `rand() & 3` to its timer; the
harness is built with `rand` replaced by a function returning 0, and the port's test gives it a
generator returning 0 too.

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\mt32-reference.exe`.

Run: `out\mt32-reference.exe <control.rom> <pcm.rom> <script.txt> <output.raw>`. The output is the
16-bit little-endian stereo samples at 32 kHz.

`genscript.py <seed> <seconds> <out.txt>` writes a seeded script of notes on all nine parts,
program changes, controllers (modulation, volume, pan, expression, hold, the pitch bend range,
reset, all notes off), pitch bends, and Roland data sets to the timbres, patches, patch temp areas,
rhythm key map, reverb, partial reserve, master volume and master tune, and a display message.
Each line is `<frame> M <packed message>`, `<frame> S <sysex hex>` or `<frame> E` (the end); the
harness renders up to each frame and then plays the event with `playMsg` or `playSysex`.

`scripts` holds `script1.txt` and `script2.txt` (seeds 1 and 2, 10 seconds each); `Mt32Tests`
holds the SHA-256 of what the harness renders from each with the CM-32L 1.02 and the MT-32 1.07
ROMs, and renders the same through the port.
