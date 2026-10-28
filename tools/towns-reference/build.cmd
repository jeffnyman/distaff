@echo off
rem Builds towns-reference.exe from ScummVM's FM Towns audio and Player_Towns_v1 with the shims in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
set FMT=%SCUMMVM%\audio\softsynth\fmtowns_pc98
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
cl /nologo /O2 /EHsc /W3 /wd4244 /wd4267 /wd4996 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0shim" /I"%SCUMMVM%" /I"%SCUMMVM%\engines" /Fo"%~dp0out\\" /Fe"%~dp0out\towns-reference.exe" "%~dp0main.cpp" "%FMT%\towns_audio.cpp" "%FMT%\towns_pc98_fmsynth.cpp" "%FMT%\pcm_common.cpp" "%FMT%\towns_euphony.cpp" "%SCUMMVM%\engines\scumm\players\player_towns.cpp"
