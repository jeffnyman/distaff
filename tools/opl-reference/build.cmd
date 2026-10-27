@echo off
rem Builds opl-reference.exe from ScummVM's mame.cpp with the shims in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
cl /nologo /O2 /EHsc /W3 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0shim" /I"%SCUMMVM%\audio\softsynth\opl" /Fo"%~dp0out\\" /Fe"%~dp0out\opl-reference.exe" "%~dp0main.cpp" "%SCUMMVM%\audio\softsynth\opl\mame.cpp"
