@echo off
rem Builds pcjr-reference.exe from ScummVM's Player_V2 with the shims in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
set PLAYERS=%SCUMMVM%\engines\scumm\players
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
cl /nologo /O2 /EHsc /W3 /wd4244 /wd4267 /wd4996 /wd4103 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0shim" /I"%SCUMMVM%\engines" /Fo"%~dp0out\\" /Fe"%~dp0out\pcjr-reference.exe" "%~dp0main.cpp" "%PLAYERS%\player_v2base.cpp" "%PLAYERS%\player_v2.cpp"
