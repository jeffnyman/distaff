# opl-reference

Builds ScummVM's MAME OPL2 emulator (`audio/softsynth/opl/mame.cpp`, compiled in place from a
ScummVM checkout) against the stand-in headers in `shim/`, to produce the reference samples
`OplTests` compares `MameOpl` with.

Build (MSVC Build Tools 2022): `build.cmd [path to scummvm]` (default `F:\Apostle\_loom\scummvm`),
which writes `out\opl-reference.exe`.

Run: `out\opl-reference.exe <script> <output.raw> [noise seed, hex] [sample rate]` (seed 1 and
44100 Hz by default). Script lines: `w <reg hex> <val hex>`, `g <count>`, `r` (reset), `#` comment.
The output is the 16-bit little-endian mono samples; `OplTests` holds the script and the SHA-256
of its output.
