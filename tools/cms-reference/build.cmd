@echo off
rem Builds cms-reference.exe from ScummVM's Player_V2CMS and CMS emulator with the shims in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
set PLAYERS=%SCUMMVM%\engines\scumm\players
set FLAGS=/nologo /O2 /EHsc /W3 /wd4244 /wd4267 /wd4996 /wd4103 /wd4250 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0shim" /I"%SCUMMVM%\engines" /I"%SCUMMVM%"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
rem the emulator's cms.cpp has the same name as the CMS base's, so it is compiled to an object of its own first
cl /c %FLAGS% /Fo"%~dp0out\softsynth_cms.obj" "%SCUMMVM%\audio\softsynth\cms.cpp" || exit /b 1
cl %FLAGS% /Fo"%~dp0out\\" /Fe"%~dp0out\cms-reference.exe" "%~dp0main.cpp" "%PLAYERS%\player_v2base.cpp" "%PLAYERS%\player_v2cms.cpp" "%SCUMMVM%\audio\chip.cpp" "%SCUMMVM%\audio\cms.cpp" "%~dp0out\softsynth_cms.obj"
