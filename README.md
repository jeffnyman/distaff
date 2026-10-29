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
rooms are drawn, and the game's own save screen (F5; the VGA CD's menu) saves and loads in ScummVM's save format, named as ScummVM names each release's saves (`loom-ega.s01`, `loom-vga.s01`, `loom-fm.s01`)
(`--load file` starts from a save), with sound on the AdLib, the PCjr, the CMS (Game Blaster) or the PC speaker (`--sound adlib|pcjr|cms|speaker`, the AdLib by default), and its lines going by at ScummVM's text speed (`--text-speed 0-9`, 6 by default; '+' and '-' on the VGA CD; `--no-subtitles` hides the VGA CD's lines while the CD speaks them, as a fresh ScummVM does), its music and effects at ScummVM's volumes (`--music-volume` and `--sfx-volume`, 0 to 256, 192 by default), and its random numbers ScummVM's, seeded from the clock unless `--random-seed N` gives the seed. It also runs headless, with the seed 1 unless given, as the tests
run it: `distaff <folder> --run 2000 --click 100:160,92 --key 200:esc --screenshot end.png`.
The walkthrough tests play the whole game, the EGA, VGA CD and FM Towns releases all three, from the
start to the end, a section at a time, each section starting from the save the one
before it made, at each of the three proficiency levels, and some ways off it: the opening played
through, Crystalgard's graves and chalice, the waterspout's ride out to sea, and the distaff's answers to drafts gone
wrong. The coverage report (`DISTAFF_COVERAGE`) lists what they leave unplayed, and sweeps every object the walkthrough
finds, looking at it and spinning every draft Bobbin knows at it from a save of the room. With `DISTAFF_EXPORT_SAVES` naming a folder, they also write those saves there,
and each has been loaded in ScummVM 2026.4.0, which drew the same screen, and saved again by it
and loaded here.
[ROADMAP.md](ROADMAP.md) gives the order of work.

| Release | SCUMM version | Data | State |
|---|---|---|---|
| DOS EGA (1990) | 3 (ScummVM's `v3old`) | `00.LFL` to `99.LFL`, XOR 0xFF | Playable to the end, with saves and sound |
| DOS VGA CD (1992) | 4 | `000.LFL`, `DISK01.LEC` (XOR 0x69), `90x.LFL`, one CD audio track | Playable to the end, with its voices and music, and its own menu (F5) to save and load |
| FM Towns (1991) | 3, 256 colours (`GF_OLD256`) | `00.LFL` to `99.LFL`, not encoded, 16 CD audio tracks | Playable to the end (it asks for the proficiency level first, as ScummVM does, unless `--difficulty` gives it), with its CD music, its sound on the Towns' emulated FM and PCM chips, its own save screen (F5), and its smooth scrolling, on as in ScummVM unless `--no-smooth-scroll` turns it off (`--semi-smooth-scroll` lets the opening's fast camera scroll 4 pixels a tick, as ScummVM's option, off by default) |

## Layout

| Path | What it is |
|---|---|
| `src/Distaff.Scumm` | The interpreter: the host contract, detection, resources, rooms and drawing them, the script decoder and engine, the game loop, costumes and walking actors, the font and text, verbs and the cursor, ScummVM-compatible saves, sound (PC speaker, PCjr, CMS on an emulated SAA1099, AdLib on an emulated OPL2, the FM Towns' FM and PCM chips). |
| `src/Distaff.Audio` | Decoders for CD audio tracks kept as files: FLAC, its own, and Ogg Vorbis, on [NVorbis](https://github.com/NVorbis/NVorbis). The interpreter takes tracks through an interface (`ICdTrack`) and carries no decoder of its own. |
| `src/Distaff.Player` | The `distaff` player: an SDL window (`Sdl`, from Maniacal), and the headless tools. |
| `tests/Distaff.Scumm.Tests` | Unit tests, and tests that run against the real game data when it is present, the player's window among them (SDL offscreen, to check that it keeps the game's pace). |
| `.github/workflows` | The build workflow: builds, tests and publishes the player on Windows, Linux and macOS on every push. |
| `tools` | Generators that port tables from the ScummVM source (`port_md5_table.py`, `port_opcodes.py`, `port_sound_tables.py`), and `opl-reference`, `pcjr-reference`, `cms-reference` and `towns-reference`, which build ScummVM's OPL2 emulator, its PC speaker and PCjr player, its CMS player and emulator and its FM Towns sound to check the ports against; `icon`, which writes the player's `distaff.ico` from the drawing in `AppIcon`. |

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

A release is one file that needs nothing installed (the .NET runtime and SDL3 inside):

```
dotnet publish src/Distaff.Player -c Release -r win-x64 -o publish
publish/distaff <game folder>
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
