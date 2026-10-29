// Plays a sound resource through ScummVM's Player_V2CMS (engines/scumm/players/player_v2cms.cpp
// and player_v2base.cpp) on its CMS emulator (audio/cms.cpp, audio/softsynth/cms.cpp and
// audio/chip.cpp), all compiled in place, and writes what it renders, as the reference for
// Distaff's PlayerV2Cms.
//
// cms-reference <sound.bin> <number> <output.raw> <frames> [chunk]
//
// The sound is the resource as the player takes it: the AdLib block when it holds a music track,
// the speaker block otherwise, from its size word. The player is made for a version 3 game with
// old-bundle resources, as Loom's EGA release, at 44100 Hz. `frames` stereo frames are read from
// the chip's stream in reads of `chunk` frames (1024 by default). The output is the 16-bit
// little-endian stereo samples.
#include "shim.h"
#include "scumm/scumm.h"
#include "scumm/players/player_v2cms.h"

#include <vector>

static OSystem systemInstance;
OSystem *g_system = &systemInstance;

int main(int argc, char **argv) {
	if (argc < 5) {
		fprintf(stderr, "usage: cms-reference <sound.bin> <number> <output.raw> <frames> [chunk]\n");
		return 2;
	}

	FILE *in = fopen(argv[1], "rb");
	if (!in) {
		fprintf(stderr, "cannot read %s\n", argv[1]);
		return 1;
	}
	std::vector<byte> data;
	for (int c; (c = fgetc(in)) != EOF;)
		data.push_back((byte)c);
	fclose(in);

	int number = atoi(argv[2]);
	long frames = atol(argv[4]);
	int chunk = argc > 5 ? atoi(argv[5]) : 1024;

	Scumm::ScummEngine vm;
	vm._game.version = 3;
	vm._game.features = Scumm::GF_OLD_BUNDLE;
	vm._sounds[number & 0xff] = data.data();
	Scumm::Player_V2CMS player(&vm);
	player.startSound(number);

	Audio::AudioStream *stream = g_system->getMixer()->stream;
	FILE *out = fopen(argv[3], "wb");
	std::vector<int16> buffer(2 * chunk);
	for (long done = 0; done < frames; done += chunk) {
		int n = (int)MIN<long>(chunk, frames - done);
		stream->readBuffer(buffer.data(), 2 * n);
		fwrite(buffer.data(), sizeof(int16), 2 * n, out);
	}
	fclose(out);
	return 0;
}
