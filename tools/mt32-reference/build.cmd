@echo off
rem Builds mt32-reference.exe from ScummVM's copy of Munt (audio\softsynth\mt32) with the harness in this folder.
rem Usage: build.cmd [path to the ScummVM checkout]  (default F:\Apostle\_loom\scummvm)
setlocal
set SCUMMVM=%~1
if "%SCUMMVM%"=="" set SCUMMVM=F:\Apostle\_loom\scummvm
set M=%SCUMMVM%\audio\softsynth\mt32
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist "%~dp0out" mkdir "%~dp0out"
rem rand() is replaced by a function returning 0 so that runs repeat; the port's tests do the same
cl /nologo /O2 /EHsc /std:c++17 /D rand=mt32_test_rand /I"%~dp0inc" /I"%M%" /Fo"%~dp0out\\" /Fe"%~dp0out\mt32-reference.exe" "%~dp0harness.cpp" "%M%\Analog.cpp" "%M%\BReverbModel.cpp" "%M%\Display.cpp" "%M%\File.cpp" "%M%\FileStream.cpp" "%M%\LA32FloatWaveGenerator.cpp" "%M%\LA32Ramp.cpp" "%M%\LA32WaveGenerator.cpp" "%M%\MidiStreamParser.cpp" "%M%\Part.cpp" "%M%\Partial.cpp" "%M%\PartialManager.cpp" "%M%\Poly.cpp" "%M%\ROMInfo.cpp" "%M%\Synth.cpp" "%M%\Tables.cpp" "%M%\TVA.cpp" "%M%\TVF.cpp" "%M%\TVP.cpp" "%M%\sha1\sha1.cpp"
