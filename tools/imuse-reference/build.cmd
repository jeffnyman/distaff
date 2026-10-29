@echo off
rem Builds imuse-reference.exe from ScummVM's iMuse, its RO parser and its MIDI parser, and rate-reference.exe from
rem ScummVM's mixer rate converter, with the shims in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
set S=%SCUMMVM%\engines\scumm
set FLAGS=/nologo /O2 /EHsc /std:c++17 /W3 /wd4244 /wd4267 /wd4996 /wd4103 /wd4305 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0shim" /I"%SCUMMVM%\engines" /I"%SCUMMVM%"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
cl %FLAGS% /Fo"%~dp0out\\" /Fe"%~dp0out\imuse-reference.exe" "%~dp0main.cpp" "%~dp0mididrv_tables.cpp" "%~dp0unused_parsers.cpp" "%S%\imuse\imuse.cpp" "%S%\imuse\imuse_player.cpp" "%S%\imuse\imuse_part.cpp" "%S%\imuse\instrument.cpp" "%S%\imuse\drivers\midi.cpp" "%S%\midiparser_ro.cpp" "%SCUMMVM%\audio\midiparser.cpp" || exit /b 1
cl %FLAGS% /Fo"%~dp0out\\" /Fe"%~dp0out\rate-reference.exe" "%~dp0rate_main.cpp" "%SCUMMVM%\audio\rate.cpp"
