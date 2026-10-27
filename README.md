# Distaff

Distaff is a C# implementation of the SCUMM interpreter for Loom. It is a port of the parts of
ScummVM's `engines/scumm` that Loom uses (SCUMM versions 3 and 4), restructured as a .NET library
on a small host contract of its own, so the same engine can run headless (as the tests run it) and
on the desktop.

Distaff is not a ScummVM fork and does not aim to run any other SCUMM game.

## Status

Early. Releases are identified by ScummVM's MD5 table, and `distaff <folder>` reports which one a
folder holds. For the EGA release the index, rooms (objects, walkboxes, entry, exit and local
scripts), global scripts, sounds and the charset are read; `distaff <folder> --room N` lists a
room. Nothing is drawn or played yet. [ROADMAP.md](ROADMAP.md) gives the order of work.

| Release | SCUMM version | Data | State |
|---|---|---|---|
| DOS EGA (1990) | 3 (ScummVM's `v3old`) | `00.LFL` to `99.LFL`, XOR 0xFF | Resources read |
| DOS VGA CD (1992) | 4 | `000.LFL`, `DISK01.LEC` (XOR 0x69), `90x.LFL`, one CD audio track | Identified |
| FM Towns (1991) | 3, 256 colours (`GF_OLD256`) | `00.LFL` to `99.LFL`, not encoded, 16 CD audio tracks | Identified |

## Layout

| Path | What it is |
|---|---|
| `src/Distaff.Scumm` | The interpreter. For now: the host contract, detection, resources and rooms. |
| `src/Distaff.Player` | The `distaff` command line player. |
| `tests/Distaff.Scumm.Tests` | Unit tests, and tests that run against the real game data when it is present. |
| `tools` | Generators that port tables from the ScummVM source (`port_md5_table.py`). |

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

GPL-3.0-or-later, as ScummVM is. See [LICENSE](LICENSE).
