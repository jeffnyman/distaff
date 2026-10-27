# Distaff

Distaff is a C# implementation of the SCUMM interpreter for Loom. It is a port of the parts of
ScummVM's `engines/scumm` that Loom uses (SCUMM versions 3 and 4), restructured as a .NET library
on a small host contract of its own, so the same engine can run headless (as the tests run it) and
on the desktop.

Distaff is not a ScummVM fork and does not aim to run any other SCUMM game.

## Status

Early. Releases are identified by ScummVM's MD5 table, and `distaff <folder>` reports which one a
folder holds. For the EGA release the index, rooms (objects, walkboxes, entry, exit and local
scripts), global scripts, sounds and the charset are read, and rooms are drawn with their
objects and masks: `distaff <folder> --room N` writes a room as a PNG and lists it. Every script
decodes; `--script N` and `--room-scripts N` disassemble them. `distaff <folder>` plays the game
in a window with the mouse and keys: the distaff and its drafts, text, the cursor and dark
rooms are drawn, and the game's own save screen (F5) saves and loads in ScummVM's save format
(`--load file` starts from a save), with sound on the AdLib, the PCjr or the PC speaker. It also runs headless, as the tests
run it: `distaff <folder> --run 2000 --click 100:160,92 --key 200:esc --screenshot end.png`.
The walkthrough tests play the whole game, from the proficiency screen to the end, a section at a
time, each section starting from the save the one before it made.
[ROADMAP.md](ROADMAP.md) gives the order of work.

| Release | SCUMM version | Data | State |
|---|---|---|---|
| DOS EGA (1990) | 3 (ScummVM's `v3old`) | `00.LFL` to `99.LFL`, XOR 0xFF | Playable to the end, with saves and sound |
| DOS VGA CD (1992) | 4 | `000.LFL`, `DISK01.LEC` (XOR 0x69), `90x.LFL`, one CD audio track | Boots and plays its opening and the island, with its voices and music; saves with `--save` and loads with `--load` |
| FM Towns (1991) | 3, 256 colours (`GF_OLD256`) | `00.LFL` to `99.LFL`, not encoded, 16 CD audio tracks | Identified |

## Layout

| Path | What it is |
|---|---|
| `src/Distaff.Scumm` | The interpreter: the host contract, detection, resources, rooms and drawing them, the script decoder and engine, the game loop, costumes and walking actors, the font and text, verbs and the cursor, ScummVM-compatible saves, sound (PC speaker, PCjr, AdLib on an emulated OPL2). |
| `src/Distaff.Audio` | Decoders for CD audio tracks kept as files: Ogg Vorbis, on [NVorbis](https://github.com/NVorbis/NVorbis). The interpreter takes tracks through an interface (`ICdTrack`) and carries no decoder of its own. |
| `src/Distaff.Player` | The `distaff` player: an SDL window (`Sdl`, from Maniacal), and the headless tools. |
| `tests/Distaff.Scumm.Tests` | Unit tests, and tests that run against the real game data when it is present. |
| `tools` | Generators that port tables from the ScummVM source (`port_md5_table.py`, `port_opcodes.py`, `port_sound_tables.py`), and `opl-reference`, which builds ScummVM's OPL2 emulator to check the port against. |

The host contract is part of `Distaff.Scumm`: the engine base class, the graphics, input and audio
drivers it is given and a headless host (`Hosting`, `Input`), screen surfaces and PNG writing
(`Graphics`), and game folders and save stores (`Storage`). It comes from
[Daventry](https://github.com/jeffnyman/daventry), the AGI and SCI interpreter, by way of
[Maniacal](https://github.com/jeffnyman/maniacal), the Maniac Mansion interpreter. Distaff shares
no code with either at build time.

## Building

Distaff needs the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/Distaff.Player -- <game folder>
```

## Game data

Distaff ships no game data. Point it at a folder holding a release. The tests look for the folders
`loom-ega`, `loom-vga` and `loom-fmtowns` in the folder named by the `DISTAFF_GAMES` environment
variable, or, walking up from the test binary, in any folder above it or in a `games` folder beside
it. The repository's own `games` folder is the usual place, and `.gitignore` keeps it and all game
files out of the repository. A test whose folder is absent passes without checking.

| Folder | Release | Index MD5 |
|---|---|---|
| `loom-ega` | DOS EGA, English | `37f56ceb13e401a7ac7d9e6b37fecaf7` |
| `loom-vga` | DOS VGA CD, English, with `Track1.ogg` | `5d88b9d6a88e6f8e90cded9d01b7f082` |
| `loom-fmtowns` | FM Towns, English, with `Track1.flac` to `Track16.flac` | `c5d10e190d4b4d59114b824f2fdbd00e` |

## License

GPL-3.0-or-later, as ScummVM is. See [LICENSE](LICENSE). NVorbis, which `Distaff.Audio` uses, is
under the MIT license.
